using System.Runtime.CompilerServices;

namespace Permissions.Resources;

public static class Localized
{
    public static string ShowPermissionsFormat(ulong p) => Get(p);
    public static string PermissionsDelimiter(ulong p) => Get(p);
    public static string PermissionFiniteFormat(ulong p) => Get(p);
    public static string PermissionFormat(ulong p) => Get(p);
    public static string TimeFormat(ulong p) => Get(p);
    
    public static string Get(ulong platformId, [CallerMemberName] string key = "")
    {
        return Localization.Plugin.Instance.API.GetLocalizedString(platformId, MyPluginInfo.PLUGIN_GUID, key);
    }
}