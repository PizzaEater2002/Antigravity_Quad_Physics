using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using Odal.Architecture;
using Odal.Core;

namespace Odal.Track
{
    /// <summary>
    /// Адаптер (Скрипт №16), связывающий старый RoadGenerator / SplineContainer 
    /// с новой архитектурой. Реализует ISplineProvider.
    /// </summary>
    public class SplinePathAdapter : MonoBehaviour, ISplineProvider, IUpdatable
    {
        [Header("References")]
        [Tooltip("Контейнер сплайнов, на основе которого строится трасса")]
        [SerializeField] private SplineContainer _splineContainer;
        [Tooltip("Генератор дороги для получения ширины трассы")]
        [SerializeField] private RoadGenerator _roadGenerator;

        private ServiceLocator _locator;

        public float TrackWidth => _roadGenerator != null ? _roadGenerator.roadWidth : 10f;

        public void Init(ServiceLocator locator)
        {
            if (_roadGenerator == null) _roadGenerator = GetComponent<RoadGenerator>();

            _locator = locator;
            _locator.RegisterService<ISplineProvider>(this);
            
            locator.GetService<UpdateManager>().RegisterUpdatable(this);
        }

        public void Tick(float deltaTime)
        {
            // Место для отслеживания состояния сплайна в рантайме, если потребуется
        }

        private void OnDestroy()
        {
            if (_locator != null)
            {
                _locator.UnregisterService<ISplineProvider>();
                _locator.GetService<UpdateManager>()?.UnregisterUpdatable(this);
            }
        }

        public void GetNearestPoint(Vector3 position, out Vector3 nearestPoint, out Vector3 tangent, out Vector3 upNormal)
        {
            if (_splineContainer == null || _splineContainer.Splines.Count == 0)
            {
                nearestPoint = position;
                tangent = Vector3.forward;
                upNormal = Vector3.up;
                return;
            }

            SplineUtility.GetNearestPoint(
                _splineContainer.Spline, 
                _splineContainer.transform.InverseTransformPoint(position), 
                out float3 localNearest, 
                out float t);

            _splineContainer.Evaluate(t, out float3 pos, out float3 tn, out float3 up);

            nearestPoint = _splineContainer.transform.TransformPoint((Vector3)pos);
            tangent = _splineContainer.transform.TransformDirection((Vector3)tn).normalized;
            upNormal = _splineContainer.transform.TransformDirection((Vector3)up).normalized;
        }
    }
}
