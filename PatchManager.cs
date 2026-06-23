using HarmonyLib;
using Outer_Swirl.Patch;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace Outer_Swirl
{
    /// <summary>
    /// 统一管理 Harmony 补丁的注册、启用、刷新、卸载。
    /// 所有对外方法均是线程安全的，可在任意线程调用。
    /// </summary>
    internal static class PatchManager
    {
        // ------------------- 内部类（提前声明） -------------------
        private class PatchRegistration
        {
            public Type PatchType { get; }
            public Func<bool> IsEnabled { get; }

            public PatchRegistration(Type patchType, Func<bool> isEnabled)
            {
                PatchType = patchType;
                IsEnabled = isEnabled;
            }
        }
        // -------------------------------------------------------

        #region 字段

        private static Harmony _harmony;
        private static readonly Dictionary<string, object> _delegateCache = new();
        private static readonly Dictionary<Type, PatchRegistration> _registeredPatches = new();
        private static readonly HashSet<Type> _appliedPatches = new();
        private static readonly object _lock = new();

        #endregion

        #region 生命周期

        /// <summary>
        /// 在 Mod 启动时由 Main 调用，确保内部状态干净。
        /// </summary>
        public static void Initialize(Harmony harmony)
        {
            lock (_lock)
            {
                _harmony = harmony ?? throw new ArgumentNullException(nameof(harmony));
                _delegateCache.Clear();
                _registeredPatches.Clear();
                _appliedPatches.Clear();
                _methodCache.Clear();   // 新增
                _fieldCache.Clear();    // 新增
                Debug.Log("[PatchManager] 初始化完成");
            }
        }

        #endregion

        #region 注册接口

        /// <summary>
        /// 注册单个补丁。<paramref name="toggle"/> 用于判断该补丁当前是否应启用。
        /// </summary>
        public static void RegisterPatch(Type patchType, Func<bool> toggle)
        {
            if (patchType == null) throw new ArgumentNullException(nameof(patchType));
            if (toggle == null) throw new ArgumentNullException(nameof(toggle));

            lock (_lock)
            {
                _registeredPatches[patchType] = new PatchRegistration(patchType, toggle);
                Debug.Log($"[PatchManager] 注册补丁: {patchType.FullName}");
            }
        }

        /// <summary>
        /// 批量注册补丁。所有补丁共享同一个 <paramref name="toggle"/> 回调。
        /// </summary>
        public static void RegisterPatches(Func<bool> toggle, params Type[] patchTypes)
        {
            if (toggle == null) throw new ArgumentNullException(nameof(toggle));
            if (patchTypes == null) throw new ArgumentNullException(nameof(patchTypes));

            foreach (var pt in patchTypes)
                RegisterPatch(pt, toggle);
        }

        #endregion

        #region 应用 / 移除

        /// <summary>
        /// 按注册表逐个尝试启用补丁。首次调用后，后续需要手动刷新（RefreshPatches）。
        /// </summary>
        public static void ApplyAll()
        {
            lock (_lock)
            {
                if (_harmony == null) return;

                foreach (var reg in _registeredPatches.Values)
                {
                    if (reg.IsEnabled())
                        ApplyInternal(reg.PatchType);
                }
            }
        }

        /// <summary>
        /// 只对指定补丁进行一次性启用（若已启用则忽略）。
        /// </summary>
        public static void ApplyPatch(Type patchType)
        {
            if (patchType == null) throw new ArgumentNullException(nameof(patchType));

            lock (_lock)
            {
                if (_harmony == null) return;
                if (_registeredPatches.TryGetValue(patchType, out var reg) && reg.IsEnabled())
                    ApplyInternal(patchType);
            }
        }

        /// <summary>
        /// 只对指定补丁进行一次性卸载（若未启用则忽略）。
        /// </summary>
        public static void RemovePatch(Type patchType)
        {
            if (patchType == null) throw new ArgumentNullException(nameof(patchType));

            lock (_lock)
            {
                if (_harmony == null) return;
                if (_appliedPatches.Contains(patchType))
                {
                    try
                    {
                        _harmony.CreateClassProcessor(patchType).Unpatch();
                        _appliedPatches.Remove(patchType);
                        Debug.Log($"[PatchManager] 已卸载: {patchType.FullName}");
                    }
                    catch (Exception ex)
                    {
                        Debug.Log($"[PatchManager] 卸载出错 ({patchType.FullName}): {ex}");
                    }
                }
            }
        }

        #endregion

        #region 刷新 / 全部卸载

        /// <summary>
        /// 根据最新的开关状态重新对齐已注册补丁的实际状态。
        /// </summary>
        public static void RefreshPatches()
        {
            lock (_lock)
            {
                if (_harmony == null) return;

                foreach (var reg in _registeredPatches.Values)
                {
                    var shouldBeEnabled = reg.IsEnabled();
                    var isApplied = _appliedPatches.Contains(reg.PatchType);

                    if (shouldBeEnabled && !isApplied)
                        ApplyInternal(reg.PatchType);
                    else if (!shouldBeEnabled && isApplied)
                        RemovePatch(reg.PatchType);
                }
            }
        }

        /// <summary>
        /// 彻底卸载本 Mod 所有补丁，并清空缓存。一般在 Mod 被禁用时调用。
        /// </summary>
        public static void UnpatchAll()
        {
            lock (_lock)
            {
                _harmony?.UnpatchAll(Main.Mod.Info.Id);
                _appliedPatches.Clear();
                _delegateCache.Clear();
                Debug.Log("[PatchManager] 已全部卸载");
            }
        }

        #endregion

        #region 辅助工具

        // 在 PatchManager 类中添加以下方法

        #region 实例字段
        public static AccessTools.FieldRef<T, F> CreateFieldRef<T, F>(string fieldName) where T : class
        {
            if (string.IsNullOrWhiteSpace(fieldName))
                throw new ArgumentException("字段名不能为空", nameof(fieldName));

            var key = $"Field:{typeof(T).FullName}.{fieldName}";
            lock (_lock)
            {
                if (_delegateCache.TryGetValue(key, out var cached))
                    return (AccessTools.FieldRef<T, F>)cached;

                var fieldRef = AccessTools.FieldRefAccess<T, F>(fieldName);
                _delegateCache[key] = fieldRef;
                return fieldRef;
            }
        }
        #endregion

        #region 实例属性
        public static Func<T, F> CreatePropertyGetter<T, F>(string propertyName) where T : class
        {
            var key = $"PropGet:{typeof(T).FullName}.{propertyName}";
            lock (_lock)
            {
                if (_delegateCache.TryGetValue(key, out var cached))
                    return (Func<T, F>)cached;

                var prop = AccessTools.Property(typeof(T), propertyName);
                if (prop == null) throw new MissingMemberException($"Property '{propertyName}' not found on {typeof(T)}");
                var getMethod = prop.GetGetMethod(true);
                if (getMethod == null) throw new InvalidOperationException($"Property '{propertyName}' has no getter");

                var del = (Func<T, F>)Delegate.CreateDelegate(typeof(Func<T, F>), getMethod);
                _delegateCache[key] = del;
                return del;
            }
        }

        public static Action<T, F> CreatePropertySetter<T, F>(string propertyName) where T : class
        {
            var key = $"PropSet:{typeof(T).FullName}.{propertyName}";
            lock (_lock)
            {
                if (_delegateCache.TryGetValue(key, out var cached))
                    return (Action<T, F>)cached;

                var prop = AccessTools.Property(typeof(T), propertyName);
                if (prop == null) throw new MissingMemberException($"Property '{propertyName}' not found on {typeof(T)}");
                var setMethod = prop.GetSetMethod(true);
                if (setMethod == null) throw new InvalidOperationException($"Property '{propertyName}' has no setter");

                var del = (Action<T, F>)Delegate.CreateDelegate(typeof(Action<T, F>), setMethod);
                _delegateCache[key] = del;
                return del;
            }
        }
        #endregion

        #region 静态字段
        public static Func<TField> CreateStaticFieldGetter<TField>(Type declaringType, string fieldName)
        {
            var key = $"StaticFieldGet:{declaringType.FullName}.{fieldName}";
            lock (_lock)
            {
                if (_delegateCache.TryGetValue(key, out var cached))
                    return (Func<TField>)cached;

                var fi = AccessTools.Field(declaringType, fieldName);
                if (fi == null) throw new MissingMemberException($"{declaringType}.{fieldName}");
                if (!fi.IsStatic) throw new ArgumentException("Field is not static");

                // 使用 DynamicMethod 生成 ldsfld + ret
                var method = new DynamicMethod($"get_{fieldName}", typeof(TField), Type.EmptyTypes, true);
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldsfld, fi);
                il.Emit(OpCodes.Ret);
                var del = (Func<TField>)method.CreateDelegate(typeof(Func<TField>));
                _delegateCache[key] = del;
                return del;
            }
        }

        public static Action<TField> CreateStaticFieldSetter<TField>(Type declaringType, string fieldName)
        {
            var key = $"StaticFieldSet:{declaringType.FullName}.{fieldName}";
            lock (_lock)
            {
                if (_delegateCache.TryGetValue(key, out var cached))
                    return (Action<TField>)cached;

                var fi = AccessTools.Field(declaringType, fieldName);
                if (fi == null) throw new MissingMemberException($"{declaringType}.{fieldName}");
                if (!fi.IsStatic) throw new ArgumentException("Field is not static");

                var method = new DynamicMethod($"set_{fieldName}", typeof(void), new[] { typeof(TField) }, true);
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Stsfld, fi);
                il.Emit(OpCodes.Ret);
                var del = (Action<TField>)method.CreateDelegate(typeof(Action<TField>));
                _delegateCache[key] = del;
                return del;
            }
        }
        #endregion

        #region 静态属性
        public static Func<TField> CreateStaticPropertyGetter<TField>(Type declaringType, string propertyName)
        {
            var key = $"StaticPropGet:{declaringType.FullName}.{propertyName}";
            lock (_lock)
            {
                if (_delegateCache.TryGetValue(key, out var cached))
                    return (Func<TField>)cached;

                var prop = AccessTools.Property(declaringType, propertyName);
                if (prop == null) throw new MissingMemberException($"{declaringType}.{propertyName}");
                var getMethod = prop.GetGetMethod(true);
                if (getMethod == null) throw new InvalidOperationException("Property has no getter");
                var del = (Func<TField>)Delegate.CreateDelegate(typeof(Func<TField>), getMethod);
                _delegateCache[key] = del;
                return del;
            }
        }

        public static Action<TField> CreateStaticPropertySetter<TField>(Type declaringType, string propertyName)
        {
            var key = $"StaticPropSet:{declaringType.FullName}.{propertyName}";
            lock (_lock)
            {
                if (_delegateCache.TryGetValue(key, out var cached))
                    return (Action<TField>)cached;

                var prop = AccessTools.Property(declaringType, propertyName);
                if (prop == null) throw new MissingMemberException($"{declaringType}.{propertyName}");
                var setMethod = prop.GetSetMethod(true);
                if (setMethod == null) throw new InvalidOperationException("Property has no setter");
                var del = (Action<TField>)Delegate.CreateDelegate(typeof(Action<TField>), setMethod);
                _delegateCache[key] = del;
                return del;
            }
        }
        #endregion

        // 在 PatchManager 的 #region 辅助工具 中添加

        #region MethodInfo / FieldInfo 缓存

        private static readonly Dictionary<string, MethodInfo> _methodCache = new Dictionary<string, MethodInfo>();
        private static readonly Dictionary<string, FieldInfo> _fieldCache = new Dictionary<string, FieldInfo>();

        /// <summary>
        /// 获取并缓存 MethodInfo（实例或静态）。
        /// </summary>
        public static MethodInfo GetMethodInfo(Type declaringType, string methodName, Type[] parameters = null, Type[] generics = null)
        {
            if (declaringType == null) throw new ArgumentNullException(nameof(declaringType));
            if (string.IsNullOrWhiteSpace(methodName)) throw new ArgumentException("方法名不能为空", nameof(methodName));

            string key = $"{declaringType.FullName}.{methodName}";
            if (parameters != null)
                key += "_" + string.Join(",", parameters.Select(t => t.FullName));
            if (generics != null)
                key += "_generic_" + string.Join(",", generics.Select(t => t.FullName));

            lock (_lock)
            {
                if (_methodCache.TryGetValue(key, out var cached))
                    return cached;

                MethodInfo method;
                if (parameters != null)
                    method = AccessTools.Method(declaringType, methodName, parameters, generics);
                else
                    method = AccessTools.Method(declaringType, methodName, generics);

                if (method == null)
                    throw new MissingMethodException($"在 {declaringType} 中找不到方法 {methodName}");

                _methodCache[key] = method;
                return method;
            }
        }

        /// <summary>
        /// 获取并缓存 FieldInfo（实例或静态）。
        /// </summary>
        public static FieldInfo GetFieldInfo(Type declaringType, string fieldName)
        {
            if (declaringType == null) throw new ArgumentNullException(nameof(declaringType));
            if (string.IsNullOrWhiteSpace(fieldName)) throw new ArgumentException("字段名不能为空", nameof(fieldName));

            string key = $"{declaringType.FullName}.{fieldName}";

            lock (_lock)
            {
                if (_fieldCache.TryGetValue(key, out var cached))
                    return cached;

                var field = AccessTools.Field(declaringType, fieldName);
                if (field == null)
                    throw new MissingFieldException($"在 {declaringType} 中找不到字段 {fieldName}");

                _fieldCache[key] = field;
                return field;
            }
        }

        #endregion

        /// <summary>
        /// 返回当前已注册的所有补丁类型（调试/状态展示用）。
        /// </summary>
        public static IReadOnlyCollection<Type> GetRegisteredPatchTypes()
        {
            lock (_lock)
            {
                return _registeredPatches.Keys.ToList().AsReadOnly();
            }
        }

        /// <summary>
        /// 返回当前已实际应用的补丁类型（调试/状态展示用）。
        /// </summary>
        public static IReadOnlyCollection<Type> GetAppliedPatchTypes()
        {
            lock (_lock)
            {
                return _appliedPatches.ToList().AsReadOnly();
            }
        }

        #endregion

        #region 私有实现

        // 统一的内部 Patch 方法，负责异常捕获与日志记录
        private static void ApplyInternal(Type patchType)
        {
            if (_appliedPatches.Contains(patchType)) return;

            try
            {
                _harmony.CreateClassProcessor(patchType).Patch();
                _appliedPatches.Add(patchType);
                Debug.Log($"[PatchManager] 已应用: {patchType.FullName}");
            }
            catch (Exception ex)
            {
                Debug.Log($"[PatchManager] 应用出错 ({patchType.FullName}): {ex}");
            }
        }

        #endregion

        /// <summary>
        /// 注册本项目内部所有已知的补丁类型。
        /// 仅在同一程序集内调用，不会暴露给外部。若后续新增补丁，只需在此处加入一行注册即可。
        /// </summary>
        public static void RegisterAll()
        {
            // 这里的补丁类均为 internal，仍然可以在同一程序集（Outer_Swirl）内部访问。
            RegisterPatches(() => true,
                typeof(OuterSwirlEventSystem.EditorAwakePatch),
                typeof(OuterSwirlEventSystem.ApplyEventPatch),
                typeof(OuterSwirlEventSystem.ScnGamePlayOuterSwirlResetPatch),
                typeof(OuterSwirlEventSystem.ParseEnum),
                typeof(OuterSwirlEventSystem.EnumGetValuesPatch),
                typeof(OuterSwirlEventSystem.RDStringGetWithCheckPatch),
                typeof(FoolSwirlPatch.PatchStart),
                typeof(FoolSwirlPatch.PatchUpdateRefreshAngles),
                typeof(FoolSwirlPatch.PatchMoveToNextFloor),
                typeof(OuterSwirlEventSystem.LevelDataEncode),
                typeof(OuterSwirlEventSystem.RdEditorUtilsCheckModsDependency),
                typeof(OuterSwirlEventSystem.LevelEventTypeToString),
                typeof(FoolSwirlPatch.PatchCreateMesh),
                typeof(FoolSwirlPatch.PatchHoldRendererUpdate)
            );
        }
    }
}
