using System;
using System.IO;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Permissions.API;
using Permissions.Core;

namespace Permissions;
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("com.agitoreiken.database", BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("com.agitoreiken.commands", BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("com.agitoreiken.localization", BepInDependency.DependencyFlags.HardDependency)]
public class Plugin : BasePlugin
{
    public static Plugin Instance;
    private Harmony _harmony;
    public IPermissionsAPI API;
    public override void Load()
    {
        Instance = this;
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} version {MyPluginInfo.PLUGIN_VERSION} is loading...");

        API = new PermissionsAPI();
        Localization.Plugin.Instance.API.RegisterPlugin(MyPluginInfo.PLUGIN_GUID,
            $"{Paths.ConfigPath}/{MyPluginInfo.PLUGIN_GUID}");
        _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        _harmony.PatchAll(System.Reflection.Assembly.GetExecutingAssembly());

        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

    }

    public override bool Unload()
    {
        return true;
    } 
}
