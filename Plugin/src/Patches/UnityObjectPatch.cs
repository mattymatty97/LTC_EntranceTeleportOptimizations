using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

// ReSharper disable CoVariantArrayConversion

namespace EntranceTeleportOptimizations.Patches;

[HarmonyPatch]
internal static class UnityObjectPatch
{
    [HarmonyPrefix]
    [HarmonyBefore("LethalPerformance")]
    [HarmonyPatch(typeof(Object), nameof(Object.FindObjectsByType), typeof(Type), typeof(FindObjectsInactive),
        typeof(FindObjectsSortMode))]
    private static bool FindObjectsByTypeRedirect(bool __runOriginal, Type type,
        FindObjectsInactive findObjectsInactive, FindObjectsSortMode sortMode, ref Object[] __result)
    {
        if (!__runOriginal)
            return false;

        if (type != typeof(EntranceTeleport))
            return true;

        var enumerable = GetFilteredTeleports(findObjectsInactive != FindObjectsInactive.Exclude);

        switch (sortMode)
        {
            case FindObjectsSortMode.None:
                __result = enumerable.ToArray();
                break;
            case FindObjectsSortMode.InstanceID:
                __result = enumerable.OrderBy(e => e.GetInstanceID()).ToArray();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(sortMode), sortMode, null);
        }

        return false;
    }

    [HarmonyPrefix]
    [HarmonyBefore("LethalPerformance")]
    [HarmonyPatch(typeof(Object), nameof(Object.FindObjectsOfType), typeof(Type), typeof(bool))]
    private static bool FindObjectsOfTypeRedirect(bool __runOriginal, Type type, bool includeInactive,
        ref Object[] __result)
    {
        if (!__runOriginal)
            return false;

        if (type != typeof(EntranceTeleport))
            return true;

        __result = GetFilteredTeleports(includeInactive).ToArray();
        return false;
    }

    private static IEnumerable<EntranceTeleport> GetFilteredTeleports(bool includeInactive)
    {
        var enumerator = EntranceTeleportPatches.TeleportList
            .Where(s => s != null);
        if (!includeInactive)
            enumerator = enumerator.Where(s => s.isActiveAndEnabled);
        return enumerator;
    }
}
