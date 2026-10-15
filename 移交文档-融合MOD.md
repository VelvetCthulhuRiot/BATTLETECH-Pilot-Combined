# 移交文档：融合 MOD（GenderRatio + PortraitSwap）

> **本文档的首要任务：让"更换电脑 / 新开对话"之后，接手者能凭它继续工作，能理解项目现在的状态，
> 以及接下来该做什么。**
> **这份文档是给一个没有任何上下文的 agent 的。** 请完整读完再动手。
> 尤其第 3 节（决策已锁定）、第 6 节（核心规则）、第 8 节（踩过的坑）。
>
> 撰写时间：2026-10-04（决策锁定版）
> 新工程：`%USERPROFILE%\Documents\BTCombinedMod\`
> 旧工程（**只读参考，不要改动**）：
> * `%USERPROFILE%\Documents\BTGenderRatioMod\`（v0.2.16）
> * `%USERPROFILE%\Documents\BTPortraitMod\`（v0.4.1）

---

## ⚠️ 现状（2026-10-15 更新：**项目已完成，本文档转为"设计档案 + 交接"**）

**下面第 3 节起的里程碑计划（M1–M9）已经全部实现并逐项真机验证通过**，
融合版当前版本 **0.2.0**，已可从本仓库直接发布。

* **想知道"现在到底实现了什么、怎么用"** → 看 [README.zh-CN.md](README.zh-CN.md)（中文）
  或 [README.md](README.md)（English）。
* **想知道"某个功能为什么这么做、踩过什么坑"** → 看 [docs/进展记录.md](docs/进展记录.md)，
  它按时间顺序记录了每一次真机反馈、根因与修法（**这份是活的排查手册**）。
* **想知道"素材/许可能不能发布"** → 看 [docs/素材与许可.md](docs/素材与许可.md)。
* **版本变更** → [CHANGELOG.md](CHANGELOG.md)。

**相对本文档原始计划的主要变化**：

| 本文档原计划 | 实际结果 |
|---|---|
| M1–M9 里程碑 | 全部完成；M9 还额外做了「启用头像替换」热切换、立即重扫、多语言、对勾标记 |
| 玩家指挥官头像列表"换成替换池" | 升级为**决策 X**：格子数 = 图片数、一张图一格、不重复不遗漏（见进展记录 §15） |
| §5 决策"藏掉没有头像的性别"（改代词选择器） | **放弃实现** —— 游戏的 `get_gender()` 按**选项下标**算性别，删选项会让性别判错（见进展记录 §18） |
| M6 空池强制关闭 | 已实现，并且在「立即重扫」之后也会重新做一致性处理 |
| — | 新增：界面多语言（跟随游戏语言 + `loc\*.json` 可扩展）、`ui_language` 强制开关 |
| — | 旧的两个 MOD 部署副本与 4 个残留文件已删除；两个**源工程保留**作只读参考 |

**代码位置速查**（19 个 C# 文件，单 DLL）：

| 关注点 | 文件 |
|---|---|
| 入口 / 初始化顺序 | `src\PilotCombinedMod.cs` |
| 配置（单文件 + 迁移） | `src\CombinedConfig.cs`、`GenderRatioConfig.cs`、`PortraitConfig.cs` |
| 肖像池扫描与性别分类 | `src\PortraitRegistry.cs`、`GenderNames.cs` |
| 核心规则（池子优先） | `src\Mechanic.cs` |
| 头像补丁 | `src\PortraitPatches.cs`、`PortraitSwapMod.cs` |
| 角色创建头像列表 | `src\PortraitGalleryFilter.cs` |
| 性别收口 + F9 窗口 | `src\GenderRatioPatches.cs`、`GenderRatioWindow.cs`、`StepSlider.cs` |
| 多语言 | `src\Loc.cs`（+ `mod\loc\*.json`） |
| 存档安全 | `src\SaveGuard.cs` |

---

## 0. 一句话概要

把两个**已各自跑通并真机验证过**的 BattleTech MOD 融合成一个：
* **GenderRatio** —— 调整新生成驾驶员的性别刷出比例（F9 自绘窗口调节）
* **PortraitSwap** —— 用本地 PNG 替换驾驶员头像

融合后的核心新能力：**肖像池决定"有哪些性别可用"，性别比例只在这个可用空间内分配**。

---

## 1. 项目背景：为什么会有这个项目

最初始于「给 BattleTech 做驾驶员头像替换 MOD」，后来从中连带着做了一个独立子项目
「全局驾驶员性别刷新比例」。现在两个子项目要合并。

**合并的动机**：
1. 减少游戏内 MOD 数量；
2. 也是为了解决长期存在的一个问题 —— 在没有足够某性别的肖像图片时，
   仍然会生成该性别的驾驶员，导致有些驾驶员没有替换头像（或原版头像与替换池混在一起）。
   → 见第 6 节的「肖像优先级」规则。

**旧的两个 MOD 已停用**（用户已在游戏内禁用），只启用融合后的新 MOD。

---

## 2. 两个源工程的现状（已核实）

### 2.1 GenderRatio（`%USERPROFILE%\Documents\BTGenderRatioMod\`，v0.2.16）

```
build.ps1                    编译 / 脱机单测 / 部署
mod\mod.json                 Game Mod；DLL=GenderRatio.dll；入口 GenderRatio.GenderRatioMod.Init
mod\genderratio.json         配置（schema v5）
src\Mechanic.cs              纯逻辑：三百分比 / 滑块语义 / 锁定比例（★不引用 UnityEngine，可脱机单测）
src\Ratio.cs                 整数比例：解析（自动约分）+ "最接近的整数百分比"换算
src\GenderRatioConfig.cs     genderratio.json 读写
src\Log.cs                   UTF-8 BOM 日志
src\Patches.cs               GetNameAndGender Prefix + 覆写 GenderWeights + 存档名单 Postfix
src\GenderRatioUi.cs         热键安装（GameInstance.Update 前缀）
src\GenderRatioWindow.cs     ★ 全自绘窗口 + 绝对定位（41000 字节）
src\StepSlider.cs            自绘步进滑块（圆角轨道 + 圆形手柄）
tests\TestMain.cs            46 项脱机断言
tools\iltool.py              无依赖 IL/元数据读取器（★修好过两处列偏移 bug）
tools\verify_deploy.py       部署后校验
tools\check_encoding.py      扫描文本文件有无 GBK 乱码
tools\probe\Probe*.cs        13 个反射 probe（拿游戏 API 精确签名用）
docs\UI实现笔记.md            ★ 五轮真机踩坑全记录 + API 真值表（约 49000 字节）
docs\接管-性别比例MOD.md      上一轮交接的修正与补充
```

标识：HarmonyId `com.btgenderratio.mod`；ModName/FolderName `GenderRatio`；
日志 `GenderRatio.log`；配置 `genderratio.json`；窗口 `WinW=760 / WinH=552`，Canvas 用
**ConstantPixelSize + scaleFactor=1**。

### 2.2 PortraitSwap（`%USERPROFILE%\Documents\BTPortraitMod\`，v0.4.1）

```
build.ps1 / README.md
mod\mod.json                 Game Mod；DLL=PortraitSwap.dll；入口 PortraitSwap.PortraitSwapMod.Init
mod\portraits.json           配置
mod\portraits\{female,male}\*.png   图片池
src\Log.cs                   [PortraitSwap] 前缀日志，写 PortraitSwap.log
src\PortraitSwapMod.cs       入口 + Patch 安装
src\Patches.cs               3+2 个挂点（见下表）
src\PortraitConfig.cs        portraits.json 模型（PortraitConfig / PortraitRule）
src\PortraitRegistry.cs      ★ 29000 字节：图片池、性别判定、贴图/Sprite 缓存、规则匹配
docs\机制分析.md              30000 字节，原版头像机制的 IL 级分析
docs\移交说明-原版ModLoader禁用系统mod的bug.md
test_portraits\ / 女性头像\ / 男性头像\   （素材副本）
```

PortraitSwap 的挂点：

| 挂点 | 类型 | 作用 |
|---|---|---|
| `PortraitManager.GeneratePortraitTexture` | Prefix，**返回 false 跳过原方法** | 3D 形态驾驶员的唯一渲染收口；拦掉就不跑 3D |
| `PilotDef.GetPortraitSprite` | Postfix，改 `__result` | 固定位图驾驶员（传奇佣兵/剧情角色） |
| `PilotDef.GetPortraitSpriteThumb` | Postfix，改 `__result` | 同上（缩略图） |
| `PilotDef.get_PortraitSettings` | Postfix，**只读**记录 settingsId↔PilotDef | 让"呼号/标签"规则对 3D 形态也生效 |
| `ModLoader.GetSaveAffectingModDefs` | Postfix，摘掉自己 | 与 GenderRatio **重复**，合并成一份 |
| （可选）`SGCharacterCreationPortraitCustomization.OnAddedToHierarchy` | Postfix | 隐藏「自定义外貌」按钮 |

标识：HarmonyId `com.btportraitmod.portraitswap`；ModName/FolderName `PortraitSwap`；
日志 `PortraitSwap.log`；配置 `portraits.json`。

`PortraitRegistry` 可复用接口：
```csharp
internal static string ModDirectory;
internal static PortraitConfig Config;
internal static void RememberSettings(string settingsId, PilotDef pilotDef);
internal static string ResolveImageForSettings(PortraitSettings settings);   // 3D 形态
internal static string ResolveImageForPilotDef(PilotDef def);                // 固定位图
internal static Gender ParseGender(string s);
internal static Gender DetectGender(string relativePath);
internal static Texture2D GetHandout(string settingsId, string relativePath, int size);
internal static Sprite GetSprite(string relativePath);
internal static bool Wildcard(string pattern, string text);
```
内部已有 `PoolByGender`（`Dictionary<Gender, List<PoolEntry>>`）、`PoolUntagged`、`_poolBuilt`
—— **但没有对外暴露"每个性别的图片张数"**。融合后要加只读访问器（第 6 节需要）。

---

## 3. 决策已锁定（用户已拍板，不要再问）

| # | 决策 | 说明 |
|---|---|---|
| 1 | **旧的两个 MOD 停用**，只启用新 MOD | 用户已在游戏内禁用；代码保留不动以便回退 |
| 2 | **不做**"按头像池自动配比性别比例" | 用户认为手动调比例已经足够强大。**不要**再加这个功能 |
| 3 | **头像替换可开关** | 沿用 PortraitSwap 的 `enabled` 思路，但**开关语义要按第 6 节重定义** |
| 4 | **肖像优先级高于比例调节** | 一旦开启肖像替换，生成的驾驶员**必须**是有替换头像的性别（第 6 节） |
| 5 | **启用肖像替换时只按男女比例** | **无论是否开启「其他性别」、无论它显示多少，都认为 男+女 = 100%**，只参考男女比例 |
| 6 | **某性别无肖像则无视比例** | 只生成"有肖像图片"的那些性别（第 6 节） |
| 7 | **其他性别留位** | 用户还没准备其他性别（如机器人）的肖像，**代码要留出空间**，将来加图即可生效 |
| 8 | **沿用现有头像图片** | 来源见第 5 节 |
| 9 | **一张肖像都没有 ⇒ 替换功能不可开启** | 此时 MOD **只剩性别比例调节**，替换项变灰且不能勾选（第 6.4 节） |
| 10 | **灰化规则** | 其他性别无图 ⇒ 该项变灰不可调；男女缺任一 ⇒ 男女滑块也变灰不可调（第 6.6 节） |
| 11 | **性别调节不作用于战役固定角色** | 战役开局 4 人（Glitch/Behemoth/Dekker/Medusa）**保持原性别**（第 6.7 节） |
| 12 | **性别调节不作用于玩家指挥官** | 玩家指挥官的性别**保持游戏原本流程**产生，本 MOD 不干预（第 6.7 节） |
| 13 | **玩家指挥官仍用替换后的肖像池** | 保留开局选肖像的**原本流程**（同一个选择界面、同样的选法），**只是池子换成我们的替换肖像**（第 6.8 节） |

---

## 4. 融合可行性结论

**可以，且技术上不难**，因为两者挂的是**不同方法、管不同的事**：

| 维度 | 结论 |
|---|---|
| 挂点冲突 | **无**。一个是 `PilotGenerator.GetNameAndGender`，一个是 `PortraitManager`/`PilotDef`。没有同一方法被两边都挂 |
| 时序冲突 | **无**。性别在"生成驾驶员"时决定，头像在"渲染时"决定。**但融合后两者有了依赖关系**（第 6 节） |
| Harmony ID | 要**统一成一个** |
| 配置/日志 | 要**合并**（一个配置文件 + 一个日志） |
| 存档标记 Postfix | **重复了**，合并成一份 |

### 4.1 不代表"把代码搬过去就行"

真正的三件事：
1. **命名空间/类名统一**（两工程都有 `Log`、`Patches`、`build.ps1`）
2. **配置与日志合并**（建议 `combined.json` + **自动迁移旧配置**，不破坏老用户）
3. **UI 合并**（F9 窗口里同时管性别比例与头像）

### 4.2 历史包袱

PortraitSwap 的 `mod.json` 把 **`Manifest` 显式写成空数组 `[]`、`LoadImplicitManifest: false`**，
否则原版 `ExpandManifest` 会对 `null` 列表做枚举而报错。融合后照抄。
**绝不能产出 `systemMod.json`**（原因见第 8.2 节）。

---

## 5. 头像图片素材（已核实）

| 性别 | 目录 | 数量 |
|---|---|---|
| 女性 | `%USERPROFILE%\Pictures\女性头像` | **40 张**（不重复） |
| 男性 | `%USERPROFILE%\Pictures\男性头像` | **10 张**（不重复） |
| 其他性别 | （尚未准备） | 0 |

* 旧 PortraitSwap 部署目录里曾是 40/40，**以 `Pictures` 里的 40/10 为准**（用户明确说 10 张男性）。
* 融合后建议放进新工程：`mod\portraits\female\`、`mod\portraits\male\`，
  **并预留 `mod\portraits\other\`**（将来放机器人等）。
* 用户的意图是**留出空间**：加图即可生效，不需要改代码。

---

## 6. ★ 核心规则：肖像池优先（本项目的灵魂）

这是融合后**唯一的新行为**，也是整个项目要解决的实际问题。请仔细实现。

### 6.1 一句话

> **肖像池决定"有哪些性别可用"；性别比例只在这个可用空间内分配权重。**

### 6.2 规则详解

设肖像池张数为 `femaleCount / maleCount / otherCount`，配置里的目标比例为
`femalePercent / malePercent / otherPercent`（`otherEnabled` 表示用户是否想要其他性别）。

**步骤 1：先由肖像池决定可用性别集合**

```
F = (femaleCount > 0)
M = (maleCount  > 0)
O = (otherCount  > 0)      // 其他性别：有图才算可用（用户现在没准备 ⇒ O = false）
```

**步骤 2：按可用集合裁剪比例**

| 情况 | 可用 | 最终权重（女 / 男 / 其他） | 说明 |
|---|---|---|---|
| ① | F, M, O | `pF, pM, pO` | 三者都有图：完全按配置 |
| ② | F, M | `2*pF, 2*pM, 0` | **男+女 = 100%**（用户的核心要求：无视其他性别，无论它显示多少） |
| ③ | 仅 F | `1, 0, 0` | **无视比例，只生成女性** |
| ④ | 仅 M | `0, 1, 0` | 只生成男性 |
| ⑤ | F, O | `pF, 0, pO` 归一 | 没有男性肖像 |
| ⑥ | M, O | `0, pM, pO` 归一 | 没有女性肖像 |
| ⑦ | 仅 O | `0, 0, 1` | 只生成其他性别 |
| ⑧ | 都没有 | `1, 1, 1` | 退化兜底（且此时替换功能整体不可开启，见 6.4） |

**步骤 3：权重和必须 > 0**

`SimGameState.GetWeightedResult` 要求总权重大于 0，否则会异常/返回意外结果。
表里情况 ② 的 `2*pF + 2*pM = 200`（因为裁剪后 pF+pM 原本是 100，乘 2 后是 200），
情况 ③④⑦ 是 1，都安全。若最终三个权重全为 0，**必须**退回 `1,1,1`
（或退回"不改游戏原值"）。

### 6.3 为什么"男+女 = 100%"是这样算的

用户的原话："如果开启肖像替换，应该实现只按照男女比例来生成驾驶员并替换头像，
无论是否开启其他性别，无论其他性别显示多少，都认为男+女 = 100%，只参考男女比例"。

所以配置里 `otherPercent = 10` 时，**实际分给女/男的**是原来那 90% 那一份
（`pF` / `pM`），它们之间保持比例；但因为可用集合里没有 O，**要把这份放大填满 100%**
——即 `女:男 = pF : pM`，两者之和 = 100%。乘 2 正是这个意思
（因为 `pF + pM = 100 - pO`，乘 `100/(pF+pM)` 即可；当 `pO = 0` 时就是乘 1）。

**建议实现上不要硬编码"乘 2"**，而是：先按可用集合把不需要的项剔除，
再把剩下的**按比例重新归一到总和 100**。这样任何组合都对（见 6.9 的推荐函数）。

### 6.4 一张肖像都没有时：替换功能**不可开启**

若 `femaleCount + maleCount + otherCount == 0`：

* **强制**把肖像替换视为关闭（`portraits.enabled` 在内存里被压成 false，
  并**写回配置**，避免用户以为是配置没生效）；
* 此时 MOD **只有性别比例调节功能**，行为完全等同于旧的 GenderRatio；
* UI 上「启用头像替换」勾选框**变灰且不可勾选**，并显示原因：
  `未找到任何肖像图片（portraits\female|male|other），头像替换不可开启`
* 日志明确记录一行，例如：
  `肖像池为空（female=0 male=0 other=0）→ 头像替换已强制关闭，仅启用性别比例调节`
* 一旦用户放入了图（并在窗口点「立即重扫图片池」），该项**立即恢复可勾选**。

> 这一条是用户明确要求的，属于"护栏"而非功能：避免用户在没有任何图的情况下开启替换，
> 结果所有驾驶员都变成空白/原版头像，误以为 MOD 坏了。

### 6.5 关闭肖像替换时的行为

* 完全不介入头像（交回游戏/原版逻辑）；
* 性别比例按**用户配置原样生效**（三性别都按老规则），**不做** 6.2 的裁剪。

### 6.6 UI 配合：灰化规则（用户已确认）

开启肖像替换后，"其他性别"那一路实际上被忽略了。**UI 必须如实反映**，
否则界面会骗人（勾选框和滑块看起来有效但不起作用）。灰化规则：

| 条件 | UI 表现 |
|---|---|
| `otherCount == 0` | 「启用其他性别」勾选框 + 其他性别滑块 **变灰不可调**；状态行写 `未准备其他性别肖像` |
| `otherCount > 0` | 恢复正常可调（用户以后加了图即生效） |
| `femaleCount == 0` 或 `maleCount == 0` | 女/男 滑块 **变灰不可调**；状态行写 `仅有 <性别> 肖像 → 只生成 <性别>，男女比例暂不生效` |
| 仅一个性别有图（含"仅其他"） | 除该性别外全部变灰 |
| 替换功能整体不可开启（6.4） | 「启用头像替换」本身变灰不可勾 |

**状态行建议常显肖像池张数**：`肖像池：女 40 / 男 10 / 其他 0`
**（推荐）** 在灰化项旁边加一句短提示，而不是只变灰 —— 用户能立刻知道"为什么点不动"。

### 6.7 性别调节的作用范围：**只作用于随机生成的驾驶员**

用户已确认：**性别调节不应用于战役模式的固定角色，也不应用于玩家指挥官。**

| 对象 | 性别调节 | 肖像替换 |
|---|---|---|
| 招募市场刷出的驾驶员 | ✅ 按第 6.2 节 | ✅ |
| 生涯模式开局 4 名队友 | ✅（`CareerMode` 无固定名单，随机生成） | ✅ |
| 合约里的敌方驾驶员 | ✅ | ✅ |
| 战役模式开局 4 人（具名） | ❌ **保持原性别** | ✅（若名单里有对应图） |
| 玩家指挥官（本机玩家） | ❌ **不干预，走游戏原流程** | ✅ 见 6.8 |
| 存档里已有的驾驶员 | ❌ 不追溯 | ❌ |

**实现要点（需要在 Prefix 里区分"随机生成"与"具名/玩家"）**：
`PilotGenerator.GetNameAndGender` 的 Prefix 里无法直接知道调用者是谁，
所以需要**另找一个可靠的判别依据**。可行的方向（**需要在真机上验证**）：

1. 看当前是否处于角色创建 / 战役固定名单的流程中（`SGCharacterCreationWidget` 是否活跃等）；
2. 看 `PilotGenerator` 实例上是否有可区分的状态；
3. 最稳的可能是**按 pilotDefId 白名单**排除：战役固定 4 人的 id 是已知的，
   玩家指挥官另有标识。

> ⚠️ **这一条是本项目最需要真机验证的地方。** 不要凭推测选判别方式 ——
> 用 `tools\probe\` 的运行时反射 + 日志把 Prefix 被调用时的上下文打出来，
> 拿到真实数据再决定。宁可先"只对招募市场生效"（能看到明确效果），
> 也不要盲目拦掉导致战役开局 4 人性别错乱。

### 6.8 玩家指挥官：保留原流程，只换肖像池

用户要求："保留玩家指挥官开局选择肖像的原本流程，只是肖像池变成替换后的肖像池。"
以及"性别调节不要应用在玩家指挥官"。

也就是说：
* **流程不变**：还是游戏原本的角色创建 → 选肖像界面，同一个界面、同样的选法；
* **池子变了**：界面里可选的肖像变成我们的替换肖像（按玩家所选性别给对应池）；
* **性别不变**：本 MOD **不**给玩家指挥官指定性别，性别就是玩家自己在创建时选的。

**已核实的相关 API**（`Python iltool` 输出）：

| 类型 | 关键成员 |
|---|---|
| `BattleTech.UI.SGCharacterCreationPortraitSelectionPanel` | **`PopulateList()` — public** ← **最可能的挂点**；另有 `FilterList(filter)` (public)、`PopulateList` 依赖的 `allPortraitElements` / `sortedList` / `filteredList` / `_listParent` / `radioSet`；`GetRandomizedSortOrder(nPresets)` / `PopRandom(list)` 说明列表是**打乱后取子集**的 |
| `BattleTech.UI.SGCharacterCreationPortraitCustomization` | 自定义外貌界面（PortraitSwap 已有 `OnAddedToHierarchy` Postfix 可隐藏它） |
| `BattleTech.UI.SGPortraitCustomizationPopup` | 整容弹窗 |
| `BattleTech.Portraits.PortraitManager` | `GeneratePortraitTexture`（替换渲染的收口）、`ClearPortraits(settings)`、`RequestRenderPortrait`、`BuildDefaultRenderTextures`、`PORTRAIT_PRESET_FILE_PREFIX` |
| `BattleTech.EmotePortrait` / `CharacterPortraitCustomizedMessage` / `RefreshPortraitsMessage` | 相关消息类型 |

**概念上怎么实现（推荐思路）**：
玩家在创建界面选中的是一个 **`PortraitSettings`（一个"预设"）**，
渲染时走 `PortraitManager.GeneratePortraitTexture`。既然我们已经拦了那个方法，
就可以把玩家选中的预设**在渲染阶段映射到我们池子里的一张图**：

```
玩家选性别 = 女
 → PopulateList 里提供的预设来自"女性替换池"
 → 玩家选中第 k 个预设
 → 该预设的 settingsId 被映射到 female 池里的第 k 张图
 → GeneratePortraitTexture 被我们接管，交给游戏那张替换图的副本
```

这样**完全不需要改游戏的选择界面逻辑**，只是喂给它的"预设清单"和"渲染结果"都换了。

**需要真机确认的细节**：
1. `PopulateList` 是否就是玩家指挥官列表的填充点（也可能是 `FilterList` 之后）；
2. 预设清单从哪来（`PORTRAIT_PRESET_FILE_PREFIX` 相关？还是 DataManager 里的 preset 资源？）；
3. **玩家的性别是在选肖像之前还是之后确定的**（决定我们能否按性别给池子）。

**边界情况（必须处理）**：若玩家选了**没有替换肖像的性别**（例如选了"其他性别"但
`otherCount == 0`，或选了男性而 `maleCount == 0`）：
* 因为**不能**改玩家指挥官的性别（决策 12），此时**让该性别的选项回退到游戏原本的预设池**，
  并用日志 + （可行的话）界面提示说明"该性别没有替换肖像，使用游戏原始肖像"。

> 注意这与其他随机驾驶员不同：随机驾驶员在无图时会**避开该性别**（第 6.2 节），
> 因为我们可以自由选性别；而玩家指挥官的性别由玩家决定，我们只能回退。
> **这是有意的差异，不要"统一"掉。**

### 6.9 推荐的算法写法（纯函数，可脱机单测）

**放在 `Mechanic.cs` 里，保持"不引用 UnityEngine"**，这样能继续用脱机单测覆盖：

```csharp
/// <summary>按"肖像池优先"裁剪出最终权重。返回 [女, 男, 其他]，三者之和 > 0。</summary>
public int[] ResolveFinalWeights(
    bool portraitMode,          // 是否开启了肖像替换
    bool otherEnabled,
    float otherPercent,
    bool hasFemale, bool hasMale, bool hasOther)
{
    if (!portraitMode)
        return WeightsFor(otherEnabled, Percents3(), 1f);   // 老行为，不做裁剪

    // 1) 可用集合
    bool F = hasFemale, M = hasMale, O = hasOther;
    // 2) 任一性别没图 ⇒ 该项权重置 0；三者都有 ⇒ 原样
    float f = F ? femalePercent : 0f;
    float m = M ? malePercent   : 0f;
    float o = (O && otherEnabled) ? otherPercent : 0f;

    // 3) 归一：把剩下的填满 100（这就是"男+女=100%"的通用写法）
    float sum = f + m + o;
    if (sum <= 0f) return new int[] { 1, 1, 1 };            // 兜底
    float k = 100f / sum;
    return new int[] { ToWeight(f * k), ToWeight(m * k), ToWeight(o * k) };
}
```

单测必须覆盖 6.2 表格里的①~⑧全部情况，并断言：**任一情况下三个权重之和 > 0**，
且"只有一个性别有图时，该性别权重 > 0 而其他两个 = 0"。

### 6.10 运行时怎么拿到"有没有肖像"

在 `GetNameAndGender` 的 Prefix 里需要**每帧/每次生成都能廉价读到张数**。
`PortraitRegistry` 的图片池构建有开销（扫描目录 + 解码），所以：
* 池只构建一次（已有 `_poolBuilt`），暴露 `internal static int CountOf(Gender g)`；
* Prefix 里读的是**缓存计数**，不要每次重扫目录；
* 窗口里提供「立即重扫图片池」按钮（用户加完图不用重开游戏）。

---

## 7. 融合后的产品形态

### 7.1 配置文件

建议 `combined.json`，**自动迁移**旧的 `genderratio.json` / `portraits.json`（读到就转写）。

```jsonc
{
  "schema_version": 1,
  "enabled": true,

  "//gender_ratio": "性别刷出比例。注意：开启肖像替换后，实际以肖像池可用性别为准（见文档第 6 节）。",
  "gender_ratio": {
    "enabled": true,
    "other_enabled": false,
    "other_percent": 0,
    "female_percent": 50,
    "male_percent": 50,
    "other_label": "其他性别",
    "lock_ratio": false,
    "lock_ratio_female": 1,
    "lock_ratio_male": 1
  },

  "//portraits": "头像替换。enabled=false 时完全不介入头像，性别比例按配置原样生效。若一张肖像都没有，enabled 会被强制压成 false（见文档 6.4）。",
  "portraits": {
    "enabled": true,
    "autoScan": true,
    "poolMode": "hash",
    "neutralize3DCustomization": false,
    "defaultPortrait": "",
    "pool": [],
    "rules": []
  },

  "//popup": "F9 窗口。",
  "popup": {
    "enabled": true,
    "hotkey": "F9",
    "step_percent": 1,
    "font_file": "",
    "female_label": "女",
    "male_label": "男"
  }
}
```

> 图片池路径约定（保持 PortraitSwap 的习惯）：
> `mod\portraits\female\*.png`、`mod\portraits\male\*.png`、`mod\portraits\other\*.png`（预留）

### 7.2 F9 窗口（合并后）

```
┌─ 驾驶员设置 ──────────────────────────────────┐
│ 当前：女 46% / 男 44% / 其他性别 10%           │
│ 肖像池：女 40 / 男 10 / 其他 0                 │
│ ☑ 启用头像替换（未准备其他性别肖像 → 比例按 女:男 归一）│
│ ☐ 启用「其他性别」     占比 10%   [====|==]  (灰) │
│ 女/男 分配：女 50%，男 50%        [=====|==]  │
│ ☐ 锁定男女比例   女[1] : 男[1]  (回车应用)      │
│ ──────────────────────────────────────────── │
│ [立即重扫图片池]  [打开头像文件夹]              │
│ ──────────────────────────────────────────── │
│ [应用并写回配置]  [恢复默认]  [关闭]            │
└───────────────────────────────────────────────┘
```

窗口仍是**全自绘 + 绝对定位**（不要改回复用游戏预制体，原因见第 8.1 节）。
加行之后**务必**跑一遍布局核算脚本（`WinH` 要同步调整，否则会溢出，日志会报
`窗口内容超出面板高度 Npx`）。

---

## 8. 必须遵守的硬规矩 & 踩过的坑

### 8.1 UI：不要试图复用游戏自带控件（重要）

本项目连续 5 轮真机调试的结论：**全部自绘**。因为游戏控件的方括号/标题文字/圆形手柄
是 `NestedPrefabAnchor` **异步注入**的 —— 同步扫不到（扫到 0 个），等它注入又会把
别的界面文字带进来造成**重影**。相关坑（全部真机验证过）：

| # | 现象 | 结论 |
|---|---|---|
| 1 | 代码创建的 UI **看不见、且不报错** | 根节点**必须有 `Canvas`** |
| 2 | 出现**两个窗口** | 别用 `GenericPopupBuilder` 当窗口外壳；自己画全屏 `Image` 遮罩（`raycastTarget=true` = 模态） |
| 3 | 控件**错位且点不到** | 不要改游戏预制体实例的 `sizeDelta`；换了控件后**必须重建内容根的布局**，否则停在 0 尺寸（0 尺寸的 `Graphic` 既不画也点不到） |
| 4 | **文字重影** | 见本节的"异步注入"说明 |
| 5 | 面板**上下颠倒** | `anchorMin=anchorMax=pivot=(0,1)` 时，**rect 顶边距父级顶部 = \|anchoredPosition.y\|**，即 `anchoredPosition=(x, -yFromTop)` |
| 6 | 界面被**整体缩放 0.5 倍** | 布局按像素写死时**不要**用 `CanvasScaler.ScaleWithScreenSize`；用 `ConstantPixelSize + scaleFactor=1` |
| 7 | 标题**压到黄线** | `Text` 的 rect 高度必须 ≥ 字号×行高 |
| 8 | 新加的行跑到**面板最顶部** | helper 里写死了坐标。位置相关的函数，坐标必须是**必填参数** |
| 9 | 坐标批量替换只改了一半 | 漏了 7 处间接调用。**一个变量身兼两种坐标系含义**是根因 |

**完整版**（含每一条的日志证据与修正代码）在
`..\BTGenderRatioMod\docs\UI实现笔记.md`（约 49000 字节）——**动 UI 前必读**。

### 8.2 必须是 **Game Mod**，绝不用 `systemMod.json`

原版 ModLoader 的 `InitSystemModsLoop` 在"这个系统 mod 被禁用"分支里**写错了表**
（写了 `loadedGameModStatus` 而不是 `loadedSystemModStatus`）：
系统 mod 在游戏 mod 表里没有条目 → `KeyNotFoundException` → 被标 `failedToLoad`
→ 注入两个状态文件 → `GetCombinedModStatus()` 撞重复键
→ **保存永远卡在"正在保存中"、模组界面打不开，且每次启动复现。**

**若已踩坑**（关掉游戏后）：把出问题的 mod 文件夹移出 `mods\`；删掉
`mods\HBS\Cache\{mod_status,system_mod_status,merge_cache,type_cache}.json` 与
`mods\load_order.json`；**保留** `mods\HBS\Database\MetadataDatabase.db`；启动游戏让缓存重建。

### 8.3 `mod.json` 两个必写项

```json
"Manifest": [],
"LoadImplicitManifest": false,
```

### 8.4 不锁定存档

`"IsSaveAffecting": false` **并且**在 `ModLoader.GetSaveAffectingModDefs` 上加 Postfix 摘掉自己
（两道保险）。这个 Postfix 必须**无条件注册**（放在 `enabled` 检查之前），
否则用户关掉功能后产生的存档仍会被标记成"必需本 mod"。

### 8.5 机制相关的坑

| 坑 | 结论 |
|---|---|
| 性别权重是**懒加载**的 | `PilotGenerator.GenderWeights` 建好之后再改常量**不会生效**；必须同时覆写 `GenderWeights[0..2]`（顺序已确证：**女/男/其他**） |
| 读档会**新建 `SimGameConstants`** | Prefix 里每次都要**幂等重算** |
| 贴图归属 | `PortraitManager.Clear()/ClearLargePortraits()/ClearPortraits()` 会 `DestroyImmediate` 掉 `renderResults` 里的贴图；交给游戏的那张必须是每个 `(settingsId, 尺寸)` **独占的副本**，且每次取用前验活；**自己的原型图绝不交出去** |
| 权重和必须 > 0 | 见 6.2 步骤 3 |

### 8.6 工具链的坑

| 坑 | 结论 |
|---|---|
| `dnfile` / `dncil` 在 Python 3.14 上**坏掉** | 用 `tools\iltool.py`（纯标准库） |
| `iltool.py` 原版有**两处列偏移 bug** | 已修（`MethodDef` 的 Blob 列、`Field` 行漏 `Flags(2)`） |
| 泛型/委托签名解析不出来 | 用 `tools\probe\*.cs` 的**运行时反射**拿确切类型 |
| **绝不要用 PowerShell 做 UTF-8 文本替换** | `Get-Content -Raw` + `Set-Content` 会按 GBK 解码再写回，**整个文件中文变乱码、引号被弄坏**（`error CS1010`）。改文本一律用编辑工具；事后跑 `python tools\check_encoding.py` |

---

## 9. 建议的实施顺序（每步都要能验证）

> **每一轮都要：编译 → 部署 → 让用户启动游戏 → 拿日志 → 再改。**
> 不要靠推测改机制/UI，要用日志断言。

| 阶段 | 内容 | 验证方式 |
|---|---|---|
| **M1** | 建工程，把两边 `src` 拷进来，**只做重命名与命名空间统一**（不删代码），让它能编译出一个 DLL | 编译通过；DLL 里能反射看到两套补丁类 |
| **M2** | 合并 `Log` / `ModDirectory` / `mod.json` / `build.ps1`；配置用 `combined.json` + 自动迁移 | 启动游戏，日志里两套补丁都"已修补"，配置被正确读到 |
| **M3** | 合并**存档标记** Postfix 成一份；确认 `Manifest: []` | 日志出现"存档所需 mod 名单…已摘除本 mod" |
| **M4** | `PortraitRegistry` 暴露各性别张数（`CountOf(Gender)`，用缓存的池） | 日志打印各性别张数 |
| **M5** | **实现第 6.2 节的肖像优先规则** + 脱机单测覆盖①~⑧ | 单测全过；游戏内实测：只放女性图时**全生成女性** |
| **M6** | **实现 6.4**：一张图都没有时强制关闭替换、只剩性别比例 | 把 `portraits` 目录清空启动，日志出现"肖像池为空 → 替换已强制关闭"，UI 该项变灰 |
| **M7** | **实现 6.7**：区分"随机生成"与"战役具名/玩家指挥官"（⚠️ 先探测上下文再动手） | 战役开局 4 人性别**不变**；招募市场仍按规则生效 |
| **M8** | **实现 6.8**：玩家指挥官选肖像列表换成替换池（⚠️ 先验证 `PopulateList` 是挂点） | 创建角色时看到的肖像来自我们的池；选定后渲染正确 |
| **M9** | 合并 UI 到同一个 F9 窗口（含 6.6 的全部灰化规则与状态提示） | 按 F9 看到两项；各灰化条件逐一验证 |
| **M10** | 回归：只装新 MOD 正常；确认旧两个 MOD **已停用**不会双重挂钩点 | 三种组合各启动一次看日志 |

**关于 M7 的顺序建议**：先做 M5+M6（纯数据规则，脱机可测），再做 M7。
M7 需要先**探测** Prefix 被调用时的上下文（用 `tools\probe\` + 日志把真实数据打出来），
**探测清楚再写拦截逻辑**。宁可先"只对招募市场生效"，也不要盲目拦掉导致战役开局 4 人性别错乱。

**关于 M8 的顺序建议**：M8 可以独立推进，但它依赖 `PopulateList` 的验证。
如果 M8 一时做不通，**先把 M5~M7 交付**（那时 MOD 已经可用：随机驾驶员有替换头像，
玩家指挥官走原版肖像），M8 作为增量再做 —— **不要为了 M8 卡住整个融合**。

**M10 特别提醒**：融合后如果旧的 `PortraitSwap` **仍启用**，
`PortraitManager.GeneratePortraitTexture` 会被两个 Prefix 各拦一次
（行为取决于 Harmony 排序）。用户已同意停用旧的，但请在文档与日志里
**明确检查并警告**："检测到旧 PortraitSwap 同时启用，请禁用它"。

---

## 10. 工具链怎么用

### 10.1 编译（不需要 .NET SDK）

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
```

游戏程序集（**已核实**）：
```
游戏根目录 = %BATTLETECH%
Managed    = <根>\BattleTech_Data\Managed
Harmony    = <根>\BattleTech_Data\StreamingAssets\data\mods\HBS\0Harmony.dll
```
引用：`Assembly-CSharp.dll`、`UnityEngine.dll`、`UnityEngine.CoreModule.dll`、
`UnityEngine.UI.dll`、`UnityEngine.UIModule.dll`、`UnityEngine.TextRenderingModule.dll`、
`UnityEngine.ImageConversionModule.dll`、`Newtonsoft.Json.dll`、`0Harmony.dll`

选项照抄 `BTGenderRatioMod\build.ps1`：
`/nologo /target:library /optimize+ /debug- /langversion:5 /codepage:65001`
（**`/langversion:5`** 很重要）

### 10.2 反编译 / 查签名

```powershell
python tools\iltool.py types   <名称子串>
python tools\iltool.py members <完整类型名>
python tools\iltool.py il      <完整类型名> <方法1,方法2>
python tools\iltool.py sig     <完整类型名> <方法1,方法2>
python tools\iltool.py findstr <字符串子串>
```

### 10.3 部署后校验

```powershell
python tools\verify_deploy.py     # UTF-8 / JSON 字段 / DLL 是否为托管程序集
python tools\check_encoding.py    # 扫描文本文件有无 GBK 乱码
```

### 10.4 日志位置

```
<用户>\Documents\My Games\BattleTech\mods\<MOD名>\<MOD名>.log     ← UTF-8 带 BOM
<用户>\AppData\LocalLow\Harebrained Schemes\BATTLETECH\output_log.txt  ← 带 [前缀] 的行
```

---

## 11. 给接手者的第一条回复建议

1. **先读**这三份（按顺序）：
   * 本文档（决策与规则）
   * `..\BTPortraitMod\docs\机制分析.md`（原版头像机制的 IL 分析）
   * `..\BTGenderRatioMod\docs\UI实现笔记.md`（UI 的坑，动 UI 前必读）
2. 列出"融合影响的文件清单"，确认新 MOD 的 Name / FolderName / HarmonyId
3. 按第 9 节 **M1 开始**：先让两套代码在**一个 DLL** 里编译通过，**先不改行为**
4. **不要一上来就重构 UI** —— UI 是历史重灾区，等机制先融合稳了再动
5. 第 6 节的规则**先写脱机单测**（`Mechanic.cs` 保持不引用 UnityEngine），再上真机

### 仍需向用户确认的问题

> 用户已明确的（不要再问）：旧 MOD 停用、不做自动配比、肖像优先级、灰化规则、
> 性别调节不作用于战役固定角色与玩家指挥官、玩家指挥官保留原选肖像流程只换池子、
> 一张图都没有时不可开启替换。以下是**仍未定**的：

1. 新 MOD 叫什么？（建议 Name `BTCombinedMod`，HarmonyId `com.btcombinedmod`）
2. 配置文件用 `combined.json` 还是保留 `portraits.json` + `genderratio.json` 两个？
   （建议合并 + 自动迁移）
3. 图片目录布局是否照搬（`portraits\female\`、`portraits\male\`、预留 `portraits\other\`）？
4. **玩家选了"没有替换肖像的性别"时怎么处理？**
   文档 6.8 的建议是**回退到游戏原始预设池**（因为不能改玩家指挥官的性别）。
   也可以选择"界面上直接不给该性别选项"，但那会改变原流程，与决策 13 冲突。
5. `other_label` 的显示名（现在叫「其他性别」）以后加机器人等新分类时是否要改名机制？
   （`gender_ratio.other_label` 已经可配，但滑动条文案也是写死的，可能要对齐）

---

## 12. 附：关键 API 真值（已实测，可直接依赖）

| API | 精确签名 / 事实 |
|---|---|
| `PilotGenerator.GetNameAndGender` | `void (ref string, ref string, out Gender)`，**private 实例**方法 |
| `PilotGenerator.GenderWeights` | `int[3]`，索引顺序 **[女, 男, 其他]**；**懒加载**（null 时才从常量建） |
| `SimGameConstants.Pilot` | public **属性**；三个权重是 public **int 字段** |
| `SimGameState.Constants` | public **属性**；`PilotGenerator.Sim` 是 private **字段** → 用"属性优先、字段兜底"的反射 |
| `PortraitManager.GeneratePortraitTexture` | `(PortraitSettings, Action<RenderedPortraitResult>, Action<string>, PortraitSizes[])`；Prefix 返回 **false** = 完全跳过原方法 |
| `PortraitManager` | 是 `Dictionary<string, RenderedPortraitResult>` 的子类（`__instance[settingsId]` 可直接用） |
| `GenericPopupBuilder.AddInput` | `(string, Action<string>, string, bool, bool)`；**内部会自动加一个 Confirm 按钮** |
| `GenericPopupBuilder.AddButton` | `(string, Action, bool, **PlayerAction**)` ← 第 4 参数是 `PlayerAction`，不是 `KeyCode?` |
| `GenericPopup.Abort` | `(bool forcePool)`；收起弹窗用它，别自己 Destroy（池化对象） |
| `ModLoader.GetSaveAffectingModDefs` | `public static List<string> ()` |
| `DataManager.PrecachePrefab` | `(string id, BattleTechResourceType, int count)` |
| `DataManager.PooledInstantiate` | `GameObject (string id, BattleTechResourceType, Vector3?, Quaternion?, Transform)` |
| `DataManager.GetPooledPrefab` | 返回 **`UnityEngine.Object`**（不是 `GameObject`，写错 `CS0266`） |
| `GameInstance.Update` | `void (float deltaTime)`，public 实例方法 → 做热键轮询（只声明 `__instance`） |
| `SGCharacterCreationPortraitSelectionPanel.PopulateList` | **public** 实例方法，无参 → 玩家指挥官选肖像列表的填充点（**换池最可能的挂点**，见 6.8） |
| `SGCharacterCreationPortraitSelectionPanel.FilterList` | `public (filter)`；相关字段 `allPortraitElements` / `sortedList` / `filteredList` / `radioSet` / `_listParent` |
| `PortraitManager.ClearPortraits` | `public (PortraitSettings)` —— 注意它会 `DestroyImmediate` 掉 `renderResults` 里的贴图（见 8.5） |
| `PortraitManager.PORTRAIT_PRESET_FILE_PREFIX` | 预设文件前缀（找预设清单来源的线索，见 6.8 待确认项 2） |
| 按键 | `UnityEngine.Input.GetKeyDown` 可用（`UnityEngine.CoreModule.dll` 里有）。游戏另有 InControl，不依赖 |

---

## 13. 附：已确认的游戏基础事实

| 项 | 值 |
|---|---|
| 游戏版本 | BattleTech **1.9.1**（Build 686R） |
| Unity 版本 | **2018.4.2f1**（`UnityPlayer.dll` / `globalgamemanagers` 的版本串） |
| 安装路径 | `%BATTLETECH%` |
| MOD 目录 | `%USERPROFILE%\Documents\My Games\BattleTech\mods` |
| 当前已装 MOD | `BTHanHua`、`BTHanHuaFont`、`HBS`（**一个字节都不要动**）、`PortraitSwap`、`GenderRatio` |
| 生涯模式开局人数 | `SimGameConstants.json` → `CareerMode.StartingMechWarriorCount = 4`（**没有固定名单**，随机生成 ⇒ 受本 MOD 影响） |
| 原版性别权重 | `Pilot.MaleGenerationWeight=44 / FemaleGenerationWeight=46 / NonBinaryGenerationWeight=10` |
| 战役模式开局 4 人 | Glitch / Behemoth / Dekker / Medusa，**性别写死在 pilot JSON 里** ⇒ 不受影响 |

---

## 14. 附：暂时搁置的议题（别在这轮做）

用户曾想给窗口做**真·原版游戏美术风格**，已同意**先搁置**。已核实的事实（省得重新调研）：

- ModLoader **支持** mod 自带 AssetBundle（`ModLoader.ModAssetBundlePaths` +
  `ExpandManifest` 会检查"Prefab 引用的 AssetBundle 必须先加载，且要排在 manifest 前面"）
- **UI 预制体不在 `data\assetbundles\` 里**（那个目录 83 个文件全是机甲模型，4 GB）；
  它们在 Unity 内建 `resources.assets`（286 MB）里，按 id 索引
  （`uixPrfDeco` / `brackets` / `div_Line` / `svg_frame` / `stripe` 关键字都在里面）
- 那些方括号/斜纹是 **`SVGImage` 矢量**，**文件系统里没有对应图片**可直接拿来用
- 唯一正规机制：**Unity 2018.4.2f1 打 AssetBundle + 写进 `mod.json` 的 Manifest**；
  建议先用"一个红方块 Prefab"验证管线（版本兼容 / bundle 名写法 / Manifest 顺序 / shader 解析）
