using System;
using System.Reflection;
using BattleTech;
using Harmony;
using UnityEngine;

namespace PilotCombined.GenderRatio
{
    /// <summary>
    /// 游戏内 UI 的安装与热键。
    ///
    /// 挂点：GameInstance.Update(float) 的 Prefix。只做一次按键判断，代价可忽略。
    /// 用 Prefix 而不是自己 new GameObject + DontDestroyOnLoad 做热键监听，
    /// 是为了少一个常驻对象、少一份场景切换时的生命周期问题。
    ///
    /// 具体界面在 GenderRatioWindow 里；这里只管"什么时候弹/收"。
    /// </summary>
    internal static class GenderRatioUi
    {
        private static KeyCode _hotkey = KeyCode.F9;
        private static bool _hotkeyParsed;

        internal static bool TryInstall(HarmonyInstance harmony)
        {
            try
            {
                GenderRatioConfig cfg = Patches.Config;
                if (cfg != null && !cfg.popupEnabled)
                {
                    Log.Info("combined.json 里 popup.enabled=false：不安装弹窗热键（机制照常生效）。");
                    return true;
                }

                MethodInfo update = null;
                try { update = AccessTools.Method(typeof(GameInstance), "Update", new Type[] { typeof(float) }); } catch { }
                if (update == null)
                {
                    update = typeof(GameInstance).GetMethod("Update",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new Type[] { typeof(float) }, null);
                }
                if (update == null)
                {
                    Log.Error("找不到 GameInstance.Update(float)，弹窗热键无法安装。");
                    return false;
                }

                bool ok = GenderRatioMod.Patch(harmony, update,
                    AccessTools.Method(typeof(GenderRatioUi), "Update_Prefix"), null);
                if (ok)
                    Log.Info("弹窗热键已安装（默认 F9，可在 combined.json 的 popup.hotkey 改）。");
                return ok;
            }
            catch (Exception e)
            {
                Log.Error("安装弹窗热键失败：" + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 注意：这是每帧都会跑的 Prefix，所以第一句就做最便宜的判断。
        /// 任何异常都吞掉，绝不让 UI 问题影响到游戏本体。
        /// </summary>
        internal static void Update_Prefix(GameInstance __instance)
        {
            try
            {
                GenderRatioConfig cfg = Patches.Config;
                if (cfg == null || !cfg.enabled || !cfg.popupEnabled) return;

                // 预制体还在异步加载：每帧轮询一次（只在窗口开着时才真正做事）
                GenderRatioWindow.Poll();

                if (!_hotkeyParsed)
                {
                    _hotkeyParsed = true;
                    try
                    {
                        _hotkey = (KeyCode)Enum.Parse(typeof(KeyCode), cfg.hotkey, true);
                    }
                    catch
                    {
                        Log.Warn("热键名 '" + cfg.hotkey + "' 不是合法的 KeyCode，回退为 F9。");
                        _hotkey = KeyCode.F9;
                    }
                }

                if (Input.GetKeyDown(_hotkey))
                {
                    GenderRatioWindow.Toggle(__instance);
                }
            }
            catch (Exception e)
            {
                Log.Warn("热键检查异常（已忽略，不影响游戏）：" + e.Message);
            }
        }

        // ================================================================ 供窗口调用

        /// <summary>设置女性百分比并即时生效（写进配置对象，下一次驾驶员生成就按新值抽取）。</summary>
        internal static void SetFemalePercent(float value, string source)
        {
            GenderRatioConfig cfg = Patches.Config;
            if (cfg == null) return;
            GenderRatioSettings s = cfg.settings;
            s.femalePercent = GenderRatioSettings.Snap(
                GenderRatioSettings.Clamp(value, 0f, s.FemaleSliderMax), cfg.stepPercent);
            ApplyAndLog(source);
        }

        /// <summary>开关「其他性别」并即时生效。</summary>
        internal static void SetOtherEnabled(bool enabled, string source)
        {
            GenderRatioConfig cfg = Patches.Config;
            if (cfg == null) return;
            GenderRatioSettings s = cfg.settings;
            s.otherEnabled = enabled;
            if (enabled && s.otherPercent <= 0f) s.otherPercent = cfg.stepPercent;
            s.Normalize(cfg.stepPercent);
            s.femalePercent = GenderRatioSettings.Clamp(s.femalePercent, 0f, s.FemaleSliderMax);
            ApplyAndLog(source);
        }

        internal static void SetOtherPercent(float value, string source)
        {
            GenderRatioConfig cfg = Patches.Config;
            if (cfg == null) return;
            GenderRatioSettings s = cfg.settings;
            s.otherPercent = GenderRatioSettings.Snap(
                GenderRatioSettings.Clamp(value, 0f, 100f - cfg.stepPercent), cfg.stepPercent);
            if (s.otherPercent > 0f) s.otherEnabled = true;
            s.Normalize(cfg.stepPercent);
            s.femalePercent = GenderRatioSettings.Clamp(s.femalePercent, 0f, s.FemaleSliderMax);
            ApplyAndLog(source);
        }

        /// <summary>
        /// 打一行可验证的事实（交接文档 §4.12：不要靠"看起来对"）。
        /// 真正的下发在 Patches.GetNameAndGender_Prefix 里做，而且那边是幂等的 ——
        /// 所以这里改完值，下一位新生成的驾驶员就会按新比例抽取，不需要重开游戏。
        /// </summary>
        private static void ApplyAndLog(string source)
        {
            GenderRatioConfig cfg = Patches.Config;
            GenderRatioSettings s = cfg.settings;
            s.Normalize(cfg.stepPercent);
            int[] w = s.Weights();
            Log.Info("[" + source + "] 比例已更新为 " + s.Describe(cfg.femaleLabel, cfg.maleLabel)
                     + "（权重 女 " + w[0] + " / 男 " + w[1] + " / " + s.otherLabel + " " + w[2] + "）"
                     + " —— 下一位新生成的驾驶员就会按新比例抽取。");
        }
    }
}
