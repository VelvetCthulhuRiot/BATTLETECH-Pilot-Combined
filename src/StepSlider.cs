using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PilotCombined.GenderRatio
{
    /// <summary>
    /// 「步进滑块」——用 Unity 原生 <see cref="UnityEngine.UI.Slider"/> 封装成**圆形手柄**的样子，
    /// 并只暴露本项目需要的东西（数值、范围、步长、开关、回调）。
    ///
    /// v0.2.7 起**不再复用游戏自带的滑块预制体**。原因（真机反复实测）：
    ///   * 游戏控件的方括号/文字标签是用 `NestedPrefabAnchor` **异步注入**的，
    ///     实例化那一刻还不存在，同步扫不到、也剔不干净；
    ///   * 而这些配件一旦漏进来，就会和我们的文字叠在一起造成**重影**；
    ///   * 没漏进来时，控件又只剩一条光秃秃的矩形条（原版的圆形手柄在异步注入的那部分里）。
    ///   ⇒ 与其在"注入时机"上赌运气，不如自己画一个**确定性**的滑块：
    ///     圆角轨道 + **圆形手柄**，配色照原版（暖黄手柄 / 蓝灰轨道）。
    ///
    /// 步长吸附只在这一处做，避免 UI 和游戏逻辑两边各取整一次。
    /// </summary>
    internal sealed class StepSlider : MonoBehaviour
    {
        private Slider _slider;
        private RectTransform _track;
        private Image _handleImg;
        private float _step = 1f;
        private float _max = 100f;
        private float _value;
        private UnityAction<float> _onChanged;
        private bool _suppress;

        internal float Value { get { return _value; } }
        internal float Max { get { return _max; } }

        private static readonly Color TrackColor = new Color(0.18f, 0.22f, 0.28f, 1f);
        private static readonly Color FillColor = new Color(0.29f, 0.52f, 0.72f, 1f);
        private static readonly Color HandleColor = new Color(0.949f, 0.647f, 0.196f, 1f);
        private static readonly Color HandleColorOff = new Color(0.45f, 0.45f, 0.48f, 1f);

        /// <summary>
        /// 在 parent 下建一个滑块，尺寸与位置由调用方通过 RectTransform 明确指定（absolutely positioned）。
        /// </summary>
        internal static StepSlider Create(RectTransform parent, string name, float step,
                                          float min, float max, float initial, UnityAction<float> onChanged)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(0f, -10f);
            rt.offsetMax = new Vector2(0f, 10f);

            // ---- 轨道（圆角：用一张纯色 Image 即可）----
            GameObject track = new GameObject("Track");
            track.transform.SetParent(go.transform, false);
            RectTransform trt = track.AddComponent<RectTransform>();
            trt.anchorMin = new Vector2(0f, 0.5f);
            trt.anchorMax = new Vector2(1f, 0.5f);
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.offsetMin = new Vector2(0f, -4f);
            trt.offsetMax = new Vector2(0f, 4f);
            Image trackImg = track.AddComponent<Image>();
            trackImg.color = TrackColor;
            trackImg.raycastTarget = false;
            trackImg.sprite = RoundRect();
            trackImg.type = Image.Type.Sliced;

            // ---- 已填充部分 ----
            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(go.transform, false);
            RectTransform frt = fill.AddComponent<RectTransform>();
            frt.anchorMin = new Vector2(0f, 0.5f);
            frt.anchorMax = new Vector2(0f, 0.5f);
            frt.pivot = new Vector2(0f, 0.5f);
            frt.sizeDelta = new Vector2(0f, 8f);
            frt.anchoredPosition = new Vector2(0f, 0f);
            Image fillImg = fill.AddComponent<Image>();
            fillImg.color = FillColor;
            fillImg.raycastTarget = false;
            fillImg.sprite = RoundRect();
            fillImg.type = Image.Type.Sliced;

            // ---- 圆形手柄 ----
            GameObject handle = new GameObject("Handle");
            handle.transform.SetParent(go.transform, false);
            RectTransform hrt = handle.AddComponent<RectTransform>();
            hrt.anchorMin = new Vector2(0f, 0.5f);
            hrt.anchorMax = new Vector2(0f, 0.5f);
            hrt.pivot = new Vector2(0.5f, 0.5f);
            hrt.sizeDelta = new Vector2(22f, 22f);
            Image hImg = handle.AddComponent<Image>();
            hImg.color = HandleColor;
            hImg.raycastTarget = true;
            hImg.sprite = Circle();
            hImg.type = Image.Type.Simple;

            Slider slider = go.AddComponent<Slider>();
            slider.fillRect = frt;
            slider.handleRect = hrt;
            slider.targetGraphic = hImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.wholeNumbers = false;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = initial;

            StepSlider s = go.AddComponent<StepSlider>();
            s._slider = slider;
            s._track = trt;
            s._handleImg = hImg;
            s._step = step <= 0f ? 1f : step;
            s._max = max;
            s._value = initial;
            s._onChanged = onChanged;

            slider.onValueChanged.AddListener(s.OnSliderChanged);
            s.SetValue(initial);
            return s;
        }

        // ==================================================================
        //  自绘贴图（程序生成，**零资源依赖**）
        //
        //  ★ M1 真机日志实测：Unity 的**内置**皮肤资源在正式版游戏里取不到 ——
        //      "The resource UI/Skin/Knob.psd could not be loaded from the resource file!"
        //      "The resource UI/Skin/UISprite.psd could not be loaded from the resource file!"
        //    两条各出现两次（两个滑块 × 两次尝试），于是手柄的 sprite 为 null，
        //    画出来就是一个**方块**，而且白白往 output_log 里喷 4 条 Unity ERROR。
        //    ⇒ 改成自己生成圆形 / 圆角矩形贴图：确定性最好，日志也干净。
        //      （贴图是纯白的，颜色由 Image.color 叠上去。）
        // ==================================================================

        private static Sprite _circleSprite;
        private static Sprite _roundRectSprite;

        /// <summary>纯白圆形贴图（边缘 1 像素抗锯齿）。</summary>
        private static Sprite Circle()
        {
            if (_circleSprite != null) return _circleSprite;
            const int N = 64;
            Texture2D tex = NewTexture(N, N);
            float r = (N - 1) * 0.5f;
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float dx = x - r, dy = y - r;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - d + 0.5f)));
                }
            }
            tex.Apply();
            _circleSprite = Sprite.Create(tex, new Rect(0f, 0f, N, N), new Vector2(0.5f, 0.5f), 100f);
            return _circleSprite;
        }

        /// <summary>
        /// 纯白圆角矩形贴图，带 9 宫格边框 ⇒ 横向拉多长，圆角都不会变形。
        /// 必须配合 <c>Image.type = Sliced</c> 使用。
        /// </summary>
        private static Sprite RoundRect()
        {
            if (_roundRectSprite != null) return _roundRectSprite;
            const int N = 32;        // 贴图边长
            const float Radius = 12f;// 圆角半径
            const float Border = 13f;// 9 宫格边框宽度（要 &gt;= 半径，且 &lt;= N/2）
            Texture2D tex = NewTexture(N, N);
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    // 圆角矩形的判据：到"向内收 Radius 的那个矩形"的距离
                    float cx = Mathf.Clamp(x, Radius, (N - 1) - Radius);
                    float cy = Mathf.Clamp(y, Radius, (N - 1) - Radius);
                    float dx = x - cx, dy = y - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(Radius - d + 0.5f)));
                }
            }
            tex.Apply();
            _roundRectSprite = Sprite.Create(tex, new Rect(0f, 0f, N, N), new Vector2(0.5f, 0.5f), 100f,
                                             0, SpriteMeshType.FullRect,
                                             new Vector4(Border, Border, Border, Border));
            return _roundRectSprite;
        }

        private static Texture2D NewTexture(int w, int h)
        {
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            return t;
        }

        private void OnSliderChanged(float raw)
        {
            if (_suppress) return;
            float snapped = Snap(raw, _step, _slider.minValue, _max);
            _value = snapped;

            if (Math.Abs(raw - snapped) > 0.0001f)
            {
                _suppress = true;
                _slider.value = snapped;
                _suppress = false;
            }
            if (_onChanged != null) _onChanged(snapped);
        }

        /// <summary>程序化设置数值（不触发回调，避免循环）。</summary>
        internal void SetValue(float v)
        {
            float snapped = Snap(v, _step, _slider.minValue, _max);
            _value = snapped;
            _suppress = true;
            _slider.value = snapped;
            _suppress = false;
            // Slider 自己会摆好 fill/handle，这里不用手动摆
        }

        /// <summary>设置上限（超过上限会被锁住拖不上去）。</summary>
        internal void SetMax(float max)
        {
            if (max < _slider.minValue) max = _slider.minValue;
            _max = max;
            _slider.maxValue = max;
            if (_value > max) SetValue(max);
        }

        /// <summary>禁用/置灰。</summary>
        internal void SetInteractable(bool on)
        {
            if (_slider != null) _slider.interactable = on;
            if (_handleImg != null) _handleImg.color = on ? HandleColor : HandleColorOff;
        }

        internal void SetStep(float step)
        {
            _step = step <= 0f ? 1f : step;
            SetValue(_value);
        }

        /// <summary>把任意值吸附到步长刻度上。</summary>
        internal static float Snap(float v, float step, float min, float max)
        {
            if (step <= 0f) return Mathf.Clamp(v, min, max);
            float snapped = Mathf.Round((v - min) / step) * step + min;
            return Mathf.Clamp(snapped, min, max);
        }
    }
}
