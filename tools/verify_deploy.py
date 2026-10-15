"""部署后校验：检查 mods\\PilotCombined 是否完整、合法、与 out\\ 里的产物一致。

做四件事：
  1. 列出部署目录内容，确认 portraits 三个性别子目录都在；
  2. 逐个 JSON 文件做"UTF-8 解码 + JSON 解析"（游戏侧就是这个读法），
     并检查 mod.json 的关键字段（Manifest 必须为空数组、绝不能有 systemMod.json）；
  3. 校验 DLL 是不是合法的 .NET 托管程序集（PE + CLI 目录非 0）；
  4. 用哈希比对 out\\ 与部署目录里的 DLL / mod.json —— 确认部署的确实是刚编出来的那一份。

用法：
    python tools\\verify_deploy.py
    python tools\\verify_deploy.py --mods-dir "D:\\somewhere\\mods"
"""
import hashlib
import json
import os
import struct
import sys

MOD_FOLDER = "PilotCombined"
DLL_NAME = "PilotCombined.dll"

SELF = os.path.abspath(__file__)
ROOT = os.path.dirname(os.path.dirname(SELF))

mods_dir = os.path.join(os.environ["USERPROFILE"], "Documents", "My Games", "BattleTech", "mods")
if "--mods-dir" in sys.argv:
    mods_dir = sys.argv[sys.argv.index("--mods-dir") + 1]

target = os.path.join(mods_dir, MOD_FOLDER)
stage = os.path.join(ROOT, "out", MOD_FOLDER)
problems = []


def sha(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        h.update(f.read())
    return h.hexdigest()


print("=== 部署目录:", target)
if not os.path.isdir(target):
    print("  !! 目录不存在")
    sys.exit(1)

def count_images(root):
    return len([x for x in os.listdir(root)
                if x.lower().endswith((".png", ".jpg", ".jpeg", ".dds"))])


names = sorted(os.listdir(target))
for n in names:
    p = os.path.join(target, n)
    if os.path.isdir(p):
        if n.lower() != "portraits":
            # Any other folder in the mod root (e.g. a hand-made "other_暂存" the user parks
            # images in) is NOT scanned by the mod, so it must not be reported as a missing
            # gender folder.  Just report what is inside.
            print("  %-20s <dir>  %d 张图（不在 portraits 下，mod 不会扫描）" % (n, count_images(p)))
            continue
        detail = []
        for g in ("female", "male", "other"):
            gp = os.path.join(p, g)
            if os.path.isdir(gp):
                detail.append("%s=%d" % (g, count_images(gp)))
            else:
                detail.append("%s=MISSING" % g)
                problems.append("缺少 portraits\\%s 目录" % g)
        print("  %-20s <dir>  %s" % (n, "  ".join(detail)))
    else:
        print("  %-20s %8d bytes" % (n, os.path.getsize(p)))

# --- 1. JSON 文件 -----------------------------------------------------------------
print()
print("=== JSON 文件（UTF-8 解码 + 解析） ===")
jsons = [n for n in names if n.lower().endswith(".json")]
if not jsons:
    problems.append("部署目录里一个 .json 都没有")
for n in jsons:
    p = os.path.join(target, n)
    raw = open(p, "rb").read()
    bom = raw[:3] == b"\xef\xbb\xbf"
    try:
        obj = json.loads(raw.decode("utf-8"))
        print("  %-20s BOM=%-5s 解析 OK  顶层键: %s" % (n, bom, list(obj.keys())))
    except Exception as e:
        problems.append("%s 解析失败: %s" % (n, e))
        print("  %-20s !! 失败: %s" % (n, e))

# --- 2. mod.json 关键字段 ---------------------------------------------------------
mp = os.path.join(target, "mod.json")
print()
print("=== mod.json 关键字段 ===")
if not os.path.isfile(mp):
    problems.append("没有 mod.json")
else:
    obj = json.loads(open(mp, "rb").read().decode("utf-8"))
    for k in ("Name", "Version", "DLL", "DLLEntryPoint", "IsSaveAffecting",
              "LoadImplicitManifest", "Manifest"):
        print("  %-20s = %r" % (k, obj.get(k)))
    if obj.get("Manifest") != []:
        problems.append("Manifest 必须是空数组 []")
    if obj.get("LoadImplicitManifest") is not False:
        problems.append("LoadImplicitManifest 必须是 false")
    if obj.get("DLL") != DLL_NAME:
        problems.append("DLL 字段应为 %s" % DLL_NAME)

if "systemmod.json" in [x.lower() for x in names]:
    problems.append("目录里出现了 systemMod.json —— 绝对不允许（会触发原版 ModLoader 崩溃）")

# --- 2b. 配置：combined.json / 模板 / 旧文件 ---------------------------------------
print()
print("=== 配置检查（M2 之后只有 combined.json 是活的） ===")
cfg = os.path.join(target, "combined.json")
tmpl = os.path.join(target, "combined.json.template")
legacy = [n for n in names if n.lower() in ("genderratio.json", "portraits.json")]

if not os.path.isfile(tmpl):
    problems.append("缺少 combined.json.template（配置迁移要靠它当底稿）")
else:
    try:
        json.loads(open(tmpl, "rb").read().decode("utf-8"))
        print("  combined.json.template 存在且是合法 JSON")
    except Exception as e:
        problems.append("combined.json.template 解析失败: %s" % e)

if os.path.isfile(cfg):
    print("  combined.json 存在（运行时已读取/迁移）")
    try:
        obj = json.loads(open(cfg, "rb").read().decode("utf-8"))
        g = obj.get("gender_ratio", {})
        p = obj.get("portraits", {})
        print("      gender_ratio: enabled=%s other=%s female=%s male=%s label=%r lock=%s %s:%s"
              % (g.get("enabled"), g.get("other_percent"), g.get("female_percent"),
                 g.get("male_percent"), g.get("other_label"), g.get("lock_ratio"),
                 g.get("lock_ratio_female"), g.get("lock_ratio_male")))
        print("      portraits   : enabled=%s autoScan=%s poolMode=%s rules=%d pool=%d"
              % (p.get("enabled"), p.get("autoScan"), p.get("poolMode"),
                 len(p.get("rules") or []), len(p.get("pool") or [])))
        if legacy:
            print("      NOTE: 旧文件 %s 仍在目录里但**不再被读取**（可自行删除）" % ", ".join(legacy))
    except Exception as e:
        problems.append("combined.json 解析失败: %s" % e)
elif legacy:
    print("  combined.json 还不存在，但发现旧配置 %s —— 这是**预期状态**：" % ", ".join(legacy))
    print("      首次启动游戏时 MOD 会把它们迁移成 combined.json（设置不会丢）")
else:
    print("  combined.json 不存在，也没有旧配置 —— 首次启动会从模板生成默认配置")

# --- 3. DLL 合法性 ---------------------------------------------------------------
print()
print("=== DLL 检查 ===")
dll = os.path.join(target, DLL_NAME)
if not os.path.isfile(dll):
    problems.append("没有 %s" % DLL_NAME)
else:
    raw = open(dll, "rb").read()
    print("  大小:", len(raw), " MZ 头:", raw[:2] == b"MZ")
    pe = struct.unpack_from("<I", raw, 0x3C)[0]
    print("  PE 签名:", raw[pe:pe + 4] == b"PE\0\0")
    opt = pe + 24
    magic = struct.unpack_from("<H", raw, opt)[0]
    dd = opt + (96 if magic == 0x10B else 112)
    cli_rva = struct.unpack_from("<I", raw, dd + 14 * 8)[0]
    print("  CLI 目录 RVA: 0x%x (非 0 表示是托管程序集)" % cli_rva)
    if cli_rva == 0:
        problems.append("DLL 不是托管程序集（CLI 目录为 0）")

    # --- 4. 与 out\ 比对 ---------------------------------------------------------
    print()
    print("=== 与 out\\ 产物比对（确认部署的就是刚编出来的那份） ===")
    for name in (DLL_NAME, "mod.json"):
        a = os.path.join(stage, name)
        b = os.path.join(target, name)
        if not os.path.isfile(a):
            print("  %-20s staging 里没有，跳过" % name)
            continue
        ha, hb = sha(a), sha(b)
        same = ha == hb
        print("  %-20s %s" % (name, "一致  " + ha[:16] if same else "!! 不一致"))
        if same:
            print("  %-20s   out=%s" % ("", ha[:16]))
            print("  %-20s   mod=%s" % ("", hb[:16]))
        else:
            problems.append("%s 与 out\\ 不一致（部署的可能不是最新编译产物）" % name)

# --- 结论 ------------------------------------------------------------------------
print()
if problems:
    print("发现 %d 个问题：" % len(problems))
    for p in problems:
        print("   - " + p)
    sys.exit(1)
print("全部检查通过。")
