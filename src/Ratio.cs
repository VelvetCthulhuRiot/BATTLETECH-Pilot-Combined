using System;
using System.Collections.Generic;

namespace PilotCombined.GenderRatio
{
    /// <summary>
    /// 一个**精确的**男女比例（整数对，自动约分），用于「锁定男女比例」。
    ///
    /// 为什么需要它（v0.2.14 修正的漂移 bug）：
    ///   旧做法是"每次从当前滑块读数反推比例"。可滑块只能表达**整数百分比**，
    ///   所以能实现的女:男 永远和理想比例差一点点；而每次反推都会把这个"差一点"当成新比例，
    ///   于是比例**自己漂走**（用户报的现象："固定的读数会变，滑块取小数试图接近，出现浮动"）。
    ///
    /// 正确做法：比例用**整数精确存储**，需要时再换算成最接近的整数百分比：
    ///      总量 pair 固定时   F = round(pair * w / (w + m))   M = pair - F
    ///   比例本身**永不改变**，只是"用 1% 粒度尽量接近它"。这样就彻底不会漂。
    ///
    /// 加新分类（例如以后加"机器人"）时，这里可以扩展成 w:m:o 三元组。
    /// </summary>
    public sealed class Ratio
    {
        public int W;   // 女
        public int M;   // 男

        public Ratio(int w, int m)
        {
            if (w < 0) w = 0;
            if (m < 0) m = 0;
            if (w == 0 && m == 0) { w = 1; m = 1; }
            int g = Gcd(w, m);
            W = w / g;
            M = m / g;
        }

        /// <summary>约分后的字符串，例如 "23:22"。</summary>
        public string Text { get { return W + ":" + M; } }

        public bool IsZero { get { return W == 0 && M == 0; } }

        public override string ToString() { return Text; }

        /// <summary>在总量 pair 里，按本比例算出最接近的整数百分比对（之和恒 = pair）。</summary>
        public void Split(float pair, float step, out float female, out float male)
        {
            float p = pair < 0f ? 0f : pair;
            int total = W + M;
            if (total <= 0) { female = 0f; male = p; return; }

            float raw = p * W / (float)total;
            float f = step > 0f ? (float)Math.Round(raw / step) * step : raw;
            if (f < 0f) f = 0f;
            if (f > p) f = p;

            female = f;
            male = p - f;
        }

        // ---------------------------------------------------------------- 解析

        /// <summary>
        /// 解析用户输入，支持这些写法（全部会被约分）：
        ///     "3:2" / "3：2" / "3/2" / "3-2" / "3 2" / "3,2"
        ///     "女3:男2" / "F3:M2" / "3:2 男"
        ///     "女 3 : 男 2"
        ///     纯数字 "3"  ⇒ 视为 3:1（"女3、男1"）
        /// 失败返回 null，并给出原因。
        /// </summary>
        public static Ratio Parse(string text, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(text)) { error = "输入为空"; return null; }

            List<int> nums = new List<int>();
            char[] cs = text.ToCharArray();
            for (int i = 0; i < cs.Length; i++)
            {
                if (cs[i] >= '0' && cs[i] <= '9')
                {
                    int v = 0;
                    while (i < cs.Length && cs[i] >= '0' && cs[i] <= '9')
                    {
                        v = v * 10 + (cs[i] - '0');
                        if (v > 100000) { error = "数字太大"; return null; }
                        i++;
                    }
                    nums.Add(v);
                    i--;   // 上面 while 多走了一步
                }
            }

            if (nums.Count == 0) { error = "没找到数字"; return null; }
            if (nums.Count > 2) { error = "最多只认两个数字（女:男）"; return null; }

            int w = nums[0];
            int m = nums.Count >= 2 ? nums[1] : 1;
            if (w == 0 && m == 0) { error = "两个数不能都是 0"; return null; }

            return new Ratio(w, m);
        }

        private static int Gcd(int a, int b)
        {
            if (a < 0) a = -a;
            if (b < 0) b = -b;
            while (b != 0) { int t = a % b; a = b; b = t; }
            if (a == 0) a = 1;
            return a;
        }
    }
}
