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
            _harmony.PatchAll();

            Mod.OnToggle = OnToggle;
        }

        private static bool OnToggle(UnityModManager.ModEntry ent, bool value)
        {
            IsEnabled = value;
            return true;
        }
    }
}
