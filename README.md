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

## 构建注意

- `--no-restore` 必须携带（无 NuGet 依赖；首次新项目需手动 `dotnet restore` 一次）。
- 源文件 UTF-8 无 BOM + LF；typed interop（`D.Ins`、`ModsHelper` 等）仅用于诊断组件。

## 验证记录（2026-09-12）

```
[Info: BepInEx] Loading [JianZhu 0.2.0]
[Info: JianZhu] 游戏 Defs 根目录 = ...\Territory\BepInEx\plugins
[Info: JianZhu] stuff_dic[101007] = 大通铺, effect_value=10, prefab=bed, img=bed_0
[Info: JianZhu] ✓ 101007 已进表：stuff_dic(1835 项) + build_dic 均包含，Def 通道注入成功
```

待人工确认：进存档 → 建造菜单「家具」应出现「大通铺」（贴图同小床）→ 摆放 1×2 →
分配居民睡觉（容量是否真到 10 人：床位容量消费点 `FacilityBed.OnNpcEnter` 不在反编译语料中，
若实测卡 1 人再定位该函数打 Harmony 补丁）。

## 后续：全选建造 UI

功能需求后定；F9 面板可扩展为全选 UI 载体。
