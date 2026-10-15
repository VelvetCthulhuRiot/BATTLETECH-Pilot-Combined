"""检查工程里的文本文件有没有被"UTF-8 当成 GBK 解码再存回去"毁掉。

背景：用 PowerShell 的 `Get-Content -Raw` + `Set-Content` 做过一次文本替换，
结果把 UTF-8 的中文读成 GBK 再写回去，整个文件的中文变成乱码（而且会把
字符串里的引号也弄坏）。这个脚本用来在事后快速确认哪些文件受害。

误报说明：本脚本自己的"乱码特征表"里就含有那些乱码字，所以扫描时会把
特征表所在的那一行剔掉再判断。行尾（LF / CRLF）**不算问题** —— 本工程一直用 LF。
"""
import glob
import os
import sys

# 典型的 GBK 误读产物（UTF-8 字节被当 GBK 解码）
MARKERS = ['\u9418', '\u9422', '\u7f01', '\u60a4', '\u5a04', '\u93a9',
           '\u951b', '\u6d93', '\u5a67', '\u93c1', '\u5a75', '\u95c3']

SELF = os.path.abspath(__file__)
ROOT = os.path.dirname(os.path.dirname(SELF))


def main():
    bad = []
    checked = 0
    for pat in ('src/*.cs', 'tests/*.cs', 'mod/*.json', '*.md', 'docs/*.md',
                '*.ps1', 'tools/*.py'):
        for f in sorted(glob.glob(os.path.join(ROOT, pat))):
            if os.path.abspath(f) == SELF:
                continue
            checked += 1
            raw = open(f, 'rb').read()
            rel = os.path.relpath(f, ROOT)
            try:
                txt = raw.decode('utf-8')
            except UnicodeDecodeError as e:
                bad.append((rel, '不是合法 UTF-8: %s' % e))
                continue

            hits = [m for m in MARKERS if m in txt]
            # 顺带看看有没有 U+FFFD（替换字符），也是解码出错的痕迹
            if '\ufffd' in txt:
                hits.append('U+FFFD')
            if hits:
                bad.append((rel, 'GBK 乱码痕迹: %s' % ' '.join(repr(h) for h in hits[:4])))

    for rel, why in bad:
        print('!! %-46s %s' % (rel, why))
    print()
    if bad:
        print('检查了 %d 个文件，%d 个有问题 —— 需要重新写入。' % (checked, len(bad)))
        return 1
    print('检查了 %d 个文件：全部是合法 UTF-8，没有 GBK 乱码痕迹。' % checked)
    return 0


if __name__ == '__main__':
    sys.exit(main())
