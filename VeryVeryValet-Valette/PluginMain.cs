using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using TemplatePlugin;
using Toyful;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VeryVeryValet_Valette
{
    [BepInPlugin(TemplatePluginInfo.PLUGIN_GUID, TemplatePluginInfo.PLUGIN_NAME, Version)]
    public class PluginMain : BaseUnityPlugin
    {
        public const string GameName = TemplatePluginInfo.GAME_NAME;
        private const string Version = "1.0.0";

        private readonly Harmony _harmony = new Harmony(TemplatePluginInfo.PLUGIN_GUID);
        public static ManualLogSource? logger;

        void Awake()
        {
            logger = Logger;
            _harmony.PatchAll();
            DontDestroyOnLoad(gameObject);

            SceneManager.sceneLoaded += (scene, mode) => { };
        }
        
        private struct RigColors
        {
            public Color main;
            public Color secondary;
            public Color tertiary;
            public Color clothingA;
            public Color clothingB;
            public Color clothingC;
        }

        private static bool TryGetRigColors(CharacterRig rig, out RigColors colors)
        {
            colors = default;

            if (!rig.GetColors(
                    out colors.main,
                    out colors.secondary,
                    out colors.tertiary,
                    out colors.clothingA,
                    out colors.clothingB,
                    out colors.clothingC))
                return false;

            return true;
        }

        private static void ApplyRigColors(CharacterRig rig, RigColors c)
        {
            rig.SetColors(
                c.main,
                c.secondary,
                c.tertiary,
                c.clothingA,
                c.clothingB,
                c.clothingC,
                Random.ColorHSV()
            );
        }
        
        private static readonly Dictionary<int, int> selectedColorIndex = new Dictionary<int, int>();
        private static readonly Dictionary<int, bool> editingBrightness = new Dictionary<int, bool>();

        [HarmonyPatch(typeof(ValetSelectPlayerUi))]
        private class ValetSelectPlayerUi_Patches
        {
            private static readonly Dictionary<int, bool> colorEditMode = new  Dictionary<int, bool>();

            [HarmonyPatch(nameof(ValetSelectPlayerUi.Update))]
            [HarmonyPrefix]
            public static bool OnUpdate(ValetSelectPlayerUi __instance)
            {
                var playerId = __instance._playerId;
                if (playerId < 0 || !ControlsManager.instance.IsPlayerActive(playerId) || !ScreenManager.instance.isSafeToAdjust)
                    return false;

                var deltaTime = UtilsTime.AdjustDeltaTime(Time.deltaTime);
                var stickInput = ControlsManager.instance.FilterAxisInput(deltaTime, ControlMaps.ValetSelect, __instance._playerIndex, ref __instance._axisX, ref __instance._axisY, ref __instance._axisRepeatT);

                if (!colorEditMode.ContainsKey(playerId))
                    colorEditMode[playerId] = false;

                if (ControlsManager.instance.GetButtonDown(ControlMaps.ValetSelect, ControlActions.Info, playerId))
                    colorEditMode[playerId] = !colorEditMode[playerId];

                if (colorEditMode[playerId])
                {

                    var playerRef = __instance._playerManager.GetPlayerReference(playerId);
                    if (!selectedColorIndex.ContainsKey(playerId))
                        selectedColorIndex[playerId] = 0;

                    if (!editingBrightness.ContainsKey(playerId))
                        editingBrightness[playerId] = false;

                    if (ControlsManager.instance.GetButtonDown(ControlMaps.ValetSelect, ControlActions.L, playerId))
                    {
                        selectedColorIndex[playerId] = (selectedColorIndex[playerId] + 1) % 3;
                    }

                    if (ControlsManager.instance.GetButtonDown(ControlMaps.ValetSelect, ControlActions.R, playerId))
                    {
                        editingBrightness[playerId] = !editingBrightness[playerId];
                    }
                    
                    HandleColorEditing(playerRef, stickInput, selectedColorIndex[playerId], editingBrightness[playerId]);
                    stickInput = Vector2.zero;
                }

                if (stickInput != Vector2.zero && __instance._state == ValetSelectPlayerUi.PlayerState.Choosing && !__instance._isControlsActive)
                    __instance.CycleCharacter(stickInput.x);

                if (__instance._controlsPanel.isExpanded)
                    __instance._controlsPanel.UpdateInput();

                if (ControlsManager.instance.GetButtonDown(ControlMaps.ValetSelect, ControlActions.Cancel, playerId))
                    __instance.OnCancelAction();
                if (ControlsManager.instance.GetButtonDown(ControlMaps.ValetSelect, ControlActions.Interact, playerId) && __instance._state == ValetSelectPlayerUi.PlayerState.Choosing)
                    __instance.ToggleControls(false);
                if (ControlsManager.instance.GetButtonDown(ControlMaps.ValetSelect, ControlActions.Confirm, playerId))
                    __instance.OnConfirmAction();

                if (__instance._state == ValetSelectPlayerUi.PlayerState.Invalid &&
                    ControlsManager.instance.GetButton(ControlMaps.ValetSelect, ControlActions.L, playerId) &&
                    ControlsManager.instance.GetButton(ControlMaps.ValetSelect, ControlActions.R, playerId))
                {
                    __instance.SetState(ValetSelectPlayerUi.PlayerState.Choosing);
                }

                return false;
            }

            private static void HandleColorEditing(
                PlayerMgr.PlayerReference player,
                Vector2 stick,
                int colorIndex,
                bool brightnessMode)
            {
                var rig = player.CharacterRig;
                var input = player.Input;
                if (rig == null || input == null)
                    return;

                if (!TryGetRigColors(rig, out var colors))
                    return;

                var target = colorIndex switch
                {
                    1 => colors.secondary,
                    2 => colors.tertiary,
                    _ => colors.main
                };

                Color.RGBToHSV(target, out var hue, out var sat, out var val);

                const float colorSpeed = 2f;
                const float satSpeed = 4f;
                const float valSpeed = 4f;

                hue += stick.x * colorSpeed * Time.deltaTime;

                if (brightnessMode)
                {
                    val += stick.y * valSpeed * Time.deltaTime;
                }
                else
                {
                    sat += stick.y * satSpeed * Time.deltaTime;
                }

                hue = Mathf.Repeat(hue, 1f);
                sat = Mathf.Clamp01(sat);
                val = Mathf.Clamp01(val);

                var newColor = Color.HSVToRGB(hue, sat, val);

                switch (colorIndex)
                {
                    case 1: colors.secondary = newColor; break;
                    case 2: colors.tertiary = newColor; break;
                    default: colors.main = newColor; break;
                }

                ApplyRigColors(rig, colors);
            }
        }
        
        [HarmonyPatch(typeof(PlayerMgr))]
        private static class PlayerColorPatch
        {
            [HarmonyPatch(nameof(PlayerMgr.SetPlayerColor))]
            [HarmonyPrefix]
            static bool OnSetPlayerColor(PlayerMgr __instance, int playerID)
            {
                return false;
            }
        }
    }
}