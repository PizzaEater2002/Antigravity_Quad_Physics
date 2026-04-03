using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Odal.Architecture;
using Odal.Core;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Odal.Input
{
    /// <summary>
    /// Низкоуровневый адаптер сенсорного ввода (Скрипт №5).
    /// Включает EnhancedTouchSupport и транслирует фазы касания
    /// (Began / Moved / Stationary / Ended) в вызовы JoystickGestureAnalyzer.
    /// Работает как IService + IUpdatable через UpdateManager (не использует собственный Update).
    /// </summary>
    public class InputTouchHandler : MonoBehaviour, IService, IUpdatable
    {
        private JoystickGestureAnalyzer _analyzer;
        private ServiceLocator          _locator;

        private bool    _isTouching;
        private Vector2 _touchStartPosition;

        // ═══════════════════════════════════════════════════════════════

        public void Init(ServiceLocator locator, JoystickGestureAnalyzer analyzer)
        {
            _locator  = locator;
            _analyzer = analyzer;

            // Включаем EnhancedTouch — обязательно до первого Tick
            EnhancedTouchSupport.Enable();

            locator.RegisterService<InputTouchHandler>(this);
            locator.GetService<UpdateManager>().RegisterUpdatable(this);

            Debug.Log("<b>InputTouchHandler</b>: Initialized, EnhancedTouch enabled.");
        }

        private void OnDestroy()
        {
            if (_locator != null)
            {
                _locator.GetService<UpdateManager>()?.UnregisterUpdatable(this);
                _locator.UnregisterService<InputTouchHandler>();
            }

            if (EnhancedTouchSupport.enabled)
                EnhancedTouchSupport.Disable();
        }

        // ═══════════════════════════════════════════════════════════════
        //  IUpdatable — вызывается UpdateManager каждый кадр
        // ═══════════════════════════════════════════════════════════════

        public void Tick(float deltaTime)
        {
            var activeTouches = Touch.activeTouches;

            // Нет касаний — сбрасываем состояние если было
            if (activeTouches.Count == 0)
            {
                if (_isTouching)
                {
                    _isTouching = false;
                    _analyzer.OnTouchEnded();
                }
                return;
            }

            // Работаем только с первым (основным) касанием
            Touch   primaryTouch = activeTouches[0];
            Vector2 currentPos   = primaryTouch.screenPosition;

            switch (primaryTouch.phase)
            {
                case UnityEngine.InputSystem.TouchPhase.Began:
                    _isTouching         = true;
                    _touchStartPosition = currentPos;
                    _analyzer.OnTouchBegan(currentPos);
                    break;

                case UnityEngine.InputSystem.TouchPhase.Moved:
                case UnityEngine.InputSystem.TouchPhase.Stationary:
                    if (_isTouching)
                        _analyzer.OnTouchMoved(_touchStartPosition, currentPos, deltaTime);
                    break;

                case UnityEngine.InputSystem.TouchPhase.Ended:
                case UnityEngine.InputSystem.TouchPhase.Canceled:
                    if (_isTouching)
                    {
                        _isTouching = false;
                        _analyzer.OnTouchEnded();
                    }
                    break;
            }
        }
    }
}
