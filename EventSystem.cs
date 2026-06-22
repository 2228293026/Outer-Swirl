using System;
using System.Collections.Generic;
using System.Linq;

using ADOFAI;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using System.Reflection;
using ADOFAIPropInfo = ADOFAI.PropertyInfo;

namespace Outer_Swirl
{
    public abstract class CustomEventBase
    {
        public virtual bool AllowFirstFloor => false;
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
        private static readonly List<(string name, MemberInfo member)> _propMap = new();
        private static string _eventFullName;
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

                _propMap.Clear();
                foreach (var prop in type.GetProperties())
                {
                    if (prop.IsDefined(typeof(EventPropertyAttribute), false))
                        _propMap.Add((prop.Name, prop));
                }
                foreach (var field in type.GetFields())
                {
                    if (field.IsDefined(typeof(EventPropertyAttribute), false))
                        _propMap.Add((field.Name, field));
                }
                Debug.Log($"[OuterSwirl] _propMap count={_propMap.Count}");

                _eventFullName = fullName;
                _eventCategories = categories;
                TryRegister();
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
            RegisterLocalization(json);
        }

        static void TryRegister()
        {
            if (_eventFullName == null) return;
            var eventKey = EventType.ToString();
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
                    executionTime = LevelEventExecutionTime.OnBar,
                    allowFirstFloor = _instance.AllowFirstFloor,
                    useGroups = false,
                    categories = _eventCategories,
                };
                Debug.Log("[OuterSwirl] TR: EventInfo created");

                var propsInfo = new Dictionary<string, ADOFAIPropInfo>();
                foreach (var (name, member) in _propMap)
                {
                    Debug.Log($"[OuterSwirl] TR: processing prop '{name}'");

                    Type propType = null;
                    object defaultValue = null;

                    if (member is System.Reflection.PropertyInfo pi)
                    {
                        propType = pi.PropertyType;
                        Debug.Log($"[OuterSwirl] TR: is PropertyInfo, type={propType?.Name}");
                        try { defaultValue = pi.GetValue(_instance); } catch { }
                    }
                    else if (member is System.Reflection.FieldInfo fi)
                    {
                        propType = fi.FieldType;
                        Debug.Log($"[OuterSwirl] TR: is FieldInfo, type={propType?.Name}");
                        defaultValue = fi.GetValue(_instance);
                    }
                    else
                    {
                        Debug.Log($"[OuterSwirl] TR: member is neither PropertyInfo nor FieldInfo: {member?.GetType().Name}");
                        continue;
                    }

                    var labelAttr = Attribute.GetCustomAttribute(member, typeof(PropertyLabelAttribute)) as PropertyLabelAttribute;

                    var pDict = new Dictionary<string, object>
                    {
                        ["name"] = name,
                        ["type"] = MapPropertyTypeString(propType),
                        ["default"] = defaultValue ?? "",
                        ["canBeDisabled"] = false,
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
                    catch { }
                }

                Debug.Log($"[OuterSwirl] Registered event '{_eventFullName}' (ID={CustomEventTypeBase})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OuterSwirl] Register event failed: {ex}");
            }
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

        public static void RegisterLocalization(string json)
        {
            try
            {
                Debug.Log($"[OuterSwirl] RL: start");
                var entries = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(json);
                if (entries == null) { Debug.Log("[OuterSwirl] RL: entries null"); return; }
                Debug.Log($"[OuterSwirl] RL: parsed {entries.Count} entries");

                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp-firstpass");
                if (asm == null) { Debug.Log("[OuterSwirl] RL: asm not found"); return; }
                Debug.Log("[OuterSwirl] RL: asm found");

                var locType = asm.GetType("SA.GoogleDoc.Localization");
                if (locType == null) { Debug.Log("[OuterSwirl] RL: Localization type null"); return; }
                Debug.Log("[OuterSwirl] RL: Localization type found");

                var clientField = locType.GetField("Client",
                    BindingFlags.Public | BindingFlags.Static);
                if (clientField == null) { Debug.Log("[OuterSwirl] RL: Client field null"); return; }
                Debug.Log("[OuterSwirl] RL: Client field found");

                var client = clientField.GetValue(null);
                if (client == null) { Debug.Log("[OuterSwirl] RL: client value null"); return; }
                Debug.Log($"[OuterSwirl] RL: client type={client.GetType().FullName}");

                var sheetField = client.GetType().GetField("SheetDictionary",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (sheetField == null) { Debug.Log("[OuterSwirl] RL: SheetDictionary field null"); return; }
                Debug.Log("[OuterSwirl] RL: SheetDictionary field found");

                var sheet = sheetField.GetValue(client) as Dictionary<SystemLanguage, Dictionary<string, string>>;
                if (sheet == null)
                {
                    Debug.Log("[OuterSwirl] RL: SheetDictionary not ready yet, will retry later");
                    _pendingLocJson = json;
                    return;
                }
                Debug.Log($"[OuterSwirl] RL: sheet has {sheet.Count} languages");

                foreach (var kvp in entries)
                {
                    string enValue = null;
                    foreach (var tKvp in kvp.Value)
                    {
                        if (!LangCodeMap.TryGetValue(tKvp.Key, out var lang)) continue;
                        if (!sheet.TryGetValue(lang, out var langDict))
                        {
                            langDict = new Dictionary<string, string>();
                            sheet[lang] = langDict;
                        }
                        langDict[kvp.Key] = tKvp.Value;
                        if (tKvp.Key == "en")
                            enValue = tKvp.Value;
                    }
                    // fallback: 未提供翻译的语言用英文补上
                    if (enValue != null)
                    {
                        foreach (var langKvp in sheet)
                        {
                            if (!langKvp.Value.ContainsKey(kvp.Key))
                                langKvp.Value[kvp.Key] = enValue;
                        }
                    }
                }
                Debug.Log("[OuterSwirl] RL: done");

                // 自动注入 editor.{EventType} 别名，这样编辑器显示事件名称时能找到翻译
                TryInjectEditorAlias(sheet);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OuterSwirl] Localization load error: {ex.Message}");
            }
        }

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

        [HarmonyPatch(typeof(scnGame), "ApplyEventsToFloors", typeof(List<scrFloor>))]
        internal static class ClearCachePatch
        {
            [HarmonyPrefix]
            static void ClearCache()
            {
                TryRegister();
                TryRegisterLocalization();
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

        [HarmonyPatch(typeof(scnGame), "ApplyEvent")]
        internal static class ApplyEventPatch
        {
            [HarmonyPrefix]
            static bool Prefix(LevelEvent evnt)
            {
                if ((int)evnt.eventType != CustomEventTypeBase) return true;

                var dataCopy = new Dictionary<string, object>();
                foreach (var (name, _) in _propMap)
                {
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

        [HarmonyPatch(typeof(LevelData), "EncodeToDictionary")]
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

        [HarmonyPatch(typeof(LevelData), "Decode")]
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

        // ===== Helper =====

        static void ApplyProperties(Dictionary<string, object> data)
        {
            if (data == null) return;
            foreach (var (name, member) in _propMap)
            {
                if (!data.TryGetValue(name, out var raw)) continue;
                try
                {
                    Type targetType = member switch
                    {
                        System.Reflection.PropertyInfo pi => pi.PropertyType,
                        System.Reflection.FieldInfo fi => fi.FieldType,
                        _ => null,
                    };
                    if (targetType == null) continue;

                    object val = (raw != null && targetType.IsInstanceOfType(raw))
                        ? raw : Convert.ChangeType(raw, targetType);

                    if (member is System.Reflection.PropertyInfo pi2)
                        pi2.SetValue(_instance, val);
                    else if (member is FieldInfo fi2)
                        fi2.SetValue(_instance, val);
                }
                catch { }
            }
        }
    }
}
