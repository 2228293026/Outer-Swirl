using ADOFAI;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using UnityEngine;

namespace Outer_Swirl.Patch
{
    internal static class EventsInBarPatch
    {
        // 大数，让所有按钮都排在一页（不翻页）
        internal const int EventsInBar = 999;

        // ===== Transpiler =====

        static IEnumerable<CodeInstruction> ReplaceAll11(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instr in instructions)
            {
                if ((instr.opcode == OpCodes.Ldc_I4_S || instr.opcode == OpCodes.Ldc_I4) &&
                    OperandValue(instr.operand) == 11)
                {
                    yield return new CodeInstruction(OpCodes.Ldc_I4, EventsInBar);
                }
                else
                {
                    yield return instr;
                }
            }
        }

        static int OperandValue(object operand)
        {
            if (operand == null) return -1;
            if (operand is sbyte sb) return sb;
            if (operand is byte ub) return ub;
            if (operand is int iv) return iv;
            if (operand is short sv) return sv;
            if (operand is long lv) return (int)lv;
            return -1;
        }

        // ===== 自动适配宽度 =====

        static void FitToBusiestCategory(scnEditor __instance)
        {
            // 读按钮宽度
            var buttons = __instance.levelEventsBarButtons;
            if (buttons == null || buttons.childCount == 0) return;

            float buttonWidth = 0f;
            for (int i = 0; i < buttons.childCount; i++)
            {
                var rt = buttons.GetChild(i).GetComponent<RectTransform>();
                if (rt != null && rt.sizeDelta.x > 0f) { buttonWidth = rt.sizeDelta.x; break; }
            }
            if (buttonWidth <= 0f) return;

            // 读 eventButtons 字典，找最大类别的事件数
            var eventButtons = Traverse.Create(__instance)
                .Field("eventButtons")
                .GetValue<Dictionary<LevelEventCategory, List<LevelEventButton>>>();

            if (eventButtons == null || eventButtons.Count == 0) return;

            int maxCount = eventButtons.Values.Max(list => list?.Count ?? 0);
            if (maxCount <= 0) return;

            float newWidth = buttonWidth * maxCount; // 20px padding

            // 对称加宽父面板和子容器
            static void WidenRT(RectTransform rt, float w)
            {
                if (rt == null) return;
                float cur = rt.rect.width;
                if (w <= cur) return;
                float d = w - cur;
                rt.offsetMin = new Vector2(rt.offsetMin.x - d / 2f, rt.offsetMin.y);
                rt.offsetMax = new Vector2(rt.offsetMax.x + d / 2f, rt.offsetMax.y);
            }

            WidenRT(__instance.levelEventsBar, newWidth);
            WidenRT(buttons, newWidth);
        }

        // ===== Patch 声明 =====

        [HarmonyPatch(typeof(scnEditor), "RepositionEventButtons")]
        internal static class PatchReposition
        {
            [HarmonyTranspiler]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> ins)
                => ReplaceAll11(ins);
        }

        [HarmonyPatch(typeof(scnEditor), "SetCategory")]
        internal static class PatchSetCategory
        {
            [HarmonyTranspiler]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> ins)
                => ReplaceAll11(ins);
        }

        [HarmonyPatch(typeof(scnEditor), "SetupFavoritesCategory")]
        internal static class PatchSetupFavorites
        {
            [HarmonyTranspiler]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> ins)
                => ReplaceAll11(ins);
        }

        [HarmonyPatch(typeof(scnEditor), "LoadEditorProperties")]
        internal static class PatchLoadEditorProps
        {
            [HarmonyTranspiler]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> ins)
                => ReplaceAll11(ins);

            [HarmonyPostfix]
            static void Postfix(scnEditor __instance)
                => FitToBusiestCategory(__instance);
        }
    }
}
