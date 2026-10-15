r"""从 src\Loc.cs 的内置文案表导出各语言文件模板到 mod\loc\。

为什么要这个工具：内置表（Loc.cs 的 Build()）是"**保底**"，而 loc\*.json 是"**可覆盖**"。
两者必须对齐 —— 手抄 40 条键必然会飘。所以 JSON 一律由这个脚本从 Loc.cs 生成：

  * en.json / zh.json  = 内置表里的英文 / 中文（真值）
  * de.json / ru.json  = 英文值占位 + 提示，等翻译者把值换成德文/俄文即可

用法（项目根目录）:  python tools\dump_loc.py
"""
import io
import os
import re
import sys

LOC = os.path.join("src", "Loc.cs")
OUT = os.path.join("mod", "loc")

# 匹配 Add("key", "en", "zh") 与 Add4("key", "en", "zh", "de", "ru")
CALL = re.compile(
    r'\bAdd4?\(\s*"([^"]+)"\s*,\s*"((?:[^"\\]|\\.)*)"\s*,\s*"((?:[^"\\]|\\.)*)"'
    r'(?:\s*,\s*"((?:[^"\\]|\\.)*)"\s*,\s*"((?:[^"\\]|\\.)*)")?\s*\)'
)


def unescape(s):
    """C# 字符串里的 \" 与 \\ —— 还原成真实字符。"""
    return s.replace('\\"', '"').replace("\\\\", "\\")


def parse_loc():
    src = io.open(LOC, encoding="utf-8").read()
    rows = []
    for m in CALL.finditer(src):
        key = m.group(1)
        en = unescape(m.group(2))
        zh = unescape(m.group(3))
        de = unescape(m.group(4)) if m.group(4) else None
        ru = unescape(m.group(5)) if m.group(5) else None
        rows.append((key, en, zh, de, ru))
    return rows


def write_json(path, values, header):
    lines = ['{']
    lines.append('  "//": %s,' % json_str(header))
    keys = list(values.keys())
    for i, k in enumerate(keys):
        comma = "," if i < len(keys) - 1 else ""
        lines.append('  %s: %s%s' % (json_str(k), json_str(values[k]), comma))
    lines.append('}')
    io.open(path, "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")


def json_str(s):
    out = s.replace("\\", "\\\\").replace('"', '\\"')
    out = out.replace("\n", "\\n").replace("\t", "\\t")
    return '"' + out + '"'


def main():
    if not os.path.isfile(LOC):
        print("找不到 " + LOC)
        return 2
    rows = parse_loc()
    if not rows:
        print("没解析到任何文案（Loc.cs 的 Add/Add4 写法变了？）")
        return 1

    if not os.path.isdir(OUT):
        os.makedirs(OUT)

    en = dict((k, e) for (k, e, z, d, r) in rows)
    zh = dict((k, z) for (k, e, z, d, r) in rows)
    de = dict((k, (d if d else e)) for (k, e, z, d, r) in rows)
    ru = dict((k, (r if r else e)) for (k, e, z, d, r) in rows)

    write_json(os.path.join(OUT, "en.json"), en,
               "MOD UI text - English. This file may override the built-in strings.")
    write_json(os.path.join(OUT, "zh.json"), zh,
               "MOD 界面文案 - 简体中文。这个文件可以覆盖内置文案；改完重启游戏即可生效。")
    write_json(os.path.join(OUT, "de.json"), de,
               "MOD UI text - Deutsch. Platzhalter = englischer Text; "
               "bitte die Werte durch deutsche Uebersetzungen ersetzen. "
               "(Leerer/fehlender Wert faellt auf Englisch zurueck.)")
    write_json(os.path.join(OUT, "ru.json"), ru,
               "MOD UI text - Russian. Placeholders are English; replace the values. "
               "(An empty or missing value falls back to English.)")

    print("已导出 %d 条文案到 %s" % (len(rows), OUT))
    for n in ("en.json", "zh.json", "de.json", "ru.json"):
        print("   " + os.path.join(OUT, n))
    return 0


if __name__ == "__main__":
    sys.exit(main())
