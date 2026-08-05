using UnityEngine;

namespace ChartRunner.Bike
{
    /// <summary>
    /// Передаёт контакты колеса контроллеру. Нужен потому, что коллизии приходят на объект
    /// колеса, а решение принимает шасси.
    ///
    /// Нормальная реакция берётся из <c>ContactPoint2D.normalImpulse</c>, а НЕ из скорости
    /// после решателя. Это требование docs/BIKE_PHYSICS_SPEC.md §5.1: в момент коллизии
    /// <c>Rigidbody2D.linearVelocity</c> уже пост-импульсная, и вся логика прижима мерила бы
    /// не то. Импульс делится на fixedDeltaTime в контроллере, давая силу в ньютонах.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class BikeWheelContactRelay : MonoBehaviour
    {
        public BikeController Controller;
        public bool IsRear;

        private void OnCollisionStay2D(Collision2D collision)
        {
            Report(collision);
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            Report(collision);
        }

        private void Report(Collision2D collision)
        {
            if (Controller == null) return;
            var total = 0f;
            var contacts = collision.contacts;
            for (var i = 0; i < contacts.Length; i++)
                total += contacts[i].normalImpulse;
            Controller.ReportWheelContact(IsRear, total);
        }
    }
}
