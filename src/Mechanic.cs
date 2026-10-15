using System;
using System.Globalization;

namespace PilotCombined.GenderRatio
{
    /// <summary>
    /// 一份「性别刷出比例」配置的规范化快照。
    ///
    /// 内部只保存**三个百分比**（女 / 男 / 其他），和恒为 100 —— 与游戏原版的 46/44/10
    /// 这种"任意和"权重不同，用百分比更直观，加分类时也不容易算错。
    ///
    /// 两个滑块的操作语义（v0.2.5 修正）：
    ///   * 「其他性别」滑块 → 改 otherPercent；
    ///   * 「女 / 男」滑块   → 在**女+男**这段里移动：滑块读数 x 表示女性占 (女+男) 的百分比，
    ///                        即 female = (女+男) * x/100，male = (女+男) - female。
    ///     ⇒ 女性的**绝对**占比 = 滑块读数% × (女+男)。例如 女30/男60（滑块≈33%）时拖动它，
    ///       女性绝对占比会跟着变，这是符合直觉的。
    ///   * 「锁定男女比例」勾上后，改「其他性别」时按**女:男 的比值**等比缩放
    ///     （而不是像 v0.2.4 那样把女性的绝对数值钉死 —— 那是个 bug）。
    ///
    /// 加新分类（例如以后加"机器人"）时，把 otherPercent 换成数组即可，其余不变。
    /// </summary>
    public class GenderRatioSettings
    {
        /// <summary>是否启用「其他性别」。false 时 otherPercent 恒为 0。</summary>
        public bool otherEnabled;

        /// <summary>「其他性别」占总体的百分比，0-100。</summary>
        public float otherPercent;

        /// <summary>女性占总体的百分比，0-100。</summary>
        public float femalePercent;

        /// <summary>男性占总体的百分比，0-100。</summary>
        public float malePercent;

        /// <summary>
        /// 第三类性别的**默认显示名**。机器人属于"无性别"，所以人类男女之外的一切都归到这一类。
        /// 只有一个地方定义，迁移旧配置时也用它做"旧默认名自动升级"的判断。
        /// </summary>
        public const string DefaultOtherLabel = "非二元性别或无性别";

        /// <summary>旧版本（v0.2.x 早期）用的默认显示名；迁移时会被升级成 <see cref="DefaultOtherLabel"/>。</summary>
        public const string LegacyOtherLabel = "其他性别";

        /// <summary>
        /// 分类标签（仅用于 UI / 日志显示）。可在 combined.json 的 gender_ratio.other_label 改。
        /// </summary>
        public string otherLabel = DefaultOtherLabel;

        // ---- 锁定男女比例（v0.2.14）----
        // 比例用**整数精确存储**，绝不从滑块读数反推（那会漂移，见 Ratio 的注释）。
        public bool lockRatio;
        public int lockRatioW = 1;
        public int lockRatioM = 1;

        public GenderRatioSettings() { }

        public GenderRatioSettings(bool otherEnabled, float otherPercent, float femalePercent, float malePercent)
        {
            this.otherEnabled = otherEnabled;
            this.otherPercent = otherPercent;
            this.femalePercent = femalePercent;
            this.malePercent = malePercent;
        }

        /// <summary>当前锁定的比例（整数对，已约分）。</summary>
        public Ratio LockedRatio { get { return new Ratio(lockRatioW, lockRatioM); } }

        /// <summary>按"女:男 = w:m"重排当前的百分比（在 (100-其他) 的总量里取最接近的整数百分比）。</summary>
        public void ApplyLockedRatio(float step)
        {
            Ratio r = LockedRatio;
            // 注意：这里的 pair 是"女+男 的总量"，女/男 要分的就是这一份
            float pair = 100f - (otherEnabled ? otherPercent : 0f);
            float f, m;
            r.Split(pair, step, out f, out m);
            femalePercent = f;
            malePercent = m;
        }

        /// <summary>把当前的女:男 取整后设为新的锁定比例（用户拖动滑块时用）。</summary>
        public void CaptureRatioFromCurrent()
        {
            int w = (int)Math.Round(femalePercent);
            int m = (int)Math.Round(malePercent);
            if (w == 0 && m == 0) { w = 1; m = 1; }
            Ratio r = new Ratio(w, m);
            lockRatioW = r.W;
            lockRatioM = r.M;
        }

        /// <summary>直接设定锁定比例。</summary>
        public void SetLockedRatio(int w, int m)
        {
            Ratio r = new Ratio(w, m);
            lockRatioW = r.W;
            lockRatioM = r.M;
        }

        /// <summary>
        /// 由用户输入设定锁定比例，并立刻按它重排百分比。
        /// 支持 "23:22" / "3：2" / "女3:男2" / 纯数字（视为 w:1）；会自动约分。
        /// </summary>
        public bool SetLockedRatioFromText(string text, float step, out string error)
        {
            Ratio r = Ratio.Parse(text, out error);
            if (r == null) return false;

            lockRatioW = r.W;
            lockRatioM = r.M;
            lockRatio = true;
            ApplyLockedRatio(step);
            error = null;
            return true;
        }

        // ---------------------------------------------------------------- 滑块语义

        /// <summary>
        /// 「女 / 男」滑块的**物理上限** = 100 - 其他性别。
        /// 即：滑块最右端就是"（其他性别之外）全女"，多出来的那一截由其他性别占着、拖不过去。
        /// </summary>
        public float FemaleSliderMax { get { return 100f - (otherEnabled ? otherPercent : 0f); } }

        /// <summary>
        /// 「女 / 男」滑块读数 = **女性的绝对百分比**（v0.2.16 简化）。
        ///
        /// 旧语义是"女性占 (女+男) 的比重"，在 其他=10% 时读数 51% 而女性实际只有 46%，
        /// 读起来自相矛盾。现在读数**就是**女性占比，上限 = 100-其他（到顶即"其他+全女"）。
        /// </summary>
        public float FemaleSliderValue
        {
            get { return femalePercent; }
            set
            {
                float max = FemaleSliderMax;
                float x = Clamp(value, 0f, max);   // 超过上限直接夹到上限 = 「锁住」最右那一段
                femalePercent = x;
                malePercent = max - x;
            }
        }

        /// <summary>
        /// 改「其他性别」占比。若锁定男女比例，则按**精确存储的整数比例**重排女/男
        /// （而不是从当前读数反推 —— 反推会漂移，这是 v0.2.14 修掉的 bug）。
        /// </summary>
        public void SetOtherPercent(float value, float step)
        {
            float newOther = Clamp(value, 0f, 100f);
            otherPercent = newOther;

            if (lockRatio)
            {
                ApplyLockedRatio(step);
            }
            else
            {
                // 不锁定：保持女性的绝对值，男性吃掉剩余（剩余不够就把男性压到 0）
                float newPair = 100f - newOther;
                femalePercent = Clamp(femalePercent, 0f, newPair);
                malePercent = newPair - femalePercent;
            }
        }

        public GenderRatioSettings Clone()
        {
            GenderRatioSettings c = new GenderRatioSettings(otherEnabled, otherPercent, femalePercent, malePercent);
            c.otherLabel = otherLabel;
            c.lockRatio = lockRatio;
            c.lockRatioW = lockRatioW;
            c.lockRatioM = lockRatioM;
            return c;
        }

        /// <summary>
        /// 规范化：clamp 到合法范围、总和归一到 100、未启用时把 other 归零。
        /// 注意**不做步长吸附** —— 吸附交给 UI 用 SnapToStep()，免得反复取整把女:男比值弄歪。
        /// </summary>
        public void Normalize(float stepPercent)
        {
            if (stepPercent <= 0f) stepPercent = 5f;

            otherPercent = Clamp(otherPercent, 0f, 100f);
            if (!otherEnabled) otherPercent = 0f;
            if (otherPercent > 100f - stepPercent) otherPercent = 100f - stepPercent;

            float pair = 100f - otherPercent;
            femalePercent = Clamp(femalePercent, 0f, pair);
            malePercent = pair - femalePercent;
            if (malePercent < 0f) malePercent = 0f;
        }

        /// <summary>把三个百分比吸附到步长刻度（UI 用；始终保持总和 100）。</summary>
        public void SnapToStep(float stepPercent)
        {
            if (stepPercent <= 0f) return;
            if (otherEnabled) otherPercent = Snap(otherPercent, stepPercent);
            float pair = 100f - otherPercent;

            if (lockRatio)
            {
                // 锁定时由**整数比例**决定，不吸附单个值，否则会破坏比例
                ApplyLockedRatio(stepPercent);
            }
            else
            {
                // 只吸附女性的绝对占比，男性永远 = 余量 ⇒ 总和恒为 100（不会出现 99/101）
                femalePercent = Snap(Clamp(femalePercent, 0f, pair), stepPercent);
                malePercent = pair - femalePercent;
            }
        }

        // ---------------------------------------------------------------- 计算

        /// <summary>最终三个百分比，返回 [女, 男, 其他]。</summary>
        public float[] Percents()
        {
            float other = otherEnabled ? otherPercent : 0f;
            float pair = 100f - other;
            float f = Clamp(femalePercent, 0f, pair);
            float m = pair - f;
            if (m < 0f) m = 0f;
            return new float[] { f, m, other };
        }

        /// <summary>最终生成用权重（int），返回 [女, 男, 其他]。顺序已按 IL 实证固定。</summary>
        public int[] Weights()
        {
            float[] p = Percents();
            return new int[] { ToWeight(p[0]), ToWeight(p[1]), ToWeight(p[2]) };
        }

        // ================================================================ ★ 肖像池优先（核心规则）

        /// <summary>
        /// ★★ 核心规则「肖像池优先」（移交文档 6.2）：**肖像池决定"有哪些性别可用"，
        /// 性别比例只在这个可用空间内分配权重。**
        ///
        /// 返回 [女, 男, 其他]，三者之和**恒 &gt; 0**
        /// （`SimGameState.GetWeightedResult` 要求总权重大于 0，否则会异常或返回意外结果）。
        ///
        /// <para><b>portraitMode = false</b>（未开启头像替换）：**完全等于老行为** ——
        /// 三个性别都按配置生成，不做任何裁剪（文档 6.5）。</para>
        ///
        /// <para><b>portraitMode = true</b>：</para>
        /// <list type="number">
        ///   <item>没有图的性别，权重直接归 0（文档 6.2 步骤 1/2）；</item>
        ///   <item>第三类还要用户**明确启用**了它才算可用（否则权重为 0）；</item>
        ///   <item>剩下的**按比例归一到总和 100** —— 这就是"男+女 = 100%"的通用写法
        ///         （文档 6.3：不要硬编码"乘 2"，任何可用组合都要对）；</item>
        ///   <item>若可用性别们配置的占比**全是 0**（例如只有女性有图、但 female_percent 恰好是 0），
        ///         就退成"可用性别之间平分" —— 对应文档 6.2 表格情况 ③④⑦ 的
        ///         `1,0,0` / `0,1,0` / `0,0,1`：**无视比例，只生成有图的那些性别**，
        ///         而不是把没图的性别放回来；</item>
        ///   <item>连一个可用性别都没有（图片池是空的）⇒ 退回 `1,1,1` 兜底。
        ///         ⚠️ 正常路径走不到这里，因为 portraitMode 为 true 的前提就是池子非空（文档 6.4）。</item>
        /// </list>
        /// </summary>
        public int[] ResolveFinalWeights(bool portraitMode, bool otherEnabled,
                                         bool hasFemale, bool hasMale, bool hasOther)
        {
            // ---- 未开启头像替换：老行为，一个字都不裁剪 ----
            if (!portraitMode) return Weights();

            // 当前生效的三个百分比 [女, 男, 其他]（未启用第三类时 p[2] 已经是 0）
            float[] p = Percents();

            bool F = hasFemale;
            bool M = hasMale;
            bool O = hasOther && otherEnabled;   // 第三类：有图 **且** 用户启用了它
            int usable = (F ? 1 : 0) + (M ? 1 : 0) + (O ? 1 : 0);
            if (usable == 0) return new int[] { 1, 1, 1 };

            float f = F ? p[0] : 0f;
            float m = M ? p[1] : 0f;
            float o = O ? p[2] : 0f;
            float sum = f + m + o;

            if (sum <= 0f)
            {
                // 可用性别的配置占比全是 0 ⇒ 不看比例，只在可用性别之间平分
                f = F ? 1f : 0f;
                m = M ? 1f : 0f;
                o = O ? 1f : 0f;
                sum = usable;
            }

            float k = 100f / sum;                 // 归一：把可用空间填满到 100
            return new int[] { ToWeight(f * k), ToWeight(m * k), ToWeight(o * k) };
        }

        /// <summary>用于「值没变就不重复下发/不重复打日志」的签名字符串。</summary>
        public string Signature()
        {
            float[] p = Percents();
            return F(p[0]) + "/" + F(p[1]) + "/" + F(p[2]);
        }

        /// <summary>人类可读的百分比描述，例如「女 30% / 男 60% / 其他 10%」。</summary>
        public string Describe(string femaleLabel, string maleLabel)
        {
            return Describe(femaleLabel, maleLabel, otherLabel);
        }

        /// <summary>
        /// 同上，但允许指定"第三类"的用词 —— 英文界面下要用英文词，
        /// 不能把 combined.json 里那个中文 other_label 直接印出来。
        /// </summary>
        public string Describe(string femaleLabel, string maleLabel, string otherWord)
        {
            float[] p = Percents();
            return femaleLabel + " " + Pct(p[0]) + " / " + maleLabel + " " + Pct(p[1])
                 + " / " + otherWord + " " + Pct(p[2]);
        }

        // ---------------------------------------------------------------- 工具

        public static float Clamp(float v, float lo, float hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        /// <summary>把任意值吸附到最近的步长刻度。</summary>
        public static float Snap(float v, float step)
        {
            if (step <= 0f) return v;
            return (float)(Math.Round(v / step) * step);
        }

        /// <summary>百分比 -> 权重整数。百分比 > 0 时至少给 1，避免被 GetWeightedResult 完全忽略。</summary>
        public static int ToWeight(float percent)
        {
            if (percent <= 0f) return 0;
            int w = (int)Math.Round(percent * 100f);
            if (w < 1) w = 1;
            return w;
        }

        public static string Pct(float v)
        {
            double r = Math.Round(v, 1);
            if (Math.Abs(r - Math.Round(r)) < 0.05) return ((int)Math.Round(r)).ToString(CultureInfo.InvariantCulture) + "%";
            return r.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }

        private static string F(float v)
        {
            return Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 「恢复默认」用的基准值：**精确**还原游戏原版 女46 / 男44 / 非二元10。
        /// v0.2.5 起直接存三个百分比，所以不再受步长取整影响。
        /// </summary>
        public static GenderRatioSettings VanillaDefaults()
        {
            return new GenderRatioSettings(true, 10f, 46f, 44f);
        }
    }
}
