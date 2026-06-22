using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace Outer_Swirl.Patch
{
    public static class FoolSwirlPatch
    {
        internal static bool Active { get; set; }

        // same signature as get_foolSwirl() — takes (scrPlanet this), returns bool
        static bool GetActive(scrPlanet _) => Active;

        static readonly MethodInfo _target = AccessTools.Method(typeof(scrPlanet), "get_foolSwirl");
        static readonly MethodInfo _replacement = AccessTools.Method(typeof(FoolSwirlPatch), nameof(GetActive));

        static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instr in instructions)
            {
                if (instr.opcode == OpCodes.Call && instr.operand is MethodInfo mi && mi == _target)
                {
                    instr.operand = _replacement;
                }
                yield return instr;
            }
        }

        [HarmonyPatch(typeof(scrPlanet), "Start")]
        static class PatchStart
        {
            [HarmonyTranspiler]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> ins)
                => Replace(ins);
        }

        [HarmonyPatch(typeof(scrPlanet), "Update_RefreshAngles")]
        static class PatchUpdateRefreshAngles
        {
            [HarmonyTranspiler]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> ins)
                => Replace(ins);
        }

        [HarmonyPatch(typeof(scrPlanet), "MoveToNextFloor")]
        static class PatchMoveToNextFloor
        {
            [HarmonyTranspiler]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> ins)
                => Replace(ins);
        }
    }
}
