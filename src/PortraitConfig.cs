using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PilotCombined.Portrait
{
    /// <summary>一条匹配规则。</summary>
    public class PortraitRule
    {
        /// <summary>
        /// 匹配依据：pilotDefId | callsign | name | tag | settingsId | any
        /// </summary>
        public string match = "pilotDefId";

        /// <summary>匹配值，支持 '*' 通配符。</summary>
        public string value = "";

        /// <summary>相对 mod 目录的图片路径，例如 "portraits/female/01.png"。</summary>
        public string image = "";

        /// <summary>可选性别限制：Male / Female / NonBinary。留空表示不限。</summary>
        public string gender = "";
    }

    /// <summary>
    /// 「头像替换」这一段配置的模型。
    /// M2 之后它是 combined.json 里的 <c>portraits</c> 段（由 <c>CombinedConfig</c> 调
    /// <see cref="FromJObject"/> 解析）；<see cref="Load"/> 只在"迁移旧 portraits.json"时用。
    /// </summary>
    public class PortraitConfig
    {
        public bool enabled = true;

        /// <summary>
        /// true（默认）= 当 pool 为空时，自动扫描 mod 目录下 portraits\ 里的所有图片
        /// （含子目录）。于是"把图丢进 portraits\female\ 和 portraits\male\ 就完事"。
        /// </summary>
        public bool autoScan = true;

        /// <summary>true = 隐藏角色创建界面的「自定义外貌」按钮。</summary>
        public bool neutralize3DCustomization = false;

        /// <summary>
        /// true（默认）= 角色创建的「角色头像」列表**只显示与所选代词同性的预设**。
        ///
        /// 原版列表完全不看性别，会把三种性别的预设混在一起（选男性也会看到女性预设，
        /// 以及那唯一的非二元预设 —— 换成替换头像后就是一张仿生人）。这个开关就是
        /// "要不要按代词过滤"；**关掉即完全回到原版行为**（连补丁都不安装）。
        /// 另外：该性别的预设少于 8 个、或头像池里没有该性别的图时，会自动退回"显示全部"。
        /// </summary>
        public bool filterGalleryByPronoun = true;

        /// <summary>hash（推荐）| sequential</summary>
        public string poolMode = "hash";

        /// <summary>兜底单图，留空表示未命中的驾驶员保持游戏原样。</summary>
        public string defaultPortrait = "";

        /// <summary>显式图片池。留空则用 autoScan。</summary>
        public List<string> pool = new List<string>();

        public List<PortraitRule> rules = new List<PortraitRule>();

        internal static PortraitConfig Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    Log.Warn("找不到配置文件，使用空配置：" + path);
                    return new PortraitConfig();
                }
                string json = File.ReadAllText(path);
                PortraitConfig cfg = JsonConvert.DeserializeObject<PortraitConfig>(json);
                if (cfg == null) cfg = new PortraitConfig();
                if (cfg.pool == null) cfg.pool = new List<string>();
                if (cfg.rules == null) cfg.rules = new List<PortraitRule>();
                return cfg;
            }
            catch (Exception e)
            {
                Log.Error("读取 portraits.json 失败，使用空配置：" + e.Message);
                return new PortraitConfig();
            }
        }

        /// <summary>
        /// 从 combined.json 的 <c>"portraits"</c> 段读取（融合后的主路径）。
        /// 段里的 <c>"//xxx"</c> 注释键会被 JSON.NET 当成未知成员自动忽略。
        /// </summary>
        internal static PortraitConfig FromJObject(JToken token)
        {
            try
            {
                if (token == null || token.Type == JTokenType.Null) return new PortraitConfig();
                PortraitConfig cfg = token.ToObject<PortraitConfig>();
                if (cfg == null) cfg = new PortraitConfig();
                if (cfg.pool == null) cfg.pool = new List<string>();
                if (cfg.rules == null) cfg.rules = new List<PortraitRule>();
                return cfg;
            }
            catch (Exception e)
            {
                Log.Error("读取 combined.json 的 portraits 段失败，头像使用默认设置：" + e.Message);
                return new PortraitConfig();
            }
        }

        /// <summary>转成 JSON 对象（只在"连模板文件都找不到"的兜底路径里用）。</summary>
        internal static JObject ToJObject(PortraitConfig cfg)
        {
            return JObject.FromObject(cfg == null ? new PortraitConfig() : cfg);
        }
    }
}
