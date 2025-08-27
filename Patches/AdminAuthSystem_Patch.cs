using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppSystem;
using Permissions.Core;
using Permissions.Utils;
using ProjectM;
using ProjectM.Network;
using ProjectM.Shared;
using Unity.Collections;

namespace Permissions.Patches;

[HarmonyPatch(typeof(AdminAuthSystem), "OnUpdate")]
[HarmonyPriority(Priority.Low)]
public class AdminAuthSystem_Patch
{
    public static bool Prefix(AdminAuthSystem __instance)
    {
        var adminAuthEvents = __instance._Query.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < adminAuthEvents.Length; ++i)
        {
            var entity = adminAuthEvents[i];
            if (!__instance.World.EntityManager.TryGetComponentData(entity, out FromCharacter from))
            {
                Log.Error("[AdminAuthSystem_Patch] No FromCharacter component found in entity queried with _Query");
                continue;
            }

            if (!__instance.World.EntityManager.TryGetComponentData(from.User, out User user))
            {
                Log.Error("[AdminAuthSystem_Patch] No User component found");
                continue;
            }
            
            //if (__instance.CheckAuth(__instance._ServerBootstrapSystem.ServerHostData.OwnerPlatformId, user))
            if (__instance.IsAdmin(user.PlatformId))
            {
                Log.Debug($"AdminAuthSystem Is admin {user.PlatformId}");
                ((PermissionsAPI)Plugin.Instance.API).OnAdminAuth(user.PlatformId);
            }
            else
            {
                Log.Debug($"AdminAuthSystem Is not admin {user.PlatformId}");
            }
        }

        return true;
    }
}