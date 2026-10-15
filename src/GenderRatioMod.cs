using System;
using System.IO;
using System.Reflection;
using BattleTech;
using Harmony;
using Newtonsoft.Json.Linq;

namespace PilotCombined.GenderRatio
{
    /// <summary>
    /// 入口。本 mod 是 **Game Mod**（mod.json），不是 System Mod —— 这不是风格问题，是硬要求：
    ///
    /// 原版 ModLoader 的 InitSystemModsLoop 在"这个系统 mod 被禁用"分支里错写成
    /// loadedGameModStatus[name].enabled = false（应该写 loadedSystemModStatus），
    /// 系统 mod 在游戏 mod 表里没有条目 → KeyNotFoundException → 被标 failedToLoad
    /// → UpdateModStatus 把它注入两个状态文件 → GetCombinedModStatus() 撞重复键
    /// → 保存永远卡在"正在保存中" + 模组界面废掉，且每次启动复现。
    /// InitGameModsLoop 同一位置写的就是它自己那张表（写之前还 ContainsKey 校验过），不会崩。
    /// 已核实 LoadGameMod 同样会调用 LoadAssemblyAndCallInit，所以游戏 mod 一样能带 DLL。
    ///
    /// ⇒ 本工程**只**产出 mod.json，绝不产出 systemMod.json（build.ps1 里还有一道保险）。
    /// </summary>
    public static class GenderRatioMod
    {
        internal const string Version = "0.2.16";

        /// <summary>与 PortraitSwap 完全隔离的 Harmony ID（PortraitSwap 用的是 com.btportraitmod.portraitswap）。</summary>
        internal const string HarmonyId = "com.btgenderratio.mod";

        /// <summary>mod.json 里的 "Name"。</summary>
        internal static string ModName = "GenderRatio";

        /// <summary>mod 所在目录名（通常与 Name 相同）。</summary>
        internal static string FolderName = "GenderRatio";

        /// <summary>mod 目录绝对路径（UI 用它做日志/写回配置）。</summary>
        internal static string ModDirectory = "";

        /// <summary>配置文件绝对路径，UI 改完值会写回这里。</summary>
        internal static string ConfigPath = "";

        /// <summary>当前生效的配置。</summary>
        internal static GenderRatioConfig Config;

        public static void Init(string modDir, string settingsJson)
        {
            try
            {
                if (string.IsNullOrEmpty(modDir)) modDir = SelfDirectory();
                modDir = Path.GetFullPath(modDir);
                ModDirectory = modDir;
                try { FolderName = new DirectoryInfo(modDir).Name; } catch { }

                string parsed = ExtractName(settingsJson);
                if (!string.IsNullOrEmpty(parsed)) ModName = parsed;

                Log.Section("性别比例子系统 v" + Version);
                Log.Info("modDir=" + modDir);
                Log.Info("mod 名称=" + ModName + "，目录名=" + FolderName);

                // 配置：融合后只有 combined.json 一个文件（M2）。
                // 读取 + 从旧 genderratio.json 迁移都在 CombinedConfig 里做，且是幂等的。
                CombinedConfig.Load(modDir);
                ConfigPath = CombinedConfig.ConfigPath;
                Config = CombinedConfig.GenderRatio;
                Patches.Config = Config;

                // 配置自检日志（交接文档 §4.12：每次改动都打一行可验证的事实）
                GenderRatioSettings s = Config.settings;
                Log.Info("配置读取完成：enabled=" + Config.enabled
                         + "，" + s.otherLabel + "=" + (s.otherEnabled ? "开(" + s.otherPercent + "%)" : "关")
                         + "，女性=" + s.femalePercent + "%"
                         + "，热键=" + Config.hotkey
                         + "，步长=" + Config.stepPercent + "%"
                         + "，popup=" + Config.popupEnabled);
                Log.Info("换算后的目标权重：女 " + WeightOf(s, 0) + " / 男 " + WeightOf(s, 1)
                         + " / " + s.otherLabel + " " + WeightOf(s, 2));
                if (Config.unknownKeys.Count > 0)
                {
                    Log.Warn("combined.json 里有本版本不认识的键（已忽略，可能是拼写错误）："
                             + string.Join(", ", Config.unknownKeys.ToArray()));
                }

                HarmonyInstance harmony = PilotCombinedMod.Harmony;
                if (harmony == null)
                {
                    Log.Error("共享的 Harmony 实例还没创建（入口调用顺序不对），性别比例补丁无法安装。");
                    return;
                }
                int ok = 0, fail = 0;

                // 0) 「不把自己算作存档必需 mod」那道防线**已经由入口统一安装**
                //    （PilotCombined.SaveGuard，M3 合并成一份），这里不再重复挂。

                // 图片池张数：M5 的「肖像池优先」要靠它决定有哪些性别可用。
                // 计数读的是缓存好的池，机制层每次生成驾驶员读它也不会重扫目录。
                Log.Info("图片池张数（影响可用性别）：" + Portrait.PortraitRegistry.PoolSummary());
                Log.Info("肖像池优先：" + (Portrait.PortraitRegistry.PortraitPriorityActive()
                         ? "已生效 —— 只生成『有替换头像』的性别，男女比例在可用范围内重新归一"
                         : "未生效（头像替换关闭或图片池为空）—— 三性别都按配置的比例生成"));

                if (!Config.enabled)
                {
                    Log.Warn("combined.json 里 gender_ratio.enabled=false：不改任何性别比例。");
                    Log.Info("补丁完成：成功 " + ok + " 个，失败 " + fail + " 个。");
                    return;
                }

                // 1) 性别产出的唯一收口
                MethodInfo target = null;
                try { target = AccessTools.Method(typeof(PilotGenerator), "GetNameAndGender"); } catch { }
                if (target == null)
                {
                    // AccessTools 对 private 方法可能返回 null（交接文档 §4.7），兜底手工查找
                    target = typeof(PilotGenerator).GetMethod("GetNameAndGender",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
                if (Patch(harmony, target, AccessTools.Method(typeof(Patches), "GetNameAndGender_Prefix"), null))
                    ok++; else fail++;

                // 2) 游戏内 UI（F9）。失败不影响上面的机制部分。
                if (GenderRatioUi.TryInstall(harmony)) ok++; else fail++;

                Log.Info("补丁完成：成功 " + ok + " 个，失败 " + fail + " 个。");
            }
            catch (Exception e)
            {
                Log.Error("Init 失败：" + e);
            }
        }

        private static int WeightOf(GenderRatioSettings s, int index)
        {
            int[] w = s.Weights();
            return w[index];
        }

        private static string ExtractName(string settingsJson)
        {
            try
            {
                if (string.IsNullOrEmpty(settingsJson)) return null;
                JObject jo = JObject.Parse(settingsJson);
                JToken t = jo["Name"];
                return t == null ? null : t.ToString();
            }
            catch { return null; }
        }

        internal static bool Patch(HarmonyInstance harmony, MethodInfo original, MethodInfo prefix, MethodInfo postfix)
        {
            if (original == null)
            {
                Log.Error("找不到目标方法，跳过修补。");
                return false;
            }
            try
            {
                HarmonyMethod pre = prefix == null ? null : new HarmonyMethod(prefix);
                HarmonyMethod post = postfix == null ? null : new HarmonyMethod(postfix);
                harmony.Patch(original, pre, post, null);
                Log.Info("已修补 " + original.DeclaringType.FullName + "." + original.Name
                         + "  ->  " + original.ToString()
                         + (prefix != null ? "  [Prefix]" : "") + (postfix != null ? "  [Postfix]" : ""));
                return true;
            }
            catch (Exception e)
            {
                Log.Error("修补 " + original.DeclaringType.FullName + "." + original.Name + " 失败：" + e.Message);
                return false;
            }
        }

        private static string SelfDirectory()
        {
            try
            {
                string loc = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(loc)) return Path.GetDirectoryName(loc);
            }
            catch { }
            return AppDomain.CurrentDomain.BaseDirectory;
        }
    }
}
