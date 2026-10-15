using System;
using System.IO;
using System.Reflection;
using BattleTech;
using BattleTech.Data;
using BattleTech.Portraits;
using Harmony;
using Newtonsoft.Json.Linq;

namespace PilotCombined.Portrait
{
    /// <summary>
    /// 入口。本 mod 是 **Game Mod**（mod.json），不是 System Mod —— 这一点很关键：
    ///
    /// 原版 ModLoader 的 InitSystemModsLoop 在"这个系统 mod 被禁用"分支里错写成
    /// loadedGameModStatus[name].enabled = false（应该写 loadedSystemModStatus），
    /// 系统 mod 在游戏 mod 表里没有条目 → KeyNotFoundException → 被标 failedToLoad
    /// → UpdateModStatus 把它注入两个状态文件 → GetCombinedModStatus() 撞重复键
    /// → 保存卡死 + 模组界面废掉，且每次启动复现。
    ///
    /// InitGameModsLoop 同一位置写的就是它自己那张表，不会崩。
    /// 已核实 LoadGameMod 同样会调用 LoadAssemblyAndCallInit，所以游戏 mod 一样能带 DLL。
    /// ⇒ 做成 Game Mod，就能安全地在游戏内启用/禁用。
    /// </summary>
    public static class PortraitSwapMod
    {
        internal const string Version = "0.4.1";
        internal const string HarmonyId = "com.btportraitmod.portraitswap";

        /// <summary>mod.json 里的 "Name"。</summary>
        internal static string ModName = "PortraitSwap";
        /// <summary>mod 所在目录名（通常与 Name 相同）。</summary>
        internal static string FolderName = "PortraitSwap";

        public static void Init(string modDir, string settingsJson)
        {
            try
            {
                if (string.IsNullOrEmpty(modDir)) modDir = SelfDirectory();
                modDir = Path.GetFullPath(modDir);
                PortraitRegistry.ModDirectory = modDir;
                try { FolderName = new DirectoryInfo(modDir).Name; } catch { }
                string parsed = ExtractName(settingsJson);
                if (!string.IsNullOrEmpty(parsed)) ModName = parsed;

                Log.Section("头像替换子系统 v" + Version + "（原 PortraitSwap）");
                Log.Info("modDir=" + modDir);
                Log.Info("mod 名称=" + ModName + "，目录名=" + FolderName);

                HarmonyInstance harmony = PilotCombinedMod.Harmony;
                if (harmony == null)
                {
                    Log.Error("共享的 Harmony 实例还没创建（入口调用顺序不对），头像替换补丁无法安装。");
                    return;
                }
                int ok = 0, fail = 0;

                // 0) 「不把自己算作存档必需 mod」那道防线**已经由入口统一安装**
                //    （PilotCombined.SaveGuard，M3 合并成一份），这里不再重复挂。

                // 配置：融合后只有 combined.json 一个文件（M2），由 CombinedConfig 统一读取/迁移。
                // 入口其实已经读过并设好了（为了把计数早点准备好），这里再设一次是幂等的兜底。
                CombinedConfig.Load(modDir);
                PortraitRegistry.Config = CombinedConfig.Portraits;

                // 图片池张数（一眼可见"每个性别有几张、目录名有没有读对"）
                Log.Info("图片池张数：" + PortraitRegistry.PoolSummary());

                // ★ M6（文档 6.4）：一张肖像都没有 ⇒ **强制**把替换关掉，并**写回配置**。
                //   护栏的意义：避免用户在没有任何图的情况下开着替换，结果所有驾驶员都变成
                //   原版/空白头像，还以为 mod 坏了。关掉之后本 mod 只剩性别比例调节，
                //   行为完全等同于旧的 GenderRatio。
                if (PortraitRegistry.Config.enabled && !PortraitRegistry.HasAnyPortrait())
                {
                    Log.Warn("肖像池为空（" + PortraitRegistry.PoolSummary() + "）→ 头像替换已**强制关闭**，"
                             + "本次只启用性别比例调节。");
                    Log.Warn("想恢复：把图片放进 portraits\\female（女）/ portraits\\male（男）/ "
                             + "portraits\\other（非二元性别·无性别·机器人），"
                             + "然后重启游戏（或按 F9 用『立即重扫图片池』）。"
                             + "已经帮你把 combined.json 里的 portraits.enabled 改成 false，"
                             + "免得你以为是配置没生效。");
                    PortraitRegistry.Config.enabled = false;
                    CombinedConfig.SavePortraitsEnabled(false);
                }

                if (!PortraitRegistry.Config.enabled)
                {
                    Log.Warn("combined.json 的 portraits.enabled=false：不替换任何头像"
                             + "（补丁仍然会安装，但每个入口第一句就放行 ⇒ 行为与游戏原版完全一致）。"
                             + "这样你在 F9 窗口里把『启用头像替换』勾上就能**立即生效、不用重启**。"
                             + "此时性别比例调节独立运作，不受图片有无的影响。");
                }

                // 1) 3D 渲染的唯一收口
                if (Patch(harmony, AccessTools.Method(typeof(PortraitManager), "GeneratePortraitTexture"),
                          AccessTools.Method(typeof(Patches), "GeneratePortraitTexture_Prefix"), null))
                    ok++; else fail++;

                // 1.5) 角色创建的「角色头像」列表按所选代词过滤（用户决策 A）
                //      原版列表不看性别，会把女性/非二元的预设混在一起（换成替换头像后很扎眼）。
                //      只在"头像替换开着"时才做 —— 关掉替换就完全走原版行为。
                //      它是一个**改动原版 UI 行为**的功能，所以留了 combined.json 的开关
                //      portraits.filterGalleryByPronoun（false = 完全回到原版混合列表）。
                if (!PortraitRegistry.Config.filterGalleryByPronoun)
                {
                    Log.Info("combined.json 的 portraits.filterGalleryByPronoun=false："
                             + "不做『角色头像列表按代词过滤』（保持原版混合列表）。");
                }
                else if (PortraitGalleryFilter.TryInstall(harmony)) ok++; else fail++;

                // 2) 固定头像驾驶员
                if (Patch(harmony, ResolveMethod(typeof(PilotDef), "GetPortraitSprite", typeof(DataManager)),
                          null, AccessTools.Method(typeof(Patches), "GetPortraitSprite_Postfix")))
                    ok++; else fail++;

                if (Patch(harmony, ResolveMethod(typeof(PilotDef), "GetPortraitSpriteThumb", typeof(DataManager)),
                          null, AccessTools.Method(typeof(Patches), "GetPortraitSpriteThumb_Postfix")))
                    ok++; else fail++;

                // 3) 只读关联（Harmony 1.x 没有 AccessTools.PropertyGetter，用 Property + GetGetMethod）
                PropertyInfo portraitSettingsProp = AccessTools.Property(typeof(PilotDef), "PortraitSettings");
                MethodInfo portraitSettingsGetter = portraitSettingsProp == null ? null : portraitSettingsProp.GetGetMethod(true);
                if (Patch(harmony, portraitSettingsGetter,
                          null, AccessTools.Method(typeof(Patches), "GetPortraitSettings_Postfix")))
                    ok++; else fail++;

                // 4) 可选：隐藏角色创建界面的 3D 调脸按钮
                if (PortraitRegistry.Config.neutralize3DCustomization)
                {
                    Type screen = AccessTools.TypeByName("BattleTech.UI.SGCharacterCreationNameAndAppearanceScreen");
                    if (screen != null)
                    {
                        Patches.CustomizeButtonField = AccessTools.Field(screen, "portraitCustomizationButton");
                        if (Patch(harmony, AccessTools.Method(screen, "OnAddedToHierarchy"),
                                  null, AccessTools.Method(typeof(Patches), "OnAddedToHierarchy_Postfix")))
                            ok++; else fail++;
                    }
                    else
                    {
                        Log.Warn("找不到角色创建界面类型，跳过『隐藏自定义外貌按钮』。");
                        fail++;
                    }
                }

                Log.Info("补丁完成：成功 " + ok + " 个，失败 " + fail + " 个。");
                Log.Info("规则 " + PortraitRegistry.Config.rules.Count
                         + " 条；图片池：" + (PortraitRegistry.Config.pool.Count > 0 ? "配置里列了 "
                             + PortraitRegistry.Config.pool.Count + " 张" : "启动后自动扫描（见下方日志）")
                         + "，poolMode=" + PortraitRegistry.Config.poolMode);
            }
            catch (Exception e)
            {
                Log.Error("Init 失败：" + e);
            }
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

        /// <summary>
        /// 先按参数类型精确查找，失败再按名字兜底（防止命名空间/重载带来的解析差异）。
        /// </summary>
        private static MethodInfo ResolveMethod(Type type, string name, Type paramType)
        {
            MethodInfo m = null;
            try { m = AccessTools.Method(type, name, new Type[] { paramType }); } catch { }
            if (m == null)
            {
                Log.Warn("按参数类型没找到 " + type.Name + "." + name + "(" + paramType.Name + ")，改用按名字查找。");
                try { m = AccessTools.Method(type, name); } catch (Exception e) { Log.Error("按名字查找也失败：" + e.Message); }
            }
            return m;
        }

        private static bool Patch(HarmonyInstance harmony, MethodInfo original, MethodInfo prefix, MethodInfo postfix)
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
                         + "  ->  " + original.ToString());
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
