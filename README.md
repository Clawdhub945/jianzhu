# JianZhu（大通铺 / 全选建造）

Territory（Steam appid 1455910，IL2CPP Unity 游戏）mod。
当前功能：**新增"大通铺"床** —— 占位 1×2、可睡 10 名居民、贴图复用原版小床。

## 实现方式：游戏官方 Def 数据通道（零 Harmony、零 IL2CPP interop 补丁）

反编译确认游戏自带 RimWorld 式 Def mod 系统：

```
D.LoadData()
  └─ ModsHelper.GetPluginsDir()  =  <游戏根>/BepInEx/plugins
  └─ D.LoadDefsOfMod(...)        遍历 plugins/<任意mod夹>/Defs/*.json
       └─ 文件名去扩展名 = 表名（stuff.json → stuff 表，build.json → build 表）
       └─ 文件文本 = 官方表同格式 JSON 数组，走与主表完全相同的解析器
            （StuffInfo.Load → JsonUtil.FromJson<List<StuffInfo>> → stuff_dic[id] + 子类型索引表）
```

因此**大通铺本体是纯数据 Def 注入**，不需要任何 C# 代码补丁：

- `Defs/stuff.json`：101007 行，拷贝官方小床(101001)全字段，改 `effect_value=10`（数据表
  `remark:"可睡人数"` 即床容量字段；小床=1、大床=2、双层床=2、洞穴=20）、名字/描述。
  `prefab:"bed"` 原样保留 → 实例化小床预制体，贴图/模型天然复用。
- `Defs/build.json`：101007 行，拷贝官方 build 表 101001 行。`cellw:1, cellh:2` 即 1×2 占地
  （小床本身就是 1×2）；`class_name:"FacilityBed"` 原样保留 → 行为与小床一致。
- `Defs/tech.json`：**必需！** 建造菜单按 tech.json（设施解锁表）过滤，缺行则菜单不显示
  （101007 照样进 stuff_dic/build_dic/子类型表，但 BuildMenuGroup 不装配，实机+日志取证）。
  行格式照抄小床：`{"txt_id":200, "tech_id":0, "facility_id":101007}`（tech_id=0=免科技）。
  排查这类问题用 JianZhu.dll 的菜单 dump 日志（stuff_id_list_of_sub_type_dic + BuildMenuGroup items）。

由 `_tools/make_defs.py` 从 `C:\AI\领地部分源码(AI注释)\ExtraData` 的官方表生成（勿手编）。

## JianZhu.dll 的角色

仅为诊断/状态工具（v0.2.0）：启动时打印游戏 Defs 根目录，轮询 `D.Ins.stuff_dic/build_dic`
确认 101007 已进表（≤3 分钟，进表后停止），F9 开关 IMGUI 状态面板。

## 部署（与 ChestEditor 同款工作流）

```bash
dotnet build -c Release --no-restore     # DLL → C:\TerritoryModTest
python _tools/make_defs.py --deploy      # Defs → C:\TerritoryModTest\Defs
```

- `C:\TerritoryModTest` 是游戏**本地测试目录**：游戏启动时把该目录**镜像同步**进
  `BepInEx/plugins/1005`（实测同步会删除 1005 里 TerritoryModTest 没有的文件；子目录也会镜像）。
- ⚠ 不要自建 `plugins/<自命名夹>`：会被启动同步清掉（实测 `plugins/JianZhu` 被删）。
- 游戏必须**重启**才加载新 DLL/Defs（LoadData 在启动时跑）。

## 多人床位 DLL 补丁（v0.3.0，BedPatches.cs）

原版床位是**家庭单位**：`FacilityBed.OnNpcEnter` 是入住闸门（家庭/数量校验，实测上限 2 大人+1 小孩），
`FacilityBed.IsNoNpcInHouse`（`member_list.Count==0`）决定是否进 `HousingHelper.empty_bed_list` 分配池，
且居民实际选床不走该池（实测无房者被其他床接走）。因此三件套：

1. **Harmony 补丁 `OnNpcEnter`**（仅 stuff_id==101007）：容量内强制收下，⚠ prefix 里必须自己
   `member_list.Add(npc)` + `UpdateSprite()`（原版簿记被跳过，漏记会导致名册恒 0——0.3.0 教训）。
2. **Harmony 补丁 `IsNoNpcInHouse`**（仅 101007）：改为 `count < effect_value`（未满员=可分配）。
3. **JianZhuComponent 每 3s 兜底**：未满员的大通铺放回 empty_bed_list 池尾（优先）；
   并把**无房居民**（`house_facility_guid==0`；**儿童(-2)与成年人同住允许**；
   仍排除婴儿(-3)/学生(-1)）直接 `npc.EnterHouseFacility(bed, false)` 收进人数最少且同族的床。
   类型判定用游戏谓词 `NpcType.IsPeople_NotElf_NotNoble_NotSoldier_NotPrisoner` 白名单
   （自动排除士兵 1001-1900/贵族/领主/商队/-11 等全部特殊负数类型——0.4.3 黑名单漏网教训）。
4. **旅客专属分流（0.4.2）**：按床的 `IsForTravellerOnly` 双向限制——未设"仅限旅客"的床
   不收旅客（-10/-12/-13）；设了专属的床只收旅客、不收居民。接待台范围内新建的床会被
   游戏自动标记为旅客专属。
5. **0.5.0 增补**：士兵(1001-1900)放行（兵营绑床流程的合法住户）；旅客专属床由组件
   直接给无床旅客办理入住（手工标记的床不在 TravellerHelper 池里，游戏不会派旅客来）；
   服务循环 3s→1.5s。
6. **0.5.2 脱钩修复**：游戏 `TryClearMember` 系列（夫妻分床清非育龄成员等）只删名册
   不清 NPC 侧 guid，多人大通铺会因此产生"guid 指床但名册无人"的脱钩孤儿（实测 15 个）。
   修复=闸门对大通铺跳过 TryClearMember + 服务循环每轮清残留 guid 并当轮重新收容。
7. **0.5.1 防占坑**：选床改紧凑填充（优先同族已有人且最满的床，空床只开新坑用）；
   腾床合并——同族其他床装得下时，把 ≤3 人的占坑床整体搬走释放（每轮最多 2 张）。

实机验证（最新档 `2026-09-12_14_*`）：无房 NPC 依次入住，名册 0→10 正常增长（日志 41 次 OnNpcEnter）。
容量读取 `facility_stuff_info.effect_value_int`（=10），改 Defs 即可调床位数。

## 每床容量配置文件（0.6.0）

`BepInEx/config/claude.jianzhu.cfg`（插件首次启动自动生成）：

```ini
[大通铺]

## 修改数值即可调整游戏内大通铺最大居住小人数（每张床）。默认 10，最小 1，最大 25。
# Setting type: Int32
# Default value: 10
# Acceptable value range: From 1 to 25
最大居住小人数 = 10
```

- **游戏运行中直接改文件即可，约 1.5 秒内热生效**（服务循环每轮 Config.Reload()）
- 范围 1-25，越界值会被钳制到边界并写回文件
- **猪人(6)/蚁人(1)/鼠人(2)**：默认禁止睡大通铺（`允许猪人蚁人鼠人睡床 = false`），
  改 `true` 允许居住；在床上的会被自动请走
- 只对大通铺（101007）生效，原版床不受影响

## 读档（远程验证）

游戏启动 → 轮询 `GET /api/editor/state` → **读 mtime 最新的存档目录**（玩过程中会不断生成自动档，
`ls -t` 取第一个目录名）→ `POST /api/editor/debug/load {"dir":"<目录名>"}` → 等 `inSave:true && saveLoads+1`。

## 建造注意

- `--no-restore` 必须携带（无 NuGet 依赖；首次新项目需手动 `dotnet restore` 一次）。
- 源文件 UTF-8 无 BOM + LF；typed interop（`D.Ins`、`ModsHelper` 等）仅用于诊断组件。

## 验证记录（2026-09-12，实机读档）

```
[Info: JianZhu] stuff_dic[101007] = 大通铺, effect_value=10, prefab=bed, img=bed_0
[Info: JianZhu] ✓ 101007 已进表：stuff_dic(1835 项) + build_dic 均包含
[Info: JianZhu] stuff_id_list_of_sub_type_dic[101] = [101001,101002,101003,101004,101006,101007]
```

- 建造菜单「住所」出现大通铺（贴图同小床），摆放信息：需建在(室内)、木 x10、可旋转（R）
- 实际摆放 1×2 成功，日志通知"大通铺 建造完成"，设施窗口正常（居民分配页签同小床）
- 建造快捷键 B；「菜单」按钮是系统菜单，别混

待观察：居民自动分配是否真能睡满 10 人（床位容量消费点 `FacilityBed.OnNpcEnter`
不在反编译语料中；effect_value=10 按数据表"可睡人数"语义设定，若实测卡 1 人再定位补丁）。

## 后续：全选建造 UI

功能需求后定；F9 面板可扩展为全选 UI 载体。
