using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace PilotCombined
{
    /// <summary>
    /// MOD 界面文案的多语言支持。
    ///
    /// <para><b>语言怎么定</b>：跟随**游戏设置** —— <c>Localize.Strings.CurrentCulture</c>
    /// （游戏自己的枚举：EN_US=0 / DE_DE=1 / ZH_CN=2 / ES / FR / IT / RU_RU=6 / PT…）。</para>
    ///
    /// <para>⚠️ 中文是**汉化 MOD** 提供的，所以要额外兜底：如果"当前语言的译文"里大部分含汉字，
    /// 就认定界面实际是中文（万一汉化只替换文本、没改语言设置）。两道信号都会打进日志。</para>
    ///
    /// <para><b>回退链</b>：当前语言 → English → key 本身。所以**漏翻**只会显示英文（不会显示空或中文），
    /// 而且启动时会报一次"缺哪条翻译"。</para>
    ///
    /// <para><b>加德文/俄文</b>：只要把表里对应槽位填上即可（<see cref="Add4"/>），无需改别处。</para>
    /// </summary>
    internal static class Loc
    {
        internal enum Lang { English = 0, Chinese = 1, German = 2, Russian = 3 }

        internal static Lang Current = Lang.English;

        /// <summary>语言检测的详细过程（写进日志，便于核对）。</summary>
        internal static string DetectionDetail = "(未检测)";

        private static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>();
        private static readonly HashSet<string> Missing = new HashSet<string>();

        /// <summary>&lt;mod&gt;\loc 目录（放各语言的 json；为空表示只用内置文案）。</summary>
        private static string _dir;

        static Loc() { Build(); }

        /// <summary>告诉 Loc 去哪里找 <c>loc\&lt;语言&gt;.json</c>（在 Init 里调用一次即可）。</summary>
        internal static void UseDirectory(string dir) { _dir = dir; }

        internal static bool IsChinese { get { return Current == Lang.Chinese; } }

        /// <summary>当前语言对应的文件名（en / zh / de / ru）。</summary>
        internal static string Code()
        {
            switch (Current)
            {
                case Lang.Chinese: return "zh";
                case Lang.German: return "de";
                case Lang.Russian: return "ru";
                default: return "en";
            }
        }

        /// <summary>取文案（当前语言 → English → key 本身）。</summary>
        internal static string T(string key)
        {
            string[] row;
            if (Table.TryGetValue(key, out row))
            {
                int i = (int)Current;
                if (i >= 0 && i < row.Length && !string.IsNullOrEmpty(row[i])) return row[i];
                if (!string.IsNullOrEmpty(row[0])) return row[0];      // 回退英文
            }
            if (Missing.Add(key))
            {
                Log.Warn("界面文案缺少翻译（已按英文/键名显示）：" + key);
            }
            return key;
        }

        /// <summary>取文案并做 <c>string.Format</c>（表里用 {0}/{1} 占位）。</summary>
        internal static string TF(string key, params object[] args)
        {
            try { return string.Format(T(key), args); }
            catch { return T(key); }
        }

        // ================================================================ 外部语言文件

        /// <summary>
        /// 从 <c>&lt;mod&gt;\loc\&lt;语言代码&gt;.json</c> 覆盖内置文案。
        ///
        /// <para>文件形如 <c>{ "win.title": "Pilot Spawn Ratio", "//说明": "…" }</c>；
        /// 以 <c>//</c> 开头的键当作注释忽略（和 combined.json 的习惯一致）。</para>
        ///
        /// <para><b>这样"加一门语言"= 往 loc\ 里放一个 json 文件，不用改代码、不用重新编译。</b>
        /// 缺文件、或某条文案缺失时，自动回退到内置文案（内置表里英文齐全、其余语言缺则回退英文）。</para>
        /// </summary>
        private static void ApplyFileOverrides()
        {
            if (string.IsNullOrEmpty(_dir)) return;
            string path = null;
            try { path = System.IO.Path.Combine(_dir, Code() + ".json"); }
            catch { return; }

            if (!System.IO.File.Exists(path))
            {
                Log.Info("语言文件不存在（" + path + "）⇒ 使用内置文案（" + Current + "）。"
                         + "想自定义就往 loc\\ 里放 " + Code() + ".json。");
                return;
            }

            try
            {
                string text = System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8);
                JObject obj = JObject.Parse(text);
                int applied = 0;
                List<string> unknown = new List<string>();

                foreach (KeyValuePair<string, JToken> kv in obj)
                {
                    string key = kv.Key;
                    if (string.IsNullOrEmpty(key) || key.StartsWith("//")) continue;   // 注释键
                    JToken tok = kv.Value;
                    if (tok == null || tok.Type == JTokenType.Null) continue;
                    string val = tok.ToString();
                    if (string.IsNullOrEmpty(val)) continue;

                    string[] row;
                    if (!Table.TryGetValue(key, out row))
                    {
                        unknown.Add(key);
                        continue;                     // 内置表里没有这个键 ⇒ 多半是拼错了
                    }
                    row[(int)Current] = val;
                    applied++;
                }

                Log.Info("已载入语言文件 " + Code() + ".json：覆盖 " + applied + " 条文案"
                         + (unknown.Count > 0 ? "；⚠ 有 " + unknown.Count + " 个键在内置表里不存在（可能拼错）："
                                                + string.Join("、", unknown.ToArray()) : "") + "。");
            }
            catch (Exception e)
            {
                Log.Warn("读取语言文件失败（" + path + "），改用内置文案：" + e.Message);
            }
        }

        // ================================================================ 语言检测

        internal static void Detect() { Detect(null); }

        /// <param name="manual">
        /// <c>combined.json</c> 里 <c>popup.ui_language</c> 的值：<c>auto</c>（或空）= 自动；
        /// <c>en</c>/<c>zh</c>/<c>de</c>/<c>ru</c> = 强制指定（自动判定不对时的救命开关，不用改代码）。
        /// </param>
        internal static void Detect(string manual)
        {
            // ---- ⓪ 手动强制 ----
            string m = (manual ?? "").Trim().ToLowerInvariant();
            if (m == "en" || m == "zh" || m == "de" || m == "ru")
            {
                Current = FromCode(m);
                DetectionDetail = "combined.json 的 popup.ui_language=\"" + m + "\"（手动强制）";
                ApplyFileOverrides();
                return;
            }

            string detail = "";
            bool known = false;

            // ---- ① 游戏自己的语言设置：**以它为准** ----
            try
            {
                object cur = Localize.Strings.CurrentCulture;        // Localize.Strings.Culture 枚举
                int v = Convert.ToInt32(cur);
                detail = "游戏语言设置=" + cur + "（" + v + "）"
                       + "，名称=" + Localize.Strings.CurrentCultureName
                       + " / " + Localize.Strings.CurrentCultureNameEnglish;
                switch (v)
                {
                    case 2: Current = Lang.Chinese; known = true; break;   // CULTURE_ZH_CN
                    case 1: Current = Lang.German; known = true; break;    // CULTURE_DE_DE
                    case 6: Current = Lang.Russian; known = true; break;   // CULTURE_RU_RU
                    case 0:                                                // EN_US
                    case 3: case 4: case 5: case 7: case 8:                // ES / FR / IT / PT / PT-BR
                        Current = Lang.English; known = true; break;
                    default:
                        Current = Lang.English;                            // UNDEFINED / 未知
                        detail += "（未知值 ⇒ 需要下面的兜底信号）";
                        break;
                }
            }
            catch (Exception e)
            {
                detail = "读取游戏语言设置失败：" + e.Message;
            }

            // ---- ② 兜底：只在 ① 无法确定时启用 ----
            //   ★ 教训（真机 10/15）：这个信号**不能**在 ① 有效时参与判断。
            //   游戏切成英文后，抽样译文里仍然大量含汉字（汉化 MOD 的中文词典还在被统计），
            //   于是被误判成中文 ⇒ 窗口保持中文。现在只在"语言读不到/UNDEFINED"时启用，
            //   并且把阈值提到 ≥80%。
            int sampled = 0, cjk = 0;
            try
            {
                Dictionary<string, string> all = Localize.Strings.GetAllTranslationsForCurrentLanguage();
                if (all != null && all.Count > 0)
                {
                    foreach (KeyValuePair<string, string> kv in all)
                    {
                        string val = kv.Value;
                        if (string.IsNullOrEmpty(val) || val.Length < 2) continue;
                        sampled++;
                        if (HasCjk(val)) cjk++;
                        if (sampled >= 400) break;
                    }
                }
            }
            catch (Exception e)
            {
                detail += "；抽样译文失败：" + e.Message;
            }
            if (sampled > 0) detail += "；抽样译文 " + sampled + " 条，含汉字 " + cjk + " 条（仅作诊断）";

            if (!known)
            {
                bool byCjk = sampled > 0 && cjk * 5 >= sampled * 4;         // ≥80%
                Current = byCjk ? Lang.Chinese : Lang.English;
                detail += "；语言设置不可用 ⇒ 按取样判定为 " + Current + (byCjk ? "（≥80% 含汉字）" : "（含汉字比例不足 80%）");
            }

            detail += "；最终采用=" + Current
                    + "（若不对，可在 combined.json 的 popup.ui_language 里写 en/zh/de/ru 强制指定）";
            DetectionDetail = detail;

            // 内置文案就绪后，再看有没有 loc\<语言>.json 要覆盖
            ApplyFileOverrides();
        }

        private static Lang FromCode(string code)
        {
            switch (code)
            {
                case "zh": return Lang.Chinese;
                case "de": return Lang.German;
                case "ru": return Lang.Russian;
                default: return Lang.English;
            }
        }

        private static bool HasCjk(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= '\u4e00' && c <= '\u9fff') return true;      // 基本汉字区
            }
            return false;
        }

        // ================================================================ 文案表

        /// <summary>只有英文 + 中文（其它语言暂缺 ⇒ 回退英文）。</summary>
        private static void Add(string key, string en, string zh)
        {
            Table[key] = new string[] { en, zh, null, null };
        }

        /// <summary>四语齐全版（将来补德/俄就用这个）。</summary>
        private static void Add4(string key, string en, string zh, string de, string ru)
        {
            Table[key] = new string[] { en, zh, de, ru };
        }

        private static void Build()
        {
            // ---- 窗口标题 / 按钮 ----
            Add("win.title", "Pilot Spawn Ratio", "驾驶员性别刷出比例");
            Add("btn.apply", "Apply & Save", "应用并写回配置");
            Add("btn.rescan", "Rescan Images", "立即重扫图片池");
            Add("btn.defaults", "Restore Defaults", "恢复默认");
            Add("btn.close", "Close", "关闭");

            // ---- 勾选框 ----
            Add("chk.portraits", "Enable portrait replacement (use the local images in portraits\\)",
                                  "启用头像替换（用 portraits\\ 里的本地图片换掉游戏头像）");
            Add("chk.other", "Enable \"{0}\"", "启用「{0}」");
            Add("chk.lock", "Lock the Female:Male ratio (kept when \"{0}\" changes)",
                            "锁定男女比例（用下面输入的比例，调整「{0}」时保持不变）");

            // ---- 数值行 / 提示 ----
            Add("status.now", "Current (active): ", "当前（生效中）：");
            Add("status.ratio", "Actual F : M = {0}", "实际女 : 男 = {0}");
            Add("status.locked", "   locked {0}", "   锁定目标 {0}");
            Add("status.unlocked", "   not locked", "   未锁定");
            Add("status.step", "      step {0}%", "      步长 {0}%");
            Add("other.value", "\"{0}\" share: {1}    (max {2})", "「{0}」占比：{1}    （上限 {2}）");
            Add("fm.value", "F / M split: {0}, {1}    cap (= 100 - \"{2}\") {3}, clamped",
                            "女 / 男 分配：女 {0}，男 {1}    上限（= 100 −「{2}」）{3}，超出即锁住");
            Add("hint.main", "The Female/Male slider caps at 100 - \"{0}\"; the ratio box accepts 3:2 / 23:22, Enter applies.",
                             "女/男 滑块上限 = 100 −「{0}」；比例输入支持 3:2 / 23:22，回车应用。");
            Add("ratio.label", "Ratio  F : M ", "锁定比例 女 : 男 ");
            Add("ratio.hint", "Enter to apply (e.g. 3:2, auto-reduced)", "输入后回车应用（如 3:2，自动约分）");

            // ---- 肖像池状态行 ----
            Add("pool.line1", "Portrait pools: F {0} / M {1} / {2} {3}  (total {4})",
                              "肖像池：女 {0} / 男 {1} / {2} {3}（共 {4} 张）");
            Add("pool.off.empty",
                "No portrait images found -> replacement cannot be enabled; the ratio works on its own (exactly as configured)",
                "没有找到任何肖像图片 ⇒ 头像替换不可开启；性别比例调节独立运作（完全按上面的配置）");
            Add("pool.off",
                "Replacement is OFF -> vanilla portraits; the ratio works on its own, unaffected by images",
                "头像替换已关闭 ⇒ 完全走游戏原版肖像机制；性别比例独立运作、不受图片有无影响");
            Add("pool.only", "Only genders that have portraits are generated: available {0}",
                             "只生成有肖像的性别：可用 {0}");
            Add("pool.note.other", "  ({0} has no portrait -> greyed out and never generated)",
                                   "　（{0} 没有肖像 → 该项变灰、也不会生成）");
            Add("pool.note.fm", "  (F or M missing -> split / lock / ratio box all greyed out; the ratio has no effect)",
                                "　（女/男 缺一方 → 男女分配、锁定比例、比例输入框都变灰，比例不生效）");
            Add("pool.note.ok", "  (the ratio is renormalised within the available genders)",
                                "　（比例在可用范围内归一）");

            // ---- 动作反馈 ----
            Add("apply.ok", "Applied and written to combined.json: ", "已生效并写回 combined.json：");
            Add("apply.fail", "Applied (writing the config failed, see log): ", "已生效（写回配置失败，见日志）：");
            Add("apply.portraitFail", "; writing the portrait switch failed", "；头像替换开关写回失败");
            Add("rescan.done", "Rescanned portraits: ", "已重新扫描图片池：");
            Add("rescan.pool", "{0} {1} / {2} {3} / {4} {5}  (total {6})", "{0} {1} / {2} {3} / {4} {5}（共 {6} 张）");
            Add("rescan.emptyNote", "  (no images -> replacement was switched off automatically)",
                                    "　（没有图片 ⇒ 头像替换已自动关闭）");
            Add("rescan.canEnable", "  (you can now tick \"Enable portrait replacement\")",
                                    "　（现在可以勾上「启用头像替换」了）");

            // ---- 性别用词（英文界面用这些；中文界面优先用 combined.json 里的标签）----
            Add("word.female", "Female", "女");
            Add("word.male", "Male", "男");
            Add("word.other", "Non-binary / agender", "非二元性别或无性别");
            Add("list.sep", ", ", "、");
        }
    }
}
