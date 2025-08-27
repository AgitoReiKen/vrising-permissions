using HarmonyLib;
using Permissions.Utils;
using ProjectM;

namespace Permissions.Patches;

[HarmonyPatch(typeof(GameBootstrap), "Start")]
[HarmonyPriority(Priority.Low)]
public class GameBootstrap_Patch
{
    public static void Postfix()
    {
        
        var api = (Permissions.Core.PermissionsAPI)Plugin.Instance.API;
        api.OnStartup();
    }
}
