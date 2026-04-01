using UnityEngine;
using Odal.Architecture;
using Odal.Core;
using Odal.Configs;

namespace Odal.Vehicle
{
    /// <summary>
    /// Лучевая система подвески (Скрипт №9).
    /// </summary>
    public class RaycastSuspension : MonoBehaviour, IFixedUpdatable
    {
        [Header("Raycast Points")]
        [Tooltip("Точки крепления колес (углы шасси)")]
        [SerializeField] private Transform[] _rayPoints = new Transform[4];
        
        [Header("Settings")]
        [SerializeField] private LayerMask _groundLayer;

        private Rigidbody _rb;
        private VehicleConfigSO _config;
        private ServiceLocator _locator;

        public Vector3 AverageNormal { get; private set; } = Vector3.up;
        public bool IsGrounded { get; private set; }

        public void Init(ServiceLocator locator, VehicleConfigSO config, Rigidbody rb)
        {
            _locator = locator;
            _config = config;
            _rb = rb;

            locator.GetService<UpdateManager>().RegisterFixedUpdatable(this);
        }

        private void OnDestroy()
        {
            _locator?.GetService<UpdateManager>()?.UnregisterFixedUpdatable(this);
        }

        public void FixedTick(float fixedDeltaTime)
        {
            if (_rb == null || _config == null) return;

            // Гарантируем нулевое угловое вращение каждый физический кадр.
            // FreezeRotation должно само справляться, но это страховка от
            // случайных угловых импульсов при столкновениях.
            _rb.angularVelocity = Vector3.zero;

            Vector3 normalSum = Vector3.zero;
            int hitCount = 0;

            foreach (var point in _rayPoints)
            {
                if (point == null) continue;

                Vector3 rayDir = -transform.up; 
                if (Physics.Raycast(point.position, rayDir, out RaycastHit hit, _config.SuspensionHeight, _groundLayer))
                {
                    float d = hit.distance;

                    Vector3 pointVelocity = _rb.GetPointVelocity(point.position);
                    
                    // Проекция скорости на нормаль (ось сжатия)
                    float v_rel = Vector3.Dot(pointVelocity, transform.up);

                    // Закон Гука
                    float springForce = (_config.SpringStiffness * (_config.SuspensionHeight - d)) - (_config.DampingCoefficient * v_rel);

                    // Прикладываем силу отталкивания в точке
                    _rb.AddForceAtPosition(transform.up * springForce, point.position, ForceMode.Force);

                    normalSum += hit.normal;
                    hitCount++;
                }
            }

            if (hitCount > 0)
            {
                AverageNormal = (normalSum / hitCount).normalized;
                IsGrounded = true;
            }
            else
            {
                AverageNormal = Vector3.Slerp(AverageNormal, Vector3.up, fixedDeltaTime * 2f);
                IsGrounded = false;
            }
        }
    }
}
