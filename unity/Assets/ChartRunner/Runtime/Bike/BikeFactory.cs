using ChartRunner.Input;
using ChartRunner.Track;
using ChartRunner.Tuning;
using UnityEngine;

namespace ChartRunner.Bike
{
    /// <summary>
    /// Собирает готовый байк: тела, соединения, реле контактов, контроллер.
    /// Всё кодом — префабов в проекте намеренно нет (хендофф §4).
    /// </summary>
    public static class BikeFactory
    {
        public static BikeController Spawn(BikeTuningProfile profile, LevelPhysicsOverride level,
            TerrainSampler terrain, IBikeInputSource input, Vector2 rearAxleWorldPos,
            bool withTelemetry = true)
        {
            var rig = BikeRig.Build(profile, rearAxleWorldPos);
            var controller = rig.gameObject.AddComponent<BikeController>();

            AttachRelay(rig.RearWheel, controller, true);
            AttachRelay(rig.FrontWheel, controller, false);

            controller.Initialise(profile, level, terrain, input);

            if (withTelemetry)
            {
                // Телеметрия добавляется ПОСЛЕ Initialise и только читает State.
                // Что она не влияет на физику — проверяется тестом сравнения траекторий,
                // а не заявляется в комментарии.
                var telemetry = rig.gameObject.AddComponent<Telemetry.BikeTelemetry>();
                telemetry.Bind(controller);
                rig.gameObject.AddComponent<Telemetry.TelemetryOverlay>();
            }

            return controller;
        }

        private static void AttachRelay(Rigidbody2D wheel, BikeController controller, bool isRear)
        {
            var relay = wheel.gameObject.AddComponent<BikeWheelContactRelay>();
            relay.Controller = controller;
            relay.IsRear = isRear;
        }

        /// <summary>
        /// Ставит байк на поверхность в точке x: задняя ось на высоте рельефа плюс радиус колеса.
        /// Без этого байк роняется с произвольной высоты и первые кадры уходят на посадку.
        /// </summary>
        public static Vector2 RestingRearAxle(TerrainSampler terrain, BikeTuningProfile profile, float xM)
        {
            return new Vector2(xM, terrain.HeightAt(xM) + profile.wheelRadiusM);
        }
    }
}
