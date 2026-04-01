using System;
using System.Collections.Generic;

namespace Odal.Architecture
{
    /// <summary>
    /// Локатор служб для управления зависимостями.
    /// Хранит словарь зарегистрированных систем. 
    /// Создается как обычный экземпляр (не синглтон) в Bootstrapper-е.
    /// </summary>
    public class ServiceLocator
    {
        private readonly Dictionary<Type, IService> _services;

        public ServiceLocator()
        {
            _services = new Dictionary<Type, IService>();
        }

        /// <summary>
        /// Регистрирует новый сервис.
        /// </summary>
        /// <typeparam name="T">Тип интерфейса сервиса (должен наследовать IService)</typeparam>
        /// <param name="service">Экземпляр сервиса</param>
        public void RegisterService<T>(T service) where T : IService
        {
            Type type = typeof(T);
            if (_services.ContainsKey(type))
            {
                throw new Exception($"Сервис типа {type} уже зарегистрирован!");
            }

            _services.Add(type, service);
        }

        /// <summary>
        /// Получает сервис по его типу.
        /// </summary>
        /// <typeparam name="T">Тип интерфейса сервиса</typeparam>
        /// <returns>Зарегистрированный сервис или ошибка, если не найден</returns>
        public T GetService<T>() where T : IService
        {
            Type type = typeof(T);
            if (!_services.TryGetValue(type, out IService service))
            {
                throw new Exception($"Сервис типа {type} не найден в ServiceLocator!");
            }

            return (T)service;
        }

        /// <summary>
        /// Удаляет регистрацию сервиса. Полезно при выгрузке сцен.
        /// </summary>
        public void UnregisterService<T>() where T : IService
        {
            Type type = typeof(T);
            if (_services.ContainsKey(type))
            {
                _services.Remove(type);
            }
        }
    }
}
