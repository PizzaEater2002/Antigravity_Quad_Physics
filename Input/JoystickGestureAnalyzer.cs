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

        // ── Thresholds ──────────────────────────────────────────────────
        // (Оставлены для совместимости, но логика кнопок теперь в UI)
        public float UpSwipeThreshold { get; set; } = 0.55f;
        public float DownSwipeThreshold { get; set; } = 0.45f;
        public float _boostThreshold = 0.85f;
        public float _brakeZoneStart = -0.4f;
        public float _preloadThreshold = -0.85f;

        // ── Выходное состояние (читается SpherePhysicsCore в FixedTick) ─

        /// <summary>Нормализованная тяга [0..1].</summary>
        public float Throttle { get; private set; }

        /// <summary>Нормализованное руление [-1..1]. Отрицательное = влево.</summary>
        public float Steering { get; private set; }

        /// <summary>Активен режим торможения (множитель > 0).</summary>
        public bool IsBraking => BrakeMultiplier > 0f;

        /// <summary>Множитель силы торможения для мульти-тапов.</summary>
        public float BrakeMultiplier { get; private set; }

        /// <summary>Активен режим Preload — сжатие перед прыжком (резкий свайп вниз).</summary>
        public bool IsPreloading { get; private set; }

        /// <summary>Активен режим Boost (резкий свайп вверх).</summary>
        public bool IsBoosting { get; private set; }

        /// <summary>True, пока есть активное касание экрана.</summary>
        public bool IsTouching { get; private set; }

        // ── Внутреннее состояние ────────────────────────────────────────

        // ═══════════════════════════════════════════════════════════════
        //  Публичный API — вызывается InputTouchHandler / VirtualJoystickUI
        // ═══════════════════════════════════════════════════════════════

        public void SetBoostActive(bool active)   => IsBoosting = active;
        public void SetPreloadActive(bool active) => IsPreloading = active;
        public void SetBrakeMultiplier(float mult)=> BrakeMultiplier = mult;

        /// <summary>Прямая передача нормализованных осей (для наэкранного джойстика) [-1..1].</summary>
        public void SetVirtualAxis(Vector2 normalizedAxis)
        {
            float nx = Mathf.Clamp(normalizedAxis.x, -1f, 1f);
            float ny = Mathf.Clamp(normalizedAxis.y, -1f, 1f);

            if (IsBoosting) Throttle = 1.0f;
            else if (IsBraking || IsPreloading) Throttle = 0f;
            else Throttle = Mathf.Clamp01((ny + 1f) * 0.5f);

            Steering = IsPreloading ? nx * 0.3f : nx;
            IsTouching = true;
        }

        public void OnTouchBegan(Vector2 startPosition)
        {
            IsTouching = true;
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
            Vector2 delta    = currentPos - startPos;
            float   distance = delta.magnitude;

            // ── Dead Zone ───────────────────
            if (distance < DeadZoneRadius)
            {
                if (IsBoosting) Throttle = 1f;
                else if (IsBraking || IsPreloading) Throttle = 0f;
                else Throttle = 1f; // автогаз в центре
                
                Steering = 0f;
                return;
            }

            // Normalize and clamp
            float   clampedDist = Mathf.Min(distance, MaxRadius);
            Vector2 normalized  = delta / clampedDist;

            float nx = Mathf.Clamp(normalized.x, -1f, 1f);
            float ny = Mathf.Clamp(normalized.y, -1f, 1f);

            // UI Button overrides take precedence for Throttle
            if (IsBoosting) 
                Throttle = 1.0f;
            else if (IsBraking || IsPreloading) 
                Throttle = 0f;
            else 
                Throttle = Mathf.Clamp01((ny + 1f) * 0.5f);

            // Steering Logic
            Steering = IsPreloading ? nx * 0.3f : nx;
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
            BrakeMultiplier = isBraking ? 1f : 0f;
            IsPreloading = false;
            IsBoosting   = false;
        }

        // ───────────────────────────────────────────────────────────────

        private void ResetState()
        {
            Throttle              = 0f;
            Steering              = 0f;
            BrakeMultiplier       = 0f;
            IsPreloading          = false;
            IsBoosting            = false;
        }
    }
}
