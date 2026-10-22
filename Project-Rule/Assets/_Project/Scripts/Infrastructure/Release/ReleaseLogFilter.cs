using System;
using UnityEngine;

namespace RuleGame.Infrastructure.Release
{
    /// <summary>发布版只保留错误与异常，屏蔽调试期的 Log/Warning。</summary>
    internal static class ReleaseLogFilter
    {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            Debug.unityLogger.logHandler = new ErrorsOnlyLogHandler(Debug.unityLogger.logHandler);
        }

        private sealed class ErrorsOnlyLogHandler : ILogHandler
        {
            private readonly ILogHandler _inner;

            public ErrorsOnlyLogHandler(ILogHandler inner)
            {
                _inner = inner;
            }

            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
            {
                if (logType == LogType.Log || logType == LogType.Warning) return;
                _inner.LogFormat(logType, context, format, args);
            }

            public void LogException(Exception exception, UnityEngine.Object context)
            {
                _inner.LogException(exception, context);
            }
        }
#endif
    }
}
