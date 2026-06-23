using ADOFAI;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace Outer_Swirl.Patch
{
    public static class FoolSwirlPatch
    {
        internal static bool Active { get; set; }

        private static readonly MethodInfo _foolSwirlGetter = AccessTools.PropertyGetter(typeof(scrPlanet), "foolSwirl");

        private static readonly MethodInfo _getActiveMethod = AccessTools.Method(typeof(FoolSwirlPatch), nameof(GetActive));

        private static readonly FieldInfo _FOOL_SWIRL = AccessTools.Field(typeof(GCS), nameof(GCS.FOOL_SWIRL));

        private static readonly AccessTools.FieldRef<scrPlanet, Vector3> _addBobRef = AccessTools.FieldRefAccess<scrPlanet, Vector3>("addBob");

        private static readonly AccessTools.FieldRef<scrHoldRenderer, float> _isCCWMultRef = AccessTools.FieldRefAccess<scrHoldRenderer, float>("isCCWMult");


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
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> ins)
                => Replace(ins);
        }

        [HarmonyPatch(typeof(scrHoldRenderer), nameof(scrHoldRenderer.CreateMesh))]
        internal static class PatchCreateMesh
        {
            [HarmonyTranspiler]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> ins)
                => ReplaceCreateMesh(ins);
        }

        [HarmonyPatch(typeof(scrHoldRenderer), "UpdateFoolDir")]
        internal static class PatchUpdateFoolDir
        {
            [HarmonyPrefix]
            static bool Prefix(scrHoldRenderer __instance)
            {
                if (__instance.startFloor == null || __instance.m_meshRenderer == null)
                    return false;

                if (Active)
                {
                    // 愚人节模式：强制翻转
                    float correctCCW = __instance.startFloor.isCCW ? 1f : -1f;
                    _isCCWMultRef(__instance) = correctCCW;
                    __instance.m_meshRenderer.material.SetFloat("_CCW", correctCCW);
                    return false; // 跳过原始方法
                }
                else
                {
                    // 普通模式：让原始方法执行（它会根据 GCS.FOOL_SWIRL 设置）
                    return true;
                }
            }
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

                // 根据 Active 决定 _CCW
                float correctCCW;
                if (Active)
                {
                    // 愚人节翻转：与 isCCW 相反
                    correctCCW = __instance.startFloor.isCCW ? 1f : -1f;
                }
                else
                {
                    // 普通逻辑：与 isCCW 相同（isCCW ? -1 : 1）
                    correctCCW = __instance.startFloor.isCCW ? -1f : 1f;
                }

                _isCCWMultRef(__instance) = correctCCW;
                mat.SetFloat("_CCW", correctCCW);
            }
        }
    }
}
