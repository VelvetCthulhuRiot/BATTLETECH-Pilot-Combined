# UI 实现笔记 —— 「用 MOD 添加游戏内 UI 组件」的实证结论

> 本轮（v0.2.0）的所有结论都来自**反射 probe 拿到的真实签名**，不是猜的；
> 文末列出 probe 源码位置，可复现。
> 上一轮的机制部分见 `docs\接管-性别比例MOD.md` 的引述与交接文档。

---

## 1. GenericPopupBuilder —— 路线 1，可用（已实证）

```csharp
// BattleTech.UI.GenericPopupBuilder  (public)
static Create(string title, string body)                 -> GenericPopupBuilder
static Create(GenericPopupType popupType, string body)   -> GenericPopupBuilder
AddButton(string content, Action onPress, bool closeOnPress, PlayerAction hotkey)
AddInput(string inputName, Action<string> callback, string sampleText, bool obscure, bool alphanumeric)
SetInputCharacterLimit(int limit)
SetAlwaysOnTop() / CancelOnEscape() / SetOnClose(Action p) / AddFader(...)
Render() -> GenericPopup
```

**三个容易踩的点：**

1. **`AddInput` 的回调是 `Action<string>`** —— 但交接文档 §8.4 把它列为"待确认"。
   本轮用反射确认了：`AddInput -> GenericPopupBuilder (String inputName, Action<String> callback, String sampleText, Boolean obscure, Boolean alphanumeric)`。
   回调只在**按下自动添加的 Confirm 按钮**时触发（`GenericPopup.HideInput()` 里调用 `Callback.Invoke(_inputField.text)`）。
2. **`AddInput` 内部会自己 `AddButton("Confirm", ...)`**。
   如果你再 `AddButton("Confirm", ...)`，它会在日志里报
   `Attempted to add button '{0}' to popup '{1}' but a button with that content already exists.`
   → 用输入框时**不要**再造一个同名按钮。
3. **`AddButton` 的第 4 个参数不是 `KeyCode?`，而是 `PlayerAction`**（游戏自己的按键抽象）。
   不想要热键就传 `null`。

`GenericPopup` 上可用的收尾动作：`Abort(bool forcePool)`、`Pool(bool resort)`、`ForcePool()`、`OnPooled()`。
**收起弹窗用 `Abort(false)`**（交回对象池），不要自己 `Destroy` —— 它本身是池化对象。

---

## 1.1 别信"名字像什么"：预制体 id 与实际组件的对照（真机实测）

本节最初是"按文件名推测"写的，**推断全错**，现在换成真机实测结果：

| 预制体 id | 名字暗示 | **实际装的组件** | 能不能用 |
|---|---|---|---|
| `uixPrfIndc_BASE_steppedSlider-Element` | `HBSSliderInput` | **`UIWidgets.RangeSlider`**（双手柄 int 区间滑块）+ `RangeSliderSample` | ❌ 语义不符，别用 |
| `uixPrfIndc_BASE_toggleBox` | `HBSToggle` | **`UnityEngine.UI.Toggle`**（挂在子物体 `toggleBox` 上） | ✅ 可复用 |
| `uixPrfSlidr_debugSlider` | 滑块 | `BattleTech.UI.DebugSlider` | ❌ 调试用 |
| `uixPrfPanl_mechWarriorCustomizationPopup` | 整容弹窗 | 内含 **3 个真的 `HBSSliderInput`** | ✅ **原版滑块的正确来源** |
| `uixPrfPanl_portraitCustomizationPopup` | 整容弹窗 | 内含 3 个 `HBSSliderInput` | ✅ 备选 |
| `uixPrfPanl_SIM_contracts-Screen_v2` | 合约屏幕 | 内含 2 个 `HBSSliderInput`（谈判用） | ✅ 备选 |

**获取原版滑块的正确姿势**（详见 §4.3）：实例化整屏预制体 → `GetComponentsInChildren<HBSSliderInput>(true)`
把滑块 GameObject 摘出来 → 销毁那个临时整屏实例。**不要靠猜层级，要扫。**

---

## 1.2 预制体加载的通用姿势（DataManager，签名已实证）

```csharp
DataManager dm = gameInstance.DataManager;
dm.PrecachePrefab(id, BattleTechResourceType.UIModulePrefabs, 2);
// 之后：资源是**异步**加载的，PooledInstantiate 可能先返回 null，要按帧重试
GameObject go = dm.PooledInstantiate(id, BattleTechResourceType.UIModulePrefabs,
                                     null /*Vector3?*/, null /*Quaternion?*/, parentTransform);
```

判断是否就绪：`dm.IsPrefabInPool(id)` / `dm.GetPooledPrefab(id)`。

> ⚠️ 最初实现里用了 10 秒超时兜底：拿不到就退回纯文字显示并**在日志里写明原因**，
> 不静默失败。机制部分不受影响，用户也可以直接改 `genderratio.json`。

### 1.1.1 `HBSSliderInput` 的可用面（编译期实测：哪些能碰、哪些不能）

**能直接用的（public）：**

```
属性 : Value(float) / ValueMin / ValueMax / Step / UseValueLimits / LimitMin / LimitMax
       AllowDragging / Handle / HandleRect / WholeNumberOfSteps
方法 : SetLimits(min,max) / SetBounds(min,max) / SetHandle(v) / Init() / Awake()
       Increase() / Decrease() / UpdateHandle() / UpdateFromInput(pos)
       ValueToPosition(v) / PositionToValue(pos) / GetStartPoint() / RangeSize() / InBounds(v)
事件 : OnValueChanged  (UnityEvent<float>，即 HBSSliderInput+OnChangeEvent)
```

**编译期访问不到（`CS0122: 受保护级别限制`）：**

```
字段 : allowDragging / _value / valueMin / valueMax / step / handle / handleRect / OnValueChanged
方法 : SetValue(float)
```

实操结论：
- 想写值用 **`slider.Value = x`**（public 属性 setter），**不要**用 `SetValue`（protected）。
- 想读 `handle` / `handleRect` / `allowDragging` 只能用反射（本项目仅用于**打诊断日志**，不写）。
- **`HBSSliderInput` 自己实现 `IDragHandler` + `IPointerDownHandler`**（反射确认），
  `HBSSliderHandle` 实现 `IDragHandler`。
  ⇒ **拖拽是组件自带的**，不需要挂 `EventTrigger` 代理拖拽；实例化到 Canvas 下就能拖。

### 1.1.2 `HBSToggle` 的可用面

```
IsChecked() / IsToggled() -> bool       读状态
SetToggled(bool)                        写状态（程序化同步用）
OnClicked : UnityEngine.Events.UnityEvent   ← public 字段，直接 AddListener 拿点击
```
- 它是 `HBSButton` 的子类（`HBSButton -> HBSButtonBase`），**不是** `UnityEngine.UI.Toggle`。
  ⇒ 想拿"checked"别找 `isOn` 字段（不存在），用 `IsChecked()`。
- 点击处理：`OnClicked` 比挂 `EventTrigger` 可靠。但它**不会自动翻转** checked 状态，
  所以在回调里自己 `SetToggled(!IsChecked())`。

### 1.1.3 代码搭面板（不依赖任何预制体）

窗口外壳全部用代码建，没有任何自定义预制体：

```
ModalRoot (全屏 RectTransform)
  └ Window (Image 深色底 + VerticalLayoutGroup + ContentSizeFitter)
      ├ Text (标题 / 状态 / 数值行)
      ├ Row: [Prefab: HBSToggle] + Text
      ├ Row: [Prefab: HBSSliderInput] + Text
      └ Button (Image + UnityEngine.UI.Button)
```

**字体怎么来（交接文档 §2.2 列为难点之一）—— 实测的取法优先级：**

1. `Object.FindObjectsOfType<UnityEngine.UI.Text>()`，从任意一个现成的 `Text.font` 上"借"；
2. 退回 `Resources.GetBuiltinResource<Font>("Arial.ttf")`；
3. 每步都写日志说明借到了哪个字体，拿不到就明确报错。

> 为什么不用 TMPro：`TextMeshProUGUI.font` 是 `TMP_FontAsset` 而不是 `Font`，
> 而 UI 文本用 `UnityEngine.UI.Text` + `Font` 就够，且**内置 Arial 支持中文**，
> 不依赖游戏里的 TMP 字体资源。少一层不确定性。

---

## 2. ⚠️ 真机第一次跑就踩到的坑：**没有 Canvas = 什么都不渲染**

v0.2.0 在真机上按 F9 的现象：**只出现游戏自己的空弹窗（带一个"确认"按钮），我的面板完全看不见。**

日志显示 `窗口骨架已创建`、无任何异常、`output_log.txt` 里也**没有** Unity 的 "No canvas" 警告 —— 但画面里就是没有。

**根因**：我把全屏根节点做成了「`GameObject` + `RectTransform`」，**但没给它 `Canvas`**。
Unity UGUI 的铁律：**`Graphic`（`Image`/`Text`）只有在某个 `Canvas` 的层级之下才会被渲染**。
没有 Canvas，它们既不画也不报错 —— 最糟糕的一类 bug。

**修复（v0.2.1）**：根节点上补齐三件套，并给高排序值盖住游戏 UI：

```csharp
Canvas canvas   = root.AddComponent<Canvas>();
canvas.renderMode      = RenderMode.ScreenSpaceOverlay;
canvas.sortingOrder    = 30000;      // 足够高，盖住游戏界面
canvas.overrideSorting = true;

CanvasScaler scaler = root.AddComponent<CanvasScaler>();
scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
scaler.referenceResolution = new Vector2(1920f, 1080f);   // 与游戏一致的参考分辨率
scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
scaler.matchWidthOrHeight  = 0.5f;

root.AddComponent<GraphicRaycaster>();   // 滑块/按钮要能被点到
```

**顺带修掉的第二个问题：永远先显示能用得上的东西。**
v0.2.0 是「等预制体就绪 → 才建内容」，结果预制体一直没就绪（`Poll` 里 `IsPrefabInPool` 始终 false），
用户看到的就是**完全空**的一个窗口。现在 `Open()` 里**立刻**用纯文字把当前比例渲染出来，
滑块只要加载成功就重建内容替换上去 —— 加载失败也只是"没滑块"，而不是"什么都没有"。
日志里每 2 秒打一次等待进度（`IsPrefabInPool` / `GetPooledPrefab` 的值），10 秒超时后写明原因。

另外注意 `DataManager.GetPooledPrefab` 的返回类型是 **`UnityEngine.Object`** 而不是 `GameObject`
（只有 `PooledInstantiate` 是 `GameObject`）—— 写错过一次，编译器 `CS0266` 直接拦住了。

> 这个坑值得记牢：**任何"代码创建的 UI 看不见"的问题，第一件事就是查它顶上有没有 Canvas。**

---

## 3. ⚠️ 第二次真机：预制体名字骗人，交接文档 §6 那张表这里是错的
### 3.1 真机日志给出的铁证

v0.2.1 的日志里 `DescribeComponents` 把实例结构完整打了出来，于是真相一目了然：

```
预制体 uixPrfIndc_BASE_toggleBox 上没有 HBSToggle，实例结构：
  [RectTransform(uixPrfIndc_BASE_toggleBox(Clone)), ..., 
   RectTransform(toggleBox), **Toggle(toggleBox)**, RectTransform(Background), Image(Background),
   RectTransform(svg_frame), SVGImage(svg_frame), RectTransform(svg_checkmark), SVGImage(svg_checkmark), ...]

预制体 uixPrfIndc_BASE_steppedSlider-Element 上没有 HBSSliderInput，实例结构：
  [RectTransform(...(Clone)), Image(...), LayoutElement(...), RectTransform(Representation),
   RectTransform(negotSlider_layout), ..., RectTransform(negot_RangeSlider),
   **RangeSlider(negot_RangeSlider)**, **RangeSliderSample(negot_RangeSlider)**,
   RectTransform(UsableRange), RectTransform(Fill), div_Line0..4, ...]
```

| 预制体 id | 名字暗示 | **实际装的组件** |
|---|---|---|
| `uixPrfIndc_BASE_toggleBox` | HBSToggle | **`UnityEngine.UI.Toggle`**（在子物体 `toggleBox` 上） |
| `uixPrfIndc_BASE_steppedSlider-Element` | HBSSliderInput | **`UIWidgets.RangeSlider`** + `UIWidgets.RangeSliderSample` |

**⇒ 交接文档 §2.1/§6 里「`HBSSliderInput` 就是`uixPrfIndc_BASE_steppedSlider-Element`」这个说法是错的。**
它是靠 `DebugSlider`/成员名 dump 推断出来的，没有真机验证过。

### 3.2 `HBSSliderInput` 到底在哪

用反射扫了全程序集里"谁有 `HBSSliderInput` 类型的字段"，只有 **8 处**，全部长在**整屏预制体**里：

| 所在类 | 字段 | 所属预制体（PrefabName 属性） |
|---|---|---|
| `SGContractsWidget` | `NegPaymentSlider` / `NegSalvageSlider` | `uixPrfPanl_SIM_contracts-Screen_v2` |
| `SGCharacterCreationPortraitCustomization` | `clothingSlider` / `lightingSlider` / `cameraSlider` | `uixPrfPanl_portraitCustomizationPopup` |
| `SGBarracksMWCustomizationPopup` | 同上三个 | — |

**`HBSSliderInput` 没有任何独立预制体。** 想拿它就得实例化整个合约屏幕 —— 代价和风险都不划算
（那是个整屏 UI，会连带一堆依赖和生命周期问题）。

### 3.3 最终选择（务实且仍然达成验证目标）

| 控件 | 实现 | 理由 |
|---|---|---|
| 勾选框 | **复用游戏自带 `uixPrfIndc_BASE_toggleBox`** → 拿到真正的 `UnityEngine.UI.Toggle` | 真机验证过能加载、能用；这就是"复用游戏 UI 组件"的证明 |
| 滑块 | **`StepSlider`**：Unity 原生 `UI.Slider` 封装（`src\StepSlider.cs`），5% 步长、限位、可禁用 | `HBSSliderInput` 拿不到独立预制体；RangeSlider 是双手柄 int 区间滑块，语义不合适 |

**这仍然完整证明了本项目要验证的那件事**：MOD 可以往游戏里加真正的交互式 UI 组件，
并且可以直接复用游戏自带的 UI 预制体。**不是**"因为做不到才退而求其次"——
是"游戏自带的滑块组件没有可复用的预制体，这个结论本身就是本次实验的一个重要产出"。

### 3.4 `UnityEngine.UI.Toggle` 的坑

- 状态读写是 **`isOn`**（不是 `IsChecked()`），事件是 **`onValueChanged`**（不是 `OnClicked`）。
- **没有 `SetIsOnWithoutNotify`**（那是 Unity 2019+ 的 API，本作是 2018.4）。
  ⇒ 程序化写 `isOn` 会触发 `onValueChanged`，必须自己用 `_suppressToggle` 标志防回环。
- 它挂在**子物体**上，所以要用 `GetComponentInChildren<Toggle>(true)`，用 `GetComponent` 拿不到。

---

## 4. ⚠️ 第三次真机：三个问题一次解决（v0.2.3）

用户反馈（截图 + 描述）：**① 勾选框错位且点不到；② 出现了两个窗口**（一个空的只有"确认"，
一个有滑块和勾选框）；**③ 想要游戏设置菜单里那种原版滑块 + 原版画风**。

### 4.1 「两个窗口」的原因与修复

v0.2.1 起我用 `GenericPopupBuilder.Create("", "")` 弹了个空弹窗**当模态背景**（想蹭它的变暗效果和
ESC 关闭）。`GenericPopup.Render()` 会真的实例化一个完整弹窗并显示标题、正文和它自动加的
Confirm 按钮 —— 于是屏幕上就是**两个窗口**。

**修复**：彻底不再调用 `GenericPopupBuilder`。自己画一个遮罩就行：

```csharp
GameObject dim = ...;                       // 全屏
Image dimImg = dim.AddComponent<Image>();
dimImg.color = new Color(0f, 0f, 0f, 0.55f);
dimImg.raycastTarget = true;                // 挡住下层点击 → 依然是模态
```

**教训**：`GenericPopupBuilder` 只适合"弹个提示框"；要往窗口里塞自定义控件，就不要用它当外壳。

### 4.2 勾选框错位且点不到的原因与修复

v0.2.2 里我做了这件事：

```csharp
RectTransform tgRt = tg.transform as RectTransform;
if (tgRt != null) tgRt.sizeDelta = new Vector2(28f, 28f);   // ← 罪魁祸首
```

`uixPrfIndc_BASE_toggleBox` 的实例**自带 `LayoutElement` / 布局信息**（它里面有
`RectTransform(toggleBox)` + `Toggle` + `Image(Background)` + `SVGImage(svg_frame)` + `SVGImage(svg_checkmark)`），
我硬改 `sizeDelta` 把它的布局搞乱了 —— 结果就是**画面上错位**，而且可点击区域（`Graphic.raycastTarget`
的实际矩形）也跟着变形到几乎点不到。

**修复**：**不再改预制体实例的 sizeDelta**。只设锚点让它靠左居中，并保证最小可点尺寸：

```csharp
trt.anchorMin = new Vector2(0f, 0.5f);
trt.anchorMax = new Vector2(0f, 0.5f);
trt.pivot     = new Vector2(0f, 0.5f);
trt.anchoredPosition = Vector2.zero;
if (trt.sizeDelta.x < 24f) trt.sizeDelta = new Vector2(24f, 24f);   // 只补"太小"，不改"太大"
toggle.interactable = true;
```

> **通用原则**：复用游戏预制体时，**布局交给它自己**。你只负责把它放进一个尺寸合理的宿主行，
> 不要动它的 `sizeDelta` / `localScale`。这是本项目第二次在"手贱改布局"上吃亏。

### 4.3 原版滑块：终于找到正确的来源

用户想要设置菜单里"攻击镜头 / 越肩瞄准镜头"那种滑块。截图里的那些就是 **`HBSSliderInput`**。
它没有独立预制体，但**长在整屏预制体里**——所以办法是：**实例化那个整屏预制体，把滑块 GameObject 摘出来**。

持有 `HBSSliderInput` 的类与它们的预制体（反射扫全程序集得到，共 4 处）：

| 类 | 字段 | 预制体（`PrefabName` 属性） |
|---|---|---|
| `SGBarracksMWCustomizationPopup` | `clothingSlider` / `lightingSlider` / `cameraSlider` | **`uixPrfPanl_mechWarriorCustomizationPopup`** |
| `SGCharacterCreationPortraitCustomization` | 同上三个 | `uixPrfPanl_portraitCustomizationPopup` |
| `SGContractsWidget` | `NegPaymentSlider` / `NegSalvageSlider` | `uixPrfPanl_SIM_contracts-Screen_v2` |

提取方式（**刻意不依赖层级猜测**，见 `src\PrefabWidgetSource.cs`）：

```csharp
GameObject screen = dm.PooledInstantiate(prefabId, UIModulePrefabs, null, null, null);
screen.SetActive(false);                                    // 别让它 Start 抢焦点
HBSSliderInput[] all = screen.GetComponentsInChildren<HBSSliderInput>(true);   // ← 扫，不猜
foreach (...) { slider.transform.SetParent(myRow, false); }  // 摘出来
UnityEngine.Object.Destroy(screen);                          // 摘完销毁整屏实例
```

组件、图片、配色、字体全跟着 GameObject 走，所以外观就是原版。

> 注意：**设置菜单本身就是这条路走不通的例子** —— `BattleTech.UI.SettingsMenu`
> 类里**没有任何** slider/toggle 字段（它是外壳），真正的控件在嵌套的
> `GameSettingsModule` / `AudioSettingsModule` 等模块类里。所以别去克隆设置菜单。

### 4.4 顺带修正的另一个认知

`BattleTech.UI.SettingsMenu` 的 `PrefabName` = `uixPrfPanl_settingsMenu`。
但它的滑块/勾选框字段全在子模块类里（`GameSettingsModule.OverShoulderCamToggle` 等），
所以"实例化设置菜单来拿控件"这条路**不可行**——控件不在它这个 prefab 的层级里。

---

## 5. ⚠️ 第四次真机：勾选框为什么看不见 + 原版控件到底在哪（v0.2.4）

用户反馈：滑块能拖（**改全女 → 新建存档验证完全正确 ✅**），但
**两个勾选框不出现、也点不了**；并且希望窗口外壳也用原版美术。

### 5.1 终于搞清楚"原版控件"分别是什么（本轮最重要的产出）

之前我一直在错误的预制体里找东西。把设置菜单各页的宿主类 dump 出来后，真相是：

| 控件 | 设置菜单里的实现 | 所在预制体 |
|---|---|---|
| 勾选框 | **`BattleTech.UI.HBSToggle`** | `uixPrfPanl_STG_gameModule`（12 个）/ `uixPrfPanl_STG_controlsModule`（4 个） |
| 音量等滑块 | `UIWidgets.CenteredSlider` | `uixPrfPanl_STG_audioModule` / `_videoModule` / `_gameModule` |
| 角色整容滑块 | `HBSSliderInput` | `uixPrfPanl_mechWarriorCustomizationPopup` 等 3 个整屏预制体 |

所以：
* **勾选框**要用 `HBSToggle`（从 `uixPrfPanl_STG_controlsModule` / `_gameModule` 里扫出来摘走）；
  它**不是** `UnityEngine.UI.Toggle`，也**不在** `uixPrfIndc_BASE_toggleBox` 里（那里面只是 UI.Toggle）。
* **滑块**继续用 `HBSSliderInput`（从整容弹窗整屏预制体里摘），已验证外观/手感都对。

### 5.2 `HBSToggle` 的状态机制（必须按它的规矩来）

反汇编看得很清楚（`tools\probe\Probe10.cs`）：

```
IsChecked()  -> IsToggled()
IsToggled()  -> HBSButtonBase.buttonState.IsState(Selected)     ← 状态在 FSM 里！
SetToggled(b)-> HBSButtonBase.SetState(...)
OnPointerUp -> ... OnClicked.Invoke()                           ← 点击事件是 OnClicked（无参数）
ButtonState 枚举 = { Enabled, Selected, Highlighted, Pressed }
```

**所以**：状态**不是**一个 bool 字段，而是 `buttonState` 这个 4 态 FSM 的 `Selected` 态。
读写必须走 `IsToggled()` / `SetToggled(bool)`，点击必须监听 **`OnClicked`**（不是 `onValueChanged`）。
这就解释了我之前用 `UnityEngine.UI.Toggle` 的思路为什么别扭 —— 组件用错了。

### 5.3 勾选框"看不见 + 点不到"的两个原因

1. **用了错的组件**：`uixPrfIndc_BASE_toggleBox` 里是 `UI.Toggle`，样式与原版不一致。
2. **布局没有重建**：`RebuildContent` 结束时我只重建了**窗口**的布局，
   **没有重建 `_contentRoot`**。新挂进来的控件因此可能停留在 **0 尺寸** ——
   而 0 尺寸的 `Graphic` 既不绘制、也没有可点击区域，表现就是"完全看不见、也点不到"。

**修复**：
```csharp
LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRoot.GetComponent<RectTransform>());
LayoutRebuilder.ForceRebuildLayoutImmediate(_window.GetComponent<RectTransform>());
```
再加上给控件补 `LayoutElement`（`min/preferred = 28x28`，`flexibleWidth = 0`），
并显式检查/修正 `activeSelf` 与 `CanvasGroup`（整屏预制体摘出来的控件可能带 inactive 状态）。

### 5.4 新增排障武器：布局诊断

`PrefabWidgetSource.DescribeLayout(go)` 会把一棵 UI 子树里每个节点的
**实际 rect 尺寸 / 锚点 / Graphic / CanvasGroup / Selectable** 打出来。
排查"看不见 / 点不到"时先看它 —— **0 尺寸**几乎总是元凶。
（本轮已接入日志：挂载原版控件后会打印勾选框与滑块的布局。）

### 5.5 窗口外壳的原版美术（配置可关）

新增 `popup.use_game_chrome`。开启时 `TryAdoptGameChrome()` 会：
1. 优先取**活动场景里现成的 `GenericPopup` 实例**（它就是 `uixPrfPanl_GenericPopup`），
   否则从对象池实例化一个；
2. **扫**出其中面积最大的 `Graphic` 当背景板（不猜层级）；
3. 摘到我们窗口下铺满、`SetAsFirstSibling()`、`ignoreLayout`、`raycastTarget=false`；
4. 销毁临时实例。

失败不影响功能，日志会写明原因；效果不理想就把 `use_game_chrome` 改 false 回到纯自绘外观。

### 5.6 字体：**不依赖汉化 MOD**（实测证据 + 授权结论）

部署日志里写的是 `字体：使用 Unity 内置资源 Arial.ttf`，**并没有用到汉化 MOD 的字体**，
所以**不存在对 BTHanHua 的依赖**。

关于版权：你的汉化 MOD 用的 **Noto CJK 是 SIL Open Font License 1.1**，
允许免费使用、修改、**嵌入与再分发**（唯一要求：不得单独售卖字体本身、保留版权声明）。
所以把 Noto CJK 随本 mod 一起分发**没有法律风险**。

> ⚠️ 但有个**技术**限制：**Unity 运行时无法直接从磁盘加载 ttf 文件**。
> `Font.CreateDynamicFontFromOSFont` 要的是**系统已安装的字体名**（不是路径）；
> 从文件加载必须做成 AssetBundle —— 那就要 Unity 2018.4 编辑器，正是本项目一直避免的。
> 因此 `popup.font_file` 只支持**系统字体名**（例如 `Noto Sans CJK SC` / `Microsoft YaHei`），
> 留空则自动"从游戏界面借 → 退回内置 Arial"。

---

## 6. ⚠️ 第五次真机：重影、锁定语义、步长、不透明度（v0.2.5）

用户反馈四件事，其中两件是我的 bug。

### 6.1 「作战镜头控制选项」等游戏设置菜单文字混进窗口 + 字体重影

v0.2.4 我实现了 `TryAdoptGameChrome()`：**从活动场景里找现成的 `GenericPopup` 实例**，
再"扫出面积最大的 Graphic 摘到窗口当背景板"。

**这个做法从根上就是错的**：
* 那个 `GenericPopup` 实例是**游戏自己正在用的活实例**（例如"还有未保存的改动"警告框）。
  把它的子节点摘走 ⇒ 既破坏了它的布局（游戏自己的面板文字于是留在屏幕上，看着像**重影**），
  又把我误认为"背景板"的节点（其实是整个弹窗的视觉容器）搬进了我的窗口 ⇒
  连带把**别的界面**（作战镜头设置模块）的节点也一起漏了进来。

**修复：彻底不做 chrome adoption。** `use_game_chrome` 保留但已废弃（默认 false），
窗口外壳改为**纯自绘 + 不透明**。

> **教训**：**永远不要碰游戏正在使用的活实例。**
> 要预览预制体，就用 `PooledInstantiate` 拿一个**非激活的私有副本**（本项目摘滑块/勾选框就是这么做的），
> 摘完 `Destroy`。v0.2.4 是唯一一次图省事去用活实例，结果就翻车。

### 6.2 「锁定男女比例」锁错了东西（数学 bug）

v0.2.4 的模型是"存 femalePercent，男性 = 余量"。锁定时我把 `femalePercent` 钉死不变 ⇒
用户看到的正是"**保持的是女性的比例数值不变，而不是女/男的比值不变**"。用户描述得完全准确。

**修复**：几何模型改成**存三个百分比**（女 / 男 / 其他），锁定 = 按 **女:男 的比值**等比缩放：

```csharp
// SetOtherPercent(value, keepRatio)
float pair = femalePercent + malePercent;
float ratioF = femalePercent / pair;          // 女:男 的比值
otherPercent = newOther;
femalePercent = newPair * ratioF;             // 等比缩放，比值保持不变
malePercent   = newPair - femalePercent;
```

脱机单测新增直接断言：`女30/男60`（1:2）在锁定下调其他性别后，`女/男` 恒为 `0.5`。

### 6.3 「女 / 男」滑块的读数语义（顺手讲清）

因为锁的是"比值"，滑块读数也统一成速率式：

```
滑块读数 x = 女性占 (女+男) 的百分比
女性绝对占比 = (女+男) × x/100
例：其他=10%（女+男=90），滑块 33.3% → 女 30% / 男 60%
```

`HBSSliderInput` 的数值字段实测是 **`Single`**（`_value / valueMin / valueMax / step` 全是 float），
所以 **1% 步长完全没问题**。`step_percent` 默认已从 5 改为 **1**。

### 6.4 背景不透明度 → 100%

`PanelBg = (0.039, 0.047, 0.063, 1f)` —— **alpha = 1**，完全不透明。
另外按原版风格加了两样（都用细 `Image` 拼，不依赖预制体）：
* 面板四周的**方角细线框**（`AddFrame`，4 条边）；
* 标题下方的**暖黄强调线**（`AddAccentLine`，原版强调色 `#F2A632`）。

### 6.5 按钮也做成原版风格

三个按钮现在是：**深底 + 原版描边色细线框 + 居中文字 + 悬停变亮**
（`Button.colors.highlightedColor` 调到 1.35 倍）。配色取自原版
（底色 `#161920`、描边 `#8B7A52`），仍不依赖预制体。

> 为什么没直接复用游戏的按钮预制体：`HBSButton` 的状态要走 `buttonState` FSM，
> 还依赖 `parentUIModule`、音效事件、`UIColorRefTracker` 等一串宿主环境；
> 从整屏预制体里摘一个"能用的按钮"风险明显高于收益。
> 若你希望按钮连斜纹底都完全原版，告诉我，我按 §4.3 的办法从含按钮的整屏预制体里扫一个出来。

---

## 7. ❌ 结论：复用游戏控件这条路**放弃了**（v0.2.7）

用户在 v0.2.6 后反馈：重影没了，但**勾选框又不可用、滑块变成光秃秃的矩形**。于是决定：
**不再复用游戏预制体，全部自绘。**

### 7.1 为什么放弃 —— 一条根因解释所有现象

真机日志最终暴露的机制：**游戏控件的"配件"是 `NestedPrefabAnchor` 异步注入的**。

```
摘出滑块 [0] 'panSlider'  [... RectTransform(brackets), NestedPrefabAnchor(brackets),
                              DOTweenAnimation(brackets) x5, CanvasGroup(brackets)]
从 uixPrfPanl_STG_controlsModule 里找到 0 个 HBSToggle
```

也就是说，一个滑块/开关元素在**刚实例化的那一刻**只有核心组件（轨道、Fill、Handle 本体），
**方括号装饰、标题文字（"镜头平移""反转轴"）、圆形手柄外观**都要等注入才出现。由此：

| 现象 | 原因 |
|---|---|
| 扫到 0 个 `HBSToggle` | 开关还没被注入进来 → 只能退化用后备的 `toggleBox`（所以你说"原版勾选框不可用"） |
| 滑块变成矩形条 | 圆形手柄的外观在异步注入的那部分里，没注入就没有 |
| 出现重影 | 等注入完了，那些**带文字**的装饰也一起进来了，叠在我的标签上 |
| 剔不干净 | 装饰是异步到达的，任何"同步剔一次"的做法都会漏 |

**这不是"做不到"，而是"要和引擎的注入时机赛跑"。** 试过预热（激活+挪出屏幕+alpha=0，等 3 秒）、
补剔时间窗（挂载后 3 秒内每帧重剔），仍然不稳定。用户也明确说"如果很难就先不用了"。
⇒ **判定为死路，止损。**

### 7.2 改用的方案：全自绘 + 绝对定位

| 方面 | v0.2.6 及以前 | v0.2.7 |
|---|---|---|
| 滑块 | 摘游戏的 `HBSSliderInput` | `StepSlider` 自绘：圆角轨道 + 已填充段 + **圆形手柄**（内置 `UI/Skin/Knob.psd`），配色照原版 |
| 勾选框 | 摘 `HBSToggle` / `UI.Toggle` | `CheckBox` 自绘：方框 + 描边 + 旋转短粗线做勾号，**整行可点** |
| 布局 | `VerticalLayoutGroup` + `ContentSizeFitter` | **绝对定位**（`Place(rt, x, y, w, h)` 锚左上、pivot 左上、y 向下） |
| 依赖 | 需要游戏预制体 | **零预制体依赖** |

### 7.3 为什么布局也要换掉

`VerticalLayoutGroup` 的自动计算正是之前两个顽固 bug 的根源：
* **"标题贴在黄线上"** —— 装饰线用了 `ignoreLayout`，布局组算高度时把它忽略掉，
  于是后面的元素被排到了它上面；绝对定位没有这个问题。
* **"勾选框被压成 0 尺寸、点不到"** —— 布局组把实例算成了 0 宽/0 高，
  而 0 尺寸的 `Graphic` 既不画也点不到。

现在每个元素的 y 坐标都写死在代码里，**所见即所得**；
窗口尺寸、行距、内边距都是常量（`WinW/WinH/PadX`），要调就改这几个数。

### 7.4 配色仍然照原版

自绘不等于不像原版：深底 `#0B0D11`、描边 `#8B7A52`、强调/手柄暖黄 `#F2A632`、
文字米白 `#E1E6E6`，跟 HBS 的设置菜单是一个色系。**不做**原版的斜纹底（那需要贴图资源）。

---

## 8. ⚠️ v0.2.8：绝对定位的**坐标约定**踩坑（界面整体上下颠倒）

v0.2.7 第一次用绝对定位，结果整个界面**上下镜像**、元素全跑到面板外（用户："更离谱了"）。

**根因是锚点与坐标符号不匹配。** UGUI 里父级 local y 的范围是 **[-H, 0]**
（y 轴向上为正，**父级顶部对应的 local y 是 0，底部是 -H**）。而我写的是：

```csharp
rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);   // 左上角锚点
rt.pivot     = new Vector2(0f, 1f);                  // 左上角 pivot
rt.anchoredPosition = new Vector2(x, -y);            // ← 错！当时调用方传的是"屏幕中心向下"的坐标
```

`anchoredPosition.y = -y` 意味着"在父级**底边**下方 y 像素处"，
所以元素不但跑出面板，还整体上下颠倒。

> ⚠️ 这一节后面我**又错了两次**，最终结论在 §10 与 `Place()` 的注释里：
> 正确写法就是 **`anchoredPosition = (x, -yFromTop)`**（rect 顶边距父级顶部 = |anchoredPosition.y|）。
> 当时写的 `-(yFromTop + h)` 会让每个元素**低一个自身高度**。
> 别再照本节下面的旧代码改，以 §10 为准。

> **另一条教训**：`Place()` 这类工具函数的**参数语义必须写进注释**，
> 而且调用方传的值和函数内部假设的坐标系必须严格一致。
> v0.2.7 里调用方传的是"屏幕中心向下（`-y`）"，函数期望的是别的，
> 两边都没错、合起来就是错 —— 这类 bug 靠读代码很难发现，一定要在日志里打出**实际数值**。

所以现在 `Build()` 结尾会打一行：`窗口已创建：760x520，内容底边距面板底部 Npx`，
N 为负数就说明溢出，一眼可见。

---

## 9. ⚠️ v0.2.9→v0.2.10：坐标修复要**全部调用点**，以及"标题压线"的真正原因

### 9.1 v0.2.8 的修复只做了一半（漏了 7 处）

v0.2.8 我用正则批量把坐标从"屏幕中心向下"换算成"面板顶部向下"，
**但正则只匹配 `Place(...)` 直接调用**，漏掉了 7 处间接调用：

```csharp
AddCheck(win, "OtherCheck", PadX, -y, ...)     // ← 仍在传旧坐标系的值
AddSlider(win, "OtherSlider", PadX, -y, ...)
AddButton(win, PadX, -y, ...)
```

这些 helper 内部再把这个值交给 `Place()` ⇒ **一个界面里同时存在两套坐标系**，
于是勾选框/滑块/按钮按旧系摆放、标题/文字按新系摆放，画面就乱成"上半截在面板外、下半截倒过来"。

**教训**：
1. 批量替换必须**先列出所有间接调用**（传参给同一 helper 的地方），不能只替换直接调用。
2. **不要让一个变量身兼两种含义**。原代码 `float y = WinH/2f; y -= 30f; Place(..., WinH/2f + y, ...)`
   里 `y` 既像"距中心"又像"距顶部"，调用点稍不注意就错。
   v0.2.10 改成 `float yTop = 26f;`，**全流程只有一个含义：从面板顶部往下多少像素**，
   每步显式 `yTop += 高度 + 间距`。

### 9.2 "标题压黄线"的真正原因：**文字溢出 rect**

标题 rect 只给了 32px 高，而 24 号字的行高比它大 ⇒ 字形从 rect 下沿**溢出**，压到 6px 下方那条黄线上。
**不是摆放错了**（摆放的 y 是对的），是**容器太矮**。

修复：标题 rect 给足 `TitleH = 40f`，黄线再单独留 `TitleGapToLine = 6f`。
现在标题 rect 是 26..66，黄线在 72。

> 同类陷阱：`Text` 的 rect 高度必须 ≥ 字号 × 行高，否则字形溢出到相邻元素上。
> 遇到"字压到别的东西上"，先怀疑是不是**容器太矮**，而不是位置排错。

### 9.3 新增：运行时**位置实测**日志

为了不再靠截图猜，`Build()` 结束时会把面板里每个子元素**实际占用的矩形**打出来：

```
面板内元素实际位置（x1,y1)-(x2,y2)，单位=面板内像素，y 向下：
    Title               y 26..66     x 34..726
    AccentLine          y 72..74     x 34..726
    Status              y 88..132    x 34..726
    ...
```

它用 `RectTransform.GetWorldCorners` + `InverseTransformPoint(panel)`，
量到的是**渲染时的真实位置**，不受锚点/pivot 理解偏差影响。
**位置再不对，看这段日志就能立刻分辨是"摆放错了"还是"文字溢出/换行"。**

### 9.4 脱机布局核算（改布局前先跑一遍）

布局是一串常量累加，所以可以在动手前用脚本算一遍，确认**行区间严格递增且不重叠**：

```
标题 26..66 | 黄线 72..74 | 状态 88..132 | 勾选框1 148..178 | 其他值 188..210
其他滑块 216..242 | 男女值 256..278 | 男女滑块 284..310 | 勾选框2 326..356
按钮 372..408 | 提示 424..446        内容底边 446，WinH=480 → 余 34px
```

---

## 10. ✅ v0.2.12→v0.2.13：靠**实测日志**一次性定位两个真问题

用户贴回日志后，两个悬了很久的问题**当场算清**，不再需要猜。

### 10.1 真问题一：`CanvasScaler` 把界面缩放了 0.5 倍

v0.2.10 的日志：
```
窗口已创建：760x480，内容底边距面板底部 34px
面板内元素实际位置（y 向下）：
    AccentLine   y -166..-164
    Title        y -174..-134
```
面板 rect 报出来是 480 高（尺寸没错），但 **Title 只占 20**（我设 40）——
一切都是 **0.5 倍**。

原因：`CanvasScaler` 用 `ScaleWithScreenSize` + 参考分辨率 1920×1080，
在用户屏幕上算出 `canvasScale ≈ 0.5`，于是**整套按像素写死的布局被整体压扁一半**：
窗口物理尺寸变成 380×240，我辛苦调的 20px 间距实际只有 10px，所以怎么调都觉得挤。

**修复**：改用 **`ConstantPixelSize` + `scaleFactor = 1`** —— 一像素就是一像素，
坐标常量在任何分辨率下含义一致。

> **教训**：只要布局是"按像素写死"的，就**不要**让 `CanvasScaler` 再去缩放它。

### 10.2 真问题二：`Place()` 的 y 偏移多算了一个 h

v0.2.12 日志（屏幕 2560×1440，缩放已是 1:1，数据干净）：

| 元素 | 我摆的位置 | 日志实测 | 偏移 |
|---|---|---|---|
| Title | 20..60 | 60..100 | **+40 = h** |
| AccentLine | 80..82 | 82..84 | **+2 = h** |
| Status | 104..148 | 148..192 | **+44 = h** |

规律极干净：**实测顶边 = 我想要的顶边 + 元素高度**。
我当时写的是 `anchoredPosition.y = -(yFromTop + h)`，
而实测说明"顶边 = |anchoredPosition.y|"，所以正确公式就是 **`-(yFromTop)`**，`+h` 是多余的。

改完后**所有 11 个元素都精确落在设计值上**（脚本核算，全部 MATCH）：

```
Title 20..60 | AccentLine 80..82 | Status 104..148 | OtherCheckRow 170..200
OtherValue 216..238 | OtherSliderRow 252..278 | FemaleValue 304..326
FemaleSliderRow 340..366 | LockCheckRow 392..422 | Buttons 440..476 | Hint 490..512
内容底边 512，WinH 540 → 余 28
```

> **元教训**：锚点/pivot 的"脑内推演"我连续错了三次（v0.2.7 镜像、v0.2.9 低一个 h、
> 中间还错误怀疑过文字行高）。
> **唯一可靠依据是运行时实测**：`GetWorldCorners` + `InverseTransformPoint`，
> 换算成"距父级顶部"打印。`DescribeBounds()` 就干这个 —— 以后布局问题**先看它**。
> 它自己也修过一次：第一版直接输出父级 local y（负数）很难读，
> 现在统一换算成"从顶部往下的正数"，能直接和代码里的常量对照。

---

## 11. ✅ v0.2.14：锁定比例改为**整数精确存储 + 手动输入**（修掉漂移）

用户报的现象："勾选锁定后，如果其他性别调到一个不好保持的数，固定的读数会变，
滑块取小数试图接近该比值，有时无法完全达到，比例就浮动。"

### 11.1 漂移的根因

旧实现**每次从当前滑块读数反推比例**：

```
ratioF = femalePercent / (femalePercent + malePercent)   // ← 从滑块读出来的！
```

而滑块只能表达**整数百分比**，能实现的女:男 永远差理想值一点点；
每次反推都把这个"差一点"当成**新比例** ⇒ 比例自己一步步走掉。
这是**设计错误**，不是调参问题，所以只调间距/步长都治不好。

### 11.2 新设计：比例是整数，滑块只负责"接近"

* 比例用 **`Ratio`（整数对）** 精确存储，**永不从滑块反推**；
* 换算：`F = round(pair * W / (W+M))`，`M = pair - F`（用 1% 粒度尽量接近）；
* 用户**手动输入**比例（两个输入框夹一个冒号），支持 `23:22` / `46:44`（=23:22，自动约分）/
  `3：2`（全角冒号）/ `女3:男2` / `3/2` / 纯数字（视为 w:1）；
* 用户**直接拖男女滑块**时才更新比例（`CaptureRatioFromCurrent()`，取整成正整数对）。

脱机单测直接压这条：**20 轮 × 61 次改动其他性别**，断言比例恒为 `23:22`、
且每次回到 other=10 都是同一组百分比（46/44）—— 之前会漂，现在不漂。

> 一个容易误解的点：比例 `23:22` 在 pair=90 时**不可能**精确实现，
> 最接近的是 46/44（而 45/45 属于 `1:1`，是另一个比例）。
> **判据不是"能达到理想值"，而是"每次都回到同一组值"。**
> 我第一版测试断言写成 45/45，被测试自己抓出来了 —— 这正是单测的价值。

### 11.3 输入框实现（标准 UI.InputField，自绘）

```csharp
InputField f = go.AddComponent<InputField>();
f.textComponent = txt;                      // 不指定会报错，必须手动给
f.contentType = InputField.ContentType.IntegerNumber;   // 只让输数字
f.onEndEdit.AddListener(...);               // 回车 / 失焦时应用
```

细节：
* 任一框里写了分隔符（`:` `：` `/` `-` `,` 空格）就把它当**完整比例**解析，
  所以可以只在女方框里写 `3:2`，更省事；
* `RefreshAll()` 回填输入框前要判断 `isFocused`，否则会把用户正在输入的内容冲掉；
* 配置持久化：`lock_ratio` / `lock_ratio_female` / `lock_ratio_male`（schema v5）。

---

## 12. ⚠️ v0.2.15：`NewRow()` 写死了坐标 ⇒ 新加的行跑到面板顶部

用户反馈："比例的输入框出现了，约分也正常，但显示位置在最上面。"

日志一句话定位：
```
LockCheckRow        y 392..422
RatioInputRow       y   0..34     ← 跑到最顶上了！
Buttons             y 482..518
```

原因：比例输入行是用 `NewRow()` 建的，而那个 helper 里**写死了 `Place(rt, 0, 0, ...)`**，
**根本没有接收 y 参数**（它当初只为"整行铺满宽度"的用法写的，而 `AddCheck` 那条路径
是自己在内部 `Place` 的，所以我一直没发现这个坑）。

**修复**：把 y 变成**必填参数** `NewRow(parent, name, yFromTop, height)`。

> **教训（与 §9.1 同类）**：任何"可以不传/写死默认坐标"的 helper 都是 bug 温床。
> 位置相关的函数，**坐标必须是必填参数**，让编译器帮你挡住遗漏。
> 另外这类"新增元素没摆对位置"的问题，**日志里的 `DescribeBounds` 一眼可辨**：
> 其他元素都成对递增，只有一个 y 从 0 开始。

这次日志还报了一个真实溢出：
```
WARN 窗口内容超出面板高度 14px！（当前 WinH=540，内容底边=554）
```
说明我加比例行时忘了同步加高面板。这轮收紧行距（标题→线 20→16、按钮→提示 14→10、
比例行→按钮 16→12）并把 `WinH` 定为 **552**：内容底边 536，顶部留白 26px、底部 16px，
日志不再报警。

---

## 13. ✅ v0.2.16：滑块读数改为**女性绝对占比**（用户提的模型更简单）

用户指出两件事：

**(a) 「锁定比例 女:男」标签顶到窗口边框上。**
原因：v0.2.15 我把 `NewRow()` 的坐标改成必填参数后，它的 rect 变成
`Place(rt, 0f, yFromTop, WinW - PadX*2f, ...)` —— 那是**给「整行铺满」的用法**，
而比例行是在行内用相对 x 贴左排的，于是贴左元素落在了 x=0（面板边缘）。
修复：比例行**不再用 NewRow**，自己建一个带 `PadX` 缩进的宿主行（`Place(row, PadX, ...)`），
内部子元素用相对行首的 x。

**(b) 滑块读数与女性占比不一致，应该让读数就是女性占比，并把最右边那段锁住。**
用户的推理是对的，而且这个模型**更简单**。旧语义：

```
滑块读数 = 女性 / (女 + 男)        ⇒ other=10%、女46% 时读数 51%   ← 读起来自相矛盾
```

新语义（v0.2.16）：

```
滑块读数 = 女性的绝对百分比（就是 femalePercent）
滑块上限 = 100 − 其他性别          ⇒ other=10% 时上限 90，最右 10% 拖不过去
```

于是：
* 读数与"女 X%"完全一致，**不再需要单独一行"滑块读数"**（用户建议删掉，已删）；
* "锁住最右那段"由 `StepSlider.SetMax(s.FemaleSliderMax)` 实现
  —— 关键：`RefreshAll` 里原来写的是 **`SetMax(100f)`**，那才会让用户把其他性别挤掉；
* 锁定比例时的换算也统一：`Split(pair = 100 - 其他, ...)`。

> **又一次"helper 语义不匹配"**：`NewRow` 是"整行"，比例行需要"带缩进的行内布局"，
> 混用就出错。**用法不同的布局要分开写，别硬套同一个 helper。**

单测同步更新到 46 项，新增/改写：
`女46% 时读数就是 46%`、`上限 90`、`写 95 被夹到 90`、`拖到顶 = 其他 10% + 全女`。

---

## 14. 每帧轮询的挂点：`GameInstance.Update(float)`

```
BattleTech.GameInstance::Update -> Void (Single deltaTime)   public 实例方法
```
用 Harmony **Prefix**（只声明 `__instance`，不碰参数），代价是一次按键判断；
比"自己 `new GameObject()` + `DontDestroyOnLoad` 当热键监听器"少一个常驻对象、少一堆场景切换问题。

`UnityEngine.Input.GetKeyDown` 可用 —— 已确认 `GetKeyDown` 在 `UnityEngine.CoreModule.dll` 里
（这台机器上**没有** `UnityEngine.InputLegacyModule.dll`，但 CoreModule 里有 `UnityEngine.Input`）。
游戏另有 InControl（`InControl.dll`），本 mod 不依赖它。

热键名从配置读，用 `(KeyCode)Enum.Parse(typeof(KeyCode), name, true)`，解析失败回退 F9 并报警。

---

## 15. 本轮修好的两处工具 bug（`tools/iltool.py`）

这两处都是**真实的列偏移错误**，会让签名解析全错（但 `il` 模式的 token 解析不受影响）：

| # | 位置 | 错误 | 修正 |
|---|---|---|---|
| 1 | `read_method`（MethodDef） | 从 `o+8` 读 Blob 列，实际那是 **Name** 列；`B` 是 4 时行宽 18，Blob 在 `o+12` | Blob 列 = `o + 8 + S` |
| 2 | `read_field_sig`（Field） | 漏读行首的 **Flags(2)**，把 Flags 当成 Name 索引 | 先 `o += 2` |

修好之后 `sig` 模式能正确解析出：

```
GenericPopupBuilder::AddInput  ->  GenericPopupBuilder (string, Action<string>, string, bool, bool)
DataManager::PooledInstantiate ->  GameObject (string, BattleTechResourceType, Nullable<Vector3>, Nullable<Quaternion>, Transform)
HBSSliderInput::SetLimits      ->  void (Single, Single)
```

另外 `sig` 模式新增了：`ARRAY` / `VAR` / `MVAR` / 泛型实参个数（`readcomp` 压缩整数）解析。

> 但 `parse_type` 对 `ELEMENT_TYPE_GENERICINST` 后面跟的 **TypeSpec 压缩 token**
> 仍解析不出来（会打印 `tok(0x........)`）。这是已知限制：
> **想知道委托/泛型的确切类型，用 `tools/probe/Probe.cs` 那套运行时反射更可靠**
> （`AddInput` 的 `Action<string>` 就是这么确认的）。已在此处留明确注释，不再硬猜。

---

## 16. 复现方式（probe）

`tools\probe\` 下四个 probe，用 csc 编译（引用 `Assembly-CSharp.dll` 等），直接跑：

```powershell
$g = "D:\MyDownload\Things\Steam\steamapps\common\BATTLETECH"; $m = "$g\BattleTech_Data\Managed"
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /langversion:5 `
  /codepage:65001 /out:"$env:TEMP\Probe.exe" `
  "/r:$m\Assembly-CSharp.dll" "/r:$m\UnityEngine.dll" "/r:$m\UnityEngine.CoreModule.dll" `
  "/r:$m\UnityEngine.UI.dll" "/r:$m\Newtonsoft.Json.dll" tools\probe\Probe.cs
& "$env:TEMP\Probe.exe"
```

| 文件 | 用途 |
|---|---|
| `Probe.cs` | GenericPopupBuilder / DataManager / HBSSliderInput 的精确签名与字段类型 |
| `Probe2.cs` | HBS UI 组件的基类与实现的接口、`OnChangeEvent` 的泛型实参 |
| `Probe3.cs` | `GameInstance.Update` / `UIModule` 的 prefab API |
| `Probe4.cs` | 所有 Slider 类型、`HBSSliderInput`/`HBSToggle` 实现的接口 |
| `Probe5.cs` | `UIModule.SortBehavior` / 带 Canvas 引用的类（**目前跑不通**：`asm.GetTypes()` 会因为 `Canvas`/`CanvasGroup` 被转发到 `UnityEngine.UIModule` 而抛异常，预加载也没解决；已知限制，别再花时间） |

> 为什么需要它：`dnfile` / `dncil` 在这台机器的 Python 3.14 上是坏的，而自写的 iltool
> 对泛型/委托签名有残留限制。**运行时反射是唯一 100% 可靠的签名来源** —— 它连
> 编译器都骗不过去（`SetValue` 是 protected 就是 `CS0122`，一次编译就试出来了）。

---

## 17. 待确认 / 下一步

### 已经验证过的

| 项 | 结论 |
|---|---|
| 代码创建的 UI 能不能显示 | ✅ 能，但**必须有 Canvas**（见 §2） |
| 窗口是不是只有一个 | ✅ 只有一个（见 §4.1） |
| **比例改动真的生效** | ✅ **已实测**：拖到全女 → 新建存档验证完全正确 |
| 锁定男女比例 = 锁**比值** | ✅ v0.2.5 修正；脱机单测直接断言比值不变 |
| 1% 步长 | ✅ 数值字段/自绘滑块都支持 |
| 复用游戏控件 | ❌ **放弃**（`NestedPrefabAnchor` 异步注入导致扫不到 / 重影 / 矩形滑块，见 §7） |
| 自绘控件 + 绝对定位 | ✅ v0.2.7 起采用，**待本轮实测确认外观与位置** |

### 接下来要做的（按优先级）

1. **确认位置全部正确**：标题不再压黄线、两个勾选框可见可点、滑块是**圆形手柄**。
   窗口尺寸与行距都是代码里的常量（`GenderRatioWindow.WinW / WinH / PadX`），要调告诉我。
2. **分辨率测试**：`CanvasScaler` 用 `ScaleWithScreenSize` + 参考分辨率 1920×1080 +
   `matchWidthOrHeight = 0.5`。理论上换分辨率不会错位；若发现偏移，把两个分辨率都告诉我。
3. **勾选框与滑块联动**：关掉「其他性别」时该滑块应变灰且不可拖。
4. **要不要把「应用」按钮去掉**：现在拖动**已经即时生效**了（滑块回调直接改 `Patches.Config.settings`），
   「应用」只负责把值写回 json 文件。若不需要"记住"，这个按钮可以去掉。

### 已知的取舍（写清楚，避免以后再绕回来）

* 自绘控件的**配色**照原版（深底 / 暖黄 / 米白文字），但**没有**原版的斜纹底与方括号装饰 ——
  那些是贴图资源。要"真原版"只有一条稳路：**Unity 2018.4 编辑器 + 自定义 AssetBundle**
  （交接文档 §2.3 路线 3），本项目一直刻意避开它。
* 复用游戏预制体的技术结论仍然有效，只是**不适合本项目**：
  如果哪天必须复用，正确姿势是"实例化 → **等 NestedPrefabAnchor 注入完成**（保持激活、挪出屏幕、
  alpha=0，轮询到内容出现）→ 再扫再摘"，而不是同步摘。这条已写进 §7.1。
