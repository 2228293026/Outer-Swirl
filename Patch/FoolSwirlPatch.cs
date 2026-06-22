using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace Outer_Swirl.Patch
{
    public static class FoolSwirlPatch
    {
        internal static bool Active { get; set; }

        private static readonly MethodInfo _foolSwirlGetter = AccessTools.PropertyGetter(typeof(scrPlanet), "foolSwirl");

        private static readonly MethodInfo _getActiveMethod = AccessTools.Method(typeof(FoolSwirlPatch), nameof(GetActive));

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
    }
}
