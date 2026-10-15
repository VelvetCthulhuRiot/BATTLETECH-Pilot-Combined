using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;

namespace PilotCombined.GenderRatio
{
    /// <summary>
    /// 「性别比例」这一段配置的读取器。
    ///
    /// M2 之后它读的是融合配置 <c>combined.json</c>（顶层 <c>gender_ratio</c> / <c>popup</c> 两段）——
    /// 之所以能一字不改地复用，是因为 combined.json 就是这么布局的：
    /// 原来这两个段的键名与层级原封不动。迁移旧配置时它也会被用来读旧 <c>genderratio.json</c>。
    ///
    /// 用 Newtonsoft 的 JObject 手写读取（而不是反序列化成 C# 类），原因：
    ///   1) 本 mod 的 JSON 键是 snake_case，C# 字段是 CamelCase，手写更直观、少踩坑；
    ///   2) 以后加分类（比如"机器人"）时，配置字段要能向后兼容地新增，手写读取最灵活；
    ///   3) 任何字段坏掉都只影响它自己，不会让整份配置反序列化失败；
    ///   4) 写回时能只覆盖 gender_ratio 段，把 portraits / popup 段和 "//" 注释原样保住。
    /// </summary>
    public class GenderRatioConfig
    {
        // ---- gender_ratio（核心）----
        public bool enabled = true;
        public GenderRatioSettings settings = GenderRatioSettings.VanillaDefaults();

        // ---- popup（弹窗 / 滑块 UI）----
        public bool popupEnabled = true;        // 是否安装 F9 弹窗（关掉后机制照常生效）
        public string hotkey = "F9";
        public float stepPercent = 1f;
        // 说明：v0.2.7 起窗口控件全部自绘，不再复用游戏预制体，
        // 所以曾经的 use_sliders / use_game_chrome 两个开关已删除（配置里留着也会被忽略）。
        /// <summary>
        /// 字体名（**系统已安装的字体名**，不是文件路径 —— Unity 运行时无法直接读 ttf 文件）。
        /// 留空 = 按"从场景里的 UI.Text 借 → 内置 Arial"的顺序自动找。
        /// 例："Noto Sans CJK SC" 或 "Microsoft YaHei"。
        /// </summary>
        public string fontFile = "";
        public string femaleLabel = "女";
        /// <summary>
        /// 界面语言：<c>auto</c>（默认，跟随游戏设置）/ <c>en</c> / <c>zh</c> / <c>de</c> / <c>ru</c>。
        /// 自动判定万一不对（例如汉化 MOD 的方言差异），在这里强制指定即可，不用改代码。
        /// </summary>
        public string uiLanguage = "auto";
        public string maleLabel = "男";

        /// <summary>配置文件里出现的、本版本不认识的键，启动时列出来提醒用户。</summary>
        public List<string> unknownKeys = new List<string>();

        private static readonly string[] KnownKeys = new string[]
        {
            // "portraits" 是融合后 combined.json 的第三段（本读取器不解析它，
            // 但也不能把它当成"用户写错的键"报出来）
            "schema_version", "gender_ratio", "popup", "portraits"
        };
        private static readonly string[] KnownRatioKeys = new string[]
        {
            "enabled", "other_enabled", "other_percent", "female_percent", "male_percent",
            "other_label", "lock_ratio", "lock_ratio_female", "lock_ratio_male"
        };
        private static readonly string[] KnownPopupKeys = new string[]
        {
            "enabled", "hotkey", "step_percent", "font_file", "female_label", "male_label", "ui_language"
        };

        public static GenderRatioConfig Load(string path)
        {
            GenderRatioConfig cfg = new GenderRatioConfig();
            try
            {
                if (!File.Exists(path))
                {
                    Log.Warn("找不到配置文件，使用内置默认值：" + path);
                    return cfg;
                }

                JObject root = JObject.Parse(File.ReadAllText(path));
                NoteUnknown(root, KnownKeys, "", cfg);

                JObject ratio = root["gender_ratio"] as JObject;
                if (ratio == null)
                {
                    Log.Warn("配置里没有 gender_ratio 段，使用内置默认比例。");
                }
                else
                {
                    NoteUnknown(ratio, KnownRatioKeys, "gender_ratio.", cfg);
                    cfg.enabled = Bool(ratio, "enabled", cfg.enabled);

                    GenderRatioSettings s = new GenderRatioSettings();
                    s.otherEnabled = Bool(ratio, "other_enabled", false);
                    s.otherPercent = Float(ratio, "other_percent", 0f);
                    s.femalePercent = Float(ratio, "female_percent", 46f);
                    // male_percent 可选：老配置只写 female_percent 时，男性自动吃掉剩余
                    s.malePercent = Float(ratio, "male_percent", -1f);
                    s.otherLabel = Str(ratio, "other_label", s.otherLabel);
                    if (s.malePercent < 0f) s.malePercent = 100f - s.otherPercent - s.femalePercent;

                    // ---- 锁定男女比例（整数对，永不漂移）----
                    s.lockRatio = Bool(ratio, "lock_ratio", false);
                    s.lockRatioW = Int(ratio, "lock_ratio_female", 1);
                    s.lockRatioM = Int(ratio, "lock_ratio_male", 1);
                    if (s.lockRatioW < 0) s.lockRatioW = 1;
                    if (s.lockRatioM < 0) s.lockRatioM = 1;
                    if (s.lockRatioW == 0 && s.lockRatioM == 0) { s.lockRatioW = 1; s.lockRatioM = 1; }

                    s.Normalize(cfg.stepPercent);
                    cfg.settings = s;
                }

                JObject popup = root["popup"] as JObject;
                if (popup != null)
                {
                    NoteUnknown(popup, KnownPopupKeys, "popup.", cfg);
                    cfg.popupEnabled = Bool(popup, "enabled", cfg.popupEnabled);
                    cfg.hotkey = Str(popup, "hotkey", cfg.hotkey);
                    cfg.stepPercent = Float(popup, "step_percent", cfg.stepPercent);
                    cfg.fontFile = Str(popup, "font_file", cfg.fontFile);
                    cfg.femaleLabel = Str(popup, "female_label", cfg.femaleLabel);
                    cfg.maleLabel = Str(popup, "male_label", cfg.maleLabel);
                    cfg.uiLanguage = Str(popup, "ui_language", cfg.uiLanguage);
                    cfg.settings.Normalize(cfg.stepPercent);
                }

                if (cfg.stepPercent <= 0f || cfg.stepPercent > 50f)
                {
                    Log.Warn("step_percent 非法（" + cfg.stepPercent + "），回退为 5。");
                    cfg.stepPercent = 5f;
                    cfg.settings.Normalize(cfg.stepPercent);
                }
                if (string.IsNullOrEmpty(cfg.hotkey)) cfg.hotkey = "F9";
            }
            catch (Exception e)
            {
                Log.Error("读取配置文件失败（" + path + "），使用内置默认值：" + e.Message);
            }
            return cfg;
        }

        /// <summary>
        /// 把内存里的比例写回配置文件（UI 改了值之后调用），尽量不破坏用户写的注释/其它字段：
        /// 做法是只覆盖 gender_ratio 段里的三个键，其余原样保留。
        /// </summary>
        public static bool SaveSettings(string path, GenderRatioSettings s)
        {
            try
            {
                JObject root;
                if (File.Exists(path))
                {
                    string text = File.ReadAllText(path);
                    root = string.IsNullOrEmpty(text.Trim()) ? new JObject() : JObject.Parse(text);
                }
                else
                {
                    root = new JObject();
                }

                JObject ratio = root["gender_ratio"] as JObject;
                if (ratio == null)
                {
                    ratio = new JObject();
                    root["gender_ratio"] = ratio;
                }
                ratio["other_enabled"] = s.otherEnabled;
                ratio["other_percent"] = Math.Round(s.otherPercent, 2);
                ratio["female_percent"] = Math.Round(s.femalePercent, 2);
                ratio["male_percent"] = Math.Round(s.malePercent, 2);
                ratio["lock_ratio"] = s.lockRatio;
                ratio["lock_ratio_female"] = s.lockRatioW;
                ratio["lock_ratio_male"] = s.lockRatioM;
                if (!string.IsNullOrEmpty(s.otherLabel)) ratio["other_label"] = s.otherLabel;

                File.WriteAllText(path, root.ToString(), new System.Text.UTF8Encoding(false));
                return true;
            }
            catch (Exception e)
            {
                Log.Warn("写回配置文件失败（" + path + "，不影响本次生效）：" + e.Message);
                return false;
            }
        }

        // ---------------------------------------------------------------- 读取小工具

        private static void NoteUnknown(JObject o, string[] known, string prefix, GenderRatioConfig cfg)
        {
            foreach (KeyValuePair<string, JToken> kv in o)
            {
                // 以 "//" 开头的键是刻意的注释（JSON 本身不支持注释），不算拼写错误
                if (kv.Key.StartsWith("//", StringComparison.Ordinal)) continue;

                bool found = false;
                for (int i = 0; i < known.Length; i++)
                {
                    if (string.Equals(known[i], kv.Key, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
                }
                if (!found) cfg.unknownKeys.Add(prefix + kv.Key);
            }
        }

        private static bool Bool(JObject o, string key, bool fallback)
        {
            JToken t = o[key];
            if (t == null || t.Type == JTokenType.Null) return fallback;
            try { return t.Value<bool>(); }
            catch { Log.Warn("配置项 " + key + " 不是布尔值，忽略：" + t); return fallback; }
        }

        private static float Float(JObject o, string key, float fallback)
        {
            JToken t = o[key];
            if (t == null || t.Type == JTokenType.Null) return fallback;
            try { return t.Value<float>(); }
            catch
            {
                try { return float.Parse(t.ToString(), CultureInfo.InvariantCulture); }
                catch { Log.Warn("配置项 " + key + " 不是数字，忽略：" + t); return fallback; }
            }
        }

        private static int Int(JObject o, string key, int fallback)
        {
            JToken t = o[key];
            if (t == null || t.Type == JTokenType.Null) return fallback;
            try { return t.Value<int>(); }
            catch
            {
                try { return (int)float.Parse(t.ToString(), CultureInfo.InvariantCulture); }
                catch { Log.Warn("配置项 " + key + " 不是整数，忽略：" + t); return fallback; }
            }
        }

        private static string Str(JObject o, string key, string fallback)        {
            JToken t = o[key];
            if (t == null || t.Type == JTokenType.Null) return fallback;
            string v = t.ToString();
            return string.IsNullOrEmpty(v) ? fallback : v;
        }
    }
}
