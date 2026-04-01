using UnityEngine;
using Odal.Architecture;
using Odal.Vehicle;
using Odal.Configs;
using Odal.Input;
using Odal.Track;
using Odal.Cameras;
using Odal.Gameplay;

namespace Odal.Core
{
    /// <summary>
    /// Главная точка входа. Связывает ввод, физику и камеру.
    /// </summary>
    public class GameBootstrapper : MonoBehaviour, IFixedUpdatable
    {
        [Header("System")]
        [SerializeField] private UpdateManager _updateManagerPrefab;

        [Header("Player Vehicle")]
        [SerializeField] private VehicleConfigSO          _vehicleConfig;
        [SerializeField] private SpherePhysicsCore        _playerVehicle;

        [Header("Input")]
        [Tooltip("Сырой тач-ввод (без визуала). Создаётся автоматически если не назначен.")]
        [SerializeField] private InputTouchHandler        _inputTouchHandler;

        [Tooltip("Визуальный экранный джойстик (Canvas). Если назначен — InputTouchHandler не используется.")]
        [SerializeField] private VirtualJoystickUI         _virtualJoystick;

        [Header("Track")]
        [SerializeField] private SplinePathAdapter        _splinePathAdapter;

        [Header("Camera")]
        [SerializeField] private CameraCinematicController _cinematicCamera;

        [Header("Gameplay")]
        [Tooltip("Менеджер респауна. Опционален — если не назначен, создаётся динамически.")]
        [SerializeField] private Odal.Gameplay.RespawnManager _respawnManager;

        private ServiceLocator          _serviceLocator;
        private UpdateManager           _activeUpdateManager;
        private JoystickGestureAnalyzer _analyzer;

        private void Awake()
        {
            Application.targetFrameRate = 60; // Разлочка 60 FPS для плавности на мобилках
            InitArchitecture();
            InitGame();
        }

        private void InitArchitecture()
        {
            _serviceLocator = new ServiceLocator();

            if (_updateManagerPrefab != null)
                _activeUpdateManager = Instantiate(_updateManagerPrefab, transform);
            else
            {
                var go = new GameObject("[UpdateManager]");
                go.transform.SetParent(transform);
                _activeUpdateManager = go.AddComponent<UpdateManager>();
            }

            _serviceLocator.RegisterService<UpdateManager>(_activeUpdateManager);
        }

        private void InitGame()
        {
            // --- Проверка обязательных ---
            if (_vehicleConfig == null) { Debug.LogError("Bootstrapper: _vehicleConfig = null!", this); return; }
            if (_playerVehicle == null) { Debug.LogError("Bootstrapper: _playerVehicle = null!", this); return; }

            // 1. Физика
            _playerVehicle.Init(_serviceLocator, _vehicleConfig);

            // 2. Анализатор жестов
            _analyzer = new JoystickGestureAnalyzer();
            _serviceLocator.RegisterService<JoystickGestureAnalyzer>(_analyzer);

            // 3. Ввод — если есть визуальный джойстик, он приоритетнее
            if (_virtualJoystick != null)
            {
                _virtualJoystick.Init(_serviceLocator, _analyzer);
                Debug.Log("<b>Bootstrapper</b>: Используется VirtualJoystickUI.");
            }
            else
            {
                // Фоллбэк — сырые касания без визуала
                if (_inputTouchHandler == null)
                {
                    var go = new GameObject("[InputTouchHandler]");
                    go.transform.SetParent(transform);
                    _inputTouchHandler = go.AddComponent<InputTouchHandler>();
                }
                _inputTouchHandler.Init(_serviceLocator, _analyzer);
            }

            // 4. Сплайн (до камеры)
            if (_splinePathAdapter != null)
                _splinePathAdapter.Init(_serviceLocator);

            // 4.5 Авто-выравнивание сферы по направлению сплайна при старте
            AlignVehicleToSpline();

            // 5. Камера
            if (_cinematicCamera != null)
                _cinematicCamera.Init(_serviceLocator);

            // 6. Респаун
            if (_respawnManager == null)
            {
                var go = new GameObject("[RespawnManager]");
                go.transform.SetParent(transform);
                _respawnManager = go.AddComponent<Odal.Gameplay.RespawnManager>();
            }
            _respawnManager.Init(_serviceLocator, _playerVehicle);

            // 7. Подписка на FixedUpdate
            _activeUpdateManager.RegisterFixedUpdatable(this);

            Debug.Log("<b>Bootstrapper</b>: Все системы запущены.");
        }

        /// <summary>
        /// При старте разворачивает сферу forward по касательной сплайна,
        /// чтобы камера сразу смотрела вдоль трассы.
        /// </summary>
        private void AlignVehicleToSpline()
        {
            if (_playerVehicle == null) return;

            Odal.Track.ISplineProvider spline = null;
            try { spline = _serviceLocator.GetService<Odal.Track.ISplineProvider>(); }
            catch { /* сплайна нет — ОК */ }

            if (spline == null) return;

            spline.GetNearestPoint(
                _playerVehicle.transform.position,
                out _, out Vector3 tangent, out _);

            Vector3 flatTangent = Vector3.ProjectOnPlane(tangent, Vector3.up);
            if (flatTangent.sqrMagnitude > 0.001f)
            {
                _playerVehicle.transform.rotation = Quaternion.LookRotation(flatTangent.normalized, Vector3.up);
                Debug.Log($"<b>Bootstrapper</b>: Сфера выровнена по сплайну.");
            }
        }

        private void OnDestroy()
        {
            _activeUpdateManager?.UnregisterFixedUpdatable(this);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Связка ввода → физики
        // ═══════════════════════════════════════════════════════════════

        public void FixedTick(float dt)
        {
            if (_analyzer == null || _playerVehicle == null || _vehicleConfig == null) return;

            // --- Клавиатурный ввод в редакторе ---
#if UNITY_EDITOR
            ReadKeyboard();
#endif

            if (!_analyzer.IsTouching) return;

            // Газ
            if (_analyzer.IsBoosting)
            {
                _playerVehicle.ApplyThrust(_vehicleConfig.BoostForce);
            }
            else if (!_analyzer.IsBraking && !_analyzer.IsPreloading)
            {
                _playerVehicle.ApplyThrust(_vehicleConfig.ThrustForce * _analyzer.Throttle);
            }

            // Руль — передаём нормализованный [-1..1] НАПРЯМУЮ
            _playerVehicle.ApplySteering(_analyzer.Steering);

            // Тормоз
            if (_analyzer.IsBraking)
                _playerVehicle.ApplyBrake(_vehicleConfig.BrakeForce);
        }

#if UNITY_EDITOR
        private void ReadKeyboard()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;

            float h = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f)
                    - (kb.aKey.isPressed || kb.leftArrowKey.isPressed  ? 1f : 0f);

            bool fwd = kb.wKey.isPressed || kb.upArrowKey.isPressed;
            bool brk = kb.sKey.isPressed || kb.downArrowKey.isPressed;
            bool any = Mathf.Abs(h) > 0.01f || fwd || brk;

            if (any)
            {
                float throttle = brk ? 0f : 1f;
                _analyzer.SetEditorInput(h, throttle, true, brk);
            }
            // Если клавиш нет — НЕ сбрасываем! Джойстик сам управляет IsTouching.
        }
#endif
    }
}
