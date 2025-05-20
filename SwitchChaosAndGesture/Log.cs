using BepInEx.Logging;
using MonoMod.Cil;

namespace SwitchChaosAndGesture;

internal static class Log
{
    private static ManualLogSource _logger;

    internal static void Init(ManualLogSource logger) => _logger = logger;
    internal static void Info(object data) => _logger.LogInfo(data);
    internal static void Debug(object data) => _logger.LogDebug(data);
    internal static void Message(object data) => _logger.LogMessage(data);
    internal static void Warning(object data) => _logger.LogWarning(data);
    internal static void Error(object data) => _logger.LogError(data);
    internal static void Fatal(object data) => _logger.LogFatal(data);

    internal static void PatchFail(string message) => _logger.LogError("Failed to patch " + message);
    internal static void PatchFail(ILContext il) => PatchFail(il.Method.Name);
}