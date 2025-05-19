using HarmonyLib;
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
    private const string BASE_ERROR_MESSAGE = "Failed to patch method: ";

    private static bool popNextCooldown = false;
    internal static readonly Dictionary<CharacterMaster, List<float>[]> masterCooldowns = [];
    private static readonly HashSet<EquipmentIndex> bannedAutocastEquipment = [];

    public static void Init()
    {
        On.RoR2.EquipmentCatalog.SetEquipmentDefs += CollectGestureBlacklistedEquipment;
        IL.RoR2.EquipmentSlot.MyFixedUpdate += CheckEquipmentCanBeAutocast;
        IL.EntityStates.GoldGat.BaseGoldGatState.FixedUpdate += CheckCrowdfunderCanBeAutocast;
        On.RoR2.CharacterMaster.OnEnable += AddMasterToDict;
        On.RoR2.CharacterMaster.OnDisable += RemoveMasterFromDict;
        IL.RoR2.Inventory.SetEquipmentInternal += InitialiseCooldownTrackingForNewSlots;
        IL.RoR2.Inventory.UpdateEquipment += ApplyCooldownPenaltyOnChargeGain;
        IL.RoR2.EquipmentSlot.OnEquipmentExecuted += ApplyOrQueueCooldownPenaltyOnExecute;
        Inventory.onInventoryChangedGlobal += EnsureNoTrackedCooldownsWithoutChaos;
        Run.onRunDestroyGlobal += ResetMasterDict;
        On.RoR2.Language.GetLocalizedStringByToken += FormatBottledChaosDesc;
    }

    private static void CollectGestureBlacklistedEquipment(On.RoR2.EquipmentCatalog.orig_SetEquipmentDefs orig, EquipmentDef[] newEquipmentDefs)
    {
        orig(newEquipmentDefs);
        foreach (var name in SwitchChaosAndGesture.bannedAutocastEquipment.Value.Split(','))
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
            x => x.MatchCallOrCallvirt<Inventory>(nameof(Inventory.GetItemCount)),
            x => x.MatchLdcI4(0),
            x => x.MatchCgt()))
        {
            SwitchChaosAndGesture.Logger.LogError(BASE_ERROR_MESSAGE + il.Method.Name);
            return;
        }
        c.Emit(OpCodes.Ldarg_0);
        c.EmitDelegate<Func<bool, EquipmentSlot, bool>>((autocast, equipmentSlot) =>
        {
            return autocast && !bannedAutocastEquipment.Contains(equipmentSlot.equipmentIndex);
        });
    }

    private static void CheckCrowdfunderCanBeAutocast(ILContext il)
    {
        var c = new ILCursor(il);
        if (!c.TryGotoNext(
            MoveType.After,
            x => x.MatchCallOrCallvirt<Inventory>(nameof(Inventory.GetItemCount)),
            x => x.MatchLdcI4(0),
            x => x.MatchBle(out _),
            x => x.MatchLdarg(0),
            x => x.MatchLdcI4(1)))
        {
            SwitchChaosAndGesture.Logger.LogError(BASE_ERROR_MESSAGE + il.Method.Name);
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

    private static void InitialiseCooldownTrackingForNewSlots(ILContext il)
    {
        var c = new ILCursor(il);
        if (!c.TryGotoNext(
            MoveType.After,
            x => x.MatchLdarg(0),
            x => x.MatchLdfld<Inventory>(nameof(Inventory.equipmentStateSlots)),
            x => x.MatchLdlen(),
            x => x.MatchConvI4()))
        {
            SwitchChaosAndGesture.Logger.LogError(BASE_ERROR_MESSAGE + il.Method.Name);
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
                            cooldowns[i] = [];
                        }
                        masterCooldowns[master] = cooldowns;
                    }
                }
            }
            return num;
        });
    }

    private static void ApplyCooldownPenaltyOnChargeGain(ILContext il)
    {
        var c = new ILCursor(il);
        if (!c.TryGotoNext(
            MoveType.After,
            x => x.MatchLdfld<EquipmentState>(nameof(EquipmentState.equipmentDef)),
            x => x.MatchLdfld<EquipmentDef>(nameof(EquipmentDef.cooldown))))
        {
            SwitchChaosAndGesture.Logger.LogError(BASE_ERROR_MESSAGE + il.Method.Name);
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
                    SwitchChaosAndGesture.Logger.LogWarning("Inventory.UpdateEquipment cooldown array not resized properly.");
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

    private static void ApplyOrQueueCooldownPenaltyOnExecute(ILContext il)
    {
        var errorMessage = BASE_ERROR_MESSAGE + il.Method.Name;
        Inventory inventory = null;
        CharacterMaster master = null;
        byte slot = 0;
        bool addCooldownNow = false;
        var c = new ILCursor(il);
        if (!c.TryGotoNext(
            x => x.MatchLdarg(0),
            x => x.MatchCallOrCallvirt(AccessTools.PropertyGetter(typeof(EquipmentSlot), nameof(EquipmentSlot.equipmentIndex)))))
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
        if (!c.TryGotoNext(x => x.MatchCallOrCallvirt<EquipmentSlot>(nameof(EquipmentSlot.PerformEquipmentAction))))
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
                SwitchChaosAndGesture.Logger.LogError("EquipmentSlot.OnEquipmentExecuted: Empty cooldown queue");
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
            SwitchChaosAndGesture.Logger.LogError(errorMessage);
            return;
        }
        c.EmitDelegate(() =>
        {
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

    private static void EnsureNoTrackedCooldownsWithoutChaos(Inventory inventory)
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
        if (master && masterCooldowns.TryGetValue(master, out var cooldowns))
        {
            foreach (var slotQueue in cooldowns)
            {
                slotQueue.Clear();
            }
        }
    }

    private static void ResetMasterDict(Run obj)
    {
        masterCooldowns.Clear();
    }

    private static string FormatBottledChaosDesc(On.RoR2.Language.orig_GetLocalizedStringByToken orig, Language self, string token)
    {
        var result = orig(self, token);
        if (token == "ITEM_RANDOMEQUIPMENTTRIGGER_DESC")
        {
            result = string.Format(result, SwitchChaosAndGesture.chaosCooldownPenalty.Value * 100f);
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
                sb.AppendLine("-Slot " + i + ": [" + string.Join(", ", slots[i]) + "]");
            }
        }
        Debug.Log(sb.ToString().Trim('\n'));
    }
#endif
}