using System;
using PilotCombined;
using PilotCombined.GenderRatio;

/// <summary>
/// 脱机单测：验证「性别刷出比例」的全部数学，不开游戏、不引用 UnityEngine。
///
/// v0.2.16 变更：**「女/男」滑块读数 = 女性的绝对百分比**，上限 = 100 - 其他性别
///   （旧语义是"女性占女+男的比重"，其他=10% 时读数 51% 而女性只有 46%，读起来自相矛盾）。
/// v0.2.14 变更：锁定比例用整数精确存储，绝不从滑块反推（否则会漂移）。
/// </summary>
internal static class TestMain
{
    private static int _pass, _fail;

    private static void Check(bool ok, string what, string detail)
    {
        if (ok) { _pass++; Console.WriteLine("  [ok]   " + what); }
        else { _fail++; Console.WriteLine("  [FAIL] " + what + "   <<< " + detail); }
    }

    private static void Near(float got, float want, float tol, string what)
    {
        Check(Math.Abs(got - want) <= tol, what,
              "got=" + got.ToString("0.###") + " want=" + want.ToString("0.###"));
    }

    private static int Main()
    {
        Console.WriteLine("=== GenderRatio 脱机单测 ===");

        // ---------------------------------------------------------------- 1
        Console.WriteLine();
        Console.WriteLine("[1] 其他性别关闭：滑块读数就是女性占比，两端 = 全男 / 全女");
        GenderRatioSettings s = new GenderRatioSettings(false, 0f, 50f, 50f);
        Near(s.FemaleSliderMax, 100f, 0.001f, "关闭其他时滑块上限 = 100");
        Near(s.FemaleSliderValue, 50f, 0.01f, "女50 -> 读数 50");

        s.FemaleSliderValue = 0f;
        float[] p = s.Percents();
        Near(p[0], 0f, 0.01f, "滑块 0   -> 女 0%（全男）");
        Near(p[1], 100f, 0.01f, "滑块 0   -> 男 100%");

        s.FemaleSliderValue = 100f;
        p = s.Percents();
        Near(p[0], 100f, 0.01f, "滑块 100 -> 女 100%（全女）");
        Near(p[1], 0f, 0.01f, "滑块 100 -> 男 0%");

        // ---------------------------------------------------------------- 2
        Console.WriteLine();
        Console.WriteLine("[2] ★ 其他=10%：读数 = 女性占比（46% 就读 46%），上限 90 并锁住");
        s = new GenderRatioSettings(true, 10f, 46f, 44f);
        Near(s.FemaleSliderMax, 90f, 0.001f, "上限 = 100-10 = 90（最右 10% 锁住）");
        Near(s.FemaleSliderValue, 46f, 0.01f, "女46% -> 读数就是 46%（不再是 51%）");

        s.FemaleSliderValue = 90f;
        p = s.Percents();
        Near(p[0], 90f, 0.01f, "拖到顶 -> 女 90%");
        Near(p[1], 0f, 0.01f, "拖到顶 -> 男 0%（其他 10% + 全女）");
        Near(p[2], 10f, 0.01f, "其他恒 10%");

        Console.WriteLine();
        Console.WriteLine("[3] 越界写入夹到上限（= 用户说的『把最右边 10% 锁住』）");
        s = new GenderRatioSettings(true, 10f, 46f, 44f);
        s.FemaleSliderValue = 95f;
        Near(s.femalePercent, 90f, 0.01f, "写 95 被夹到 90");
        s.FemaleSliderValue = 0f;
        Near(s.femalePercent, 0f, 0.01f, "写 0 -> 全男");

        // ---------------------------------------------------------------- 4
        Console.WriteLine();
        Console.WriteLine("[4] 比例解析与自动约分");
        string err;
        Check(Ratio.Parse("23:22", out err).Text == "23:22", "23:22 -> 23:22", err);
        Check(Ratio.Parse("46:44", out err).Text == "23:22", "46:44 -> 约分为 23:22", err);
        Check(Ratio.Parse("3：2", out err).Text == "3:2", "全角冒号 3：2 -> 3:2", err);
        Check(Ratio.Parse("女3:男2", out err).Text == "3:2", "女3:男2 -> 3:2", err);
        Check(Ratio.Parse("3/2", out err).Text == "3:2", "3/2 -> 3:2", err);
        Check(Ratio.Parse("3-2", out err).Text == "3:2", "3-2 -> 3:2", err);
        Check(Ratio.Parse("女 3 : 男 2", out err).Text == "3:2", "带空格中文 -> 3:2", err);
        Check(Ratio.Parse("3", out err).Text == "3:1", "纯数字 3 -> 3:1", err);
        Check(Ratio.Parse("2:0", out err).Text == "1:0", "2:0 -> 1:0（全女）", err);
        Check(Ratio.Parse("0:5", out err).Text == "0:1", "0:5 -> 0:1（全男）", err);
        Check(Ratio.Parse("abc", out err) == null, "无数字 -> 解析失败", "");
        Check(Ratio.Parse("1:2:3", out err) == null, "三个数字 -> 解析失败", "");
        Check(Ratio.Parse("0:0", out err) == null, "0:0 -> 解析失败", "");

        // ---------------------------------------------------------------- 5
        Console.WriteLine();
        Console.WriteLine("[5] ★ 锁定比例不漂移（v0.2.14 修的核心 bug）");
        s = new GenderRatioSettings(true, 10f, 46f, 44f);
        s.SetLockedRatio(23, 22);
        s.lockRatio = true;
        s.ApplyLockedRatio(1f);
        Near(s.femalePercent, 46f, 0.01f, "23:22 + other=10 -> 女 46%");

        string expect = s.LockedRatio.Text;
        float expF = s.femalePercent, expM = s.malePercent;
        bool stable = true;
        string trace = "";
        for (int round = 0; round < 20; round++)
        {
            for (float o = 0f; o <= 60f; o += 1f)
            {
                s.SetOtherPercent(o, 1f);
                if (s.LockedRatio.Text != expect) { stable = false; trace = "比例变成 " + s.LockedRatio.Text; }
            }
            s.SetOtherPercent(10f, 1f);
            if (s.LockedRatio.Text != expect) { stable = false; trace = "比例变成 " + s.LockedRatio.Text; }
            if (Math.Abs(s.femalePercent - expF) > 0.01f || Math.Abs(s.malePercent - expM) > 0.01f)
            {
                stable = false;
                trace = "回到 other=10 时 女" + s.femalePercent + "/男" + s.malePercent
                      + "（应为 " + expF + "/" + expM + "）";
            }
        }
        Check(stable, "20 轮 x 61 次改动后：比例恒为 " + expect
                      + "，回到 other=10 恒为 女" + expF + "/男" + expM, trace);

        // ---------------------------------------------------------------- 6
        Console.WriteLine();
        Console.WriteLine("[6] 锁定 3:2，其他性别 0..90 全程：三项之和恒 100");
        s = new GenderRatioSettings(true, 10f, 54f, 36f);
        s.SetLockedRatio(3, 2);
        s.lockRatio = true;
        bool sumOk = true;
        string bad = "";
        for (float o = 0f; o <= 90f; o += 1f)
        {
            s.SetOtherPercent(o, 1f);
            float[] q = s.Percents();
            if (Math.Abs(q[0] + q[1] + q[2] - 100f) > 0.01f)
            { sumOk = false; bad = "sum=" + (q[0] + q[1] + q[2]) + " @other=" + o; }
        }
        Check(sumOk, "其他 0..90 全程：三项之和恒 100", bad);
        s.SetOtherPercent(10f, 1f);
        p = s.Percents();
        Near(p[0], 54f, 0.01f, "3:2 且 other=10 -> 女 54%");
        Near(p[1], 36f, 0.01f, "3:2 且 other=10 -> 男 36%");

        // ---------------------------------------------------------------- 7
        Console.WriteLine();
        Console.WriteLine("[7] 步长 1% / 5%：吸附后总和恒为 100");
        foreach (float step in new float[] { 1f, 5f })
        {
            bool allOk = true;
            string b = "";
            for (float o = 0f; o <= 100f; o += step)
            {
                for (float f = 0f; f <= 100f; f += step)
                {
                    GenderRatioSettings t = new GenderRatioSettings(true, o, f, 100f - o - f);
                    t.Normalize(step);
                    t.SnapToStep(step);
                    float[] q = t.Percents();
                    if (Math.Abs(q[0] + q[1] + q[2] - 100f) > 0.01f)
                    { allOk = false; b = "sum=" + (q[0] + q[1] + q[2]); }
                    int[] w = t.Weights();
                    if (w[0] + w[1] + w[2] <= 0) { allOk = false; b = "权重和=0"; }
                }
            }
            Check(allOk, "步长 " + step + "%：穷举后总和恒 100 且权重和 > 0", b);
        }

        // ---------------------------------------------------------------- 8
        Console.WriteLine();
        Console.WriteLine("[8] 默认值 = 精确的原版 46/44/10");
        GenderRatioSettings d = GenderRatioSettings.VanillaDefaults();
        d.Normalize(1f);
        p = d.Percents();
        Near(p[0], 46f, 0.01f, "默认 女 46%");
        Near(p[1], 44f, 0.01f, "默认 男 44%");
        Near(p[2], 10f, 0.01f, "默认 其他 10%");
        Near(d.FemaleSliderValue, 46f, 0.01f, "默认滑块读数 = 46%");
        int[] dw = d.Weights();
        Check(dw[0] == 4600 && dw[1] == 4400 && dw[2] == 1000, "默认权重 4600/4400/1000",
              dw[0] + "/" + dw[1] + "/" + dw[2]);

        // ---------------------------------------------------------------- 9
        Console.WriteLine();
        Console.WriteLine("[9] 拖动滑块后捕捉比例为整数对");
        s = new GenderRatioSettings(true, 10f, 30f, 60f);
        s.CaptureRatioFromCurrent();
        Check(s.LockedRatio.Text == "1:2", "女30/男60 -> 1:2", s.LockedRatio.Text);
        s = new GenderRatioSettings(true, 10f, 29f, 61f);
        s.CaptureRatioFromCurrent();
        Check(s.LockedRatio.W > 0 && s.LockedRatio.M > 0, "女29/男61 -> 正整数比例", s.LockedRatio.Text);

        // ---------------------------------------------------------------- 10
        Console.WriteLine();
        Console.WriteLine("[10] 越界与开关");
        s = new GenderRatioSettings(true, 10f, 45f, 45f);
        s.SetOtherPercent(999f, 1f);
        Near(s.otherPercent, 100f, 0.01f, "其他写 999 被夹到 100");
        s.Normalize(5f);
        Check(s.otherPercent <= 95f, "Normalize 后其他 <= 95", s.otherPercent.ToString());
        s.otherEnabled = false;
        s.Normalize(5f);
        Near(s.Percents()[2], 0f, 0.01f, "未启用时其他为 0");
        Near(s.FemaleSliderMax, 100f, 0.01f, "未启用时滑块上限回到 100");

        Console.WriteLine();
        Console.WriteLine("[11] 百分比格式化（日志用）");
        Check(GenderRatioSettings.Pct(46f) == "46%", "Pct(46) = 46%", GenderRatioSettings.Pct(46f));
        Check(GenderRatioSettings.Pct(45.5f) == "45.5%", "Pct(45.5) = 45.5%", GenderRatioSettings.Pct(45.5f));

        // ---------------------------------------------------------------- 12
        // ★ 核心规则：肖像池优先（文档 6.2 的 8 种情况全都要覆盖）
        Console.WriteLine();
        Console.WriteLine("[12] ★ 肖像池优先（文档 6.2 情况 ①~⑧）");

        // 一个方便的小工具：w = ResolveFinalWeights(...)
        // 注意用 女80 / 男10 / 其他10 作为基准，正好是用户当前配置的量级。
        GenderRatioSettings cfgBase = new GenderRatioSettings(true, 10f, 80f, 10f);

        // 情况① 三者都有图 ⇒ 完全按配置
        int[] w1 = cfgBase.ResolveFinalWeights(true, true, true, true, true);
        int[] ref1 = cfgBase.Weights();
        Check(w1[0] == ref1[0] && w1[1] == ref1[1] && w1[2] == ref1[2],
              "① F,M,O 都有图 ⇒ 完全按配置（" + w1[0] + "/" + w1[1] + "/" + w1[2] + "）",
              w1[0] + "/" + w1[1] + "/" + w1[2]);

        // 情况② 只有 F,M（用户当前的真实情形！）⇒ 男+女 = 100%，比例保持 8:1
        int[] w2 = cfgBase.ResolveFinalWeights(true, true, true, true, false);
        Check(w2[2] == 0, "② 没有其他性别肖像 ⇒ 其权重为 0", "w[2]=" + w2[2]);
        Check(w2[0] > 0 && w2[1] > 0, "② 女/男 权重都 > 0",
              w2[0] + "/" + w2[1]);
        // 80:10 归一后 = 88.9% : 11.1% ⇒ 权重 8889 : 1111（ToWeight 是 percent*100）
        Check(Math.Abs(w2[0] / (float)(w2[0] + w2[1]) - 80f / 90f) < 0.001f,
              "② 归一后 女:男 仍 = 8:1（80:10）",
              w2[0] + ":" + w2[1]);
        Check(Math.Abs((w2[0] + w2[1] + w2[2]) - 10000f) < 2f,
              "② 三项权重之和 ≈ 10000（被归一填满 100%）",
              "" + (w2[0] + w2[1] + w2[2]));

        // 情况③ 仅 F ⇒ 无视比例，只生成女性
        int[] w3 = cfgBase.ResolveFinalWeights(true, true, true, false, false);
        Check(w3[0] > 0 && w3[1] == 0 && w3[2] == 0,
              "③ 仅女性有图 ⇒ 只生成女性（无视比例）", w3[0] + "/" + w3[1] + "/" + w3[2]);

        // 情况④ 仅 M
        int[] w4 = cfgBase.ResolveFinalWeights(true, true, false, true, false);
        Check(w4[0] == 0 && w4[1] > 0 && w4[2] == 0,
              "④ 仅男性有图 ⇒ 只生成男性", w4[0] + "/" + w4[1] + "/" + w4[2]);

        // 情况⑤ F,O
        int[] w5 = cfgBase.ResolveFinalWeights(true, true, true, false, true);
        Check(w5[1] == 0 && w5[0] > 0 && w5[2] > 0,
              "⑤ 女 + 其他有图 ⇒ 男性为 0，女与其他的比例保持 80:10",
              w5[0] + "/" + w5[1] + "/" + w5[2]);
        Check(Math.Abs(w5[0] / (float)(w5[0] + w5[2]) - 80f / 90f) < 0.001f,
              "⑤ 归一后 女:其他 仍 = 80:10", w5[0] + ":" + w5[2]);

        // 情况⑥ M,O
        int[] w6 = cfgBase.ResolveFinalWeights(true, true, false, true, true);
        Check(w6[0] == 0 && w6[1] > 0 && w6[2] > 0,
              "⑥ 男 + 其他有图 ⇒ 女性为 0", w6[0] + "/" + w6[1] + "/" + w6[2]);

        // 情况⑦ 仅 O
        int[] w7 = cfgBase.ResolveFinalWeights(true, true, false, false, true);
        Check(w7[0] == 0 && w7[1] == 0 && w7[2] > 0,
              "⑦ 仅其他性别有图 ⇒ 只生成其他性别", w7[0] + "/" + w7[1] + "/" + w7[2]);

        // 情况⑧ 都没有 ⇒ 兜底 1,1,1
        int[] w8 = cfgBase.ResolveFinalWeights(true, true, false, false, false);
        Check(w8[0] == 1 && w8[1] == 1 && w8[2] == 1,
              "⑧ 一张图都没有 ⇒ 兜底 1,1,1", w8[0] + "/" + w8[1] + "/" + w8[2]);

        // 8 种情况下"权重和必须 > 0"（GetWeightedResult 的硬要求）
        bool allPositive = true;
        string badCase = "";
        int[][] all = new int[][] { w1, w2, w3, w4, w5, w6, w7, w8 };
        for (int i = 0; i < all.Length; i++)
        {
            int t = all[i][0] + all[i][1] + all[i][2];
            if (t <= 0) { allPositive = false; badCase = "情况" + (i + 1) + " 权重和=" + t; }
        }
        Check(allPositive, "①~⑧ 任一情况权重和都 > 0", badCase);

        // ---------------------------------------------------------------- 13
        Console.WriteLine();
        Console.WriteLine("[13] 关闭头像替换 ⇒ 完全等于老行为（不做任何裁剪）");
        GenderRatioSettings cfgOff = new GenderRatioSettings(true, 10f, 80f, 10f);
        int[] off1 = cfgOff.ResolveFinalWeights(false, true, true, true, false);   // 故意说"没有其他性别肖像"
        int[] off2 = cfgOff.Weights();
        Check(off1[0] == off2[0] && off1[1] == off2[1] && off1[2] == off2[2],
              "portraitMode=false ⇒ 与 Weights() 完全一致（其他性别照旧 10%）",
              off1[0] + "/" + off1[1] + "/" + off1[2] + " vs " + off2[0] + "/" + off2[1] + "/" + off2[2]);
        Check(off1[2] > 0, "关闭替换时 其他性别 权重不受图片池影响", "w[2]=" + off1[2]);

        // 第三类没有图 & 用户没启用 ⇒ 与"没图"等价
        int[] w9 = new GenderRatioSettings(false, 0f, 80f, 20f).ResolveFinalWeights(true, false, true, true, false);
        Check(w9[0] > 0 && w9[1] > 0 && w9[2] == 0,
              "其他性别未启用 + 无图 ⇒ 只在女/男 之间归一", w9[0] + "/" + w9[1] + "/" + w9[2]);

        // 可用性别的占比全是 0 的极端情况：只有女性有图、female 却是 0
        int[] w10 = new GenderRatioSettings(true, 0f, 0f, 100f).ResolveFinalWeights(true, false, true, false, false);
        Check(w10[0] > 0 && w10[1] == 0 && w10[2] == 0,
              "只有女性有图但 female_percent=0 ⇒ 仍然只生成女性（不看比例）",
              w10[0] + "/" + w10[1] + "/" + w10[2]);

        // ---------------------------------------------------------------- 14
        // ★ 性别名字规则（图片池按目录名分性别）。这一段是「用户放图 = 生效」的唯一入口，
        //   写错一个词就会让整批图静默变成"未标注"，所以必须有单测。
        Console.WriteLine();
        Console.WriteLine("[14] ★ 性别名字 → 槽位（portraits 目录名 / 文件名前缀）");

        Check(GenderNames.SlotFromToken("female") == GenderNames.Female, "female → 女", "");
        Check(GenderNames.SlotFromToken("F") == GenderNames.Female, "F → 女（大小写不敏感）", "");
        Check(GenderNames.SlotFromToken("男") == GenderNames.Male, "「男」→ 男", "");
        Check(GenderNames.SlotFromToken("Female") == GenderNames.Female, "Female（首字母大写）→ 女", "");

        // ★ 这一条就是真机发现的 bug：portraits\other\ 里的图原来被当成"未标注"
        Check(GenderNames.SlotFromToken("other") == GenderNames.Other,
              "★ other → 第三类（用户就是这么命名 dir 的）", "");
        Check(GenderNames.SlotFromToken("nonbinary") == GenderNames.Other, "nonbinary → 第三类", "");
        Check(GenderNames.SlotFromToken("non-binary") == GenderNames.Other, "non-binary → 第三类", "");
        Check(GenderNames.SlotFromToken("中性") == GenderNames.Other, "「中性」→ 第三类", "");
        Check(GenderNames.SlotFromToken("机器人") == GenderNames.Other, "「机器人」→ 第三类", "");
        Check(GenderNames.SlotFromToken("robot") == GenderNames.Other, "robot → 第三类", "");

        Check(GenderNames.SlotFromToken("portraits") == GenderNames.Unknown, "portraits 本身不是性别", "");
        Check(GenderNames.SlotFromToken("01") == GenderNames.Unknown, "纯数字不认识", "");
        Check(GenderNames.SlotFromToken("") == GenderNames.Unknown, "空串不认识", "");
        Check(GenderNames.SlotFromToken("robots_old") == GenderNames.Unknown, "没约定的词不认识", "");

        Check(GenderNames.SlotFromPath("portraits/female/01.png") == GenderNames.Female,
              "路径 portraits/female/01.png → 女", "");
        Check(GenderNames.SlotFromPath(@"portraits\male\07.png") == GenderNames.Male,
              @"路径 portraits\male\07.png（反斜杠）→ 男", "");
        Check(GenderNames.SlotFromPath("portraits/other/16.png") == GenderNames.Other,
              "★ 路径 portraits/other/16.png → 第三类", "");
        Check(GenderNames.SlotFromPath("portraits/01.png") == GenderNames.Unknown,
              "路径 portraits/01.png（没分目录、文件名也没前缀）→ 未标注", "");
        Check(GenderNames.SlotFromPath("portraits/female_02.png") == GenderNames.Female,
              "文件名前缀 female_02.png → 女", "");
        Check(GenderNames.SlotFromPath("portraits/other/robot_03.png") == GenderNames.Other,
              "目录 other 优先于文件名前缀 → 第三类", "");

        Check(GenderNames.TagOf(GenderNames.Other) == "other",
              "日志标签：第三类写 other（与目录名一致）", GenderNames.TagOf(GenderNames.Other));

        // 「女性头像 / 男性头像」正是用户源文件夹的真实名字 —— 直接拖进 portraits 下是最可能发生的失误
        string[] badDirs = GenderNames.UnrecognizedDirTokens("portraits/女性头像/01.png");
        Check(badDirs.Length == 1 && badDirs[0] == "女性头像",
              "★ 名字不认识的子目录会被列出来（用于启动报警）",
              badDirs.Length == 0 ? "(空)" : string.Join(",", badDirs));
        Check(GenderNames.UnrecognizedDirTokens("portraits/robot/01.png").Length == 0,
              "robot 是认可的别名，不报警", "");
        Check(GenderNames.UnrecognizedDirTokens("portraits/robots/01.png").Length == 0,
              "robots 是认可的别名，不报警", "");
        Check(GenderNames.UnrecognizedDirTokens("portraits/other/01.png").Length == 0,
              "认识的子目录不会报警", "");
        Check(GenderNames.UnrecognizedDirTokens("portraits/_停用/01.png").Length == 0,
              "下划线开头的目录视为用户主动停用，不报警", "");
        Check(GenderNames.UnrecognizedDirTokens("portraits/female/01.png").Length == 0,
              "female 目录不报警", "");

        Console.WriteLine();
        Console.WriteLine("=== 结果：" + _pass + " 通过 / " + _fail + " 失败 ===");
        return _fail == 0 ? 0 : 1;
    }
}

