using UnityEngine;
using System.Collections.Generic;
using Odal.Architecture;

namespace Odal.Core
{
    /// <summary>
    /// Централизованный менеджер обновлений, наследуемый от MonoBehaviour.
    /// Использует собственный единственный Update() и FixedUpdate() 
    /// для вызова Tick() и FixedTick() у подписанных объектов.
    /// </summary>
    public class UpdateManager : MonoBehaviour, IService
    {
        private readonly List<IUpdatable> _updatables = new List<IUpdatable>();
        private readonly List<IFixedUpdatable> _fixedUpdatables = new List<IFixedUpdatable>();

        public void RegisterUpdatable(IUpdatable updatable)
        {
            if (!_updatables.Contains(updatable))
                _updatables.Add(updatable);
        }

        public void UnregisterUpdatable(IUpdatable updatable)
        {
            if (_updatables.Contains(updatable))
                _updatables.Remove(updatable);
        }

        public void RegisterFixedUpdatable(IFixedUpdatable fixedUpdatable)
        {
            if (!_fixedUpdatables.Contains(fixedUpdatable))
                _fixedUpdatables.Add(fixedUpdatable);
        }

        public void UnregisterFixedUpdatable(IFixedUpdatable fixedUpdatable)
        {
            if (_fixedUpdatables.Contains(fixedUpdatable))
                _fixedUpdatables.Remove(fixedUpdatable);
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            for (int i = 0; i < _updatables.Count; i++)
            {
                _updatables[i].Tick(deltaTime);
            }
        }

        private void FixedUpdate()
        {
            float fixedDelta = Time.fixedDeltaTime;
            for (int i = 0; i < _fixedUpdatables.Count; i++)
            {
                _fixedUpdatables[i].FixedTick(fixedDelta);
            }
        }
    }
}
