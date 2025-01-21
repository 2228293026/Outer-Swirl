using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityModManagerNet;

namespace Outer_Swirl
{
    public class MainSettings : UnityModManager.ModSettings, IDrawable
    {
        public bool OuterSwirl;
        public void OnChange()
        {
        }

        public void OnGUI(UnityModManager.ModEntry modEntry)
        {
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            Main.Settings.OuterSwirl = GUILayout.Toggle(Main.Settings.OuterSwirl, "Outer Swirl");
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        public void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            Main.Settings.Save(modEntry);
            Main.OuterSwirl = OuterSwirl;
            UnityModManager.ModSettings.Save(Main.Settings, modEntry);

        }

        public void OnHideGUI(UnityModManager.ModEntry modEntry)
        {
            OnSaveGUI(modEntry);
        }
    }
}

