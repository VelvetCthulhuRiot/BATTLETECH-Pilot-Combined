using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

// ★ 类型别名：本类里有一个**静态字段**叫 GenderRatio，它会把子命名空间 GenderRatio 遮住，
//   于是 `GenderRatio.GenderRatioConfig` 会被编译器当成"字段成员访问"而报 CS1061。
//   用别名绕开，别去改字段名（调用方 CombinedConfig.GenderRatio 已经写好了）。
using GrConfig = PilotCombined.GenderRatio.GenderRatioConfig;
using GrSettings = PilotCombined.GenderRatio.GenderRatioSettings;
using PsConfig = PilotCombined.Portrait.PortraitConfig;

namespace PilotCombined
{
    /// <summary>
    /// 融合后的**唯一配置文件**：<c>&lt;mod目录&gt;\combined.json</c>。
    ///
    /// 文件布局（三段，和旧的两个配置一一对应）：
    /// <code>
    /// {
    ///   "schema_version": 1,
    ///   "gender_ratio": { ... },   // 旧 genderratio.json 的核心段
    ///   "popup":        { ... },   // 旧 genderratio.json 的 UI 段
    ///   "portraits":    { ... }    // 旧 portraits.json 的全部内容
    /// }
    /// </code>
    ///
    /// 为什么这么布局：<c>GenderRatioConfig.Load()</c> 原本就是读顶层
    /// <c>gender_ratio</c> / <c>popup</c> 两段，所以<b>它一字不改就能读 combined.json</b>；
    /// 只要把 portraits 段交给 PortraitConfig 解析即可。改动面最小 = 风险最小。
    ///
    /// 自动迁移（M2）：
    ///   combined.json 不存在时，在本目录和**旧 MOD 的部署目录**里找
    ///   <c>genderratio.json</c> / <c>portraits.json</c>，把值搬过来，
    ///   以 <c>combined.json.template</c>（build.ps1 随 mod 一起部署的带注释模板）为底稿写出
    ///   新的 combined.json —— 这样迁移出来的文件也带完整注释，用户看得懂。
    /// </summary>
    internal static class CombinedConfig
    {
        internal const int SchemaVersion = 1;

        /// <summary>combined.json 的绝对路径（UI 写回配置也用它）。</summary>
        internal static string ConfigPath = "";

        /// <summary>性别比例那一段（等价于旧的 genderratio.json）。</summary>
        internal static GrConfig GenderRatio;

        /// <summary>头像那一段（等价于旧的 portraits.json）。</summary>
        internal static PsConfig Portraits;

        /// <summary>本次启动是否执行了"从旧配置迁移"。</summary>
        internal static bool Migrated;

        /// <summary>迁移过程里的人话说明（给用户看）。</summary>
        internal static readonly List<string> MigrationNotes = new List<string>();

        private static bool _loaded;

        /// <summary>
        /// **仅供脱机测试使用**：清掉"已加载"标记，让同一个进程里能再跑一次 Load。
        /// 游戏里永远不需要调用它。
        /// </summary>
        internal static void ResetForTesting()
        {
            _loaded = false;
            Migrated = false;
            MigrationNotes.Clear();
            ConfigPath = "";
            GenderRatio = null;
            Portraits = null;
        }

        /// <summary>
        /// 读配置；必要时执行迁移。**幂等**：两个子系统各调用一次也只做一次。
        /// </summary>
        internal static void Load(string modDir)
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                ConfigPath = Path.Combine(modDir, "combined.json");

                if (File.Exists(ConfigPath))
                {
                    Read(ConfigPath);
                    WarnIfLegacyFilesPresent(modDir);
                    return;
                }

                // ---------- 没有 combined.json：迁移 ----------
                Migrate(modDir);
                Read(ConfigPath);
            }
            catch (Exception e)
            {
                Log.Error("读取 combined.json 失败，全部使用内置默认值：" + e);
                if (GenderRatio == null) GenderRatio = new GrConfig();
                if (Portraits == null) Portraits = new PsConfig();
            }
        }

        // ================================================================ 读取

        private static void Read(string path)
        {
            // 顶层三段：gender_ratio / popup 直接复用旧的读取器（它本来就按这两段读）
            GenderRatio = GrConfig.Load(path);

            // portraits 段交给 PortraitConfig
            PsConfig ps = new PsConfig();
            try
            {
                JObject root = JObject.Parse(File.ReadAllText(path));
                ps = PsConfig.FromJObject(root["portraits"]);
            }
            catch (Exception e)
            {
                Log.Error("combined.json 里的 portraits 段读取失败，头像使用默认设置：" + e.Message);
            }
            Portraits = ps;

            Log.Info("配置文件：" + path + "（schema_version=" + SchemaVersion + "）");
        }

        /// <summary>combined.json 和旧配置文件同时存在时提醒一句，避免用户改错文件。</summary>
        private static void WarnIfLegacyFilesPresent(string modDir)
        {
            List<string> found = new List<string>();
            foreach (string n in new string[] { "genderratio.json", "portraits.json" })
            {
                if (File.Exists(Path.Combine(modDir, n))) found.Add(n);
            }
            if (found.Count > 0)
            {
                Log.Warn("检测到旧的配置文件（" + string.Join("、", found.ToArray())
                         + "）—— 它们**已经不再被读取**，请只修改 combined.json。"
                         + "（M2 已把两者合并；这两个文件可以放心删除。）");
            }
        }

        // ================================================================ 迁移

        private static void Migrate(string modDir)
        {
            Migrated = true;
            Log.Section("配置迁移（旧的 genderratio.json / portraits.json → combined.json）");

            string legacyGr = FirstExisting(new string[]
            {
                Path.Combine(modDir, "genderratio.json"),
                Path.Combine(SiblingDir(modDir, "GenderRatio"), "genderratio.json")
            });
            string legacyPs = FirstExisting(new string[]
            {
                Path.Combine(modDir, "portraits.json"),
                Path.Combine(SiblingDir(modDir, "PortraitSwap"), "portraits.json")
            });

            // 底稿：优先用带注释的模板，保证迁移出来的文件也看得懂
            JObject root = null;
            string template = Path.Combine(modDir, "combined.json.template");
            if (File.Exists(template))
            {
                try { root = JObject.Parse(File.ReadAllText(template)); }
                catch (Exception e) { Log.Warn("模板 combined.json.template 解析失败，改用内置默认值：" + e.Message); }
            }
            if (root == null) root = BuiltinTemplate();

            int moved = 0;

            if (legacyGr != null)
            {
                GrConfig gr = GrConfig.Load(legacyGr);
                JObject ratio = root["gender_ratio"] as JObject;
                if (ratio == null) { ratio = new JObject(); root["gender_ratio"] = ratio; }
                GrSettings s = gr.settings;
                ratio["enabled"] = gr.enabled;
                ratio["other_enabled"] = s.otherEnabled;
                ratio["other_percent"] = Math.Round(s.otherPercent, 2);
                ratio["female_percent"] = Math.Round(s.femalePercent, 2);
                ratio["male_percent"] = Math.Round(s.malePercent, 2);

                // 显示名升级：旧版本的默认名是「其他性别」，新默认名是「非二元性别或无性别」。
                // 只有"还是旧默认值"时才升级；用户自己改过的名字原样保留。
                string label = s.otherLabel;
                if (string.IsNullOrEmpty(label) || label == GrSettings.LegacyOtherLabel)
                {
                    label = GrSettings.DefaultOtherLabel;
                    Log.Info("other_label：旧默认显示名「" + GrSettings.LegacyOtherLabel
                             + "」已升级为「" + label + "」（想换别的名字就改 combined.json 里的 gender_ratio.other_label）");
                }
                ratio["other_label"] = label;

                ratio["lock_ratio"] = s.lockRatio;
                ratio["lock_ratio_female"] = s.lockRatioW;
                ratio["lock_ratio_male"] = s.lockRatioM;

                JObject popup = root["popup"] as JObject;
                if (popup == null) { popup = new JObject(); root["popup"] = popup; }
                popup["enabled"] = gr.popupEnabled;
                popup["hotkey"] = gr.hotkey;
                popup["step_percent"] = gr.stepPercent;
                popup["font_file"] = gr.fontFile;
                popup["female_label"] = gr.femaleLabel;
                popup["male_label"] = gr.maleLabel;

                moved++;
                MigrationNotes.Add("性别比例 ← " + legacyGr
                    + "（女 " + s.femalePercent + "% / 男 " + s.malePercent + "% / "
                    + s.otherLabel + " " + s.otherPercent + "%"
                    + (s.lockRatio ? "，锁定 " + s.lockRatioW + ":" + s.lockRatioM : "") + "）");
                Log.Info(MigrationNotes[MigrationNotes.Count - 1]);
            }
            else
            {
                Log.Info("没找到旧的 genderratio.json，性别比例使用模板默认值。");
            }

            if (legacyPs != null)
            {
                PsConfig ps = PsConfig.Load(legacyPs);

                // 只覆盖**字段值**，保留模板里的 "//xxx" 注释键（整个替换会把注释冲掉）
                JObject pj = root["portraits"] as JObject;
                if (pj == null) { pj = new JObject(); root["portraits"] = pj; }
                pj["enabled"] = ps.enabled;
                pj["autoScan"] = ps.autoScan;
                pj["neutralize3DCustomization"] = ps.neutralize3DCustomization;
                pj["poolMode"] = ps.poolMode;
                pj["defaultPortrait"] = ps.defaultPortrait;
                pj["pool"] = JArray.FromObject(ps.pool);
                pj["rules"] = JArray.FromObject(ps.rules);

                moved++;
                MigrationNotes.Add("头像替换 ← " + legacyPs
                    + "（enabled=" + ps.enabled + "，autoScan=" + ps.autoScan
                    + "，规则 " + ps.rules.Count + " 条，显式图片 " + ps.pool.Count + " 张）");
                Log.Info(MigrationNotes[MigrationNotes.Count - 1]);
            }
            else
            {
                Log.Info("没找到旧的 portraits.json，头像替换使用模板默认值。");
            }

            root["schema_version"] = SchemaVersion;

            try
            {
                File.WriteAllText(ConfigPath, root.ToString(), new UTF8Encoding(false));
                Log.Info("已写出 " + ConfigPath
                         + (moved > 0 ? "（迁移了 " + moved + " 份旧配置，你的设置没有丢）"
                                      : "（没有旧配置可迁移，全部用默认值）"));
                Log.Info("旧的 genderratio.json / portraits.json 仍然留在目录里，但**不再被读取**；"
                         + "确认新配置没问题后可以自行删除。");
            }
            catch (Exception e)
            {
                Log.Error("写出 combined.json 失败（本次仍按内存里的值运行）：" + e.Message);
            }
        }

        private static string FirstExisting(string[] paths)
        {
            foreach (string p in paths)
            {
                try { if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p; } catch { }
            }
            return null;
        }

        /// <summary>mods 目录下的兄弟目录（旧 MOD 的部署位置），找不到就返回一个不存在的路径。</summary>
        private static string SiblingDir(string modDir, string name)
        {
            try
            {
                string parent = Path.GetDirectoryName(modDir);
                if (string.IsNullOrEmpty(parent)) return modDir;
                return Path.Combine(parent, name);
            }
            catch { return modDir; }
        }

        /// <summary>连模板文件都没有时的兜底底稿（无注释，但字段齐全）。</summary>
        private static JObject BuiltinTemplate()
        {
            JObject root = new JObject();
            root["schema_version"] = SchemaVersion;

            JObject ratio = new JObject();
            ratio["enabled"] = true;
            ratio["other_enabled"] = true;
            ratio["other_percent"] = 10;
            ratio["female_percent"] = 46;
            ratio["male_percent"] = 44;
            ratio["other_label"] = "非二元性别或无性别";
            ratio["lock_ratio"] = false;
            ratio["lock_ratio_female"] = 23;
            ratio["lock_ratio_male"] = 22;
            root["gender_ratio"] = ratio;

            JObject popup = new JObject();
            popup["enabled"] = true;
            popup["hotkey"] = "F9";
            popup["step_percent"] = 1;
            popup["font_file"] = "";
            popup["female_label"] = "女";
            popup["male_label"] = "男";
            root["popup"] = popup;

            PsConfig ps = new PsConfig();
            root["portraits"] = PsConfig.ToJObject(ps);
            return root;
        }

        // ================================================================ 写回

        /// <summary>
        /// 把内存里的性别比例写回 combined.json 的 gender_ratio 段（UI 的「应用并写回配置」用）。
        /// 其余段原样保留。
        /// </summary>
        internal static bool SaveGenderRatio(GrSettings s)
        {
            if (string.IsNullOrEmpty(ConfigPath)) return false;
            return GrConfig.SaveSettings(ConfigPath, s);
        }

        /// <summary>
        /// 把 <c>portraits.enabled</c> 写回 combined.json。
        /// M6（文档 6.4）"一张肖像都没有 ⇒ 强制关闭替换"必须**写到文件里**，
        /// 否则用户下次看到配置里写着 true、却怎么都不换头像，会以为 mod 坏了。
        /// 只改这一个键，其余段（含注释）原样保留。
        /// </summary>
        internal static bool SavePortraitsEnabled(bool enabled)
        {
            if (string.IsNullOrEmpty(ConfigPath)) return false;
            try
            {
                JObject root;
                if (File.Exists(ConfigPath))
                {
                    string text = File.ReadAllText(ConfigPath);
                    root = string.IsNullOrEmpty(text.Trim()) ? new JObject() : JObject.Parse(text);
                }
                else
                {
                    root = new JObject();
                }

                JObject pj = root["portraits"] as JObject;
                if (pj == null) { pj = new JObject(); root["portraits"] = pj; }
                pj["enabled"] = enabled;

                File.WriteAllText(ConfigPath, root.ToString(), new UTF8Encoding(false));
                Log.Info("已把 portraits.enabled=" + enabled + " 写回 " + ConfigPath);
                return true;
            }
            catch (Exception e)
            {
                Log.Warn("写回 portraits.enabled 失败（不影响本次运行）：" + e.Message);
                return false;
            }
        }
    }
}
