using ADOFAI;
using DG.Tweening;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace Outer_Swirl.Patch
{
    public static class FoolSwirlPatch
    {
        // ---------- 运行时开关（行星用） ----------
        internal static bool Active { get; set; }

        // ---------- 楼层外圈状态表（hold 用，初始化时烘焙） ----------
        // 像 SetPlanetRotation 的 planetEase 一样逐楼层累积，
        // DrawHolds → CreateMesh 时按 startFloor 查表，无需运行时更新。
        private static readonly Dictionary<int, bool> _floorStates = new();

        internal static void PrecomputeFloorStates(List<LevelEvent> events)
        {
            _floorStates.Clear();
            if (events == null) return;

            // 只记录 Outer Swirl 事件的状态。按照事件顺序扫一遍，
            // 每个事件楼层记录切换后的状态。无事件的楼层在 GetFloorState 中回溯填充。
            foreach (var ev in events)
            {
                if ((int)ev.eventType == OuterSwirlEventSystem.CustomEventTypeBase)
                    _floorStates[ev.floor] = ev.GetBool("enabled");
            }
        }

        // 供 CreateMesh Transpiler 调用：获取某个 floor 的外圈状态。
        // 如果该楼层没有直接记录，回溯到前一个有记录的楼层。
        internal static bool GetFloorState(int seqId)
        {
            if (_floorStates.TryGetValue(seqId, out var state))
                return state;

            // 回溯查找前一个记录过的楼层
            for (int i = seqId - 1; i >= 0; i--)
                if (_floorStates.TryGetValue(i, out var s))
                    return s;

            return false;
        }

        // ---------- 反射缓存 ----------
        private static readonly MethodInfo _foolSwirlGetter =
            PatchManager.GetMethodInfo(typeof(scrPlanet), "get_foolSwirl");

        private static readonly MethodInfo _getActiveMethod =
            PatchManager.GetMethodInfo(typeof(FoolSwirlPatch), nameof(GetActive));

        private static readonly FieldInfo _FOOL_SWIRL =
            PatchManager.GetFieldInfo(typeof(GCS), nameof(GCS.FOOL_SWIRL));

        private static readonly AccessTools.FieldRef<scrPlanet, scrPlanet> _movingToNextRef =
            PatchManager.CreateFieldRef<scrPlanet, scrPlanet>("movingToNext");

        private static readonly AccessTools.FieldRef<scrPlanet, float> _swirlTweenRef =
            PatchManager.CreateFieldRef<scrPlanet, float>("swirlTween");

        private static readonly Func<scrPlanet, bool> _foolSwirl =
            PatchManager.CreatePropertyGetter<scrPlanet, bool>("foolSwirl");

        private static readonly MethodInfo _getFloorStateMethod =
            PatchManager.GetMethodInfo(typeof(FoolSwirlPatch), nameof(GetFloorState),
                new[] { typeof(int) });

        static bool GetActive() => Active;

        // ===== Transpiler 辅助方法 =====

        /// <summary>
        /// 行星 Transpiler：在 foolSwirl getter (call) 后注入 || Active。
        /// 保证运行时行星外圈可以动态切换。
        /// </summary>
        static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instr in instructions)
            {
                yield return instr;

                if (instr.opcode == OpCodes.Call && instr.operand is MethodInfo mi &&
                    mi == _foolSwirlGetter)
                {
                    yield return new CodeInstruction(OpCodes.Call, _getActiveMethod);
                    yield return new CodeInstruction(OpCodes.Or);
                }
            }
        }

        /// <summary>
        /// CreateMesh Transpiler：在 GCS.FOOL_SWIRL 字段 (ldsfld) 后注入 || GetFloorState(startFloor.seqID)。
        /// 注意：GCS.FOOL_SWIRL 是 static，所以 opcode 是 Ldsfld 不是 Ldfld。
        /// 这样每个 hold 创建时拿到对应楼层的烘焙值，不依赖运行时全局 Active。
        /// </summary>
        static IEnumerable<CodeInstruction> ReplaceCreateMesh(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instr in instructions)
            {
                yield return instr;

                // GCS.FOOL_SWIRL 是 public static bool → ldsfld
                if (instr.opcode == OpCodes.Ldsfld && instr.operand is FieldInfo fi && fi == _FOOL_SWIRL)
                {
                    // 栈顶是 GCS.FOOL_SWIRL 的值
                    // 再 push startFloor.seqID
                    yield return new CodeInstruction(OpCodes.Ldarg_0);   // this (scrHoldRenderer)
                    yield return new CodeInstruction(OpCodes.Ldfld,
                        AccessTools.Field(typeof(scrHoldRenderer), "startFloor"));  // this.startFloor
                    yield return new CodeInstruction(OpCodes.Ldfld,
                        AccessTools.Field(typeof(scrFloor), "seqID"));   // startFloor.seqID
                    yield return new CodeInstruction(OpCodes.Call, _getFloorStateMethod);  // GetFloorState(seqID)
                    yield return new CodeInstruction(OpCodes.Or);        // GCS.FOOL_SWIRL || GetFloorState(...)
                }
            }
        }

        // ===== 行星补丁 =====

        [HarmonyPatch(typeof(scrPlanet), "Start")]
        internal static class PatchStart
        {
            [HarmonyTranspiler]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> ins)
                => Replace(ins);
        }

        [HarmonyPatch(typeof(scrPlanet), "Update_RefreshAngles")]
        internal static class PatchUpdateRefreshAngles
        {
            [HarmonyTranspiler]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> ins)
                => Replace(ins);
        }

        [HarmonyPatch(typeof(scrPlanet), "MoveToNextFloor")]
        internal static class PatchMoveToNextFloor
        {
            [HarmonyTranspiler]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = new List<CodeInstruction>();

                foreach (var instr in instructions)
                {
                    codes.Add(instr);

                    if (instr.opcode == OpCodes.Call && instr.operand is MethodInfo mi &&
                        mi == _foolSwirlGetter)
                    {
                        codes.Add(new CodeInstruction(OpCodes.Call, _getActiveMethod));
                        codes.Add(new CodeInstruction(OpCodes.Or));
                    }
                }

                // 在 ret 前插入 SyncExtraPlanetsSwirlTween(this, floor)
                var syncMethod = PatchManager.GetMethodInfo(typeof(FoolSwirlPatch), nameof(SyncExtraPlanetsSwirlTween));
                for (int i = codes.Count - 1; i >= 0; i--)
                {
                    if (codes[i].opcode == OpCodes.Ret)
                    {
                        codes.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));      // this (scrPlanet)
                        codes.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_1));  // floor (scrFloor)
                        codes.Insert(i + 2, new CodeInstruction(OpCodes.Call, syncMethod));
                        break;
                    }
                }

                return codes;
            }
        }

        private static void SyncExtraPlanetsSwirlTween(scrPlanet instance, scrFloor floor)
        {
            if (floor == null || floor.numPlanets <= 2)
                return;

            var system = instance.planetarySystem;
            if (system == null) return;

            scrPlanet movingToNext = _movingToNextRef(instance);
            if (movingToNext == null) return;

            bool shouldSwirl = Active || _foolSwirl(instance);
            float targetSwirl = shouldSwirl ? 1f : 0f;
            float initialSwirl = shouldSwirl ? 0f : 1f;

            float duration = 0.5f;
            if (floor.nextfloor != null)
                duration = (float)(floor.nextfloor.entryTimePitchAdj - floor.entryTimePitchAdj);
            else if (floor.prevfloor != null)
                duration = (float)(floor.entryTimePitchAdj - floor.prevfloor.entryTimePitchAdj);
            duration = Mathf.Min(duration * 0.5f,
                (float)(instance.conductor.crotchetAtStart * floor.speed) / instance.conductor.song.pitch);
            if (duration <= 0f) duration = 0.5f;

            bool allMatched = true;
            foreach (var planet in system.planetList)
            {
                if (planet == null) continue;
                if (Mathf.Abs(_swirlTweenRef(planet) - targetSwirl) > 0.01f)
                    allMatched = false;
            }
            if (allMatched) return;

            foreach (var planet in system.planetList)
            {
                if (planet == null) continue;
                _swirlTweenRef(planet) = initialSwirl;
            }

            DOVirtual.DelayedCall(0.02f, () =>
            {
                foreach (var planet in system.planetList)
                {
                    if (planet == null) continue;
                    DOTween.To(() => _swirlTweenRef(planet),
                               x => _swirlTweenRef(planet) = x,
                               targetSwirl, duration)
                           .SetEase(Ease.OutSine);
                }
            });
        }

        // ===== Hold 补丁 =====

        /// <summary>
        /// CreateMesh Transpiler：逐楼层烘焙外圈状态。
        /// GCS.FOOL_SWIRL || GetFloorState(startFloor.seqID)
        /// 配合 PrecomputeFloorStates，编辑器和关卡加载时 holds 自动拿到正确方向。
        /// 运行时 Active 变化不触发 hold 更新——因为初始化时已经烘焙好了。
        /// </summary>
        [HarmonyPatch(typeof(scrHoldRenderer), nameof(scrHoldRenderer.CreateMesh))]
        internal static class PatchCreateMesh
        {
            [HarmonyTranspiler]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> ins)
                => ReplaceCreateMesh(ins);
        }

        /// <summary>
        /// 在 ApplyEventsToFloors 阶段逐楼层计算外圈状态。
        /// 类似 SetPlanetRotation 的做法：扫一遍事件，累计状态，确定每个楼层的外圈开关。
        /// </summary>
        [HarmonyPatch(typeof(scnGame), nameof(scnGame.ApplyEventsToFloors),
            new[] { typeof(List<scrFloor>), typeof(LevelData), typeof(scrLevelMaker), typeof(List<LevelEvent>) })]
        internal static class PreprocessFloorStates
        {
            [HarmonyPrefix]
            static void Prefix(List<LevelEvent> events)
            {
                PrecomputeFloorStates(events);
            }
        }
    }
}
