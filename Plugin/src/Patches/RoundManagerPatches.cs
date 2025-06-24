using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EntranceTeleportOptimizations.Utils.IL;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Pool;
using Object = UnityEngine.Object;

namespace EntranceTeleportOptimizations.Patches;

[HarmonyPatch]
public static class RoundManagerPatches
{
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(RoundManager), nameof(RoundManager.FindMainEntrancePosition))]
    [HarmonyPatch(typeof(RoundManager), nameof(RoundManager.FindMainEntranceScript))]
    private static IEnumerable<CodeInstruction> ReplaceFindMainEntrancePosition(
        IEnumerable<CodeInstruction> instructions, ILGenerator ilGenerator, MethodBase method)
    {
        var codes = instructions.ToArray();
        // - var teleports = Object.FindObjectsOfType<EntranceTeleport>(includeInactive: false);
        // + var teleports = GetAllEntranceTeleports();
        var injector = new ILInjector(codes, ilGenerator)
            .Find([
                ILMatcher.Ldc(),
                ILMatcher.Call(typeof(Object).GetGenericMethod(nameof(Object.FindObjectsOfType), [typeof(bool)],
                    [typeof(EntranceTeleport)])),
                ILMatcher.Stloc().CaptureAs(out var storeArray),
            ]);

        if (!injector.IsValid)
        {
            injector.GoToStart();
            injector.PrintContext(15, $"{method.DeclaringType!.Name}.{method.Name}");
            EntranceTeleportOptimizations.Log.LogError(
                $"Failed to find the call to get all entrance teleports in {method.DeclaringType!.Name}.{method.Name}");
            return codes;
        }

        injector
            .ReplaceLastMatch([
                new CodeInstruction(OpCodes.Call,
                    typeof(EntranceTeleportPatches).GetMethod(nameof(EntranceTeleportPatches.GetAllEntranceTeleports),
                        BindingFlags.NonPublic | BindingFlags.Static)),
                storeArray,
            ]);

        // - for (int i = 0; i < teleports.Length; i++)
        // + for (int i = 0; i < TeleportList.Count; i++)
        // = {
        // +     if (teleports[i] == null)
        // +         continue;
        // +     if (!teleports[i].isActiveAndEnabled)
        // +         continue;
        // =     if (teleports[i].entranceId != 0)
        injector.Find([
                ILMatcher.Opcode(OpCodes.Br).CaptureOperandAs(out Label continueLabel),
            ])
            .FindLabel(continueLabel)
            .Find([
                ILMatcher.Ldloc().CaptureAs(out var loadIndex),
                ILMatcher.Ldloc(storeArray.GetStlocIndex()),
                ILMatcher.Opcode(OpCodes.Ldlen),
                ILMatcher.Opcode(OpCodes.Conv_I4),
            ]);

        if (!injector.IsValid)
        {
            EntranceTeleportOptimizations.Log.LogError(
                $"Failed to find the call to teleports.Length in {method.DeclaringType!.Name}.{method.Name}");
            return codes;
        }

        injector.ReplaceLastMatch([
                loadIndex.Clone(),
                new CodeInstruction(OpCodes.Ldsfld,
                    typeof(EntranceTeleportPatches).GetField(nameof(EntranceTeleportPatches.TeleportList),
                        BindingFlags.Static | BindingFlags.NonPublic)),
                new CodeInstruction(OpCodes.Call,
                    typeof(List<EntranceTeleport>).GetProperty(nameof(List<EntranceTeleport>.Count))!.GetMethod),
            ])
            .Find([
                ILMatcher.Branch().CaptureOperandAs(out Label loopStartLabel)
            ])
            .FindLabel(loopStartLabel);

        if (!injector.IsValid)
        {
            EntranceTeleportOptimizations.Log.LogError(
                $"Failed to find start of loop in  {method.DeclaringType!.Name}.{method.Name}");
            return codes;
        }

        var loadArray = storeArray.StlocToLdloc();
        injector.InsertAfterBranch([
                loadArray.Clone(),
                loadIndex.Clone(),
                new CodeInstruction(OpCodes.Ldelem_Ref),
                new CodeInstruction(OpCodes.Ldnull),
                new CodeInstruction(OpCodes.Call,
                    typeof(Object).GetMethod("op_Equality", [typeof(Object), typeof(Object)])),
                new CodeInstruction(OpCodes.Brtrue, continueLabel),
            ])
            .Insert([
                loadArray.Clone(),
                loadIndex.Clone(),
                new CodeInstruction(OpCodes.Ldelem_Ref),
                new CodeInstruction(OpCodes.Call,
                    typeof(Behaviour).GetProperty(nameof(Behaviour.isActiveAndEnabled))!.GetMethod),
                new CodeInstruction(OpCodes.Brfalse, continueLabel),
            ]);

        return injector.ReleaseInstructions();
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(RoundManager), nameof(RoundManager.SetExitIDs))]
    private static IEnumerable<CodeInstruction> PatchSetExitIDs(IEnumerable<CodeInstruction> instructions,
        ILGenerator ilGenerator, MethodBase method)
    {
        var codes = instructions.ToArray();
        // - var teleports = (from x in Object.FindObjectsOfType<EntranceTeleport>()
        // + var teleports = (from x in GetAvailableEntranceTeleports()
        // = orderby (x.transform.position - mainEntrancePosition).sqrMagnitude
        // = select x).ToArray();
        var injector = new ILInjector(codes, ilGenerator)
            .Find([
                ILMatcher.Stloc().CaptureAs(out var oldStloc),
                ILMatcher.Call(typeof(Object).GetGenericMethod(nameof(Object.FindObjectsOfType), [],
                    [typeof(EntranceTeleport)])),
                ILMatcher.Ldloc().CaptureAs(out var oldLdloc),
            ]);

        if (!injector.IsValid)
        {
            injector.GoToStart();
            injector.PrintContext(15, $"{method.DeclaringType!.Name}.{method.Name}");
            EntranceTeleportOptimizations.Log.LogError(
                $"Failed to find the call to get all entrance teleports in {method.DeclaringType!.Name}.{method.Name}");
            return codes;
        }

        injector
            .ReplaceLastMatch([
                oldStloc,
                new CodeInstruction(OpCodes.Call,
                    typeof(EntranceTeleportPatches).GetMethod(nameof(EntranceTeleportPatches.GetValidEntranceTeleports),
                        BindingFlags.NonPublic | BindingFlags.Static)),
                oldLdloc,
            ]);

        return injector.ReleaseInstructions();
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(RoundManager), nameof(RoundManager.SetExitIDs))]
    private static void OnDungeonReady()
    {
        using (DictionaryPool<int, (EntranceTeleport entrance, EntranceTeleport exit)>.Get(out var memory))
        {
            foreach (var teleport in EntranceTeleportPatches.TeleportList)
            {
                if (!teleport)
                    continue;

                EntranceTeleportPatches.TeleportMap.Remove(teleport);
                teleport.exitPoint = null;
                teleport.exitPointAudio = null;
                teleport.gotExitPoint = false;

                if (!teleport.isActiveAndEnabled)
                    continue;

                var id = teleport.entranceId;

                if (!memory.TryGetValue(id, out var element))
                {
                    element = new ValueTuple<EntranceTeleport, EntranceTeleport>();
                }

                if (teleport.isEntranceToBuilding)
                {
                    if (element.entrance != null)
                        EntranceTeleportOptimizations.Log.LogWarning($"Found duplicated entrance for id: {id}!");
                    else
                        element.entrance = teleport;
                }
                else
                {
                    if (element.exit != null)
                        EntranceTeleportOptimizations.Log.LogWarning($"Found duplicated exit for id: {id}!");
                    else
                        element.exit = teleport;
                }

                memory[id] = element;
            }

            foreach (var (id, (entrance, exit)) in memory)
            {
                if (entrance == null || exit == null)
                {
                    EntranceTeleportOptimizations.Log.LogWarning($"Found Missing Teleport for id: {id}!");
                    continue;
                }

                EntranceTeleportPatches.TeleportMap[entrance] = exit;
                entrance.exitPoint = exit.entrancePoint;
                entrance.exitPointAudio = exit.entrancePointAudio;
                entrance.gotExitPoint = true;
                //call the function in case a mod is listening for it
                entrance.FindExitPoint();

                EntranceTeleportPatches.TeleportMap[exit] = entrance;
                exit.exitPoint = entrance.entrancePoint;
                exit.exitPointAudio = entrance.entrancePointAudio;
                exit.gotExitPoint = true;
                //call the function in case a mod is listening for it
                exit.FindExitPoint();

                if (EntranceTeleportOptimizations.PluginConfig.RenameInteriorGameObjectsConfig.Value)
                    exit.gameObject.name = $"{entrance.gameObject.name} (Interior)";
            }

            EntranceTeleportOptimizations.Log.LogWarning(
                $"Connected {EntranceTeleportPatches.TeleportMap.Count} teleports");
        }
    }
}
