using System;
using HarmonyLib;

namespace JianZhu;

/// <summary>
/// 大通铺(101007)多人床位补丁。
/// 原版床位是"家庭单位"：OnNpcEnter 是入住闸门（家庭/容量校验，实测最多 2 大人+1 小孩），
/// IsNoNpcInHouse（member_list.Count == 0）决定床位是否进 HousingHelper.empty_bed_list 分配池
/// ——所以普通床住满一个家庭后就不再进池。
/// 大通铺按数据表 effect_value(=10，remark"可睡人数")放开：未满员持续进池 + 强制收人。
/// 两处补丁都只对 stuff_id==101007 生效，其他床走原逻辑。
/// </summary>
internal static class BedPatches
{
    public const int DormBedId = 101007;
    private const int FallbackCapacity = 10;

    internal static bool IsDorm(FacilityBed bed) => bed != null && bed.stuff_id == DormBedId;

    internal static int CapacityOf(FacilityBed bed)
    {
        try
        {
            var info = bed.facility_stuff_info;
            if (info != null && info.effect_value_int > 0) return info.effect_value_int;
        }
        catch { /* 字段访问失败按兜底容量 */ }
        return FallbackCapacity;
    }

    internal static int MemberCount(FacilityBed bed)
    {
        try { return bed.member_list?.Count ?? 0; }
        catch { return 0; }
    }
}

/// <summary>入住闸门：大通铺容量内（且 npc 存活）强制收下，绕过家庭/数量限制。
/// ⚠ 原版 OnNpcEnter 负责 member_list.Add 等簿记，prefix 必须自己补记成员，
/// 否则 NPC 拿到 facility_bed 但床名册恒 0（0.3.0 实测教训）。</summary>
[HarmonyPatch(typeof(FacilityBed), nameof(FacilityBed.OnNpcEnter))]
internal static class BedOnNpcEnterPatch
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
    static bool Prefix(FacilityBed __instance, Npc npc, ref bool __result)
    {
        if (!BedPatches.IsDorm(__instance)) return true; // 非大通铺走原逻辑

        JianZhu.Plugin.LogInfo(
            $"[JianZhu] OnNpcEnter: bed={__instance.guid} npc={(npc != null ? npc.npc_name : "null")} " +
            $"count={BedPatches.MemberCount(__instance)}");

        if (npc == null || npc.is_dead
            || BedPatches.MemberCount(__instance) >= BedPatches.CapacityOf(__instance))
        {
            __result = false;
            return false;
        }

        try
        {
            __instance.member_list.Add(npc);
            __instance.UpdateSprite(); // 枕头等"已分配"视觉状态
        }
        catch (Exception ex)
        {
            JianZhu.Plugin.LogError($"[JianZhu] 大通铺收人簿记失败: {ex.Message}");
        }
        __result = true;
        return false;
    }
}

/// <summary>空床判定：大通铺改为"未满员即视为可分配"，让空床池持续接纳它。</summary>
[HarmonyPatch(typeof(FacilityBed), nameof(FacilityBed.IsNoNpcInHouse))]
internal static class BedIsNoNpcInHousePatch
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
    static bool Prefix(FacilityBed __instance, ref bool __result)
    {
        if (!BedPatches.IsDorm(__instance)) return true;
        __result = BedPatches.MemberCount(__instance) < BedPatches.CapacityOf(__instance);
        return false;
    }
}
