using ChartRunner.Tuning;
using UnityEditor;
using UnityEngine;

namespace ChartRunner.EditorTools
{
    /// <summary>
    /// Импорт спрайтов героя. Спрайты выгнаны ИЗ БРАУЗЕРНОЙ ИГРЫ её же функциями
    /// (bikeSprites при DPR=4) — это валидированный годами арт, а не новый рисунок.
    ///
    /// PPU выводится, а не назначается: канвас исходника рисует 12 пикселей на мировой
    /// пиксель (SPR_S=3 × DPR=4), мировой пиксель = UnitsContract.PxToM метров.
    /// Пивот корпуса — origin спрайта исходника: SPR_OX=34, SPR_OY=26 в боксе 72×48.
    /// </summary>
    public class SpriteImportSettings : AssetPostprocessor
    {
        private const float CanvasPxPerWorldPx = 12f; // SPR_S(3) × DPR(4)

        private void OnPreprocessTexture()
        {
            if (!assetPath.Contains("ChartRunner/Resources/Art/")) return;

            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = CanvasPxPerWorldPx / UnitsContract.PxToM;
            ti.mipmapEnabled = true;   // спрайт на экране втрое меньше нативного размера
            ti.filterMode = FilterMode.Trilinear;
            ti.alphaIsTransparency = true;
            ti.maxTextureSize = 2048;

            var s = ti.spritePivot;
            if (assetPath.Contains("bike-body"))
            {
                // SPR_OX/SPR_OY: origin в 34 px от левого края, 26 px от ВЕРХА (ось Y канваса вниз).
                s = new Vector2(34f / 72f, (48f - 26f) / 48f);
            }
            else
            {
                s = new Vector2(0.5f, 0.5f);
            }

            var settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = s;
            ti.SetTextureSettings(settings);
        }
    }
}
