# PilotCombined — pilot gender ratio + portrait replacement (BATTLETECH)

A single mod that merges **pilot gender spawn ratio** and **pilot portrait replacement** into one
DLL, and adds the rule that ties them together:

> **The portrait pool decides which genders exist. The ratio only distributes inside that space.**

If you have no non-binary portraits, no non-binary pilots are generated — the ratio is renormalised
across the genders you *can* show. Everything is honest about it: the in-game panel greys out
whatever cannot take effect, and says why.

* No game files are modified. Nothing is injected into saves. The mod can be disabled or deleted at
  any time, and existing saves keep working.
* One DLL, one config file, one log file, one customisable 760×652 panel on **F9**.
* UI localisation follows the **game language** (English / 简体中文 today; German and Russian slots
  are ready and can be added with a JSON file, no recompiling).

---

## Contents

| | |
|---|---|
| [Features](#features) | what it does |
| [Requirements](#requirements) | game version, mod loader |
| [Install](#install) | copy one folder |
| [Quick start](#quick-start) | portraits in, press F9 |
| [The core rule](#the-core-rule-portrait-pool-priority) | pool vs. ratio |
| [The F9 panel](#the-f9-panel) | every control, and the greying rules |
| [Custom portraits](#custom-portraits) | folder layout, sizes, limits |
| [Character creation](#character-creation) | why the portrait gallery behaves the way it does |
| [Configuration](#configuration) | `combined.json` |
| [Language](#language) | game-language detection + drop-in translations |
| [Troubleshooting](#troubleshooting) | log lines and known non-issues |
| [Building from source](#building-from-source) | `build.ps1`, tests, tools |
| [Repository layout](#repository-layout) | what each file is |
| [Credits & licensing](#credits--licensing) | Harmony, portraits, game assets |

---

## Features

**1. Pilot gender spawn ratio**

* Set the female / male / non-binary split for **newly generated** pilots (hiring halls, contracts,
  career start) with a slider panel.
* Optional "lock ratio" (e.g. 3:1) so changing the non-binary share keeps the female:male ratio.
* Pilots that already exist in a save are never touched.

**2. Portrait replacement**

* Point the mod at a folder of your own square images and every pilot gets a picture instead of the
  game's 3D-rendered face.
* Deterministic: a given pilot always gets the same image (hash-based), so faces stay consistent
  across a campaign.
* Per-pilot rules available in the config (pin a specific image to a specific callsign).

**3. Portrait pool priority** — the merged rule

* Only genders that actually have portraits are generated.
* The configured ratio is renormalised inside that set.
* One image = one choice in the character-creation portrait gallery; nothing is unreachable or
  duplicated.

**4. In-game panel (F9)**

* Enable/disable portrait replacement live (**no restart**).
* Re-scan the portrait folder live (**no restart**).
* See the actual pool sizes and the actual weights that will be used.
* Everything that cannot take effect is greyed out, with the reason printed above it.

---

## Requirements

* **BATTLETECH 1.9.1-686R** (the last official patch) on Windows.
* The game's **built-in mod loader** — ModTek is *not* required.
  Harmony is shipped with the game, so nothing else has to be installed.
* Nothing else. No .NET install, no runtime, no dependencies.

---

## Install

1. Download this repository (or the release zip).
2. Copy the `mod` folder to your BATTLETECH mods directory and rename it so the layout is:

   ```
   %USERPROFILE%\Documents\My Games\BattleTech\mods\PilotCombined\
       PilotCombined.dll
       mod.json
       combined.json
       combined.json.template
       loc\            en.json  zh.json  de.json  ru.json
       portraits\      female\  male\  other\   README.txt
   ```

   > Keep the folder name `PilotCombined` (it must match `mod.json`'s `Name`).

3. Start the game. The mod appears in the in-game **MODS** screen and is enabled by default.
4. Press **F9** anywhere to open the settings panel.

To uninstall: delete the folder. Saves are unaffected.

---

## Quick start

1. Put your images into `portraits\` by gender:

   ```
   portraits\female\   01.png  02.png  ...      (women)
   portraits\male\     01.png  02.png  ...      (men)
   portraits\other\    01.png  ...              (non-binary / agender / bionic)
   ```

   Square PNGs, 512×512 is ideal (1024×1024 is fine, avoid >2048). Transparent *or* opaque
   backgrounds both work — the mod passes the image through untouched.

2. Start the game, press **F9** and check the pool line:

   ```
   Portrait pools: F 59 / M 12 / Non-binary / agender 4  (total 75)
   Only genders that have portraits are generated: available Female, Male, Non-binary / agender
   ```

3. Add or remove images later and just press **Rescan Images** in the panel — no restart needed
   (the pool is otherwise only scanned at startup).

---

## The core rule: portrait pool priority

Say the pool is `female 59 / male 12 / other 0` and the configured ratio is `80 / 10 / 10`:

| Gender | Configured | Pool | Generated? | Effective |
|---|---|---|---|---|
| Female | 80 % | 59 | yes | **88.9 %** |
| Male | 10 % | 12 | yes | **11.1 %** |
| Non-binary | 10 % | 0 | **no** | 0 % |

The ratio is renormalised across the available genders (the 8:1 female:male *ratio* is preserved).
The log states the effective weights:

```
[PilotCombined] 性别刷出比例已生效（最终权重）：女 8889 / 男 1111 / 非二元性别或无性别 0
                ★ 已按『肖像池优先』裁剪：只生成有替换头像的性别
```

Turn portrait replacement **off** and this rule switches off with it: the ratio applies to all three
genders exactly as configured, and no portrait is touched.

---

## The F9 panel

| Control | Effect |
|---|---|
| **Enable portrait replacement** | master switch. Off = stock portraits, and the ratio becomes independent of images. Applies instantly. |
| **Enable "&lt;non-binary label&gt;"** | whether the third gender takes part at all |
| **"&lt;non-binary label&gt;" share** | slider, capped at 99 % |
| **F / M split** | one slider for the female share; male is the remainder |
| **Lock the Female:Male ratio** | keeps e.g. 3:1 while you move the non-binary share |
| **Ratio  F : M** | type `3:2` (or `23:22`) and press Enter |
| **Apply & Save** | writes the ratio and the portrait switch to `combined.json` |
| **Rescan Images** | re-scans `portraits\` and clears the rendered-portrait cache |
| **Restore Defaults** | back to the vanilla 46 / 44 / 10 |
| **Close** | close the panel (F9 also toggles) |

**Greying rules** (the panel never lies):

* replacement off → nothing is greyed; the ratio is independent of images;
* the third gender has no portraits → its checkbox and slider are greyed;
* female *or* male has no portraits → the F/M slider, the lock checkbox and the ratio box are all
  greyed (the ratio cannot take effect), and the status line says so;
* no portraits at all → the master switch cannot be turned on.

---

## Custom portraits

* **Folder name decides the gender** — `female` / `male` / `other` (many aliases are accepted:
  `f`, `woman`, `female`, `女`, `女性`; `m`, `man`, `男`, `男性`; `nonbinary`, `nb`, `robot`, `mech`,
  `无性别`, `非二元`, `中性`, `机器人`, …). Unrecognised folders are reported in the log and counted
  as *untagged* (usable by anyone).
* **File name prefix also works**: `f_01.png`, `m_01.png`, `other_01.png`.
* The pool is scanned once at startup (or on **Rescan Images**); sub-folders are scanned recursively.
* Deterministic assignment (`poolMode: hash`): each pilot def always maps to the same image.
* `portraits\README.txt` (shipped) documents all of this in Chinese.

Limits worth knowing:

* The character-creation gallery has **90 slots**, so a single gender pool can show at most 90
  images in that list. Extra images are still used for randomly generated pilots.
* Images are cached at 4 sizes (64/128/256/512) — very large files cost VRAM for no visual gain.

---

## Character creation

The portrait gallery of the player character is rebuilt by the mod:

* **the number of slots equals the number of images you have** for the selected pronoun — one image
  per slot, no duplicates, nothing unreachable;
* selecting **"they / them"** shows **all** your images (deliberate: it is the "unrestricted" option);
* thumbnails are written directly onto the slots every frame, so nothing can leak across genders;
* a slot whose preset cannot be read is hidden rather than showing an uncontrolled image.

The player commander's own gender still comes from the pronoun selector, and the four campaign-mode
starters are fixed by the game — the ratio only affects *randomly generated* pilots.

---

## Configuration

Everything lives in `mods\PilotCombined\combined.json` (a commented template is shipped as
`combined.json.template`). Three sections:

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

An older single-mod config (`genderratio.json` / `portraits.json`) is migrated automatically on
first run.

`popup.ui_language`: `auto` (follow the game), or force `en` / `zh` / `de` / `ru`.

---

## Language

The panel follows the **game's** language setting (`Localize.Strings.CurrentCulture`), with a
translation-sampling fallback for localisation mods that only replace text.

Add or change a language **without recompiling** — drop a file into `loc\`:

```
loc\en.json   loc\zh.json   loc\de.json   loc\ru.json
```

```json
{
  "//": "keys starting with // are comments",
  "win.title": "Pilot Spawn Ratio",
  "chk.other": "Enable \"{0}\""
}
```

* resolution order: `loc\<lang>.json` → built-in table → English → the key itself (never blank);
* `{0}`/`{1}` are `string.Format` placeholders — keep them, and keep their count;
* a mistyped key is named in the log at startup;
* `tools\dump_loc.py` regenerates those four files from the built-in table, so they never drift.

---

## Troubleshooting

**Log file:** `mods\PilotCombined\PilotCombined.log` (rewritten on every start).

Lines worth knowing:

```
界面语言：Chinese（游戏语言设置=CULTURE_ZH_CN（2）…）      ← language detection
图片池张数（影响可用性别）：女 59 / 男 12 / …              ← pool census
角色头像列表已重建：代词=女，池子 59 张 … 显示 59 格 …      ← creation gallery
性别刷出比例已生效（最终权重）：… ★ 已按『肖像池优先』裁剪    ← effective weights
分配 -> pilotDefId=… | pool/hash/female[40] | portraits/female/20.png
```

Known non-issues:

* `MessageCenter [ERROR] CRITICAL ERROR` right after quitting is a vanilla analytics null-reference
  on shutdown, not this mod.
* Images added while the game is running are invisible until **Rescan Images** (or a restart) —
  the pool is scanned once by design.

---

## Building from source

Windows + the game's `BattleTech_Data\Managed` assemblies (the build script finds them via the
Steam path; see `build.ps1`).

```powershell
# compile + run the offline test suite (130 assertions) + lint the deployed files
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test

# stage to out\ and deploy to the game's mods folder
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Deploy

# build the release zip (dist\PilotCombined-<version>.zip) for a GitHub Release
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Pack
```

`-Pack` writes a zip containing `PilotCombined\` with the built DLL, `mod.json`, the commented
`combined.json`, `loc\` and an **empty** `portraits\` (your own images never leak into a release).
Users cannot compile the mod themselves — it needs the game's assemblies — so a release must ship
this zip.

The compiler is the .NET Framework `csc.exe` shipped with Windows
(`/langversion:5 /codepage:65001`), so no SDK is required. Helper scripts:

| Script | Purpose |
|---|---|
| `tools\check_dll.ps1` | reflection assertions over the built DLL (entry point, patch set, no duplicate save patch) |
| `tools\verify_deploy.py` | validates the deployed folder: JSON parses, pool census, DLL is the one just built |
| `tools\check_publish.py` | pre-publish audit over the **tracked** files: secrets/credentials, files > 50 MB, local absolute paths or usernames, binary inventory |
| `tools\check_encoding.py` | catches GBK mojibake in sources/docs |
| `tools\check_loc.py` | catches UI strings that bypass the localisation layer |
| `tools\dump_loc.py` | regenerates `loc\*.json` from the built-in table |
| `tools\resolve_gamedir.ps1` | shared game-directory locator (dot-sourced by `build.ps1`, `check_dll.ps1`, `probe_api.ps1`) |
| `tools\probe_api.ps1`, `tools\iltool.py` | reflection / IL inspection of the game assemblies (used to verify game APIs before writing patches) |

### Releasing

Releases are published by `.github/workflows/attach-release-asset.yml` — no local
`gh release upload` (some networks cannot reach `uploads.github.com`):

```powershell
build.ps1 -Pack                                   # -> dist\PilotCombined-0.2.0.zip
copy dist\PilotCombined-0.2.0.zip .               # the workflow uploads a repo-root zip
# write docs\release-notes\v0.2.0.md, then:
git tag -a v0.2.0 -m "v0.2.0"; git push origin v0.2.0
```

The workflow creates (or updates) the Release, uses `docs/release-notes/<tag>.md` as the body and
attaches the zip; it is safe to re-run. Verify by downloading the asset and comparing SHA-256.

> Two notes for future releases:
> * build and deploy in **one** invocation (`build.ps1 -Deploy -Pack`) so the DLL that is deployed
>   to your game folder is byte-identical to the one inside the release zip;
> * `csc` output is **not** byte-reproducible (a fresh compile gets a new MVID/timestamp), so
>   "compile again and compare hashes" is not a valid check — compare the **downloaded asset**
>   against the uploaded one instead.

---

## Repository layout

```
src\           19 C# files, compiled into the single PilotCombined.dll
tests\         offline test harness (math, config migration, pool classification)
mod\           the payload that gets deployed (mod.json, combined.json, loc\, portraits\)
tools\         verification and inspection scripts
docs\          notes: game-mechanics analysis, UI notes, progress log
build.ps1      build / test / deploy
移交文档-融合MOD.md   the original design & handover document
```

---

## Credits & licensing

* **Code:** MIT — see [LICENSE](LICENSE).
* **Harmony:** the mod patches through Harmony, which is shipped with the game (MIT licensed by
  Andreas Pardeike). Nothing is redistributed here.
* **Portraits included in this repository:** AI-generated locally with ComfyUI (Stable-Diffusion-family
  models), then background-removed with a local script. They are not game assets and contain no
  Harebrained Schemes artwork. See [docs/素材与许可.md](docs/素材与许可.md) for the provenance and
  the model-licence checklist (if you were the one who generated them, verify your checkpoint's and
  LoRAs' licences before publishing).
* **BATTLETECH** and **MechWarrior** are trademarks of their respective owners. This mod contains no
  game assets, requires a legitimate copy of the game, and is not affiliated with or endorsed by
  Harebrained Schemes or Paradox Interactive.
