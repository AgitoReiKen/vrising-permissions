using System;
using System.Collections.Generic;

namespace Permissions.API;

public interface IPermissionsAPI
{
    public bool AddPermission(ulong platformId, string permissionId, TimeSpan? time);
    public bool SetPermission(ulong platformId, string permissionId, TimeSpan? time);
    public bool RemovePermission(ulong platformId, string permissionId);
    public bool HasPermission(ulong platformId, string permissionId);
    public bool HasPermissions(ulong platformId, params string[] permissionIds);
    public bool HasAnyPermission(ulong platformId, params string[] permissionIds);
    public bool GetPermissions(ulong platformId, out Dictionary<string, DateTime?> permissions);

    public bool AddToGroup(ulong platformId, string groupId, TimeSpan? time);
    public bool RemoveFromGroup(ulong platformId, string groupId);
    public bool IsInGroup(ulong platformId, string groupId);
    public bool IsInGroups(ulong platformId, params string[] groupIds);

    public HashSet<string> GetRegisteredPermissions();
    public Dictionary<string, HashSet<string>> GetRegisteredGroups();
}