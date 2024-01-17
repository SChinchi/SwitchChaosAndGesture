using BepInEx;
using BepInEx.Configuration;
using RoR2;
using System.Security.Permissions;
using UnityEngine;
using UnityEngine.AddressableAssets;

#pragma warning disable CS0618
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618
[assembly: HG.Reflection.SearchableAttribute.OptIn]

namespace SwitchChaosAndGesture
{
    [BepInDependency(R2API.LanguageAPI.PluginGUID)]
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class SwitchChaosAndGesture : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "GChinchi";
        public const string PluginName = "SwitchChaosAndGesture";
        public const string PluginVersion = "1.0.0";

        internal static ConfigEntry<bool> isGestureAllowed, isGestureBlacklisted;
        internal static ConfigEntry<string> bannedAutocastEquipment;
        internal static ConfigEntry<float> chaosCooldownPenalty;
        internal static AssetBundle assetBundle;
        internal static new BepInEx.Logging.ManualLogSource Logger;

        public void Awake()
        {
            Logger = base.Logger;
            isGestureAllowed = Config.Bind("Settings", "allowGesture", true, "Allow Gesture of the Drowned to be in the item pool.");
            isGestureBlacklisted = Config.Bind("Settings", "blacklistGesture", false, "Blacklist the item for enemies.");
            bannedAutocastEquipment = Config.Bind("Settings", "bannedAutocastEquipment", "Recycle,GoldGat,BossHunter,FireBallDash", "Which equipment will not be autocast with Gesture. Run the 'equipment_list' command on the console for a list of all internal name options.");
            chaosCooldownPenalty = Config.Bind("Settings", "chaosCooldown", .2f, "The percent of the activated equipment's cooldown that will be added on due to Bottled Chaos' effect.");
            if (chaosCooldownPenalty.Value < 0)
            {
                chaosCooldownPenalty.Value = 0f;
                Logger.LogWarning("The 'chaosCooldown' config setting has a negative value. Readjusting to 0.0");
            }
            LoadBundle(Info.Location);
            Hooks.Init();
            RoR2Application.onLoad += ApplyModelChanges;
        }

        private void LoadBundle(string directory)
        {
            var filename = "assetdata.bundle";
            assetBundle = AssetBundle.LoadFromFile(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(directory), filename));
            if (assetBundle == null)
            {
                Logger.LogError($"Failed to load '{filename}'");
            }
        }

        private void ApplyModelChanges()
        {
            var mat = Addressables.LoadAssetAsync<Material>("RoR2/DLC1/RandomEquipmentTrigger/matBottledChaos.mat").WaitForCompletion();
            mat.mainTexture = Addressables.LoadAssetAsync<Texture2D>("RoR2/Base/Common/ColorRamps/texLunarWispTracer 1.png").WaitForCompletion();

            var effect = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/RandomEquipmentTrigger/RandomEquipmentTriggerProcEffect.prefab").WaitForCompletion();
            SetMaterial(effect.transform.Find("Vase").gameObject, "RoR2/Base/LunarWisp/matLunarWispStones.mat", 0);
            SetMaterial(effect.transform.Find("Trails").gameObject, "RoR2/Base/LunarWisp/matLunarWispMinigunTracer.mat", 1);
        }

        private void SetMaterial(GameObject go, string material, int index)
        {
            var psr = go.GetComponent<ParticleSystemRenderer>();
            var materials = psr.sharedMaterials;
            materials[index] = Addressables.LoadAssetAsync<Material>(material).WaitForCompletion();
            psr.sharedMaterials = materials;
        }
    }
}