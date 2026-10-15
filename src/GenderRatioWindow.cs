using System;
using System.Collections.Generic;
using BattleTech;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PilotCombined.GenderRatio
{
    /// <summary>
    /// 游戏内性别比例窗口。
    ///
    /// **v0.2.7 起：不再复用任何游戏预制体，全部自绘。**
    ///
    /// 为什么放弃复用（每一步都是真机实测换来的）：
    ///   * 游戏控件（HBSSliderInput / HBSToggle）的方括号、标题文字、圆形手柄
    ///     是由 `NestedPrefabAnchor` **异步注入**的：
    ///       - 实例化那一刻扫不到 ⇒ 扫到 0 个 HBSToggle、滑块只剩一条矩形；
    ///       - 等它注入之后又会把"镜头平移""反转轴"等文字带进窗口 ⇒ **重影**；
    ///       - 想同步剔掉它，又因为时机问题剔不干净。
    ///   * 结论：这条路能做，但要和引擎的注入时机赛跑，收益不值当。
    ///     ⇒ 自己画：结构、位置、尺寸全部**确定**，不再有任何时序赌博。
    ///
    /// 布局方式也从 `VerticalLayoutGroup` 改成**绝对定位**：
    ///   布局组的自动计算正是之前"标题贴到黄线上""勾选框被压成 0 尺寸点不到"的根源。
    ///   现在每个元素自己写死 y 坐标，所见即所得。
    ///
    /// 配色仍照原版（深底 / 暖黄强调 / 蓝灰滑块轨道），所以观感依然是 HBS 风格。
    /// </summary>
    internal static class GenderRatioWindow
    {
        private const int SortingOrder = 30000;
        private const float WinW = 760f;
        /// <summary>
        /// 面板高度。布局从顶部累计（见 Build），**内容溢出时日志会报警**。
        /// 606 → 652：M9 在最上面加了一行「启用头像替换」总开关。
        /// </summary>
        private const float WinH = 652f;
        private const float PadX = 34f;          // 左右内边距

        // 原版配色
        private static readonly Color PanelBg = new Color(0.043f, 0.051f, 0.067f, 1f);
        private static readonly Color FrameColor = new Color(0.545f, 0.478f, 0.322f, 1f);
        private static readonly Color AccentColor = new Color(0.949f, 0.647f, 0.196f, 1f);
        private static readonly Color TitleColor = new Color(0.949f, 0.902f, 0.722f, 1f);
        private static readonly Color TextColor = new Color(0.88f, 0.88f, 0.90f, 1f);
        private static readonly Color DimColor = new Color(0.70f, 0.70f, 0.74f, 1f);
        private static readonly Color ButtonBg = new Color(0.086f, 0.098f, 0.125f, 1f);
        private static readonly Color CheckOn = new Color(0.949f, 0.647f, 0.196f, 1f);
        private static readonly Color CheckOff = new Color(0.16f, 0.18f, 0.22f, 1f);

        private static GameObject _root;
        private static GameObject _window;
        private static bool _open;

        private static Text _statusText;
        private static Text _poolText;
        private static Text _otherValueText;
        private static Text _femaleValueText;
        private static Text _hintText;

        private static StepSlider _otherSlider;
        private static StepSlider _femaleSlider;
        private static CheckBox _otherCheck;
        private static CheckBox _lockCheck;
        private static CheckBox _portraitCheck;
        private static UnityEngine.UI.InputField _ratioWField;
        private static UnityEngine.UI.InputField _ratioMField;

        private static GameInstance _game;

        private static Font _font;
        private static bool _fontResolved;

        // ================================================================ 对外入口

        internal static void Toggle(GameInstance game)
        {
            if (game != null) _game = game;
            if (_open) { Close(); return; }
            Open();
        }

        internal static bool IsOpen { get { return _open; } }

        internal static void Open()
        {
            try
            {
                GenderRatioConfig cfg = Patches.Config;
                if (cfg == null) return;

                // ★ 界面语言在这里**再检测一次**，然后才建窗口（Build 会把文案定下来）。
                //   原因：mod 初始化时游戏的语言/译文可能还没就绪（那时可能读到 UNDEFINED），
                //   而按下 F9 时游戏肯定已经完全加载 —— 这也是"语言跟随游戏设置"最可靠的时机。
                //   语言变了才记一行日志，避免每次开窗口都刷屏。
                Loc.Lang langBefore = Loc.Current;
                Loc.Detect(cfg.uiLanguage);
                if (Loc.Current != langBefore)
                    Log.Info("界面语言（开窗口时重新检测）：" + Loc.Current + "（" + Loc.DetectionDetail + "）");

                Build();

                _open = true;
                Log.Info("已打开性别比例窗口（v0.2.7 自绘控件 + 绝对定位，不复用游戏预制体）。");
            }
            catch (Exception e)
            {
                Log.Error("打开窗口失败：" + e);
                Close();
            }
        }

        internal static void Close()
        {
            _open = false;
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null; _window = null;
            _statusText = null; _otherValueText = null; _femaleValueText = null; _hintText = null;
            _otherSlider = null; _femaleSlider = null; _otherCheck = null; _lockCheck = null;
            _ratioWField = null; _ratioMField = null;
            Log.Info("性别比例窗口已关闭。");
        }

        /// <summary>保留给热键补丁调用；自绘控件不需要逐帧做事。</summary>
        internal static void Poll() { }

        // ================================================================ 构建

        private static void Build()
        {
            if (_root != null) { UnityEngine.Object.Destroy(_root); _root = null; }

            _root = new GameObject("GenderRatioRoot");
            UnityEngine.Object.DontDestroyOnLoad(_root);

            Canvas canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            canvas.overrideSorting = true;

            // ★ 用 ConstantPixelSize：界面尺寸 = **真实屏幕像素**，与分辨率无关。
            //
            // 之前用 ScaleWithScreenSize + 参考分辨率 1920x1080，在用户的屏幕上算出
            // canvasScale ≈ 0.5（真机日志：面板 760x480 报出来是 762x482 的**局部**坐标，
            // 但子元素实际只占一半），于是整套"按像素写死"的布局被整体压扁一半 ⇒ 看着错位。
            // 改成一像素就是一像素之后，坐标常量在任何分辨率下含义都一样。
            CanvasScaler scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            _root.AddComponent<GraphicRaycaster>();

            Log.Info("屏幕 " + Screen.width + "x" + Screen.height
                     + "，Canvas 用 ConstantPixelSize(scaleFactor=1) ⇒ 窗口固定 " + WinW + "x" + WinH + " 真实像素。");

            RectTransform rootRt = _root.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero; rootRt.offsetMax = Vector2.zero;
            rootRt.pivot = new Vector2(0.5f, 0.5f);

            // 遮蔽层
            RectTransform dim = NewRect(_root.transform, "Dim");
            Stretch(dim);
            Image dimImg = dim.gameObject.AddComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0f, 0.62f);
            dimImg.raycastTarget = true;

            // 主面板
            _window = new GameObject("Window");
            _window.transform.SetParent(_root.transform, false);
            RectTransform win = _window.AddComponent<RectTransform>();
            win.anchorMin = new Vector2(0.5f, 0.5f);
            win.anchorMax = new Vector2(0.5f, 0.5f);
            win.pivot = new Vector2(0.5f, 0.5f);
            win.anchoredPosition = Vector2.zero;
            win.sizeDelta = new Vector2(WinW, WinH);

            Image bg = _window.AddComponent<Image>();
            bg.color = PanelBg;
            bg.raycastTarget = true;

            AddBorder(win, FrameColor, 1.5f);

            // ---------------- 绝对定位布局 ----------------
            // ★ 唯一约定：下面所有数字都是"从面板**顶部**往下多少像素"，元素高度也固定写死。
            //   Place() 负责把它换算成 UGUI 的 local y（见 Place 的注释）。
            //   改位置只需要改这一组常量，不用动别的地方。
            //
            // 间距是照游戏自己设置菜单的**留白比例**定的（对着它的截图量的）：
            //   小标签 → 它的控件 约 31px（我们面板比原版设置菜单大 ~2.1 倍，所以取 ~14px 同级留白）
            //   小节标题 → 内容  约 25px
            // 之前用 4~6px，结果整块显得挤在一起、看着"没对齐"。
            const float TitleH = 40f;              // 给足行高，否则 24 号字会溢出 rect
            const float TitleGapToLine = 16f;      // 标题与黄线之间的留白（原来是 6px，太挤像压线）
            const float rowGap = 14f;
            float yTop = 26f;   // 顶部留白

            Text title = AddText(win, "Title", Loc.T("win.title"), 24, TitleColor, TextAnchor.MiddleLeft);
            Place(title.rectTransform, PadX, yTop, WinW - PadX * 2f, TitleH);
            yTop += TitleH + TitleGapToLine;

            Image accent = NewImage(win, "AccentLine", AccentColor);   // 暖黄强调线
            Place(accent.rectTransform, PadX, yTop, WinW - PadX * 2f, 2f);
            yTop += 2f + 22f;

            _statusText = AddText(win, "Status", "", 16, TextColor, TextAnchor.UpperLeft);
            Place(_statusText.rectTransform, PadX, yTop, WinW - PadX * 2f, 44f);
            yTop += 44f + 10f;

            // ---- 肖像池状态（两行，14 号字）----
            // 这一行是"界面不许骗人"的关键：用户必须能一眼看到
            //   ① 每个性别到底有几张图；② 因此哪些性别会被生成；
            //   ③ 哪些控件为什么是灰的（文档 6.6 / 决策 10）。
            _poolText = AddText(win, "PoolInfo", "", 14, DimColor, TextAnchor.UpperLeft);
            Place(_poolText.rectTransform, PadX, yTop, WinW - PadX * 2f, 40f);
            yTop += 40f + 10f;

            // ---- 勾选框 0：头像替换总开关（M9）----
            // 放在最上面：它决定"下面这些比例设置会不会被图片有无影响"。
            _portraitCheck = AddCheck(win, "PortraitCheck", PadX, yTop,
                Loc.T("chk.portraits"),
                delegate (bool v) { SetPortraitReplacement(v); });
            yTop += 30f + 16f;

            // ---- 勾选框 1 ----
            _otherCheck = AddCheck(win, "OtherCheck", PadX, yTop,
                Loc.TF("chk.other", OtherLabel()),
                delegate (bool v)
                {
                    GenderRatioSettings s = Patches.Config.settings;
                    s.otherEnabled = v;
                    if (!v) s.otherPercent = 0f;
                    else if (s.otherPercent <= 0f) s.otherPercent = Step();
                    s.Normalize(Step());
                    Log.Info("勾选框「" + OtherLabel() + "」-> " + v);
                    RefreshAll();
                });
            yTop += 30f + 16f;

            // ---- 其他性别滑块 ----
            _otherValueText = AddText(win, "OtherValue", "", 15, DimColor, TextAnchor.MiddleLeft, true);
            Place(_otherValueText.rectTransform, PadX, yTop, WinW - PadX * 2f, 22f);
            yTop += 22f + 14f;

            _otherSlider = AddSlider(win, "OtherSlider", PadX, yTop, WinW - PadX * 2f, true);
            yTop += 26f + rowGap + 12f;

            // ---- 男女滑块 ----
            // v0.2.16：滑块读数现在**就是女性占比**，所以不再需要单独一行"滑块读数"，
            // 直接把分配与上限写在同一行里。
            _femaleValueText = AddText(win, "FemaleValue", "", 15, DimColor, TextAnchor.MiddleLeft, true);
            Place(_femaleValueText.rectTransform, PadX, yTop, WinW - PadX * 2f, 22f);
            yTop += 22f + 14f;

            _femaleSlider = AddSlider(win, "FemaleSlider", PadX, yTop, WinW - PadX * 2f, false);
            yTop += 26f + rowGap + 12f;

            // ---- 勾选框 2：锁定男女比例 ----
            _lockCheck = AddCheck(win, "LockCheck", PadX, yTop,
                Loc.TF("chk.lock", OtherLabel()),
                delegate (bool v)
                {
                    GenderRatioSettings s = Patches.Config.settings;
                    s.lockRatio = v;
                    if (v)
                    {
                        // 勾上时：如果用户已经填了比例就用它，否则把当前女:男 取整作为初始比例
                        if (!TryApplyTypedRatio(false)) s.CaptureRatioFromCurrent();
                        s.ApplyLockedRatio(Step());
                    }
                    Log.Info("勾选框「锁定男女」-> " + v
                             + "，当前锁定比例 = " + s.LockedRatio.Text);
                    RefreshAll();
                });
            yTop += 30f + 10f;

            // ---- 比例输入行（女 : 男）----
            BuildRatioInput(win, yTop, out _ratioWField, out _ratioMField);
            yTop += 34f + 12f;

            // ---- 按钮行 ----
            // 4 个按钮挤一行：总宽 = 4*150 + 3*12 = 636（面板内容区 692），不用再加高窗口。
            float bw = 150f, gap = 12f;
            AddButton(win, PadX, yTop, bw, Loc.T("btn.apply"), ApplyAndPersist);
            AddButton(win, PadX + (bw + gap), yTop, bw, Loc.T("btn.rescan"), RescanPortraits);
            AddButton(win, PadX + (bw + gap) * 2f, yTop, bw, Loc.T("btn.defaults"), RestoreDefaults);
            AddButton(win, PadX + (bw + gap) * 3f, yTop, bw, Loc.T("btn.close"), Close);
            yTop += 36f + 10f;

            // ---- 底部提示 ----
            // 措辞注意宽度：这一行是 692px 宽、字号 14 的**单行**，太长会被截断/把尾巴挤掉。
            // 所以这里用了最短的写法（「最右那段锁住」那个信息在上面的女/男 那一行已经说了）。
            _hintText = AddText(win, "Hint",
                                Loc.TF("hint.main", OtherLabel()),
                                14, DimColor, TextAnchor.MiddleLeft, true);
            Place(_hintText.rectTransform, PadX, yTop, WinW - PadX * 2f, 22f);
            yTop += 22f;

            float remain = WinH - yTop;
            if (remain < 0f)
                Log.Warn("窗口内容超出面板高度 " + (-remain).ToString("0") + "px！"
                         + "请把日志给我，我调 WinH 或行距（当前 WinH=" + WinH + "，内容底边=" + yTop.ToString("0") + "）。");

            RefreshAll();
            Log.Info("窗口已创建：" + WinW + "x" + WinH + "，内容底边距面板底部 " + remain.ToString("0") + "px。");
            if (WinW > Screen.width - 40 || WinH > Screen.height - 40)
                Log.Warn("窗口 " + WinW + "x" + WinH + " 可能装不下当前屏幕 " + Screen.width + "x" + Screen.height
                         + "（ConstantPixelSize 下是真实像素）。若被裁切请告诉我分辨率，我调小 WinW/WinH。");
            Log.Info(DescribeBounds(win));
        }

        /// <summary>
        /// 诊断：把面板里每个子元素**实际占用的矩形**打出来（单位 = 面板内像素，y 从面板顶部往下）。
        /// 位置不对时看这个，比对着截图猜快得多 —— 能立刻分辨"是摆放错了"还是"是文字溢出/换行了"。
        /// </summary>
        internal static string DescribeBounds(RectTransform panel)
        {
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("面板 ").Append(panel.rect.width.ToString("0")).Append("x")
                  .Append(panel.rect.height.ToString("0"))
                  .Append("；下列为各元素在面板内的实际占位（y 从面板顶部往下）：");
                Vector3[] c = new Vector3[4];
                for (int i = 0; i < panel.childCount; i++)
                {
                    RectTransform rt = panel.GetChild(i) as RectTransform;
                    if (rt == null) continue;
                    rt.GetWorldCorners(c);
                    // 父级 local 空间：y 向上为正、原点在父级中心 ⇒ 转成"距父级顶部"
                    Vector3 tl = panel.InverseTransformPoint(c[1]);
                    Vector3 br = panel.InverseTransformPoint(c[3]);
                    float half = panel.rect.height * 0.5f;
                    float top = half - tl.y;
                    float bottom = half - br.y;
                    if (top > bottom) { float t = top; top = bottom; bottom = t; }
                    sb.Append("\n    ").Append(rt.name.PadRight(22))
                      .Append("y ").Append(top.ToString("0")).Append("..").Append(bottom.ToString("0"))
                      .Append("   x ").Append((tl.x + panel.rect.width * 0.5f).ToString("0"))
                      .Append("..").Append((br.x + panel.rect.width * 0.5f).ToString("0"));
                }
                return sb.ToString();
            }
            catch (Exception e) { return "位置诊断失败：" + e.Message; }
        }

        // ================================================================ 子控件

        private static StepSlider AddSlider(RectTransform parent, string name, float x, float y, float w, bool isOther)
        {
            // 宿主行（绝对定位）
            GameObject host = new GameObject(name + "Row");
            host.transform.SetParent(parent, false);
            RectTransform hrt = host.AddComponent<RectTransform>();
            Place(hrt, x, y, w, 26f);

            bool cap = isOther;
            StepSlider s = StepSlider.Create(hrt, name, Step(), 0f, 100f,
                isOther ? Patches.Config.settings.otherPercent : Patches.Config.settings.FemaleSliderValue,
                delegate (float v) { OnSliderMoved(cap, v); });
            s.SetInteractable(!isOther || Patches.Config.settings.otherEnabled);
            return s;
        }

        private static void OnSliderMoved(bool isOther, float value)
        {
            GenderRatioConfig cfg = Patches.Config;
            if (cfg == null) return;
            GenderRatioSettings s = cfg.settings;
            float step = cfg.stepPercent;

            if (isOther)
            {
                // 锁定时按**存储的整数比例**重排女/男（比例不会因滑块粒度而漂移）
                s.SetOtherPercent(GenderRatioSettings.Snap(value, step), step);
            }
            else
            {
                // v0.2.16：滑块读数 = 女性的绝对百分比，上限 = 100-其他（超出直接夹住 = 锁住最右那段）
                float max = s.FemaleSliderMax;
                s.femalePercent = GenderRatioSettings.Snap(
                    GenderRatioSettings.Clamp(value, 0f, max), step);
                s.malePercent = max - s.femalePercent;
                if (s.malePercent < 0f) s.malePercent = 0f;
                // 用户直接拖了男女滑块 = 明确想改比例 ⇒ 把当前女:男 取整后更新为新的锁定比例
                if (s.lockRatio) s.CaptureRatioFromCurrent();
            }
            RefreshAll();
        }

        /// <summary>
        /// 读取两个输入框里的比例并应用。
        /// 支持在任一框里写完整比例（如 W 框写 "3:2"，M 框留空）——这种写法更省事。
        /// </summary>
        private static bool TryApplyTypedRatio(bool verbose)
        {
            GenderRatioConfig cfg = Patches.Config;
            if (cfg == null) return false;
            GenderRatioSettings s = cfg.settings;

            string tw = _ratioWField == null ? "" : _ratioWField.text;
            string tm = _ratioMField == null ? "" : _ratioMField.text;

            // 任一框里含分隔符 ⇒ 当作完整比例解析
            string full = null;
            if (!string.IsNullOrEmpty(tw) && HasSeparator(tw)) full = tw;
            else if (!string.IsNullOrEmpty(tm) && HasSeparator(tm)) full = tm;

            string text = full != null ? full : (tw + ":" + tm);
            string error;
            if (!s.SetLockedRatioFromText(text, cfg.stepPercent, out error))
            {
                if (verbose) Log.Warn("比例输入 '" + text + "' 无法解析：" + error);
                return false;
            }

            if (verbose)
                Log.Info("比例输入已应用：" + text + " ⇒ 约分后 " + s.LockedRatio.Text
                         + "，女 " + GenderRatioSettings.Pct(s.femalePercent)
                         + " / 男 " + GenderRatioSettings.Pct(s.malePercent));
            return true;
        }

        private static bool HasSeparator(string t)
        {
            return t.IndexOf(':') >= 0 || t.IndexOf('：') >= 0 || t.IndexOf('/') >= 0
                || t.IndexOf('-') >= 0 || t.IndexOf(',') >= 0 || t.IndexOf(' ') >= 0;
        }

        private static void OnRatioFieldChanged(string _)
        {
            ApplyRatioFromFields();
        }

        private static void ApplyRatioFromFields()
        {
            try
            {
                GenderRatioConfig cfg = Patches.Config;
                if (cfg == null) return;
                if (!TryApplyTypedRatio(false)) return;

                // 填了比例就视为想锁定
                cfg.settings.lockRatio = true;
                Log.Info("勾选框「锁定男女」-> True（由比例输入触发），锁定比例 = "
                         + cfg.settings.LockedRatio.Text);
                RefreshAll();
            }
            catch (Exception e) { Log.Warn("应用比例输入异常：" + e.Message); }
        }

        private static float Step()
        {
            GenderRatioConfig cfg = Patches.Config;
            if (cfg == null) return 1f;
            return cfg.stepPercent <= 0f ? 1f : cfg.stepPercent;
        }

        /// <summary>
        /// 「女 : 男」比例输入行：两个输入框夹一个冒号。
        ///
        /// 为什么要有它（v0.2.14）：锁定比例**必须**由用户明确给出，
        /// 不能从滑块读数反推 —— 滑块只有 1% 粒度，反推会把"差一点"当成新比例，
        /// 于是比例自己漂走。存成整数对之后，比例恒定，滑块只是"尽量接近它"。
        /// </summary>
        private static void BuildRatioInput(RectTransform parent, float yFromTop,
                                            out UnityEngine.UI.InputField wField,
                                            out UnityEngine.UI.InputField mField)
        {
            // ★ 不能直接用 NewRow：它的 rect 是铺满 PadX..WinW-PadX 的，
            //   那样贴左的子元素还是落在 PadX 上（对），但早期 NewRow 写死坐标时 rect 从 x=0 开始，
            //   结果这个标签顶到了窗口边框上（用户反馈）。这里显式给一个带 PadX 缩进的宿主行，
            //   并让 rect 宽度从 0 起算，方便内部用"相对行首"的 x。
            GameObject host = new GameObject("RatioInputRow");
            host.transform.SetParent(parent, false);
            RectTransform row = host.AddComponent<RectTransform>();
            float w = WinW - PadX * 2f;
            Place(row, PadX, yFromTop, w, 34f);

            Text label = AddText(row, "RatioLabel", Loc.T("ratio.label"), 16, TextColor, TextAnchor.MiddleLeft);
            Place(label.rectTransform, 0f, 0f, 150f, 34f);

            wField = AddNumberField(row, "RatioW", 152f, 28f, 62f);
            Text colon = AddText(row, "Colon", ":", 18, TextColor, TextAnchor.MiddleCenter);
            Place(colon.rectTransform, 218f, 0f, 18f, 34f);
            mField = AddNumberField(row, "RatioM", 240f, 28f, 62f);
            // 回车应用（标准 InputField 的 onEndEdit 就是回车/失焦时触发）
            wField.onEndEdit.AddListener(OnRatioFieldChanged);
            mField.onEndEdit.AddListener(OnRatioFieldChanged);

            Text hint = AddText(row, "RatioHint", Loc.T("ratio.hint"), 13, DimColor, TextAnchor.MiddleLeft);
            Place(hint.rectTransform, 314f, 0f, w - 314f, 34f);
        }

        /// <summary>一个带底框的数字输入框（标准 UnityEngine.UI.InputField）。</summary>
        private static UnityEngine.UI.InputField AddNumberField(RectTransform parent, string name,
                                                               float x, float y, float w)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(w, 28f);

            Image bg = go.AddComponent<Image>();
            bg.color = new Color(0.10f, 0.12f, 0.15f, 1f);
            Outline ol = go.AddComponent<Outline>();
            ol.effectColor = FrameColor;
            ol.effectDistance = new Vector2(1f, -1f);

            Text txt = AddText(go.transform as RectTransform, "Text", "", 16, TextColor, TextAnchor.MiddleCenter);
            txt.supportRichText = false;
            txt.raycastTarget = false;
            txt.rectTransform.anchorMin = Vector2.zero;
            txt.rectTransform.anchorMax = Vector2.one;
            txt.rectTransform.offsetMin = new Vector2(4f, 0f);
            txt.rectTransform.offsetMax = new Vector2(-4f, 0f);

            UnityEngine.UI.InputField f = go.AddComponent<UnityEngine.UI.InputField>();
            f.textComponent = txt;
            f.targetGraphic = bg;
            f.lineType = UnityEngine.UI.InputField.LineType.SingleLine;
            f.characterLimit = 4;
            f.contentType = UnityEngine.UI.InputField.ContentType.IntegerNumber;
            f.text = "1";
            return f;
        }

        /// <summary>
        /// 新建一个"宿主行"（绝对定位）。
        /// ⚠️ yFromTop 是**必填**的 —— 早期版本这里写死 `Place(rt, 0, 0, ...)`，
        /// 结果新加的比例输入行被钉在面板最顶部（真机日志：RatioInputRow y 0..34）。
        /// 凡是"可以忘传坐标"的 helper 都是 bug 温床，所以把它变成必填参数。
        /// </summary>
        private static RectTransform NewRow(RectTransform parent, string name, float yFromTop, float height)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            Place(rt, 0f, yFromTop, WinW - PadX * 2f, height);
            return rt;
        }

        /// <summary>自绘勾选框：方框 + 勾号 + 右侧文字，整行可点。</summary>
        private static CheckBox AddCheck(RectTransform parent, string name, float x, float y,
                                        string label, UnityAction<bool> onChanged)
        {
            GameObject row = new GameObject(name + "Row");
            row.transform.SetParent(parent, false);
            RectTransform rrt = row.AddComponent<RectTransform>();
            Place(rrt, x, y, WinW - PadX * 2f, 30f);

            // 方框
            GameObject box = new GameObject("Box");
            box.transform.SetParent(row.transform, false);
            RectTransform brt = box.AddComponent<RectTransform>();
            brt.anchorMin = new Vector2(0f, 0.5f);
            brt.anchorMax = new Vector2(0f, 0.5f);
            brt.pivot = new Vector2(0f, 0.5f);
            brt.anchoredPosition = Vector2.zero;
            brt.sizeDelta = new Vector2(24f, 24f);
            Image boxImg = box.AddComponent<Image>();
            boxImg.color = CheckOff;
            boxImg.raycastTarget = true;      // 点方框也算
            Outline ol = box.AddComponent<Outline>();
            ol.effectColor = FrameColor;
            ol.effectDistance = new Vector2(1.5f, -1.5f);

            // 勾号：**程序生成的真·对勾贴图**（白色 + alpha，颜色由 Image.color 叠加）。
            // 旧版是"一条 45° 旋转的短粗线"，看起来像斜杠（用户反馈要改成对勾）。
            // 走贴图而不是"两个旋转矩形拼"的原因：距离场画出来的边缘自带抗锯齿，
            // 也不会在两段接缝处露出缺口（和滑块圆形手柄是同一套路子，不依赖任何资源）。
            GameObject tick = new GameObject("Tick");
            tick.transform.SetParent(box.transform, false);
            RectTransform trt = tick.AddComponent<RectTransform>();
            trt.anchorMin = new Vector2(0.5f, 0.5f);
            trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(22f, 22f);
            trt.anchoredPosition = Vector2.zero;
            Image tickImg = tick.AddComponent<Image>();
            tickImg.sprite = TickSprite();
            tickImg.color = CheckOn;
            tickImg.raycastTarget = false;

            // 文字
            Text txt = AddText(row.transform as RectTransform, "Label", label, 16, TextColor, TextAnchor.MiddleLeft);
            RectTransform lrt = txt.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(0f, 0.5f);
            lrt.offsetMin = new Vector2(36f, 0f);
            lrt.offsetMax = new Vector2(0f, 0f);

            // 整行可点
            Button b = row.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            Image rowHit = row.AddComponent<Image>();
            rowHit.color = new Color(0f, 0f, 0f, 0f);
            rowHit.raycastTarget = true;
            b.targetGraphic = rowHit;

            CheckBox cb = new CheckBox();
            cb.Root = row;
            cb.Box = boxImg;
            cb.Tick = tick;
            cb.Label = txt;
            cb.Hit = b;
            cb.Value = false;
            cb.Changed = onChanged;
            b.onClick.AddListener(delegate { cb.Set(!cb.Value); });
            return cb;
        }

        internal sealed class CheckBox
        {
            internal GameObject Root;
            internal Image Box;
            internal GameObject Tick;
            internal Text Label;
            internal Button Hit;
            internal bool Value;
            internal bool Disabled;
            internal UnityAction<bool> Changed;

            private static readonly Color BoxOn = new Color(0.12f, 0.14f, 0.18f, 1f);
            private static readonly Color BoxDisabled = new Color(0.11f, 0.12f, 0.14f, 1f);
            private static readonly Color TickDisabled = new Color(0.40f, 0.40f, 0.43f, 1f);
            private static readonly Color TextDisabled = new Color(0.48f, 0.48f, 0.51f, 1f);

            internal void Set(bool v)
            {
                if (Disabled) return;              // 变灰时点不动（按钮本身也已经 interactable=false）
                Value = v;
                Paint();
                if (Changed != null) Changed(v);
            }

            /// <summary>不打回调地设置（供刷新用）。</summary>
            internal void SetSilent(bool v)
            {
                Value = v;
                Paint();
            }

            /// <summary>
            /// 变灰 / 恢复。变灰时**点不动**，方框、勾号、文字一起调暗 ——
            /// 用户一眼就知道"这一项现在不算数"，而不是点了没反应、以为 mod 坏了。
            /// </summary>
            internal void SetInteractable(bool on)
            {
                Disabled = !on;
                if (Hit != null) Hit.interactable = on;
                if (Label != null) Label.color = on ? TextColor : TextDisabled;
                Paint();
            }

            private void Paint()
            {
                if (Tick != null)
                {
                    Tick.SetActive(Value);
                    Image ti = Tick.GetComponent<Image>();
                    if (ti != null) ti.color = Disabled ? TickDisabled : CheckOn;
                }
                if (Box != null)
                    Box.color = Disabled ? BoxDisabled : (Value ? BoxOn : CheckOff);
            }
        }

        private static void AddButton(RectTransform parent, float x, float y, float w,
                                      string label, UnityAction onClick)
        {
            GameObject go = new GameObject("Button_" + label);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            Place(rt, x, y, w, 36f);

            Image img = go.AddComponent<Image>();
            img.color = ButtonBg;
            img.raycastTarget = true;
            AddBorder(rt, FrameColor, 1.5f);

            Button b = go.AddComponent<Button>();
            b.targetGraphic = img;
            ColorBlock cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
            cb.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            b.colors = cb;
            if (onClick != null) b.onClick.AddListener(onClick);

            Text t = AddText(go.transform as RectTransform, "Text", label, 16, TextColor, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform);
        }

        // ================================================================ 刷新

        private static void RefreshAll()
        {
            try
            {
                GenderRatioConfig cfg = Patches.Config;
                if (cfg == null) return;
                GenderRatioSettings s = cfg.settings;
                float step = cfg.stepPercent;
                s.Normalize(step);

                float[] p = s.Percents();
                float pair = 100f - p[2];

                if (_statusText != null)
                {
                    _statusText.text = Loc.T("status.now") + FemaleWord() + " " + GenderRatioSettings.Pct(p[0])
                                     + " / " + MaleWord() + " " + GenderRatioSettings.Pct(p[1])
                                     + " / " + OtherLabel() + " " + GenderRatioSettings.Pct(p[2])
                                     + "\n" + Loc.TF("status.ratio", Ratio(Mathf.RoundToInt(p[0]), Mathf.RoundToInt(p[1])))
                                     + (s.lockRatio ? Loc.TF("status.locked", s.LockedRatio.Text) : Loc.T("status.unlocked"))
                                     + Loc.TF("status.step", step);
                }
                if (_otherValueText != null)
                    _otherValueText.text = Loc.TF("other.value", OtherLabel(), GenderRatioSettings.Pct(p[2]),
                                                  GenderRatioSettings.Pct(100f - step));
                if (_femaleValueText != null)
                    _femaleValueText.text = Loc.TF("fm.value", GenderRatioSettings.Pct(p[0]), GenderRatioSettings.Pct(p[1]),
                                                   OtherLabel(), GenderRatioSettings.Pct(pair));

                // ============================================================ 肖像池状态 + 灰化
                // 文档 6.6 / 决策 10：开启头像替换后，"没有肖像的性别"其实不会被生成，
                // 界面必须**如实反映**，否则勾选框和滑块看起来有效、实际不生效（界面骗人）。
                bool portraitsEnabled = Portrait.PortraitRegistry.Config != null
                                        && Portrait.PortraitRegistry.Config.enabled;
                int cF = Portrait.PortraitRegistry.CountOfGender(Gender.Female);
                int cM = Portrait.PortraitRegistry.CountOfGender(Gender.Male);
                int cO = Portrait.PortraitRegistry.CountOfGender(Gender.NonBinary);
                bool poolEmpty = (cF + cM + cO) == 0;
                bool priority = portraitsEnabled && !poolEmpty;   // 「肖像池优先」是否真的在起作用

                if (_poolText != null)
                {
                    // ★ 一律用 OtherLabel()（语言感知），**不要**用 s.otherLabel ——
                    //   后者是 combined.json 里的中文标签，英文界面下会直接印出中文
                    //   （真机 10/15：池子那两行漏了这一点，英文界面里冒出「非二元性别或无性别」）。
                    string line1 = Loc.TF("pool.line1", cF, cM, OtherLabel(), cO, (cF + cM + cO));
                    string line2;
                    if (!portraitsEnabled)
                    {
                        line2 = Loc.T(poolEmpty ? "pool.off.empty" : "pool.off");
                    }
                    else
                    {
                        string sep = Loc.T("list.sep");
                        string avail = "";
                        if (cF > 0) avail += (avail.Length > 0 ? sep : "") + FemaleWord();
                        if (cM > 0) avail += (avail.Length > 0 ? sep : "") + MaleWord();
                        if (cO > 0) avail += (avail.Length > 0 ? sep : "") + OtherLabel();
                        line2 = Loc.TF("pool.only", avail);
                        if (cO == 0)
                            line2 += Loc.TF("pool.note.other", OtherLabel());
                        else if (cF == 0 || cM == 0)
                            line2 += Loc.T("pool.note.fm");
                        else
                            line2 += Loc.T("pool.note.ok");
                    }
                    _poolText.text = line1 + "\n" + line2;
                }

                // 灰化规则（文档 6.6 的表 + 用户 10/15 的补充意见）
                //   ① 未开替换 ⇒ 什么都不灰（老行为，三性别都按比例）
                //   ② 第三类没有肖像 ⇒ 勾选框 + 该滑块变灰
                //   ③ 女或男缺任一方 ⇒ 此时"男女之间的比例"没有任何意义
                //      （只会生成剩下的那个性别 → 权重被归一成 100%），
                //      所以**女/男 滑块 + 「锁定男女比例」勾选框 + 比例输入框 全部变灰**，
                //      免得用户以为改了会生效、还点了「应用并写回配置」。
                bool otherUsable = !priority || cO > 0;
                bool fmUsable = !priority || (cF > 0 && cM > 0);
                if (_otherCheck != null) _otherCheck.SetInteractable(otherUsable);
                if (_otherSlider != null) _otherSlider.SetInteractable(s.otherEnabled && otherUsable);
                if (_femaleSlider != null) _femaleSlider.SetInteractable(fmUsable);
                if (_lockCheck != null) _lockCheck.SetInteractable(fmUsable);
                if (_ratioWField != null) _ratioWField.interactable = fmUsable;
                if (_ratioMField != null) _ratioMField.interactable = fmUsable;

                // 总开关本身：**一张图都没有时不能打开**（文档 6.4）；
                // 但"已经开着"时始终允许关掉（否则用户就被锁在一个空池子的状态里了）。
                bool portraitToggleUsable = (cF + cM + cO) > 0 || portraitsEnabled;
                if (_portraitCheck != null)
                {
                    _portraitCheck.SetInteractable(portraitToggleUsable);
                    _portraitCheck.SetSilent(portraitsEnabled);
                }

                if (_otherCheck != null) _otherCheck.SetSilent(s.otherEnabled);
                if (_lockCheck != null) _lockCheck.SetSilent(s.lockRatio);

                // 比例输入框：只在用户没在编辑时同步，避免把正在输入的内容冲掉
                if (_ratioWField != null && !_ratioWField.isFocused && _ratioWField.text != s.lockRatioW.ToString())
                    _ratioWField.text = s.lockRatioW.ToString();
                if (_ratioMField != null && !_ratioMField.isFocused && _ratioMField.text != s.lockRatioM.ToString())
                    _ratioMField.text = s.lockRatioM.ToString();

                if (_otherSlider != null)
                {
                    _otherSlider.SetStep(step);
                    _otherSlider.SetMax(s.otherEnabled ? 100f : 0f);
                    // 可交互性已经在上面的"灰化规则"里统一设置（文档 6.6），这里不再覆盖
                    if (Math.Abs(_otherSlider.Value - p[2]) > 0.01f) _otherSlider.SetValue(p[2]);
                }
                if (_femaleSlider != null)
                {
                    _femaleSlider.SetStep(step);
                    // ★ 上限 = 100 - 其他性别 ⇒ 最右边那一段被"锁住"，拖不过去。
                    //   （旧版这里写 100，所以能拖到"女+男=100"、把其他性别挤掉。）
                    _femaleSlider.SetMax(s.FemaleSliderMax);
                    float want = s.FemaleSliderValue;
                    if (Math.Abs(_femaleSlider.Value - want) > 0.01f) _femaleSlider.SetValue(want);
                }
            }
            catch (Exception e) { Log.Warn("刷新窗口数值异常：" + e.Message); }
        }

        private static string Ratio(int f, int m)
        {
            if (f <= 0 && m <= 0) return "-";
            if (f <= 0) return "0 : 1";
            if (m <= 0) return "1 : 0";
            int g = Gcd(f, m);
            return (f / g) + " : " + (m / g);
        }

        /// <summary>
        /// 第三类性别的显示名。取自 combined.json 的 <c>gender_ratio.other_label</c>
        /// （默认「非二元性别或无性别」），所以**窗口里不要再写死**这个名字 ——
        /// 之前窗口里三处写死、状态行却用配置值，同屏出现两种叫法，很容易看糊涂。
        /// 窗口每次打开都会重建，所以在 Build 时取一次就够了。
        /// </summary>
        private static string OtherLabel()
        {
            // 英文（含将来的德/俄）界面下**不用** combined.json 里那个中文标签，
            // 否则窗口里会蹦出「非二元性别或无性别」这种中文词。
            if (!Loc.IsChinese) return Loc.T("word.other");
            try
            {
                GenderRatioConfig cfg = Patches.Config;
                if (cfg != null && cfg.settings != null && !string.IsNullOrEmpty(cfg.settings.otherLabel))
                    return cfg.settings.otherLabel;
            }
            catch { }
            return GenderRatioSettings.DefaultOtherLabel;
        }

        /// <summary>
        /// 当前语言的肖像池摘要（窗口用；日志里仍用 <c>PortraitRegistry.PoolSummary()</c> 的中文版）。
        /// </summary>
        private static string PoolWords()
        {
            int f = Portrait.PortraitRegistry.CountOfGender(Gender.Female);
            int m = Portrait.PortraitRegistry.CountOfGender(Gender.Male);
            int o = Portrait.PortraitRegistry.CountOfGender(Gender.NonBinary);
            return Loc.TF("rescan.pool", FemaleWord(), f, MaleWord(), m, OtherLabel(), o, f + m + o);
        }

        /// <summary>女 / 男的用词：中文界面用 combined.json 里的标签（可自定义），否则用当前语言的词。</summary>
        private static string FemaleWord()
        {
            if (!Loc.IsChinese) return Loc.T("word.female");
            try
            {
                GenderRatioConfig cfg = Patches.Config;
                if (cfg != null && !string.IsNullOrEmpty(cfg.femaleLabel)) return cfg.femaleLabel;
            }
            catch { }
            return "女";
        }

        private static string MaleWord()
        {
            if (!Loc.IsChinese) return Loc.T("word.male");
            try
            {
                GenderRatioConfig cfg = Patches.Config;
                if (cfg != null && !string.IsNullOrEmpty(cfg.maleLabel)) return cfg.maleLabel;
            }
            catch { }
            return "男";
        }

        private static int Gcd(int a, int b) { while (b != 0) { int t = a % b; a = b; b = t; } return a == 0 ? 1 : a; }

        // ================================================================ 动作

        private static void ApplyAndPersist()
        {
            try
            {
                GenderRatioConfig cfg = Patches.Config;
                if (cfg == null) return;
                GenderRatioSettings s = cfg.settings;
                s.Normalize(cfg.stepPercent);

                int[] w = s.Weights();
                Log.Info("[滑块窗口] 比例已更新为 " + s.Describe(cfg.femaleLabel, cfg.maleLabel)
                         + "（权重 女 " + w[0] + " / 男 " + w[1] + " / " + s.otherLabel + " " + w[2] + "）"
                         + " —— 下一位新生成的驾驶员就会按新比例抽取。");

                bool saved = false;
                if (!string.IsNullOrEmpty(GenderRatioMod.ConfigPath))
                    saved = GenderRatioConfig.SaveSettings(GenderRatioMod.ConfigPath, s);

                // 头像替换的总开关也一起写回（两者都在同一个 combined.json 里）
                bool savedPortraits = true;
                if (Portrait.PortraitRegistry.Config != null)
                    savedPortraits = CombinedConfig.SavePortraitsEnabled(Portrait.PortraitRegistry.Config.enabled);

                if (_hintText != null)
                    _hintText.text = Loc.T(saved ? "apply.ok" : "apply.fail")
                                   + s.Describe(FemaleWord(), MaleWord(), OtherLabel())
                                   + (savedPortraits ? "" : Loc.T("apply.portraitFail"));
                RefreshAll();
            }
            catch (Exception e) { Log.Error("应用比例失败：" + e); }
        }

        /// <summary>
        /// ★ 头像替换总开关（M9）。开 = 现在的整套机制（本地图片 + 肖像池优先 + 头像列表=你的图）；
        /// 关 = **完全回到游戏原版肖像机制**，而且性别比例调节**独立运作**、不再受图片有无影响。
        /// </summary>
        private static void SetPortraitReplacement(bool on)
        {
            try
            {
                if (Portrait.PortraitRegistry.Config == null) return;
                Portrait.PortraitRegistry.Config.enabled = on;

                // **立即写回配置**（不像别的控件那样等「应用」）：
                // 这是一个"模式开关"，用户勾完就退出游戏的话，下次启动应当还是这个状态。
                bool saved = CombinedConfig.SavePortraitsEnabled(on);

                Log.Info("勾选框「启用头像替换」-> " + on
                         + (on ? "（头像列表 = 一个格子一张你的图；新驾驶员只生成有替换头像的性别）"
                               : "（完全回到游戏原版肖像机制；性别比例调节独立运作、不受图片有无影响）")
                         + (saved ? "；已写回 combined.json" : "；⚠ 写回 combined.json 失败（见日志）"));

                ClearGamePortraitCache();      // 让已经渲染出来的头像重新渲染一次，立刻看到变化
                RefreshAll();
            }
            catch (Exception e) { Log.Warn("切换『启用头像替换』失败：" + e.Message); }
        }

        /// <summary>
        /// 让游戏把已渲染的头像全部重画一次。
        /// 为什么需要：关掉替换之后，游戏侧 <c>PortraitManager</c> 里还缓存着我们之前交给它的贴图，
        /// 而且 <c>PortraitSettings.IsRendered()</c> 会因此认为"已渲染"而跳过重画 ——
        /// 名册里就会一直留着旧头像。<c>PortraitManager.Clear()</c> 会把它们清掉、按当前设置重画。
        /// </summary>
        private static void ClearGamePortraitCache()
        {
            try
            {
                if (_game == null) return;
                BattleTech.Data.DataManager dm = _game.DataManager;
                if (dm == null) return;
                System.Reflection.PropertyInfo p = typeof(BattleTech.Data.DataManager).GetProperty(
                    "PortraitManager",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic);
                if (p == null) return;
                BattleTech.Portraits.PortraitManager pm =
                    p.GetValue(dm, null) as BattleTech.Portraits.PortraitManager;
                if (pm == null) return;
                pm.Clear();
                Log.Info("已清掉游戏侧的已渲染头像缓存（名册/大图会按当前设置重新渲染一次）。");
            }
            catch (Exception e)
            {
                Log.Warn("清理已渲染头像缓存失败（名册里的旧头像可能暂时留着）：" + e.Message);
            }
        }

        /// <summary>
        /// ★「立即重扫图片池」（M9）：重新扫描 <c>portraits\</c> 目录。
        ///
        /// 为什么需要它：图片池**只在启动时扫描一次**（之后缓存起来给机制层廉价读取），
        /// 所以平时"加图/删图"要重启游戏才生效。这个按钮让你**当场生效**：
        ///   * 重新扫描 → 更新三份性别列表与"未标注"；
        ///   * 清掉游戏侧已渲染的头像缓存 → 名册/大图按新池子重新渲染；
        ///   * 刷新窗口 → 池子张数、灰化规则、以及"能不能开替换"立刻更新。
        /// 创建界面里的头像列表不用管：它每帧对照当前池子，池子一变下一帧就会重算。
        /// </summary>
        private static void RescanPortraits()
        {
            try
            {
                Portrait.PortraitRegistry.RescanPool();
                ClearGamePortraitCache();

                string note = "";

                // ① 重扫后变成"一张图都没有" ⇒ 按文档 6.4 立刻强制关闭替换（并写回配置），
                //    否则会出现"配置说开着、实际什么都换不了"的不一致状态。
                if (Portrait.PortraitRegistry.Config != null
                    && Portrait.PortraitRegistry.Config.enabled
                    && !Portrait.PortraitRegistry.HasAnyPortrait())
                {
                    Portrait.PortraitRegistry.Config.enabled = false;
                    CombinedConfig.SavePortraitsEnabled(false);
                    Log.Warn("重扫后发现一张肖像都没有 ⇒ 头像替换已强制关闭（并写回 combined.json）。");
                    note = Loc.T("rescan.emptyNote");
                }
                // ② 重扫后变成"有图了"、而替换当前是关的 ⇒ 提示用户现在可以勾上（不自动开，明确一点）
                else if (Portrait.PortraitRegistry.Config != null
                         && !Portrait.PortraitRegistry.Config.enabled
                         && Portrait.PortraitRegistry.HasAnyPortrait())
                {
                    note = Loc.T("rescan.canEnable");
                }

                if (_hintText != null)
                    _hintText.text = Loc.T("rescan.done") + PoolWords() + note;

                RefreshAll();
            }
            catch (Exception e) { Log.Error("重新扫描图片池失败：" + e); }
        }

        private static void RestoreDefaults()
        {
            GenderRatioConfig cfg = Patches.Config;
            if (cfg == null) return;
            cfg.settings.otherEnabled = true;
            cfg.settings.otherPercent = 10f;
            cfg.settings.femalePercent = 46f;
            cfg.settings.malePercent = 44f;
            Log.Info("已把窗口里的值重置为原版默认：女 46% / 男 44% / 其他 10%（尚未应用）");
            RefreshAll();
        }

        // ================================================================ 勾号贴图（程序生成）

        private static Sprite _tickSprite;

        /// <summary>
        /// 生成一张"对勾"贴图（纯白 + alpha 抗锯齿）。画法是**距离场**：对每个像素求它到
        /// 两段线段的最近距离，距离小于半线宽就填、边缘 1 像素做渐变。
        ///
        /// 两段线（贴图坐标，y 向上，32×32）：
        ///   起点 (4,16) → 折点 (12,7) → 终点 (28,26)
        /// 也就是"短的一撇往下、长的一捺往上"的常用对勾形状。
        /// </summary>
        private static Sprite TickSprite()
        {
            if (_tickSprite != null) return _tickSprite;

            const int N = 32;
            const float X0 = 4f, Y0 = 16f;      // 左端
            const float X1 = 12f, Y1 = 7f;      // 折点（最低处）
            const float X2 = 28f, Y2 = 26f;     // 右上端
            const float Half = 2.8f;            // 半线宽

            Texture2D tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float d = Mathf.Min(SegmentDistance(px, py, X0, Y0, X1, Y1),
                                        SegmentDistance(px, py, X1, Y1, X2, Y2));
                    float a = Mathf.Clamp01(Half - d + 0.5f);       // 半像素渐变 = 抗锯齿
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            _tickSprite = Sprite.Create(tex, new Rect(0f, 0f, N, N), new Vector2(0.5f, 0.5f), 100f);
            return _tickSprite;
        }

        /// <summary>点到线段的距离（纯几何，纯函数）。</summary>
        private static float SegmentDistance(float px, float py, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay;
            float wx = px - ax, wy = py - ay;
            float len2 = vx * vx + vy * vy;
            float t = len2 <= 0f ? 0f : Mathf.Clamp01((wx * vx + wy * vy) / len2);
            float dx = wx - t * vx, dy = wy - t * vy;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        // ================================================================ 构件小工具

        private static RectTransform NewRect(Transform parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<RectTransform>();
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 绝对定位：从**父级左上角**往下量 yFromTop 像素放置元素。
        ///
        /// ⚠️ 坐标约定（被真机日志连续打脸三次才定下来，务必按这个来）：
        ///   父级 local y 向上为正，**父级顶部 = 0，底部 = -H**。
        ///   `anchorMin = anchorMax = pivot = (0,1)`（左上角对齐）时，
        ///   rect 从 anchoredPosition 这个点**向下**展开，即
        ///       rect_y ∈ [anchoredPosition.y - h, anchoredPosition.y]
        ///   想让它的**顶边**落在"距父级顶部 yFromTop"处，就要
        ///       anchoredPosition.y = -(yFromTop + h)
        ///
        /// 历史错误（两版都错在这里，靠 v0.2.12 的位置实测日志才算清）：
        ///   * v0.2.7 写 `-(y)` ⇒ 顶边落在 yFromTop+h 处，整体偏高一个 h；
        ///   * v0.2.9 写 `-(yFromTop + h)` 却又把 pivot 当"底边"理解 ⇒ 反过来又低一个 h；
        ///     真机实测：Title 摆在 20..60，日志显示 60..100 —— 正好低一个 h。
        ///   ⇒ 实测数据（`DescribeBounds`）是唯一可靠依据，别再靠脑内推演锚点行为。
        /// </summary>
        private static void Place(RectTransform rt, float x, float yFromTop, float w, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            // 实测结论：顶边 = |anchoredPosition.y|，所以这里直接用 -(yFromTop)
            rt.anchoredPosition = new Vector2(x, -yFromTop);
        }

        private static Image NewImage(RectTransform parent, string name, Color color)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            Image img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>四边细线框。</summary>
        private static void AddBorder(RectTransform target, Color color, float thickness)
        {
            AddEdge(target, color, thickness, new Vector2(0f, 1f), new Vector2(1f, 1f));
            AddEdge(target, color, thickness, new Vector2(0f, 0f), new Vector2(1f, 0f));
            AddEdge(target, color, thickness, new Vector2(0f, 0f), new Vector2(0f, 1f));
            AddEdge(target, color, thickness, new Vector2(1f, 0f), new Vector2(1f, 1f));
        }

        private static void AddEdge(RectTransform target, Color color, float thickness,
                                    Vector2 aMin, Vector2 aMax)
        {
            Image img = NewImage(target, "Edge", color);
            RectTransform rt = img.rectTransform;
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = new Vector2(0.5f, 0.5f);
            bool horizontal = Math.Abs(aMax.y - aMin.y) < 0.001f;
            rt.sizeDelta = horizontal ? new Vector2(0f, thickness) : new Vector2(thickness, 0f);
            rt.anchoredPosition = Vector2.zero;
        }

        private static Text AddText(RectTransform parent, string name, string content, int size,
                                    Color color, TextAnchor anchor)
        {
            return AddText(parent, name, content, size, color, anchor, false);
        }

        /// <summary>
        /// noWrap = true ⇒ **不自动换行**。
        /// 单行的"数值 / 提示"行必须用它：那些文字的 rect 只有一行高，
        /// 一旦自动换行，第二行会按 verticalOverflow=Overflow 画到 rect 外面，
        /// 正好**压在下方的勾选框 / 滑块上**（改了显示名长度之后这个风险就实打实存在了）。
        /// 标题与状态行是刻意多行的（用 "\n" 手动断行），保持默认 false ——
        /// 注意 Overflow 只关掉"自动"换行，显式的 "\n" 仍然生效。
        /// </summary>
        private static Text AddText(RectTransform parent, string name, string content, int size,
                                    Color color, TextAnchor anchor, bool noWrap)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            Text t = go.AddComponent<Text>();
            t.font = GetFont();
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = noWrap ? HorizontalWrapMode.Overflow : HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            return t;
        }

        // ================================================================ 字体

        internal static Font GetFont()
        {
            if (_fontResolved && _font != null) return _font;
            _fontResolved = true;

            try
            {
                GenderRatioConfig cfg = Patches.Config;
                if (cfg != null && !string.IsNullOrEmpty(cfg.fontFile))
                {
                    Font f = Font.CreateDynamicFontFromOSFont(cfg.fontFile, 16);
                    if (f != null)
                    {
                        _font = f;
                        Log.Info("字体：使用配置指定的系统字体 '" + cfg.fontFile + "'（不依赖任何 MOD）。");
                        return _font;
                    }
                    Log.Warn("字体：系统里找不到字体 '" + cfg.fontFile + "'，改用其它来源。");
                }
            }
            catch (Exception e) { Log.Warn("加载系统字体失败：" + e.Message); }

            try
            {
                Text[] texts = UnityEngine.Object.FindObjectsOfType<Text>();
                for (int i = 0; i < texts.Length; i++)
                {
                    if (texts[i] != null && texts[i].font != null)
                    {
                        _font = texts[i].font;
                        Log.Info("字体：从场景 UI.Text('" + SafeName(texts[i]) + "') 借到 '" + _font.name + "'。");
                        return _font;
                    }
                }
            }
            catch (Exception e) { Log.Warn("查找场景字体失败：" + e.Message); }

            try
            {
                _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                Log.Info("字体：使用 Unity 内置 Arial.ttf（实测可显示本窗口的中文）。");
            }
            catch (Exception e) { Log.Warn("加载内置 Arial 失败：" + e.Message); }

            if (_font == null) Log.Error("字体解析失败：窗口文字可能不显示（功能不受影响）。");
            return _font;
        }

        private static string SafeName(Component c)
        {
            try { return c.gameObject.name + " / " + c.GetType().Name; } catch { return "?"; }
        }
    }
}
