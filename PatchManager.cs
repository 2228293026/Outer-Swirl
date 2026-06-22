using HarmonyLib;
using Outer_Swirl.Patch;
using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// 为字段生成并缓存 AccessTools 委托，以便快速读写。
        /// </summary>
        public static AccessTools.FieldRef<T, F> CreateFieldRef<T, F>(string fieldName) where T : class
        {
            if (string.IsNullOrWhiteSpace(fieldName))
                throw new ArgumentException("字段名不能为空", nameof(fieldName));

            var key = $"{typeof(T).FullName}.{fieldName}";
            lock (_lock)
            {
                if (_delegateCache.TryGetValue(key, out var cached))
                    return (AccessTools.FieldRef<T, F>)cached;

                var fieldRef = AccessTools.FieldRefAccess<T, F>(fieldName);
                _delegateCache[key] = fieldRef;
                return fieldRef;
            }
        }

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
                typeof(OuterSwirlEventSystem.ClearCachePatch),
                typeof(OuterSwirlEventSystem.EditorAwakePatch),
                typeof(OuterSwirlEventSystem.ApplyEventPatch),
                typeof(OuterSwirlEventSystem.MoveToNextFloorPatch),
                typeof(OuterSwirlEventSystem.EncodeToDictionaryPatch),
                typeof(OuterSwirlEventSystem.DecodePatch),
                typeof(FoolSwirlPatch.PatchStart),
                typeof(FoolSwirlPatch.PatchUpdateRefreshAngles),
                typeof(FoolSwirlPatch.PatchMoveToNextFloor)
            );
        }
    }
}
