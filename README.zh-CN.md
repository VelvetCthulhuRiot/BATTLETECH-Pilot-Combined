# PilotCombined —— 驾驶员性别比例 + 头像替换（BATTLETECH 整合 MOD）

把「**驾驶员性别刷出比例**」和「**驾驶员头像替换**」合成一个 DLL，并加上把两者绑在一起的那条规则：

> **肖像池决定"有哪些性别可用"，性别比例只在这个可用空间内分配。**

如果你没有非二元性别的头像，就不会生成非二元性别的驾驶员 —— 比例会在"你真的有图"的性别之间
重新归一。界面会**如实**告诉你这件事：所有不可能生效的控件都会变灰，并在上面写明原因。

* **不修改游戏任何原文件**，不往存档里塞东西，随时可以停用或删除，读档不受影响。
* 一个 DLL、一份配置、一个日志、一个 **F9** 唤出的 760×652 自绘面板。
* 界面语言**跟随游戏语言**（当前支持 English / 简体中文；德文、俄文的位置已留好，
  加语言只需放一个 JSON 文件，**不用重新编译**）。

[English README](README.md) · [更新记录](CHANGELOG.md) · [设计/交接文档](移交文档-融合MOD.md) · [开发进展记录](docs/进展记录.md)

---

## 目录

| | |
|---|---|
| [功能](#功能) | 这个 MOD 做什么 |
| [环境要求](#环境要求) | 游戏版本、前置 |
| [安装](#安装) | 拷一个文件夹 |
| [快速上手](#快速上手) | 放图 → 按 F9 |
| [核心规则](#核心规则肖像池优先) | 池子与比例的关系 |
| [F9 面板](#f9-面板) | 每个控件的作用与灰化规则 |
| [自定义头像](#自定义头像) | 目录规则、尺寸、上限 |
| [角色创建界面](#角色创建界面) | 头像列表为什么是现在这样 |
| [配置文件](#配置文件) | `combined.json` |
| [多语言](#多语言) | 自动判定 + 放文件即翻译 |
| [排查问题](#排查问题) | 日志与已知的"假报错" |
| [从源码构建](#从源码构建) | `build.ps1`、测试、工具 |
| [仓库结构](#仓库结构) | 每个目录是什么 |
| [鸣谢与许可](#鸣谢与许可) | Harmony、头像素材、游戏素材 |

---

## 功能

**1. 驾驶员性别刷出比例**

* 用滑块面板设置**新生成**驾驶员的 女 / 男 / 非二元 比例（招募市场、合约敌方、生涯开局）。
* 可选的「锁定比例」（例如 3:1）：调整非二元占比时，男女比值保持不变。
* **存档里已有的驾驶员永远不受影响。**

**2. 驾驶员头像替换**

* 指向一个你自己的正方形图片目录，所有驾驶员都会用图片代替游戏 3D 渲染的脸。
* **确定性分配**（按哈希）：同一个驾驶员永远拿到同一张图，一个战役里脸不会变来变去。
* 配置里可以给特定驾驶员（例如按呼号）固定某张图。

**3. 肖像池优先** —— 融合后的新规则

* **只生成真的有头像的那些性别。**
* 配置的比例在这个可用集合内**重新归一**。
* 角色创建界面里，**一张图 = 一个可选格子**，不会出现"有图却选不到"或"同一张图重复出现"。

**4. 游戏内面板（F9）**

* 运行中**开关头像替换**（不用重启）。
* 运行中**重扫图片目录**（不用重启）。
* 显示真实的池子张数与**真正会生效的权重**。
* 一切不可能生效的控件都会变灰，并在上方写明原因。

---

## 环境要求

* **BATTLETECH 1.9.1-686R**（最后一个官方补丁），Windows。
* 游戏**自带的 MOD 加载器**即可 —— **不需要 ModTek**。
  Harmony 随游戏一起提供，所以不用另外装任何东西。
* 没有其它依赖：不装 .NET、不装运行库。

---

## 安装

1. 下载本仓库（或 Release 里的压缩包）。
2. 把 `mod` 目录拷进游戏的 mods 目录，并改名为 `PilotCombined`，最终结构为：

   ```
   %USERPROFILE%\Documents\My Games\BattleTech\mods\PilotCombined\
       PilotCombined.dll
       mod.json
       combined.json
       combined.json.template
       loc\            en.json  zh.json  de.json  ru.json
       portraits\      female\  male\  other\   README.txt
   ```

   > 文件夹名必须是 `PilotCombined`（要和 `mod.json` 里的 `Name` 一致）。

3. 启动游戏。在游戏内的 **MODS（模组）** 界面能看到它，默认已启用。
4. 在任何界面按 **F9** 打开设置面板。

卸载：删掉那个文件夹即可，存档不受影响。

---

## 快速上手

1. 按性别把图片放进 `portraits\`：

   ```
   portraits\female\   01.png  02.png  ...      （女）
   portraits\male\     01.png  02.png  ...      （男）
   portraits\other\    01.png  ...              （非二元 / 无性别 / 仿生人）
   ```

   正方形 PNG，**512×512 最合适**（1024 也行；不要超过 2048）。透明底或不透明底**都可以**，
   MOD 原样透传，不做任何抠图或合成。

2. 启动游戏，按 **F9** 看池子那一行：

   ```
   肖像池：女 59 / 男 12 / 非二元性别或无性别 4（共 75 张）
   只生成有肖像的性别：可用 女、男、非二元性别或无性别
   ```

3. 以后增删图片，直接按面板上的 **「立即重扫图片池」** 即可生效，**不用重启**
   （否则图片池只在启动时扫描一次）。

---

## 核心规则：肖像池优先

假设池子是 `女 59 / 男 12 / 其他 0`，配置比例是 `80 / 10 / 10`：

| 性别 | 配置 | 池子 | 会生成吗 | 实际生效 |
|---|---|---|---|---|
| 女 | 80 % | 59 | 会 | **88.9 %** |
| 男 | 10 % | 12 | 会 | **11.1 %** |
| 非二元 | 10 % | 0 | **不会** | 0 % |

比例会在可用性别之间重新归一（女:男 仍然是 8:1）。日志里会写出真正生效的权重：

```
[PilotCombined] 性别刷出比例已生效（最终权重）：女 8889 / 男 1111 / 非二元性别或无性别 0
                ★ 已按『肖像池优先』裁剪：只生成有替换头像的性别
```

**把头像替换关掉，这条规则也会跟着关掉**：比例完全按配置作用在三个性别上，头像一个都不换。

---

## F9 面板

| 控件 | 作用 |
|---|---|
| **启用头像替换** | 总开关。关掉 = 完全走游戏原版肖像机制，且比例不再受图片影响。**立即生效**。 |
| **启用「非二元性别或无性别」** | 第三类性别是否参与生成 |
| **「非二元性别或无性别」占比** | 滑块，上限 99% |
| **女 / 男 分配** | 一根滑块控制女性占比，男性取剩余 |
| **锁定男女比例** | 调整非二元占比时保持例如 3:1 的男女比值 |
| **锁定比例 女:男** | 输入 `3:2`（或 `23:22`）后回车 |
| **应用并写回配置** | 把比例与头像开关写进 `combined.json` |
| **立即重扫图片池** | 重扫 `portraits\`，并清掉游戏侧已渲染的头像缓存 |
| **恢复默认** | 回到原版默认 46 / 44 / 10 |
| **关闭** | 关面板（F9 也可以开关） |

**灰化规则（界面不许骗人）**：

* 关掉替换 → 什么都不灰，比例与图片无关；
* 第三类没有头像 → 它的勾选框与滑块变灰；
* 女或男**缺任一方** → 女/男滑块、锁定勾选框、比例输入框**全部变灰**（比例无法生效），状态行写明；
* 一张图都没有 → 总开关不可勾选。

---

## 自定义头像

* **目录名决定性别** —— `female` / `male` / `other`，并认很多别名：
  `f`、`woman`、`女`、`女性`；`m`、`man`、`男`、`男性`；
  `nonbinary`、`nb`、`robot`、`mech`、`无性别`、`非二元`、`中性`、`机器人` …
  认不出的目录名会在日志里点名，并计入"未标注"（任何驾驶员都可能分到）。
* **文件名前缀也等效**：`f_01.png`、`m_01.png`、`other_01.png`。
* 图片池在启动时扫描一次（或在面板上点「立即重扫图片池」）；子目录会递归扫描。
* 确定性分配（`poolMode: hash`）：同一个驾驶员定义永远映射到同一张图。
* 随 MOD 附带的 `portraits\README.txt` 里也写了这些。

两个需要知道的上限：

* 角色创建的头像列表**只有 90 个格子**，所以单个性别的池子最多能在那个列表里显示 90 张
  （超出的图仍然会给随机生成的驾驶员用）。
* 图片会被缓存成 4 档尺寸（64/128/256/512），过大的图只会白吃显存。

---

## 角色创建界面

玩家指挥官的头像列表由本 MOD 重建：

* **格子数 = 你为该代词准备的图片数** —— 一张图一个格子，不重复、不遗漏；
* 选 **「他们」** 会显示**全部**图片（有意如此：它就是"不限制性别"那一档）；
* 缩略图每帧直接写在格子上，所以不会再出现"女性列表里混进仿生人"这种事；
* 读不到预设信息的格子会被**藏起来**，而不是显示一张不受控的图。

玩家指挥官本人的性别仍由你在创建界面选的**代词**决定；战役模式开局固定的 4 位同伴由游戏写死。
比例只作用于**随机生成**的驾驶员。

---

## 配置文件

全部在 `mods\PilotCombined\combined.json`（带注释的模板随 MOD 一起发布为 `combined.json.template`）。
三段：

```jsonc
{
  "gender_ratio": { "enabled": true, "female_percent": 80, "male_percent": 10,
                    "other_percent": 10, "other_label": "非二元性别或无性别",
                    "lock_ratio": true, "lock_ratio_female": 8, "lock_ratio_male": 1 },
  "popup":        { "enabled": true, "hotkey": "F9", "step_percent": 1,
                    "font_file": "", "ui_language": "auto" },
  "portraits":    { "enabled": true, "autoScan": true, "poolMode": "hash",
                    "defaultPortrait": "", "pool": [], "rules": [] }
}
```

旧版单 MOD 的配置（`genderratio.json` / `portraits.json`）会在首次运行时自动迁移。

`popup.ui_language`：`auto`（跟随游戏）或强制 `en` / `zh` / `de` / `ru`。

`popup.font_file`：留空 = 自动从游戏界面借字体（推荐）；也可以写系统已安装的字体名。

---

## 多语言

面板语言跟随**游戏**的语言设置（`Localize.Strings.CurrentCulture`），并对"只替换文本、
不改语言设置"的汉化 MOD 做了抽样兜底。

**加语言或改文案不用重新编译** —— 往 `loc\` 里放文件即可：

```
loc\en.json   loc\zh.json   loc\de.json   loc\ru.json
```

```json
{
  "//": "以 // 开头的键是注释",
  "win.title": "Pilot Spawn Ratio",
  "chk.other": "Enable \"{0}\""
}
```

* 取值顺序：`loc\<语言>.json` → 内置表 → 英文 → 键名（**永远不会空白**）；
* `{0}`/`{1}` 是 `string.Format` 占位符，**必须保留**，个数也不能变；
* 键拼错时启动日志会点名；
* `tools\dump_loc.py` 会从内置表重新生成这四个文件，避免两者脱节。

---

## 排查问题

**日志文件**：`mods\PilotCombined\PilotCombined.log`（每次启动会重写）。

值得认识的几行：

```
界面语言：Chinese（游戏语言设置=CULTURE_ZH_CN（2）…）      ← 语言判定
图片池张数（影响可用性别）：女 59 / 男 12 / …              ← 池子普查
角色头像列表已重建：代词=女，池子 59 张 … 显示 59 格 …      ← 创建界面列表
性别刷出比例已生效（最终权重）：… ★ 已按『肖像池优先』裁剪    ← 实际生效权重
分配 -> pilotDefId=… | pool/hash/female[40] | portraits/female/20.png
```

**已知的"假报错"**：

* 退出游戏时出现 `MessageCenter [ERROR] CRITICAL ERROR` —— 那是原版在关停时的空引用，
  与本 MOD 无关。
* 游戏运行中往目录里加图不会生效，需要点「立即重扫图片池」或重启 —— 这是刻意设计
  （图片池只扫一次，机制层读取才够便宜）。

---

## 从源码构建

需要 Windows + 游戏自带的 `BattleTech_Data\Managed` 程序集（构建脚本按 Steam 路径查找，见 `build.ps1`）。

```powershell
# 编译 + 跑离线测试（130 项断言）+ 校验部署产物
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test

# 打包到 out\ 并部署到游戏 mods 目录
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Deploy

# 生成发布用压缩包 dist\PilotCombined-<版本>.zip（挂到 GitHub Release 用）
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Pack
```

`-Pack` 会打包出 `PilotCombined\` 目录（含编译好的 DLL、`mod.json`、带注释的 `combined.json`、
`loc\`，以及**空的** `portraits\` —— 你自己的图片不会漏进发布包）。
**别人没法自己编译本 MOD**（需要游戏自带的程序集），所以发布时必须提供这个压缩包。

编译器用 Windows 自带的 .NET Framework `csc.exe`（`/langversion:5 /codepage:65001`），
**不需要安装 SDK**。辅助脚本：

| 脚本 | 作用 |
|---|---|
| `tools\check_dll.ps1` | 对编译产物做反射断言（入口点、补丁集合、存档补丁不得重复） |
| `tools\verify_deploy.py` | 校验部署目录：JSON 可解析、图片池普查、DLL 与刚编出的一致（哈希） |
| `tools\check_publish.py` | **发布前审计**（只看已跟踪文件）：密钥/凭据、超过 50MB 的文件、本机绝对路径或用户名、二进制清单 |
| `tools\check_encoding.py` | 抓源码/文档里的 GBK 乱码 |
| `tools\check_loc.py` | 抓"没走多语言层"的界面文案 |
| `tools\dump_loc.py` | 从内置表重新生成 `loc\*.json` |
| `tools\resolve_gamedir.ps1` | 共享的"找游戏目录"脚本（`build.ps1` / `check_dll.ps1` / `probe_api.ps1` 都引用它） |
| `tools\probe_api.ps1`、`tools\iltool.py` | 反射 / IL 工具，用来在写补丁前核实游戏 API 的真实签名 |

### 发版

由 `.github/workflows/attach-release-asset.yml` 完成（不用本地 `gh release upload` ——
部分网络环境连不上 `uploads.github.com`）：

```powershell
build.ps1 -Pack                                   # -> dist\PilotCombined-0.2.0.zip
copy dist\PilotCombined-0.2.0.zip .               # 工作流上传的是仓库根目录里的 zip
# 写好 docs\release-notes\v0.2.0.md，然后：
git tag -a v0.2.0 -m "v0.2.0"; git push origin v0.2.0
```

工作流会创建（或更新）Release、用 `docs/release-notes/<tag>.md` 当说明、挂上 zip，**可安全重跑**。
验证方式：把资产**下载回来比 SHA-256**。

---

## 仓库结构

```
src\           19 个 C# 文件，编译成单个 PilotCombined.dll
tests\         离线测试（数学、配置迁移、图片池分类）
mod\           要部署的东西（mod.json、combined.json、loc\、portraits\）
tools\         校验与探查脚本
docs\          笔记：游戏机制分析、UI 实现笔记、开发进展记录
build.ps1      编译 / 测试 / 部署
移交文档-融合MOD.md   最初的设计与交接文档
```

---

## 鸣谢与许可

* **代码**：MIT —— 见 [LICENSE](LICENSE)。
* **Harmony**：本 MOD 通过 Harmony 挂钩，Harmony 随游戏提供（Andreas Pardeike，MIT）。
  本仓库不重新分发它。
* **随仓库附带的头像**：用本地 ComfyUI（Stable Diffusion 系模型）生成，再用本地脚本抠图处理。
  不是游戏素材，也不含 Harebrained Schemes 的美术资源。
  出处与**模型许可核对清单**见 [docs/素材与许可.md](docs/素材与许可.md)
  （若你就是生成者，发布前请确认 checkpoint 与 LoRA 的许可条款）。
* **BATTLETECH**、**MechWarrior** 是各自权利人的商标。本 MOD 不含任何游戏素材，
  需要正版游戏，与 Harebrained Schemes / Paradox Interactive 无隶属或背书关系。
