﻿namespace YourRootNamespace.Logging.LogProviders
{
    using System;
	using System.Collections.Generic;
	using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Linq.Expressions;

#if LIBLOG_EXCLUDE_CODE_COVERAGE
    [ExcludeFromCodeCoverage]
#endif
    internal class NLogLogProvider : LogProviderBase
    {
        private readonly Func<string, object> _getLoggerByNameDelegate;

        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "LogManager")]
        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "NLog")]
        public NLogLogProvider()
        {
            if (!IsLoggerAvailable()) throw new LibLogException("NLog.LogManager not found");
            _getLoggerByNameDelegate = GetGetLoggerMethodCall();
        }

        static NLogLogProvider()
        {
            ProviderIsAvailableOverride = true;
        }
        
        public static bool ProviderIsAvailableOverride { get; set; }

        public override Logger GetLogger(string name)
        {
            return new NLogLogger(_getLoggerByNameDelegate(name)).Log;
        }

        public static bool IsLoggerAvailable()
        {
            return ProviderIsAvailableOverride && GetLogManagerType() != null;
        }

        protected override OpenNdc GetOpenNdcMethod()
        {
            var messageParam = Expression.Parameter(typeof(string), "message");

            var ndlcContextType = FindType("NLog.NestedDiagnosticsLogicalContext", "NLog");
            if (ndlcContextType != null)
            {
                var pushObjectMethod = ndlcContextType.GetMethod("PushObject", typeof(object));
                if (pushObjectMethod != null)
                {
                    // NLog 4.6 introduces PushObject with correct handling of logical callcontext (NDLC)
                    var pushObjectMethodCall = Expression.Call(null, pushObjectMethod, messageParam);
                    return Expression.Lambda<OpenNdc>(pushObjectMethodCall, messageParam).Compile();
                }
            }

            var ndcContextType = FindType("NLog.NestedDiagnosticsContext", "NLog");
            var pushMethod = ndcContextType.GetMethod("Push", typeof(string));

            var pushMethodCall = Expression.Call(null, pushMethod, messageParam);
            return Expression.Lambda<OpenNdc>(pushMethodCall, messageParam).Compile();
        }

        protected override OpenMdc GetOpenMdcMethod()
        {
            var keyParam = Expression.Parameter(typeof(string), "key");

            var ndlcContextType = FindType("NLog.NestedDiagnosticsLogicalContext", "NLog");
            if (ndlcContextType != null)
            {
                var pushObjectMethod = ndlcContextType.GetMethod("PushObject", typeof(object));
                if (pushObjectMethod != null)
                {
                    // NLog 4.6 introduces SetScoped with correct handling of logical callcontext (MDLC)
                    var mdlcContextType = FindType("NLog.MappedDiagnosticsLogicalContext", "NLog");
                    if (mdlcContextType != null)
                    {
                        var setScopedMethod = mdlcContextType.GetMethod("SetScoped", typeof(string), typeof(object));
                        if (setScopedMethod != null)
                        {
                            var valueObjParam = Expression.Parameter(typeof(object), "value");
                            var setScopedMethodCall = Expression.Call(null, setScopedMethod, keyParam, valueObjParam);
                            var setMethodLambda = Expression.Lambda<Func<string, object, IDisposable>>(setScopedMethodCall, keyParam, valueObjParam).Compile();
                            return (key, value, _) => setMethodLambda(key, value);
                        }
                    }
                }
            }

            var mdcContextType = FindType("NLog.MappedDiagnosticsContext", "NLog");
            var setMethod = mdcContextType.GetMethod("Set", typeof(string), typeof(string));
            var removeMethod = mdcContextType.GetMethod("Remove", typeof(string));
            var valueParam = Expression.Parameter(typeof(string), "value");
            var setMethodCall = Expression.Call(null, setMethod, keyParam, valueParam);
            var removeMethodCall = Expression.Call(null, removeMethod, keyParam);

            var set = Expression
                .Lambda<Action<string, string>>(setMethodCall, keyParam, valueParam)
                .Compile();
            var remove = Expression
                .Lambda<Action<string>>(removeMethodCall, keyParam)
                .Compile();

            return (key, value, _) =>
            {
                set(key, value.ToString());
                return new DisposableAction(() => remove(key));
            };
        }

        private static Type GetLogManagerType()
        {
            return FindType("NLog.LogManager", "NLog");
        }

        private static Func<string, object> GetGetLoggerMethodCall()
        {
            var logManagerType = GetLogManagerType();
            var method = logManagerType.GetMethod("GetLogger", typeof(string));
            var nameParam = Expression.Parameter(typeof(string), "name");
            var methodCall = Expression.Call(null, method, nameParam);
            return Expression.Lambda<Func<string, object>>(methodCall, nameParam).Compile();
        }

#if LIBLOG_EXCLUDE_CODE_COVERAGE
    [ExcludeFromCodeCoverage]
#endif
        internal class NLogLogger
        {
            private static Func<string, object, string, object[], Exception, object> s_logEventInfoFact;

            private static object s_levelTrace;
            private static object s_levelDebug;
            private static object s_levelInfo;
            private static object s_levelWarn;
            private static object s_levelError;
            private static object s_levelFatal;

            private static bool s_structuredLoggingEnabled;
            private static readonly Lazy<bool> Initialized = new Lazy<bool>(Initialize);
            private static Exception s_initializeException;

            delegate string LoggerNameDelegate(object logger);
            delegate void LogEventDelegate(object logger, Type wrapperType, object logEvent);
            delegate bool IsEnabledDelegate(object logger);
            delegate void LogDelegate(object logger, string message);
            delegate void LogExceptionDelegate(object logger, string message, Exception exception);

            private static LoggerNameDelegate s_loggerNameDelegate;
            private static LogEventDelegate s_logEventDelegate;

            private static IsEnabledDelegate s_isTraceEnabledDelegate;
            private static IsEnabledDelegate s_isDebugEnabledDelegate;
            private static IsEnabledDelegate s_isInfoEnabledDelegate;
            private static IsEnabledDelegate s_isWarnEnabledDelegate;
            private static IsEnabledDelegate s_isErrorEnabledDelegate;
            private static IsEnabledDelegate s_isFatalEnabledDelegate;

            private static LogDelegate s_traceDelegate;
            private static LogDelegate s_debugDelegate;
            private static LogDelegate s_infoDelegate;
            private static LogDelegate s_warnDelegate;
            private static LogDelegate s_errorDelegate;
            private static LogDelegate s_fatalDelegate;

            private static LogExceptionDelegate s_traceExceptionDelegate;
            private static LogExceptionDelegate s_debugExceptionDelegate;
            private static LogExceptionDelegate s_infoExceptionDelegate;
            private static LogExceptionDelegate s_warnExceptionDelegate;
            private static LogExceptionDelegate s_errorExceptionDelegate;
            private static LogExceptionDelegate s_fatalExceptionDelegate;

            private readonly object _logger;

            internal NLogLogger(object logger)
            {
                _logger = logger;
            }

            private static bool Initialize()
            {
                try
                {
                    var logEventLevelType = FindType("NLog.LogLevel", "NLog");
                    if (logEventLevelType == null) throw new LibLogException("Type NLog.LogLevel was not found.");

                    var levelFields = logEventLevelType.GetFields().ToList();
                    s_levelTrace = levelFields.First(x => x.Name == "Trace").GetValue(null);
                    s_levelDebug = levelFields.First(x => x.Name == "Debug").GetValue(null);
                    s_levelInfo = levelFields.First(x => x.Name == "Info").GetValue(null);
                    s_levelWarn = levelFields.First(x => x.Name == "Warn").GetValue(null);
                    s_levelError = levelFields.First(x => x.Name == "Error").GetValue(null);
                    s_levelFatal = levelFields.First(x => x.Name == "Fatal").GetValue(null);

                    var logEventInfoType = FindType("NLog.LogEventInfo", "NLog");
                    if (logEventInfoType == null) throw new LibLogException("Type NLog.LogEventInfo was not found.");

                    var loggingEventConstructor =
                        logEventInfoType.GetConstructorPortable(logEventLevelType, typeof(string),
                            typeof(IFormatProvider), typeof(string), typeof(object[]), typeof(Exception));

                    var loggerNameParam = Expression.Parameter(typeof(string));
                    var levelParam = Expression.Parameter(typeof(object));
                    var messageParam = Expression.Parameter(typeof(string));
                    var messageArgsParam = Expression.Parameter(typeof(object[]));
                    var exceptionParam = Expression.Parameter(typeof(Exception));
                    var levelCast = Expression.Convert(levelParam, logEventLevelType);

                    var newLoggingEventExpression =
                        Expression.New(loggingEventConstructor,
                            levelCast,
                            loggerNameParam,
                            Expression.Constant(null, typeof(IFormatProvider)),
                            messageParam,
                            messageArgsParam,
                            exceptionParam
                        );

                    s_logEventInfoFact = Expression.Lambda<Func<string, object, string, object[], Exception, object>>(
                        newLoggingEventExpression,
                        loggerNameParam, levelParam, messageParam, messageArgsParam, exceptionParam).Compile();

                    var loggerType = FindType("NLog.Logger", "NLog");

                    s_loggerNameDelegate = GetLoggerNameDelegate(loggerType);

                    s_logEventDelegate = GetLogEventDelegate(loggerType, logEventInfoType);

                    s_isTraceEnabledDelegate = GetIsEnabledDelegate(loggerType, "IsTraceEnabled");
                    s_isDebugEnabledDelegate = GetIsEnabledDelegate(loggerType, "IsDebugEnabled");
                    s_isInfoEnabledDelegate = GetIsEnabledDelegate(loggerType, "IsInfoEnabled");
                    s_isWarnEnabledDelegate = GetIsEnabledDelegate(loggerType, "IsWarnEnabled");
                    s_isErrorEnabledDelegate = GetIsEnabledDelegate(loggerType, "IsErrorEnabled");
                    s_isFatalEnabledDelegate = GetIsEnabledDelegate(loggerType, "IsFatalEnabled");

                    s_traceDelegate = GetLogDelegate(loggerType, "Trace");
                    s_debugDelegate = GetLogDelegate(loggerType, "Debug");
                    s_infoDelegate = GetLogDelegate(loggerType, "Info");
                    s_warnDelegate = GetLogDelegate(loggerType, "Warn");
                    s_errorDelegate = GetLogDelegate(loggerType, "Error");
                    s_fatalDelegate = GetLogDelegate(loggerType, "Fatal");

                    s_traceExceptionDelegate = GetLogExceptionDelegate(loggerType, "TraceException");
                    s_debugExceptionDelegate = GetLogExceptionDelegate(loggerType, "DebugException");
                    s_infoExceptionDelegate = GetLogExceptionDelegate(loggerType, "InfoException");
                    s_warnExceptionDelegate = GetLogExceptionDelegate(loggerType, "WarnException");
                    s_errorExceptionDelegate = GetLogExceptionDelegate(loggerType, "ErrorException");
                    s_fatalExceptionDelegate = GetLogExceptionDelegate(loggerType, "FatalException");

                    s_structuredLoggingEnabled = IsStructuredLoggingEnabled();
                }
                catch (Exception ex)
                {
                    s_initializeException = ex;
                    return false;
                }

                return true;
            }

            private static IsEnabledDelegate GetIsEnabledDelegate(Type loggerType, string propertyName)
            {
                var isEnabledPropertyInfo = loggerType.GetProperty(propertyName);
                var instanceParam = Expression.Parameter(typeof(object));
                var instanceCast = Expression.Convert(instanceParam, loggerType);
                var propertyCall = Expression.Property(instanceCast, isEnabledPropertyInfo);
                return Expression.Lambda<IsEnabledDelegate>(propertyCall, instanceParam).Compile();
            }

            private static LoggerNameDelegate GetLoggerNameDelegate(Type loggerType)
            {
                var isEnabledPropertyInfo = loggerType.GetProperty("Name");
                var instanceParam = Expression.Parameter(typeof(object));
                var instanceCast = Expression.Convert(instanceParam, loggerType);
                var propertyCall = Expression.Property(instanceCast, isEnabledPropertyInfo);
                return Expression.Lambda<LoggerNameDelegate>(propertyCall, instanceParam).Compile();
            }

            private static LogDelegate GetLogDelegate(Type loggerType, string name)
            {
                var logMethodInfo = loggerType.GetMethod(name, new Type[] { typeof(string) });
                var instanceParam = Expression.Parameter(typeof(object));
                var instanceCast = Expression.Convert(instanceParam, loggerType);
                var messageParam = Expression.Parameter(typeof(string));
                var logCall = Expression.Call(instanceCast, logMethodInfo, messageParam);
                return Expression.Lambda<LogDelegate>(logCall, instanceParam, messageParam).Compile();
            }

            private static LogEventDelegate GetLogEventDelegate(Type loggerType, Type logEventType)
            {
                var logMethodInfo = loggerType.GetMethod("Log", new Type[] { typeof(Type), logEventType });
                var instanceParam = Expression.Parameter(typeof(object));
                var instanceCast = Expression.Convert(instanceParam, loggerType);
                var loggerTypeParam = Expression.Parameter(typeof(Type));
                var logEventParam = Expression.Parameter(typeof(object));
                var logEventCast = Expression.Convert(logEventParam, logEventType);
                var logCall = Expression.Call(instanceCast, logMethodInfo, loggerTypeParam, logEventCast);
                return Expression.Lambda<LogEventDelegate>(logCall, instanceParam, loggerTypeParam, logEventParam).Compile();
            }

            private static LogExceptionDelegate GetLogExceptionDelegate(Type loggerType, string name)
            {
                var logMethodInfo = loggerType.GetMethod(name, new Type[] { typeof(string), typeof(Exception) });
                var instanceParam = Expression.Parameter(typeof(object));
                var instanceCast = Expression.Convert(instanceParam, loggerType);
                var messageParam = Expression.Parameter(typeof(string));
                var exceptionParam = Expression.Parameter(typeof(Exception));
                var logCall = Expression.Call(instanceCast, logMethodInfo, messageParam, exceptionParam);
                return Expression.Lambda<LogExceptionDelegate>(logCall, instanceParam, messageParam, exceptionParam).Compile();
            }

            [SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
            public bool Log(LogLevel logLevel, Func<string> messageFunc, Exception exception,
                params object[] formatParameters)
            {
                if (!Initialized.Value)
                    throw new LibLogException(ErrorInitializingProvider, s_initializeException);

                if (messageFunc == null) return IsLogLevelEnable(logLevel);

                if (s_logEventInfoFact != null)
                {
                    if (IsLogLevelEnable(logLevel))
                    {
                        var formatMessage = messageFunc();
                        if (!s_structuredLoggingEnabled)
                        {
							IEnumerable<string> _;
                            formatMessage =
                                LogMessageFormatter.FormatStructuredMessage(formatMessage,
                                    formatParameters,
                                    out _);
                            formatParameters = null; // Has been formatted, no need for parameters
                        }

                        var callsiteLoggerType = typeof(NLogLogger);
                        // Callsite HACK - Extract the callsite-logger-type from the messageFunc
                        var methodType = messageFunc.Method.DeclaringType;
                        if (methodType == typeof(LogExtensions) ||
                            methodType != null && methodType.DeclaringType == typeof(LogExtensions))
                            callsiteLoggerType = typeof(LogExtensions);
                        else if (methodType == typeof(LoggerExecutionWrapper) || methodType != null &&
                                 methodType.DeclaringType == typeof(LoggerExecutionWrapper))
                            callsiteLoggerType = typeof(LoggerExecutionWrapper);
                        var nlogLevel = TranslateLevel(logLevel);
                        var nlogEvent = s_logEventInfoFact(s_loggerNameDelegate(_logger), nlogLevel, formatMessage, formatParameters,
                            exception);
                        s_logEventDelegate(_logger, callsiteLoggerType, nlogEvent);
                        return true;
                    }

                    return false;
                }

                messageFunc = LogMessageFormatter.SimulateStructuredLogging(messageFunc, formatParameters);
                if (exception != null) return LogException(logLevel, messageFunc, exception);

                switch (logLevel)
                {
                    case LogLevel.Debug:
                        if (s_isDebugEnabledDelegate(_logger))
                        {
                            s_debugDelegate(_logger, messageFunc());
                            return true;
                        }

                        break;
                    case LogLevel.Info:
                        if (s_isInfoEnabledDelegate(_logger))
                        {
                            s_infoDelegate(_logger, messageFunc());
                            return true;
                        }

                        break;
                    case LogLevel.Warn:
                        if (s_isWarnEnabledDelegate(_logger))
                        {
                            s_warnDelegate(_logger, messageFunc());
                            return true;
                        }

                        break;
                    case LogLevel.Error:
                        if (s_isErrorEnabledDelegate(_logger))
                        {
                            s_errorDelegate(_logger, messageFunc());
                            return true;
                        }

                        break;
                    case LogLevel.Fatal:
                        if (s_isFatalEnabledDelegate(_logger))
                        {
                            s_fatalDelegate(_logger, messageFunc());
                            return true;
                        }

                        break;
                    default:
                        if (s_isTraceEnabledDelegate(_logger))
                        {
                            s_traceDelegate(_logger, messageFunc());
                            return true;
                        }

                        break;
                }

                return false;
            }

            [SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
            private bool LogException(LogLevel logLevel, Func<string> messageFunc, Exception exception)
            {
                switch (logLevel)
                {
                    case LogLevel.Debug:
                        if (s_isDebugEnabledDelegate(_logger))
                        {
                            s_debugExceptionDelegate(_logger, messageFunc(), exception);
                            return true;
                        }

                        break;
                    case LogLevel.Info:
                        if (s_isInfoEnabledDelegate(_logger))
                        {
                            s_infoExceptionDelegate(_logger, messageFunc(), exception);
                            return true;
                        }

                        break;
                    case LogLevel.Warn:
                        if (s_isWarnEnabledDelegate(_logger))
                        {
                            s_warnExceptionDelegate(_logger, messageFunc(), exception);
                            return true;
                        }

                        break;
                    case LogLevel.Error:
                        if (s_isErrorEnabledDelegate(_logger))
                        {
                            s_errorExceptionDelegate(_logger, messageFunc(), exception);
                            return true;
                        }

                        break;
                    case LogLevel.Fatal:
                        if (s_isFatalEnabledDelegate(_logger))
                        {
                            s_fatalExceptionDelegate(_logger, messageFunc(), exception);
                            return true;
                        }

                        break;
                    default:
                        if (s_isTraceEnabledDelegate(_logger))
                        {
                            s_traceExceptionDelegate(_logger, messageFunc(), exception);
                            return true;
                        }

                        break;
                }

                return false;
            }

            private bool IsLogLevelEnable(LogLevel logLevel)
            {
                switch (logLevel)
                {
                    case LogLevel.Debug:
                        return s_isDebugEnabledDelegate(_logger);
                    case LogLevel.Info:
                        return s_isInfoEnabledDelegate(_logger);
                    case LogLevel.Warn:
                        return s_isWarnEnabledDelegate(_logger);
                    case LogLevel.Error:
                        return s_isErrorEnabledDelegate(_logger);
                    case LogLevel.Fatal:
                        return s_isFatalEnabledDelegate(_logger);
                    default:
                        return s_isTraceEnabledDelegate(_logger);
                }
            }

            private object TranslateLevel(LogLevel logLevel)
            {
                switch (logLevel)
                {
                    case LogLevel.Trace:
                        return s_levelTrace;
                    case LogLevel.Debug:
                        return s_levelDebug;
                    case LogLevel.Info:
                        return s_levelInfo;
                    case LogLevel.Warn:
                        return s_levelWarn;
                    case LogLevel.Error:
                        return s_levelError;
                    case LogLevel.Fatal:
                        return s_levelFatal;
                    default:
                        throw new ArgumentOutOfRangeException("logLevel", logLevel, null);
                }
            }

            private static bool IsStructuredLoggingEnabled()
            {
                var configFactoryType = FindType("NLog.Config.ConfigurationItemFactory", "NLog");
                if (configFactoryType != null)
                {
                    var parseMessagesProperty = configFactoryType.GetProperty("ParseMessageTemplates");
                    if (parseMessagesProperty != null)
                    {
                        var defaultProperty = configFactoryType.GetProperty("Default");
                        if (defaultProperty != null)
                        {
                            var configFactoryDefault = defaultProperty.GetValue(null, null);
                            if (configFactoryDefault != null)
                            {
                                var parseMessageTemplates =
                                    parseMessagesProperty.GetValue(configFactoryDefault, null) as bool?;
                                if (parseMessageTemplates != false) return true;
                            }
                        }
                    }
                }

                return false;
            }
        }
    }
}