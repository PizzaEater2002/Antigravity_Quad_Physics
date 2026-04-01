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
    /// 
    /// ● Каждые 0.5с сохраняет SafePoint если игрок на земле.
    /// ● Если Y позиция ниже порога — респаун в последнюю SafePoint.
    /// ● Выравнивает сферу по тангенсу сплайна после респауна.
    /// ● Событие OnRespawn — для привязки UI эффектов (Fade-to-Black и т.д.).
    /// </summary>
    public class RespawnManager : MonoBehaviour, IService, IUpdatable
    {
        [Header("Settings")]
        [Tooltip("Насколько низко относительно трассы можно упасть (глубина падения), прежде чем сработает респаун.")]
        [SerializeField] private float _fallDepthThreshold = 5f;

        [Tooltip("Интервал сохранения SafePoint (секунды).")]
        [SerializeField] private float _safePointInterval = 0.5f;

        [Header("Track Raycasting")]
        [Tooltip("Слой физической дороги. Нужен чтобы найти точную высоту поверхности под сплайном.")]
        [SerializeField] private LayerMask _trackLayer = ~0;

        [Tooltip("Высота, с которой пускаем луч вниз, чтобы нащупать дорогу.")]
        [SerializeField] private float _raycastHeightOffset = 50f;

        // ── Событие ──────────────────────────────────────────────────
        /// <summary>Вызывается при респауне. Подпишись для Fade-to-Black UI.</summary>
        public event Action OnRespawn;

        // ── Зависимости ──────────────────────────────────────────────
        private ServiceLocator    _locator;
        private SpherePhysicsCore _player;
        private ISplineProvider   _spline;

        // ── Состояние ────────────────────────────────────────────────
        private Vector3    _lastSafePosition;
        private Quaternion _lastSafeRotation;

        private float _safePointTimer;
        private bool  _hasSafePoint;

        private float _fallCheckTimer;
        private float _currentTrackHeight;
        private float _respawnCooldownTimer;

        // ═══════════════════════════════════════════════════════════════
        //  Инициализация
        // ═══════════════════════════════════════════════════════════════

        public void Init(ServiceLocator locator, SpherePhysicsCore player)
        {
            _locator = locator;
            _player  = player;

            // Сплайн — опционален
            try   { _spline = locator.GetService<ISplineProvider>(); }
            catch { /* без сплайна — ОК, выравнивание по forward */ }

            // Первоначальный SafePoint = стартовая позиция игрока
            _lastSafePosition = _player.transform.position;
            _lastSafeRotation = _player.transform.rotation;
            _hasSafePoint     = true;

            locator.RegisterService<RespawnManager>(this);
            locator.GetService<UpdateManager>().RegisterUpdatable(this);

            _currentTrackHeight = _player.transform.position.y;
            Debug.Log($"<b>RespawnManager</b>: Init OK. FallDepthThreshold={_fallDepthThreshold}");
        }

        private void OnDestroy()
        {
            if (_locator == null) return;
            _locator.GetService<UpdateManager>()?.UnregisterUpdatable(this);
            _locator.UnregisterService<RespawnManager>();
        }

        // ═══════════════════════════════════════════════════════════════
        //  IUpdatable
        // ═══════════════════════════════════════════════════════════════

        public void Tick(float deltaTime)
        {
            if (_player == null) return;

            // 1. Обновление SafePoint
            _safePointTimer += deltaTime;
            if (_safePointTimer >= _safePointInterval)
            {
                _safePointTimer = 0f;
                UpdateSafePoint();
            }

            // 2. Детекция падения (проверяем каждые 0.1 сек ради оптимизации)
            if (_respawnCooldownTimer > 0f)
            {
                _respawnCooldownTimer -= deltaTime;
            }
            else
            {
                _fallCheckTimer += deltaTime;
                if (_fallCheckTimer >= 0.1f)
                {
                    _fallCheckTimer = 0f;
                    UpdateTrackHeightAndCheckFall();
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  Логика
        // ═══════════════════════════════════════════════════════════════

        private void UpdateTrackHeightAndCheckFall()
        {
            if (_spline != null)
            {
                // Находим ближайшую точку на сплайне к текущей позиции байка
                _spline.GetNearestPoint(_player.transform.position,
                    out Vector3 nearestPoint, out _, out _);

                _currentTrackHeight = nearestPoint.y;

                // Уточняем высоту по физической поверхности, как при респауне
                Vector3 rayStart = nearestPoint + Vector3.up * _raycastHeightOffset;
                if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, _raycastHeightOffset * 2f, _trackLayer))
                {
                    _currentTrackHeight = hit.point.y;
                }
            }
            else
            {
                // Если сплайна почему-то нет, отталкиваемся от последней безопасной точки
                _currentTrackHeight = _lastSafePosition.y;
            }

            // Респавним, если упали ниже текущей высоты трассы минус порог
            if (_player.transform.position.y < _currentTrackHeight - _fallDepthThreshold)
            {
                DoRespawn();
            }
        }

        /// <summary>
        /// Сохраняет текущую позицию как безопасную,
        /// только если игрок на земле (подвеска видит поверхность).
        /// </summary>
        private void UpdateSafePoint()
        {
            // Проверяем через подвеску: если AverageNormal ≠ Vector3.up (слерпнутый),
            // значит хотя бы один луч попал в землю
            var suspension = _player.Suspension;
            if (suspension == null) return;

            Vector3 normal = suspension.AverageNormal;

            // В воздухе AverageNormal постепенно слерпится к Vector3.up,
            // но на земле он всегда отличается от чистого (0,1,0) из-за рельефа
            // Надёжнее: проверяем положение + скорость Y
            Rigidbody rb = _player.VehicleRigidbody;
            if (rb == null) return;

            bool isGrounded = normal.sqrMagnitude > 0.9f
                           && Mathf.Abs(rb.linearVelocity.y) < 3f;

            if (isGrounded)
            {
                _lastSafePosition = _player.transform.position;
                _lastSafeRotation = _player.transform.rotation;
                _hasSafePoint     = true;
            }
        }

        /// <summary>
        /// Респаун: обнуление скоростей, телепорт в SafePoint, выравнивание по сплайну.
        /// </summary>
        public void DoRespawn()
        {
            if (_player == null || !_hasSafePoint) return;

            Rigidbody rb = _player.VehicleRigidbody;
            if (rb != null)
            {
                rb.linearVelocity  = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            _respawnCooldownTimer = 1.5f; // Блокировка повторных срабатываний

            // Телепорт и выравнивание
            if (_spline != null)
            {
                // Ищем точку на сплайне (соответствующую центру трассы)
                _spline.GetNearestPoint(_lastSafePosition,
                    out Vector3 nearestPoint, out Vector3 tangent, out Vector3 upNormal);

                // Пускаем луч вниз, чтобы найти РЕАЛЬНУЮ физическую поверхность (меш) под сплайном
                Vector3 rayStart = nearestPoint + Vector3.up * _raycastHeightOffset;
                if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, _raycastHeightOffset * 2f, _trackLayer))
                {
                    nearestPoint = hit.point;
                    // Игнорируем hit.normal, так как луч может попасть в кривой бордюр/ограждение
                }

                // Ставим прямо над найденной поверхностью, чтобы 100% не провалиться сквозь меш
                _player.transform.position = nearestPoint + Vector3.up * 1f;

                // Выравнивание: тангенс сплайна проецируем на строго горизонтальную плоскость
                Vector3 projectedTangent = Vector3.ProjectOnPlane(tangent, Vector3.up);
                if (projectedTangent.sqrMagnitude > 0.001f)
                    _player.transform.rotation = Quaternion.LookRotation(projectedTangent.normalized, Vector3.up);
                else
                    _player.transform.rotation = _lastSafeRotation;
            }
            else
            {
                // Если сплайна нет — старый добрый респаун на месте падения
                _player.transform.position = _lastSafePosition + Vector3.up * 0.5f;
                _player.transform.rotation = _lastSafeRotation;
            }

            Debug.Log("<b>RespawnManager</b>: Респаун выполнен!");

            // Событие для UI (Fade-to-Black и т.д.)
            OnRespawn?.Invoke();
        }
    }
}
