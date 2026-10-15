using System;
using System.Collections.Generic;
using System.Reflection;
using Harmony;

namespace PilotCombined
{
    /// <summary>
    /// **不把本 mod 算作"存档必需 mod"** 的那一道防线（M3：合并成一份）。
    ///
    /// 背景：`ModLoader.GetSaveAffectingModDefs()` 是 public static `List&lt;string&gt;`，
    /// `GameInstanceSave..ctor` 用它决定"这个存档需要哪些 mod"，读档时 `AreNecessaryModsInstalled`
    /// 也用它算当前集合。两边取值一致，所以在 Postfix 里把自己摘掉不会自相矛盾。
    ///
    /// ⚠️ 必须在建 Harmony 实例之后**无条件**安装（早于两个子系统的 `enabled` 检查）：
    /// 否则用户在配置里关掉功能之后产生的存档，仍会被标记成"必需本 mod"。
    /// 这就是为什么它放在入口里调用，而不是塞进某个子系统。
    ///
    /// M3 之前：两个子系统**各自**挂了一个一模一样的 Postfix（同一方法被挂两次，都能跑但纯属重复）。
    /// M3 之后：只有这一份。
    /// </summary>
    internal static class SaveGuard
    {
        internal static bool Installed;

        private static bool _logged;

        internal static void Install(HarmonyInstance harmony)
        {
            if (harmony == null)
            {
                Log.Error("『不锁定存档』补丁无法安装：Harmony 实例为空。");
                return;
            }

            try
            {
                // 用 TypeByName 而不是编译期引用 BattleTech.ModSupport.ModLoader：
                // 少一个编译期依赖，将来类型搬位置也只是这里找不到、不会连累整个 DLL 加载。
                Type modLoader = AccessTools.TypeByName("BattleTech.ModSupport.ModLoader");
                if (modLoader == null)
                {
                    Log.Error("找不到 BattleTech.ModSupport.ModLoader，无法安装『不锁定存档』补丁。");
                    return;
                }

                MethodInfo target = AccessTools.Method(modLoader, "GetSaveAffectingModDefs");
                if (target == null)
                {
                    Log.Error("找不到 ModLoader.GetSaveAffectingModDefs，无法安装『不锁定存档』补丁。");
                    return;
                }

                HarmonyMethod post = new HarmonyMethod(
                    AccessTools.Method(typeof(SaveGuard), "GetSaveAffectingModDefs_Postfix"));
                harmony.Patch(target, null, post, null);

                Installed = true;
                Log.Info("已修补 " + target.DeclaringType.FullName + "." + target.Name + "  ->  "
                         + target.ToString() + "  [Postfix]"
                         + "   （作用：本 mod 永远不会被写进存档的『必需 mod』名单）");
            }
            catch (Exception e)
            {
                Log.Error("安装『不锁定存档』补丁失败（只影响存档标记）：" + e.Message);
            }
        }

        /// <summary>
        /// 只声明 <c>ref __result</c>，不声明 <c>__instance</c> —— 这样静态/实例方法都能匹配。
        /// （Harmony 1.x 按**参数名**匹配。）
        /// </summary>
        internal static void GetSaveAffectingModDefs_Postfix(ref List<string> __result)
        {
            try
            {
                if (__result == null) return;
                int removed = __result.RemoveAll(IsSelfName);
                if (!_logged)
                {
                    _logged = true;
                    Log.Info("存档所需 mod 名单（首次调用时）：[" + string.Join(", ", __result.ToArray()) + "]"
                             + (removed > 0
                                ? " —— 已摘除本 mod，此后产生的存档不再要求本 mod"
                                : " —— 本 mod 本来就不在名单里（mod.json 的 IsSaveAffecting=false 生效了）"));
                }
            }
            catch (Exception e)
            {
                Log.Warn("GetSaveAffectingModDefs_Postfix 失败（只影响存档标记）：" + e.Message);
            }
        }

        /// <summary>本 mod 的各种叫法（Name / 目录名 / 兜底硬编码名），任一命中即视为自己。</summary>
        private static bool IsSelfName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.Trim();
            return n.Equals(PilotCombinedMod.ModName, StringComparison.OrdinalIgnoreCase)
                || n.Equals(PilotCombinedMod.FolderName, StringComparison.OrdinalIgnoreCase)
                || n.Equals("PilotCombined", StringComparison.OrdinalIgnoreCase);
        }
    }
}
