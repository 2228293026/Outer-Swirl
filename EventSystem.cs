using ADOFAI;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;
using ADOFAIPropInfo = ADOFAI.PropertyInfo;

namespace Outer_Swirl
{
    public abstract class CustomEventBase
    {
        public virtual bool AllowFirstFloor => false;
        public virtual LevelEventExecutionTime ExecutionTime => LevelEventExecutionTime.OnPrebar;
        public virtual bool isDecoration => false;
        public virtual void OnApply() { }
        public virtual void OnFloor() { }
        public virtual Sprite GetIcon() => null;
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class EventNameAttribute(string nameKey) : Attribute
    {
        public string NameKey { get; } = nameKey;
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class EventCategoryAttribute(params string[] categories) : Attribute
    {
        public string[] Categories { get; } = categories;
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class EventPropertyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class PropertyToggleableAttribute(bool toggleable) : Attribute
    {
        public bool Toggleable { get; } = toggleable;
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class PropertyLabelAttribute : Attribute
    {
        public string LocalizationKey { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class PropertyGroupAttribute : Attribute
    {
        public string Name { get; set; }
    }

    public static class OuterSwirlEventSystem
    {
        public const int CustomEventTypeBase = 100000;
        public static LevelEventType EventType { get; private set; }
        public static LevelEventInfo EventInfo { get; private set; }

        private static CustomEventBase _instance;
        private static bool _initialized;
        private static List<LevelEvent> _backup = new();
        private static readonly Dictionary<int, Dictionary<string, object>> _floorCache = new();
        private sealed class PropAccessor
        {
            public string Name;
            public Type Type;
            public MemberInfo Member;
            public Func<CustomEventBase, object> Getter;
            public Action<CustomEventBase, object> Setter;
        }

        private static readonly List<PropAccessor> _propAccessors = new();
        internal static string _eventFullName;
        private static List<LevelEventCategory> _eventCategories;
        private static string _pendingLocJson;

        public static void Initialize(CustomEventBase ev)
        {
            try
            {
                if (_initialized) { Debug.Log("[OuterSwirl] Already initialized, skip"); return; }
                _initialized = true;
                Debug.Log("[OuterSwirl] Initialize start");

                _instance = ev;
                EventType = (LevelEventType)CustomEventTypeBase;

                var type = ev.GetType();
                var nameAttr = Attribute.GetCustomAttribute(type, typeof(EventNameAttribute)) as EventNameAttribute;
                var fullName = nameAttr?.NameKey ?? type.Name;
                Debug.Log($"[OuterSwirl] fullName={fullName}");

                var categories = new List<LevelEventCategory>();
                foreach (var rawName in Attribute.GetCustomAttributes(type, typeof(EventCategoryAttribute))
                    .Cast<EventCategoryAttribute>().SelectMany(a => a.Categories))
                {
                    if (Enum.TryParse(rawName, true, out LevelEventCategory cat))
                        categories.Add(cat);
                }

                _propAccessors.Clear();
                foreach (var prop in type.GetProperties())
                {
                    if (prop.IsDefined(typeof(EventPropertyAttribute), false))
                    {
                        _propAccessors.Add(new PropAccessor
                        {
                            Name = prop.Name,
                            Type = prop.PropertyType,
                            Member = prop,
                            Getter = BuildGetter(prop, type),
                            Setter = BuildSetter(prop, type, prop.PropertyType)
                        });
                    }
                }
                foreach (var field in type.GetFields())
                {
                    if (field.IsDefined(typeof(EventPropertyAttribute), false))
                    {
                        _propAccessors.Add(new PropAccessor
                        {
                            Name = field.Name,
                            Type = field.FieldType,
                            Member = field,
                            Getter = BuildGetter(field, type),
                            Setter = BuildSetter(field, type, field.FieldType)
                        });
                    }
                }
                Debug.Log($"[OuterSwirl] _propAccessors count={_propAccessors.Count}");

                _eventFullName = fullName;
                _eventCategories = categories;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OuterSwirl] Initialize failed: {ex}");
            }
        }

        static void TryRegisterLocalization()
        {
            if (_pendingLocJson == null) return;
            var json = _pendingLocJson;
            _pendingLocJson = null;
            OuterSwirlLocalization.RegisterLocalization(json);
        }

        static void TryRegister()
        {
            if (OuterSwirlEventSystem._eventFullName == null) return;
            var eventKey = OuterSwirlEventSystem.EventType.ToString();
            if (GCS.levelEventsInfo != null && GCS.levelEventsInfo.ContainsKey(eventKey))
            {
                EventInfo = GCS.levelEventsInfo[eventKey];
                return;
            }
            if (GCS.levelEventsInfo == null || GCS.levelEventTypeString == null)
            {
                Debug.Log("[OuterSwirl] TR: GCS not ready yet, will retry later");
                return;
            }
            try
            {
                Debug.Log("[OuterSwirl] TR: start");
                GCS.levelEventTypeString[EventType] = eventKey;
                Debug.Log("[OuterSwirl] TR: levelEventTypeString ok");

                EventInfo = new LevelEventInfo
                {
                    name = eventKey,
                    type = EventType,
                    executionTime = _instance.ExecutionTime,
                    allowFirstFloor = _instance.AllowFirstFloor,
                    useGroups = false,
                    categories = _eventCategories,
                };
                Debug.Log("[OuterSwirl] TR: EventInfo created");

                var propsInfo = new Dictionary<string, ADOFAIPropInfo>();
                foreach (var accessor in _propAccessors)
                {
                    var name = accessor.Name;
                    var member = accessor.Member;
                    var propType = accessor.Type;
                    Debug.Log($"[OuterSwirl] TR: processing prop '{name}'");

                    object defaultValue = null;
                    try { defaultValue = accessor.Getter(_instance); }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[OuterSwirl] Getter for '{name}' failed: {ex}");
                    }

                    var labelAttr = Attribute.GetCustomAttribute(member, typeof(PropertyLabelAttribute)) as PropertyLabelAttribute;

                    var pDict = new Dictionary<string, object>
                    {
                        ["name"] = name,
                        ["type"] = MapPropertyTypeString(propType),
                        ["default"] = defaultValue ?? "",
                        ["key"] = labelAttr?.LocalizationKey ?? ""
                    };
                    Debug.Log("[OuterSwirl] TR: pDict ready");

                    var pInfo = new ADOFAIPropInfo(pDict, EventInfo);
                    Debug.Log("[OuterSwirl] TR: ADOFAIPropInfo created");

                    propsInfo[name] = pInfo;
                }

                EventInfo.propertiesInfo = propsInfo;
                Debug.Log("[OuterSwirl] TR: propertiesInfo assigned");

                GCS.levelEventsInfo[eventKey] = EventInfo;
                Debug.Log($"[OuterSwirl] TR: event registered in GCS");

                if (GCS.levelEventIcons != null)
                {
                    try
                    {
                        var icon = _instance.GetIcon();
                        if (icon != null)
                            GCS.levelEventIcons[EventType] = icon;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[OuterSwirl] Icon retrieval failed: {ex}");
                    }
                }

                Debug.Log($"[OuterSwirl] Registered event '{_eventFullName}' (ID={CustomEventTypeBase})");

                if (GCS.levelEventsInfo.ContainsKey(eventKey))
                    RegisterSoloType();

            }
            catch (Exception ex)
            {
                Debug.LogError($"[OuterSwirl] Register event failed: {ex}");
            }
        }

        private static void RegisterSoloType()
        {
            try
            {
                var getter = PatchManager.CreateStaticFieldGetter<LevelEventType[]>(typeof(EditorConstants), nameof(EditorConstants.soloTypes));
                var setter = PatchManager.CreateStaticFieldSetter<LevelEventType[]>(typeof(EditorConstants), nameof(EditorConstants.soloTypes));

                var current = getter();
                var customType = (LevelEventType)CustomEventTypeBase;
                if (Array.IndexOf(current, customType) >= 0) return;

                var newArray = new LevelEventType[current.Length + 1];
                Array.Copy(current, newArray, current.Length);
                newArray[current.Length] = customType;
                setter(newArray);

                Debug.Log($"[OuterSwirl] Added event type {customType} to soloTypes");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OuterSwirl] Failed to register solo type: {ex}");
            }
        }

        static Func<CustomEventBase, object> BuildGetter(MemberInfo member, Type declaringType)
        {
            var instanceParam = Expression.Parameter(typeof(CustomEventBase), "instance");
            var castInstance = Expression.Convert(instanceParam, declaringType);

            Expression memberAccess = member switch
            {
                System.Reflection.PropertyInfo pi => Expression.Property(castInstance, pi),
                System.Reflection.FieldInfo fi => Expression.Field(castInstance, fi),
                _ => throw new ArgumentException("Unsupported member")
            };

            var boxed = Expression.Convert(memberAccess, typeof(object));
            return Expression.Lambda<Func<CustomEventBase, object>>(boxed, instanceParam).Compile();
        }

        static Action<CustomEventBase, object> BuildSetter(MemberInfo member, Type declaringType, Type memberType)
        {
            var instanceParam = Expression.Parameter(typeof(CustomEventBase), "instance");
            var valueParam = Expression.Parameter(typeof(object), "value");
            var castInstance = Expression.Convert(instanceParam, declaringType);

            // 如果 raw 运行时类型 == memberType，直接 unbox；否则走 ChangeType
            var isInstanceOf = Expression.TypeIs(valueParam, memberType);
            var directCast = Expression.Convert(valueParam, memberType);
            var changeTypeCall = Expression.Call(
                typeof(Convert), nameof(Convert.ChangeType), null,
                valueParam, Expression.Constant(memberType));
            var convertedCast = Expression.Convert(changeTypeCall, memberType);

            var finalValue = Expression.Condition(isInstanceOf, directCast, convertedCast);

            Expression assign = member switch
            {
                System.Reflection.PropertyInfo pi => Expression.Assign(Expression.Property(castInstance, pi), finalValue),
                System.Reflection.FieldInfo fi => Expression.Assign(Expression.Field(castInstance, fi), finalValue),
                _ => throw new ArgumentException("Unsupported member")
            };

            return Expression.Lambda<Action<CustomEventBase, object>>(assign, instanceParam, valueParam).Compile();
        }

        static string MapPropertyTypeString(Type t)
        {
            if (t == typeof(bool)) return "Bool";
            if (t == typeof(int)) return "Int";
            if (t == typeof(float)) return "Float";
            if (t == typeof(string)) return "String";
            return "String";
        }

        static readonly Dictionary<string, SystemLanguage> LangCodeMap = new()
        {
            ["en"] = SystemLanguage.English,
            ["zh"] = SystemLanguage.ChineseSimplified,
            ["zh-CN"] = SystemLanguage.ChineseSimplified,
            ["zh-TW"] = SystemLanguage.ChineseTraditional,
            ["ja"] = SystemLanguage.Japanese,
            ["ko"] = SystemLanguage.Korean,
            ["fr"] = SystemLanguage.French,
            ["de"] = SystemLanguage.German,
            ["es"] = SystemLanguage.Spanish,
            ["pt"] = SystemLanguage.Portuguese,
            ["ru"] = SystemLanguage.Russian,
            ["it"] = SystemLanguage.Italian,
            ["nl"] = SystemLanguage.Dutch,
            ["pl"] = SystemLanguage.Polish,
            ["tr"] = SystemLanguage.Turkish,
            ["th"] = SystemLanguage.Thai,
        };

        static void TryInjectEditorAlias(Dictionary<SystemLanguage, Dictionary<string, string>> sheet)
        {
            if (!sheet.Any()) return;
            // 编辑器用 RDString.Get($"editor.{LevelEventType}")，所以翻译 key 必须是 "editor.100000"
            // 自动从 _eventFullName 复制翻译到 editor.100000，不需要用户手动配
            string editorKey = $"editor.{EventType}";  // → "editor.100000"
            foreach (var langDict in sheet.Values)
            {
                if (!langDict.ContainsKey(editorKey) && langDict.TryGetValue(_eventFullName, out var val))
                    langDict[editorKey] = val;
            }
        }

        // ===== Harmony Patch Classes =====

        [HarmonyPatch(typeof(scnGame), nameof(scnGame.ApplyEventsToFloors), typeof(List<scrFloor>))]
        internal static class ClearCachePatch
        {
            [HarmonyPrefix]
            static void ClearCache()
            {
                _floorCache.Clear();
                Patch.FoolSwirlPatch.Active = false;
            }
        }

        [HarmonyPatch(typeof(scnEditor), "Awake")]
        internal static class EditorAwakePatch
        {
            [HarmonyPostfix]
            static void AfterAwake()
            {
                TryRegister();
                TryRegisterLocalization();
            }
        }

        [HarmonyPatch(typeof(scnGame), nameof(scnGame.ApplyEvent))]
        internal static class ApplyEventPatch
        {
            [HarmonyPrefix]
            static bool Prefix(LevelEvent evnt)
            {
                if ((int)evnt.eventType != CustomEventTypeBase) return true;

                var dataCopy = new Dictionary<string, object>();
                foreach (var accessor in _propAccessors)
                {
                    var name = accessor.Name;
                    if (evnt.ContainsKey(name))
                        dataCopy[name] = evnt[name];
                }
                _floorCache[evnt.floor] = dataCopy;
                return false;
            }
        }

        [HarmonyPatch(typeof(scrPlanet), "MoveToNextFloor")]
        internal static class MoveToNextFloorPatch
        {
            [HarmonyPostfix]
            static void Postfix(scrFloor floor)
            {
                if (_floorCache.TryGetValue(floor.seqID, out var data))
                {
                    ApplyProperties(data);
                    _instance.OnFloor();
                }
            }
        }

        [HarmonyPatch(typeof(LevelData), nameof(LevelData.EncodeToDictionary))]
        internal static class EncodeToDictionaryPatch
        {
            [HarmonyPrefix]
            static void Prefix(LevelData __instance)
            {
                var list = __instance.levelEvents;
                _backup.Clear();
                _backup.AddRange(list);

                for (int i = 0; i < list.Count; i++)
                {
                    var ev = list[i];
                    if ((int)ev.eventType != CustomEventTypeBase) continue;

                    var encoded = ev.Encode(false);
                    var json = JsonConvert.SerializeObject(encoded, Formatting.Indented);

                    var commentEvent = new LevelEvent(ev.floor, LevelEventType.EditorComment);
                    commentEvent["comment"] = "!EVENT\n" + json;
                    list[i] = commentEvent;
                }
            }

            [HarmonyPostfix]
            static void Postfix(LevelData __instance)
            {
                __instance.levelEvents.Clear();
                __instance.levelEvents.AddRange(_backup);
            }
        }

        [HarmonyPatch(typeof(LevelData), nameof(LevelData.Decode))]
        internal static class DecodePatch
        {
            [HarmonyPostfix]
            static void Postfix(LevelData __instance, Dictionary<string, object> dict, out LoadResult status)
            {
                status = default;
                var list = __instance.levelEvents;
                for (int i = 0; i < list.Count; i++)
                {
                    var ev = list[i];
                    if (ev.eventType != LevelEventType.EditorComment) continue;
                    if (!ev.ContainsKey("comment")) continue;

                    var comment = ev.GetString("comment");
                    if (string.IsNullOrEmpty(comment)) continue;

                    const string prefix = "!EVENT\n";
                    if (!comment.StartsWith(prefix)) continue;

                    try
                    {
                        var json = comment.Substring(prefix.Length);
                        var evDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                        list[i] = new LevelEvent(evDict);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[OuterSwirl] Decode error: {ex.Message}");
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Enum), nameof(Enum.GetValues))]
        internal static class EnumGetValuesPatch
        {
            private static bool _executeOriginal = false;

            [HarmonyPrefix]
            private static bool Prefix(Type enumType, ref Array __result)
            {
                if (enumType == typeof(LevelEventType) && !_executeOriginal)
                {
                    _executeOriginal = true;
                    // 获取原版枚举值列表
                    var original = Enum.GetValues(typeof(LevelEventType)) as LevelEventType[];

                    var merged = original.Concat(new[] { (LevelEventType)OuterSwirlEventSystem.CustomEventTypeBase }).ToArray();
                    __result = merged;
                    _executeOriginal = false;
                    return false; // 跳过原方法
                }
                return true; // 其他类型正常处理
            }
        }

        [HarmonyPatch(typeof(RDString), nameof(RDString.GetWithCheck))]
        internal static class RDStringGetWithCheckPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(string key, out bool exists, ref string __result)
            {
                if (OuterSwirlLocalization.TryGetLocalizedString(key, out string value))
                {
                    __result = value;
                    exists = true;
                    return false; // 跳过原方法
                }
                exists = false;
                return true; // 继续原方法
            }
        }
    

    // ===== Helper =====

        static void ApplyProperties(Dictionary<string, object> data)
        {
            if (data == null) return;
            foreach (var accessor in _propAccessors)
            {
                var name = accessor.Name;
                if (!data.TryGetValue(name, out var raw)) continue;
                try
                {
                    accessor.Setter(_instance, raw);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[OuterSwirl] Setter for '{name}' failed: {ex}");
                }
            }
        }
    }
}
