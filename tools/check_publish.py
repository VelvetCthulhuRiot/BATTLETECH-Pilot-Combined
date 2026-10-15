r"""发布前检查：把"不该进仓库的东西"和"可能泄露本机信息的东西"一次性找出来。

只检查 **git 已跟踪的文件**（`git ls-files`）—— 那才是真正会被推上去的内容，
所以 .gitignore 里挡掉的东西这里不会误报。

检查项：
  1. 单文件体积（>50 MB 硬失败；>10 MB 提示）
  2. 疑似密钥 / 凭据（私钥块、常见 token 前缀、password/secret 赋值、.env 之类）
  3. 含本机绝对路径或用户名的文本（C:\Users\<name>、D:\ 这类、以及当前用户名本身）
  4. 二进制文件清单（图片以外的二进制要人工确认）
  5. 疑似第三方 / 游戏本体内容（游戏目录名、.dll/.assets/.bundle 等）

用法（项目根目录）:  python tools\check_publish.py
退出码：0 = 没有硬问题（可能有提示）；1 = 有必须处理的问题。
"""
import io
import os
import re
import subprocess
import sys

HARD_MB = 50.0      # 单文件硬上限
WARN_MB = 10.0      # 提示阈值

# ---- 疑似密钥 / 凭据 ----
SECRET_PATTERNS = [
    (r"-----BEGIN [A-Z ]*PRIVATE KEY-----", "私钥块"),
    (r"\bghp_[A-Za-z0-9]{20,}", "GitHub personal access token"),
    (r"\bgithub_pat_[A-Za-z0-9_]{20,}", "GitHub fine-grained token"),
    (r"\bsk-[A-Za-z0-9]{20,}", "OpenAI 风格密钥"),
    (r"\bAKIA[0-9A-Z]{16}\b", "AWS access key id"),
    (r"\bxox[baprs]-[A-Za-z0-9-]{10,}", "Slack token"),
    (r"\bAIza[0-9A-Za-z_\-]{30,}", "Google API key"),
    (r"(?i)\b(api[_-]?key|apikey|secret|passwd|password|access[_-]?token|client[_-]?secret)\b\s*[:=]\s*['\"][^'\"]{6,}['\"]",
     "疑似 键/密码 赋值"),
]

# ---- 本机路径 / 用户名 ----
LOCAL_PATH_PATTERNS = [
    (r"[A-Za-z]:\\Users\\[^\\\s\"']+", "本机用户目录绝对路径"),
    (r"[A-Za-z]:\\MyDownload\\", "本机自定义盘符路径"),
    (r"\bD:\\MyDownload\b", "本机 Steam 路径"),
]

# ---- 疑似第三方 / 游戏本体内容 ----
THIRDPARTY_PATTERNS = [
    (r"(?i)\bBattleTech_Data\b", "游戏数据目录名"),
    (r"(?i)StreamingAssets", "游戏资源目录名"),
    (r"(?i)\bHBS\b", "HBS 命名空间/目录"),
]

BINARY_EXT = {".dll", ".exe", ".zip", ".png", ".jpg", ".jpeg", ".webp", ".dds",
              ".a8", ".bundle", ".assets", ".ttf", ".otf", ".ttc", ".pdb", ".so", ".dylib"}
TEXT_EXT = {".cs", ".py", ".ps1", ".sh", ".yml", ".yaml", ".json", ".md", ".txt",
            ".xml", ".cfg", ".ini", ".toml", ".csv", ".html", ".css", ".js"}

SKIP_DIRS = (".git/", "out/", "dist/", "obj/")


def tracked_files():
    out = subprocess.check_output(["git", "ls-files", "-z"])
    names = [n for n in out.decode("utf-8", "replace").split("\0") if n]
    return [n for n in names if not n.startswith(SKIP_DIRS)]


def username_hits(text, username):
    hits = []
    for pat, why in LOCAL_PATH_PATTERNS:
        n = len(re.findall(pat, text))
        if n:
            hits.append((why, n))
    if username:
        n = text.count(username)
        if n:
            hits.append(("本机用户名 '%s'" % username, n))
    return hits


def main():
    try:
        files = tracked_files()
    except Exception as e:
        print("无法读取 git 跟踪列表（是否在仓库根目录？）：" + str(e))
        return 2

    username = os.environ.get("USERNAME") or os.environ.get("USER") or ""

    print("=== 已跟踪文件：%d 个 ===" % len(files))
    sizes = []
    for f in files:
        try:
            sizes.append((os.path.getsize(f), f))
        except OSError:
            sizes.append((0, f))
    sizes.sort(reverse=True)
    print("--- 最大的 12 个 ---")
    for sz, f in sizes[:12]:
        print("  %8.2f MB  %s" % (sz / 1048576.0, f))

    too_big = [(sz, f) for sz, f in sizes if sz > HARD_MB * 1048576]
    warn_big = [(sz, f) for sz, f in sizes if WARN_MB * 1048576 < sz <= HARD_MB * 1048576]
    print("")
    if too_big:
        print("!! 超过 %.0f MB（GitHub 单文件硬上限）:" % HARD_MB)
        for sz, f in too_big:
            print("   %8.2f MB  %s" % (sz / 1048576.0, f))
    else:
        print("OK 没有超过 %.0f MB 的单文件（当前最大 %.2f MB）"
              % (HARD_MB, (sizes[0][0] / 1048576.0) if sizes else 0))
    if warn_big:
        print("提示 超过 %.0f MB（可接受，但最好别留在仓库里）:" % WARN_MB)
        for sz, f in warn_big:
            print("   %8.2f MB  %s" % (sz / 1048576.0, f))

    print("")
    print("=== 内容扫描 ===")
    secrets, pathhits, third, binaries = [], [], [], []
    for f in files:
        ext = os.path.splitext(f)[1].lower()
        try:
            with open(f, "rb") as fh:
                raw = fh.read()
        except OSError:
            continue
        is_bin = b"\0" in raw[:8192] or ext in BINARY_EXT and ext not in TEXT_EXT
        if is_bin:
            binaries.append(f)
            continue
        text = raw.decode("utf-8", "replace")

        for pat, why in SECRET_PATTERNS:
            m = re.search(pat, text)
            if m:
                secrets.append((f, why, m.group(0)[:60]))
        hits = username_hits(text, username)
        if hits:
            pathhits.append((f, hits))
        for pat, why in THIRDPARTY_PATTERNS:
            n = len(re.findall(pat, text))
            if n:
                third.append((f, why, n))

    if secrets:
        print("!! 疑似密钥/凭据（必须处理）:")
        for f, why, sample in secrets:
            print("   %s : %s  例: %s" % (f, why, sample))
    else:
        print("OK 没有匹配到密钥/凭据特征")

    if pathhits:
        print("提示 含本机绝对路径/用户名的文本文件（共 %d 个，按命中次数排序）:" % len(pathhits))
        pathhits.sort(key=lambda x: -sum(n for _, n in x[1]))
        for f, hits in pathhits[:15]:
            print("   %-46s %s" % (f, "，".join("%s×%d" % (w, n) for w, n in hits)))
        if len(pathhits) > 15:
            print("   ... 另有 %d 个" % (len(pathhits) - 15))
    else:
        print("OK 没有发现本机绝对路径/用户名")

    if third:
        print("提示 提到游戏/第三方目录名的文件（确认里面没有游戏本体内容）:")
        for f, why, n in third[:10]:
            print("   %-46s %s ×%d" % (f, why, n))
    else:
        print("OK 没有提到游戏数据目录")

    print("")
    print("=== 二进制文件（%d 个，需人工确认不是构建产物/游戏素材）===" % len(binaries))
    byext = {}
    for f in binaries:
        byext.setdefault(os.path.splitext(f)[1].lower(), []).append(f)
    for ext, lst in sorted(byext.items()):
        print("   %-8s %d 个   例: %s" % (ext or "(无扩展名)", len(lst), lst[0]))

    hard = bool(secrets) or bool(too_big)
    print("")
    print("结论：" + ("存在必须处理的问题（见上面的 !!）" if hard else "没有必须处理的问题（提示项请自行判断）"))
    return 1 if hard else 0


if __name__ == "__main__":
    sys.exit(main())
