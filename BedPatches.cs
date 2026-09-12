using System;
using System.Collections.Generic;
using System.Reflection;
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

    /// <summary>每床容量：由 config/claude.jianzhu.cfg 的「最大居住小人数」驱动（1-25 钳制）</summary>
    internal static BepInEx.Configuration.ConfigEntry<int>? CapacityEntry;

    /// <summary>猪人(6)/蚁人(1)/鼠人(2) 睡床开关：默认禁止，cfg 改 true 允许</summary>
    internal static BepInEx.Configuration.ConfigEntry<bool>? AllowPigAntRatEntry;
    private static readonly HashSet<int> ForbiddenRaces = new() { 6, 1, 2 };

    internal static bool IsRaceForbidden(int raceId)
    {
        if (AllowPigAntRatEntry != null && AllowPigAntRatEntry.Value) return false;
        return ForbiddenRaces.Contains(raceId);
    }

    internal static bool IsDorm(FacilityBed bed) => bed != null && bed.stuff_id == DormBedId;

    internal static int CapacityOf(FacilityBed bed)
    {
        try
        {
            if (CapacityEntry != null) return Math.Clamp(CapacityEntry.Value, 1, 25);
        }
        catch { }
        return FallbackCapacity;
    }

    internal static int MemberCount(FacilityBed bed)
    {
        try { return bed.member_list?.Count ?? 0; }
        catch { return 0; }
    }

    /// <summary>同族限制：床内已有成员时，与首成员种族不同 = 不匹配（空床无限制）</summary>
    internal static bool RaceMismatch(FacilityBed bed, Npc npc)
    {
        try
        {
            var members = bed.member_list;
            if (members == null || members.Count == 0) return false;
            var first = members[0];
            return first != null && first.race_id != npc.race_id;
        }
        catch { return false; }
    }

    /// <summary>名册成员判定（interop == 按底层指针比较）</summary>
    internal static bool IsMember(FacilityBed bed, Npc npc)
    {
        try
        {
            var ms = bed.member_list;
            if (ms == null) return false;
            for (int i = 0; i < ms.Count; i++)
                if (ms[i] == npc) return true;
            return false;
        }
        catch { return false; }
    }

    internal static bool IsTraveller(Npc npc)
    {
        try
        {
            int t = npc._npc_type;
            return t == -10 || t == -12 || t == -13; // 旅客/旅客刺客/旅客赏金猎人
        }
        catch { return false; }
    }

    /// <summary>普通居民类型白名单：儿童(-2)特批 + 士兵放行（游戏设计里士兵由兵营
    /// 绑床流程 HousingHelper.MoveNpcToThisBedBindToWorkFacility 分配合法床位）+
    /// 游戏谓词 IsPeople_NotElf_NotNoble_NotSoldier_NotPrisoner（自动排除贵族/领主/
    /// 商队/-11 等特殊负数类型）。再显式排除婴儿(-3)/学生(-1)。</summary>
    internal static bool IsAllowedResidentType(Npc npc)
    {
        try
        {
            int t = npc._npc_type;
            if (t == -2) return true;             // 儿童允许与成年人同住
            if (t == -3 || t == -1) return false; // 婴儿/学生
            if (NpcType.IsSoldier(t)) return true; // 士兵(1001-1900)：兵营绑床的合法住户
            return NpcType.IsPeople_NotElf_NotNoble_NotSoldier_NotPrisoner(t);
        }
        catch { return false; }
    }

    /// <summary>床位准入总校验（闸门与直接收容共用）：
    /// 存活、非精灵/石头人、同族、未满员、旅客专属分流、类型白名单</summary>
    internal static bool CanAccept(FacilityBed bed, Npc npc, out string reason)
    {
        reason = "";
        try
        {
            if (npc == null || npc.is_dead) { reason = "死亡"; return false; }
            if (npc.IsSpriteOrStoneMan()) { reason = "精灵/石头人"; return false; }
            if (IsRaceForbidden(npc.race_id)) { reason = "猪人/蚁人/鼠人禁止睡床"; return false; }
            if (RaceMismatch(bed, npc)) { reason = "异族"; return false; }
            if (MemberCount(bed) >= CapacityOf(bed)) { reason = "满员"; return false; }
            bool travellerOnly = bed.IsForTravellerOnly;
            if (travellerOnly)
            {
                if (!IsTraveller(npc)) { reason = "旅客专属床不收居民"; return false; }
            }
            else
            {
                if (IsTraveller(npc)) { reason = "非旅客专属床不收旅客"; return false; }
                if (!IsAllowedResidentType(npc)) { reason = $"类型不允许(t{npc._npc_type})"; return false; }
            }
            return true;
        }
        catch { reason = "异常"; return false; }
    }
}

/// <summary>原版 TryClear* 系列（夫妻分床清"非育龄成员" TryClearNotCouplesChildbearingAge、
/// 清儿童/清学生、TryClearMember 等）只从 member_list 删人、不清 NPC 侧 house_facility_guid
/// ——原版床只住一个家庭无所谓，多人大通铺被它清一次就产生"guid 指床但名册无人"的
/// 脱钩孤儿（0.4.x 实测 15 个）。对大通铺跳过全部 TryClear* 清理；普通床照常。</summary>
[HarmonyPatch]
internal static class BedTryClearMemberPatch
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
    static IEnumerable<MethodBase> TargetMethods()
    {
        var found = new List<MethodBase>();
        foreach (var m in AccessTools.GetDeclaredMethods(typeof(FacilityBed)))
            if (m.Name.StartsWith("TryClear"))
                found.Add(m);
        JianZhu.Plugin.LogInfo($"[JianZhu] TryClear* 清理补丁命中 {found.Count} 个方法（大通铺跳过）");
        return found;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
    static bool Prefix(FacilityBed __instance) => !BedPatches.IsDorm(__instance);
}

/// <summary>入住闸门：大通铺容量内强制收下，绕过家庭/数量限制。儿童(-2)与成年人
/// 同住允许；精灵/石头人/异族拒绝。⚠ 原版 OnNpcEnter 负责 member_list.Add 等簿记，
/// prefix 必须自己补记成员，否则 NPC 拿到 facility_bed 但床名册恒 0（0.3.0 实测教训）。</summary>
[HarmonyPatch(typeof(FacilityBed), nameof(FacilityBed.OnNpcEnter))]
internal static class BedOnNpcEnterPatch
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
    static bool Prefix(FacilityBed __instance, Npc npc, ref bool __result)
    {
        if (!BedPatches.IsDorm(__instance)) return true; // 非大通铺走原逻辑

        if (!BedPatches.CanAccept(__instance, npc, out string reason))
        {
            try
            {
                string name = npc != null ? npc.npc_name : "null";
                JianZhu.Plugin.LogV($"[JianZhu] 拒绝「{name}」入住大通铺: {reason}");
            }
            catch { }
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
