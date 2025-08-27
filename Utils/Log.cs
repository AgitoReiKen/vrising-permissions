using System;
using BepInEx.Logging;
using Permissions;

namespace Permissions.Utils;
static class AnsiColors
{
    public static string green = "\u001b[32m";
    public static string reset = "\u001b[0m";
}
internal static class Log
{ 
    public static void Warning(string s1) => Plugin.Instance.Log.LogWarning(s1);

    public static void Error(string s1) => Plugin.Instance.Log.LogError(s1);

    public static void Debug(string s1) => Plugin.Instance.Log.LogDebug(s1);

    public static void Info(string s1) => Plugin.Instance.Log.LogInfo($"{AnsiColors.green}{s1}{AnsiColors.reset}");
    
    
 
}