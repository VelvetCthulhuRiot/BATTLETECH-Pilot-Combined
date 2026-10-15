#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
iltool.py —— 无依赖的 .NET 元数据 / IL 读取器（只用 Python 标准库）

为什么需要它：
  常见的反编译三方库 dnfile / dncil 在 Python 3.14 上会因为
  `string.templatelib.Template` 的语义变化而随机或必然报错
  （AttributeError: 'string.templatelib.Template' object has no attribute '_member_map_'）。
  本工具自己解析 PE / CLI 元数据 / #~ 表流 / #Strings / #Blob / #US，不依赖任何第三方包，
  在这台机器上稳定可用。

用法：
  python iltool.py types    <名称子串>                   列出匹配的类型（含可见性）
  python iltool.py members  <完整类型名>                 列出该类型的字段与方法（含参数名/可见性/签名）
  python iltool.py il       <完整类型名> <方法1,方法2>   反汇编指定方法（解析方法/字段/字符串/类型 token）
  python iltool.py sig      <完整类型名> <方法1,方法2>   解析方法签名 blob 为可读类型串（含泛型/委托）
  python iltool.py findstr  <字符串子串>                 在 #US 字符串堆里搜索（用于定位用法）

示例：
  python iltool.py sig   BattleTech.UI.GenericPopupBuilder AddInput
  python iltool.py types GenericPopup

注意：
  * 嵌套类型（如 PortraitManager+PortraitSizes）在元数据里 Namespace 为空、Name 是简单名，
    本工具会标成 <nested>，用简单名搜索即可。
  * 只解析到 0x08(Param) 表为止的偏移；这已经涵盖本工具需要的全部表。
  * `sig` 模式额外解析 TypeSpec（0x1B）与 GenericInst（0x15），足以看懂
    Action<string> / Action<int> / List<string> 这类签名。
"""
import struct
import sys
import os


def _default_game_assembly():
    """找游戏本体的 Assembly-CSharp.dll。

    顺序：环境变量 %BATTLETECH_DIR% → Steam 库列表（注册表 + libraryfolders.vdf）
    → 几个常见默认位置。找不到就返回 None（下面会给出明确报错）。
    以前这里写死了一台机器的路径 —— 公开仓库里既没用又泄露本机信息。
    """
    rel = os.path.join("BattleTech_Data", "Managed", "Assembly-CSharp.dll")

    def ok(root):
        if not root:
            return None
        p = os.path.join(root, rel)
        return p if os.path.isfile(p) else None

    hit = ok(os.environ.get("BATTLETECH_DIR"))
    if hit:
        return hit

    roots = []
    # 注册表里的 Steam 安装位置（HKCU\Software\Valve\Steam\SteamPath）
    try:
        import winreg
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam") as k:
            roots.append(winreg.QueryValueEx(k, "SteamPath")[0].replace("/", "\\"))
    except Exception:
        pass
    roots += [r"C:\Program Files (x86)\Steam", r"C:\Program Files\Steam",
              r"D:\Steam", r"E:\Steam", r"F:\Steam"]

    for sr in dict.fromkeys(roots):
        if not os.path.isdir(sr):
            continue
        libs = [sr]
        vdf = os.path.join(sr, "steamapps", "libraryfolders.vdf")
        if os.path.isfile(vdf):
            try:
                import re
                for line in open(vdf, encoding="utf-8", errors="replace"):
                    m = re.search(r'"path"\s*"([^"]+)"', line)
                    if m:
                        libs.append(m.group(1).replace("\\\\", "\\"))
            except Exception:
                pass
        for lib in dict.fromkeys(libs):
            hit = ok(os.path.join(lib, "steamapps", "common", "BATTLETECH"))
            if hit:
                return hit

    for cand in (r"C:\Program Files (x86)\Steam\steamapps\common\BATTLETECH",
                 r"C:\Program Files\GOG Galaxy\Games\BATTLETECH",
                 r"C:\GOG Games\BATTLETECH"):
        hit = ok(cand)
        if hit:
            return hit
    return None


# 默认读游戏本体；想检查别的程序集（例如我们自己编出来的 PilotCombined.dll）
# 就设环境变量：$env:ILTOOL_DLL="C:\...\PilotCombined.dll"
PATH = os.environ.get("ILTOOL_DLL") or _default_game_assembly()
if not PATH or not os.path.isfile(PATH):
    sys.stderr.write(
        "找不到游戏程序集。请设 %%BATTLETECH_DIR%% 指向游戏安装目录，"
        "或用 $env:ILTOOL_DLL 指定要分析的 dll。\n")
    sys.exit(2)

data = open(PATH, "rb").read()
u2 = lambda o: struct.unpack_from("<H", data, o)[0]
u4 = lambda o: struct.unpack_from("<I", data, o)[0]

# ---------------- PE ----------------
e = u4(0x3C)
coff = e + 4
nsec = u2(coff + 2)
opt = coff + 20
magic = u2(opt)
dd = opt + (96 if magic == 0x10B else 112)
cli_rva = u4(dd + 14 * 8)
secs = []
so = opt + u2(coff + 16)
for i in range(nsec):
    b = so + i * 40
    secs.append((u4(b + 12), max(u4(b + 8), u4(b + 16)), u4(b + 20)))


def r2o(r):
    for va, sz, pr in secs:
        if va <= r < va + sz:
            return r - va + pr
    raise ValueError("rva 0x%x not mapped" % r)


# ---------------- 元数据根 / 流 ----------------
cli = r2o(cli_rva)
md = r2o(u4(cli + 8))
vl = u4(md + 12)
p = md + 16 + ((vl + 3) // 4) * 4
nstreams = u2(p + 2)
p += 4
streams = {}
for i in range(nstreams):
    o = u4(p); s = u4(p + 4)
    ne = data.index(b"\0", p + 8)
    streams[data[p + 8:ne].decode("ascii")] = (md + o, s)
    p = (ne + 1 + 3) & ~3

tab_off, _ = streams["#~"]
str_off, _ = streams["#Strings"]
blob_off, _ = streams["#Blob"]
us_off, _ = streams["#US"]

# ---------------- #~ 表头 ----------------
heap = data[tab_off + 6]
valid = struct.unpack_from("<Q", data, tab_off + 8)[0]
q = tab_off + 24
rows = {}
for i in range(64):
    if valid & (1 << i):
        rows[i] = u4(q)
        q += 4
        tables_start = q
S = 4 if heap & 0x01 else 2
G = 4 if heap & 0x02 else 2
B = 4 if heap & 0x04 else 2


def sa(idx):
    z = data.index(b"\0", str_off + idx)
    return data[str_off + idx:z].decode("utf-8", "replace")


def blob_at(idx):
    b0 = data[blob_off + idx]
    if b0 & 0x80 == 0:
        L, n = b0, 1
    elif b0 & 0xC0 == 0x80:
        L, n = ((b0 & 0x3F) << 8) | data[blob_off + idx + 1], 2
    else:
        L, n = ((b0 & 0x1F) << 24) | (data[blob_off + idx + 1] << 16) | \
               (data[blob_off + idx + 2] << 8) | data[blob_off + idx + 3], 4
    return data[blob_off + idx + n: blob_off + idx + n + L]


def us_at(offset):
    p0 = us_off + offset
    b0 = data[p0]
    if b0 & 0x80 == 0:
        L, n = b0, 1
    elif b0 & 0xC0 == 0x80:
        L, n = ((b0 & 0x3F) << 8) | data[p0 + 1], 2
    else:
        L, n = ((b0 & 0x1F) << 24) | (data[p0 + 1] << 16) | (data[p0 + 2] << 8) | data[p0 + 3], 4
    raw = data[p0 + n:p0 + n + L]
    body = raw[:-1] if L % 2 == 1 else raw
    return body.decode("utf-16-le", "replace")


def si(t): return 4 if rows.get(t, 0) > 0xFFFF else 2


def ci(tabs, bits):
    m = max(rows.get(t, 0) for t in tabs)
    return 2 if m < (1 << (16 - bits)) else 4


TDOR = ci([2, 1, 27], 2)
RS = ci([0, 26, 35, 1], 2)
sizes = {
    0x00: 2 + S + G + G + G,                      # Module
    0x01: RS + S + S,                              # TypeRef
    0x02: 4 + S + S + TDOR + si(4) + si(6),        # TypeDef
    0x03: si(4),                                   # FieldPtr
    0x04: 2 + S + B,                               # Field
    0x05: si(6),                                   # MethodPtr
    0x06: 4 + 2 + 2 + S + B + si(8),               # MethodDef
    0x07: si(8),                                   # ParamPtr
    0x08: 2 + 2 + S,                               # Param
    0x09: si(2),                                   # InterfaceImpl
    0x0A: si(2) + si(6) + si(1),                   # MemberRef
    0x0B: 2 + si(2) + B,                           # Constant
    0x0C: 2 + 2 + si(6) + B,                       # CustomAttribute
    0x0D: si(6) + si(10),                          # FieldMarshal
    0x0E: 2 + si(6),                               # DeclSecurity
    0x0F: 2 + 2 + si(2) + si(1),                   # ClassLayout
    0x10: 4 + si(4),                               # FieldLayout
    0x11: si(2),                                   # StandAloneSig
    0x12: si(2) + si(2),                           # EventMap
    0x14: 2 + S + si(1),                           # Event
    0x15: si(2) + si(1),                           # PropertyMap
    0x17: 2 + S + B,                               # Property
    0x18: 2 + si(6),                               # MethodSemantics
    0x19: si(2) + si(1) + si(1),                   # MethodImpl
    0x1A: si(2),                                   # ModuleRef
    0x1B: B,                                       # TypeSpec
    0x1C: 2 + si(6),                               # ImplMap
    0x1D: 4 + si(4),                               # FieldRVA
    0x20: 4 + 2 + 2 + 2 + 2 + 4 + B,               # Assembly
    0x23: 2 + S + B,                               # AssemblyRef (简化，足够跳过)
    0x26: si(2),                                   # NestedClass
    0x27: si(2) + si(1) + si(1),                   # GenericParam
    0x28: si(6),                                   # MethodSpec
    0x29: si(2) + si(2),                           # GenericParamConstraint
}
offs = {}
cur = tables_start
for i in range(64):
    if i in rows:
        offs[i] = cur
        if i not in sizes:
            break
        cur += rows[i] * sizes[i]

VIS = {0: "public", 1: "public", 2: "nested-public", 3: "nested-private",
       4: "nested-family", 5: "nested-assembly", 6: "nested-famandassem",
       7: "nested-famorassem"}


def read_typedef(i):
    o = offs[0x02] + (i - 1) * sizes[0x02]
    fl = u4(o); o += 4
    nm = sa(u4(o)); o += S
    ns = sa(u4(o)); o += S
    o += TDOR
    o += si(4)
    ml = struct.unpack_from("<I" if si(6) == 4 else "<H", data, o)[0]
    return fl, nm, ns, ml


def read_typedef_ext(i):
    """返回 (flags, name, namespace, extends_token, fieldlist, methodlist)"""
    o = offs[0x02] + (i - 1) * sizes[0x02]
    fl = u4(o); o += 4
    nm = sa(u4(o)); o += S
    ns = sa(u4(o)); o += S
    ext = struct.unpack_from("<I" if TDOR == 4 else "<H", data, o)[0]; o += TDOR
    f0 = struct.unpack_from("<I" if si(4) == 4 else "<H", data, o)[0]; o += si(4)
    m0 = struct.unpack_from("<I" if si(6) == 4 else "<H", data, o)[0]
    return fl, nm, ns, ext, f0, m0


def read_method(i):
    """MethodDef 行布局：RVA(4) ImplFlags(2) Flags(2) Name(S) Signature(B) ParamList(si(8))"""
    o = offs[0x06] + (i - 1) * sizes[0x06]
    rva = u4(o)
    fl = u2(o + 6)
    nm = sa(u4(o + 8))
    sig = blob_at(u4(o + 8 + S))
    pl = struct.unpack_from("<I" if si(8) == 4 else "<H", data, o + 8 + S + B)[0]
    return rva, fl, nm, sig, pl


def read_field(i):
    o = offs[0x04] + (i - 1) * sizes[0x04]
    o += 2
    return sa(u4(o))


def read_param(i):
    o = offs[0x08] + (i - 1) * sizes[0x08]
    o += 4
    return sa(u4(o))


def read_field_sig(i):
    """Field 行布局：Flags(2) Name(S) Signature(B)。原版工具漏了开头的 Flags(2)。"""
    o = offs[0x04] + (i - 1) * sizes[0x04] + 2
    nm = sa(u4(o))
    return nm, blob_at(u4(o + S))


# method index -> 所属类型
owner = {}
type_end = {}
for i in range(1, rows.get(0x02, 0) + 1):
    fl, nm, ns, ml = read_typedef(i)
    full = (ns + "." + nm) if ns else nm
    nxt = read_typedef(i + 1)[3] if i < rows[0x02] else rows.get(0x06, 0) + 1
    type_end[i] = nxt
    for mi in range(ml, nxt):
        owner[mi] = full


def method_body(rva):
    o = r2o(rva)
    b0 = data[o]
    if (b0 & 3) == 2:
        return data[o + 1:o + 1 + (b0 >> 2)]
    if (b0 & 3) == 3:
        h = 4 * (u2(o) >> 12)
        return data[o + h:o + h + u4(o + 4)]
    return b""


def params_of(mi):
    _, _, _, _, pl = read_method(mi)
    if not pl:
        return []
    pend = read_method(mi + 1)[4] if mi < rows.get(0x06, 0) else rows.get(0x08, 0) + 1
    return [x for x in (read_param(pi) for pi in range(pl, pend)) if x]


def find_type(name):
    for i in range(1, rows.get(0x02, 0) + 1):
        fl, nm, ns, ml = read_typedef(i)
        full = (ns + "." + nm) if ns else nm
        if full == name:
            return i, fl, nm, ns, ml
    return None


# ---------------------------------------------------------------- 签名解析
PRIM = {0x01: "void", 0x02: "bool", 0x03: "char", 0x04: "sbyte", 0x05: "byte",
        0x06: "short", 0x07: "ushort", 0x08: "int", 0x09: "uint", 0x0A: "long",
        0x0B: "ulong", 0x0C: "float", 0x0D: "double", 0x0E: "string",
        0x16: "TypedReference", 0x18: "IntPtr", 0x19: "UIntPtr", 0x1C: "object"}


def type_name_from_token(tok, depth=0):
    if depth > 6:
        return "..."
    tb, ix = (tok >> 24) & 0xFF, tok & 0xFFFFFF
    if tb == 0x01 and 1 <= ix <= rows.get(0x01, 0):        # TypeRef
        o = offs[0x01] + (ix - 1) * sizes[0x01] + RS
        n = sa(u4(o)); o += S
        ns = sa(u4(o))
        return (ns + "." + n) if ns else n
    if tb == 0x02 and 1 <= ix <= rows.get(0x02, 0):        # TypeDef
        fl, n, ns, _ = read_typedef(ix)
        return (ns + "." + n) if ns else n
    if tb == 0x1B and 1 <= ix <= rows.get(0x1B, 0):        # TypeSpec
        o = offs[0x1B] + (ix - 1) * sizes[0x1B]
        return parse_type(blob_at(u4(o)), 0, depth + 1)[0]
    return "tok(0x%08x)" % tok


def parse_type(blob, i, depth=0):
    e = blob[i]; i += 1
    if e in PRIM:
        return PRIM[e], i
    if e == 0x0F:                                          # PTR
        n, i = parse_type(blob, i, depth)
        return n + "*", i
    if e == 0x10:                                          # BYREF
        n, i = parse_type(blob, i, depth)
        return "ref " + n, i
    if e in (0x11, 0x12):                                  # VALUETYPE / CLASS
        tok = struct.unpack_from("<I", blob, i)[0]; i += 4
        return type_name_from_token(tok, depth), i
    if e == 0x13:                                          # VAR (泛型类型参数)
        n, i = readcomp(blob, i)
        return "!" + str(n), i
    if e == 0x1E:                                          # MVAR (泛型方法参数)
        n, i = readcomp(blob, i)
        return "!!" + str(n), i
    if e == 0x15:                                          # GENERICINST
        kind = blob[i]; i += 1
        tok = struct.unpack_from("<I", blob, i)[0]; i += 4
        base = type_name_from_token(tok, depth)
        argc, i = readcomp(blob, i)
        args = []
        for _ in range(argc):
            a, i = parse_type(blob, i, depth + 1)
            args.append(a)
        if "`" in base:
            base = base.split("`")[0]
        return base + "<" + ", ".join(args) + ">", i
    if e == 0x14:                                          # ARRAY
        n, i = parse_type(blob, i, depth)
        rank, i = readcomp(blob, i)
        nsizes, i = readcomp(blob, i)
        for _ in range(nsizes):
            _, i = readcomp(blob, i)
        nlob, i = readcomp(blob, i)
        for _ in range(nlob):
            _, i = readcomp(blob, i)
        return n + "[" + "," * (rank - 1) + "]", i
    if e == 0x1F:                                          # SZARRAY
        n, i = parse_type(blob, i, depth)
        return n + "[]", i
    if e == 0x45:                                          # PINNED
        return parse_type(blob, i, depth)
    return "elem(0x%02x)" % e, i


def readcomp(blob, i):
    b0 = blob[i]
    if b0 & 0x80 == 0:
        return b0, i + 1
    if b0 & 0xC0 == 0x80:
        return ((b0 & 0x3F) << 8) | blob[i + 1], i + 2
    return ((b0 & 0x1F) << 24) | (blob[i + 1] << 16) | (blob[i + 2] << 8) | blob[i + 3], i + 4


def method_sig_readable(sig):
    if len(sig) < 2:
        return "?"
    flags = sig[0]
    i = 1
    if flags & 0x10:                                       # GENERIC：先读泛型参数个数
        argc, i = readcomp(sig, i)
        for _ in range(argc):
            i += 2
    pc, i = readcomp(sig, i)                               # 再读形参个数
    ret, i = parse_type(sig, i)
    ps = []
    for _ in range(pc):
        n, i = parse_type(sig, i)
        ps.append(n)
    return ret + " (" + ", ".join(ps) + ")"


mode = sys.argv[1] if len(sys.argv) > 1 else ""

if mode == "types":
    key = sys.argv[2].lower()
    n = 0
    for i in range(1, rows.get(0x02, 0) + 1):
        fl, nm, ns, ml = read_typedef(i)
        full = (ns + "." + nm) if ns else nm
        if key in full.lower():
            tag = "" if ns else "   <nested? 用简单名搜索>"
            print("%-70s %s%s" % (full, VIS.get(fl & 7, "?"), tag))
            n += 1
    print("共 %d 个匹配" % n)

elif mode == "members":
    t = find_type(sys.argv[2])
    if not t:
        print("找不到类型 " + sys.argv[2])
        sys.exit(1)
    i, fl, nm, ns, ml = t
    full = (ns + "." + nm) if ns else nm
    print("TYPE %s   visibility=%s" % (full, VIS.get(fl & 7, "?")))
    nxt = type_end[i]
    fl_start = None
    for j in range(1, rows.get(0x02, 0) + 1):
        if j == i:
            o = offs[0x02] + (j - 1) * sizes[0x02] + 4 + S + S + TDOR
            fl_start = struct.unpack_from("<I" if si(4) == 4 else "<H", data, o)[0]
    if i < rows[0x02]:
        o = offs[0x02] + i * sizes[0x02] + 4 + S + S + TDOR
        fl_end = struct.unpack_from("<I" if si(4) == 4 else "<H", data, o)[0]
    else:
        fl_end = rows.get(0x04, 0) + 1
    print("  -- fields --")
    if fl_start:
        for fi in range(fl_start, fl_end):
            fn, fs = read_field_sig(fi)
            try:
                fsr = method_sig_readable(fs)
            except Exception:
                fsr = "?"
            print("     %-38s : %s" % (fn, fsr))
    print("  -- methods --")
    for mi in range(ml, nxt):
        rva, mfl, mn, sig, pl = read_method(mi)
        ps = params_of(mi)
        print("     %-40s static=%-5s public=%-5s (%s)" % (
            mn, bool(mfl & 0x10), bool(mfl & 0x6), ", ".join(ps)))

elif mode == "sig":
    tname = sys.argv[2]
    want = set(sys.argv[3].split(","))
    t = find_type(tname)
    if not t:
        print("找不到类型 " + tname)
        sys.exit(1)
    i, fl, nm, ns, ml = t
    full = (ns + "." + nm) if ns else nm
    for mi in range(ml, type_end[i]):
        rva, mfl, mn, sig, pl = read_method(mi)
        if mn not in want:
            continue
        try:
            r = method_sig_readable(sig)
        except Exception as ex:
            r = "解析失败: %s  raw=%s" % (ex, sig.hex(" "))
        print("%s::%s  ->  %s" % (full, mn, r))

elif mode == "findstr":
    key = sys.argv[2]
    n = 0
    size = streams["#US"][1]
    off = 0
    while off < size and n < 200:
        b0 = data[us_off + off]
        if b0 & 0x80 == 0:
            L, hn = b0, 1
        elif b0 & 0xC0 == 0x80:
            L, hn = ((b0 & 0x3F) << 8) | data[us_off + off + 1], 2
        else:
            break
        if L > 0:
            raw = data[us_off + off + hn: us_off + off + hn + L]
            body = raw[:-1] if L % 2 == 1 else raw
            s = body.decode("utf-16-le", "replace")
            if key.lower() in s.lower():
                print("0x%06x  %r" % (off, s))
                n += 1
        off += hn + L
    print("共 %d 条" % n)

elif mode == "il":
    tname = sys.argv[2]
    want = set(sys.argv[3].split(","))
    t = find_type(tname)
    if not t:
        print("找不到类型 " + tname)
        sys.exit(1)
    i, fl, nm, ns, ml = t
    full = (ns + "." + nm) if ns else nm
    for mi in range(ml, type_end[i]):
        rva, mfl, mn, sig, pl = read_method(mi)
        if mn not in want:
            continue
        print("=" * 78)
        print("%s::%s   static=%s public=%s" % (full, mn, bool(mfl & 0x10), bool(mfl & 0x6)))
        print("    sig    : " + sig.hex(" "))
        try:
            print("    sigR   : " + method_sig_readable(sig))
        except Exception:
            pass
        print("    params : " + ", ".join(params_of(mi)))
        if not rva:
            continue
        body = method_body(rva)
        for k in range(len(body) - 4):
            op = body[k]
            if op not in (0x28, 0x6F, 0x7B, 0x7D, 0x7E, 0x80, 0x72, 0x73):
                continue
            tok = struct.unpack_from("<I", body, k + 1)[0]
            tb, ix = tok >> 24, tok & 0xFFFFFF
            info = None
            if tb == 0x06 and 1 <= ix <= rows.get(0x06, 0):
                info = "M " + owner.get(ix, "?") + "::" + read_method(ix)[2]
            elif tb == 0x04 and 1 <= ix <= rows.get(0x04, 0):
                info = "F " + read_field(ix)
            elif tb == 0x70:
                info = "S " + repr(us_at(ix))
            elif tb == 0x01 and 1 <= ix <= rows.get(0x01, 0):
                o2 = offs[0x01] + (ix - 1) * sizes[0x01] + RS
                n2 = sa(u4(o2)); o2 += S
                ns2 = sa(u4(o2))
                info = "T " + ((ns2 + "." + n2) if ns2 else n2)
            elif tb == 0x02 and 1 <= ix <= rows.get(0x02, 0):
                fl2, nm2, ns2, _ = read_typedef(ix)
                info = "T " + ((ns2 + "." + nm2) if ns2 else nm2)
            if info:
                print("    0x%04x %-8s %s" % (k, hex(op), info))
else:
    print(__doc__)
