using System;
using System.Collections.Generic;
using UnityEngine;

namespace JianZhu;

/// <summary>
/// IL2CPP 注入的 MonoBehaviour：主线程驱动（快捷键 + 诊断轮询 + IMGUI 状态面板）。
/// 大通铺本体是纯数据 Def 注入（Defs/stuff.json + build.json，走游戏官方 Def 通道），
/// 本组件只负责诊断：确认游戏读取的 Defs 目录、确认 101007 进入 stuff_dic/build_dic。
/// </summary>
public class JianZhuComponent : MonoBehaviour
{
    internal static JianZhuComponent? Instance;

    private bool _showPanel;
    private const KeyCode ToggleKey = KeyCode.F9;

    private const int BedId = 101007;

    private bool _pollStarted;
    private float _nextPollAt;
    private int _pollCount;
    private const float PollInterval = 5f;
    private const int MaxPolls = 36; // 36 次 × 5 秒 = 最多轮询 3 分钟

    // 进存档后的一次性菜单 dump（建造菜单缺大通铺的取证）
    private bool _menuDumped;
    private float _nextMenuDumpAt = 20f; // 读档一般在启动后几十秒，20s 起试

    // 最近一次轮询结果（面板展示用）
    private string _pluginsDir = "未获取";
    private bool? _inStuffDic;
    private bool? _inBuildDic;
    private int _stuffDicCount = -1;
    private string _stuffName = "";

    public JianZhuComponent(IntPtr ptr) : base(ptr)
    {
        Instance = this;
    }

    private void Start()
    {
        // 游戏官方 Def 通道根目录：ModsHelper.GetPluginsDir() = <游戏根>/BepInEx/plugins
        // 大通铺 Defs 部署在 plugins/JianZhu/Defs/{stuff,build}.json，由 D.LoadData → LoadDefsOfMod 读取
        try
        {
            _pluginsDir = ModsHelper.GetPluginsDir();
            Plugin.LogInfo($"[JianZhu] 游戏 Defs 根目录 = {_pluginsDir}");
        }
        catch (Exception ex)
        {
            Plugin.LogError($"[JianZhu] 获取 GetPluginsDir 失败: {ex.Message}");
        }

        _pollStarted = true;
        _nextPollAt = Time.time + 5f;
    }

    private void Update()
    {
        try
        {
            if (Input.GetKeyDown(ToggleKey))
            {
                _showPanel = !_showPanel;
            }

            if (Time.time >= _nextMenuDumpAt)
            {
                _nextMenuDumpAt = Time.time + 10f;
                TryDumpBuildMenu();
            }

            if (!_pollStarted || _inStuffDic == true && _inBuildDic == true) return;
            if (Time.time < _nextPollAt) return;
            _nextPollAt = Time.time + PollInterval;
            _pollCount++;
            if (_pollCount > MaxPolls)
            {
                _pollStarted = false;
                Plugin.LogWarning("[JianZhu] 轮询超时：3 分钟内 D.LoadData 未包含 101007（可能未进主场景或 Def 未生效）");
                return;
            }

            PollData();
        }
        catch (Exception ex)
        {
            Plugin.LogError($"[JianZhu] Update 异常: {ex}");
        }
    }

    /// <summary>
    /// 建造菜单取证：dump 子类型 101 的 id 列表 + 场景内全部 BuildMenuGroup 实际装配的物品。
    /// 目的：101007 在 stuff_dic/build_dic 里但建造菜单不显示，定位缺在哪一环。
    /// </summary>
    private void TryDumpBuildMenu()
    {
        if (_menuDumped) return;

        var groups = UnityEngine.Object.FindObjectsOfType<BuildMenuGroup>();
        if (groups == null || groups.Length == 0) return; // 还没进存档/菜单未初始化

        int totalItems = 0;
        foreach (var g in groups)
        {
            if (g == null || g.items == null) continue;
            totalItems += g.items.Count;
        }
        if (totalItems == 0) return; // 菜单组还没 SetInfo

        _menuDumped = true;

        // 1) 子类型 101 的注册表清单 + D.facility_list + C.build_menu_group_order
        try
        {
            var d = D.Ins;
            var subDic = d.stuff_id_list_of_sub_type_dic;
            if (subDic != null && subDic.ContainsKey(101))
            {
                var list = subDic[101];
                var ids = new List<int>();
                for (int i = 0; i < list.Count; i++) ids.Add(list[i]);
                Plugin.LogInfo($"[JianZhu] stuff_id_list_of_sub_type_dic[101] = [{string.Join(", ", ids)}]");
            }
            else
            {
                Plugin.LogWarning("[JianZhu] stuff_id_list_of_sub_type_dic 无 101 键");
            }

            var order = C.build_menu_group_order;
            if (order != null)
            {
                var ids = new List<int>();
                for (int i = 0; i < order.Count; i++) ids.Add(order[i]);
                Plugin.LogInfo($"[JianZhu] C.build_menu_group_order = [{string.Join(", ", ids)}]");
            }
        }
        catch (Exception ex)
        {
            Plugin.LogError($"[JianZhu] dump 子类型表失败: {ex.Message}");
        }

        // 2) 场景内 BuildMenuGroup 实际装配的物品（含 null 项的 GO 名/精灵名）
        foreach (var g in groups)
        {
            if (g == null || g.items == null || g.items.Count == 0) continue;
            var names = new List<string>();
            foreach (var item in g.items)
            {
                if (item == null) continue;
                if (item.stuff_info == null)
                {
                    var sprite = item.img_icon != null ? item.img_icon.sprite : null;
                    names.Add($"NULL({item.name}|{item.gameObject.name}|sp={sprite?.name ?? "-"})");
                }
                else
                {
                    names.Add($"{item.stuff_info.stuff_id}:{item.stuff_info.stuff_name}");
                }
            }
            Plugin.LogInfo($"[JianZhu] BuildMenuGroup '{g.name}' items({g.items.Count}) = [{string.Join("; ", names)}]");
        }
    }

    private void PollData()
    {        var d = D.Ins;
        if (d == null) return;

        var stuffDic = d.stuff_dic;
        if (stuffDic != null)
        {
            _stuffDicCount = stuffDic.Count;
            _inStuffDic = stuffDic.ContainsKey(BedId);
            if (_inStuffDic == true && string.IsNullOrEmpty(_stuffName))
            {
                var info = stuffDic[BedId];
                if (info != null)
                {
                    _stuffName = info.stuff_name;
                    Plugin.LogInfo(
                        $"[JianZhu] stuff_dic[{BedId}] = {_stuffName}, effect_value={info.effect_value}, " +
                        $"prefab={info.prefab}, img={info.stuff_img_on_map}, cell 依赖 build_dic");
                }
            }
        }

        var buildDic = d.build_dic;
        if (buildDic != null)
        {
            _inBuildDic = buildDic.ContainsKey(BedId);
        }

        if (_inStuffDic == true && _inBuildDic == true)
        {
            Plugin.LogInfo($"[JianZhu] ✓ 101007 已进表：stuff_dic({_stuffDicCount} 项) + build_dic 均包含，Def 通道注入成功");
            _pollStarted = false;
        }
    }

    // 最保守的 IMGUI 画法：固定 Rect + GUI.Box/GUI.Button，不用 GUI.Window 委托
    //（IL2CPP 注入下 WindowFunction 委托编组未验证过，骨架期不冒险）
    private void OnGUI()
    {
        if (!_showPanel) return;

        var rect = new Rect(40f, 40f, 420f, 220f);
        GUI.Box(rect, "JianZhu 大通铺 · 状态（骨架 0.2.0）");

        var inner = new Rect(rect.x + 12f, rect.y + 32f, rect.width - 24f, rect.height - 44f);
        GUILayout.BeginArea(inner);
        GUILayout.Label($"Defs 根目录: {_pluginsDir}");
        GUILayout.Label($"stuff_dic 项数: {(_stuffDicCount >= 0 ? _stuffDicCount.ToString() : "未加载")}");
        GUILayout.Label($"101007 in stuff_dic: {Describe(_inStuffDic)}   in build_dic: {Describe(_inBuildDic)}");
        if (!string.IsNullOrEmpty(_stuffName))
        {
            GUILayout.Label($"名字: {_stuffName}");
        }
        GUILayout.Space(6);
        GUILayout.Label("在建造菜单「家具」分类应出现「大通铺」（贴图同小床）");
        if (GUILayout.Button("关闭 (F9)"))
        {
            _showPanel = false;
        }
        GUILayout.EndArea();
    }

    private static string Describe(bool? v) => v switch
    {
        null => "未知",
        true => "✓ 有",
        false => "✗ 无",
    };
}
