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
        [Tooltip("How far down relative to the track you can fall before triggering respawn.")]
        [SerializeField] private float _fallDepthThreshold = 15f;
        
        [Tooltip("Grace period (seconds) before respawning after falling below threshold.")]
        [SerializeField] private float _fallGraceTime = 1.5f;

        [Tooltip("Height offset above the track when respawning.")]
        [SerializeField] private float _respawnHeightOffset = 2f;
        
        [Tooltip("Time the vehicle can stay flipped before auto-respawning (seconds).")]
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
        private float _currentFallTime;
        private const float CHECK_INTERVAL = 0.2f;
        
        private Vector3 _lastNearestSplinePos;
        private Vector3 _lastSplineTangent;
        private Vector3 _safeSplinePos;

        public void Init(ServiceLocator locator, SpherePhysicsCore player)
        {
            _locator = locator;
            _player  = player;

            try   { _spline = locator.GetService<ISplineProvider>(); }
            catch { /* без сплайна — fallback сценарий */ }

            _locator.RegisterService<RespawnManager>(this);
            _locator.GetService<UpdateManager>().RegisterUpdatable(this);

            if (_player != null)
            {
                _lastNearestSplinePos = _player.transform.position;
                _lastSplineTangent = _player.transform.forward;
                _safeSplinePos = _lastNearestSplinePos;
            }

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
                    out _lastNearestSplinePos, out _lastSplineTangent, out _);
            }

            CheckFallRespawn();
            CheckFlippedRespawn(deltaTime);
        }

        private void CheckFallRespawn()
        {
            // Look for the physical generated road precisely under the spline
            float trueTrackHeight = _lastNearestSplinePos.y; 

            int safeMask = _trackLayer & ~(1 << _player.gameObject.layer);
            Vector3 rayStart = _lastNearestSplinePos + Vector3.up * _raycastHeightOffset;
            bool didHitTrack = Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, _raycastHeightOffset * 2f, safeMask);

            if (didHitTrack)
            {
                trueTrackHeight = hit.point.y; // Road found, use its real height
            }

            // Check if player flew out horizontally based on TrackWidth
            Vector3 toPlayer = _player.transform.position - _lastNearestSplinePos;
            
            // CRUCIAL: Ignore forward distance caused by 0.2s cache lag during high speeds!
            Vector3 perpToPlayer = Vector3.ProjectOnPlane(toPlayer, _lastSplineTangent);
            float horizontalDist = Vector3.ProjectOnPlane(perpToPlayer, Vector3.up).magnitude;
            
            float allowedWidth = (_spline.TrackWidth * 0.5f) + 1.5f;

            bool isFallen = _player.transform.position.y < trueTrackHeight - _fallDepthThreshold;
            bool isOutHorizontally = horizontalDist > allowedWidth;

            if (isFallen || isOutHorizontally)
            {
                _currentFallTime += Time.deltaTime;
                if (_currentFallTime >= _fallGraceTime)
                {
                    DoRespawn();
                }
            }
            else
            {
                _currentFallTime = 0f;
                // Only track the safe position when the vehicle is actively on the road, AND the physical road exists!
                if (didHitTrack)
                {
                    _safeSplinePos = _lastNearestSplinePos;
                }
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
            _currentFallTime = 0f;
            _checkTimer = CHECK_INTERVAL; // Force refresh logic

            // Use the last SAFE track position before the player flew off, rather than their dead body pos
            _spline.GetNearestPoint(_safeSplinePos, 
                out Vector3 worldPos, out Vector3 worldTangent, out Vector3 worldUp);
            _lastNearestSplinePos = worldPos; // Explicitly update to stop immediate re-triggering!

            int safeMask = _trackLayer & ~(1 << _player.gameObject.layer);
            // Find the physical generated road to place the bike right on it
            Vector3 rayStart = worldPos + Vector3.up * _raycastHeightOffset;
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, _raycastHeightOffset * 2f, safeMask))
            {
                worldPos = hit.point;
                worldUp = hit.normal; // Align the bike with the real slope
                worldTangent = Vector3.ProjectOnPlane(worldTangent, worldUp).normalized;
            }

            Rigidbody rb = _player.VehicleRigidbody;
            CollisionDetectionMode oldMode = CollisionDetectionMode.Discrete;

            if (rb != null)
            {
                oldMode = rb.collisionDetectionMode;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete; // Prevent CCD from sweeping across the map
                rb.isKinematic = true; // Clear physics solver
            }

            Vector3 finalPos = worldPos + (worldUp * _respawnHeightOffset);
            Quaternion finalRot = _player.transform.rotation;

            // Protection: If tangent is zero, use forward
            if (worldTangent.sqrMagnitude > 0.001f)
                finalRot = Quaternion.LookRotation(worldTangent, worldUp);

            // Properly teleport the physics body and transform
            _player.transform.position = finalPos;
            _player.transform.rotation = finalRot;

            if (rb != null)
            {
                rb.position = finalPos;
                rb.rotation = finalRot;
                
                rb.isKinematic = false;
                rb.collisionDetectionMode = oldMode;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            Debug.Log("<b>RespawnManager</b>: Respawn triggered!");
            OnRespawn?.Invoke();
        }
    }
}
