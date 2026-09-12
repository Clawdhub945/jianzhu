using System;
using System.Collections.Generic;
using UnityEngine;

namespace JianZhu;

/// <summary>
/// IL2CPP 注入的 MonoBehaviour：主线程驱动（床位服务轮询 + Def 注入确认）。
/// 大通铺本体是纯数据 Def 注入（Defs/stuff.json + build.json，走游戏官方 Def 通道），
/// 本组件负责：空床池兜底、无房居民/旅客直接收容、腾床合并、脱钩修复。
/// </summary>
public class JianZhuComponent : MonoBehaviour
{
    internal static JianZhuComponent? Instance;

    private const int BedId = 101007;

    private bool _pollStarted;
    private float _nextPollAt;
    private int _pollCount;
    private const float PollInterval = 5f;
    private const int MaxPolls = 36; // 36 次 × 5 秒 = 最多轮询 3 分钟

    // 大通铺床位服务（每 1.5s）：空床池兜底 + 无房居民/旅客直接收容
    private float _nextPoolAt;
    private float _nextRosterAt;

    // 最近一次轮询结果（日志用）
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
        // 大通铺 Defs 部署在 plugins/<mod>/Defs/{stuff,build}.json，由 D.LoadData → LoadDefsOfMod 读取
        try
        {
            Plugin.LogV($"[JianZhu] 游戏 Defs 根目录 = {ModsHelper.GetPluginsDir()}");
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
            if (Time.time >= _nextPoolAt)
            {
                _nextPoolAt = Time.time + 1.5f;
                // cfg 热生效：改 BepInEx/config/claude.jianzhu.cfg 后最多 1.5s 生效
                try { Plugin.DormConfigFile?.Reload(); } catch { }
                RefreshDormPool();
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
    /// 大通铺收容：每 1.5s 一轮。
    /// ① 空床池兜底：未满员的大通铺保证在 HousingHelper.empty_bed_list 池尾（优先被分配）。
    /// ② 直接分配：实测居民选床不走该池（无房者常被其他床接走），这里把无房成年居民
    ///    直接送进未满员的大通铺（npc.EnterHouseFacility → 补丁后的 OnNpcEnter 记名册）。
    /// </summary>
    private void RefreshDormPool()
    {
        var beds = UnityEngine.Object.FindObjectsOfType<FacilityBed>();
        if (beds == null) return;

        var openBeds = new List<FacilityBed>();
        var openTravellerBeds = new List<FacilityBed>();
        foreach (var bed in beds)
        {
            if (!BedPatches.IsDorm(bed)) continue;

            bool travellerOnly = false;
            try { travellerOnly = bed.IsForTravellerOnly; } catch { }

            // 自愈①：清退精灵/石头人（一直有效）
            // 自愈②：旗标错配清退——专属床里的居民、非专属床里的旅客（规则只在入口查，旗标切换后残留的人要请走）
            try
            {
                var members = bed.member_list;
                if (members != null)
                {
                    for (int i = members.Count - 1; i >= 0; i--)
                    {
                        var m = members[i];
                        if (m == null || m.is_dead) continue;
                        bool mismatch = m.IsSpriteOrStoneMan()
                                        || BedPatches.IsRaceForbidden(m.race_id)
                                        || (travellerOnly
                                            ? !BedPatches.IsTraveller(m)                       // 专属床只留旅客
                                            : (!BedPatches.IsAllowedResidentType(m)            // 普通床只留白名单居民
                                               || BedPatches.IsTraveller(m)));
                        if (!mismatch) continue;
                        Plugin.LogV($"[JianZhu] 清退错配成员「{m.npc_name}」(type={m._npc_type}, 专属={travellerOnly})");
                        m.ExitHouseFacility();
                    }
                }
            }
            catch (Exception ex) { Plugin.LogError($"[JianZhu] 清退错配成员失败: {ex.Message}"); }

            int cnt = BedPatches.MemberCount(bed);
            int cap = BedPatches.CapacityOf(bed);
            // 专属床不参与居民收容（留给旅客直配），普通床未满员参与
            if (cnt < cap && !travellerOnly) openBeds.Add(bed);
            if (cnt < cap && travellerOnly) openTravellerBeds.Add(bed);
        }

        // 诊断：名册 dump（30s 一次），乱入住一眼可见
        if (Time.time >= _nextRosterAt)
        {
            _nextRosterAt = Time.time + 30f;
            try
            {
                foreach (var bed in beds)
                {
                    if (!BedPatches.IsDorm(bed)) continue;
                    var ms = bed.member_list;
                    if (ms == null || ms.Count == 0) continue;
                    var roster = new List<string>();
                    for (int i = 0; i < ms.Count; i++)
                    {
                        var m = ms[i];
                        if (m != null)
                            roster.Add($"{m.npc_name}(t{m._npc_type},r{m.race_id})");
                    }
                    bool tOnly = false;
                    try { tOnly = bed.IsForTravellerOnly; } catch { }
                    Plugin.LogV($"[JianZhu] 名册 bed={bed.guid}{(tOnly ? "[旅]" : "")}: {string.Join(", ", roster)}");
                }
            }
            catch (Exception ex) { Plugin.LogError($"[JianZhu] 名册 dump 失败: {ex.Message}"); }
        }

        // ① 空床池兜底 + 置尾优先
        try
        {
            var helper = GetHousingHelper();
            var pool = helper?.empty_bed_list;
            if (pool != null)
            {
                bool changed = false;
                foreach (var bed in openBeds)
                {
                    int idx = IndexInPool(pool, bed);
                    if (idx >= 0)
                    {
                        if (idx != pool.Count - 1) { pool.RemoveAt(idx); pool.Add(bed); changed = true; }
                    }
                    else
                    {
                        pool.Add(bed);
                        changed = true;
                    }
                }
                // 满员/消失的床从池里摘除；旅客专属床不进居民池（居民分配会不停撞墙）
                for (int i = pool.Count - 1; i >= 0; i--)
                {
                    var it = pool[i];
                    if (it == null || !BedPatches.IsDorm(it)) continue;
                    bool tOnly = false;
                    try { tOnly = it.IsForTravellerOnly; } catch { }
                    if (tOnly || BedPatches.MemberCount(it) >= BedPatches.CapacityOf(it))
                    {
                        pool.RemoveAt(i);
                        changed = true;
                    }
                }
                if (changed) Plugin.LogV($"[JianZhu] 空床池维护完成，当前 {pool.Count} 项");
            }
        }
        catch (Exception ex) { Plugin.LogError($"[JianZhu] 空床池维护失败: {ex.Message}"); }

        // ② NPC 扫描（单次共用）：仅在有未满员床时执行——全员满员的稳态零开销，
        //    避免 FindObjectsOfType 在大地图（2000+ NPC）上每 1.5s 一次的卡顿（工坊性能）
        bool hasOpen = openBeds.Count > 0 || openTravellerBeds.Count > 0;
        Npc[]? npcsAll = hasOpen ? UnityEngine.Object.FindObjectsOfType<Npc>() : null;

        // ②a 脱钩修复：guid 指向大通铺但名册里没有自己（TryClearMember 清人不清 guid 的残留）
        //    → 清 guid 重新变无房，当轮下方收容循环立刻重新分床
        if (npcsAll != null)
        try
        {
            var dormByGuid = new Dictionary<int, FacilityBed>();
            foreach (var bed in beds)
            {
                if (!BedPatches.IsDorm(bed)) continue;
                dormByGuid[bed.guid] = bed;
            }
            if (dormByGuid.Count > 0)
            {
                foreach (var npc in npcsAll)
                {
                    if (npc == null || npc.is_dead) continue;
                    int g = npc.house_facility_guid;
                    if (g == 0 || !dormByGuid.TryGetValue(g, out var bedOwner)) continue;
                    if (BedPatches.IsMember(bedOwner, npc)) continue;
                    string nm = "";
                    try { nm = npc.npc_name ?? ""; } catch { }
                    Plugin.LogV($"[JianZhu] 脱钩修复：「{nm}」guid→bed{g} 但名册无此人，清 guid 重新分配");
                    npc.house_facility_guid = 0;
                }
            }
        }
        catch (Exception ex) { Plugin.LogError($"[JianZhu] 脱钩修复失败: {ex.Message}"); }

        // ② 直接收容无房成年居民（没有普通空位时跳过居民段，但旅客直配/腾床仍要跑）
        if (openBeds.Count > 0 && npcsAll != null)
        try
        {
            foreach (var npc in npcsAll)
            {
                if (openBeds.Count == 0) break;
                if (npc == null || npc.is_dead) continue;
                if (npc.house_facility_guid != 0) continue; // 已有住房
                if (npc.IsSpriteOrStoneMan()) continue;     // 精灵/石头人不收
                if (!BedPatches.IsAllowedResidentType(npc)) continue; // 类型白名单（儿童特批，士兵/旅客/-11 等排除）

                // 选床策略（紧凑填充防占坑）：优先"已有同族成员且最满"的床把人压实，
                // 没有同族非空床才开新床（空床）。避免 1-3 人就锁死一张床。
                FacilityBed? target = null;
                int packedCount = -1;
                FacilityBed? emptyBed = null;
                foreach (var bed in openBeds)
                {
                    if (!BedPatches.CanAccept(bed, npc, out _)) continue;
                    int c = BedPatches.MemberCount(bed);
                    if (c > 0)
                    {
                        if (c > packedCount) { packedCount = c; target = bed; }
                    }
                    else if (emptyBed == null) emptyBed = bed;
                }
                if (target == null) target = emptyBed;
                if (target == null) continue; // 没有同族空位/仅剩旅客专属床

                string name = "";
                try { name = npc.npc_name ?? ""; } catch { }
                Plugin.LogV($"[JianZhu] 收容无房居民「{name}」→ 大通铺({BedPatches.MemberCount(target)}/{BedPatches.CapacityOf(target)})");
                npc.EnterHouseFacility(target, false);

                if (BedPatches.MemberCount(target) >= BedPatches.CapacityOf(target))
                    openBeds.Remove(target);
            }
        }
        catch (Exception ex) { Plugin.LogError($"[JianZhu] 收容分配失败: {ex.Message}"); }

        // ②b 腾床合并（每轮最多 2 张）：某床只有 1-3 人占坑、且同族其他床装得下全员
        //    → 整体搬过去，把这张床释放给其他种族（矮人等后来种族才有床用）
        try
        {
            int consolidations = 0;
            foreach (var bed in beds)
            {
                if (consolidations >= 2) break;
                if (!BedPatches.IsDorm(bed)) continue;
                bool tOnly = false;
                try { tOnly = bed.IsForTravellerOnly; } catch { }
                if (tOnly) continue;

                var ms = bed.member_list;
                if (ms == null || ms.Count == 0 || ms.Count > 3) continue;
                var first = ms[0];
                if (first == null) continue;
                int raceId = first.race_id;

                // 找同族且空闲位足够的床（排除自己）
                FacilityBed? sink = null;
                int sinkFree = 0;
                foreach (var other in beds)
                {
                    if (other == bed || !BedPatches.IsDorm(other)) continue;
                    bool ot = false;
                    try { ot = other.IsForTravellerOnly; } catch { }
                    if (ot) continue;
                    var oms = other.member_list;
                    if (oms == null || oms.Count == 0 || oms[0] == null || oms[0].race_id != raceId) continue;
                    int free = BedPatches.CapacityOf(other) - oms.Count;
                    if (free > sinkFree) { sinkFree = free; sink = other; }
                }
                if (sink == null || sinkFree < ms.Count) continue; // 装不下全员，不拆

                // 整体搬移
                string raceLog = $"race{raceId}";
                Plugin.LogV($"[JianZhu] 腾床合并：bed={bed.guid}({ms.Count}人,{raceLog}) → bed={sink.guid}(余位{sinkFree})");
                for (int i = ms.Count - 1; i >= 0; i--)
                {
                    var m = ms[i];
                    if (m == null || m.is_dead) continue;
                    m.ExitHouseFacility();
                    if (BedPatches.CanAccept(sink, m, out _))
                        m.EnterHouseFacility(sink, false);
                }
                consolidations++;
            }
        }
        catch (Exception ex) { Plugin.LogError($"[JianZhu] 腾床合并失败: {ex.Message}"); }

        // ③ 旅客专属床直配：游戏只给旅店（接待台范围）分旅客，手工标记的床不在
        //    TravellerHelper.empty_bed_list 里永远不会来人——这里把没有床的旅客
        //    直接送进未满员的旅客专属大通铺（EnterHouseFacility 标准入口）。
        if (openTravellerBeds.Count > 0 && npcsAll != null)
        {
            try
            {
                foreach (var npc in npcsAll)
                {
                    if (openTravellerBeds.Count == 0) break;
                    if (npc == null || npc.is_dead) continue;
                    if (npc.house_facility_guid != 0) continue; // 已有床位
                    if (!BedPatches.IsTraveller(npc)) continue;

                    FacilityBed? target = null;
                    int best = int.MaxValue;
                    foreach (var bed in openTravellerBeds)
                    {
                        if (!BedPatches.CanAccept(bed, npc, out _)) continue;
                        int c = BedPatches.MemberCount(bed);
                        if (c < best) { best = c; target = bed; }
                    }
                    if (target == null) break;

                    string name = "";
                    try { name = npc.npc_name ?? ""; } catch { }
                    Plugin.LogV($"[JianZhu] 旅客「{name}」入住旅客专属大通铺({best}/{BedPatches.CapacityOf(target)})");
                    npc.EnterHouseFacility(target, false);

                    if (BedPatches.MemberCount(target) >= BedPatches.CapacityOf(target))
                        openTravellerBeds.Remove(target);
                }
            }
            catch (Exception ex) { Plugin.LogError($"[JianZhu] 旅客直配失败: {ex.Message}"); }
        }
    }

    /// <summary> HousingHelper 是普通类（非 MonoBehaviour），走 Game.main_scene → area_map → my_territory → housing_helper </summary>
    private static HousingHelper? GetHousingHelper()
    {
        try
        {
            var ms = Game.main_scene;
            var am = ms?.area_map;
            var territory = am?.my_territory;
            return territory?.housing_helper;
        }
        catch { return null; }
    }

    private static int IndexInPool(Il2CppSystem.Collections.Generic.List<FacilityBed> pool, FacilityBed bed)
    {
        for (int i = 0; i < pool.Count; i++)
        {
            var item = pool[i];
            if (item == bed) return i; // interop == 按底层指针比较
        }
        return -1;
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

}
