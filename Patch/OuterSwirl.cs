using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;

namespace Outer_Swirl.Patch
{
    [HarmonyPatch(typeof(scrPlanet), "get_foolSwirl")]
    public static class H_scrPlanet_get_foolSwirl
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (Main.OuterSwirl)
            {
                __result = true;
            }
        }
    }
}
