using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EntranceTeleportOptimizations.Utils.IL;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace EntranceTeleportOptimizations.Patches;

[HarmonyPatch]
internal static class EntranceTeleportPatches
{
    internal static readonly List<EntranceTeleport> TeleportList = [];
    internal static readonly Dictionary<EntranceTeleport, EntranceTeleport> TeleportMap = [];

    internal static EntranceTeleport[] GetAllEntranceTeleports()
    {
        return NoAllocHelpers.ExtractArrayFromListT(TeleportList);
    }

    internal static IEnumerable<EntranceTeleport> GetValidEntranceTeleports()
    {
        return TeleportList.Where(t => t && t.isActiveAndEnabled);
    }

    internal static float GetEnemyDetectionRange(EntranceTeleport @this)
    {
        if (@this.isEntranceToBuilding)
            return EntranceTeleportOptimizations.PluginConfig.InsideEnemyDetectionRangeConfig.Value;

        return EntranceTeleportOptimizations.PluginConfig.OutsideEnemyDetectionRangeConfig.Value;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(EntranceTeleport), nameof(EntranceTeleport.Awake))]
    private static void OnTeleportAwake(EntranceTeleport __instance)
    {
        TeleportList.Add(__instance);

        if (__instance.entranceId == 0)
            return;

        if (__instance.isEntranceToBuilding)
            return;

        if (__instance.entranceId != 1)
            EntranceTeleportOptimizations.Log.LogError($"Found FireExit with id {__instance.entranceId}");

        //force any fireExit generated in the dungeon to 1
        if (EntranceTeleportOptimizations.PluginConfig.PatchPrefabIDsConfig.Value)
            __instance.entranceId = 1;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(NetworkBehaviour), nameof(NetworkBehaviour.OnDestroy))]
    private static void OnTeleportDestroy(NetworkBehaviour __instance)
    {
        if (__instance is not EntranceTeleport teleport)
            return;

        TeleportList.Remove(teleport);
        TeleportMap.Remove(teleport);
    }

    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(EntranceTeleport), nameof(EntranceTeleport.FindExitPoint))]
    private static bool ReplaceFindExitPoint(EntranceTeleport __instance, ref bool __result, bool __runOriginal)
    {
        if (!__runOriginal)
        {
            EntranceTeleportOptimizations.Log.LogWarning("Another mod is trying to suppress FindExitPoint!");
        }

        //if we already have an exit
        if (__instance.exitPoint)
        {
            var hasValue = TeleportMap.TryGetValue(__instance, out var destination);
            if (hasValue && destination && destination.entranceId == __instance.entranceId)
            {
                __result = true;
                if (!__runOriginal)
                    __instance.exitPoint = destination.entrancePoint;
                return false;
            }

            if (!hasValue || !destination)
            {
                EntranceTeleportOptimizations.Log.LogWarning($"exitPoint for {__instance} was set outside this mod!");
            }
            else
            {
                EntranceTeleportOptimizations.Log.LogWarning(
                    $"{__instance} was using ID {destination.entranceId} but now is {__instance.entranceId}!");
            }
        }

        var isEntrance = __instance.isEntranceToBuilding;
        var id = __instance.entranceId;

        __result = false;
        foreach (var entranceTeleport in TeleportList)
        {
            if (!entranceTeleport)
                continue;

            if (!entranceTeleport.isActiveAndEnabled)
                continue;

            if (entranceTeleport.isEntranceToBuilding == isEntrance)
                continue;

            if (entranceTeleport.entranceId != id)
                continue;

            __instance.exitPoint = entranceTeleport.entrancePoint;
            __instance.exitPointAudio = entranceTeleport.entrancePointAudio;

            TeleportMap[__instance] = entranceTeleport;
            __result = true;

            if (!__instance.isEntranceToBuilding &&
                EntranceTeleportOptimizations.PluginConfig.RenameInteriorGameObjectsConfig.Value)
            {
                __instance.gameObject.name = $"{entranceTeleport.gameObject.name} (Interior)";
            }

            break;
        }

        return false;
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(EntranceTeleport), nameof(EntranceTeleport.Update))]
    private static IEnumerable<CodeInstruction> PatchUpdate(IEnumerable<CodeInstruction> instructions,
        ILGenerator ilGenerator, MethodBase method)
    {
        var codes = instructions.ToArray();

        var unityEqualityMethod =
            typeof(Object).GetMethod("op_Equality",
                [typeof(Object), typeof(Object)]);

        var injector = new ILInjector(codes, ilGenerator);

        if (EntranceTeleportOptimizations.PluginConfig.DetectEnemyBothSidesConfig.Value)
        {
            // - if (triggerScript == null || !isEntranceToBuilding)
            // + if (triggerScript == null)
            injector.Find([
                ILMatcher.Ldarg(),
                ILMatcher.Ldfld(typeof(EntranceTeleport).GetField(nameof(EntranceTeleport.isEntranceToBuilding))),
                ILMatcher.Branch(),
                ILMatcher.Opcode(OpCodes.Ret),
            ]);

            if (!injector.IsValid)
            {
                injector.GoToStart();
                injector.PrintContext(30, $"{method.DeclaringType!.Name}.{method.Name}");
                EntranceTeleportOptimizations.Log.LogError(
                    $"Failed to find the check for {nameof(EntranceTeleport.isEntranceToBuilding)} in {method.DeclaringType!.Name}.{method.Name}");
                return codes;
            }

            injector.RemoveLastMatch();
        }

        // - if (!gotExitPoint)
        // - {
        // -     if (FindExitPoint())
        // -     {
        // -         gotExitPoint = true;
        // -     }
        // - }
        // = checkForEnemiesInterval = 1f;
        injector.Find([
                ILMatcher.Ldarg().CaptureAs(out var loadThis),
                ILMatcher.Ldfld(typeof(EntranceTeleport).GetField(nameof(EntranceTeleport.gotExitPoint),
                    BindingFlags.Instance | BindingFlags.NonPublic)),
                ILMatcher.Branch().CaptureOperandAs(out Label trueLabel),
            ])
            .FindLabel(trueLabel);

        if (!injector.IsValid)
        {
            injector.GoToStart();
            injector.PrintContext(30, $"{method.DeclaringType!.Name}.{method.Name}");
            EntranceTeleportOptimizations.Log.LogError(
                $"Failed to find the check for {nameof(EntranceTeleport.gotExitPoint)} in {method.DeclaringType!.Name}.{method.Name}");
            return codes;
        }

        injector.RemoveLastMatch();

        // = checkForEnemiesInterval = 1f;
        // + if (exitPoint == null)
        // +     return;
        injector.Find([
            ILMatcher.Ldarg(),
            ILMatcher.LdcF32(),
            ILMatcher.Stfld(typeof(EntranceTeleport).GetField(nameof(EntranceTeleport.checkForEnemiesInterval),
                BindingFlags.Instance | BindingFlags.NonPublic)),
        ]);

        if (!injector.IsValid)
        {
            injector.GoToStart();
            injector.PrintContext(30, $"{method.DeclaringType!.Name}.{method.Name}");
            EntranceTeleportOptimizations.Log.LogError(
                $"Failed to find store of {nameof(EntranceTeleport.checkForEnemiesInterval)} in {method.DeclaringType!.Name}.{method.Name}");
            return codes;
        }

        injector.GoToMatchEnd()
            .AddLabel(out Label newContinueLabel)
            .Insert([
                loadThis,
                new CodeInstruction(OpCodes.Ldfld,
                    typeof(EntranceTeleport).GetField(nameof(EntranceTeleport.exitPoint))),
                new CodeInstruction(OpCodes.Ldnull),
                new CodeInstruction(OpCodes.Call, unityEqualityMethod),
                new CodeInstruction(OpCodes.Brfalse, newContinueLabel),
                new CodeInstruction(OpCodes.Ret),
            ]);

        // = for (int i = 0; i < RoundManager.Instance.SpawnedEnemies.Count; i++)
        // = {
        // +    if (RoundManager.Instance.SpawnedEnemies[i] == null)
        // +        continue;
        // =    if (Vector3.Distance(RoundManager.Instance.SpawnedEnemies[i].transform.position, exitPoint.transform.position) < 7.7f && !RoundManager.Instance.SpawnedEnemies[i].isEnemyDead)

        var roundManagerInstanceMethod = typeof(RoundManager).GetProperty(nameof(RoundManager.Instance))!.GetMethod;
        var roundManagerSpawnedEnemiesField = typeof(RoundManager).GetField(nameof(RoundManager.SpawnedEnemies));

        injector.Find([
                ILMatcher.Ldloc(),
                ILMatcher.Call(roundManagerInstanceMethod),
                ILMatcher.Ldfld(roundManagerSpawnedEnemiesField),
                ILMatcher.Callvirt(typeof(List<EnemyAI>).GetProperty(nameof(List<EnemyAI>.Count))!.GetMethod),
                ILMatcher.Branch().CaptureOperandAs(out Label loopStart)
            ])
            .FindLabel(loopStart);

        if (!injector.IsValid)
        {
            injector.GoToStart();
            injector.PrintContext(30, $"{method.DeclaringType!.Name}.{method.Name}");
            EntranceTeleportOptimizations.Log.LogError(
                $"Failed to find start of enemies loop {nameof(EntranceTeleport.checkForEnemiesInterval)} in {method.DeclaringType!.Name}.{method.Name}");
            return codes;
        }


        injector.Back(1);

        var loopContinue = (Label)injector.Instruction.operand;

        injector.Find([
            ILMatcher.Call(roundManagerInstanceMethod),
            ILMatcher.Ldfld(roundManagerSpawnedEnemiesField),
            ILMatcher.Ldloc(),
            ILMatcher.Callvirt(typeof(List<EnemyAI>).GetMethod("get_Item"))
        ]);

        if (!injector.IsValid)
        {
            injector.GoToStart();
            injector.PrintContext(30, $"{method.DeclaringType!.Name}.{method.Name}");
            EntranceTeleportOptimizations.Log.LogError(
                $"Failed to find enemy indexing {nameof(EntranceTeleport.checkForEnemiesInterval)} in {method.DeclaringType!.Name}.{method.Name}");
            return codes;
        }

        injector.GoToMatchEnd()
            .DefineLabel(out var afterCheck)
            .Insert([
                new CodeInstruction(OpCodes.Dup),
                new CodeInstruction(OpCodes.Ldnull),
                new CodeInstruction(OpCodes.Call, unityEqualityMethod),
                new CodeInstruction(OpCodes.Brfalse, afterCheck),
                new CodeInstruction(OpCodes.Pop),
                new CodeInstruction(OpCodes.Br, loopContinue),
            ])
            .AddLabel(afterCheck);

        // - if (Vector3.Distance(RoundManager.Instance.SpawnedEnemies[i].transform.position, exitPoint.transform.position) < 7.7f
        // = && !RoundManager.Instance.SpawnedEnemies[i].isEnemyDead)
        // + if (Vector3.Distance(RoundManager.Instance.SpawnedEnemies[i].transform.position, exitPoint.transform.position) < GetEnemyDetectionRange(this)
        // = && !RoundManager.Instance.SpawnedEnemies[i].isEnemyDead)

        injector.Find([
            ILMatcher.Call(typeof(Vector3).GetMethod(nameof(Vector3.Distance), [typeof(Vector3), typeof(Vector3)]))
                .CaptureAs(out var distanceCallInsruction),
            ILMatcher.LdcF32(7.7f),
            ILMatcher.Branch().CaptureAs(out var continueInsruction)
        ]);

        if (!injector.IsValid)
        {
            injector.GoToStart();
            injector.PrintContext(40, $"{method.DeclaringType!.Name}.{method.Name}");
            EntranceTeleportOptimizations.Log.LogError(
                $"Failed to find call to {nameof(Vector3)}.{nameof(Vector3.Distance)} in {method.DeclaringType!.Name}.{method.Name}");
            return codes;
        }

        injector.ReplaceLastMatch([
            distanceCallInsruction,
            loadThis,
            new CodeInstruction(OpCodes.Call,
                typeof(EntranceTeleportPatches).GetMethod(nameof(GetEnemyDetectionRange),
                    BindingFlags.Static | BindingFlags.NonPublic)),
            continueInsruction
        ]);

        return injector.ReleaseInstructions();
    }
}
