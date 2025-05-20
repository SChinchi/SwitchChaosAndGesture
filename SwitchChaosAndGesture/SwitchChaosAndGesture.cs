using BepInEx;
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

namespace SwitchChaosAndGesture;

[BepInDependency(R2API.LanguageAPI.PluginGUID)]
[BepInPlugin(PluginGUID, PluginName, PluginVersion)]
public class SwitchChaosAndGesture : BaseUnityPlugin
{
    public const string PluginGUID = PluginAuthor + "." + PluginName;
    public const string PluginAuthor = "Chinchi";
    public const string PluginName = "SwitchChaosAndGesture";
    public const string PluginVersion = "1.1.1";

    private static AssetBundle assetBundle;

    public void Awake()
    {
        Log.Init(Logger);
        Configs.Init(Config);
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
            if (itemDef.name == nameof(DLC1Content.Items.RandomEquipmentTrigger))
            {
                UpdateItemDef(itemDef, ItemTier.Lunar, "texBottledChaosIcon", true);
            }
            else if (itemDef.name == nameof(RoR2Content.Items.AutoCastEquipment))
            {
                UpdateItemDef(itemDef, ItemTier.Tier3, "texFossilIcon", Configs.IsGestureAllowed.Value);
                ModifyItemTag(itemDef, ItemTag.AIBlacklist, Configs.IsGestureBlacklisted.Value);
            }
        }
    }

    private static void UpdateItemDef(ItemDef itemDef, ItemTier tier, string sprite, bool isDroppable)
    {
#pragma warning disable CS0618
        itemDef.deprecatedTier = tier;
#pragma warning restore CS0618
        var assetBundle = SwitchChaosAndGesture.assetBundle;
        if (assetBundle)
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
                itemDef.tags = [.. tags];
            }
        }
        else
        {
            if (!itemDef.tags.Contains(tag))
            {
                var tags = itemDef.tags.ToList();
                tags.Add(tag);
                itemDef.tags = [.. tags];
            }
        }
    }

    private void ApplyModelChanges()
    {
        Addressables.LoadAssetAsync<Material>("RoR2/DLC1/RandomEquipmentTrigger/matBottledChaos.mat").Completed += delegate (AsyncOperationHandle<Material> material)
        {
            Addressables.LoadAssetAsync<Texture2D>("RoR2/Base/Common/ColorRamps/texLunarWispTracer 1.png").Completed += delegate (AsyncOperationHandle<Texture2D> texture)
            {
                material.Result.mainTexture = texture.Result;
            };
        };
        Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/RandomEquipmentTrigger/RandomEquipmentTriggerProcEffect.prefab").Completed += delegate (AsyncOperationHandle<GameObject> effect)
        {
            SetMaterial(effect.Result.transform.Find("Vase").gameObject, "RoR2/Base/LunarWisp/matLunarWispStones.mat", 0);
            SetMaterial(effect.Result.transform.Find("Trails").gameObject, "RoR2/Base/LunarWisp/matLunarWispMinigunTracer.mat", 1);
        };
    }

    private void SetMaterial(GameObject go, string material, int index)
    {
        Addressables.LoadAssetAsync<Material>(material).Completed += delegate (AsyncOperationHandle<Material> obj)
        {
            var psr = go.GetComponent<ParticleSystemRenderer>();
            var materials = psr.sharedMaterials;
            materials[index] = obj.Result;
            psr.sharedMaterials = materials;
        };
    }
}