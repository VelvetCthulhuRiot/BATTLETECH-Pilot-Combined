using System;
using System.Collections.Generic;
using System.Reflection;
using BattleTech;

namespace PilotCombined.GenderRatio
{
    /// <summary>
    /// 实际下钩子的地方。两个补丁：
    ///
    ///   1) PilotGenerator.GetNameAndGender(ref string, ref string, out Gender) 的 Prefix
    ///      —— 唯一的性别产出点。IL 实证（交接文档 §1.2）：
    ///         if (GenderWeights == null) {
    ///             GenderWeights = new int[3];
    ///             GenderWeights[0] = Sim.Constants.Pilot.FemaleGenerationWeight;
    ///             GenderWeights[1] = Sim.Constants.Pilot.MaleGenerationWeight;
    ///             GenderWeights[2] = Sim.Constants.Pilot.NonBinaryGenerationWeight;
    ///         }
    ///         gender = Genders[Sim.GetWeightedResult(Sim.NetworkRandom.Float(), GenderWeights)];
    ///
    ///      所以改那三个 int 常量 + 覆写已建好的 GenderWeights[0..2] = 全局生效，
    ///      而且**不需要**知道数组索引和性别的对应关系之外的任何东西（顺序已确证：女/男/其他）。
    ///
    ///   2) ModLoader.GetSaveAffectingModDefs() 的 Postfix —— 把本 mod 从"存档必需 mod"
    ///      名单里摘掉（交接文档 §4.4）。必须**无条件**注册，放在 enabled 检查之前，
    ///      否则用户关掉功能后产生的存档仍会被标记成"必需本 mod"。
    ///
    /// Prefix 只声明 __instance：不碰 ref/out 参数，所以不可能破坏原方法语义；
    /// 任何异常都吞掉并返回 true（放行原逻辑），最坏情况只是比例没生效。
    /// </summary>
    internal static class Patches
    {
        /// <summary>当前生效的配置（由 GenderRatioMod.Init 注入）。</summary>
        internal static GenderRatioConfig Config;

        // ---------------------------------------------------------------- 反射缓存
        // 只做一次查找，之后每次驾驶员生成直接读字段，开销可忽略。

        private static bool _reflectionReady;
        private static bool _reflectionFailed;
        private static FieldInfo _simField;             // PilotGenerator.Sim            (private)
        private static PropertyInfo _constantsProp;     // SimGameState.Constants        (public)
        private static PropertyInfo _pilotProp;         // SimGameConstants.Pilot        (public)
        private static FieldInfo _femaleField;          // .FemaleGenerationWeight       (public int)
        private static FieldInfo _maleField;            // .MaleGenerationWeight
        private static FieldInfo _otherField;           // .NonBinaryGenerationWeight
        private static FieldInfo _weightsField;         // PilotGenerator.GenderWeights  (private int[])

        private static string _lastSignature;
        private static int _applyCount;
        private static bool _liveTypeWarned;
        private static bool _liveOverwriteConfirmed;

        /// <summary>M7 验证用：已经打过日志的调用来源（每种只打一次，最多 <see cref="MaxLoggedCallers"/> 种）。</summary>
        private static readonly System.Collections.Generic.HashSet<string> _loggedCallers =
            new System.Collections.Generic.HashSet<string>();
        private const int MaxLoggedCallers = 8;

        /// <summary>
        /// 打一行"GetNameAndGender 是被谁调用的"。成本只在**前几次**调用上（之后靠哈希集早退），
        /// 拿到 8 种调用来源后彻底不再做栈回溯。
        /// </summary>
        private static void LogCallerOnce()
        {
            if (_loggedCallers.Count >= MaxLoggedCallers) return;
            try
            {
                System.Diagnostics.StackTrace st = new System.Diagnostics.StackTrace(1, false);
                string top = "";
                for (int i = 0; i < st.FrameCount && i < 3; i++)
                {
                    System.Reflection.MethodBase m = st.GetFrame(i) == null ? null : st.GetFrame(i).GetMethod();
                    if (m == null) continue;
                    string name = (m.DeclaringType != null ? m.DeclaringType.Name + "." : "") + m.Name;
                    top += (top.Length > 0 ? " ← " : "") + name;
                }
                if (top.Length > 0 && _loggedCallers.Add(top))
                {
                    Log.Info("[M7 调用来源] 性别收口 GetNameAndGender ← " + top);
                }
            }
            catch { }
        }

        /// <summary>把上一段 apply 的结果记下来，供启动自检 / 日志用。</summary>
        internal static string LastApplyMessage = "(尚未触发过驾驶员生成)";

        // ================================================================ 1) 性别比例

        internal static void GetNameAndGender_Prefix(PilotGenerator __instance)
        {
            try
            {
                // ★ M7 验证用：把"谁在调用 GetNameAndGender"打出来（每个不同调用者只打一次，最多 8 条）。
                //   目的：证明这条收口只被"随机生成驾驶员"走到 ——
                //   玩家指挥官的性别来自创建界面的代词选择（SGCharacterCreationNamePanel.gender）、
                //   战役固定 4 人的性别写死在各自的 pilot JSON 里，两者都**不经过这里**，
                //   所以"性别调节不作用于他们"是**结构性**保证，不是靠运行时判断。
                LogCallerOnce();

                if (__instance == null) return;

                GenderRatioConfig cfg = Config;
                if (cfg == null || !cfg.enabled) return;

                GenderRatioSettings s = cfg.settings;
                if (s == null) return;

                // 纯逻辑部分先算出来（这一段有脱机单测覆盖）
                //
                // ★ M5：「肖像池优先」（文档 6.2）—— 肖像池决定"有哪些性别可用"，
                //   配置里的比例只在这个可用空间内分配。核心规则整个在 Mechanic.ResolveFinalWeights 里，
                //   这里只负责把"当前有哪些性别有图"喂进去（读的是缓存好的计数，不重扫目录）。
                bool portraitPriority = Portrait.PortraitRegistry.PortraitPriorityActive();
                int[] w = s.ResolveFinalWeights(
                    portraitPriority,
                    s.otherEnabled,
                    Portrait.PortraitRegistry.CountOfGender(Gender.Female) > 0,
                    Portrait.PortraitRegistry.CountOfGender(Gender.Male) > 0,
                    Portrait.PortraitRegistry.CountOfGender(Gender.NonBinary) > 0);
                int total = w[0] + w[1] + w[2];
                if (total <= 0)
                {
                    if (_lastSignature != "invalid")
                    {
                        _lastSignature = "invalid";
                        Log.Warn("三个权重相加为 0，保持游戏原值不动。");
                    }
                    return;
                }

                if (!EnsureReflection(__instance)) return;

                // --- 常量（懒加载的源头）---
                object constants = _constantsProp.GetValue(GetSim(__instance), null);
                if (constants == null) return;
                object pilot = _pilotProp.GetValue(constants, null);
                if (pilot == null) return;

                // --- 已建好的权重表（即时生效的关键，交接文档 §1.4）---
                // ★ M1 真机日志实测更正：PilotGenerator.GenderWeights 的真实类型是
                //   System.Collections.Generic.List<int>，**不是** int[]（交接文档 §12 写错了）。
                //   旧代码按 int[] 强转，于是游戏把这个表建出来之后，每次调用都抛
                //   InvalidCastException（日志里刷 "Specified cast is not valid"），
                //   "改比例立即生效"就悄悄失效了 —— 只有当表还是 null 的那一次才真正生效。
                object live = _weightsField == null ? null : _weightsField.GetValue(__instance);

                // 签名把**最终权重**也算进去：这样"配置没变但图片池变了"（例如用户加了男性图）
                // 也会重新打一行可验证的日志，而不是静默换权重。
                string sig = s.Signature() + "|w" + w[0] + "," + w[1] + "," + w[2];
                bool changed = _lastSignature != sig;

                bool wrote = false;
                wrote |= SetInt(_femaleField, pilot, w[0]);
                wrote |= SetInt(_maleField, pilot, w[1]);
                wrote |= SetInt(_otherField, pilot, w[2]);

                if (live != null)
                {
                    bool recognized;
                    int liveCount;
                    if (OverwriteLive(live, w, out recognized, out liveCount)) wrote = true;

                    // 注意：这里必须把"类型不认识"和"值本来就相等"分开。
                    // OverwriteLive 返回 false 有两种含义，混在一起会打出误报
                    // （M1.1 就踩过：表是照着刚下发的常量建的，第二次调用值相同 → 误报"类型不认识"）。
                    if (!recognized)
                    {
                        if (!_liveTypeWarned)
                        {
                            _liveTypeWarned = true;
                            Log.Warn("GenderWeights 的实际类型是 " + live.GetType().FullName
                                     + "（既不是 List<int> 也不是 int[]），无法就地覆写，只能改常量；"
                                     + "要等这个表被重建（读档 / 开新游戏）比例才会完全生效。");
                        }
                    }
                    else if (liveCount < 3)
                    {
                        if (!_liveTypeWarned)
                        {
                            _liveTypeWarned = true;
                            Log.Warn("GenderWeights 只有 " + liveCount + " 项（期望 3），无法覆写，只能改常量。");
                        }
                    }
                    else if (!_liveOverwriteConfirmed)
                    {
                        _liveOverwriteConfirmed = true;
                        Log.Info("已能就地覆写 GenderWeights（实际类型 " + live.GetType().Name + "，"
                                 + liveCount + " 项）—— 之后在 F9 里改比例，下一位新生成的驾驶员立即生效。");
                    }
                }

                if (changed)
                {
                    _lastSignature = sig;
                    _applyCount++;
                    string msg = "性别刷出比例已生效（最终权重）：女 " + w[0] + " / 男 " + w[1] + " / " + s.otherLabel + " " + w[2]
                               + (portraitPriority ? "   ★ 已按『肖像池优先』裁剪：只生成有替换头像的性别" : "")
                               + "  ——  combined.json 里的配置值：" + s.Describe(cfg.femaleLabel, cfg.maleLabel)
                               + "（" + (_applyCount == 1 ? "首次下发" : "第 " + _applyCount + " 次下发") + "）";
                    if (!wrote && live == null)
                    {
                        // 常量本来就等于目标值、且权重表还没建：说明是启动时的"已经是这个值"，不用重复刷屏
                        msg += " [常量本就等于目标值，权重表尚未构建，等原方法自行构建]";
                    }
                    LastApplyMessage = msg;
                    Log.Info(msg);
                    Log.Info("范围说明：只影响**新生成**的驾驶员（招募市场、生涯模式开局队友、合约里的敌方驾驶员）；"
                             + "存档里已有的驾驶员、以及战役模式的具名剧情角色（Glitch/Behemoth/Dekker/Medusa）不受影响。");
                }
            }
            catch (Exception e)
            {
                Log.Warn("设置性别刷出比例失败（不影响其它功能）：" + e.GetType().Name + " " + e.Message);
            }
        }

        /// <summary>
        /// 把**已建好的**性别权重表就地改成 w[0..2]。
        /// 实测（真机反射）：游戏用的是 <c>List&lt;int&gt;</c>；<c>int[]</c> 作为兜底一起支持，
        /// 免得换游戏版本/平台时又踩同一个坑。
        ///
        /// 返回 true 表示值确实变了（不是本来就相等）；
        /// <paramref name="recognized"/> 表示"类型认识"（与"值没变"是两件事，调用方必须分开判断）；
        /// <paramref name="count"/> 是实际项数。
        /// </summary>
        private static bool OverwriteLive(object live, int[] w, out bool recognized, out int count)
        {
            recognized = false;
            count = 0;

            int[] arr = live as int[];
            if (arr != null)
            {
                recognized = true;
                count = arr.Length;
                if (arr.Length < 3) return false;
                bool diff = arr[0] != w[0] || arr[1] != w[1] || arr[2] != w[2];
                arr[0] = w[0]; arr[1] = w[1]; arr[2] = w[2];
                return diff;
            }

            System.Collections.Generic.IList<int> list =
                live as System.Collections.Generic.IList<int>;
            if (list != null)
            {
                recognized = true;
                count = list.Count;
                if (list.Count < 3) return false;
                bool diff = list[0] != w[0] || list[1] != w[1] || list[2] != w[2];
                list[0] = w[0]; list[1] = w[1]; list[2] = w[2];
                return diff;
            }
            return false;
        }

        /// <summary>游戏若在别处新建 PilotGenerator，_weightsField 仍是同一份 Type 上的字段，无需重建。</summary>
        private static object GetSim(PilotGenerator gen)
        {
            return _simField == null ? null : _simField.GetValue(gen);
        }

        private static bool EnsureReflection(PilotGenerator gen)
        {
            if (_reflectionReady) return true;
            if (_reflectionFailed) return false;

            try
            {
                Type genType = typeof(PilotGenerator);
                _simField = genType.GetField("Sim", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _weightsField = genType.GetField("GenderWeights", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                _constantsProp = ResolveMember(typeof(SimGameState), "Constants") as PropertyInfo;
                if (_constantsProp == null)
                {
                    Log.Error("找不到 SimGameState.Constants 属性，无法下发性别比例。");
                    _reflectionFailed = true;
                    return false;
                }

                _pilotProp = ResolveMember(typeof(SimGameConstants), "Pilot") as PropertyInfo;
                if (_pilotProp == null)
                {
                    Log.Error("找不到 SimGameConstants.Pilot 属性，无法下发性别比例。");
                    _reflectionFailed = true;
                    return false;
                }

                // 三个权重字段的类型就是 int（已用 IL/反射确认），但这里仍按 Type 查，不写死
                Type pilotConstType = _pilotProp.PropertyType;
                _femaleField = pilotConstType.GetField("FemaleGenerationWeight", MemberFlags);
                _maleField = pilotConstType.GetField("MaleGenerationWeight", MemberFlags);
                _otherField = pilotConstType.GetField("NonBinaryGenerationWeight", MemberFlags);

                if (_femaleField == null || _maleField == null || _otherField == null)
                {
                    Log.Error("找不到 " + pilotConstType.Name + " 上的三个 GenerationWeight 字段，无法下发性别比例。"
                              + "（女=" + (_femaleField != null) + " 男=" + (_maleField != null) + " 其他=" + (_otherField != null) + "）");
                    _reflectionFailed = true;
                    return false;
                }

                if (_simField == null) Log.Warn("找不到 PilotGenerator.Sim 字段（将只能改常量，无法即时覆写权重表）。");
                if (_weightsField == null) Log.Warn("找不到 PilotGenerator.GenderWeights 字段（读档后需要重新生成驾驶员才会生效）。");

                _reflectionReady = true;
                Log.Info("反射准备完成：" + pilotConstType.FullName + " 上的 "
                         + _femaleField.Name + " / " + _maleField.Name + " / " + _otherField.Name
                         + (liveWeightsAvailable()
                            ? "（可直接覆写 GenderWeights（实测类型 " + _weightsField.FieldType.Name + "），改动即时生效）"
                            : "（无 GenderWeights，读档后生效）"));
                return true;
            }
            catch (Exception e)
            {
                Log.Error("反射准备失败：" + e.Message);
                _reflectionFailed = true;
                return false;
            }
        }

        private static bool liveWeightsAvailable()
        {
            return _weightsField != null;
        }

        private const BindingFlags MemberFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>属性优先、字段兜底（交接文档 §4.8 的通用写法）。</summary>
        private static object ResolveMember(Type type, string name)
        {
            PropertyInfo pi = type.GetProperty(name, MemberFlags | BindingFlags.Static);
            if (pi != null) return pi;
            return null;
        }

        private static bool SetInt(FieldInfo fi, object target, int value)
        {
            if (fi == null) return false;
            try
            {
                object cur = fi.GetValue(target);
                int now = cur == null ? int.MinValue : Convert.ToInt32(cur);
                if (now == value) return false;            // 幂等：值相同就不写
                fi.SetValue(target, Convert.ChangeType(value, fi.FieldType));
                return true;
            }
            catch (Exception e)
            {
                Log.Warn("写入 " + fi.Name + " 失败：" + e.Message);
                return false;
            }
        }

        // ================================================================ 2) 不把自己算作"存档必需 mod"
        //
        // ★ M3 起这段挪到 PilotCombined.SaveGuard —— 两个子系统原来各挂了一个一模一样的
        //   Postfix（同一方法被挂两次，都能跑但纯属重复）。这里不再保留任何副本。
    }
}
