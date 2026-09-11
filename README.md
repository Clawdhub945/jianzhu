# JianZhu（全选建造）

Territory（Steam appid 1455910，IL2CPP Unity 游戏）的 **BepInEx 6 + IL2CPP + Harmony** mod。
目标：游戏内"全选"建造/建筑的选择 UI；具体功能需求后定，当前为骨架版。

## 构建

```bash
dotnet build -c Release --no-restore
```

- 构建产物自动复制到 `C:\TerritoryModTest`（游戏 mod 加载目录，**游戏重启才加载新 DLL**）。
- 可用 `-p:GameDir=... -p:ModDir=...` 覆盖游戏目录与部署目录。
- `--no-restore` 必须携带（项目无 NuGet 依赖，沙箱下 restore 会挂）。

## 当前进度

- 0.1.0 骨架：插件加载 + F9 开关 IMGUI 占位面板（打通 IL2CPP 组件注入与 UI 渲染路径）。

## 领域知识

本 mod 沿用 ChestEditor 仓库（`C:\AI\mod\ChestEditor`）`docs/DEVELOPMENT.md` 的全部 IL2CPP interop 硬规则：
主线程触碰 Unity/IL2CPP 对象、字符串 GC 根、`--no-restore` 构建、UTF-8 无 BOM + LF 等。
