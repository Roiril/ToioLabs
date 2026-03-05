using System;
using System.Collections.Generic;

namespace ToioLabs.Core
{
    /// <summary>
    /// Lightweight static Service Locator pattern to register and resolve global/scoped services.
    /// </summary>
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

        /// <summary>
        /// Registers a service instance.
        /// </summary>
        public static void Register<T>(T service)
        {
            var type = typeof(T);
            if (_services.ContainsKey(type))
            {
                AppLogger.LogWarning($"ServiceLocator: Service of type {type.Name} is already registered. Overwriting.");
                _services[type] = service;
            }
            else
            {
                _services.Add(type, service);
            }
        }

        /// <summary>
        /// Resolves and returns a registered service instance.
        /// </summary>
        public static T Resolve<T>()
        {
            var type = typeof(T);
            if (_services.TryGetValue(type, out var service))
            {
                return (T)service;
            }

            AppLogger.LogError($"ServiceLocator: Service of type {type.Name} not found.");
            return default;
        }

        /// <summary>
        /// Unregisters a service instance.
        /// </summary>
        public static void Unregister<T>()
        {
            var type = typeof(T);
            if (_services.ContainsKey(type))
            {
                _services.Remove(type);
            }
        }

        /// <summary>
        /// Clears all registered services. Useful for scene reloading or application quitting.
        /// </summary>
        public static void Clear()
        {
            _services.Clear();
        }
    }
}
