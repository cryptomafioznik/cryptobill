using ChartRunner.Tuning;
using UnityEngine;

namespace ChartRunner.Bike
{
    /// <summary>
    /// Собирает тела и соединения байка из <see cref="BikeTuningProfile"/> — кодом, не префабом.
    ///
    /// Состав соответствует docs/BIKE_PHYSICS_SPEC.md §2.2: шасси + два колеса на WheelJoint2D,
    /// подвеска вертикальная, мотор только на заднем.
    ///
    /// ЧТО ИЗМЕНИЛОСЬ ПРОТИВ ИСХОДНИКА И ПОЧЕМУ ЭТО ВАЖНО. В Canvas-версии колесо не было телом:
    /// точка на шасси, проникновение в полилинию, пружина по нормали и тангенциальная сила,
    /// вручную ограниченная µ·Fn. Здесь колесо — настоящий Rigidbody2D, контакт и трение считает
    /// Box2D. Из этого следует, что целый ряд членов исходника становится ЭМЕРДЖЕНТНЫМ и его
    /// НЕЛЬЗЯ переносить явно, иначе получится двойной учёт:
    ///
    ///   исходник                        в Unity
    ///   ------------------------------  --------------------------------------------------
    ///   ограничение тяги µ·FnDrive      трение Box2D само пропорционально нормальной реакции
    ///   driveTrade (вес назад = тяга)   сдвиг центра масс реально меняет нагрузку на колёса
    ///   ENGTQ (газ задирает нос)        тяга в точке контакта × высота ЦТ = настоящий момент
    ///   slopePull (импульс по склону)   составляющая гравитации
    ///   springK/springC                 JointSuspension2D
    ///
    /// Переносить явно надо только то, что было СОЗНАТЕЛЬНЫМ помощником, а не физикой:
    /// стабилизатор переда, подушка вилли, anti-loop, задний тормоз на вилли,
    /// авто-выравнивание в полёте. Они в <see cref="BikeController"/>.
    /// </summary>
    public class BikeRig : MonoBehaviour
    {
        public BikeTuningProfile Profile { get; private set; }

        public Rigidbody2D Chassis { get; private set; }
        public Rigidbody2D RearWheel { get; private set; }
        public Rigidbody2D FrontWheel { get; private set; }
        public WheelJoint2D RearJoint { get; private set; }
        public WheelJoint2D FrontJoint { get; private set; }
        public CircleCollider2D RearCollider { get; private set; }
        public CircleCollider2D FrontCollider { get; private set; }

        /// <summary>Базовый центр масс шасси в локальных координатах (без переноса веса).</summary>
        public Vector2 BaseCenterOfMass { get; private set; }

        /// <summary>
        /// Создаёт байк в точке. Ось колёс лежит на y = 0 локально, ЦТ выше на cgAboveAxleM.
        /// </summary>
        public static BikeRig Build(BikeTuningProfile profile, Vector2 rearAxleWorldPos,
            string name = "Bike", float rearGripCeilingScale = 1f)
        {
            var root = new GameObject(name);
            var rig = root.AddComponent<BikeRig>();
            rig.Profile = profile;

            var halfWb = profile.halfWheelbaseM;
            // Центр байка — посередине между осями.
            var center = rearAxleWorldPos + new Vector2(halfWb, 0f);
            root.transform.position = center;

            // ---- шасси ----
            rig.Chassis = root.AddComponent<Rigidbody2D>();
            rig.Chassis.mass = profile.chassisMassKg;
            rig.Chassis.useAutoMass = false;
            rig.Chassis.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rig.Chassis.interpolation = RigidbodyInterpolation2D.Interpolate;
            // useAutoMass = false — единственный флаг, который нужен Rigidbody2D, чтобы
            // centerOfMass и inertia перестали выводиться из коллайдеров и стали задаваемыми.
            // (automaticCenterOfMass / automaticInertiaTensor — свойства 3D Rigidbody, у 2D их нет.)
            rig.BaseCenterOfMass = new Vector2(0f, profile.cgAboveAxleM);
            rig.Chassis.centerOfMass = rig.BaseCenterOfMass;
            // Момент инерции задаём явно: он пересчитан из RB.inertia, а не выведен из коллайдера.
            rig.Chassis.inertia = profile.chassisInertiaKgM2;

            // Коллайдер рамы — НЕ несущий: колёса держат байк. Нужен, чтобы рама цеплялась
            // о рельеф при опрокидывании, а не проходила сквозь него.
            var body = root.AddComponent<CapsuleCollider2D>();
            body.direction = CapsuleDirection2D.Horizontal;
            body.size = new Vector2(halfWb * 1.6f, profile.wheelRadiusM * 1.1f);
            body.offset = new Vector2(0f, profile.cgAboveAxleM);

            // ---- колёса ----
            // ПОТОЛОК СЦЕПЛЕНИЯ ЗАДНЕГО поднят на множитель эндуро-буста.
            //
            // Почему статически, а не переменной трения: Box2D фиксирует трение в момент
            // создания контакта, поэтому менять материал на катящемся колесе — значит менять
            // его с непредсказуемым запаздыванием. Потолок ничего не делает, пока в него не
            // упираются: фактическую тягу задаёт момент мотора (BikeController.ClimbGripBoost),
            // а этот потолок лишь перестаёт её срезать на подъёме.
            //
            // Цена, которую надо знать: у ЗАДНЕГО колеса вместе с тягой растёт и предел
            // ТОРМОЖЕНИЯ. Поэтому поднят только задний потолок — основное торможение идёт
            // передним, и его предел не тронут. Влияние на тормозной путь проверяется
            // тестом C приёмочной батареи, а не рассуждением.
            rig.RearWheel = CreateWheel(root, "RearWheel",
                center + new Vector2(-halfWb, 0f), profile, out var rearCol,
                Mathf.Max(1f, rearGripCeilingScale));
            rig.FrontWheel = CreateWheel(root, "FrontWheel",
                center + new Vector2(halfWb, 0f), profile, out var frontCol);
            rig.RearCollider = rearCol;
            rig.FrontCollider = frontCol;

            // ---- соединения ----
            rig.RearJoint = CreateWheelJoint(root, rig.Chassis, rig.RearWheel,
                new Vector2(-halfWb, 0f), profile, motorised: true);
            rig.FrontJoint = CreateWheelJoint(root, rig.Chassis, rig.FrontWheel,
                new Vector2(halfWb, 0f), profile, motorised: false);

            return rig;
        }

        private static Rigidbody2D CreateWheel(GameObject parent, string name, Vector2 worldPos,
            BikeTuningProfile profile, out CircleCollider2D collider, float gripCeilingScale = 1f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, true);
            go.transform.position = worldPos;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.mass = profile.wheelMassKg;
            rb.useAutoMass = false;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            collider = go.AddComponent<CircleCollider2D>();
            collider.radius = profile.wheelRadiusM;
            collider.sharedMaterial = new PhysicsMaterial2D("Tyre")
            {
                friction = profile.tyreFriction * gripCeilingScale,
                bounciness = 0f
            };
            return rb;
        }

        private static WheelJoint2D CreateWheelJoint(GameObject host, Rigidbody2D chassis,
            Rigidbody2D wheel, Vector2 anchorLocal, BikeTuningProfile profile, bool motorised)
        {
            var joint = host.AddComponent<WheelJoint2D>();
            joint.connectedBody = wheel;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = anchorLocal;
            joint.connectedAnchor = Vector2.zero;

            joint.suspension = new JointSuspension2D
            {
                frequency = profile.suspensionFrequency,
                dampingRatio = profile.suspensionDamping,
                angle = 90f // ход подвески вертикальный в системе шасси
            };

            joint.useMotor = motorised;
            if (motorised)
            {
                joint.motor = new JointMotor2D { motorSpeed = 0f, maxMotorTorque = 0f };
            }

            return joint;
        }
    }
}
