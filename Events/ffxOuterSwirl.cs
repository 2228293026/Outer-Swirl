using ADOFAI;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Outer_Swirl.Events
{
    public class ffxOuterSwirl : ffxPlusBase
    {
        public bool Enable { get; set; } = true;

        public override void Decode(LevelEvent evnt)
        {
            Enable = evnt.GetBool("enabled");
        }

        public override void StartEffect(scrPlanet planet = null)
        {
            ResetEffect(Enable);
        }
        public static void ResetEffect(bool enable)
        {
            Patch.FoolSwirlPatch.Active = enable;
        }
    }
}