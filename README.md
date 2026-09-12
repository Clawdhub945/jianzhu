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
   仍排除婴儿(-3)/学生(-1)/流民(-5)/旅客(-10,-12,-13)/贵族(61)/领主(70)）直接 `npc.EnterHouseFacility(bed, false)` 收进人数最少的床。

实机验证（最新档 `2026-09-12_14_*`）：无房 NPC 依次入住，名册 0→10 正常增长（日志 41 次 OnNpcEnter）。
容量读取 `facility_stuff_info.effect_value_int`（=10），改 Defs 即可调床位数。

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
