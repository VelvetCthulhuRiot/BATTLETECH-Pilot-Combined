using System;
using System.Reflection;
using BattleTech;
using BattleTech.Portraits;
using Harmony;
using UnityEngine;

namespace PilotCombined.Portrait
{
    /// <summary>
    /// 实际下钩子的地方。三个钩子，各管一段路：
    ///
    ///   1) PortraitManager.GeneratePortraitTexture  —— 3D 形态驾驶员（玩家指挥官、
    ///      随机雇佣兵、上过整容弹窗的人）的唯一渲染收口。拦下来就彻底不跑 3D。
    ///
    ///   2) PilotDef.GetPortraitSprite /
    ///      GetPortraitSpriteThumb               —— 固定头像驾驶员（传奇佣兵、众筹者、
    ///      剧情角色）走"按 Description.Icon 取 Sprite"。这里换成你的图。
    ///
    ///   3) PilotDef.get_PortraitSettings         —— 只读关联：记录 settingsId 与 PilotDef
    ///      的对应，让"呼号/标签"规则对 3D 形态驾驶员也生效。不改任何返回值。
    ///
    /// 关于贴图归属（这是一个真实踩过的坑）：
    ///   PortraitManager.Clear() / ClearLargePortraits() / ClearPortraits() 都会
    ///   DestroyImmediate 掉 renderResults 里的贴图。所以交给游戏的那一张必须是
    ///   每个 (settingsId, 尺寸) 独占的副本，并且每次取用前验活；我们自己的原型图
    ///   绝不交出去。
    /// </summary>
    internal static class Patches
    {
        // ================================================================ 1) 3D 渲染收口

        // 参数名必须与原方法一致：settings / loaded / failed / sizes
        internal static bool GeneratePortraitTexture_Prefix(
            PortraitManager __instance,
            PortraitSettings settings,
            Action<RenderedPortraitResult> loaded,
            Action<string> failed,
            PortraitManager.PortraitSizes[] sizes)
        {
            try
            {
                if (settings == null) return true;

                string image = PortraitRegistry.ResolveImageForSettings(settings);
                if (image == null) return true;   // 不在名单里 -> 交回游戏原逻辑

                if (settings.Description == null || string.IsNullOrEmpty(settings.Description.Id)) return true;
                string settingsId = settings.Description.Id;

                // 原方法在 sizes 为空时默认用 VERYSMALL + MEDIUM
                PortraitManager.PortraitSizes[] want = (sizes == null || sizes.Length == 0)
                    ? new PortraitManager.PortraitSizes[]
                        { PortraitManager.PortraitSizes.VERYSMALL, PortraitManager.PortraitSizes.MEDIUM }
                    : sizes;

                RenderedPortraitResult result;
                if (!__instance.TryGetValue(settingsId, out result) || result == null)
                {
                    result = new RenderedPortraitResult();
                    __instance[settingsId] = result;
                }

                int made = 0;
                for (int i = 0; i < want.Length; i++)
                {
                    int size = (int)want[i];
                    Texture2D tex = PortraitRegistry.GetHandout(settingsId, image, size);
                    if (tex != null) { result[want[i]] = tex; made++; }
                }
                if (made == 0) return true;      // 一张都没做出来，老实交给游戏

                if (loaded != null) loaded(result);

                return false;   // 关键：返回 false = 完全跳过原方法，3D 管线不会启动
            }
            catch (Exception e)
            {
                Log.Error("GeneratePortraitTexture_Prefix 异常，回退原逻辑：" + e);
                return true;
            }
        }

        // ================================================================ 2) 固定头像驾驶员

        internal static void GetPortraitSprite_Postfix(PilotDef __instance, ref Sprite __result)
        {
            try
            {
                string image = PortraitRegistry.ResolveImageForPilotDef(__instance);
                if (image == null) return;
                Sprite s = PortraitRegistry.GetSprite(image);
                if (s != null) __result = s;
            }
            catch (Exception e) { Log.Error("GetPortraitSprite_Postfix: " + e.Message); }
        }

        internal static void GetPortraitSpriteThumb_Postfix(PilotDef __instance, ref Sprite __result)
        {
            try
            {
                string image = PortraitRegistry.ResolveImageForPilotDef(__instance);
                if (image == null) return;
                Sprite s = PortraitRegistry.GetSprite(image);
                if (s != null) __result = s;
            }
            catch (Exception e) { Log.Error("GetPortraitSpriteThumb_Postfix: " + e.Message); }
        }

        // ================================================================ 3) 只读关联

        internal static void GetPortraitSettings_Postfix(PilotDef __instance, PortraitSettings __result)
        {
            try
            {
                if (__result == null || __result.Description == null) return;
                PortraitRegistry.RememberSettings(__result.Description.Id, __instance);
            }
            catch { }
        }

        // ================================================================ 可选：隐藏 3D 整容入口

        internal static FieldInfo CustomizeButtonField;

        internal static void OnAddedToHierarchy_Postfix(object __instance)
        {
            try
            {
                if (CustomizeButtonField == null) return;
                object v = CustomizeButtonField.GetValue(__instance);
                GameObject go = v as GameObject;
                if (go == null)
                {
                    Component c = v as Component;
                    if (c != null) go = c.gameObject;
                }
                if (go != null && go.activeSelf)
                {
                    go.SetActive(false);
                    Log.Info("已隐藏角色创建界面的『自定义外貌』按钮（3D 调脸入口）");
                }
            }
            catch (Exception e)
            {
                Log.Warn("隐藏『自定义外貌』按钮失败（不影响功能）：" + e.Message);
            }
        }

        // ================================================================ 5) 不把自己算作"存档必需 mod"
        //
        // ★ M3 起这段挪到 PilotCombined.SaveGuard —— 两个子系统原来各挂了一个一模一样的
        //   Postfix（同一方法被挂两次，都能跑但纯属重复）。这里不再保留任何副本。
    }
}
