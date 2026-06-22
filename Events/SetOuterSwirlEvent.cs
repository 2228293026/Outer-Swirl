using ADOFAI;
using UnityEngine;

namespace Outer_Swirl.Events
{
    [EventName("OuterSwirlEvent")]
    [EventCategory("Gameplay")]
    public class SetOuterSwirlEvent : CustomEventBase
    {
        public override bool AllowFirstFloor => true;
        public override LevelEventExecutionTime ExecutionTime => LevelEventExecutionTime.OnPrebar;
        public override bool isDecoration => false;

        [EventProperty]
        [PropertyToggleable(true)]
        [PropertyLabel(LocalizationKey = "OuterSwirlEvent.Enable.label")]
        public bool Enable { get; set; } = true;

        public override void OnApply()
        {
            //Patch.FoolSwirlPatch.Active = Enable;
        }

        public override void OnFloor()
        {
            Patch.FoolSwirlPatch.Active = Enable;
        }

        private Sprite _flippedIcon;

        public override Sprite GetIcon()
        {
            if (_flippedIcon != null) return _flippedIcon;

            // 获取原始图标
            var original = GCS.levelEventIcons.TryGetValue(LevelEventType.SetPlanetRotation, out var icon) ? icon : null;
            if (original == null) return base.GetIcon();

            var srcTex = original.texture;
            var rect = original.rect;
            var width = (int)rect.width;
            var height = (int)rect.height;

            // 创建 RenderTexture
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(srcTex, rt);

            // 读取像素
            var tempTex = new Texture2D(width, height, TextureFormat.ARGB32, false);
            RenderTexture.active = rt;
            tempTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tempTex.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);

            // 翻转
            var colors = tempTex.GetPixels();
            var flipped = new Color[colors.Length];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    flipped[y * width + x] = colors[y * width + (width - 1 - x)];

            var newTex = new Texture2D(width, height);
            newTex.SetPixels(flipped);
            newTex.Apply();

            // 销毁临时纹理
            UnityEngine.Object.DestroyImmediate(tempTex);

            // 创建 Sprite
            var newRect = new Rect(0, 0, width, height);
            var pivot = original.pivot;
            var ppu = original.pixelsPerUnit;
            _flippedIcon = Sprite.Create(newTex, newRect, pivot, ppu);

            return _flippedIcon;
        }
    }
}
