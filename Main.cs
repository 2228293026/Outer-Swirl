using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Discord;
using HarmonyLib;
using static System.Net.Mime.MediaTypeNames;
using UnityEngine;
using UnityModManagerNet;

namespace Outer_Swirl
{
    public class Main
    {
        public static bool OuterSwirl;
        internal static UnityModManager.ModEntry Mod;

        private static Harmony _harmony;
        internal static bool IsEnabled { get; private set; }
        internal static MainSettings Settings { get; private set; }
        public static void Load(UnityModManager.ModEntry modEntry)
        {
            Settings = UnityModManager.ModSettings.Load<MainSettings>(modEntry);
            OuterSwirl = Settings.OuterSwirl;
            Mod = modEntry;
            Mod.OnToggle = new Func<UnityModManager.ModEntry, bool, bool>(OnToggle);
            Mod.OnGUI = Settings.OnGUI;
            Mod.OnSaveGUI = Settings.OnSaveGUI;
            Mod.OnHideGUI = Settings.OnHideGUI;

        }

        private static bool OnToggle(UnityModManager.ModEntry ent, bool value)
        {
            IsEnabled = value;
            if (value)
            {
                Main._harmony = new Harmony(Mod.Info.Id);
                Main._harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            else
            {
                Main._harmony.UnpatchAll(Mod.Info.Id);
                Main._harmony = null;
            }
            return true;
        }
    }
}
