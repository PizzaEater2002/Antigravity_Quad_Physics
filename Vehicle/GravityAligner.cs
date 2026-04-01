using UnityEngine;
using Odal.Architecture;
using Odal.Core;

namespace Odal.Vehicle
{
    /// <summary>
    /// Выравнивает квадроцикл и применяет кастомную гравитацию (Скрипт №11).
    /// </summary>
    public class GravityAligner : MonoBehaviour, IFixedUpdatable
    {
        [Header("Settings")]
        [Tooltip("Сила гравитации (м/с^2)")]
        [SerializeField] private float _gravityForce = 9.81f;
        [Tooltip("Скорость выравнивания модели шасси по нормали")]
        [SerializeField] private float _alignSpeed = 8f;

        private Rigidbody _rb;
        private RaycastSuspension _suspension;
        private ServiceLocator _locator;

        public void Init(ServiceLocator locator, Rigidbody rb, RaycastSuspension suspension)
        {
            _locator = locator;
            _rb = rb;
            _suspension = suspension;

            if (_rb != null)
            {
                _rb.useGravity = false; // Отключаем стандартную
            }

            locator.GetService<UpdateManager>().RegisterFixedUpdatable(this);
        }

        private void OnDestroy()
        {
            _locator?.GetService<UpdateManager>()?.UnregisterFixedUpdatable(this);
        }

        public void FixedTick(float fixedDeltaTime)
        {
            if (_rb == null || _suspension == null) return;

            Vector3 nAvg = _suspension.AverageNormal;

            // ── Кастомная гравитация ─────────────────────────────────────────────
            // Направление гравитации — против нормали поверхности (или Vector3.down в воздухе)
            Vector3 gravDir = nAvg.sqrMagnitude > 0.001f ? -nAvg : Vector3.down;
            _rb.AddForce(gravDir * (_gravityForce * _rb.mass), ForceMode.Force);

            // ── Выравнивание по нормали ──────────────────────────────────────────

            // MATH CHECK: если AverageNormal ≈ нуль (в воздухе) — не вычисляем LookRotation!
            // Иначе получим NaN и «вертолёт».
            if (nAvg.sqrMagnitude < 0.001f)
            {
                // В воздухе: плавно выравниваемся к мировому up, не трогая направление forward
                Vector3 airForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                if (airForward.sqrMagnitude > 0.001f)
                {
                    Quaternion airTarget = Quaternion.LookRotation(airForward.normalized, Vector3.up);
                    transform.rotation   = Quaternion.Slerp(transform.rotation, airTarget, _alignSpeed * 0.3f * fixedDeltaTime);
                }
                return;
            }

            // На земле: выравниваем transform по нормали поверхности.
            // НЕ используем _rb.MoveRotation — он конфликтует с FreezeRotation.
            Vector3 forwardPlane = Vector3.ProjectOnPlane(transform.forward, nAvg);
            if (forwardPlane.sqrMagnitude < 0.001f)
            {
                // Если forward параллелен нормали — берём запасной вектор
                forwardPlane = Vector3.ProjectOnPlane(Vector3.forward, nAvg);
            }

            if (forwardPlane.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(forwardPlane.normalized, nAvg);
                transform.rotation   = Quaternion.Slerp(transform.rotation, targetRot, _alignSpeed * fixedDeltaTime);
            }
        }
    }
}
