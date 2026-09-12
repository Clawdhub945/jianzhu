#!/usr/bin/env python3
# 组装/刷新创意工坊包：workshop/JianZhu/{About,Defs,Textures,Assemblies}
# 用法: python _tools/pack_workshop.py   （先 dotnet build + make_defs 再跑本脚本）
import re
import shutil
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
PKG = REPO / "workshop" / "JianZhu"

def main():
    # 1) DLL
    dll = REPO / "bin" / "Release" / "net6.0" / "JianZhu.dll"
    (PKG / "Assemblies").mkdir(parents=True, exist_ok=True)
    shutil.copy2(dll, PKG / "Assemblies" / "JianZhu.dll")
    # 2) Defs
    (PKG / "Defs").mkdir(parents=True, exist_ok=True)
    for f in ("stuff.json", "build.json", "tech.json"):
        shutil.copy2(REPO / "Defs" / f, PKG / "Defs" / f)
    # 3) Textures（mod 图标贴图：游戏扫 <mod>/Textures/textures.xml）
    (PKG / "Textures").mkdir(parents=True, exist_ok=True)
    for f in ("textures.xml", "ui_101007.png"):
        shutil.copy2(REPO / "Textures" / f, PKG / "Textures" / f)
    # 4) About.xml 的 modVersion 与 Plugin.cs 同步
    plugin = (REPO / "Plugin.cs").read_text(encoding="utf-8")
    ver = re.search(r'PLUGIN_VERSION = "([^"]+)"', plugin).group(1)
    about = PKG / "About" / "About.xml"
    at = about.read_text(encoding="utf-8")
    at = re.sub(r"<modVersion>[^<]+</modVersion>", f"<modVersion>{ver}</modVersion>", at)
    about.write_bytes(at.encode("utf-8"))  # LF 写盘
    print(f"workshop 包已刷新: Assemblies/JianZhu.dll, Defs/*.json, Textures/*, modVersion={ver}")

if __name__ == "__main__":
    main()
