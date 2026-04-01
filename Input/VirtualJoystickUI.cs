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

        [Header("Settings")]
        [SerializeField] private float _knobRadius = 80f;
        [SerializeField] private bool  _hideWhenInactive = true;

        [Header("Цвета режимов")]
        [SerializeField] private Color _colorIdle     = new Color(1f, 1f, 1f, 0.3f);
        [SerializeField] private Color _colorGas      = new Color(0.2f, 0.9f, 0.3f, 0.6f);
        [SerializeField] private Color _colorBrake    = new Color(0.95f, 0.2f, 0.2f, 0.6f);
        [SerializeField] private Color _colorSteering = new Color(0.7f, 0.3f, 0.9f, 0.6f);

        private JoystickGestureAnalyzer _analyzer;
        private ServiceLocator          _locator;
        private Canvas                  _parentCanvas;
        private Camera                  _uiCamera;
        private RectTransform           _canvasRect;

        private bool    _isDragging;
        private Vector2 _startScreenPos;

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
                _isDragging     = true;
                _startScreenPos = pointerPos;
                PlaceJoystickAt(pointerPos);  // база И ручка в одну точку
                SetVisible(true);
                _analyzer.OnTouchBegan(pointerPos);
            }
            else if (isPressed && _isDragging)
            {
                UpdateKnobPosition(pointerPos);
                _analyzer.OnTouchMoved(_startScreenPos, pointerPos, deltaTime);
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

            if (_joystickKnob.parent == _joystickBase)
            {
                // Дочерний: считаем локальное смещение от базы
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _joystickBase, screenPos, _uiCamera, out Vector2 localPos);

                if (localPos.magnitude > _knobRadius)
                    localPos = localPos.normalized * _knobRadius;

                _joystickKnob.anchoredPosition = localPos;
            }
            else
            {
                // Сиблинг: считаем смещение от начальной позиции базы
                RectTransform parentRect = _joystickBase.parent as RectTransform;
                if (parentRect == null) return;

                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRect, screenPos, _uiCamera, out Vector2 localPos);

                Vector2 delta = localPos - _joystickBase.anchoredPosition;
                if (delta.magnitude > _knobRadius)
                    delta = delta.normalized * _knobRadius;

                _joystickKnob.anchoredPosition = _joystickBase.anchoredPosition + delta;
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

            if (_analyzer.IsBraking)
                target = _colorBrake;        // Красный — тормоз
            else if (Mathf.Abs(_analyzer.Steering) > 0.3f)
                target = _colorSteering;     // Лиловый — поворот
            else if (_analyzer.Throttle > 0.1f)
                target = _colorGas;          // Зелёный — газ
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
