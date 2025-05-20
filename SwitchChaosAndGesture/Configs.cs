using BepInEx.Bootstrap;
using BepInEx.Configuration;
using RiskOfOptions.OptionConfigs;
using RiskOfOptions.Options;
using RiskOfOptions;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SwitchChaosAndGesture;

internal class Configs
{
    internal static ConfigEntry<bool> IsGestureAllowed { get; private set; }
    internal static ConfigEntry<bool> IsGestureBlacklisted { get; private set; }
    internal static ConfigEntry<string> BannedAutocastEquipment { get; private set; }
    internal static ConfigEntry<float> GestureScaling { get; private set; }
    internal static ConfigEntry<float> ChaosCooldownPenalty { get; private set; }

    internal static void Init(ConfigFile config, string assemblyLocation)
    {
        var defaultScaling = 0.15f;
        IsGestureAllowed = config.Bind("Gesture of the Drowned", "Include In Item Pool", true, "Allow Gesture of the Drowned to be in the item pool.");
        IsGestureBlacklisted = config.Bind("Gesture of the Drowned", "AI Blacklist", false, "Blacklist the item for enemies.");

        BannedAutocastEquipment = config.Bind("Gesture of the Drowned", "Banned Equipment", "Recycle,GoldGat,BossHunter,FireBallDash",
            "Which equipment will not be autocast with Gesture. Run the 'equipment_list' command on the console for a list of all internal name options.");
        BannedAutocastEquipment.SettingChanged += ReloadBannedAutocastEquipment;

        GestureScaling = config.Bind("Gesture of the Drowned", "Stack Scaling", defaultScaling, "Modify the cooldown scaling for 1+ stacks.");
        if (GestureScaling.Value < 0f)
        {
            GestureScaling.Value = 0f;
            Log.Warning("The 'Stack Scaling' config setting has a negative value. Readjusting to 0.0");
        }
        else if (GestureScaling.Value > 1f)
        {
            GestureScaling.Value = defaultScaling;
            Log.Warning($"The 'Stack Scaling' config setting has a value higher than 1.0. Readjusting to the default {defaultScaling}");
        }

        ChaosCooldownPenalty = config.Bind("Bottled Chaos", "Cooldown Penalty", .2f, "The percent of the activated equipment's cooldown that will be added on due to Bottled Chaos' effect.");
        if (ChaosCooldownPenalty.Value < 0)
        {
            ChaosCooldownPenalty.Value = 0f;
            Log.Warning("The 'Cooldown Penalty' config setting has a negative value. Readjusting to 0.0");
        }

        if (Chainloader.PluginInfos.ContainsKey(SwitchChaosAndGesture.RiskOfOptionsGUID))
        {
            RiskOfOptionsInit(assemblyLocation);
        }
    }

    private static void ReloadBannedAutocastEquipment(object sender, System.EventArgs e)
    {
        Hooks.ReloadBlacklistedEquipment();
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static void RiskOfOptionsInit(string assemblyLocation)
    {
        FileInfo iconFile = null;
        var files = new DirectoryInfo(Path.GetDirectoryName(assemblyLocation)).GetFiles("icon.png", SearchOption.TopDirectoryOnly);
        if (files != null && files.Length > 0)
        {
            iconFile = files[0];
        }
        if (iconFile != null)
        {
            var name = $"{SwitchChaosAndGesture.PluginName}Icon";
            var texture = new Texture2D(256, 256);
            texture.name = name;
            if (texture.LoadImage(File.ReadAllBytes(iconFile.FullName)))
            {
                var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                sprite.name = name;
                ModSettingsManager.SetModIcon(sprite, SwitchChaosAndGesture.PluginGUID, SwitchChaosAndGesture.PluginName);
            }
        }

        ModSettingsManager.AddOption(new StringInputFieldOption(BannedAutocastEquipment, new InputFieldConfig
        {
            submitOn = InputFieldConfig.SubmitEnum.OnExitOrSubmit
        }));
        ModSettingsManager.AddOption(new FloatFieldOption(GestureScaling, new FloatFieldConfig
        {
            Min = 0f,
            Max = 1f
        }));
        ModSettingsManager.AddOption(new FloatFieldOption(ChaosCooldownPenalty, new FloatFieldConfig
        {
            Min = 0f
        }));
    }
}