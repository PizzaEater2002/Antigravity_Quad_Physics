using UnityEngine;
using Odal.Architecture;

namespace Odal.Input
{
    /// <summary>
    /// Математическое ядро виртуального джойстика (Скрипт №6).
    /// Анализирует смещение пальца и выдаёт нормализованные команды управления.
    /// Чистый C#-сервис; не зависит от MonoBehaviour.
    /// </summary>
    public class JoystickGestureAnalyzer : IService
    {
        // ── Настройки (в пикселях экранного пространства) ──────────────

        /// <summary>Радиус мёртвой зоны (пикс). Внутри — автогаз, руль по центру.</summary>
        public float DeadZoneRadius { get; set; } = 30f;

        /// <summary>Максимальный радиус отклонения (пикс). Дальше — насыщение.</summary>
        public float MaxRadius { get; set; } = 150f;

        /// <summary>
        /// Порог скорости свайпа (пикс/сек).
        /// Быстрее → Boost (вверх) или Preload (вниз).
        /// Медленнее → нормаль или Тормоз (вниз).
        /// </summary>
        public float SwipeVelocityThreshold { get; set; } = 800f;

        /// <summary>Минимальный нормализованный Y-компонент для классификации свайпа ВВЕРХ.</summary>
        public float UpSwipeThreshold { get; set; } = 0.55f;

        /// <summary>Минимальный нормализованный |Y|-компонент для классификации свайпа ВНИЗ.</summary>
        public float DownSwipeThreshold { get; set; } = 0.45f;

        // ── Выходное состояние (читается SpherePhysicsCore в FixedTick) ─

        /// <summary>Нормализованная тяга [0..1].</summary>
        public float Throttle { get; private set; }

        /// <summary>Нормализованное руление [-1..1]. Отрицательное = влево.</summary>
        public float Steering { get; private set; }

        /// <summary>Активен режим торможения (медленный свайп вниз).</summary>
        public bool IsBraking { get; private set; }

        /// <summary>Активен режим Preload — сжатие перед прыжком (резкий свайп вниз).</summary>
        public bool IsPreloading { get; private set; }

        /// <summary>Активен режим Boost (резкий свайп вверх).</summary>
        public bool IsBoosting { get; private set; }

        /// <summary>True, пока есть активное касание экрана.</summary>
        public bool IsTouching { get; private set; }

        // ── Внутреннее состояние ────────────────────────────────────────

        private Vector2 _previousPosition;
        private float   _instantSwipeVelocity; // px/sec

        // ═══════════════════════════════════════════════════════════════
        //  Публичный API — вызывается InputTouchHandler
        // ═══════════════════════════════════════════════════════════════

        public void OnTouchBegan(Vector2 startPosition)
        {
            IsTouching        = true;
            _previousPosition = startPosition;
            ResetState();
        }

        /// <summary>
        /// Основная точка анализа. Вызывается каждый кадр пока палец на экране.
        /// </summary>
        /// <param name="startPos">Позиция начального касания.</param>
        /// <param name="currentPos">Текущая позиция пальца.</param>
        /// <param name="deltaTime">Время кадра для расчёта V_swipe = ΔDistance / ΔTime.</param>
        public void OnTouchMoved(Vector2 startPos, Vector2 currentPos, float deltaTime)
        {
            // ── V_swipe = ΔDistance / ΔTime ─────────────────────────────
            float frameDelta         = (currentPos - _previousPosition).magnitude;
            _instantSwipeVelocity    = deltaTime > 0.0001f ? frameDelta / deltaTime : 0f;
            _previousPosition        = currentPos;

            Vector2 delta    = currentPos - startPos;
            float   distance = delta.magnitude;

            // ── Мёртвая зона: автогаз, руль по центру ───────────────────
            if (distance < DeadZoneRadius)
            {
                Throttle     = 1f;
                Steering     = 0f;
                IsBraking    = false;
                IsPreloading = false;
                IsBoosting   = false;
                return;
            }

            // Нормализуем с насыщением на MaxRadius → компоненты в [-1..1]
            float   clampedDist = Mathf.Min(distance, MaxRadius);
            Vector2 normalized  = delta / clampedDist;

            float nx = Mathf.Clamp(normalized.x, -1f, 1f); // руление
            float ny = Mathf.Clamp(normalized.y, -1f, 1f); // вертикаль

            // ── BOOST: резкий свайп ВВЕРХ ────────────────────────────────
            if (ny > UpSwipeThreshold && _instantSwipeVelocity > SwipeVelocityThreshold)
            {
                IsBoosting   = true;
                IsPreloading = false;
                IsBraking    = false;
                Throttle     = 1f;
                Steering     = nx;
                return;
            }

            // ── Свайп ВНИЗ: дифференциация Тормоз / Preload ─────────────
            if (ny < -DownSwipeThreshold)
            {
                Throttle   = 0f;
                Steering   = nx;
                IsBoosting = false;

                if (_instantSwipeVelocity > SwipeVelocityThreshold)
                {
                    // Резкий → Preload (подготовка к прыжку)
                    IsPreloading = true;
                    IsBraking    = false;
                }
                else
                {
                    // Медленный → Тормоз
                    IsBraking    = true;
                    IsPreloading = false;
                }
                return;
            }

            // ── Обычное движение: газ + руление ─────────────────────────
            // ny: [-1..1] → throttle [0..1] (при ny=0 → 0.5, ny=+1 → 1.0)
            IsBraking    = false;
            IsPreloading = false;
            IsBoosting   = false;
            Throttle     = Mathf.Clamp01((ny + 1f) * 0.5f);
            Steering     = nx;
        }

        public void OnTouchEnded()
        {
            IsTouching = false;
            ResetState();
        }

        /// <summary>
        /// Заполняет состояние напрямую — для клавиатурного тестирования в редакторе.
        /// Минует математику свайпа (V_swipe), которая не нужна при вводе с клавиатуры.
        /// </summary>
        public void SetEditorInput(float steering, float throttle, bool isTouching, bool isBraking = false)
        {
            IsTouching   = isTouching;
            Steering     = Mathf.Clamp(steering, -1f, 1f);
            Throttle     = Mathf.Clamp01(throttle);
            IsBraking    = isBraking;
            IsPreloading = false;
            IsBoosting   = false;
        }

        // ───────────────────────────────────────────────────────────────

        private void ResetState()
        {
            Throttle              = 0f;
            Steering              = 0f;
            IsBraking             = false;
            IsPreloading          = false;
            IsBoosting            = false;
            _instantSwipeVelocity = 0f;
        }
    }
}
