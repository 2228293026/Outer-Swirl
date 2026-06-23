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
            if (evnt.TryGet("enable", out bool enable))
                Enable = enable;
        }

        public override void StartEffect(scrPlanet planet = null)
        {
            Patch.FoolSwirlPatch.Active = Enable;
        }

        public override void Kill()
        {
            base.Kill();
        }
    }
}