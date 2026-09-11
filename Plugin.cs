using System;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace JianZhu;

[BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    public const string PLUGIN_GUID = "claude.jianzhu";
    public const string PLUGIN_NAME = "JianZhu";
    public const string PLUGIN_VERSION = "0.1.0";

    internal static ManualLogSource Logger = null!;

    public override void Load()
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }

        Logger = Log;

        // 失焦时 Unity Update 不跑 → 快捷键/主线程任务全部饿死，与 ChestEditor 同款处理
        try { Application.runInBackground = true; }
        catch (Exception ex) { LogError($"[JianZhu] 设置 runInBackground 失败: {ex.Message}"); }

        ClassInjector.RegisterTypeInIl2Cpp<JianZhuComponent>();
        var go = new GameObject("JianZhuRoot");
        GameObject.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<JianZhuComponent>();

        LogInfo($"{PLUGIN_NAME} v{PLUGIN_VERSION} 已加载！按 F9 开关全选建造面板");
    }

    internal static void LogInfo(string msg) => Logger.LogInfo(msg);
    internal static void LogWarning(string msg) => Logger.LogWarning(msg);
    internal static void LogError(string msg) => Logger.LogError(msg);
}
