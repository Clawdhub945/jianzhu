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
    public const string PLUGIN_VERSION = "0.6.1";

    internal static ManualLogSource Logger = null!;

    private HarmonyLib.Harmony? _harmony;
    internal static BepInEx.Configuration.ConfigFile? DormConfigFile;
    internal static BepInEx.Configuration.ConfigEntry<int>? CapacityEntry;

    public override void Load()
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }

        Logger = Log;

        // 大通铺每床容量配置：自动生成 BepInEx/config/claude.jianzhu.cfg，
        // 改文件后由服务循环定期 Config.Reload() 热生效，范围钳制 1-25
        try
        {
            DormConfigFile = Config;
            CapacityEntry = Config.Bind("大通铺", "最大居住小人数", 10,
                new BepInEx.Configuration.ConfigDescription(
                    "修改数值即可调整游戏内大通铺最大居住小人数（每张床）。默认 10，最小 1，最大 25。",
                    new BepInEx.Configuration.AcceptableValueRange<int>(1, 25)));
            BedPatches.CapacityEntry = CapacityEntry;
            CapacityEntry.SettingChanged += (_, _) =>
                LogInfo($"[JianZhu] 容量配置变更 → 每床 {CapacityEntry.Value} 人");

            // 猪人(6)/蚁人(1)/鼠人(2) 睡床开关：默认禁止，cfg 改 true 允许
            var allowSpecial = Config.Bind("大通铺", "允许猪人蚁人鼠人睡床", false,
                new BepInEx.Configuration.ConfigDescription(
                    "默认 false=禁止猪人/蚁人/鼠人这三个种族睡大通铺（在床上的会被请走）；改为 true 允许居住。"));
            BedPatches.AllowPigAntRatEntry = allowSpecial;
            allowSpecial.SettingChanged += (_, _) =>
                LogInfo($"[JianZhu] 种族限制变更 → 猪人/蚁人/鼠人睡床 {(allowSpecial.Value ? "允许" : "禁止")}");

            LogInfo($"[JianZhu] 容量配置: 每床 {CapacityEntry.Value} 人, 猪人/蚁人/鼠人睡床 {(allowSpecial.Value ? "允许" : "禁止")} (BepInEx/config/claude.jianzhu.cfg)");
        }
        catch (Exception ex) { LogError($"[JianZhu] 容量配置绑定失败: {ex}"); }

        // 失焦时 Unity Update 不跑 → 快捷键/主线程任务全部饿死，与 ChestEditor 同款处理
        try { Application.runInBackground = true; }
        catch (Exception ex) { LogError($"[JianZhu] 设置 runInBackground 失败: {ex.Message}"); }

        // 大通铺多人床位补丁（OnNpcEnter / IsNoNpcInHouse，仅 101007 生效）
        try
        {
            _harmony = new HarmonyLib.Harmony(PLUGIN_GUID);
            _harmony.PatchAll();
            LogInfo("[JianZhu] 床位补丁已挂：OnNpcEnter + IsNoNpcInHouse（仅 101007）");
        }
        catch (Exception ex) { LogError($"[JianZhu] 挂床位补丁失败: {ex}"); }

        ClassInjector.RegisterTypeInIl2Cpp<JianZhuComponent>();
        var go = new GameObject("JianZhuRoot");
        GameObject.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<JianZhuComponent>();

        LogInfo($"{PLUGIN_NAME} v{PLUGIN_VERSION} 已加载！按 F9 开关大通铺状态面板");
    }

    internal static void LogInfo(string msg) => Logger.LogInfo(msg);
    internal static void LogWarning(string msg) => Logger.LogWarning(msg);
    internal static void LogError(string msg) => Logger.LogError(msg);
}
