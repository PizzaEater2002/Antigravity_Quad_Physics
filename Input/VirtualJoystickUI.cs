using UnityEngine;
using UnityEngine.UI;
using Odal.Architecture;
using Odal.Core;

namespace Odal.Input
{
    /// <summary>
    /// Экранный виртуальный джойстик (Скрипт №7).
    /// Мышь + тач. Цветовая индикация режимов.
    /// </summary>
    public class VirtualJoystickUI : MonoBehaviour, IService, IUpdatable
    {
        [Header("UI Elements")]
        [SerializeField] private RectTransform _joystickBase;
        [SerializeField] private RectTransform _joystickKnob;

        [Header("Images (для смены цвета)")]
        [SerializeField] private Image _baseImage;
        [SerializeField] private Image _knobImage;
        [SerializeField] private Image _boostDot;
        [SerializeField] private Image _preloadDot;

        [Header("Settings")]
        [SerializeField] private float _knobRadius = 80f;
        [SerializeField] private bool  _hideWhenInactive = true;
        [SerializeField] private float _minBottomDistanceCM = 2f;

        [Header("Magnetic Buttons Radius")]
        [SerializeField] private float _buttonSnapRadius = 35f;

        [Header("Цвета режимов")]
        [SerializeField] private Color _colorIdle     = new Color(0.2f, 0.9f, 0.2f, 0.6f); // Green (ГАЗ)
        [SerializeField] private Color _colorBoost    = new Color(0.2f, 0.6f, 1f, 0.8f); // Blue
        [SerializeField] private Color _colorPreload  = new Color(1f, 0.5f, 0f, 0.8f); // Orange
        [SerializeField] private Color _colorBrake    = new Color(0.95f, 0.2f, 0.2f, 0.6f); // Red
        [SerializeField] private Color _colorSteering = new Color(0.7f, 0.3f, 0.9f, 0.6f); // Lilac

        private JoystickGestureAnalyzer _analyzer;
        private ServiceLocator          _locator;
        private Canvas                  _parentCanvas;
        private Camera                  _uiCamera;
        private RectTransform           _canvasRect;

        private bool    _isDragging;
        private Vector2 _startScreenPos;

        private float   _lastTapTime;
        private int     _tapCombo;
        private float   _tapWindow = 0.35f;
        private float   _currentBrakeMult;

        // ═══════════════════════════════════════════════════════════════

        public void Init(ServiceLocator locator, JoystickGestureAnalyzer analyzer)
        {
            if (!UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.enabled)
                UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Enable();

            _locator  = locator;
            _analyzer = analyzer;

            _parentCanvas = GetComponentInParent<Canvas>();
            if (_parentCanvas != null)
            {
                _canvasRect = _parentCanvas.GetComponent<RectTransform>();
                if (_parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    _uiCamera = _parentCanvas.worldCamera;
            }

            // Авто-поиск Image если не назначены
            if (_baseImage == null && _joystickBase != null)
                _baseImage = _joystickBase.GetComponent<Image>();
            if (_knobImage == null && _joystickKnob != null)
                _knobImage = _joystickKnob.GetComponent<Image>();

            // Отключаем видимость дотов, оставляя их объекты активными для математики
            if (_boostDot != null) _boostDot.enabled = false;
            if (_preloadDot != null) _preloadDot.enabled = false;

            locator.RegisterService<VirtualJoystickUI>(this);
            locator.GetService<UpdateManager>().RegisterUpdatable(this);

            if (_hideWhenInactive) SetVisible(false);
            Debug.Log("<b>VirtualJoystickUI</b>: Init OK.");
        }

        private void OnDestroy()
        {
            if (_locator == null) return;
            _locator.GetService<UpdateManager>()?.UnregisterUpdatable(this);
            _locator.UnregisterService<VirtualJoystickUI>();
        }

        // ═══════════════════════════════════════════════════════════════

        public void Tick(float deltaTime)
        {
            bool    isPressed;
            bool    justPressed;
            bool    justReleased;
            Vector2 pointerPos;

            if (HasTouch(out var touchPos, out var touchPhase))
            {
                isPressed    = true;
                justPressed  = touchPhase == UnityEngine.InputSystem.TouchPhase.Began;
                justReleased = touchPhase == UnityEngine.InputSystem.TouchPhase.Ended
                            || touchPhase == UnityEngine.InputSystem.TouchPhase.Canceled;
                pointerPos   = touchPos;
            }
            else
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse == null) { HandleRelease(); return; }

                isPressed    = mouse.leftButton.isPressed;
                justPressed  = mouse.leftButton.wasPressedThisFrame;
                justReleased = mouse.leftButton.wasReleasedThisFrame;
                pointerPos   = mouse.position.ReadValue();
            }

            if (justPressed && !_isDragging)
            {
                float dpi = Screen.dpi <= 0 ? 160f : Screen.dpi;
                float minBottomPx = (_minBottomDistanceCM / 2.54f) * dpi;

                // Ignore the initial touch entirely if the user tapped inside the forbidden bottom zone
                if (pointerPos.y < minBottomPx) 
                    return;

                if (Time.unscaledTime - _lastTapTime <= _tapWindow)
                {
                    _tapCombo++; // Увеличиваем счетчик при быстром следующем нажатии
                }
                else
                {
                    _tapCombo = 1; // Сброс серии
                }
                _lastTapTime = Time.unscaledTime;

                // Если это 2+ тап с удержанием, активируем тормоз (без прогрессии)
                _currentBrakeMult = (_tapCombo > 1) ? 1f : 0f;

                _isDragging     = true;
                _startScreenPos = pointerPos;
                PlaceJoystickAt(pointerPos);  // база И ручка в одну точку
                SetVisible(true);
                _analyzer.OnTouchBegan(pointerPos);
            }
            else if (isPressed && _isDragging)
            {
                UpdateKnobPosition(pointerPos);
                UpdateColors();
            }
            else if ((justReleased || !isPressed) && _isDragging)
            {
                HandleRelease();
            }
        }

        private void HandleRelease()
        {
            if (!_isDragging) return;
            _isDragging = false;
            
            _currentBrakeMult = 0f;
            _analyzer.SetBrakeMultiplier(0f);
            
            _analyzer.OnTouchEnded();
            ResetKnob();
            SetColors(_colorIdle);
            if (_hideWhenInactive) SetVisible(false);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Позиционирование
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Размещает И базу И ручку в точку нажатия.
        /// </summary>
        private void PlaceJoystickAt(Vector2 screenPos)
        {
            // Конвертируем экранную точку в локальные координаты Canvas
            RectTransform parentRect = (_joystickBase != null)
                ? _joystickBase.parent as RectTransform
                : _canvasRect;

            if (parentRect == null) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect, screenPos, _uiCamera, out Vector2 localPos);

            // База — в точку нажатия
            if (_joystickBase != null)
                _joystickBase.anchoredPosition = localPos;

            // Position and Activate external buttons
            if (_boostDot != null)
            {
                _boostDot.gameObject.SetActive(true);
                _boostDot.rectTransform.anchoredPosition = new Vector2(0f, _knobRadius); // above
            }
            if (_preloadDot != null)
            {
                _preloadDot.gameObject.SetActive(true);
                _preloadDot.rectTransform.anchoredPosition = new Vector2(0f, -_knobRadius); // below
            }

            // Ручка — тоже в центр базы (не в центр канваса!)
            if (_joystickKnob != null)
            {
                if (_joystickKnob.parent == _joystickBase)
                {
                    // Дочерний — просто обнуляем
                    _joystickKnob.anchoredPosition = Vector2.zero;
                }
                else
                {
                    // Сиблинг — ставим в ту же мировую точку что и базу
                    _joystickKnob.anchoredPosition = localPos;
                }
            }
        }

        private void UpdateKnobPosition(Vector2 screenPos)
        {
            if (_joystickBase == null || _joystickKnob == null) return;

            Vector2 newKnobPos = Vector2.zero;

            // 1. Считаем сырое локальное смещение
            if (_joystickKnob.parent == _joystickBase)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _joystickBase, screenPos, _uiCamera, out Vector2 localPos);

                if (localPos.magnitude > _knobRadius)
                    localPos = localPos.normalized * _knobRadius;

                newKnobPos = localPos;
            }
            else
            {
                RectTransform parentRect = _joystickBase.parent as RectTransform;
                if (parentRect != null)
                {
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        parentRect, screenPos, _uiCamera, out Vector2 localPos);

                    Vector2 delta = localPos - _joystickBase.anchoredPosition;
                    if (delta.magnitude > _knobRadius)
                        delta = delta.normalized * _knobRadius;

                    newKnobPos = delta; // We track relative delta for distance checks
                }
            }

            // 2. Button Constraints & Analyzer State Sync
            bool isBoosting = false;
            bool isPreloading = false;

            // Буст теперь срабатывает просто по высоте (верхние 30% радиуса), без магнита по Х,
            // чтобы можно было подруливать влево/вправо удерживая верхнюю позицию
            if (newKnobPos.y >= _knobRadius * 0.7f)
            {
                isBoosting = true;
            }
            else if (_preloadDot != null && Vector2.Distance(newKnobPos, _preloadDot.rectTransform.anchoredPosition) <= _buttonSnapRadius)
            {
                newKnobPos = _preloadDot.rectTransform.anchoredPosition; // Magnetic Snap for Preload Only!
                isPreloading = true;
            }

            // Set states on analyzer
            _analyzer.SetBoostActive(isBoosting);
            _analyzer.SetPreloadActive(isPreloading);
            _analyzer.SetBrakeMultiplier(_currentBrakeMult);

            // Send normalized analog offset for perfect progressive steering [-1..1]
            _analyzer.SetVirtualAxis(newKnobPos / _knobRadius);

            // 3. Apply position
            if (_joystickKnob.parent == _joystickBase)
            {
                _joystickKnob.anchoredPosition = newKnobPos;
            }
            else
            {
                _joystickKnob.anchoredPosition = _joystickBase.anchoredPosition + newKnobPos;
            }
        }

        private void ResetKnob()
        {
            if (_joystickKnob == null) return;
            if (_joystickKnob.parent == _joystickBase)
                _joystickKnob.anchoredPosition = Vector2.zero;
            else if (_joystickBase != null)
                _joystickKnob.anchoredPosition = _joystickBase.anchoredPosition;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Цвета
        // ═══════════════════════════════════════════════════════════════

        private void UpdateColors()
        {
            if (_analyzer == null) return;

            Color target;

            if (_analyzer.IsPreloading)
                target = _colorPreload;      // Orange — Preload
            else if (_analyzer.IsBoosting)
                target = _colorBoost;        // Blue — Boost
            else if (_analyzer.IsBraking)
                target = _colorBrake;        // Red — Тормоз
            else if (Mathf.Abs(_analyzer.Steering) > 0.3f)
                target = _colorSteering;     // Lilac/Purple — Поворот
            else
                target = _colorIdle;

            SetColors(target);
        }

        private void SetColors(Color color)
        {
            if (_knobImage != null) _knobImage.color = color;
            // Базе — тот же цвет но ещё прозрачнее
            if (_baseImage != null)
            {
                Color baseColor = color;
                baseColor.a *= 0.4f;
                _baseImage.color = baseColor;
            }
        }

        // ═══════════════════════════════════════════════════════════════

        private void SetVisible(bool visible)
        {
            if (_joystickBase != null) _joystickBase.gameObject.SetActive(visible);
            if (_joystickKnob != null && _joystickKnob.parent != _joystickBase)
                _joystickKnob.gameObject.SetActive(visible);

            if (_boostDot != null) _boostDot.gameObject.SetActive(visible);
            if (_preloadDot != null) _preloadDot.gameObject.SetActive(visible);
        }

        private bool HasTouch(out Vector2 position, out UnityEngine.InputSystem.TouchPhase phase)
        {
            var touches = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches;
            if (touches.Count > 0)
            {
                position = touches[0].screenPosition;
                phase    = touches[0].phase;
                return true;
            }
            position = Vector2.zero;
            phase    = default;
            return false;
        }
    }
}
