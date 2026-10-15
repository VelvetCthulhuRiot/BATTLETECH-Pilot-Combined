using System;
using System.Collections.Generic;
using System.IO;
using BattleTech;
using UnityEngine;

// 类型别名：本文件在 PilotCombined.Portrait 里，直接用 GenderRatioSettings 要跨命名空间，
// 而 `using PilotCombined.GenderRatio;` 会让 Log / Patches 产生二义性 —— 用别名最干净。
using GrSettings = PilotCombined.GenderRatio.GenderRatioSettings;

namespace PilotCombined.Portrait
{
    /// <summary>图片池里的一张图 + 它的性别归属。</summary>
    internal class PoolEntry
    {
        internal string Path;          // 相对 mod 目录的规范路径
        internal Gender Gender;        // Gender.None = 未标注
    }

    /// <summary>
    /// 名字 -> 图片的解析与缓存。
    ///
    /// 三层缓存，目的是做到"游戏可以随便销毁我给它的贴图"：
    ///
    ///   1. Protypes   —— 我们自己的底图（按尺寸缩放好的），**绝不交给游戏**，所以永不失效。
    ///   2. Handouts   —— 交给游戏放进 PortraitManager 字典的那一份，每个
    ///                    (settingsId, 尺寸) 独占一张。游戏会 DestroyImmediate 它
    ///                    （PortraitManager.Clear / ClearLargePortraits / ClearPortraits），
    ///                    所以每次取用前都用 Unity 的 == 判断存活，死了就照底图重建。
    ///   3. Sprites    —— 固定位图那条路（PilotDef.GetPortraitSprite*）用的 Sprite，
    ///                    基于底图创建并验活。
    ///
    /// 性别：PortraitSettings.get_Gender() 是游戏自己按 headMesh 推出来的
    /// （Female=2 / Male=3 / NonBinary=4，headMesh <= 0.4 判为 Male），
    /// 固定位图驾驶员则读 HumanDescriptionDef.Gender。
    /// 图片池按"目录名 / 文件名前缀"自动分性别，因此不需要逐张手工标注。
    /// </summary>
    internal static class PortraitRegistry
    {
        internal static string ModDirectory = "";
        internal static PortraitConfig Config = new PortraitConfig();

        private static readonly Dictionary<string, Texture2D> Prototypes = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Texture2D> Handouts = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        private static readonly HashSet<string> Failed = new HashSet<string>();

        private static readonly Dictionary<string, string> Assignment = new Dictionary<string, string>();
        private static readonly Dictionary<string, int> Cursor = new Dictionary<string, int>();
        private static readonly Dictionary<string, string> LoggedChoice = new Dictionary<string, string>();

        private static readonly List<PoolEntry> Pool = new List<PoolEntry>();
        private static readonly Dictionary<Gender, List<PoolEntry>> PoolByGender = new Dictionary<Gender, List<PoolEntry>>();
        private static readonly List<PoolEntry> PoolUntagged = new List<PoolEntry>();
        private static bool _poolBuilt;

        /// <summary>
        /// ★ 角色创建列表专用的「指定格子 → 图片」映射（settingsId → 相对路径）。
        ///
        /// 为什么需要它：游戏的「角色头像」列表格子数是**按预设性别固定**的
        /// （实测：女 44 格 / 男 45 格 / 非二元只有 1 格），而"每个格子按哈希分一张图"
        /// 会导致**能出现的不重复图片数 ≤ 该性别的格子数** —— 用户有 59 张女性图却只有
        /// 44 格，于是 15 张永远选不到；非二元只有 1 格，4 张仿生人只能出现 1 张。
        ///
        /// 解决方案（用户决策 X）：**把"格子"和"预设性别"解耦** ——
        /// 列表变成"每个格子一张你的图、每张图恰好一个格子"，
        /// 由 <c>PortraitGalleryFilter</c> 在创建界面里写好这里的映射。
        /// </summary>
        private static readonly Dictionary<string, string> GalleryOverride = new Dictionary<string, string>();

        private static readonly Dictionary<string, string> SettingsToPilotDefId = new Dictionary<string, string>();
        private static readonly Dictionary<string, PilotDef> KnownPilotDefs = new Dictionary<string, PilotDef>();
        private static string _lastSettingsId;
        private static string _lastPilotDefId;

        // 游戏实际用到的四档尺寸（PortraitManager.PortraitSizes）
        private static readonly int[] AllSizes = new int[] { 64, 128, 256, 512 };

        // ================================================================ 关联（只读）

        internal static void RememberSettings(string settingsId, PilotDef pilotDef)
        {
            if (string.IsNullOrEmpty(settingsId) || pilotDef == null) return;
            string pid = PilotDefId(pilotDef);
            if (string.IsNullOrEmpty(pid)) return;
            if (settingsId == _lastSettingsId && pid == _lastPilotDefId) return;
            _lastSettingsId = settingsId;
            _lastPilotDefId = pid;
            SettingsToPilotDefId[settingsId] = pid;
            if (!KnownPilotDefs.ContainsKey(pid)) KnownPilotDefs[pid] = pilotDef;
        }

        private static string PilotDefId(PilotDef def)
        {
            try
            {
                if (def.Description != null && !string.IsNullOrEmpty(def.Description.Id)) return def.Description.Id;
            }
            catch { }
            return null;
        }

        // ================================================================ 对外入口

        /// <summary>
        /// **头像替换的总开关**。false 时这两个入口一律返回 null ⇒ 所有头像补丁都变成空操作，
        /// 游戏完全走原版肖像机制（3D 渲染 / 原版固定位图）。
        ///
        /// 为什么用"入口拦一道"而不是"卸载补丁"：Harmony 补丁是启动时装的，
        /// 而用户要在 F9 窗口里**随时**开关；入口判一个 bool 既即时、又不需要重启。
        /// （装了补丁但一律放行，行为与原版完全一致。）
        /// </summary>
        internal static bool Enabled
        {
            get { return Config != null && Config.enabled; }
        }

        /// <summary>3D 形态驾驶员：返回选中的图片路径（相对 mod 目录），null 表示不接管。</summary>
        internal static string ResolveImageForSettings(BattleTech.Portraits.PortraitSettings settings)
        {
            if (!Enabled) return null;
            if (settings == null) return null;
            string settingsId = null;
            Gender gender = Gender.None;
            try
            {
                if (settings.Description != null) settingsId = settings.Description.Id;
                gender = settings.Gender;
            }
            catch { }
            if (string.IsNullOrEmpty(settingsId)) return null;

            PilotDef def = null;
            string pid;
            if (SettingsToPilotDefId.TryGetValue(settingsId, out pid)) KnownPilotDefs.TryGetValue(pid, out def);

            return ResolveImage(def, settingsId, gender);
        }

        /// <summary>固定位图驾驶员：返回选中的图片路径（相对 mod 目录）。</summary>
        internal static string ResolveImageForPilotDef(PilotDef def)
        {
            if (!Enabled) return null;
            if (def == null) return null;
            Gender gender = Gender.None;
            try { if (def.Description != null) gender = def.Description.Gender; } catch { }
            return ResolveImage(def, null, gender);
        }

        // ================================================================ 选择逻辑

        private static string ResolveImage(PilotDef def, string settingsId, Gender gender)
        {
            if (!Config.enabled) return null;

            string pilotDefId = def != null ? PilotDefId(def) : null;
            string callsign = SafeCallsign(def);
            string name = SafeName(def);

            // 0) ★ 角色创建列表的「指定格子 → 图片」映射（由 PortraitGalleryFilter 设置）。
            //    必须优先于规则与哈希分配：这张脸是玩家在创建界面里**看到并选中**的那一张，
            //    之后渲染指挥官头像时必须还是它。
            if (!string.IsNullOrEmpty(settingsId))
            {
                string ov;
                if (GalleryOverride.TryGetValue(settingsId, out ov) && EnsureUsable(ov))
                {
                    Note(pilotDefId, callsign, settingsId, gender, ov, "gallery");
                    return ov;
                }
            }

            // 1) 显式规则
            for (int i = 0; i < Config.rules.Count; i++)
            {
                PortraitRule rule = Config.rules[i];
                if (rule == null || string.IsNullOrEmpty(rule.image)) continue;
                if (!GenderAllowed(rule.gender, gender)) continue;

                string mode = (rule.match ?? "").ToLowerInvariant();
                string subject;
                switch (mode)
                {
                    case "any": subject = ""; break;
                    case "pilotdefid": subject = pilotDefId; break;
                    case "callsign": subject = callsign; break;
                    case "name": subject = name; break;
                    case "tag": subject = FirstMatchingTag(def, rule.value); break;
                    case "settingsid": subject = settingsId; break;
                    default: subject = null; break;
                }

                bool hit;
                if (mode == "any") hit = true;
                else if (mode == "tag") hit = subject != null;
                else hit = subject != null && Wildcard(rule.value, subject);

                if (hit && EnsureUsable(rule.image))
                {
                    Note(pilotDefId, callsign, settingsId, gender, rule.image, "rule:" + rule.match + "=" + rule.value);
                    return rule.image;
                }
            }

            // 2) 图片池（按性别过滤）
            if (!_poolBuilt) BuildPool();
            if (Pool.Count > 0)
            {
                List<PoolEntry> candidates = CandidatesFor(gender);
                if (candidates.Count > 0)
                {
                    string key = StableKey(pilotDefId, callsign, settingsId);
                    string image = PickFromPool(key, candidates);
                    if (!string.IsNullOrEmpty(image) && EnsureUsable(image))
                    {
                        Note(pilotDefId, callsign, settingsId, gender, image,
                             "pool/" + Config.poolMode + "/" + GenderTag(gender) + "[" + candidates.Count + "]");
                        return image;
                    }
                }
            }

            // 3) 兜底
            if (!string.IsNullOrEmpty(Config.defaultPortrait) && EnsureUsable(Config.defaultPortrait))
            {
                Note(pilotDefId, callsign, settingsId, gender, Config.defaultPortrait, "default");
                return Config.defaultPortrait;
            }

            return null;
        }

        /// <summary>只做存在性与可解码性校验，真正的贴图按需生成。</summary>
        private static bool EnsureUsable(string relativePath)
        {
            if (Failed.Contains(relativePath)) return false;
            string full = FullPathOf(relativePath);
            if (!File.Exists(full))
            {
                Log.Error("图片不存在：" + full);
                Failed.Add(relativePath);
                return false;
            }
            return true;
        }

        private static string PickFromPool(string key, List<PoolEntry> candidates)
        {
            string cached;
            if (Assignment.TryGetValue(key, out cached) && InList(candidates, cached)) return cached;

            string chosen;
            if (string.Equals(Config.poolMode, "sequential", StringComparison.OrdinalIgnoreCase))
            {
                int c;
                Cursor.TryGetValue("c", out c);
                chosen = candidates[c % candidates.Count].Path;
                Cursor["c"] = c + 1;
            }
            else
            {
                chosen = candidates[(int)(Fnv1a(key) % (uint)candidates.Count)].Path;
            }
            Assignment[key] = chosen;
            return chosen;
        }

        private static bool InList(List<PoolEntry> list, string path)
        {
            for (int i = 0; i < list.Count; i++) if (list[i].Path == path) return true;
            return false;
        }

        private static List<PoolEntry> CandidatesFor(Gender gender)
        {
            List<PoolEntry> exact;
            if (gender != Gender.None && PoolByGender.TryGetValue(gender, out exact) && exact.Count > 0) return exact;
            if (PoolUntagged.Count > 0) return PoolUntagged;
            return Pool;
        }

        private static bool GenderAllowed(string ruleGender, Gender pilotGender)
        {
            if (string.IsNullOrEmpty(ruleGender)) return true;
            Gender g = ParseGender(ruleGender);
            if (g == Gender.None) return true;
            return g == pilotGender;
        }

        internal static Gender ParseGender(string s)
        {
            // 名字规则集中在 PilotCombined.GenderNames（不引用 UnityEngine，所以有脱机单测覆盖）
            switch (GenderNames.SlotFromToken(s))
            {
                case GenderNames.Female: return Gender.Female;
                case GenderNames.Male: return Gender.Male;
                case GenderNames.Other: return Gender.NonBinary;
                default: return Gender.None;
            }
        }

        private static string GenderTag(Gender g)
        {
            switch (g)
            {
                case Gender.Male: return GenderNames.TagOf(GenderNames.Male);
                case Gender.Female: return GenderNames.TagOf(GenderNames.Female);
                case Gender.NonBinary: return GenderNames.TagOf(GenderNames.Other);
                default: return "unknown";
            }
        }

        // ================================================================ 图片池构建

        private static void BuildPool()
        {
            _poolBuilt = true;
            Pool.Clear();
            PoolByGender.Clear();
            PoolUntagged.Clear();

            List<string> paths = new List<string>();
            bool explicitPool = Config.pool != null && Config.pool.Count > 0;
            if (explicitPool)
            {
                for (int i = 0; i < Config.pool.Count; i++)
                {
                    string p = Config.pool[i];
                    if (string.IsNullOrEmpty(p)) continue;
                    if (File.Exists(FullPathOf(p))) paths.Add(Canonical(p));
                    else Log.Error("pool 里的图片不存在：" + p);
                }
            }
            else if (Config.autoScan)
            {
                string dir = Path.Combine(ModDirectory, "portraits");
                if (Directory.Exists(dir))
                {
                    string[] files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < files.Length; i++)
                    {
                        string ext = Path.GetExtension(files[i]).ToLowerInvariant();
                        if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".dds")
                            paths.Add(Canonical(files[i]));
                    }
                }
                else
                {
                    Log.Warn("没找到 portraits 目录：" + dir);
                }
            }

            for (int i = 0; i < paths.Count; i++)
            {
                PoolEntry e = new PoolEntry();
                e.Path = paths[i];
                e.Gender = DetectGender(paths[i]);
                Pool.Add(e);
                if (e.Gender == Gender.None) PoolUntagged.Add(e);
                else
                {
                    List<PoolEntry> l;
                    if (!PoolByGender.TryGetValue(e.Gender, out l)) { l = new List<PoolEntry>(); PoolByGender[e.Gender] = l; }
                    l.Add(e);
                }
            }

            // ★ 目录名不认识 ⇒ 那批图会**静默**变成"未标注"，性别可用性判断（M5 肖像池优先）就错了。
            //   这是用户最容易翻车的地方（文件夹拼写 / 用了没约定的词），所以必须明确报警。
            List<string> unknownDirs = new List<string>();
            for (int i = 0; i < paths.Count; i++)
            {
                string[] bad = GenderNames.UnrecognizedDirTokens(paths[i]);
                for (int j = 0; j < bad.Length; j++)
                    if (!unknownDirs.Contains(bad[j])) unknownDirs.Add(bad[j]);
            }
            if (unknownDirs.Count > 0)
            {
                Log.Warn("portraits 下有**名字不认识**的子目录：" + string.Join("、", unknownDirs.ToArray())
                         + " —— 这些目录里的图片会被当成『未标注』，"
                         + "『肖像池优先』会因此认为对应性别没有肖像。"
                         + "请改用 female / male / other（也认 nonbinary / 中性 / 机器人 等别名）。");
            }

            Log.Info("图片池：" + (explicitPool ? "来自配置 pool" : "自动扫描 portraits 目录")
                     + "，共 " + Pool.Count + " 张 —— 女 " + CountOf(Gender.Female)
                     + " / 男 " + CountOf(Gender.Male)
                     + " / " + OtherLabel() + " " + CountOf(Gender.NonBinary)
                     + " / 未标注 " + PoolUntagged.Count);
            // 抽样一行：每类列出第一张图的路径。分性别读错（例如目录名不被识别）时，
            // 这一行能立刻看出"某一类其实一张都没有 / 全跑到未标注去了"。
            Log.Info("图片池抽样（每类第一张，用来确认目录名有没有读对）："
                     + "女=" + FirstPathOf(Gender.Female)
                     + " | 男=" + FirstPathOf(Gender.Male)
                     + " | " + OtherLabel() + "=" + FirstPathOf(Gender.NonBinary)
                     + " | 未标注=" + (PoolUntagged.Count > 0 ? PoolUntagged[0].Path : "-"));
            if (Pool.Count > 0 && PoolUntagged.Count == 0
                && (PoolByGender.Count == 1 || (CountOf(Gender.Male) > 0 && CountOf(Gender.Female) == 0)))
            {
                Log.Warn("提示：图片池只覆盖了一部分性别。建议按 portraits\\male\\ 与 portraits\\female\\ 分目录，"
                         + "否则异性驾驶员也会分到这些图（可能造成头像与语音不匹配）。");
            }
        }

        private static int CountOf(Gender g)
        {
            List<PoolEntry> l;
            return PoolByGender.TryGetValue(g, out l) ? l.Count : 0;
        }

        /// <summary>某个性别池子里的第一张图（诊断日志用）。</summary>
        private static string FirstPathOf(Gender g)
        {
            List<PoolEntry> l;
            if (PoolByGender.TryGetValue(g, out l) && l.Count > 0) return l[0].Path;
            return "-";
        }

        /// <summary>
        /// 池子里有没有**任何一张**图。M6（文档 6.4）用它决定"头像替换功能能不能开启"：
        /// 一张都没有 ⇒ 强制关闭替换，只剩性别比例调节。
        /// </summary>
        internal static bool HasAnyPortrait()
        {
            EnsurePool();
            return Pool.Count > 0;
        }

        // ================================================================ 对外：图片池计数（M4）

        /// <summary>确保图片池已构建（幂等；真正解码贴图仍然是懒的）。</summary>
        private static void EnsurePool()
        {
            if (!_poolBuilt) BuildPool();
        }

        /// <summary>
        /// 某个性别的图片张数。**读的是缓存好的池，不重扫目录** ——
        /// 机制层（每次生成驾驶员）会调用它，所以这里必须廉价（文档 6.10）。
        /// </summary>
        internal static int CountOfGender(Gender g)
        {
            EnsurePool();
            return CountOf(g);
        }

        /// <summary>有用例的性别是否齐全，以及总数量的可读摘要（日志与窗口共用）。</summary>
        internal static string PoolSummary()
        {
            EnsurePool();
            return "女 " + CountOf(Gender.Female)
                 + " / 男 " + CountOf(Gender.Male)
                 + " / " + OtherLabel() + " " + CountOf(Gender.NonBinary)
                 + " / 未标注 " + PoolUntagged.Count
                 + "（共 " + Pool.Count + " 张）";
        }

        /// <summary>
        /// ★ 是否应该按「肖像池优先」裁剪性别权重（文档 6.1/6.2）。
        /// 条件：**头像替换开着** 且 **池子里至少有一张图**。
        ///
        /// 第二个条件就是文档 6.4 的核心（"一张肖像都没有 ⇒ 替换功能不可开启"）：
        /// 池子为空时绝不能进入"肖像优先"模式 —— 那会把三个权重都压成 0，
        /// 或者退化成 1,1,1（三性别等概率），两种都不是用户想要的。
        /// （M6 会把"强制把 portraits.enabled 压成 false + 写回配置 + UI 变灰"补齐。）
        /// </summary>
        internal static bool PortraitPriorityActive()
        {
            if (Config == null || !Config.enabled) return false;
            EnsurePool();
            return Pool.Count > 0;
        }

        /// <summary>
        /// 丢掉缓存重扫图片池（窗口的「立即重扫图片池」按钮用）。
        /// 只重建**文件清单**；已解码的贴图仍按路径复用，所以不会打断正在显示的头像。
        /// </summary>
        internal static void RescanPool()
        {
            _poolBuilt = false;
            BuildPool();
            Log.Info("已重新扫描图片池：" + PoolSummary());
        }

        /// <summary>
        /// 某个性别的图片路径列表（顺序稳定 —— 按文件路径排序，见 BuildPool）。
        /// 供 <c>PortraitGalleryFilter</c> 做"格子 → 图片"的一对一分配。
        /// </summary>
        internal static List<string> PathsOf(Gender g)
        {
            EnsurePool();
            List<string> result = new List<string>();
            List<PoolEntry> l;
            if (g != Gender.None && PoolByGender.TryGetValue(g, out l))
                for (int i = 0; i < l.Count; i++) result.Add(l[i].Path);
            return result;
        }

        /// <summary>
        /// **所有**图片的路径（不分性别）。「他们」这个代词按用户决策当作"不限制性别"处理时用它。
        /// </summary>
        internal static List<string> AllPaths()
        {
            EnsurePool();
            List<string> result = new List<string>();
            for (int i = 0; i < Pool.Count; i++) result.Add(Pool[i].Path);
            return result;
        }

        // ================================================================ 对外：角色创建列表的指定映射

        /// <summary>把一个列表格子的 settingsId 指定到某张图（用户决策 X）。</summary>
        internal static void SetGalleryOverride(string settingsId, string relativePath)
        {
            if (string.IsNullOrEmpty(settingsId) || string.IsNullOrEmpty(relativePath)) return;
            GalleryOverride[settingsId] = relativePath;
        }

        /// <summary>
        /// 清掉所有指定映射。<paramref name="keepSettingsId"/> 不为空时**保留它** ——
        /// 玩家最终选中的那张必须继续生效，否则创建完指挥官、头像会变回哈希分配的那张。
        /// </summary>
        internal static void ClearGalleryOverrides(string keepSettingsId)
        {
            if (GalleryOverride.Count == 0) return;
            string keepPath = null;
            bool hasKeep = false;
            if (!string.IsNullOrEmpty(keepSettingsId))
                hasKeep = GalleryOverride.TryGetValue(keepSettingsId, out keepPath);
            GalleryOverride.Clear();
            if (hasKeep && keepPath != null) GalleryOverride[keepSettingsId] = keepPath;
        }

        internal static int GalleryOverrideCount { get { return GalleryOverride.Count; } }

        /// <summary>
        /// 丢掉某个 settingsId 已经"交给游戏"的那几份贴图，强制下次取图时重建。
        /// 用途：列表格子的图片被改成另一张之后，缩略图必须重画，否则会一直显示旧图。
        /// </summary>
        internal static void InvalidateHandouts(string settingsId)
        {
            if (string.IsNullOrEmpty(settingsId) || Handouts.Count == 0) return;
            string prefix = settingsId + "|";
            List<string> kill = null;
            foreach (KeyValuePair<string, Texture2D> kv in Handouts)
            {
                if (kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    if (kill == null) kill = new List<string>();
                    kill.Add(kv.Key);
                }
            }
            if (kill == null) return;
            for (int i = 0; i < kill.Count; i++) Handouts.Remove(kill[i]);
        }

        /// <summary>第三类性别的显示名（读 combined.json 的 gender_ratio.other_label，与窗口保持一致）。</summary>
        private static string OtherLabel()
        {
            try
            {
                if (CombinedConfig.GenderRatio != null && CombinedConfig.GenderRatio.settings != null
                    && !string.IsNullOrEmpty(CombinedConfig.GenderRatio.settings.otherLabel))
                    return CombinedConfig.GenderRatio.settings.otherLabel;
            }
            catch { }
            return GrSettings.DefaultOtherLabel;
        }

        /// <summary>从目录名或文件名前缀推断性别，用户无需逐张标注（规则见 GenderNames）。</summary>
        internal static Gender DetectGender(string relativePath)
        {
            switch (GenderNames.SlotFromPath(relativePath))
            {
                case GenderNames.Female: return Gender.Female;
                case GenderNames.Male: return Gender.Male;
                case GenderNames.Other: return Gender.NonBinary;
                default: return Gender.None;
            }
        }

        // ================================================================ 贴图（三层缓存）

        private static string FullPathOf(string relativePath)
        {
            string full = relativePath;
            if (!Path.IsPathRooted(full)) full = Path.Combine(ModDirectory, relativePath);
            try { full = Path.GetFullPath(full); } catch { }
            return full;
        }

        private static string Canonical(string path)
        {
            string f = path.Replace('\\', '/');
            string root = ModDirectory.Replace('\\', '/').TrimEnd('/') + "/";
            if (f.StartsWith(root, StringComparison.OrdinalIgnoreCase)) f = f.Substring(root.Length);
            return f;
        }

        /// <summary>
        /// 取一张"原型"贴图（按尺寸缩放好的底图）。这是我们自己的资产，永不交给游戏。
        /// 某个路径第一次被用到时，一次解码 + 建好全部四档尺寸，避免反复读盘。
        /// </summary>
        private static Texture2D GetPrototype(string relativePath, int size)
        {
            if (size <= 0) return null;
            string key = relativePath + "|" + size;
            Texture2D p;
            if (Prototypes.TryGetValue(key, out p))
            {
                if (p != null) return p;      // Unity 的 == 对已销毁对象返回 true
                Prototypes.Remove(key);
            }

            if (Failed.Contains(relativePath)) return null;

            if (NeedsBuild(relativePath, size)) BuildPrototypes(relativePath, size);

            if (Prototypes.TryGetValue(key, out p) && p != null) return p;
            return null;
        }

        private static bool NeedsBuild(string relativePath, int size)
        {
            Texture2D t;
            if (!Prototypes.TryGetValue(relativePath + "|" + size, out t) || t == null) return true;
            for (int i = 0; i < AllSizes.Length; i++)
            {
                if (!Prototypes.TryGetValue(relativePath + "|" + AllSizes[i], out t) || t == null) return true;
            }
            return false;
        }

        /// <summary>
        /// 某个图片路径第一次被用到时，一次解码 + 建好四档标准尺寸（外加本次需要的尺寸），
        /// 之后全尺寸复用，避免反复读盘解码。
        /// </summary>
        private static void BuildPrototypes(string relativePath, int extraSize)
        {
            string full = FullPathOf(relativePath);
            Texture2D src = null;
            try
            {
                if (!File.Exists(full)) { Log.Error("图片不存在：" + full); Failed.Add(relativePath); return; }
                byte[] bytes = File.ReadAllBytes(full);
                src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                src.wrapMode = TextureWrapMode.Clamp;
                if (!src.LoadImage(bytes))
                {
                    Log.Error("无法解码图片（需要 PNG / JPG / DDS）：" + full);
                    Failed.Add(relativePath);
                    return;
                }

                int srcMax = Mathf.Max(src.width, src.height);
                List<int> targets = new List<int>(AllSizes);
                if (extraSize > 0 && Array.IndexOf(AllSizes, extraSize) < 0) targets.Add(extraSize);

                for (int i = 0; i < targets.Count; i++)
                {
                    int wanted = targets[i];
                    int target = Mathf.Min(wanted, srcMax);
                    string key = relativePath + "|" + wanted;
                    Texture2D exist;
                    if (Prototypes.TryGetValue(key, out exist) && exist != null) continue;
                    Prototypes[key] = Downscale(src, target, relativePath);
                }
                Log.Info("已载入头像 " + src.width + "x" + src.height + " : " + relativePath);
            }
            catch (Exception e)
            {
                Log.Error("加载图片异常 " + full + " : " + e.Message);
                Failed.Add(relativePath);
            }
            finally
            {
                if (src != null)
                {
                    try { UnityEngine.Object.Destroy(src); } catch { }
                }
            }
        }

        /// <summary>阶梯式折半缩放（和游戏自身 DownsampleSprite 同一思路），避免大比例缩放产生锯齿。</summary>
        private static Texture2D Downscale(Texture2D src, int target, string tag)
        {
            if (target <= 0) target = 1;
            RenderTexture cur = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            try
            {
                Graphics.Blit(src, cur);
                int w = src.width, h = src.height;
                while (w > target * 2 && h > target * 2)
                {
                    int nw = Mathf.Max(target, w / 2);
                    int nh = Mathf.Max(target, h / 2);
                    RenderTexture next = RenderTexture.GetTemporary(nw, nh, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(cur, next);
                    RenderTexture.ReleaseTemporary(cur);
                    cur = next; w = nw; h = nh;
                }

                RenderTexture final = RenderTexture.GetTemporary(target, target, 0, RenderTextureFormat.ARGB32);
                Texture2D dst = null;
                RenderTexture prev = RenderTexture.active;
                try
                {
                    Graphics.Blit(cur, final);
                    RenderTexture.active = final;
                    dst = new Texture2D(target, target, TextureFormat.RGBA32, false);
                    dst.wrapMode = TextureWrapMode.Clamp;
                    dst.ReadPixels(new Rect(0f, 0f, target, target), 0, 0);
                    dst.Apply();
                    dst.name = "PortraitSwapProto_" + target + "_" + Path.GetFileNameWithoutExtension(tag);
                }
                finally
                {
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(final);
                }
                return dst;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(cur);
            }
        }

        /// <summary>
        /// 取得"交给游戏"的那一张贴图。每个 (settingsId, 尺寸) 独占一张，
        /// 因为游戏会 DestroyImmediate 掉字典里的贴图；用前必须验活。
        /// </summary>
        internal static Texture2D GetHandout(string settingsId, string relativePath, int size)
        {
            string key = settingsId + "|" + size;
            Texture2D t;
            if (Handouts.TryGetValue(key, out t))
            {
                if (t != null) return t;
                Handouts.Remove(key);        // 被游戏销毁了
                Log.Info("检测到贴图已被游戏清理，重建：" + relativePath + " @" + size + " (settingsId=" + settingsId + ")");
            }

            Texture2D proto = GetPrototype(relativePath, size);
            if (proto == null) return null;

            Texture2D copy = CopyOf(proto);
            if (copy == null) return null;
            Handouts[key] = copy;
            return copy;
        }

        private static Texture2D CopyOf(Texture2D src)
        {
            Texture2D dst = null;
            try
            {
                dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
                dst.wrapMode = TextureWrapMode.Clamp;
                Graphics.CopyTexture(src, dst);
                dst.name = src.name + "_inst";
                return dst;
            }
            catch
            {
                // 退路：RenderTexture 中转
                try
                {
                    if (dst != null) UnityEngine.Object.Destroy(dst);
                    RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
                    RenderTexture prev = RenderTexture.active;
                    try
                    {
                        Graphics.Blit(src, rt);
                        RenderTexture.active = rt;
                        dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
                        dst.wrapMode = TextureWrapMode.Clamp;
                        dst.ReadPixels(new Rect(0f, 0f, src.width, src.height), 0, 0);
                        dst.Apply();
                        return dst;
                    }
                    finally
                    {
                        RenderTexture.active = prev;
                        RenderTexture.ReleaseTemporary(rt);
                    }
                }
                catch (Exception e)
                {
                    Log.Error("复制贴图失败：" + e.Message);
                    return null;
                }
            }
        }

        /// <summary>固定位图驾驶员那条路要的是 Sprite，基于我们自己的原型图创建。</summary>
        internal static Sprite GetSprite(string relativePath)
        {
            Sprite cached;
            if (Sprites.TryGetValue(relativePath, out cached))
            {
                if (cached != null) return cached;
                Sprites.Remove(relativePath);
            }

            int biggest = AllSizes[AllSizes.Length - 1];
            Texture2D proto = GetPrototype(relativePath, biggest);
            if (proto == null) return null;

            try
            {
                Sprite s = Sprite.Create(proto,
                    new Rect(0f, 0f, proto.width, proto.height),
                    new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                s.name = "PortraitSwap_" + Path.GetFileNameWithoutExtension(relativePath);
                Sprites[relativePath] = s;
                return s;
            }
            catch (Exception e)
            {
                Log.Error("创建 Sprite 异常 " + relativePath + " : " + e.Message);
                return null;
            }
        }

        // ================================================================ 杂项

        private static string FirstMatchingTag(PilotDef def, string pattern)
        {
            if (def == null || def.PilotTags == null) return null;
            try
            {
                string[] tags = def.PilotTags.ToArray();
                for (int i = 0; i < tags.Length; i++)
                {
                    if (Wildcard(pattern, tags[i])) return tags[i];
                }
            }
            catch { }
            return null;
        }

        private static string SafeCallsign(PilotDef def)
        {
            try { if (def != null && def.Description != null) return def.Description.Callsign; } catch { }
            return null;
        }

        private static string SafeName(PilotDef def)
        {
            try { if (def != null && def.Description != null) return def.Description.Name; } catch { }
            return null;
        }

        private static string StableKey(string pilotDefId, string callsign, string settingsId)
        {
            if (!string.IsNullOrEmpty(pilotDefId)) return "pid:" + pilotDefId;
            if (!string.IsNullOrEmpty(callsign)) return "cs:" + callsign;
            if (!string.IsNullOrEmpty(settingsId)) return "sid:" + settingsId;
            return "unknown";
        }

        private static uint Fnv1a(string s)
        {
            uint h = 2166136261u;
            for (int i = 0; i < s.Length; i++)
            {
                h ^= (byte)(s[i] & 0xFF);
                h *= 16777619u;
                h ^= (byte)(s[i] >> 8);
                h *= 16777619u;
            }
            return h;
        }

        internal static bool Wildcard(string pattern, string text)
        {
            if (pattern == null) return false;
            if (text == null) text = "";
            if (pattern.IndexOf('*') < 0) return string.Equals(pattern, text, StringComparison.OrdinalIgnoreCase);

            string[] parts = pattern.Split('*');
            int pos = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0) continue;
                int at = text.IndexOf(part, pos, StringComparison.OrdinalIgnoreCase);
                if (at < 0) return false;
                if (i == 0 && pattern[0] != '*' && at != 0) return false;
                pos = at + part.Length;
            }
            if (pattern[pattern.Length - 1] != '*' && pos != text.Length) return false;
            return true;
        }

        private static void Note(string pilotDefId, string callsign, string settingsId, Gender gender,
                                 string image, string how)
        {
            string key = (pilotDefId ?? "-") + "|" + (settingsId ?? "-");
            string what = how + " | " + image;
            string prev;
            if (LoggedChoice.TryGetValue(key, out prev) && prev == what) return;
            LoggedChoice[key] = what;
            Log.Info("分配 -> pilotDefId=" + (pilotDefId ?? "-")
                     + " callsign=" + (callsign ?? "-")
                     + " settingsId=" + (settingsId ?? "-")
                     + " gender=" + GenderTag(gender)
                     + " | " + how + " | " + image);
        }
    }
}
