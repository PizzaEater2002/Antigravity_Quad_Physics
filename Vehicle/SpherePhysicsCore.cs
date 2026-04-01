using UnityEngine;
using Odal.Architecture;
using Odal.Core;
using Odal.Configs;

namespace Odal.Vehicle
{
    /// <summary>
    /// Главный контроллер физики сферы (Скрипт №8).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class SpherePhysicsCore : MonoBehaviour, IFixedUpdatable
    {
        [Header("Components")]
        [SerializeField] private RaycastSuspension _suspension;
        [SerializeField] private GravityAligner _gravityAligner;

        private Rigidbody       _rb;
        private VehicleConfigSO _config;
        private ServiceLocator  _locator;

        public Rigidbody VehicleRigidbody => _rb;
        public RaycastSuspension Suspension => _suspension;

        public void Init(ServiceLocator locator, VehicleConfigSO config)
        {
            _locator = locator;
            _config  = config;
            _rb      = GetComponent<Rigidbody>();
            _rb.mass = _config.Mass;

            // Замораживаем вращение от физики — поворот только через transform
            _rb.constraints     = RigidbodyConstraints.FreezeRotation;
            _rb.angularDamping  = 0f;
            _rb.angularVelocity = Vector3.zero;

            if (_suspension     != null) _suspension.Init(locator, config, _rb);
            if (_gravityAligner != null) _gravityAligner.Init(locator, _rb, _suspension);

            locator.GetService<UpdateManager>().RegisterFixedUpdatable(this);
            Debug.Log($"<b>SpherePhysicsCore</b>: Init OK. Mass={_rb.mass}");
        }

        private void OnDestroy()
        {
            _locator?.GetService<UpdateManager>()?.UnregisterFixedUpdatable(this);
        }

        public void FixedTick(float fixedDeltaTime) { }

        // ═══════════════════════════════════════════════════════════════
        //  API управления — вызывается из GameBootstrapper
        // ═══════════════════════════════════════════════════════════════

        public void ApplyThrust(float force)
        {
            if (_rb == null || force < 0.001f) return;

            // Нормаль: из подвески или мировой up
            Vector3 up = (_suspension != null && _suspension.AverageNormal.sqrMagnitude > 0.001f)
                ? _suspension.AverageNormal : Vector3.up;

            Vector3 dir = Vector3.ProjectOnPlane(transform.forward, up);
            if (dir.sqrMagnitude < 0.001f) dir = Vector3.forward;
            dir.Normalize();

            _rb.AddForce(dir * force, ForceMode.Force);
        }

        public void ApplySteering(float normalizedSteer)
        {
            // normalizedSteer [-1..1] — прямо от анализатора
            if (Mathf.Abs(normalizedSteer) < 0.01f) return;

            float maxSpeed = (_config != null) ? _config.MaxSteeringSpeed : 60f;
            float yawDeg   = normalizedSteer * maxSpeed * Time.fixedDeltaTime;

            // 1. Поворачиваем transform (визуал + forward для газа)
            transform.Rotate(Vector3.up, yawDeg, Space.World);

            // 2. АРКАДНАЯ ФИЗИКА: поворачиваем ещё и вектор скорости!
            //    Без этого сфера крутится на месте, но летит в старом направлении.
            if (_rb.linearVelocity.sqrMagnitude > 0.1f)
            {
                Quaternion yawRot   = Quaternion.AngleAxis(yawDeg, Vector3.up);
                Vector3 flatVel     = Vector3.ProjectOnPlane(_rb.linearVelocity, Vector3.up);
                float   verticalVel = _rb.linearVelocity.y;    // вертикаль не трогаем (гравитация/прыжки)
                Vector3 newFlatVel  = yawRot * flatVel;
                _rb.linearVelocity  = newFlatVel + Vector3.up * verticalVel;
            }
        }

        public void ApplyBrake(float force)
        {
            if (_rb == null) return;
            Vector3 flatVel = Vector3.ProjectOnPlane(_rb.linearVelocity, Vector3.up);
            if (flatVel.sqrMagnitude > 0.01f)
                _rb.AddForce(-flatVel.normalized * force, ForceMode.Force);
        }
    }
}
