using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using BepInEx;
using Il2CppSystem.Linq;
using Permissions.Utils;
namespace Permissions.Core;
public class Config
{
    public HashSet<string> Permissions;
    public Dictionary<string, HashSet<string>> Groups;
    public string? AdminAuthGroup = null;
    public JObject? ShowPermissionsCommand;
    public JObject? AddPermissionCommand;
    public JObject? SetPermissionCommand;
    public JObject? RemovePermissionCommand;
    public JObject? AddToGroupCommand;
    public JObject? RemoveFromGroupCommand;
    public JObject? RefreshPermissionsCommand;
    public JObject? ReloadCommand;
    public Dictionary<string, HashSet<string>> PermissionAccess;
    public Config(JObject json)
    {
        Permissions = new();
        Groups = new();
        PermissionAccess = new();
        if (json.TryGetValue("Commands", out var _commands))
        {
            var commands = _commands.Cast<JObject>();
            if (commands.TryGetValue("ShowPermissions", out var showPermissionsCommand))
            {
                ShowPermissionsCommand = showPermissionsCommand.Cast<JObject>();
            }
            if (commands.TryGetValue("AddPermission", out var addPermissionCommand))
            {
                AddPermissionCommand = addPermissionCommand.Cast<JObject>();
            } 
            if (commands.TryGetValue("SetPermission", out var setPermissionCommand))
            {
                SetPermissionCommand = setPermissionCommand.Cast<JObject>();
            }
            if (commands.TryGetValue("RemovePermission", out var removePermissionCommand))
            {
                RemovePermissionCommand = removePermissionCommand.Cast<JObject>();
            }
            if (commands.TryGetValue("AddToGroup", out var addToGroupCommand))
            {
                AddToGroupCommand = addToGroupCommand.Cast<JObject>();
            }
            if (commands.TryGetValue("RemoveFromGroup", out var removeFromGroup))
            {
                RemoveFromGroupCommand = removeFromGroup.Cast<JObject>();
            } 
            if (commands.TryGetValue("RefreshPermissions", out var refreshPermissions))
            {
                RefreshPermissionsCommand = refreshPermissions.Cast<JObject>();
            }
            if (commands.TryGetValue("Reload", out var reload))
            {
                ReloadCommand = reload.Cast<JObject>();
            }
        }

        var permissions = json["Permissions"].Cast<JArray>().Values<string>().ToArray();
        for (int i = 0; i < permissions.Count; ++i)
        {
            Permissions.Add(permissions[i]);
        }
        
        var groups = json["Groups"].Cast<JObject>().Properties().ToArray();
        for (int i = 0; i < groups.Count; ++i)
        {
            var group = groups[i].Name;
            var perms = groups[i].Value.Cast<JArray>().Values<string>().ToArray();
            Groups[group] = new();
            for (int x = 0; x < perms.Count; ++x)
            {
                if (!Permissions.Contains(perms[x]))
                {
                    throw new Exception($"Group {group} has permission {perms[x]} that doesn't exist in Permissions");
                }

                Groups[group].Add(perms[x]);
            }
            
        }
        if (json.TryGetValue("AdminAuthGroup", out var adminAuthGroup))
        {
            string str = (string)adminAuthGroup;
            if (!Groups.ContainsKey(str))
            {
                Log.Error($"AdminAuthGroup points to invalid group {str}. AdminAuth user will not receive any groups.");
                return;
            }

            AdminAuthGroup = str;
        }

        if (json.TryGetValue("PermissionAccess", out var permissionAccess))
        {
            var props = permissionAccess.Cast<JObject>().Properties().ToArray();
            for (int i = 0; i < props.Count; ++i)
            {
                var permission = props[i];
                if (!Permissions.Contains(permission.Name))
                {
                    throw new Exception($"[PermissionAccess] Permission is not registered {permission.Name}");
                }

                var permissionsStr = (string)permission.Value;
                var _permissions = permissionsStr.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                bool addAll = _permissions.Any(x => x.Equals("*"));
                PermissionAccess[permission.Name] = new();
                if (addAll)
                {                
                    PermissionAccess[permission.Name] = new(Permissions);
                }
                foreach (var p in _permissions.Where(x=>!x.Equals("*")))
                {
                    if (!Permissions.Contains(p))
                    {
                        throw new Exception(
                            $"[PermissionAccess] Contains invalid permission \"{p}\" for \"{permission.Name}\"");
                    }

                    // Exclude as we already added all
                    if (addAll)
                    {
                        PermissionAccess[permission.Name].Remove(p);
                    }
                    else
                    {
                        PermissionAccess[permission.Name].Add(p);
                    }
                }
            }
        }
    }
    
    public static Config Load()
    {
        var configPath = $"{Paths.ConfigPath}/{MyPluginInfo.PLUGIN_GUID}/config.json";
        string configText;
        try
        {
            configText = File.ReadAllText(configPath);
        }
        catch (Exception)
        {
            Log.Error($"Couldn't read config at {configPath}");
            throw;
        }

        JObject json;
        try
        {
            json = JObject.Parse(configText);
        }
        catch (Exception)
        {
            Log.Error("Couldn't parse config");
            throw;
        }

        return new Config(json);
    }
}