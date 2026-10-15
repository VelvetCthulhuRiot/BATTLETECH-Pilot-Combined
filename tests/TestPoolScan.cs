using System;
using System.Collections.Generic;
using System.IO;
using PilotCombined;

/// <summary>
/// 脱机「图片池普查」：**照抄 PortraitRegistry.BuildPool 的扫描与分类逻辑**，
/// 在一个真实目录上跑一遍，打印每个性别有多少张、抽样路径、以及"名字不认识"的子目录。
///
/// 为什么要单独做这个：BuildPool 本身在 PortraitRegistry 里（那个类引用 UnityEngine，
/// 脱机跑不了），而"用户放进去的图有没有被正确分到性别"恰恰是最容易出错、
/// 又最需要在**启动游戏之前**就确认的一环（曾经真的踩过：`portraits\other\` 不被识别，
/// 16 张图全被当成"未标注"，导致非二元性别被认为没有肖像）。
///
/// 用法：
///   Scan.exe                 —— 只跑内置的临时目录断言
///   Scan.exe &lt;mod目录&gt;      —— 额外普查这个 mod 目录（它下面应有 portraits\）
/// </summary>
internal static class TestPoolScan
{
    private static int _pass, _fail;

    private static void Check(bool ok, string what, string detail)
    {
        if (ok) { _pass++; Console.WriteLine("  [ok]   " + what); }
        else { _fail++; Console.WriteLine("  [FAIL] " + what + "   <<< " + detail); }
    }

    private static void Put(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
    }

    /// <summary>
    /// 与 PortraitRegistry.BuildPool 的**扫描 + 分类**部分逐句对应。
    /// （只保留不依赖 UnityEngine 的逻辑：枚举文件、过滤扩展名、转成相对路径、按名字判性别。）
    /// </summary>
    private static void Scan(string modDir, out int cF, out int cM, out int cO, out int cU,
                             out List<string> unrecognized, out Dictionary<string, string> samples)
    {
        cF = cM = cO = cU = 0;
        unrecognized = new List<string>();
        samples = new Dictionary<string, string>();

        string dir = Path.Combine(modDir, "portraits");
        if (!Directory.Exists(dir))
        {
            Console.WriteLine("  （没有 portraits 目录：" + dir + "）");
            return;
        }

        string root = modDir.Replace('\\', '/').TrimEnd('/') + "/";
        List<string> paths = new List<string>();
        string[] files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < files.Length; i++)
        {
            string ext = Path.GetExtension(files[i]).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".dds") continue;
            string f = files[i].Replace('\\', '/');
            if (f.StartsWith(root, StringComparison.OrdinalIgnoreCase)) f = f.Substring(root.Length);
            paths.Add(f);
        }

        for (int i = 0; i < paths.Count; i++)
        {
            int slot = GenderNames.SlotFromPath(paths[i]);
            string key;
            if (slot == GenderNames.Female) { cF++; key = "女"; }
            else if (slot == GenderNames.Male) { cM++; key = "男"; }
            else if (slot == GenderNames.Other) { cO++; key = "第三类"; }
            else { cU++; key = "未标注"; }
            if (!samples.ContainsKey(key)) samples[key] = paths[i];

            string[] bad = GenderNames.UnrecognizedDirTokens(paths[i]);
            for (int j = 0; j < bad.Length; j++)
                if (!unrecognized.Contains(bad[j])) unrecognized.Add(bad[j]);
        }
    }

    private static int Main(string[] args)
    {
        Console.WriteLine("=== PilotCombined 图片池普查 ===");

        // ---------------------------------------------------------------- 自带断言
        string root = Path.Combine(Path.GetTempPath(), "PilotCombinedPoolTest");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);

        // 目录结构刻意覆盖：三种性别 + 一个"名字不认识"的目录（模拟用户把 `女性头像` 直接拖进来）
        Put(Path.Combine(root, "portraits", "female", "01.png"), "x");
        Put(Path.Combine(root, "portraits", "female", "02.png"), "x");
        Put(Path.Combine(root, "portraits", "male", "01.png"), "x");
        Put(Path.Combine(root, "portraits", "other", "01.png"), "x");
        Put(Path.Combine(root, "portraits", "other", "02.png"), "x");
        Put(Path.Combine(root, "portraits", "other", "03.png"), "x");
        Put(Path.Combine(root, "portraits", "女性头像", "01.png"), "x");
        Put(Path.Combine(root, "portraits", "male", "readme.txt"), "不是图片，应被忽略");

        int cF, cM, cO, cU;
        List<string> bad;
        Dictionary<string, string> samples;
        Scan(root, out cF, out cM, out cO, out cU, out bad, out samples);

        Console.WriteLine();
        Console.WriteLine("[1] 临时目录（female 2 / male 1 / other 3 / 女性头像 1 / txt 1）");
        Console.WriteLine("      分类结果：女 " + cF + " / 男 " + cM + " / 第三类 " + cO + " / 未标注 " + cU);
        foreach (KeyValuePair<string, string> kv in samples)
            Console.WriteLine("      抽样 " + kv.Key + " = " + kv.Value);

        Check(cF == 2, "female 目录 2 张 → 女 2", "实际 " + cF);
        Check(cM == 1, "male 目录 1 张 → 男 1", "实际 " + cM);
        Check(cO == 3, "★ other 目录 3 张 → 第三类 3（这就是踩过的坑）", "实际 " + cO);
        Check(cU == 1, "名字不认识的目录里的图 → 未标注 1", "实际 " + cU);
        Check(bad.Count == 1 && bad[0] == "女性头像",
              "★ 名字不认识的子目录被列出（会触发启动报警）",
              bad.Count == 0 ? "(空)" : string.Join(",", bad.ToArray()));
        Check(samples.ContainsKey("第三类") && samples["第三类"] == "portraits/other/01.png",
              "第三类的抽样路径正确",
              samples.ContainsKey("第三类") ? samples["第三类"] : "-");

        Directory.Delete(root, true);

        // ---------------------------------------------------------------- 真实目录普查
        if (args.Length > 0 && !string.IsNullOrEmpty(args[0]))
        {
            string modDir = args[0];
            Console.WriteLine();
            Console.WriteLine("[2] 真实目录普查：" + Path.Combine(modDir, "portraits"));
            Scan(modDir, out cF, out cM, out cO, out cU, out bad, out samples);
            Console.WriteLine("      共 " + (cF + cM + cO + cU) + " 张 —— 女 " + cF + " / 男 " + cM
                              + " / 第三类 " + cO + " / 未标注 " + cU);
            foreach (KeyValuePair<string, string> kv in samples)
                Console.WriteLine("      抽样 " + kv.Key + " = " + kv.Value);
            if (bad.Count > 0)
            {
                Console.WriteLine("      ⚠ 名字不认识的子目录：" + string.Join("、", bad.ToArray())
                                  + "  → 这些图会被当成『未标注』，请改名成 female / male / other");
            }
            else
            {
                Console.WriteLine("      没有名字不认识的子目录 ✓");
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== 结果：" + _pass + " 通过 / " + _fail + " 失败 ===");
        return _fail == 0 ? 0 : 1;
    }
}
