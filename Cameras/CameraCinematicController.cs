using UnityEngine;
using Odal.Architecture;
using Odal.Core;
using Odal.Track;
using Odal.Input;

namespace Odal.Cameras
{
    /// <summary>
    /// Кинематическая камера гонки (Скрипт №20).
    /// Следует за сферой, смотрит вперёд по трассе, динамический FOV, тряска.
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Camera))]
    public class CameraCinematicController : MonoBehaviour, IService, IUpdatable
    {
        [Header("Follow Target")]
        [SerializeField] private Transform _target;
        [SerializeField] private Rigidbody _targetRigidbody;
        [SerializeField] private Vector3 _followOffset = new Vector3(0f, 4f, -10f);
        [SerializeField] private float _followSmoothTime = 0.25f;

        [Header("Look Ahead")]
        [SerializeField] private float _lookAheadDistance = 15f;
        [SerializeField] private float _lookAtSmoothSpeed = 5f;

        [Header("Dynamic FOV")]
        [SerializeField] private float _normalFov = 70f;
        [SerializeField] private float _boostFov  = 95f;
        [SerializeField] private float _fovSmoothSpeed = 4f;

        [Header("Camera Shake")]
        [SerializeField] private float _maxShakeIntensity = 0.35f;
        [SerializeField] private float _shakeSpeed = 25f;
        [SerializeField] private float _maxImpactVelocity = 15f;
        [SerializeField] private float _minImpactVelocity = 4f;
        [SerializeField] private float _shakeDecaySpeed = 3.5f;

        private ServiceLocator          _locator;
        private ISplineProvider         _splineProvider;
        private JoystickGestureAnalyzer _gestureAnalyzer;
        private UnityEngine.Camera      _cam;

        private Vector3 _smoothVelocity;
        private Vector3 _currentLookTarget;
        private Vector3 _lastStableDir = Vector3.forward;

        private float _currentShakeAmount;
        private float _prevVerticalVelocity;
        private float _perlinSeedX;
        private float _perlinSeedY;

        // ═══════════════════════════════════════════════════════════════

        public void Init(ServiceLocator locator)
        {
            _locator = locator;
            _cam     = GetComponent<UnityEngine.Camera>();

            if (_target == null)
                Debug.LogError("<b>Camera</b>: _target is not assigned!", this);

            try   { _splineProvider  = locator.GetService<ISplineProvider>(); }
            catch { /* без сплайна — ОК */ }

            try   { _gestureAnalyzer = locator.GetService<JoystickGestureAnalyzer>(); }
            catch { /* без FOV — ОК */ }

            if (_target != null)
                _currentLookTarget = _target.position;

            _perlinSeedX = Random.Range(0f, 100f);
            _perlinSeedY = Random.Range(100f, 200f);

            locator.RegisterService<CameraCinematicController>(this);
            locator.GetService<UpdateManager>().RegisterUpdatable(this);

            SnapToTarget(); // Снап на старте игры
            
            Debug.Log($"<b>Camera</b>: Init OK. Target={(_target != null ? _target.name : "NULL")}");
        }

        private void OnDestroy()
        {
            if (_locator == null) return;
            _locator.GetService<UpdateManager>()?.UnregisterUpdatable(this);
            _locator.UnregisterService<CameraCinematicController>();
        }

        // ═══════════════════════════════════════════════════════════════

        public void Tick(float dt)
        {
            if (_target == null) return;

            // 1. Позиция — следуем за целью
            Vector3 basePos = CalculateFollowPosition(dt);

            // 2. Шейк
            DetectLanding();
            _currentShakeAmount = Mathf.MoveTowards(_currentShakeAmount, 0f, _shakeDecaySpeed * dt);
            Vector3 shake = GetShakeOffset();

            transform.position = basePos + shake;

            // 3. Взгляд — на цель + немного вперёд
            UpdateLookAt(dt);

            // 4. FOV
            UpdateFov(dt);
        }

        // ═══════════════════════════════════════════════════════════════
        //  1. Follow — по вектору скорости сферы
        // ═══════════════════════════════════════════════════════════════

        private Vector3 CalculateFollowPosition(float dt)
        {
            // Определяем направление «сзади» по вектору скорости (не target.forward)
            if (_targetRigidbody != null)
            {
                Vector3 flatVel = Vector3.ProjectOnPlane(_targetRigidbody.linearVelocity, Vector3.up);
                if (flatVel.sqrMagnitude > 1f) // > 1 м/с
                {
                    _lastStableDir = flatVel.normalized;
                }
                else
                {
                    // Если стоим (после старта/респавна), берём направление носа
                    Vector3 ff = Vector3.ProjectOnPlane(_target.forward, Vector3.up);
                    if (ff.sqrMagnitude > 0.001f) _lastStableDir = ff.normalized;
                }
            }
            else
            {
                // fallback: transform.forward
                Vector3 ff = Vector3.ProjectOnPlane(_target.forward, Vector3.up);
                if (ff.sqrMagnitude > 0.001f) _lastStableDir = ff.normalized;
            }

            Quaternion yaw = Quaternion.LookRotation(_lastStableDir, Vector3.up);
            Vector3 desired = _target.position + yaw * _followOffset;

            return Vector3.SmoothDamp(transform.position, desired, ref _smoothVelocity,
                _followSmoothTime, Mathf.Infinity, dt);
        }

        // ═══════════════════════════════════════════════════════════════
        //  2. LookAt — среднее между target и точка вперёд
        // ═══════════════════════════════════════════════════════════════

        private void UpdateLookAt(float dt)
        {
            Vector3 aheadPoint;

            if (_splineProvider != null)
            {
                _splineProvider.GetNearestPoint(_target.position,
                    out Vector3 nearest, out Vector3 tangent, out _);
                aheadPoint = nearest + tangent.normalized * _lookAheadDistance;
            }
            else
            {
                aheadPoint = _target.position + _lastStableDir * _lookAheadDistance;
            }

            // 95% на байк, 5% на сплайн — камера жёстко привязана к машине
            Vector3 lookGoal = Vector3.Lerp(_target.position, aheadPoint, 0.05f);

            _currentLookTarget = Vector3.Lerp(_currentLookTarget, lookGoal, _lookAtSmoothSpeed * dt);

            Vector3 lookDir = _currentLookTarget - transform.position;
            if (lookDir.sqrMagnitude > 0.01f)
            {
                Quaternion rot = Quaternion.LookRotation(lookDir, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, rot, _lookAtSmoothSpeed * dt);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  3. FOV
        // ═══════════════════════════════════════════════════════════════

        private void UpdateFov(float dt)
        {
            if (_cam == null) return;
            bool boost = _gestureAnalyzer != null && _gestureAnalyzer.IsBoosting;
            float goal = boost ? _boostFov : _normalFov;
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, goal, _fovSmoothSpeed * dt);
        }

        // ═══════════════════════════════════════════════════════════════
        //  4. Shake
        // ═══════════════════════════════════════════════════════════════

        private void DetectLanding()
        {
            if (_targetRigidbody == null) return;
            float vy = _targetRigidbody.linearVelocity.y;
            bool fell = _prevVerticalVelocity < -_minImpactVelocity;
            bool stopped = vy > _prevVerticalVelocity + _minImpactVelocity * 0.5f;
            if (fell && stopped)
            {
                float impact = Mathf.Abs(_prevVerticalVelocity);
                float norm = Mathf.Clamp01((impact - _minImpactVelocity) /
                    Mathf.Max(_maxImpactVelocity - _minImpactVelocity, 0.1f));
                _currentShakeAmount = Mathf.Max(_currentShakeAmount, norm);
            }
            _prevVerticalVelocity = vy;
        }

        private Vector3 GetShakeOffset()
        {
            if (_currentShakeAmount <= 0.001f) return Vector3.zero;
            float t = Time.time * _shakeSpeed;
            float nx = (Mathf.PerlinNoise(t + _perlinSeedX, 0f) - 0.5f) * 2f;
            float ny = (Mathf.PerlinNoise(0f, t + _perlinSeedY) - 0.5f) * 2f;
            return transform.rotation * new Vector3(nx, ny, 0f) * (_currentShakeAmount * _maxShakeIntensity);
        }

        public void TriggerShake(float intensity)
        {
            _currentShakeAmount = Mathf.Clamp01(Mathf.Max(_currentShakeAmount, intensity));
        }

        /// <summary>
        /// Моментально перемещает и поворачивает камеру, чтобы смотреть туда же, куда направлен нос машины.
        /// </summary>
        public void SnapToTarget()
        {
            if (_target == null) return;

            _smoothVelocity = Vector3.zero;

            Vector3 ff = Vector3.ProjectOnPlane(_target.forward, Vector3.up);
            if (ff.sqrMagnitude > 0.001f) _lastStableDir = ff.normalized;

            Quaternion yaw = Quaternion.LookRotation(_lastStableDir, Vector3.up);
            transform.position = _target.position + yaw * _followOffset;

            _currentLookTarget = _target.position + _lastStableDir * _lookAheadDistance;
            Vector3 lookDir = _currentLookTarget - transform.position;
            if (lookDir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
        }
    }
}
