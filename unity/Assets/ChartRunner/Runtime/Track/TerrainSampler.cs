using UnityEngine;

namespace ChartRunner.Track
{
    /// <summary>
    /// Аналог `terrainAt(x)` из исходника: высота и уклон в точке, в метрах и радианах.
    ///
    /// КРИТИЧНО (установлено измерением на пункте 4): уклон считается по СЕТКЕ узлов
    /// шагом 0.735 м, а НЕ как производная кривой. Это разные числа — на вупсах 35.6°
    /// против 35.75°, на секции 3 совпадают, — и физика исходника видела именно сетку,
    /// потому что коллизия шла по полилинии. Производная кривой нужна только для отчётов.
    ///
    /// Помощники управления читают уклон ВПЕРЕДИ по ходу (в исходнике
    /// `terrainAt(bike.x + max(20, vx*8)).ang`), поэтому сэмплер обязан быть доступен
    /// в произвольной точке, а не только под колесом.
    /// </summary>
    public class TerrainSampler
    {
        private readonly TrackProfile _profile;
        private readonly float _stepM;
        private readonly float _pxToM;

        public TerrainSampler(TrackProfile profile)
        {
            _profile = profile;
            _pxToM = Tuning.UnitsContract.PxToM;
            _stepM = profile.NodeStepM;
        }

        public TrackProfile Profile => _profile;

        /// <summary>Высота поверхности в метрах в мировой точке x (метры).</summary>
        public float HeightAt(float xM)
        {
            return _profile.HeightPx(xM / _pxToM) * _pxToM;
        }

        /// <summary>
        /// Уклон поверхности, радианы, положительный = вверх по ходу движения (+x).
        /// Считается ровно как в исходнике: atan2 разности высот соседних узлов СЕТКИ.
        /// </summary>
        public float SlopeAt(float xM)
        {
            var i = Mathf.Floor(xM / _stepM);
            var x0 = i * _stepM;
            var x1 = x0 + _stepM;
            return Mathf.Atan2(HeightAt(x1) - HeightAt(x0), _stepM);
        }

        /// <summary>Есть ли разрыв (пропасть) в точке x.</summary>
        public bool IsGap(float xM) => _profile.IsGap(xM / _pxToM);

        /// <summary>Финиш, метры.</summary>
        public float EndM => _profile.EndM;
    }
}
