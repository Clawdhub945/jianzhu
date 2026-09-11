using System;
using UnityEngine;

namespace JianZhu;

/// <summary>
/// IL2CPP 注入的 MonoBehaviour：主线程驱动（快捷键 + IMGUI 占位面板）。
/// 全选建造的具体功能需求后定，此面板仅用于打通 IL2CPP 组件注入与 UI 渲染路径。
/// </summary>
public class JianZhuComponent : MonoBehaviour
{
    internal static JianZhuComponent? Instance;

    private bool _showPanel;
    private const KeyCode ToggleKey = KeyCode.F9;

    public JianZhuComponent(IntPtr ptr) : base(ptr)
    {
        Instance = this;
    }

    private void Update()
    {
        try
        {
            if (Input.GetKeyDown(ToggleKey))
            {
                _showPanel = !_showPanel;
                Plugin.LogInfo($"[JianZhu] 面板切换为 {_showPanel}");
            }
        }
        catch (Exception ex)
        {
            Plugin.LogError($"[JianZhu] Update 异常: {ex}");
        }
    }

    // 最保守的 IMGUI 画法：固定 Rect + GUI.Box/GUI.Button，不用 GUI.Window 委托
    //（IL2CPP 注入下 WindowFunction 委托编组未验证过，骨架期不冒险；后续功能定稿再升级）
    private void OnGUI()
    {
        if (!_showPanel) return;

        var rect = new Rect(40f, 40f, 320f, 180f);
        GUI.Box(rect, "全选建造（骨架 0.1.0）");

        var inner = new Rect(rect.x + 12f, rect.y + 32f, rect.width - 24f, rect.height - 44f);
        GUILayout.BeginArea(inner);
        GUILayout.Label("全选 UI 占位：具体功能需求后定。");
        GUILayout.Label("按 F9 开关本面板。");
        if (GUILayout.Button("关闭"))
        {
            _showPanel = false;
        }
        GUILayout.EndArea();
    }
}
