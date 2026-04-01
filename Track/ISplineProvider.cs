using UnityEngine;
using Odal.Architecture;

namespace Odal.Track
{
    /// <summary>
    /// Интерфейс для получения информации о трассе/сплайне.
    /// Наследует IService, чтобы его можно было зарегистрировать в ServiceLocator.
    /// </summary>
    public interface ISplineProvider : IService
    {
        /// <summary>
        /// Возвращает ближайшую точку на сплайне, тангенс (направление вперед) и нормаль (вектор вверх).
        /// </summary>
        void GetNearestPoint(Vector3 position, out Vector3 nearestPoint, out Vector3 tangent, out Vector3 upNormal);
    }
}
