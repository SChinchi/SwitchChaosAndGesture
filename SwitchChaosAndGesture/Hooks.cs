using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace SwitchChaosAndGesture;

internal class Hooks
{
    private static bool popNextCooldown = false;
    private static readonly Dictionary<CharacterMaster, List<float>[][]> masterCooldowns = [];
    private static readonly HashSet<EquipmentIndex> bannedAutocastEquipment = [];

    public static void Init()
    {
        On.RoR2.EquipmentCatalog.SetEquipmentDefs += CollectGestureBlacklistedEquipment;
        IL.RoR2.EquipmentSlot.MyFixedUpdate += CheckEquipmentCanBeAutocast;
        IL.EntityStates.GoldGat.BaseGoldGatState.FixedUpdate += CheckCrowdfunderCanBeAutocast;
        On.RoR2.CharacterMaster.OnEnable += AddMasterToDict;
        On.RoR2.CharacterMaster.OnDisable += RemoveMasterFromDict;
        On.RoR2.Inventory.SetEquipmentInternal_EquipmentState_uint_uint += InitialiseCooldownTrackingForNewSlots;
        IL.RoR2.Inventory.UpdateEquipment += ApplyCooldownPenaltyOnChargeGain;
        IL.RoR2.Inventory.CalculateEquipmentCooldownScale += ModifyGestureCooldownScaling;
        IL.RoR2.EquipmentSlot.OnEquipmentExecuted_byte_byte_EquipmentIndex += ApplyOrQueueCooldownPenaltyOnExecute;
        Inventory.onInventoryChangedGlobal += EnsureNoTrackedCooldownsWithoutChaos;
        Run.onRunDestroyGlobal += ResetMasterDict;
        On.RoR2.Language.GetLocalizedStringByToken += FormatBottledChaosDesc;
    }

    private static void CollectGestureBlacklistedEquipment(On.RoR2.EquipmentCatalog.orig_SetEquipmentDefs orig, EquipmentDef[] newEquipmentDefs)
    {
        orig(newEquipmentDefs);
        ReloadBlacklistedEquipment();
    }

    internal static void ReloadBlacklistedEquipment()
    {
        bannedAutocastEquipment.Clear();
        foreach (var name in Configs.BannedAutocastEquipment.Value.Split(','))
        {
            bannedAutocastEquipment.Add(EquipmentCatalog.FindEquipmentIndex(name.Trim()));
        }
        // In case of any name typos resulting to none
        bannedAutocastEquipment.Remove(EquipmentIndex.None);
    }

    private static void CheckEquipmentCanBeAutocast(ILContext il)
    {
        var c = new ILCursor(il);
        if (!c.TryGotoNext(
            MoveType.After,
            x => x.MatchLdsfld(typeof(RoR2Content.Items), nameof(RoR2Content.Items.AutoCastEquipment)),
            x => x.MatchCallOrCallvirt<Inventory>(nameof(Inventory.GetItemCountEffective))))
        {
            Log.PatchFail(il);
            return;
        }
        c.Emit(OpCodes.Ldarg_0);
        c.EmitDelegate<Func<int, EquipmentSlot, int>>((canAutocast, equipmentSlot) =>
        {
            return !bannedAutocastEquipment.Contains(equipmentSlot.equipmentIndex) ? canAutocast : 0;
        });
    }

    private static void CheckCrowdfunderCanBeAutocast(ILContext il)
    {
        var c = new ILCursor(il);
        if (!c.TryGotoNext(
            MoveType.After,
            x => x.MatchCallOrCallvirt<Inventory>(nameof(Inventory.GetItemCountEffective)),
            x => x.MatchLdcI4(0),
            x => x.MatchBle(out _),
            x => x.MatchLdarg(0),
            x => x.MatchLdcI4(1)))
        {
            Log.PatchFail(il);
            return;
        }
        c.Emit(OpCodes.Ldarg_0);
        c.EmitDelegate<Func<bool, EntityStates.GoldGat.BaseGoldGatState, bool>>((autocast, state) =>
        {
            return state.shouldFire || (autocast && !bannedAutocastEquipment.Contains(RoR2Content.Equipment.GoldGat.equipmentIndex));
        });
    }

    private static void AddMasterToDict(On.RoR2.CharacterMaster.orig_OnEnable orig, CharacterMaster self)
    {
        orig(self);
        if (NetworkServer.active && !masterCooldowns.ContainsKey(self))
        {
            masterCooldowns[self] = [];
        }
    }

    private static void RemoveMasterFromDict(On.RoR2.CharacterMaster.orig_OnDisable orig, CharacterMaster self)
    {
        orig(self);
        if (NetworkServer.active && masterCooldowns.ContainsKey(self))
        {
            masterCooldowns.Remove(self);
        }
    }

    private static bool InitialiseCooldownTrackingForNewSlots(On.RoR2.Inventory.orig_SetEquipmentInternal_EquipmentState_uint_uint orig, Inventory self, EquipmentState equipmentState, uint slot, uint set)
    {
        if (NetworkServer.active
            && Run.instance
            && !Run.instance.IsEquipmentExpansionLocked(equipmentState.equipmentIndex))
        {
            var master = self.GetComponent<CharacterMaster>();
            if (master != null && masterCooldowns.TryGetValue(master, out var slots))
            {
                int currentSlotLength = slots.Length;
                if (currentSlotLength <= slot)
                {
                    Array.Resize(ref slots, (int)(slot + 1U));
                    for (int i = currentSlotLength; i < slots.Length; i++)
                    {
                        slots[i] = [];
                    }
                    masterCooldowns[master] = slots;
                }
                int currentSetLength = slots[slot].Length;
                if (currentSetLength <= set)
                {
                    var slotSets = slots[slot];
                    Array.Resize(ref slotSets, (int)(set + 1U));
                    for (int i = currentSetLength; i < slotSets.Length; i++)
                    {
                        slotSets[i] = [];
                    }
                    slots[slot] = slotSets;
                }
            }
        }
        return orig(self, equipmentState, slot, set);
    }

    private static void ApplyCooldownPenaltyOnChargeGain(ILContext il)
    {
        var c = new ILCursor(il);
        if (!c.TryGotoNext(
            MoveType.After,
            x => x.MatchLdfld<EquipmentState>(nameof(EquipmentState.equipmentDef)),
            x => x.MatchLdfld<EquipmentDef>(nameof(EquipmentDef.cooldown))))
        {
            Log.PatchFail(il);
            return;
        }
        c.Emit(OpCodes.Ldarg_0);
        c.Emit(OpCodes.Ldloc_1);
        c.Emit(OpCodes.Ldloc_2);
        c.Emit(OpCodes.Ldloc_3);
        c.EmitDelegate<Func<float, Inventory, uint, uint, byte, float>>((equipmentCooldown, inventory, maxCharges, slot, set) =>
        {
            var master = inventory.GetComponent<CharacterMaster>();
            if (master != null && masterCooldowns.TryGetValue(master, out var cooldowns))
            {
                if (cooldowns.Length <= slot || cooldowns[slot].Length <= set)
                {
                    // This should never happen
                    Log.Warning("Inventory.UpdateEquipment cooldown array not resized properly.");
                    return equipmentCooldown;
                }
                var cooldownQueue = cooldowns[slot][set];
                if (cooldownQueue.Count > 0 && popNextCooldown)
                {
                    // Need to make sure first that we are not about to reach max charges or the pop would be wasted
                    var state = inventory._equipmentStateSlots[slot][set];
                    if (!(state.charges + (byte)1 >= maxCharges && !state.chargeFinishTime.isPositiveInfinity))
                    {
                        var extraCooldown = cooldownQueue[0];
                        cooldownQueue.RemoveAt(0);
                        return equipmentCooldown + extraCooldown * Configs.ChaosCooldownPenalty.Value;
                    }
                }
            }
            return equipmentCooldown;
        });
    }

    private static void ModifyGestureCooldownScaling(ILContext il)
    {
        var c = new ILCursor(il);
        int gestureStacksVar = -1;
        if (!c.TryGotoNext(
                x => x.MatchLdsfld(typeof(RoR2Content.Items), nameof(RoR2Content.Items.AutoCastEquipment)),
                x => x.MatchCallOrCallvirt<Inventory>(nameof(Inventory.GetItemCountEffective)),
                x => x.MatchStloc(out gestureStacksVar)) ||
            !c.TryGotoNext(
                x => x.MatchLdcR4(out _), // first stack
                x => x.MatchLdcR4(out _), // scaling
                x => x.MatchLdloc(gestureStacksVar)))
        {
            Log.PatchFail(il);
            return;
        }
        c.Index += 2;
        c.EmitDelegate<Func<float, float>>(scaling =>
        {
            return 1 - Configs.GestureScaling.Value;
        });
    }

    private static void ApplyOrQueueCooldownPenaltyOnExecute(ILContext il)
    {
        Inventory inventory = null;
        CharacterMaster master = null;
        byte slot = 0;
        byte set = 0;
        bool addCooldownNow = false;
        var c = new ILCursor(il);
        if (!c.TryGotoNext(
            MoveType.After,
            x => x.MatchLdarg(0),
            x => x.MatchLdfld<EquipmentSlot>(nameof(EquipmentSlot.inventory))))
        {
            Log.PatchFail(il.Method.Name + " #1");
            return;
        }
        c.Emit(OpCodes.Ldarg_0);
        c.Emit(OpCodes.Ldarg_1);
        c.Emit(OpCodes.Ldarg_2);
        // Setup stuff
        c.EmitDelegate<Action<EquipmentSlot, byte, byte>>((equipmentSlot, currentSlot, currentSet) =>
        {
            // `Inventory.UpdateEquipment` is called shortly after in the original method
            // and we don't want to pop any cooldowns stored already. `addCooldownNow` will
            // take care of that at the end of this patch method.
            popNextCooldown = false;
            inventory = equipmentSlot.inventory;
            master = inventory.GetComponent<CharacterMaster>();
            slot = currentSlot;
            set = currentSet;
            var state = inventory.GetActiveEquipment();
            var hasChaos = inventory.GetItemCountEffective(DLC1Content.Items.RandomEquipmentTrigger) > 0;
            if (hasChaos)
            {
                masterCooldowns[master][slot][set].Add(0f);
            }
            addCooldownNow = state.chargeFinishTime.isPositiveInfinity && hasChaos;
        });
        if (!c.TryGotoNext(x => x.MatchCallOrCallvirt<EquipmentSlot>(nameof(EquipmentSlot.PerformEquipmentAction))))
        {
            Log.PatchFail(il.Method.Name + " #2");
            return;
        }
        c.Index += 3;
        c.EmitDelegate<Func<EquipmentIndex, EquipmentIndex>>((equipmentIndex) =>
        {
            var cooldownQueue = masterCooldowns[master][slot][set];
            if (cooldownQueue.Count == 0)
            {
                Log.Error("EquipmentSlot.OnEquipmentExecuted: Empty cooldown queue");
            }
            else
            {
                cooldownQueue[cooldownQueue.Count - 1] += EquipmentCatalog.GetEquipmentDef(equipmentIndex).cooldown;
            }
            return equipmentIndex;
        });
        if (!c.TryGotoNext(
            MoveType.After,
            x => x.MatchCallOrCallvirt(typeof(EffectManager), nameof(EffectManager.SpawnEffect))))
        {
            Log.PatchFail(il.Method.Name + " #3");
            return;
        }
        c.EmitDelegate(() =>
        {
            if (addCooldownNow)
            {
                var extraCooldown = masterCooldowns[master][slot][set][0] * inventory.CalculateEquipmentCooldownScale() * Configs.ChaosCooldownPenalty.Value;
                masterCooldowns[master][slot][set].RemoveAt(0);
                var state = inventory.GetEquipment(slot, set);
                inventory.SetEquipment(new EquipmentState(state.equipmentIndex, state.chargeFinishTime + extraCooldown, state.charges), slot, set);
            }
            popNextCooldown = true;
        });
    }

    private static void EnsureNoTrackedCooldownsWithoutChaos(Inventory inventory)
    {
        if (!NetworkServer.active)
        {
            return;
        }
        if (inventory.GetItemCountEffective(DLC1Content.Items.RandomEquipmentTrigger) > 0)
        {
            return;
        }
        var master = inventory.GetComponent<CharacterMaster>();
        if (master && masterCooldowns.TryGetValue(master, out var cooldowns))
        {
            foreach (var slot in cooldowns)
            {
                foreach (var set in slot)
                {
                    set.Clear();
                }
            }
        }
    }

    private static void ResetMasterDict(Run _)
    {
        masterCooldowns.Clear();
    }

    private static string FormatBottledChaosDesc(On.RoR2.Language.orig_GetLocalizedStringByToken orig, Language self, string token)
    {
        var result = orig(self, token);
        if (token == "ITEM_RANDOMEQUIPMENTTRIGGER_DESC")
        {
            result = string.Format(result, Configs.ChaosCooldownPenalty.Value * 100f);
        }
        return result;
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
                for (int j = 0; j < slots[i].Length; j++)
                {
                    sb.AppendLine($"-Slot {i}: [{string.Join(", ", slots[i][j])}]");
                }
            }
        }
        Debug.Log(sb.ToString().Trim('\n'));
    }
#endif
}