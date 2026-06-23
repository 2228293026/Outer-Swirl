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
        internal static bool Active { get; set; }

        // 全部改用 PatchManager 缓存
        private static readonly MethodInfo _foolSwirlGetter =
            PatchManager.GetMethodInfo(typeof(scrPlanet), "get_foolSwirl");  // 属性 getter

        private static readonly MethodInfo _getActiveMethod =
            PatchManager.GetMethodInfo(typeof(FoolSwirlPatch), nameof(GetActive));

        private static readonly FieldInfo _FOOL_SWIRL =
            PatchManager.GetFieldInfo(typeof(GCS), nameof(GCS.FOOL_SWIRL));

        private static readonly AccessTools.FieldRef<scrHoldRenderer, float> _isCCWMultRef =
            PatchManager.CreateFieldRef<scrHoldRenderer, float>("isCCWMult");

        private static readonly AccessTools.FieldRef<scrPlanet, scrPlanet> _movingToNextRef =
            PatchManager.CreateFieldRef<scrPlanet, scrPlanet>("movingToNext");

        private static readonly AccessTools.FieldRef<scrPlanet, float> _swirlTweenRef =
            PatchManager.CreateFieldRef<scrPlanet, float>("swirlTween");

        private static readonly Func<scrPlanet, bool> _foolSwirl =
            PatchManager.CreatePropertyGetter<scrPlanet, bool>("foolSwirl");

        static bool GetActive() => Active;

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
        static IEnumerable<CodeInstruction> ReplaceCreateMesh(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instr in instructions)
            {
                yield return instr;

                if (instr.opcode == OpCodes.Ldfld && instr.operand is FieldInfo fi && fi == _FOOL_SWIRL)
                {
                    yield return new CodeInstruction(OpCodes.Call, _getActiveMethod);
                    yield return new CodeInstruction(OpCodes.Or);
                }
            }
        }

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

        [HarmonyPatch(typeof(scrHoldRenderer), nameof(scrHoldRenderer.CreateMesh))]
        internal static class PatchCreateMesh
        {
            [HarmonyTranspiler]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> ins)
                => ReplaceCreateMesh(ins);
        }

        [HarmonyPatch(typeof(scrHoldRenderer), "Update")]
        internal static class PatchHoldRendererUpdate
        {
            [HarmonyPostfix]
            static void Postfix(scrHoldRenderer __instance)
            {
                if (__instance.startFloor == null || __instance.m_meshRenderer == null) return;

                // 确保材质独立
                Material mat = __instance.m_meshRenderer.material;
                if (mat == __instance.m_meshRenderer.sharedMaterial && __instance.m_meshRenderer.sharedMaterial != null)
                {
                    mat = new Material(__instance.m_meshRenderer.sharedMaterial);
                    __instance.m_meshRenderer.material = mat;
                }

                /*
                float correctCCW;
                if (flip.HasValue)
                {
                    correctCCW = flip.Value ? (__instance.startFloor.isCCW ? 1f : -1f) : (__instance.startFloor.isCCW ? -1f : 1f);
                }
                else
                {
                    correctCCW = __instance.startFloor.isCCW ? -1f : 1f;
                }


                _isCCWMultRef(__instance) = correctCCW;
                mat.SetFloat("_CCW", correctCCW);
                */
            }
        }

        /*

        private static bool? GetHoldFlip(scrFloor floor)
        {
            if (floor == null) return null;
            var levelData = scnGame.instance?.levelData;
            if (levelData == null) return null;
            var events = levelData.levelEvents;
            if (events == null) return null;

            foreach (var ev in events)
            {
                if (ev.floor == floor.seqID && ev.eventType == LevelEventType.Hold)
                {
                    if (ev.ContainsKey("flip"))
                    {
                        object val = ev["flip"];
                        if (val is bool b) return b;
                        if (val is int i) return i != 0;
                    }
                    break;
                }
            }
            return null;
        }
        */
    }
}
