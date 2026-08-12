using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ChartRunner.Game
{
    /// <summary>
    /// Пост-обработка кадра: HDR + bloom + виньетка + цветокоррекция. Включение движка,
    /// ради которого делался перенос, — до этого файла в проекте не использовалась НИ ОДНА
    /// возможность URP, и картинка законно проигрывала Canvas-версии.
    ///
    /// Что даёт каждый узел и почему он тут:
    ///  - Bloom — физика контражура: против низкого солнца всё яркое разливается по
    ///    оптике глаза/камеры. Порог 1.0 — светится только то, что ЯРЧЕ диапазона
    ///    обычных цветов, то есть ровно геометрия на Emissive-материале: кромка свечей
    ///    (= данные), солнце, маркеры. Правило «светятся только данные» соблюдено
    ///    конструкцией, а не дисциплиной.
    ///  - Vignette — сводит взгляд к центру кадра, где герой. Слабая: 0.24.
    ///  - ColorAdjustments — общий контраст и насыщенность golden hour. Умеренные
    ///    значения: сцена уже окрашена вершинными цветами, пост только доводит.
    ///
    /// Всё строится кодом, как и остальная сцена: ассет Volume-профиля нельзя собрать
    /// и проверить в batchmode.
    /// </summary>
    public static class PostFX
    {
        /// <summary>
        /// Вешает пост-обработку на камеру. Возвращает false, если URP не активен —
        /// молчать об этом нельзя: без URP весь Emissive-свет умирает вместе с bloom.
        /// </summary>
        public static bool Attach(Camera cam)
        {
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                Debug.LogError("PostFX: render pipeline НЕ активен — bloom и HDR не работают. "
                               + "Проверь QualitySettings.customRenderPipeline.");
                return false;
            }

            cam.allowHDR = true;

            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            // Сглаживание краёв — MSAA из ассета конвейера; FXAA поверх только мылит.
            data.antialiasing = AntialiasingMode.None;

            var go = new GameObject("PostVolume");
            go.transform.SetParent(cam.transform, false);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 0f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "ChartRunnerPost";

            var bloom = profile.Add<Bloom>();
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.85f);
            // Широкий рассев: свечение как от источника в дымке, а не резкий ореол.
            bloom.scatter.Override(0.72f);

            var vig = profile.Add<Vignette>();
            vig.intensity.Override(0.24f);
            vig.smoothness.Override(0.42f);

            var grade = profile.Add<ColorAdjustments>();
            grade.saturation.Override(8f);
            grade.contrast.Override(10f);

            vol.sharedProfile = profile;
            return true;
        }
    }
}
