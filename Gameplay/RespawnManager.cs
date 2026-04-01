using System;
using UnityEngine;
using Odal.Architecture;
using Odal.Core;
using Odal.Vehicle;
using Odal.Track;

namespace Odal.Gameplay
{
    /// <summary>
    /// Система респауна (Скрипт №21).
    /// Переписана согласно легаси-логике:
    /// - Отказ от SafePoint. Всегда ищется ближайшая точка на сплайне.
    /// - Определение падения по trueTrackHeight (raycast от сплайна вниз до меша).
    /// - Респаун при перевороте (Flipped Check > 3с).
    /// - Мгновенное гашение физических скоростей и центрирование.
    /// </summary>
    public class RespawnManager : MonoBehaviour, IService, IUpdatable
    {
        [Header("Settings")]
        [Tooltip("Насколько низко относительно трассы можно упасть, чтобы сработал респаун.")]
        [SerializeField] private float _fallDepthThreshold = 15f;
        
        [Tooltip("Высота появления байка НАД треком при респауне")]
        [SerializeField] private float _respawnHeightOffset = 2f;
        
        [Tooltip("Время, которое машина может лежать вверх ногами до авто-респавна (секунды)")]
        [SerializeField] private float _maxFlippedTime = 3f;

        [Header("Track Raycasting")]
        [SerializeField] private LayerMask _trackLayer = ~0;
        [SerializeField] private float _raycastHeightOffset = 50f;

        public event Action OnRespawn;

        private ServiceLocator _locator;
        private SpherePhysicsCore _player;
        private ISplineProvider _spline;

        private float _flippedTimer;
        private float _checkTimer;
        private const float CHECK_INTERVAL = 0.2f;
        
        private Vector3 _lastNearestSplinePos;

        public void Init(ServiceLocator locator, SpherePhysicsCore player)
        {
            _locator = locator;
            _player  = player;

            try   { _spline = locator.GetService<ISplineProvider>(); }
            catch { /* без сплайна — fallback сценарий */ }

            _locator.RegisterService<RespawnManager>(this);
            _locator.GetService<UpdateManager>().RegisterUpdatable(this);

            if (_player != null)
                _lastNearestSplinePos = _player.transform.position;

            Debug.Log($"<b>RespawnManager</b>: Init OK. FallDepthThreshold={_fallDepthThreshold}");
        }

        private void OnDestroy()
        {
            if (_locator == null) return;
            _locator.GetService<UpdateManager>()?.UnregisterUpdatable(this);
            _locator.UnregisterService<RespawnManager>();
        }

        public void Tick(float deltaTime)
        {
            if (_player == null || _spline == null) return;

            _checkTimer -= deltaTime;
            if (_checkTimer <= 0f)
            {
                _checkTimer = CHECK_INTERVAL;
                _spline.GetNearestPoint(_player.transform.position, 
                    out _lastNearestSplinePos, out _, out _);
            }

            CheckFallRespawn();
            CheckFlippedRespawn(deltaTime);
        }

        private void CheckFallRespawn()
        {
            // Ищем физическую сгенерированную дорогу точно под сплайном
            float trueTrackHeight = _lastNearestSplinePos.y; 

            Vector3 rayStart = _lastNearestSplinePos + Vector3.up * _raycastHeightOffset;
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, _raycastHeightOffset * 2f, _trackLayer))
            {
                trueTrackHeight = hit.point.y; // Дорога найдена, берем её реальную высоту
            }

            // Вылетел по горизонтали? Используем TrackWidth из ISplineProvider
            Vector3 toPlayer = _player.transform.position - _lastNearestSplinePos;
            float horizontalDist = Vector3.ProjectOnPlane(toPlayer, Vector3.up).magnitude;
            float allowedWidth = (_spline.TrackWidth * 0.5f) + 1.5f;

            bool isFallen = _player.transform.position.y < trueTrackHeight - _fallDepthThreshold;
            bool isOutHorizontally = horizontalDist > allowedWidth;

            if (isFallen || isOutHorizontally)
            {
                DoRespawn();
            }
        }

        private void CheckFlippedRespawn(float deltaTime)
        {
            float upDotProduct = Vector3.Dot(_player.transform.up, Vector3.up);

            if (upDotProduct < 0.2f)
            {
                _flippedTimer += deltaTime;
                if (_flippedTimer >= _maxFlippedTime)
                {
                    DoRespawn();
                }
            }
            else
            {
                _flippedTimer = 0f;
            }
        }

        [ContextMenu("Force Respawn")]
        public void DoRespawn()
        {
            if (_player == null || _spline == null) return;

            _flippedTimer = 0f;

            // Вычисляем данные на сплайне В ТЕКУЩЕЙ точке
            _spline.GetNearestPoint(_player.transform.position, 
                out Vector3 worldPos, out Vector3 worldTangent, out Vector3 worldUp);

            // Ищем физическую сгенерированную дорогу, чтобы поставить байк прямо на неё
            Vector3 rayStart = worldPos + Vector3.up * _raycastHeightOffset;
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, _raycastHeightOffset * 2f, _trackLayer))
            {
                worldPos = hit.point;
                worldUp = hit.normal; // Выравниваем байк по реальному склону
                worldTangent = Vector3.ProjectOnPlane(worldTangent, worldUp).normalized;
            }

            // Move the vehicle to the nearest point on the road, elevated by the offset
            _player.transform.position = worldPos + (worldUp * _respawnHeightOffset);
            
            // Защита: Если tangent равен zero, используем forward
            if (worldTangent.sqrMagnitude > 0.001f)
                _player.transform.rotation = Quaternion.LookRotation(worldTangent, worldUp);

            // Мгновенное гашение скоростей (требование: public Rigidbody VehicleRigidbody => _rb;)
            Rigidbody rb = _player.VehicleRigidbody;
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            Debug.Log("<b>RespawnManager</b>: レスパウン完了 (Фулл респаун по легаси системе)!");
            OnRespawn?.Invoke();
        }
    }
}
