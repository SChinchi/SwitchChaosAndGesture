using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace SwitchChaosAndGesture
{
    internal class Hooks
    {
        private const string BASE_ERROR_MESSAGE = "Failed to patch method: ";

        private static bool popNextCooldown = false;
        internal static readonly Dictionary<CharacterMaster, List<float>[]> masterCooldowns = new();
        private static readonly HashSet<EquipmentIndex> bannedAutocastEquipment = new();

        public static void Init()
        {
            On.RoR2.ItemCatalog.SetItemDefs += ItemCatalog_SetItemDefs;
            On.RoR2.EquipmentCatalog.SetEquipmentDefs += EquipmentCatalog_SetEquipmentDefs;
            On.RoR2.CharacterMaster.OnEnable += CharacterMaster_OnEnable;
            On.RoR2.CharacterMaster.OnDisable += CharacterMaster_OnDisable;
            IL.RoR2.EquipmentSlot.MyFixedUpdate += EquipmentSlot_MyFixedUpdate;
            IL.RoR2.Inventory.SetEquipmentInternal += Inventory_SetEquipmentInternal;
            IL.RoR2.Inventory.UpdateEquipment += Inventory_UpdateEquipment;
            IL.RoR2.EquipmentSlot.OnEquipmentExecuted += EquipmentSlot_OnEquipmentExecuted;
            IL.EntityStates.GoldGat.BaseGoldGatState.FixedUpdate += BaseGoldGatState_FixedUpdate;
            Inventory.onInventoryChangedGlobal += Inventory_onInventoryChangedGlobal;
            Run.onRunDestroyGlobal += Run_onRunDestroyGlobal;
        }

        private static void EquipmentSlot_MyFixedUpdate(ILContext il)
        {
            var c = new ILCursor(il);
            if (!c.TryGotoNext(
                MoveType.After,
                x => x.MatchLdsfld(typeof(RoR2Content.Items), "AutoCastEquipment"),
                x => x.MatchCall<Inventory>("GetItemCount"),
                x => x.MatchLdcI4(0),
                x => x.MatchCgt()
            ))
            {
                SwitchChaosAndGesture.Logger.LogError(BASE_ERROR_MESSAGE + "EquipmentSlot.FixedUpdate");
                return;
            }
            c.Emit(OpCodes.Ldarg_0);
            c.EmitDelegate<Func<bool, EquipmentSlot, bool>>((autocast, equipmentSlot) =>
            {
                return autocast && !bannedAutocastEquipment.Contains(equipmentSlot.equipmentIndex);
            });
        }

        private static void BaseGoldGatState_FixedUpdate(ILContext il)
        {
            var c = new ILCursor(il);
            if (!c.TryGotoNext(
                MoveType.After,
                x => x.MatchCallvirt<Inventory>("GetItemCount"),
                x => x.MatchLdcI4(0),
                x => x.MatchBle(out _),
                x => x.MatchLdarg(0),
                x => x.MatchLdcI4(1)
            ))
            {
                SwitchChaosAndGesture.Logger.LogError(BASE_ERROR_MESSAGE + "EntityStates.GoldGat.BaseGoldGatState.FixedUpdate");
                return;
            }
            c.Emit(OpCodes.Ldarg_0);
            c.EmitDelegate<Func<bool, EntityStates.GoldGat.BaseGoldGatState ,bool>>((autocast, state) =>
            {
                return state.shouldFire || (autocast && !bannedAutocastEquipment.Contains(RoR2Content.Equipment.GoldGat.equipmentIndex));
            });
        }

        private static void ItemCatalog_SetItemDefs(On.RoR2.ItemCatalog.orig_SetItemDefs orig, ItemDef[] newItemDefs)
        {
            foreach (var itemDef in newItemDefs)
            {
                if (itemDef == DLC1Content.Items.RandomEquipmentTrigger)
                {
                    UpdateItemDef(itemDef, ItemTier.Lunar, "texBottledChaosIcon", true);
                }
                else if (itemDef == RoR2Content.Items.AutoCastEquipment)
                {
                    UpdateItemDef(itemDef, ItemTier.Tier3, "texFossilIcon", SwitchChaosAndGesture.isGestureAllowed.Value);
                    AddOrRemoveTag(itemDef, ItemTag.AIBlacklist, SwitchChaosAndGesture.isGestureBlacklisted.Value);
                }
            }
            orig(newItemDefs);
        }

        private static void EquipmentCatalog_SetEquipmentDefs(On.RoR2.EquipmentCatalog.orig_SetEquipmentDefs orig, EquipmentDef[] newEquipmentDefs)
        {
            orig(newEquipmentDefs);
            foreach (var name in SwitchChaosAndGesture.bannedAutocastEquipment.Value.Split(','))
            {
                bannedAutocastEquipment.Add(EquipmentCatalog.FindEquipmentIndex(name.Trim()));
            }
            // In case of any name typos resulting to none
            bannedAutocastEquipment.Remove(EquipmentIndex.None);
        }

        private static void UpdateItemDef(ItemDef itemDef, ItemTier tier, string sprite, bool isDroppable)
        {
            itemDef.deprecatedTier = tier;
            itemDef.tier = tier;
            var assetBundle = SwitchChaosAndGesture.assetBundle;
            if (assetBundle != null)
            {
                itemDef.pickupIconSprite = assetBundle.LoadAsset<Sprite>(sprite);
            }
            AddOrRemoveTag(itemDef, ItemTag.WorldUnique, isDroppable);
        }

        private static void AddOrRemoveTag(ItemDef itemDef, ItemTag tag, bool add)
        {
            if (add && itemDef.tags.Contains(tag))
            {
                var tags = itemDef.tags.ToList();
                tags.Remove(tag);
                itemDef.tags = tags.ToArray();
            }
            else if (!add && !itemDef.tags.Contains(tag))
            {
                var tags = itemDef.tags.ToList();
                tags.Add(tag);
                itemDef.tags = tags.ToArray();
            }
        }

        private static void CharacterMaster_OnEnable(On.RoR2.CharacterMaster.orig_OnEnable orig, CharacterMaster self)
        {
            orig(self);
            if (NetworkServer.active && !masterCooldowns.ContainsKey(self))
            {
                masterCooldowns[self] = new List<float>[0];
            }
        }

        private static void CharacterMaster_OnDisable(On.RoR2.CharacterMaster.orig_OnDisable orig, CharacterMaster self)
        {
            orig(self);
            if (NetworkServer.active && masterCooldowns.ContainsKey(self))
            {
                masterCooldowns.Remove(self);
            }
        }

        private static void Inventory_SetEquipmentInternal(ILContext il)
        {
            var c = new ILCursor(il);
            if (!c.TryGotoNext(
                MoveType.After,
                x => x.MatchLdarg(0),
                x => x.MatchLdfld<Inventory>("equipmentStateSlots"),
                x => x.MatchLdlen(),
                x => x.MatchConvI4()
            ))
            {
                SwitchChaosAndGesture.Logger.LogError(BASE_ERROR_MESSAGE + "Inventory.SetEquipmentInternal");
                return;
            }
            c.Emit(OpCodes.Ldarg_0);
            c.Emit(OpCodes.Ldarg_2);
            c.EmitDelegate<Func<int, Inventory, uint, int>>((num, inventory, slot) =>
            {
                if (NetworkServer.active)
                {
                    var master = inventory.GetComponent<CharacterMaster>();
                    if (master != null && masterCooldowns.TryGetValue(master, out var cooldowns))
                    {
                        if (cooldowns.Length <= slot)
                        {
                            Array.Resize(ref cooldowns, (int)(slot + 1U));
                            for (int i = num; i < cooldowns.Length; i++)
                            {
                                cooldowns[i] = new();
                            }
                            masterCooldowns[master] = cooldowns;
                        }
                    }
                }
                return num;
            });
        }

        private static void Inventory_UpdateEquipment(ILContext il)
        {
            var c = new ILCursor(il);
            if (!c.TryGotoNext(
                MoveType.After,
                x => x.MatchLdloc(3),
                x => x.MatchLdfld<EquipmentState>("equipmentDef"),
                x => x.MatchLdfld<EquipmentDef>("cooldown")
            ))
            {
                SwitchChaosAndGesture.Logger.LogError(BASE_ERROR_MESSAGE + "RoR2.Inventory.UpdateEquipment");
                return;
            }
            c.Emit(OpCodes.Ldarg_0);
            c.Emit(OpCodes.Ldloc_1);
            c.Emit(OpCodes.Ldloc_2);
            c.EmitDelegate<Func<float, Inventory, uint, byte, float>>((equipmentCooldown, inventory, maxCharges, slot) =>
            {
                var master = inventory.GetComponent<CharacterMaster>();
                if (master != null && masterCooldowns.TryGetValue(master, out var cooldowns))
                {
                    if (cooldowns.Length <= slot)
                    {
                        // This should never happen
                        SwitchChaosAndGesture.Logger.LogWarning("IL.Inventory.UpdateEquipment cooldown array not resized properly.");
                        return equipmentCooldown;
                    }
                    var cooldownQueue = cooldowns[slot];
                    if (cooldownQueue.Count > 0 && popNextCooldown)
                    {
                        // Need to make sure first that we are not about to reach max charges or the pop would be wasted
                        var state = inventory.equipmentStateSlots[slot];
                        if (!(state.charges + (byte)1 >= maxCharges && !state.chargeFinishTime.isPositiveInfinity))
                        {
                            var extraCooldown = cooldownQueue[0];
                            cooldownQueue.RemoveAt(0);
                            return equipmentCooldown + extraCooldown * SwitchChaosAndGesture.chaosCooldownPenalty.Value;
                        }
                    }
                }
                return equipmentCooldown;
            });
        }

        private static void EquipmentSlot_OnEquipmentExecuted(ILContext il)
        {
            var errorMessage = BASE_ERROR_MESSAGE + "RoR2.EquipmentSlot.OnEquipmentExecuted";
            Inventory inventory = null;
            CharacterMaster master = null;
            byte slot = 0;
            bool addCooldownNow = false;
            var c = new ILCursor(il);
            if (!c.TryGotoNext(
                x => x.MatchLdarg(0),
                x => x.MatchCall<EquipmentSlot>("get_equipmentIndex")
            ))
            {
                SwitchChaosAndGesture.Logger.LogError(errorMessage);
                return;
            }
            c.Index += 1;
            // Setup stuff
            c.EmitDelegate<Func<EquipmentSlot, EquipmentSlot>>(equipmentSlot =>
            {
                // `Inventory.UpdateEquipment` is called shortly after in the original method
                // and we don't want to pop any cooldowns stored already. `addCooldownNow` will
                // take care of that at the end of this patch method.
                popNextCooldown = false;
                inventory = equipmentSlot.inventory;
                master = inventory.GetComponent<CharacterMaster>();
                slot = inventory.activeEquipmentSlot;
                var state = inventory.equipmentStateSlots[slot];
                var hasChaos = inventory.GetItemCount(DLC1Content.Items.RandomEquipmentTrigger) > 0;
                if (hasChaos)
                {
                    masterCooldowns[master][slot].Add(0f);
                }
                addCooldownNow = state.chargeFinishTime.isPositiveInfinity && hasChaos;
                return equipmentSlot;
            });
            if (!c.TryGotoNext(x => x.MatchCall<EquipmentSlot>("PerformEquipmentAction")))
            {
                SwitchChaosAndGesture.Logger.LogError(errorMessage);
                return;
            }
            c.Index += 3;
            c.EmitDelegate<Func<EquipmentIndex, EquipmentIndex>>((equipmentIndex) =>
            {
                var cooldownQueue = masterCooldowns[master][slot];
                if (cooldownQueue.Count == 0)
                {
                    SwitchChaosAndGesture.Logger.LogError("IL.EquipmentSlot.OnEquipmentExecuted: Empty cooldown queue");
                }
                else
                {
                    cooldownQueue[cooldownQueue.Count - 1] += EquipmentCatalog.GetEquipmentDef(equipmentIndex).cooldown;
                }
                return equipmentIndex;
            });
            if (!c.TryGotoNext(MoveType.After, x => x.MatchCall(typeof(EffectManager), "SpawnEffect")))
            {
                SwitchChaosAndGesture.Logger.LogError(errorMessage);
                return;
            }
            c.EmitDelegate(() => {
                if (addCooldownNow)
                {
                    var extraCooldown = masterCooldowns[master][slot][0] * inventory.CalculateEquipmentCooldownScale() * SwitchChaosAndGesture.chaosCooldownPenalty.Value;
                    masterCooldowns[master][slot].RemoveAt(0);
                    var state = inventory.GetEquipment(slot);
                    inventory.SetEquipment(new EquipmentState(state.equipmentIndex, state.chargeFinishTime + extraCooldown, state.charges), slot);
                }
                popNextCooldown = true;
            });
        }

        private static void Inventory_onInventoryChangedGlobal(Inventory inventory)
        {
            if (!NetworkServer.active)
            {
                return;
            }
            if (inventory.GetItemCount(DLC1Content.Items.RandomEquipmentTrigger) > 0)
            {
                return;
            }
            var master = inventory.GetComponent<CharacterMaster>();
            if (master != null && masterCooldowns.TryGetValue(master, out var cooldowns))
            {
                foreach (var slotQueue in cooldowns)
                {
                    slotQueue.Clear();
                }
            }
        }

        private static void Run_onRunDestroyGlobal(Run obj)
        {
            masterCooldowns.Clear();
        }

#if DEBUG
        [ConCommand(commandName = "dump_cooldowns", flags = ConVarFlags.ExecuteOnServer, helpText = "Dump the extra equipment cooldown queues.")]
        private static void CCDumpCooldowns(ConCommandArgs args)
        {
            var master = args.senderMaster;
            var sb = new System.Text.StringBuilder();
            foreach (var kvp in masterCooldowns)
            {
                sb.AppendLine((kvp.Key.playerCharacterMasterController ? kvp.Key.playerCharacterMasterController.GetDisplayName() : kvp.Key.name) + (kvp.Key == master ? " <--- Caller" : ""));
                var slots = kvp.Value;
                for (int i = 0; i < slots.Length; i++)
                {
                    sb.AppendLine("-Slot " + i + ": [" + string.Join(", ", slots[i]) + "]");
                }
            }
            Debug.Log(sb.ToString().Trim('\n'));
        }
#endif
    }
}