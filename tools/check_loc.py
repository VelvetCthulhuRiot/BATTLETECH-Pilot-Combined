r"""核查 GenderRatioWindow.cs 里还有没有"面向用户但没走 Loc"的中文文案。

判定规则（够用即可，不追求完美语法分析）：
  * 只看**字符串字面量**里的中文；
  * 跳过注释（// /// 与 /* */）；
  * 跳过**跨越多行**的日志语句（Log.Info/Warn/Error 那种拼接了好几行的）；
  * 跳过只写日志的调试方法 DescribeBounds()；
  * 剩余的一律报出来 —— 那些就是"用户能在窗口里看到、却没做多语言"的漏网之鱼。

已知的两处**有意保留**的中文：FemaleWord()/MaleWord() 里 `return "女"` / `return "男"`
（中文界面在配置缺标签时的兜底），它们会出现在输出里，属正常。

用法（项目根目录）:  python tools\check_loc.py
"""
import io
import re
import sys
import os

WINDOW = os.path.join("src", "GenderRatioWindow.cs")
LOC = os.path.join("src", "Loc.cs")

# 取 C# 字符串字面量：必须处理转义（`\"`、`\\`），否则带转义的字符串会让匹配错位，
# 后面的字面量就全被吞进一个跨行的"假字符串"里（这坑踩过一次：键提取几乎全丢）。
CS_STR = re.compile(r'"((?:[^"\\]|\\.)*)"')
CJK = re.compile(r"[\u4e00-\u9fff]")

# 这些方法**允许**直接读 combined.json 里的中文标签（它们本身就是"语言感知词"的出口）
ALLOWED_METHODS = ("DescribeBounds", "OtherLabel", "FemaleWord", "MaleWord")
# 面向用户的字符串里出现这些，就说明漏用了 FemaleWord()/MaleWord()/OtherLabel()
RAW_LABEL = re.compile(r"\b(s|cfg|config)\.(otherLabel|femaleLabel|maleLabel)\b")


def check_window():
    """① 窗口里有没有没走 Loc 的中文；② 有没有直接用配置里的中文标签。"""
    lines = io.open(WINDOW, encoding="utf-8").read().splitlines()
    bad = []
    raw = []
    in_block = False
    in_log = False
    skip_depth = 0
    depth = 0
    skipped = None

    for no, raw_line in enumerate(lines, 1):
        line = raw_line.strip()

        if skip_depth == 0 and re.match(r"(private|internal|public).*\b(\w+)\s*\(", line):
            m = re.match(r"(private|internal|public).*\b(\w+)\s*\(", line)
            if m and m.group(2) in ALLOWED_METHODS:
                skip_depth = 1
                skipped = m.group(2)
                depth = line.count("{") - line.count("}")
                continue
        if skip_depth > 0:
            depth += line.count("{") - line.count("}")
            if depth <= 0:
                skip_depth = 0
                skipped = None
            continue

        if in_block:
            if "*/" in line:
                in_block = False
            continue
        if line.startswith("/*"):
            if "*/" not in line:
                in_block = True
            continue
        if in_log:
            if ";" in line:
                in_log = False
            continue
        if line.startswith("//") or line.startswith("///"):
            continue
        if re.match(r'return "(女|男)";$', line):
            continue

        if "Log." in line:
            if ";" not in line:
                in_log = True
            continue

        for m in CS_STR.finditer(line):
            if CJK.search(m.group(1)):
                bad.append((no, m.group(1)))
        if RAW_LABEL.search(line):
            raw.append((no, line))
    return bad, raw


def check_keys():
    """③ 窗口里用到的 Loc 键 与 Loc.cs 表里的键 是否一一对得上（防拼错 / 防多余）。"""
    win = io.open(WINDOW, encoding="utf-8").read()
    loc = io.open(LOC, encoding="utf-8").read()
    # 收集"长得像键"的字符串字面量（小写字母 + 点号，如 apply.ok / status.ratio）。
    # 不用解析调用结构：`Loc.TF("status.ratio", Ratio(Mathf.RoundToInt(p[0]), ...))` 这种
    # 嵌套括号会让"匹配参数再取字面量"的做法漏掉键，所以直接按形状筛更稳。
    # 键的形状：小写起头、点分段，段里允许大写（如 rescan.emptyNote / apply.portraitFail）
    KEY_SHAPE = re.compile(r"^[a-z][A-Za-z0-9_]*(\.[A-Za-z0-9_]+)+$")
    used = set(m.group(1) for m in CS_STR.finditer(win) if KEY_SHAPE.match(m.group(1)))
    defined = set(re.findall(r'Add4?\(\s*"([^"]+)"', loc))
    return sorted(used - defined), sorted(defined - used)


def main():
    if not os.path.isfile(WINDOW):
        print("找不到 " + WINDOW)
        return 2

    ok = True
    bad, raw = check_window()
    if bad:
        ok = False
        print("① 窗口里发现 %d 处可能没做多语言的中文文案：" % len(bad))
        for no, text in bad:
            print("   %4d  %s" % (no, text))
    else:
        print("① OK：窗口里面向用户的文案已全部走 Loc（没有漏翻的中文）。")

    if raw:
        ok = False
        print("② 这些地方直接用配置里的中文标签，应改用 FemaleWord()/MaleWord()/OtherLabel()：")
        for no, text in raw:
            print("   %4d  %s" % (no, text))
    else:
        print("② OK：没有直接引用 combined.json 里的中文标签。")

    missing, unused = check_keys()
    if missing:
        ok = False
        print("③ 窗口用了但 Loc.cs 里没有的键（会原样显示键名）：" + ", ".join(missing))
    if unused:
        print("③ 提示：Loc.cs 里定义了但窗口没用的键（可能已废弃）：" + ", ".join(unused))
    if not missing:
        print("③ OK：窗口用到的键在 Loc.cs 里都存在。")

    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
