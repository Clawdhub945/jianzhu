#!/usr/bin/env python3
# 从官方数据表生成 JianZhu 的 Def 文件（官方 Def mod 通道：BepInEx/plugins/<mod>/Defs/*.json）
# 用法: python _tools/make_defs.py [--deploy]
# 行拷贝自官方 stuff.json/build.json 的 101001(小床)，只改需求字段，避免手写 90 个键出错。
import json
import shutil
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
EXTRA_DATA = Path(r"C:\AI\领地部分源码(AI注释)\ExtraData")
GAME_PLUGINS = Path(r"C:\Program Files (x86)\Steam\steamapps\common\Territory\BepInEx\plugins")

MOD_ID = 101007          # 101001-101006 已被官方占用（小床/大床/精致的床/洞穴/双层床/精致小床）
SLEEP_COUNT = 10         # 可睡人数（stuff.effect_value，数据表 remark="可睡人数"）
NAME = "大通铺"
DESC = "可供十名居民同时睡觉的通铺。"
# 部署进 C:\TerritoryModTest\Defs —— 游戏启动时把整个 TerritoryModTest 镜像同步进
# BepInEx/plugins/1005（实测：TerritoryModTest 里没有的文件会从 1005 删除）。
TEST_DIR = Path(r"C:\TerritoryModTest")
DEF_DEPLOY_DIR = TEST_DIR / "Defs"


def load(table: str):
    return json.loads((EXTRA_DATA / f"{table}.json").read_text(encoding="utf-8"))


def main():
    stuff = load("stuff")
    build = load("build")

    src_stuff = next(r for r in stuff if r.get("stuff_id") == 101001)
    new_stuff = dict(src_stuff)
    new_stuff["stuff_id"] = MOD_ID
    new_stuff["effect_value"] = float(SLEEP_COUNT)
    new_stuff["stuff_namezh-CN"] = NAME
    new_stuff["desczh-CN"] = DESC
    # prefab/stuff_img/stuff_img_on_map 保持 "bed"/"ui_101001"/"bed_0" —— 贴图直接复用小床

    src_build = next(r for r in build if r.get("id") == 101001)
    new_build = dict(src_build)
    new_build["id"] = MOD_ID
    # class_name=FacilityBed / cellw=1 / cellh=2 均随小床原值；占地 1×2 即小床自身占地
    new_build["guide_info_zh-CN"] = (
        '<sprite name="ic_locating_npc"> 定位睡在此处的居民\n'
        "# 可供十名居民同时睡觉"
    )

    out_dir = REPO / "Defs"
    out_dir.mkdir(exist_ok=True)
    (out_dir / "stuff.json").write_text(
        json.dumps([new_stuff], ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    (out_dir / "build.json").write_text(
        json.dumps([new_build], ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")

    # 建造菜单按 tech.json（设施解锁表）过滤 —— 实测缺行则菜单不显示（日志取证：
    # 101007 进了 stuff_dic/build_dic/子类型表，但 BuildMenuGroup 不装配）。
    # tech_id=0 = 免科技直接可建（同小床 txt_id=200 行的格式）。
    new_tech = {"txt_id": 200, "tech_id": 0, "facility_id": MOD_ID, "seed_id": "", "event_id": ""}
    (out_dir / "tech.json").write_text(
        json.dumps([new_tech], ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"generated: {out_dir}\\stuff.json, build.json, tech.json")

    if "--deploy" in sys.argv:
        dst = DEF_DEPLOY_DIR
        dst.mkdir(parents=True, exist_ok=True)
        for f in ("stuff.json", "build.json", "tech.json"):
            shutil.copy2(out_dir / f, dst / f)
        print(f"deployed: {dst}")


if __name__ == "__main__":
    main()
