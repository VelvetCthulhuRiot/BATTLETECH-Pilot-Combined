using System;
using System.Collections.Generic;
using System.Reflection;
using BattleTech;
using BattleTech.Data;
using BattleTech.Portraits;
using BattleTech.UI;
using Harmony;
using UnityEngine;
using UnityEngine.UI;      // RawImage（PortraitSelectorElement.thumbnail 的真实类型）

// 本文件在 PilotCombined 命名空间里，而 PortraitRegistry 在**子**命名空间 PilotCombined.Portrait，
// 直接写名字看不见 —— 用类型别名最清楚（也避免和 using BattleTech.Portraits 混淆）。
using PsRegistry = PilotCombined.Portrait.PortraitRegistry;

namespace PilotCombined
{
    /// <summary>
    /// 角色创建的「角色头像」列表：**把格子变成"你的图片库"**（用户决策 X）。
    ///
    /// <para><b>为什么要这么做</b>（真机日志实证）：游戏的列表格子数是**按预设性别固定**的 ——
    /// 女 44 格 / 男 45 格 / 非二元仅 1 格（共 90 格）。而"每个格子按哈希分一张图"
    /// 意味着**能出现的不重复图片数 ≤ 该性别的格子数**：</para>
    /// <list type="bullet">
    ///   <item>女：59 张图 / 44 格 ⇒ 最多 44 张能出现，**15 张永远选不到**；</item>
    ///   <item>非二元：4 张图 / **1 格** ⇒ 4 张仿生人只能出现 1 张；</item>
    ///   <item>男：12 张图 / 45 格 ⇒ 都能出现，但同脸重复约 3.75 次。</item>
    /// </list>
    ///
    /// <para><b>做法</b>：把"格子"和"预设性别"**解耦** ——</para>
    /// <list type="number">
    ///   <item>按所选代词挑池子：女 ⇒ <c>portraits\female\</c>；男 ⇒ <c>portraits\male\</c>；
    ///         <b>「他们」⇒ 全部图片</b>（用户明确要求把「他们」当作"不限制性别"处理）；</item>
    ///   <item>格子数 = 池子里的图片数（<c>M</c>）：只显示前 <c>min(M, 90)</c> 个格子，其余隐藏；</item>
    ///   <item>第 i 个可见格子 → 池子里的第 i 张图（一对一，**不重复、不遗漏**），
    ///         通过 <c>PortraitRegistry.SetGalleryOverride</c> 指定；</item>
    ///   <item>指定之后强制重画这几个格子的缩略图（否则会一直显示哈希分到的那张旧图）；</item>
    ///   <item>离开创建界面时清理映射，但**保留玩家最终选中的那一张** ——
    ///         否则创建完指挥官，头像会变回哈希分配的那张。</item>
    /// </list>
    ///
    /// <para>效果：选「他」12 格 / 选「她」59 格 / 选「他们」75 格（就用户当前的图片数），
    /// 每个格子一张图、张张不同。想更多格子就往对应目录加图。</para>
    ///
    /// ⚠️ 与 <c>GenderRatioUi</c> 共用 <c>GameInstance.Update</c>：那边是 **Prefix**（F9 热键），
    /// 这边是 **Postfix**（列表），两件事互不干扰，不是"重复挂点"。
    /// </summary>
    internal static class PortraitGalleryFilter
    {
        /// <summary>找不到界面时，每隔这么多帧才重新 FindObjectOfType 一次（它不便宜）。</summary>
        private const int RefindEveryFrames = 30;

        /// <summary>改完映射后再重画几帧缩略图（压掉游戏自己可能还在飞的首次渲染）。</summary>
        private const int RefreshFrames = 2;

        // ---- 反射缓存 ----
        private static FieldInfo _fPortraitSelection;    // screen.PortraitSelection（private）
        private static FieldInfo _fNamePanel;            // screen.Name（private）
        private static FieldInfo _fAllElements;          // panel.allPortraitElements（private）
        private static FieldInfo _fElementSettings;      // element.portraitSettings（private）
        private static FieldInfo _fThumbnail;            // element.thumbnail : RawImage（private）
        private static PropertyInfo _pPortraitManager;   // DataManager.PortraitManager（nonpublic）
        private static bool _refReady;
        private static bool _refFailed;

        private static SGCharacterCreationNameAndAppearanceScreen _screen;
        private static GameInstance _game;
        private static int _missFrames;
        private static int _errorFrames;

        private static string _lastSignature = "";
        private static string _lastSelectedId = "";
        private static bool _applied;                    // 是否已经写过映射（离开时才知道要不要清理）
        private static int _refreshFrames;
        private static int _refreshStarted;

        /// <summary>当前"格子 → 图片"的分配结果（每帧据此把图直接贴到格子上）。</summary>
        private static readonly Dictionary<string, string> _assigned = new Dictionary<string, string>();

        /// <summary>缩略图用这一档尺寸（RawImage 会自己缩放，随便哪一档看起来都一样）。</summary>
        private const int ThumbSize = 128;
        private static readonly List<PortraitSelectorElement> _refreshList = new List<PortraitSelectorElement>();

        // ---- M8：代词选择器（"藏掉没有替换肖像的性别"）----
        private static FieldInfo _fPronounSelector;      // namePanel.pronounSelector : HorizontalScrollSelectorText（private）
        private static PropertyInfo _pHePronoun;         // namePanel.hePronoun（nonpublic）
        private static PropertyInfo _pShePronoun;        // namePanel.shePronoun（nonpublic）
        private static PropertyInfo _pTheyPronoun;       // namePanel.theyPronoun（nonpublic）
        private static int _prunedScreenId;              // 已经裁剪过选项的那个界面实例（0 = 还没做）
        private static string _prunedSignature = "";     // 池子情况变了就重新裁剪

        // ================================================================ 安装

        internal static bool TryInstall(HarmonyInstance harmony)
        {
            if (harmony == null) return false;
            try
            {
                MethodInfo update = typeof(GameInstance).GetMethod("Update",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new Type[] { typeof(float) }, null);
                if (update == null)
                {
                    Log.Error("找不到 GameInstance.Update(float)，无法安装『角色头像列表』处理。");
                    return false;
                }

                MethodInfo post = typeof(PortraitGalleryFilter).GetMethod("Update_Postfix",
                    BindingFlags.Static | BindingFlags.NonPublic);
                harmony.Patch(update, null, new HarmonyMethod(post), null);

                Log.Info("已安装『角色头像列表 = 你的图片库』（用户决策 X）："
                         + "按所选代词选池子（「他们」= 全部图片），格子数 = 该池子的图片数，"
                         + "一个格子一张图、不重复不遗漏。");
                return true;
            }
            catch (Exception e)
            {
                Log.Error("安装『角色头像列表』失败（不影响其它功能）：" + e.Message);
                return false;
            }
        }

        /// <summary>每帧调用（<c>GameInstance.Update</c> 的 Postfix）。任何异常都吞掉。</summary>
        internal static void Update_Postfix(GameInstance __instance)
        {
            if (__instance != null) _game = __instance;
            try
            {
                Poll();
                _errorFrames = 0;
            }
            catch (Exception e)
            {
                _errorFrames++;
                if (_errorFrames == 1)
                {
                    Log.Warn("角色头像列表处理这一帧出错（会继续试）：" + e.GetType().Name + " " + e.Message);
                }
                else if (_errorFrames == 300)
                {
                    _refFailed = true;
                    Log.Error("角色头像列表处理连续出错 300 帧，已停用该功能（不影响其它功能）："
                              + e.GetType().Name + " " + e.Message);
                }
            }
        }

        // ================================================================ 每帧逻辑

        private static void Poll()
        {
            if (_refFailed) return;
            if (!_refReady && !EnsureReflection()) return;

            // ★ 头像替换被关掉（F9 窗口的总开关 / combined.json 的 portraits.enabled）：
            //   立刻清掉我们写过的所有"格子→图片"指定，游戏完全走原版肖像机制。
            if (!PsRegistry.Enabled)
            {
                if (_applied || PsRegistry.GalleryOverrideCount > 0)
                {
                    _applied = false;
                    _refreshFrames = 0;
                    _refreshList.Clear();
                    _assigned.Clear();
                    PsRegistry.ClearGalleryOverrides(null);
                    Log.Info("头像替换已关闭 ⇒ 已清掉头像列表的『格子→图片』指定，完全回到游戏原版肖像机制。");
                }
                return;
            }

            if (_screen == null)
            {
                if (_applied) Cleanup();                      // 离开角色创建界面
                if (++_missFrames < RefindEveryFrames) return;
                _missFrames = 0;
                _screen = UnityEngine.Object.FindObjectOfType<SGCharacterCreationNameAndAppearanceScreen>();
                if (_screen == null) return;
                _lastSignature = "";
                Log.Info("检测到角色创建界面：准备把头像列表变成『一个格子一张你的图』。");
            }

            // 延迟重画：映射改完之后连着几帧把缩略图重画一遍，压掉游戏可能还在进行的首次渲染
            if (_refreshFrames > 0)
            {
                _refreshFrames--;
                for (int i = 0; i < _refreshList.Count; i++) RenderThumb(_refreshList[i]);
                if (_refreshFrames == 0)
                {
                    _refreshList.Clear();
                    if (_refreshStarted > 0)
                    {
                        Log.Info("已重新渲染 " + _refreshStarted + " 个格子的缩略图（都换成指定的那张图）。");
                        _refreshStarted = 0;
                    }
                }
            }

            SGCharacterCreationPortraitSelectionPanel panel = GetPanel();
            if (panel == null) return;

            // ★ M8 的"把没有肖像的性别从代词选择器里删掉"**绝对不能做**：
            //   游戏的 SGCharacterCreationNamePanel.get_gender() 是**按选项下标**算性别的 ——
            //   IL: `pronounSelector` → `HorizontalScrollSelector::get_selectionIdx`。
            //   删掉一个选项会让后面所有选项的下标整体错位：选「她」会被当成「男」、
            //   选「他们」会被当成「女」（真机实测：女性列表串进仿生人、「他们」反而显示 59 张女性图）。
            //   替代做法见下面"池子为空 ⇒ 退回全部图片"的分支。详见 docs/进展记录.md §18。
            //   （下面的 PrunePronounOptions 方法保留作参考，但**不要调用**。）

            // 记住玩家当前选中的那张（离开界面时要保留它的映射）
            try
            {
                PortraitSelectorElement sel = panel.selectedPortrait;
                if (sel != null)
                {
                    string id = ElementId(sel);
                    if (!string.IsNullOrEmpty(id) && id != "?") _lastSelectedId = id;
                }
            }
            catch { }

            if (!panel.ListPopulated) return;

            List<PortraitSelectorElement> elements = GetElements(panel);
            if (elements == null || elements.Count == 0) return;

            Gender chosen = GetChosenGender();
            List<string> pool = PoolFor(chosen);
            bool fellBack = false;
            if (pool.Count == 0)
            {
                // ★ 该性别**没有替换肖像**（例如 portraits\male 是空的）。
                //   绝对**不能**因此把该性别的代词选项从选择器里删掉 —— 游戏的 get_gender()
                //   按选项下标算性别，删一项会让下标错位（见 EnablePronounPruning 的注释）。
                //   ⇒ 退回"全部图片"：列表照常可用、每张图都能被选到，并在日志里说明。
                pool = PsRegistry.AllPaths();
                fellBack = true;
            }
            if (pool.Count == 0)
            {
                // 连一张图都没有（正常走不到：M6 会把替换整体关掉，本组件就不会安装）
                if (_lastSignature != "empty")
                {
                    _lastSignature = "empty";
                    Log.Warn("角色头像列表：一张替换肖像都没有，本次不做任何过滤/指定。");
                }
                return;
            }
            if (fellBack && _lastSignature != "fallback:" + chosen)
            {
                _lastSignature = "fallback:" + chosen;
                Log.Warn("角色头像列表：代词=" + GenderWord(chosen) + " 对应的图片池是空的"
                         + "（" + PoolFolder(chosen) + "）⇒ **本次退回『全部图片』**（" + pool.Count + " 张），"
                         + "让你仍然能正常挑图。想按性别挑图就往那个目录里放图；"
                         + "「肖像池优先」仍然会让新驾驶员只在有图的性别里生成。");
            }

            int wantVisible = Math.Min(pool.Count, elements.Count);

            // ★ 每帧做一次**一致性检查**：可见格子显示的那张图必须正好是 pool[i]。
            //
            //   为什么不能只靠"签名变了才重算"：真机上出现过"女性列表里冒出仿生人"，
            //   原因是**残留的旧指定**与**可见集合变化**叠加 ——
            //   例如先在「他们」里给 63 个格子指定过图（含 4 张仿生人），切到「她」后
            //   只重算前 59 个格子，第 60~89 个格子上仍留着旧指定（其中就有仿生人）；
            //   一旦元素顺序被游戏重排（PopulateList 里的 GetRandomizedSortOrder / 池化复用），
            //   那些"带着仿生人旧指定"的格子就可能变成可见格子。
            //   ⇒ 只有**每帧对照当前池子**才能保证"看到的一定是我指定的那张"。
            bool needAssign = false;
            for (int i = 0; i < wantVisible; i++)
            {
                string id = ElementId(elements[i]);
                if (string.IsNullOrEmpty(id) || id == "?") continue;   // 读不到 id 的格子跳过（下一段会兜底）
                string img;
                if (!_assigned.TryGetValue(id, out img) || img != pool[i % pool.Count])
                {
                    needAssign = true;
                    break;
                }
            }

            string sig = chosen + "|" + pool.Count + "|" + elements.Count + "|" + wantVisible;
            if (needAssign || sig != _lastSignature)
            {
                _lastSignature = sig;
                ApplyAssignment(elements, pool, chosen, wantVisible);
            }

            // 可见性每帧纠正（游戏重建列表 / 切换代词之后能自愈），
            // 并把"指定的那张图"**直接贴到 RawImage 上**（见 ForceThumbnail 的注释）。
            for (int i = 0; i < elements.Count; i++)
            {
                PortraitSelectorElement el = elements[i];
                if (el == null) continue;
                bool want = i < wantVisible;
                if (want)
                {
                    string id = ElementId(el);
                    string image;
                    if (string.IsNullOrEmpty(id) || id == "?")
                    {
                        // ★ 读不到预设信息的格子：我们**没法**控制它显示哪张图
                        //   （元素是上一帧刚生成的、SetData 的协程还没跑完就会出现这种情况）。
                        //   这种格子显示的是游戏按"预设自身性别"哈希分到的图 ——
                        //   当某个性别的池子是空的、兜底会退化成"整池任取"时，
                        //   就会出现真机上看到的那种"女性列表里冒出仿生人"。
                        //   ⇒ 宁可隐藏这一格，也不让它显示不受控的图。
                        want = false;
                        if (!_unreadableWarned)
                        {
                            _unreadableWarned = true;
                            Log.Warn("有个格子的预设信息暂时读不到，已先把它隐藏（通常下一帧就会恢复；"
                                     + "若一直如此，该格子会被隐藏而不是显示一张不受控的图）。");
                        }
                    }
                    else if (_assigned.TryGetValue(id, out image))
                    {
                        ForceThumbnail(el, id, image);
                    }
                }
                if (el.gameObject.activeSelf != want) el.gameObject.SetActive(want);
            }

            // 选中的格子被隐藏了 ⇒ 自动改选第一个可见格子
            SelectIfHidden(panel, elements, wantVisible);
        }

        private static void ApplyAssignment(List<PortraitSelectorElement> elements,
                                            List<string> pool, Gender chosen, int wantVisible)
        {
            _applied = true;
            _refreshList.Clear();
            _assigned.Clear();

            // 先把**所有**旧指定清掉，再按当前池子重新指定可见格子 ——
            // 这样"上一轮留给隐藏格子的残留指定"不会留下来害人（见 Poll 里一致性检查的注释）。
            PsRegistry.ClearGalleryOverrides(null);

            int assigned = 0;
            string example = "";
            for (int i = 0; i < elements.Count && i < wantVisible; i++)
            {
                PortraitSelectorElement el = elements[i];
                if (el == null) continue;
                string id = ElementId(el);
                if (string.IsNullOrEmpty(id) || id == "?") continue;

                string image = pool[i % pool.Count];
                PsRegistry.SetGalleryOverride(id, image);
                PsRegistry.InvalidateHandouts(id);
                DropGameCache(id);
                _assigned[id] = image;
                _refreshList.Add(el);
                assigned++;
                if (assigned <= 3) example += "\n      " + id + " → " + image;
            }
            _refreshFrames = RefreshFrames;

            Log.Info("角色头像列表已重建：代词=" + GenderWord(chosen)
                     + "，池子 " + pool.Count + " 张（" + PoolFolder(chosen) + "）"
                     + "，列表共 " + elements.Count + " 格 ⇒ 显示 " + wantVisible
                     + " 格、每格一张、不重复不遗漏（已指定 " + assigned + " 个格子）"
                     + (example.Length > 0 ? "，例如：" + example : ""));
            if (pool.Count > elements.Count)
            {
                Log.Warn("注意：这个池子有 " + pool.Count + " 张图，但列表最多只有 " + elements.Count
                         + " 格 ⇒ 只能显示前 " + elements.Count + " 张。");
            }
        }

        private static void SelectIfHidden(SGCharacterCreationPortraitSelectionPanel panel,
                                           List<PortraitSelectorElement> elements, int wantVisible)
        {
            try
            {
                PortraitSelectorElement sel = panel.selectedPortrait;
                if (sel == null || sel.gameObject.activeSelf) return;
                for (int i = 0; i < elements.Count && i < wantVisible; i++)
                {
                    if (elements[i] != null && elements[i].gameObject.activeSelf)
                    {
                        panel.Select(elements[i]);
                        Log.Info("  （原来选中的头像被隐藏了，已自动改选第一个可见头像："
                                 + ElementId(elements[i]) + "）");
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn("改选可见头像失败（不影响列表本身）：" + e.Message);
            }
        }

        private static void Cleanup()
        {
            _applied = false;
            _refreshFrames = 0;
            _refreshList.Clear();
            int before = PsRegistry.GalleryOverrideCount;
            PsRegistry.ClearGalleryOverrides(_lastSelectedId);
            Log.Info("离开角色创建界面：已清理头像列表的『格子→图片』映射（" + before + " 个 ⇒ "
                     + PsRegistry.GalleryOverrideCount + " 个）"
                     + (string.IsNullOrEmpty(_lastSelectedId)
                        ? ""
                        : "，保留你选中的 " + _lastSelectedId + "（否则指挥官头像会变回原来那张）"));
        }

        // ================================================================ M8：代词选项裁剪

        /// <summary>
        /// ★ M8（用户决策：**在"选性别"那一步就把没有替换肖像的性别隐藏掉**）。
        ///
        /// 代词选择器是 <c>BattleTech.UI.HorizontalScrollSelectorText</c>（就是 ◄ [ 他 ] ► 那个），
        /// 它提供了 <c>SetOptions(string[])</c> / <c>SelectOption(string)</c> /
        /// <c>public string selection</c> / <c>public List&lt;LocalizableText&gt; options</c>，
        /// 所以可以安全地把选项列表换成"只剩有图的性别"。
        ///
        /// 保留规则：
        /// <list type="bullet">
        ///   <item>女 ⇒ <c>portraits\female</c> 里有图；</item>
        ///   <item>男 ⇒ <c>portraits\male</c> 里有图；</item>
        ///   <item>「他们」⇒ 按用户决策当作"不限制性别"（见 <see cref="PoolFor"/>），
        ///         只要有**任意一张**图就保留；</item>
        ///   <item>一个都不剩就不动（兜底）。正常不会发生：一张图都没有时 M6 已经把替换整体关掉，
        ///         这个组件压根不会安装。</item>
        /// </list>
        ///
        /// 用 <c>sel.options.Count</c> 与目标数量对比来决定"要不要做"：
        /// 既幂等（做完就不再动），又能在游戏自己重新 LoadOptions 之后自愈。
        /// 裁剪后会把玩家原来的选择还回去（还不了就落到第一个保留项，并写明原因）。
        /// </summary>
        private static void PrunePronounOptions()
        {
            if (_fPronounSelector == null || _pHePronoun == null || _pShePronoun == null || _pTheyPronoun == null)
                return;

            try
            {
                SGCharacterCreationNamePanel np = _fNamePanel.GetValue(_screen) as SGCharacterCreationNamePanel;
                if (np == null) return;
                HorizontalScrollSelectorText sel = _fPronounSelector.GetValue(np) as HorizontalScrollSelectorText;
                if (sel == null) return;

                bool hasF = PsRegistry.CountOfGender(Gender.Female) > 0;
                bool hasM = PsRegistry.CountOfGender(Gender.Male) > 0;
                bool hasO = PsRegistry.HasAnyPortrait();       // 「他们」= 不限制性别，有任意图即可

                string he = _pHePronoun.GetValue(np, null) as string;
                string she = _pShePronoun.GetValue(np, null) as string;
                string they = _pTheyPronoun.GetValue(np, null) as string;

                List<string> keep = new List<string>();
                if (hasF && !string.IsNullOrEmpty(she)) keep.Add(she);
                if (hasM && !string.IsNullOrEmpty(he)) keep.Add(he);
                if (hasO && !string.IsNullOrEmpty(they)) keep.Add(they);

                if (keep.Count == 0) return;                   // 兜底：不能把选项清空

                // 选项数量用**反射 + 非泛型 ICollection** 读：
                // options 的元素类型是 TMPro 相关类型（LocalizableText 里带 TextMeshProUGUI），
                // 直接写 sel.options.Count 会让编译器要求引用 Unity.TextMeshPro —— 没必要。
                int have = -1;
                try
                {
                    PropertyInfo pOpts = AccessTools.Property(typeof(HorizontalScrollSelectorText), "options");
                    object opts = pOpts == null ? null : pOpts.GetValue(sel, null);
                    System.Collections.ICollection coll = opts as System.Collections.ICollection;
                    if (coll != null) have = coll.Count;
                }
                catch { }
                if (have == keep.Count) return;                 // 已经是目标状态

                string current = sel.selection;
                sel.SetOptions(keep.ToArray());

                // SetOptions 会把选择重置成第一项 —— 尽量把玩家原来的选择还回去
                if (!string.IsNullOrEmpty(current) && keep.Contains(current))
                {
                    sel.SelectOption(current);
                }
                else if (!string.IsNullOrEmpty(current))
                {
                    Log.Info("  （你原来选的代词「" + current + "」所在目录没有替换肖像，已不再提供）");
                }

                _prunedScreenId = _screen.GetInstanceID();
                _prunedSignature = (hasF ? "F" : "") + (hasM ? "M" : "") + (hasO ? "O" : "");
                Log.Info("代词选项已裁剪（M8）：只保留有替换肖像的性别 —— " + string.Join("、", keep.ToArray())
                         + (hasF ? "" : "；已去掉「女」（portraits\\female 是空的）")
                         + (hasM ? "" : "；已去掉「男」（portraits\\male 是空的）")
                         + (hasO ? "" : "；已去掉「" + OtherWord() + "」（一张图都没有）"));
            }
            catch (Exception e)
            {
                Log.Warn("裁剪代词选项失败（不影响其它功能）：" + e.Message);
            }
        }

        // ================================================================ 池子

        /// <summary>
        /// 按代词挑池子。**「他们」（游戏里的 Gender.NonBinary）按用户决策当作"不限制性别"**，
        /// 给全部图片（女 + 男 + 非二元）—— 这与"随机驾驶员"的规则不同，是**有意**的：
        /// 随机驾驶员仍然只在各自性别的池子里取图（见 Mechanic.ResolveFinalWeights）。
        /// </summary>
        private static List<string> PoolFor(Gender chosen)
        {
            switch (chosen)
            {
                case Gender.Female: return PsRegistry.PathsOf(Gender.Female);
                case Gender.Male: return PsRegistry.PathsOf(Gender.Male);
                case Gender.NonBinary: return PsRegistry.AllPaths();     // 「他们」= 不限制性别
                default: return new List<string>();                      // 还没选出有效代词
            }
        }

        // ================================================================ 缩略图

        private static void RenderThumb(PortraitSelectorElement el)
        {
            try
            {
                if (el == null) return;
                // ★★ 必须用 StartCoroutine！
                //    PortraitSelectorElement.RenderThumbnail() 的返回类型是 IEnumerator（**协程**），
                //    直接 `el.RenderThumbnail();` 只是构造了一个迭代器然后丢掉 —— **协程根本不会执行**，
                //    缩略图永远是哈希分到的那张旧图（上一版就是这么失效的，真机表现为
                //    "格子里的图男女混杂、还有重复"）。
                //    （反汇编 <RenderThumbnail>d__16::MoveNext 确认：协程体里就是调
                //     PortraitManager.GeneratePortraitTexture + 把结果贴到 RawImage。）
                el.StartCoroutine(el.RenderThumbnail());
                _refreshStarted++;
            }
            catch (Exception e) { Log.Warn("重画头像缩略图失败：" + e.Message); }
        }

        /// <summary>
        /// **每帧**把"指定好的那张图"直接贴到格子的 <c>RawImage</c> 上。
        ///
        /// <para>为什么还要这一手（不只是让游戏重画）：</para>
        /// <list type="bullet">
        ///   <item><c>RenderThumbnail()</c> 是协程，时序上要等好几帧，而且它贴图时读的是游戏自己的
        ///         已渲染缓存 —— 时机和缓存两层都可能让我们指定的图显示不出来；</item>
        ///   <item>直接写 <c>RawImage.texture</c> 是**确定**的：谁覆盖了我们，下一帧再盖回来；</item>
        ///   <item>贴图取自我们自己的 <c>GetHandout</c>（每次取用前验活，被游戏销毁了会自动重建）。</item>
        /// </list>
        /// </summary>
        private static void ForceThumbnail(PortraitSelectorElement el, string settingsId, string image)
        {
            try
            {
                if (el == null || _fThumbnail == null) return;
                RawImage img = _fThumbnail.GetValue(el) as RawImage;
                if (img == null) return;
                Texture2D tex = PsRegistry.GetHandout(settingsId, image, ThumbSize);
                if (tex == null) return;
                if (img.texture != tex) img.texture = tex;
            }
            catch (Exception e)
            {
                if (!_thumbWarned)
                {
                    _thumbWarned = true;
                    Log.Warn("直接贴缩略图失败（会退回游戏自己的渲染）：" + e.GetType().Name + " " + e.Message);
                }
            }
        }

        private static bool _thumbWarned;
        private static bool _unreadableWarned;

        /// <summary>
        /// 清掉**游戏自己**那张已渲染贴图的缓存。必须清：
        /// <c>PortraitSettings.IsRendered()</c> 查的就是 <c>PortraitManager.ContainsKey(settingsId)</c>，
        /// 不清的话游戏会认为"已经渲染过"而直接跳过，缩略图永远是旧图。
        /// </summary>
        private static void DropGameCache(string settingsId)
        {
            try
            {
                if (_pPortraitManager == null || _game == null) return;
                DataManager dm = _game.DataManager;
                if (dm == null) return;
                object pm = _pPortraitManager.GetValue(dm, null);
                PortraitManager manager = pm as PortraitManager;
                if (manager == null) return;
                manager.Remove(settingsId);          // PortraitManager : Dictionary<string, RenderedPortraitResult>
            }
            catch (Exception e)
            {
                Log.Warn("清理游戏侧头像缓存失败（可能显示旧图）：" + e.Message);
            }
        }

        // ================================================================ 反射

        private static bool EnsureReflection()
        {
            try
            {
                Type screen = typeof(SGCharacterCreationNameAndAppearanceScreen);
                _fPortraitSelection = AccessTools.Field(screen, "PortraitSelection");
                _fNamePanel = AccessTools.Field(screen, "Name");
                _fAllElements = AccessTools.Field(typeof(SGCharacterCreationPortraitSelectionPanel), "allPortraitElements");
                _fElementSettings = AccessTools.Field(typeof(PortraitSelectorElement), "portraitSettings");
                _fThumbnail = AccessTools.Field(typeof(PortraitSelectorElement), "thumbnail");
                _fPronounSelector = AccessTools.Field(typeof(SGCharacterCreationNamePanel), "pronounSelector");
                _pHePronoun = AccessTools.Property(typeof(SGCharacterCreationNamePanel), "hePronoun");
                _pShePronoun = AccessTools.Property(typeof(SGCharacterCreationNamePanel), "shePronoun");
                _pTheyPronoun = AccessTools.Property(typeof(SGCharacterCreationNamePanel), "theyPronoun");
                _pPortraitManager = AccessTools.Property(typeof(DataManager), "PortraitManager");

                if (_fPortraitSelection == null || _fNamePanel == null
                    || _fAllElements == null || _fElementSettings == null)
                {
                    Log.Error("角色创建界面的字段名和预期不一致，『角色头像列表』处理无法启用。"
                              + "（PortraitSelection=" + (_fPortraitSelection != null)
                              + " Name=" + (_fNamePanel != null)
                              + " allPortraitElements=" + (_fAllElements != null)
                              + " portraitSettings=" + (_fElementSettings != null) + "）");
                    _refFailed = true;
                    return false;
                }
                if (_pPortraitManager == null)
                {
                    Log.Warn("找不到 DataManager.PortraitManager，改完映射后可能无法强制重画缩略图"
                             + "（列表可能显示旧图，直到你切换代词）。");
                }
                _refReady = true;
                return true;
            }
            catch (Exception e)
            {
                Log.Error("准备角色头像列表的反射失败，功能停用：" + e.Message);
                _refFailed = true;
                return false;
            }
        }

        private static SGCharacterCreationPortraitSelectionPanel GetPanel()
        {
            try { return _fPortraitSelection.GetValue(_screen) as SGCharacterCreationPortraitSelectionPanel; }
            catch { return null; }
        }

        private static Gender GetChosenGender()
        {
            try
            {
                SGCharacterCreationNamePanel np = _fNamePanel.GetValue(_screen) as SGCharacterCreationNamePanel;
                if (np == null) return Gender.None;
                return np.gender;                 // public 属性
            }
            catch { return Gender.None; }
        }

        private static List<PortraitSelectorElement> GetElements(SGCharacterCreationPortraitSelectionPanel panel)
        {
            try { return _fAllElements.GetValue(panel) as List<PortraitSelectorElement>; }
            catch { return null; }
        }

        private static string ElementId(PortraitSelectorElement el)
        {
            try
            {
                if (el == null) return null;
                PortraitSettings ps = _fElementSettings.GetValue(el) as PortraitSettings;
                if (ps != null && ps.Description != null && !string.IsNullOrEmpty(ps.Description.Id))
                    return ps.Description.Id;
            }
            catch { }
            return "?";
        }

        // ================================================================ 文案

        private static string GenderWord(Gender g)
        {
            switch (g)
            {
                case Gender.Female: return "女";
                case Gender.Male: return "男";
                case Gender.NonBinary: return OtherWord() + "（按『不限制性别』处理）";
                default: return "未定(" + (int)g + ")";
            }
        }

        private static string PoolFolder(Gender g)
        {
            switch (g)
            {
                case Gender.Female: return "portraits\\female";
                case Gender.Male: return "portraits\\male";
                case Gender.NonBinary: return "全部图片（portraits 下所有图）";
                default: return "?";
            }
        }

        private static string OtherWord()
        {
            try
            {
                if (CombinedConfig.GenderRatio != null && CombinedConfig.GenderRatio.settings != null
                    && !string.IsNullOrEmpty(CombinedConfig.GenderRatio.settings.otherLabel))
                    return CombinedConfig.GenderRatio.settings.otherLabel;
            }
            catch { }
            return GenderRatio.GenderRatioSettings.DefaultOtherLabel;
        }
    }
}
