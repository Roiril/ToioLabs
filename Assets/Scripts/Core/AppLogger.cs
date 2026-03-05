using System.Diagnostics;

namespace ToioLabs.Core
{
    /// <summary>
    /// Centralized AppLogger that supports conditional compilation to strip logs from production builds.
    /// Define 'ENABLE_LOGS' in Player Settings -> Scripting Define Symbols to enable logs.
    /// Or use Unity's default UNITY_EDITOR to always log in Editor.
    /// </summary>
    public static class AppLogger
    {
        [Conditional("ENABLE_LOGS")]
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Log(object message)
        {
            UnityEngine.Debug.Log(message);
        }

        [Conditional("ENABLE_LOGS")]
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void LogWarning(object message)
        {
            UnityEngine.Debug.LogWarning(message);
        }

        [Conditional("ENABLE_LOGS")]
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void LogError(object message)
        {
            UnityEngine.Debug.LogError(message);
        }

        [Conditional("ENABLE_LOGS")]
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void LogFormat(string format, params object[] args)
        {
            UnityEngine.Debug.LogFormat(format, args);
        }
    }
}
