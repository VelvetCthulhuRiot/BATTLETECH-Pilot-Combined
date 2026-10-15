using System;
using System.IO;
using System.Text;

namespace PilotCombined
{
    /// <summary>
    /// 融合 MOD 的**唯一**日志（合并自旧 GenderRatio 与 PortraitSwap 的两个 Log）。
    ///
    /// 同时写两处：
    ///   1) 游戏 <c>output_log.txt</c>，带 <c>[PilotCombined]</c> 前缀；
    ///   2) <c>&lt;mod目录&gt;\PilotCombined.log</c>，UTF-8 **带 BOM**
    ///      （中文 Windows 上双击用记事本打开也不乱码）。
    ///
    /// ⚠️ 本文件被「脱机单测」（<c>tests\TestMain.cs</c>）引用，所以 UnityEngine 那一行走**反射**，
    /// 不写 <c>using UnityEngine</c> —— 这样测试程序不需要引用 UnityEngine.dll。
    /// 改这个类时**务必**保持这个方法名集合不变：Init / Section / Info / Warn / Error。
    /// </summary>
    internal static class Log
    {
        /// <summary>写进 output_log.txt 的前缀（便于和汉化 MOD 等区分）。</summary>
        internal const string Prefix = "[PilotCombined]";

        private static string _path;
        private static readonly object _lock = new object();
        private static readonly Encoding Utf8Bom = new UTF8Encoding(true);
        private static bool _unityWarned;

        /// <summary>
        /// 建立日志文件。每次启动**清空重写**（旧日志会被覆盖，看日志请看完再退出游戏）。
        /// header 一般写"版本 + modDir"。
        /// </summary>
        internal static void Init(string path, string header)
        {
            _path = path;
            try
            {
                if (File.Exists(_path)) File.Delete(_path);
                File.WriteAllText(_path,
                    "PilotCombined log @ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n"
                    + (header == null ? "" : header + "\r\n"), Utf8Bom);
            }
            catch
            {
                _path = null;
            }
        }

        /// <summary>打一条醒目的分节线，让日志里的"两个子系统"一眼分得开。</summary>
        internal static void Section(string title)
        {
            Write("INFO ", "================ " + title + " ================");
        }

        internal static void Info(string msg) { Write("INFO ", msg); }
        internal static void Warn(string msg) { Write("WARN ", msg); }
        internal static void Error(string msg) { Write("ERROR", msg); }

        private static void Write(string level, string msg)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + level + " " + msg;

            // 1) 游戏 output_log.txt —— 带 [PilotCombined] 前缀
            if (!_unityWarned)
            {
                try
                {
                    Type dbg = Type.GetType("UnityEngine.Debug, UnityEngine.CoreModule");
                    if (dbg != null)
                    {
                        var m = dbg.GetMethod("Log", new Type[] { typeof(object) });
                        if (m != null) m.Invoke(null, new object[] { Prefix + " " + msg });
                    }
                }
                catch
                {
                    _unityWarned = true;    // 静默：脱机单测里没有 UnityEngine
                }
            }

            // 2) mod 目录下的独立日志
            if (_path == null) return;
            lock (_lock)
            {
                try { File.AppendAllText(_path, line + "\r\n", Utf8Bom); } catch { }
            }
        }
    }
}
