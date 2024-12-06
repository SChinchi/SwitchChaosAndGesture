using BepInEx;
using BepInEx.Configuration;
using RoR2;
using System.Linq;
using System.Security.Permissions;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

#pragma warning disable CS0618
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618
#if DEBUG
[assembly: HG.Reflection.SearchableAttribute.OptIn]
#endif

namespace SwitchChaosAndGesture
{
    [BepInDependency(R2API.LanguageAPI.PluginGUID)]
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class SwitchChaosAndGesture : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "Chinchi";
        public const string PluginName = "SwitchChaosAndGesture";
        public const string PluginVersion = "1.1.1";

        internal static ConfigEntry<bool> isGestureAllowed, isGestureBlacklisted;
        internal static ConfigEntry<string> bannedAutocastEquipment;
        internal static ConfigEntry<float> chaosCooldownPenalty;
        internal static AssetBundle assetBundle;
        internal static new BepInEx.Logging.ManualLogSource Logger;

        public void Awake()
        {
            Logger = base.Logger;
            isGestureAllowed = Config.Bind("Gesture of the Drowned", "Include In Item Pool", true, "Allow Gesture of the Drowned to be in the item pool.");
            isGestureBlacklisted = Config.Bind("Gesture of the Drowned", "AI Blacklist", false, "Blacklist the item for enemies.");
            bannedAutocastEquipment = Config.Bind("Gesture of the Drowned", "Banned Equipment", "Recycle,GoldGat,BossHunter,FireBallDash",
                "Which equipment will not be autocast with Gesture. Run the 'equipment_list' command on the console for a list of all internal name options.");
            chaosCooldownPenalty = Config.Bind("Bottled Chaos", "Cooldown Penalty", .2f, "The percent of the activated equipment's cooldown that will be added on due to Bottled Chaos' effect.");
            if (chaosCooldownPenalty.Value < 0)
            {
                chaosCooldownPenalty.Value = 0f;
                Logger.LogWarning("The 'Cooldown Penalty' config setting has a negative value. Readjusting to 0.0");
            }
            LoadBundle(Info.Location);
            ModifyItems();
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

        private static void ModifyItems()
        {
            Addressables.LoadAssetAsync<ItemDef>("RoR2/Base/AutoCastEquipment/AutoCastEquipment.asset").Completed += Modify;
            Addressables.LoadAssetAsync<ItemDef>("RoR2/DLC1/RandomEquipmentTrigger/RandomEquipmentTrigger.asset").Completed += Modify;

            static void Modify(AsyncOperationHandle<ItemDef> handle)
            {
                var itemDef = handle.Result;
                if (itemDef.name == "RandomEquipmentTrigger")
                {
                    UpdateItemDef(itemDef, ItemTier.Lunar, "texBottledChaosIcon", true);
                }
                else if (itemDef.name == "AutoCastEquipment")
                {
                    UpdateItemDef(itemDef, ItemTier.Tier3, "texFossilIcon", isGestureAllowed.Value);
                    ModifyItemTag(itemDef, ItemTag.AIBlacklist, isGestureBlacklisted.Value);
                }
            }
        }

        private static void UpdateItemDef(ItemDef itemDef, ItemTier tier, string sprite, bool isDroppable)
        {
            #pragma warning disable CS0618
            itemDef.deprecatedTier = tier;
            #pragma warning restore CS0618
            var assetBundle = SwitchChaosAndGesture.assetBundle;
            if (assetBundle != null)
            {
                itemDef.pickupIconSprite = assetBundle.LoadAsset<Sprite>(sprite);
            }
            ModifyItemTag(itemDef, ItemTag.WorldUnique, !isDroppable);
        }

        private static void ModifyItemTag(ItemDef itemDef, ItemTag tag, bool include)
        {
            if (!include)
            {
                if (itemDef.tags.Contains(tag))
                {
                    var tags = itemDef.tags.ToList();
                    tags.Remove(tag);
                    itemDef.tags = tags.ToArray();
                }
            }
            else
            {
                if (!itemDef.tags.Contains(tag))
                {
                    var tags = itemDef.tags.ToList();
                    tags.Add(tag);
                    itemDef.tags = tags.ToArray();
                }
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