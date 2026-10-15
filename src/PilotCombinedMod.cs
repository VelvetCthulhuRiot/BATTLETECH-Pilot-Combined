using System;
using System.IO;
using Harmony;
using PilotCombined.GenderRatio;
using PilotCombined.Portrait;

namespace PilotCombined
{
    /// <summary>
    /// 融合 MOD（PilotCombined）的**唯一入口**。由 mod.json 的
    /// <c>"DLLEntryPoint": "PilotCombined.PilotCombinedMod.Init"</c> 指定。
    ///
    /// 结构：
    ///   * 本类负责"全局唯一的东西"—— 日志文件、Harmony 实例、自己的 mod.json 文本；
    ///   * 两个子系统各自负责自己的补丁：
    ///       - <see cref="GenderRatioMod"/> —— 性别刷出比例（+ F9 窗口）
    ///       - <see cref="PortraitSwapMod"/> —— 头像替换
    ///     两者内部仍分处 <c>PilotCombined.GenderRatio</c> 与 <c>PilotCombined.Portrait</c>
    ///     两个子命名空间（各有一个同名的 <c>Patches</c> 类）。**这是刻意的**：
    ///     合并成一个命名空间需要把几十处 <c>Patches.</c> 引用改名，收益为零、风险不小。
    ///
    /// M1 = 两套代码合进同一个 DLL；M2 = 合并日志 / 配置 / Harmony 实例。
    ///
    /// ⚠️ 为什么必须是 **Game Mod**（mod.json）而不是 System Mod（systemMod.json）：
    /// 原版 ModLoader 的 InitSystemModsLoop 在"这个系统 mod 被禁用"分支里写错了表
    /// （写 loadedGameModStatus 而不是 loadedSystemModStatus）；系统 mod 在游戏 mod 表里
    /// 没有条目 → KeyNotFoundException → 被标 failedToLoad → 状态文件被污染 →
    /// 保存永远卡在"正在保存中"、模组界面打不开，且每次启动复现。
    /// 游戏 mod 的同一段逻辑写的是它自己那张表，不会崩。详见 docs 目录。
    /// </summary>
    public static class PilotCombinedMod
    {
        /// <summary>融合 MOD 自己的版本号（两个子系统的版本号仍各自保留在它们的类里）。</summary>
        internal const string Version = "0.2.0";

        /// <summary>融合后的**唯一** Harmony ID。两个子系统共用这一个实例。</summary>
        internal const string HarmonyId = "com.pilotcombined.mod";

        /// <summary>共享的 Harmony 实例；由 <see cref="Init"/> 创建，两个子系统只读它。</summary>
        internal static HarmonyInstance Harmony;

        /// <summary>mod.json 里的 "Name"。</summary>
        internal static string ModName = "PilotCombined";

        /// <summary>mod 所在目录名（通常与 Name 相同）。</summary>
        internal static string FolderName = "PilotCombined";

        /// <summary>mod 目录绝对路径（配置、日志、图片池都相对它）。</summary>
        internal static string ModDirectory = "";

        public static void Init(string modDir, string settingsJson)
        {
            if (string.IsNullOrEmpty(modDir)) modDir = SelfDirectory();
            modDir = Path.GetFullPath(modDir);
            ModDirectory = modDir;
            try { FolderName = new DirectoryInfo(modDir).Name; } catch { }

            // ModLoader 传进来的 settingsJson 实测拿不到内容（M1 真机日志：两个子系统都把
            // mod 名称退回了写死的旧值），所以这里自己按 **显式 UTF-8** 读一遍 mod.json：
            //   1) mod 名称 / 版本能正确读到；
            //   2) 顺带绕开"用系统 ANSI(GBK) 去读 UTF-8 文件"那条老坑
            //      —— 它会把中文变成乱码，严重时连 JSON 的引号都被弄坏。
            string settings = settingsJson;
            try
            {
                string modJsonPath = Path.Combine(modDir, "mod.json");
                if (File.Exists(modJsonPath))
                {
                    string text = File.ReadAllText(modJsonPath, new System.Text.UTF8Encoding(false));
                    if (!string.IsNullOrEmpty(text)) settings = text;
                }
            }
            catch (Exception e)
            {
                Debug_("[PilotCombined] 自己读 mod.json 失败，改用 ModLoader 传进来的文本：" + e.Message);
            }

            // ---------------- 全局唯一：日志文件 ----------------
            Log.Init(Path.Combine(modDir, "PilotCombined.log"),
                     "PilotCombined v" + Version + "（融合 MOD：性别刷出比例 + 头像替换），modDir=" + modDir
                     + "\r\n日志说明：本文件每次启动都会被清空重写；两个子系统的内容都写在这里。");
            Log.Info("入口启动：PilotCombined v" + Version + "，目录名=" + FolderName);

            // ---------------- 全局唯一：Harmony 实例 ----------------
            try
            {
                Harmony = HarmonyInstance.Create(HarmonyId);
                Log.Info("Harmony 实例已创建：" + HarmonyId + "（两个子系统共用；旧的两个 ID 已废弃）");
            }
            catch (Exception e)
            {
                Log.Error("创建 Harmony 实例失败，两个子系统的补丁都不会安装：" + e);
                return;
            }

            // ---------------- 配置与图片池：提前准备好 ----------------
            // 为什么在调用子系统**之前**做：
            //   * 配置只读一次（CombinedConfig.Load 幂等），两个子系统共用同一份；
            //   * 图片池的"各性别张数"是 M5「肖像池优先」的输入，必须按**真实配置**
            //     （autoScan / pool / portraits 目录）来扫。若等到头像子系统才设 Config，
            //     先跑起来的性别子系统就会用默认配置把池子建错，而且会被 _poolBuilt 缓存住。
            CombinedConfig.Load(modDir);
            PortraitRegistry.ModDirectory = modDir;
            PortraitRegistry.Config = CombinedConfig.Portraits;

            // ---------------- 界面语言：跟随游戏设置 ----------------
            // 放在配置之后、子系统之前：窗口和子系统日志都会用到它。
            // 判定以**游戏自己的语言设置**（Localize.Strings.CurrentCulture）为准；
            // 抽样译文只作诊断、并在"语言读不到"时兜底。
            // combined.json 的 popup.ui_language 可以强制指定（en/zh/de/ru），自动判定不对时的救命开关。
            Loc.UseDirectory(Path.Combine(modDir, "loc"));
            Loc.Detect(CombinedConfig.GenderRatio != null ? CombinedConfig.GenderRatio.uiLanguage : null);
            Log.Info("界面语言：" + Loc.Current + "（" + Loc.DetectionDetail + "）");

            // ---------------- 全局唯一：不把自己算作"存档必需 mod"（M3：只有这一份）----------------
            // 必须**无条件**安装（早于两个子系统的 enabled 检查），
            // 否则用户关掉功能后产生的存档仍会被标记成"必需本 mod"。
            SaveGuard.Install(Harmony);

            // ---------------- 两个子系统（各自 try/catch，互不连累）----------------
            try
            {
                GenderRatioMod.Init(modDir, settings);
            }
            catch (Exception e)
            {
                Log.Error("性别比例子系统 Init 抛异常（已隔离，头像替换仍会继续）：" + e);
                Debug_("[PilotCombined] 性别比例子系统 Init 失败：" + e);
            }

            try
            {
                PortraitSwapMod.Init(modDir, settings);
            }
            catch (Exception e)
            {
                Log.Error("头像替换子系统 Init 抛异常（已隔离）：" + e);
                Debug_("[PilotCombined] 头像替换子系统 Init 失败：" + e);
            }

            Log.Info("======== 入口 Init 结束 ========");
        }

        /// <summary>
        /// 走反射调 UnityEngine.Debug.LogError，这样万一环境里没有 UnityEngine 也不会炸。
        /// （日志本体在 <see cref="Log"/> 里，这里只用于"日志还没建起来之前"的兜底。）
        /// </summary>
        private static void Debug_(string msg)
        {
            try
            {
                Type dbg = Type.GetType("UnityEngine.Debug, UnityEngine.CoreModule");
                if (dbg == null) return;
                var m = dbg.GetMethod("LogError", new Type[] { typeof(object) });
                if (m != null) m.Invoke(null, new object[] { msg });
            }
            catch { }
        }

        private static string SelfDirectory()
        {
            try
            {
                string loc = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(loc)) return Path.GetDirectoryName(loc);
            }
            catch { }
            return AppDomain.CurrentDomain.BaseDirectory;
        }
    }
}
