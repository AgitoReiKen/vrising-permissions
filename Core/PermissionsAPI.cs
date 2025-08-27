using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Commands.API;
using Database.API;
using Il2CppSystem.Threading;
using Permissions.API;
using Permissions.Resources;
using Permissions.Utils;
using ProjectM;
using ProjectM.Network;
using ProjectM.Shared;
using Unity.Collections;

namespace Permissions.Core;

public class PermissionsRegistry
{
    public Dictionary<ulong, Dictionary<string, DateTime?>> UserPermissions = new();
    public PermissionsRegistry()
    {
        using var con = Database.Plugin.Instance.API.GetConnection(MyPluginInfo.PLUGIN_GUID);
        try
        {
            con.Open();
            using var q =
                con.Query(
                    $"CREATE TABLE IF NOT EXISTS {MyPluginInfo.PLUGIN_NAME} (" +
                    $"PlatformId BIGINT NOT NULL, " +
                    $"Permission VARCHAR(32) NOT NULL, " +
                    $"ExpiresAt DATETIME DEFAULT NULL, " +
                    $"GrantedAt DATETIME NOT NULL, " +
                    $"UpdatedAt DATETIME NOT NULL, " +
                    $"PRIMARY KEY (PlatformId, Permission)" +
                    ");"
                );
            q.ExecuteNonQuery();
            con.Close();
        }
        catch (Exception)
        {
            Log.Error($"Couldn't create {MyPluginInfo.PLUGIN_NAME} table");
            con.Close();
            throw;
        }

        DeleteExpired();
    }
    public void Upsert(ulong platformId, string permission, DateTime? expiresAt)
    {
        EnsureLoaded(platformId);
        
        UserPermissions[platformId][permission] = expiresAt;
        
        using var con = Database.Plugin.Instance.API.GetConnection(MyPluginInfo.PLUGIN_GUID);
        try
        {
            con.Open();
            bool hasRow;

            using (var select = con.Query(
                       $"SELECT count(1) FROM {MyPluginInfo.PLUGIN_NAME} WHERE PlatformId=@p0 AND Permission=@p1",
                       platformId,
                       permission))
            {
                hasRow = (long)select.ExecuteScalar()! != 0;
            }
            
            if (hasRow)
            {
                using var update = con.Query($"UPDATE {MyPluginInfo.PLUGIN_NAME} SET ExpiresAt=@p0, UpdatedAt=@p1 WHERE PlatformId=@p2 AND Permission=@p3",
                    expiresAt, DateTime.UtcNow, platformId, permission);
                update.ExecuteNonQuery();
            }
            else
            {
                using var insert =
                    con.Query(
                        $"INSERT INTO {MyPluginInfo.PLUGIN_NAME}(PlatformId, Permission, ExpiresAt, GrantedAt, UpdatedAt) VALUES(@p0, @p1, @p2, @p3, @p4)",
                        platformId, permission, expiresAt, DateTime.UtcNow, DateTime.UtcNow);
                insert.ExecuteNonQuery();
            }
            con.Close();
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't UPSERT permission {permission} for user {platformId}");
            Log.Error(ex.Message);
            con.Close();
            throw;
        }

    }

    public void Delete(ulong platformId, string permission)
    {
        EnsureLoaded(platformId);

        UserPermissions[platformId].Remove(permission);
        
        using var con = Database.Plugin.Instance.API.GetConnection(MyPluginInfo.PLUGIN_GUID);
        try
        {
            con.Open();
            using var q = con.Query($"DELETE FROM {MyPluginInfo.PLUGIN_NAME} WHERE PlatformId=@p0 AND Permission=@p1", platformId,
                permission);
            q.ExecuteNonQuery();
            con.Close();
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't DELETE permission {permission} for user {platformId}");
            Log.Error(ex.Message);
            con.Close();
            throw;
        }

    }
     
    public Dictionary<string, DateTime?> Get(ulong platformId)
    {
        EnsureLoaded(platformId);
        
        RemoveExpired(UserPermissions[platformId]);
        
        return UserPermissions[platformId];
    }
    public bool Has(ulong platformId, string permission)
    {
        EnsureLoaded(platformId);

        if (!UserPermissions[platformId].TryGetValue(permission, out DateTime? expiresAt)) return false;
        
        if (IsExpired(expiresAt)) { UserPermissions[platformId].Remove(permission);  return false; }
        
        return true;
    }
    public void RefreshPermissions(ulong? platformId = null )
    {
        if (platformId != null)
        {
            if (UserPermissions.TryGetValue(platformId.GetValueOrDefault(), out var permissions))
            {
                permissions.Clear();
            }
            return;
        }
        // Will be filled when any action with particular user happens.
        UserPermissions.Clear();
    }
    
    private void DeleteExpired()
    {
        using var con = Database.Plugin.Instance.API.GetConnection(MyPluginInfo.PLUGIN_GUID);
        try
        {
            con.Open();
            using var q = con.Query($"DELETE FROM {MyPluginInfo.PLUGIN_NAME} WHERE ExpiresAt IS NOT NULL AND ExpiresAt <= @p0", DateTime.UtcNow);
            q.ExecuteNonQuery();
            con.Close();
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't DELETE expired permissions");
            Log.Error(ex.Message);
            con.Close();
        }
    }

    private void EnsureLoaded(ulong platformId)
    {
        if (!UserPermissions.ContainsKey(platformId))
        {
            Log.Debug($"{platformId} is not loaded");
            UserPermissions[platformId] = LoadPermissions(platformId);
            Log.Debug($"Loaded: {String.Join(',', UserPermissions[platformId])}");
        }
    }
    
    private void RemoveExpired(Dictionary<string, DateTime?> permissions)
    {
        HashSet<string> expired = new();
        foreach (var p in permissions)
        {
            if (IsExpired(p.Value))
            {
                expired.Add(p.Key);
            }
        }
 
        foreach (var p in expired)
        {
            permissions.Remove(p); 
        }
    }
    /// <param name="time">If set to null - permission will never expire</param>
    /// <param name="additive">If set to true - add time to permission, otherwise set based on DateTime.UtcNow</param>
    /// <returns></returns>
    public DateTime? BuildExpirationTime(ulong platformId, string permission, TimeSpan? time, bool additive)
    {
        if (time == null) return null;
        EnsureLoaded(platformId);
        
        if (UserPermissions[platformId].TryGetValue(permission, out var expirationTime))
        {
            if (expirationTime != null && expirationTime > DateTime.UtcNow && additive)
            {
                return expirationTime + time;
            }

        }
        return DateTime.UtcNow + time;
    }

    public bool IsExpired(DateTime? expiresAt)
    {
        if (expiresAt != null && DateTime.UtcNow >= expiresAt)
        {
            return true;
        }

        return false;
    }
    private Dictionary<string, DateTime?> LoadPermissions(ulong platformId)
    {
        Dictionary<string, DateTime?> data = new();
        using var con = Database.Plugin.Instance.API.GetConnection(MyPluginInfo.PLUGIN_GUID);
        try
        {
            con.Open();
            using var q =
                con.Query($"SELECT Permission, ExpiresAt FROM {MyPluginInfo.PLUGIN_NAME} " +
                          $"WHERE PlatformId=@p0 AND (ExpiresAt IS NULL OR ExpiresAt > @p1)",
                    platformId, DateTime.UtcNow);
            using var r = q.ExecuteReader();
            var permissionOrd = r.GetOrdinal("Permission");
            var expiresAtOrd = r.GetOrdinal("ExpiresAt");

            while (r.Read())
            {
                string permission = r.GetString(permissionOrd);
                DateTime? expiresAt = r.IsDBNull(expiresAtOrd) ? null : r.GetDateTime(expiresAtOrd);
                data[permission] = expiresAt;
            }

        }
        catch (Exception ex)
        {
            Log.Error("LoadPermissions failed with: ");
            Log.Error(ex.Message);
        }
        con.Close();
        return data;
    }

  
}
public class PermissionsAPI : IPermissionsAPI
{
    public Config Config = Config.Load();
    public PermissionsRegistry Registry = new();
    public readonly object Lock = new();

    public bool AddPermission(ulong platformId, string permissionId, TimeSpan? time)
    {
        lock (Lock)
        {
            try
            {
                Registry.Upsert(platformId, permissionId,
                    Registry.BuildExpirationTime(platformId, permissionId, time, true));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
    public bool SetPermission(ulong platformId, string permissionId, TimeSpan? time)
    {
        lock (Lock)
        {
            try
            {
                Registry.Upsert(platformId, permissionId,
                    Registry.BuildExpirationTime(platformId, permissionId, time, false));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
    public bool RemovePermission(ulong platformId, string permissionId)
    {
        lock (Lock)
        {
            try
            {
                Registry.Delete(platformId, permissionId);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public bool HasPermission(ulong platformId, string permissionId)
    {
        lock (Lock)
        {
            return Registry.Has(platformId, permissionId);
        }
    }

    public bool HasPermissions(ulong platformId, params string[] permissionIds)
    {
        lock (Lock)
        {
            var all = Registry.Get(platformId);
            return permissionIds.All(p => all.ContainsKey(p) && !Registry.IsExpired(all[p]));
        }
    }

    public bool HasAnyPermission(ulong platformId, params string[] permissionIds)
    {
        lock (Lock)
        {
            var all = Registry.Get(platformId);
            return permissionIds.Any(p => all.ContainsKey(p) && !Registry.IsExpired(all[p]));
        }
    }

    public bool GetPermissions(ulong platformId, out Dictionary<string, DateTime?> permissions)
    {
        lock (Lock)
        {
            permissions = new Dictionary<string, DateTime?>(Registry.Get(platformId));
            return true;
        }
    }

    public bool AddToGroup(ulong platformId, string groupId, TimeSpan? time = null)
    {
        if (!Config.Groups.TryGetValue(groupId, out var permissions))
        {
            Log.Error($"AddToGroup failed because there is no such group {groupId}");
            return false;
        }

        
        try
        {
            lock (Lock)
            {
                foreach (var permission in permissions)
                {
                    Registry.Upsert(platformId, permission,
                        Registry.BuildExpirationTime(platformId, permission, time, false));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"AddToGroup failed for user {platformId} and group {groupId}");
            Log.Error(ex.Message);
            return false;
        }

        return true;
    }

    public bool RemoveFromGroup(ulong platformId, string groupId)
    {
        if (!Config.Groups.TryGetValue(groupId, out var permissions))
        {
            Log.Error($"RemoveFromGroup failed because there is no such group {groupId}");
            return false;
        }

        try
        {
            lock (Lock)
            {
                foreach (var permission in permissions)
                {
                    Registry.Delete(platformId, permission);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"RemoveFromGroup failed for user {platformId} and group {groupId}");
            Log.Error(ex.Message);
            return false;
        }

        return true;
    }

    public bool IsInGroup(ulong platformId, string groupId)
    {
        if (!Config.Groups.TryGetValue(groupId, out var permissions))
        {
            Log.Error($"IsInGroup returned false because there is no such group {groupId}");
            return false;
        }
        
        return HasPermissions(platformId, permissions.ToArray());
    }

    public bool IsInGroups(ulong platformId, params string[] groupIds)
    {
        HashSet<string> permissions = new();
        foreach (var groupId in groupIds)
        {
            if (!Config.Groups.TryGetValue(groupId, out var groupPermissions))
            {
                Log.Error($"IsInGroups returned false because there is no such group {groupId}");
                return false;
            }
            permissions.UnionWith(groupPermissions);
        }
        
        return HasPermissions(platformId, permissions.ToArray());

    }

    public HashSet<string> GetRegisteredPermissions()
    {
        return Config.Permissions;
    }

    public Dictionary<string, HashSet<string>> GetRegisteredGroups()
    {
        return Config.Groups;
    }

    public void OnStartup()
    {
        RegisterCommands();
    }

    public void RegisterCommands()
    {
        var commandsAPI = Commands.Plugin.Instance.API;
        if (Config.ShowPermissionsCommand != null)
        {
            commandsAPI.RegisterCommand(MyPluginInfo.PLUGIN_GUID,
                new CommandData("ShowPermissions", ShowPermissionsCommand, Config.ShowPermissionsCommand));
        } 
        
        if (Config.AddPermissionCommand != null)
        {
            commandsAPI.RegisterCommand(MyPluginInfo.PLUGIN_GUID,
                new CommandData("AddPermission", AddPermissionCommand, Config.AddPermissionCommand));
        }
        
        if (Config.SetPermissionCommand != null)
        {
            commandsAPI.RegisterCommand(MyPluginInfo.PLUGIN_GUID,
                new CommandData("SetPermission", SetPermissionCommand, Config.SetPermissionCommand));
        }
        
        if (Config.RemovePermissionCommand != null)
        {
            commandsAPI.RegisterCommand(MyPluginInfo.PLUGIN_GUID,
                new CommandData("RemovePermission", RemovePermissionCommand, Config.RemovePermissionCommand));
        }
        
        if (Config.AddToGroupCommand != null)
        {
            commandsAPI.RegisterCommand(MyPluginInfo.PLUGIN_GUID,
                new CommandData("AddToGroup", AddToGroupCommand, Config.AddToGroupCommand));
        }
        
        if (Config.RemoveFromGroupCommand != null)
        {
            commandsAPI.RegisterCommand(MyPluginInfo.PLUGIN_GUID,
                new CommandData("RemoveFromGroup", RemoveFromGroupCommand, Config.RemoveFromGroupCommand));
        }
        
        if (Config.RefreshPermissionsCommand != null)
        {
            commandsAPI.RegisterCommand(MyPluginInfo.PLUGIN_GUID,
                new CommandData("RefreshPermissions", RefreshPermissionsCommand, Config.RefreshPermissionsCommand));
        }
        
        if (Config.ReloadCommand != null)
        {
            commandsAPI.RegisterCommand(MyPluginInfo.PLUGIN_GUID,
                new CommandData("Reload", ReloadCommand, Config.ReloadCommand));
        }
    }

    public void UnregisterCommands()
    {
        var commandsAPI = Commands.Plugin.Instance.API;
        commandsAPI.UnregisterCommand(MyPluginInfo.PLUGIN_GUID, "ShowPermissions");
        commandsAPI.UnregisterCommand(MyPluginInfo.PLUGIN_GUID, "AddPermission");
        commandsAPI.UnregisterCommand(MyPluginInfo.PLUGIN_GUID, "SetPermission");
        commandsAPI.UnregisterCommand(MyPluginInfo.PLUGIN_GUID, "RemovePermission");
        commandsAPI.UnregisterCommand(MyPluginInfo.PLUGIN_GUID, "AddToGroup");
        commandsAPI.UnregisterCommand(MyPluginInfo.PLUGIN_GUID, "RemoveFromGroup");
        commandsAPI.UnregisterCommand(MyPluginInfo.PLUGIN_GUID, "RefreshPermissions");
        commandsAPI.UnregisterCommand(MyPluginInfo.PLUGIN_GUID, "Reload");
    }

    public bool HasAccess(ulong platformId, string permission)
    {
        if (!GetPermissions(platformId, out var permissions))
        {
            return false;
        }

        return permissions.Keys.Any(x =>
        {
            if (Config.PermissionAccess.TryGetValue(x, out var accessible))
            {
                return accessible.Contains(permission);
            }

            return false;
        });

    }
    public bool HasAccessToGroup(ulong platformId, string group)
    {
        if (!GetPermissions(platformId, out var permissions))
        {
            return false;
        }

        var groupPermissions = Config.Groups[group];
        foreach (var gp in groupPermissions)
        {

            if (!permissions.Keys.Any(x =>
                {
                    if (Config.PermissionAccess.TryGetValue(x, out var accessible))
                    {
                        return accessible.Contains(gp);
                    }

                    return false;
                }))
            {
                return false;
            }
        }

        return true;

    }
    public static string ShowPermissionsCommand(CommandContext context)
    {
        var platformId = context.GetPlatformId() ?? throw new Exception("ShowPermissions require non null platformId");

        if (!Plugin.Instance.API.GetPermissions(platformId, out var permissions))
        {
            throw new Exception($"Couldn't retrieve permissions for {platformId}");
        }

        StringBuilder permissionsBuilder = new();
        var delimiter = Localized.PermissionsDelimiter(platformId);
        List<string> formatted = new();
        foreach (var p in permissions)
        {
            string str;
            string key = $"PermissionName_{p.Key}";
            string permissionName = Localized.Get(platformId, key);
            if (permissionName == key) permissionName = p.Key;
            if (p.Value != null)
            {
                TimeSpan timespan = (p.Value - DateTime.UtcNow).GetValueOrDefault();
                string time = Localized.TimeFormat(platformId)
                    .Replace("{time}", Localization.Plugin.Instance.API.GetLocalizedTimeSpan(platformId, timespan));

                str = Localized.PermissionFiniteFormat(platformId).Replace("{name}", permissionName).Replace("{time}", time);
            }
            else
            {
                str = Localized.PermissionFormat(platformId).Replace("{name}", permissionName);
            }
            formatted.Add(str);
        }

        permissionsBuilder.AppendJoin(delimiter, formatted);
        return Localized.ShowPermissionsFormat(platformId).Replace("{permissions}", permissionsBuilder.ToString());
    }
    public static string AddPermissionCommand(CommandContext context, ulong platformId, string permission, TimeSpan? time = null)
    {
        ulong callerId = context.GetPlatformId() ?? 0;
        var api = (PermissionsAPI)Plugin.Instance.API;
        
        if (!api.Config.Permissions.Contains(permission))
        {
            return $"No permission with name \"{permission}\" defined in config.json";
        }
        
        if (!api.HasAccess(callerId, permission))
        {
            return $"No access to {permission}";
        }
        
        if (Plugin.Instance.API.AddPermission(platformId, permission, time))
        {
            return "Success";
        }

        return "Failed";
    }
    public static string SetPermissionCommand(CommandContext context, ulong platformId, string permission, TimeSpan? time = null)
    {
        ulong callerId = context.GetPlatformId() ?? 0;
        var api = (PermissionsAPI)Plugin.Instance.API;
        
        if (!api.Config.Permissions.Contains(permission))
        {
            return $"No permission with name \"{permission}\" defined in config.json";
        }
        
        if (!api.HasAccess(callerId, permission))
        {
            return $"No access to {permission}";
        }
        
        if (Plugin.Instance.API.SetPermission(platformId, permission, time))
        {
            return "Success";
        }

        return "Failed";
    }
    public static string RemovePermissionCommand(CommandContext context, ulong platformId, string permission)
    {
        ulong callerId = context.GetPlatformId() ?? 0;
        var api = (PermissionsAPI)Plugin.Instance.API;
        
        if (!api.Config.Permissions.Contains(permission))
        {
            return $"No permission with name \"{permission}\" defined in config.json";
        }
        
        if (!api.HasAccess(callerId, permission))
        {
            return $"No access to {permission}";
        }
        if (Plugin.Instance.API.RemovePermission(platformId, permission))
        {
            return "Success";
        }

        return "Failed";
    }
    public static string AddToGroupCommand(CommandContext context, ulong platformId, string group, TimeSpan? time = null)
    {
        ulong callerId = context.GetPlatformId() ?? 0;
        
        var api = (PermissionsAPI)Plugin.Instance.API;
        if (!api.Config.Groups.ContainsKey(group))
        {
            return $"No group with name \"{group}\" defined in config.json";
        }
        
        if (!api.HasAccessToGroup(callerId, group))
        {
            return $"You don't have access to group \"{group}\"";
        }
        if (Plugin.Instance.API.AddToGroup(platformId, group, time))
        {
            return "Success";
        }

        return "Failed";
    }
    public static string RemoveFromGroupCommand(CommandContext context, ulong platformId, string group)
    {
        ulong callerId = context.GetPlatformId() ?? 0;
        var api = (PermissionsAPI)Plugin.Instance.API;
        if (!api.Config.Groups.ContainsKey(group))
        {
            return $"No group with name \"{group}\" defined in config.json";
        }
        if (!api.HasAccessToGroup(callerId, group))
        {
            return $"You don't have access to group \"{group}\"";
        }
        if (Plugin.Instance.API.RemoveFromGroup(platformId, group))
        {
            return "Success";
        }

        return "Failed";
    }
    public static string RefreshPermissionsCommand(CommandContext context)
    {
        ((PermissionsAPI)Plugin.Instance.API).Registry.RefreshPermissions();
        return "Success";
    }

    public static string ReloadCommand(CommandContext context)
    {
        var newConfig = Core.Config.Load();
        var permissionsAPI = ((PermissionsAPI)Permissions.Plugin.Instance.API);
        permissionsAPI.Config = newConfig;
        permissionsAPI.UnregisterCommands();
        permissionsAPI.RegisterCommands();
        return "Success";

    }
    public void OnAdminAuth(ulong platformId)
    {
        if (Config.AdminAuthGroup == null) return;
        Log.Warning($"[Permissions] OnAdminAuth {platformId}");
        AddToGroup(platformId, Config.AdminAuthGroup);
    }
}