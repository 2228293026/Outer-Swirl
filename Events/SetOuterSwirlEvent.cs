using ADOFAI;
using UnityEngine;

namespace Outer_Swirl.Events
{
    [EventName("OuterSwirlEvent.displayName")]
    [EventCategory("Gameplay")]
    public class SetOuterSwirlEvent : CustomEventBase
    {
        public override bool AllowFirstFloor => true;

        [EventProperty]
        [PropertyToggleable(true)]
        [PropertyLabel(LocalizationKey = "OuterSwirlEvent.Enable.label")]
        public bool Enable { get; set; } = true;

        public override void OnApply()
        {
            Patch.FoolSwirlPatch.Active = Enable;
        }

        public override void OnFloor()
        {
            Patch.FoolSwirlPatch.Active = Enable;
        }

        public override Sprite GetIcon()
        {
            if (GCS.levelEventIcons != null &&
                GCS.levelEventIcons.TryGetValue(LevelEventType.SetPlanetRotation, out var icon))
                return icon;
            return base.GetIcon();
        }
    }
}
