using System;
using System.Collections.Generic;
using System.IO;

namespace PilotCombined
{
    /// <summary>
    /// 「性别名字 → 槽位」的**唯一**映射规则。
    ///
    /// 槽位：**0 = 女, 1 = 男, 2 = 第三类（非二元性别 / 无性别）, -1 = 不认识**。
    ///
    /// 为什么不直接返回 <c>BattleTech.Gender</c>：本文件**刻意不引用 UnityEngine / Assembly-CSharp**，
    /// 这样它能进「脱机单测」。图片池的"按目录名分性别"是最容易写错、又最难在游戏里排查的一环
    /// （改错一个字 → 整批图静默变成"未标注" → 性别可用性判断全错），所以它**必须可测**。
    ///
    /// 约定：<c>portraits\female\</c> / <c>portraits\male\</c> / <c>portraits\other\</c>；
    /// 目录名或**文件名下划线之前的前缀**都可以（<c>female_01.png</c> 也认）。
    /// 加新别名只改这里一处。
    /// </summary>
    internal static class GenderNames
    {
        internal const int Female = 0;
        internal const int Male = 1;
        internal const int Other = 2;
        internal const int Unknown = -1;

        /// <summary>把单个词（目录名 / 文件名前缀）映射成槽位。</summary>
        internal static int SlotFromToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return Unknown;

            switch (token.Trim().ToLowerInvariant())
            {
                // ---- 女 ----
                case "female": case "f": case "woman": case "women": case "girl": case "girls":
                case "女": case "女性":
                    return Female;

                // ---- 男 ----
                case "male": case "m": case "man": case "men": case "boy": case "boys":
                case "男": case "男性":
                    return Male;

                // ---- 第三类：非二元性别 / 无性别（机器人也归这里）----
                case "other": case "others":
                case "nonbinary": case "non-binary": case "non_binary": case "nb": case "enby":
                case "neutral": case "none": case "genderless": case "agender":
                case "robot": case "robots": case "mech": case "mechs": case "drone": case "drones":
                case "无性别": case "非二元": case "非二元性别": case "其他": case "其他性别":
                case "中性": case "机器人":
                    return Other;

                default:
                    return Unknown;
            }
        }

        /// <summary>
        /// 从一个**相对路径**推断性别。
        /// 先看目录段（跳过 <c>portraits</c> 这一层），再看文件名里下划线之前的前缀。
        /// </summary>
        internal static int SlotFromPath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return Unknown;

            string norm = relativePath.Replace('\\', '/');
            string[] segs = norm.Split('/');
            for (int i = 0; i < segs.Length - 1; i++)          // 最后一段是文件名，不算目录
            {
                string tok = segs[i];
                if (tok.Length == 0 || tok.Trim().ToLowerInvariant() == "portraits") continue;
                int s = SlotFromToken(tok);
                if (s != Unknown) return s;
            }

            string stem = Path.GetFileNameWithoutExtension(norm).ToLowerInvariant();
            int us = stem.IndexOf('_');
            if (us > 0) return SlotFromToken(stem.Substring(0, us));
            return Unknown;
        }

        /// <summary>
        /// 找出路径里「看起来是分类目录、但名字不认识」的那些段。
        /// 用途：用户在 <c>portraits\</c> 下建了一个拼错/没约定的子目录时，
        /// 启动日志必须**明确提醒**，而不是让那批图静默变成"未标注"。
        /// </summary>
        internal static string[] UnrecognizedDirTokens(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return new string[0];

            string norm = relativePath.Replace('\\', '/');
            string[] segs = norm.Split('/');
            List<string> bad = new List<string>();
            for (int i = 0; i < segs.Length - 1; i++)
            {
                string tok = segs[i];
                if (tok.Length == 0) continue;
                string low = tok.Trim().ToLowerInvariant();
                if (low == "portraits") continue;
                if (SlotFromToken(tok) != Unknown) continue;      // 认识
                if (low.StartsWith("_")) continue;                // 下划线开头＝用户主动"停用"的文件夹，不啰嗦
                if (!bad.Contains(tok)) bad.Add(tok);
            }
            return bad.ToArray();
        }

        /// <summary>槽位 → 日志里用的短标签（与 portraits 下的目录名一致：female / male / other）。</summary>
        internal static string TagOf(int slot)
        {
            switch (slot)
            {
                case Female: return "female";
                case Male: return "male";
                case Other: return "other";
                default: return "unknown";
            }
        }
    }
}
