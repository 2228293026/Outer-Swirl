using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;
using Outer_Swirl.Events;

namespace Outer_Swirl
{
    public class Main
    {
        internal static UnityModManager.ModEntry Mod;
        internal static bool IsEnabled { get; private set; }

        private static Harmony _harmony;

        public static void Load(UnityModManager.ModEntry modEntry)
        {
            Mod = modEntry;

            OuterSwirlEventSystem.Initialize(new SetOuterSwirlEvent());

            var locPath = Path.Combine(Mod.Path, "Localization.json");
            if (File.Exists(locPath))
                OuterSwirlEventSystem.RegisterLocalization(File.ReadAllText(locPath));

            _harmony = new Harmony(modEntry.Info.Id);
            // 初始化统一补丁管理器
            PatchManager.Initialize(_harmony);
            // 注册所有内部补丁
            RegisterAll();
            // 一次性应用所有已注册且已启用的补丁
            PatchManager.ApplyAll();

            Mod.OnToggle = OnToggle;
        }

        public static void RegisterAll()
        {
            PatchManager.RegisterAll();
        }

        private static bool OnToggle(UnityModManager.ModEntry ent, bool value)
        {
            IsEnabled = value;
            if (value)
            {
                // Mod 启用时，确保补丁全部生效
                PatchManager.ApplyAll();
            }
            else
            {
                // Mod 关闭时，全部卸载，以防单个补丁错误导致崩溃
                PatchManager.UnpatchAll();
            }
            return true;
        }
    }
}
