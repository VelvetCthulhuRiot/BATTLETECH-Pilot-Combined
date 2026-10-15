using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using PilotCombined;

/// <summary>
/// 脱机测试：**配置迁移**（旧的 genderratio.json / portraits.json → combined.json）。
///
/// 为什么要单独测它：迁移是 M2 里唯一会"动用户数据"的代码，一旦写错
/// 用户调好的比例就没了 —— 而它在游戏里很难复现（只有第一次启动才走这条路）。
/// CombinedConfig / GenderRatioConfig / PortraitConfig / Log 都**不依赖 UnityEngine**，
/// 所以可以完全离线验证，不用开游戏。
///
/// 用法：Math.exe &lt;工程根目录&gt;   （靠它找到 mod\combined.json 当模板）
/// </summary>
internal static class TestConfig
{
    private static int _pass, _fail;

    private static void Check(bool ok, string what, string detail)
    {
        if (ok) { _pass++; Console.WriteLine("  [ok]   " + what); }
        else { _fail++; Console.WriteLine("  [FAIL] " + what + "   <<< " + detail); }
    }

    private static void Put(string path, string text)
    {
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    private static int Main(string[] args)
    {
        Console.WriteLine("=== PilotCombined 配置迁移脱机测试 ===");

        string projectRoot = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        string template = Path.Combine(projectRoot, "mod", "combined.json");
        if (!File.Exists(template))
        {
            Console.WriteLine("找不到模板：" + template);
            return 2;
        }

        string root = Path.Combine(Path.GetTempPath(), "PilotCombinedCfgTest");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);

        // ---------------------------------------------------------------- 场景 1
        Console.WriteLine();
        Console.WriteLine("[1] 有旧配置 ⇒ 迁移，且用户的值必须被带过来、模板注释必须保留");

        File.Copy(template, Path.Combine(root, "combined.json.template"));

        // 故意用**和模板不同**的值，才能证明"是迁移过来的"而不是"模板默认值"
        Put(Path.Combine(root, "genderratio.json"), @"{
  ""schema_version"": 5,
  ""gender_ratio"": {
    ""enabled"": true,
    ""other_enabled"": true,
    ""other_percent"": 10.0,
    ""female_percent"": 77.0,
    ""male_percent"": 13.0,
    ""other_label"": ""其他性别"",
    ""lock_ratio"": true,
    ""lock_ratio_female"": 3,
    ""lock_ratio_male"": 1
  },
  ""popup"": {
    ""enabled"": false,
    ""hotkey"": ""F10"",
    ""step_percent"": 5,
    ""font_file"": """",
    ""female_label"": ""女"",
    ""male_label"": ""男""
  }
}");

        Put(Path.Combine(root, "portraits.json"), @"{
  ""enabled"": false,
  ""autoScan"": false,
  ""neutralize3DCustomization"": true,
  ""poolMode"": ""sequential"",
  ""defaultPortrait"": ""portraits/female/01.png"",
  ""pool"": [ ""portraits/female/01.png"" ],
  ""rules"": [ { ""match"": ""callsign"", ""value"": ""Dekker"", ""image"": ""portraits/male/03.png"", ""gender"": ""Male"" } ]
}");

        CombinedConfig.ResetForTesting();
        CombinedConfig.Load(root);

        Check(CombinedConfig.Migrated, "识别出需要迁移", "Migrated=false");
        Check(CombinedConfig.MigrationNotes.Count == 2,
              "两份旧配置都被迁移（MigrationNotes=2）",
              "实际 " + CombinedConfig.MigrationNotes.Count);
        foreach (string n in CombinedConfig.MigrationNotes) Console.WriteLine("        · " + n);

        string outPath = Path.Combine(root, "combined.json");
        Check(File.Exists(outPath), "写出了 combined.json", "文件不存在");

        // --- 内存里的值 ---
        Check(CombinedConfig.GenderRatio.settings.femalePercent == 77f,
              "迁移后 女 = 77%", "实际 " + CombinedConfig.GenderRatio.settings.femalePercent);
        Check(CombinedConfig.GenderRatio.settings.malePercent == 13f,
              "迁移后 男 = 13%", "实际 " + CombinedConfig.GenderRatio.settings.malePercent);
        Check(CombinedConfig.GenderRatio.settings.otherPercent == 10f,
              "迁移后 第三类 = 10%", "实际 " + CombinedConfig.GenderRatio.settings.otherPercent);
        Check(CombinedConfig.GenderRatio.settings.otherLabel == "非二元性别或无性别",
              "旧默认显示名「其他性别」自动升级为「非二元性别或无性别」（中文没被 GBK 弄坏）",
              "实际 " + CombinedConfig.GenderRatio.settings.otherLabel);
        Check(CombinedConfig.GenderRatio.settings.lockRatio
              && CombinedConfig.GenderRatio.settings.lockRatioW == 3
              && CombinedConfig.GenderRatio.settings.lockRatioM == 1,
              "迁移后 锁定比例 = 3:1",
              CombinedConfig.GenderRatio.settings.lockRatioW + ":" + CombinedConfig.GenderRatio.settings.lockRatioM);
        Check(CombinedConfig.GenderRatio.hotkey == "F10", "迁移后 热键 = F10",
              "实际 " + CombinedConfig.GenderRatio.hotkey);
        Check(CombinedConfig.GenderRatio.stepPercent == 5f, "迁移后 步长 = 5",
              "实际 " + CombinedConfig.GenderRatio.stepPercent);
        Check(!CombinedConfig.GenderRatio.popupEnabled, "迁移后 popup.enabled = false",
              "实际 " + CombinedConfig.GenderRatio.popupEnabled);

        Check(!CombinedConfig.Portraits.enabled, "迁移后 portraits.enabled = false",
              "实际 " + CombinedConfig.Portraits.enabled);
        Check(!CombinedConfig.Portraits.autoScan, "迁移后 portraits.autoScan = false",
              "实际 " + CombinedConfig.Portraits.autoScan);
        Check(CombinedConfig.Portraits.neutralize3DCustomization,
              "迁移后 neutralize3DCustomization = true", "实际 false");
        Check(CombinedConfig.Portraits.poolMode == "sequential",
              "迁移后 portraits.poolMode = sequential", "实际 " + CombinedConfig.Portraits.poolMode);
        Check(CombinedConfig.Portraits.pool.Count == 1 && CombinedConfig.Portraits.rules.Count == 1,
              "迁移后 图片池 1 张 / 规则 1 条",
              CombinedConfig.Portraits.pool.Count + " / " + CombinedConfig.Portraits.rules.Count);

        // --- 落盘的文件：注释要保留、值要对 ---
        string text = File.ReadAllText(outPath, Encoding.UTF8);
        Check(text.Contains("//gender_ratio"), "写出的文件保留了模板注释（//gender_ratio）", "找不到注释键");
        Check(text.Contains("//portraits"), "写出的文件保留了模板注释（//portraits）", "找不到注释键");
        try
        {
            JObject jo = JObject.Parse(text);
            Check((float)jo["gender_ratio"]["female_percent"] == 77f,
                  "落盘文件的 gender_ratio.female_percent = 77",
                  jo["gender_ratio"]["female_percent"].ToString());
            Check((bool)jo["portraits"]["enabled"] == false,
                  "落盘文件的 portraits.enabled = false", jo["portraits"]["enabled"].ToString());
            Check(jo["portraits"]["rules"].Type == JTokenType.Array
                  && ((JArray)jo["portraits"]["rules"]).Count == 1,
                  "落盘文件的 portraits.rules 是 1 条数组",
                  jo["portraits"]["rules"].ToString());
        }
        catch (Exception e)
        {
            Check(false, "落盘文件是合法 JSON", e.Message);
        }

        // ---------------------------------------------------------------- 场景 2
        Console.WriteLine();
        Console.WriteLine("[2] combined.json 已存在 ⇒ 必须直接用它，不再迁移、不被旧文件覆盖");

        string root2 = root + "\\case2";
        Directory.CreateDirectory(root2);
        File.Copy(template, Path.Combine(root2, "combined.json.template"));
        // 故意放一份"值完全不同"的旧配置：如果它被读了，就说明优先级错了
        Put(Path.Combine(root2, "genderratio.json"), @"{ ""gender_ratio"": { ""female_percent"": 1, ""male_percent"": 99 } }");
        // 再把模板拷成 combined.json，并手动改成 25/75 来代表"用户已迁移后的配置"
        string c2 = Path.Combine(root2, "combined.json");
        JObject baseObj = JObject.Parse(File.ReadAllText(template));
        baseObj["gender_ratio"]["female_percent"] = 25;
        baseObj["gender_ratio"]["male_percent"] = 75;
        Put(c2, baseObj.ToString());

        CombinedConfig.ResetForTesting();
        CombinedConfig.Load(root2);

        Check(!CombinedConfig.Migrated, "没有触发迁移", "Migrated=true");
        Check(CombinedConfig.GenderRatio.settings.femalePercent == 25f,
              "用的是 combined.json 里的 25%（不是旧文件的 1%）",
              "实际 " + CombinedConfig.GenderRatio.settings.femalePercent);
        Check(CombinedConfig.MigrationNotes.Count == 0, "MigrationNotes 为空",
              "实际 " + CombinedConfig.MigrationNotes.Count);

        // ---------------------------------------------------------------- 场景 3
        Console.WriteLine();
        Console.WriteLine("[3] 窗口「应用并写回配置」⇒ 只改 gender_ratio，portraits 段与注释必须原样保留");

        CombinedConfig.ResetForTesting();
        CombinedConfig.Load(root);                 // 场景 1 迁移出来的那个文件

        var s3 = CombinedConfig.GenderRatio.settings.Clone();
        s3.otherEnabled = false;
        s3.otherPercent = 0f;
        s3.femalePercent = 100f;
        s3.malePercent = 0f;
        Check(CombinedConfig.SaveGenderRatio(s3), "写回返回 true", "返回 false");

        string t3 = File.ReadAllText(outPath, Encoding.UTF8);
        try
        {
            JObject j3 = JObject.Parse(t3);
            Check((float)j3["gender_ratio"]["female_percent"] == 100f,
                  "写回后 gender_ratio.female_percent = 100",
                  j3["gender_ratio"]["female_percent"].ToString());
            Check(j3["portraits"] != null, "portraits 段还在", "整段消失了");
            Check((bool)j3["portraits"]["enabled"] == false,
                  "写回没有冲掉 portraits.enabled",
                  j3["portraits"]["enabled"] == null ? "null" : j3["portraits"]["enabled"].ToString());
            Check((string)j3["portraits"]["defaultPortrait"] == "portraits/female/01.png",
                  "写回没有冲掉 portraits.defaultPortrait",
                  j3["portraits"]["defaultPortrait"] == null ? "null" : j3["portraits"]["defaultPortrait"].ToString());
            Check(j3["portraits"]["rules"] != null && ((JArray)j3["portraits"]["rules"]).Count == 1,
                  "写回没有冲掉 portraits.rules", "rules 丢了");
            Check(j3["popup"] != null && (string)j3["popup"]["hotkey"] == "F10",
                  "写回没有冲掉 popup.hotkey", "popup 段丢了或值变了");
            Check(t3.Contains("//portraits") && t3.Contains("//gender_ratio"),
                  "写回保留了模板注释", "注释键丢了");
        }
        catch (Exception e)
        {
            Check(false, "写回后的文件是合法 JSON", e.Message);
        }

        // ---------------------------------------------------------------- 场景 4
        Console.WriteLine();
        Console.WriteLine("[4] 旧配置里用的是**自定义**显示名 ⇒ 必须原样保留，不能被『升级』掉");

        string root4 = Path.Combine(root, "case4");
        Directory.CreateDirectory(root4);
        File.Copy(template, Path.Combine(root4, "combined.json.template"));
        Put(Path.Combine(root4, "genderratio.json"),
            @"{ ""gender_ratio"": { ""enabled"": true, ""other_enabled"": true,
                 ""other_percent"": 10, ""female_percent"": 60, ""male_percent"": 30,
                 ""other_label"": ""机械驾驶员"" } }");

        CombinedConfig.ResetForTesting();
        CombinedConfig.Load(root4);

        Check(CombinedConfig.GenderRatio.settings.otherLabel == "机械驾驶员",
              "自定义显示名「机械驾驶员」被保留",
              "实际 " + CombinedConfig.GenderRatio.settings.otherLabel);
        Check(CombinedConfig.GenderRatio.settings.femalePercent == 60f,
              "同一份配置的 female_percent = 60 也迁移正确",
              "实际 " + CombinedConfig.GenderRatio.settings.femalePercent);

        // ---------------------------------------------------------------- 收尾
        Console.WriteLine();
        Console.WriteLine("=== 结果：" + _pass + " 通过 / " + _fail + " 失败 ===");
        try { Directory.Delete(root, true); } catch { }
        return _fail == 0 ? 0 : 1;
    }
}
