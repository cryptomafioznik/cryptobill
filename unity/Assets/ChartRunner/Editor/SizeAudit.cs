using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ChartRunner.EditorTools
{
    /// <summary>
    /// Аудит арта героя для iOS: реальный формат после импорта и потеря качества сжатия.
    /// Запуск: -buildTarget iOS -executeMethod ChartRunner.EditorTools.SizeAudit.Report
    ///
    /// Качество — PSNR по RGB, умноженному на альфу, плюс сама альфа (прозрачные пиксели
    /// не влияют). Картинка сжимается тем же кодером на CPU и распаковывается обратно.
    /// Контроли: несжатая копия обязана дать 99 дБ, ASTC 8×8 — ниже, чем 4×4.
    /// </summary>
    public static class SizeAudit
    {
        private const string Dir = "Assets/ChartRunner/Resources/Art";

        public static void Report()
        {
            AssetDatabase.ImportAsset(Dir, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
            var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { Dir })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToArray();
            var sb = new System.Text.StringBuilder();
            long bytes = 0;
            double worst = 99, worst8 = 99, ctrl = 0;
            var bad = 0;
            foreach (var p in paths)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                var size = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex);
                bytes += size;
                if (tex.format != TextureFormat.ASTC_4x4) bad++;
                var png = File.ReadAllBytes(p);
                var q4 = Psnr(png, TextureFormat.ASTC_4x4);
                worst = System.Math.Min(worst, q4);
                if (p.EndsWith("bike-0-body.png") || p.EndsWith("wheel-0-front.png"))
                {
                    ctrl = System.Math.Max(ctrl, 99 - Psnr(png, TextureFormat.RGBA32));
                    worst8 = System.Math.Min(worst8, Psnr(png, TextureFormat.ASTC_8x8));
                }
                sb.AppendLine($"{Path.GetFileName(p),-24} {tex.format,-10} {tex.width}x{tex.height} мипов {tex.mipmapCount,2} {size / 1024,5} KB  ASTC4x4 PSNR {q4:F1} дБ");
            }
            sb.AppendLine($"ИТОГО: {paths.Length} файлов, {bytes / 1048576.0:F1} МБ, не ASTC_4x4: {bad}, худший PSNR 4x4 {worst:F1} дБ");
            sb.AppendLine($"контроль: несжатая копия отклоняется от 99 дБ на {ctrl:F1}; ASTC 8x8 на контрольных {worst8:F1} дБ");
            File.WriteAllText("Logs/size-audit.txt", sb.ToString());
        }

        private static double Psnr(byte[] png, TextureFormat fmt)
        {
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            src.LoadImage(png);
            var enc = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            enc.LoadImage(png);
            if (fmt != TextureFormat.RGBA32) EditorUtility.CompressTexture(enc, fmt, 100);
            var a = src.GetPixels32();
            var b = enc.GetPixels32();
            double se = 0;
            for (var i = 0; i < a.Length; i++)
            {
                double aa = a[i].a / 255.0, ab = b[i].a / 255.0;
                for (var c = 0; c < 3; c++)
                {
                    var d = a[i][c] * aa - b[i][c] * ab;
                    se += d * d;
                }
                var da = a[i].a - (double)b[i].a;
                se += da * da;
            }
            Object.DestroyImmediate(src);
            Object.DestroyImmediate(enc);
            var mse = se / (a.Length * 4.0);
            return mse <= 1e-9 ? 99 : System.Math.Min(99, 10 * System.Math.Log10(255.0 * 255.0 / mse));
        }
    }
}
