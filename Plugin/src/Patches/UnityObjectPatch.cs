using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Pool;
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

        var unorderedArray = GetFilteredTeleports(findObjectsInactive != FindObjectsInactive.Exclude);

        switch (sortMode)
        {
            case FindObjectsSortMode.None:
                break;
            case FindObjectsSortMode.InstanceID:
                Array.Sort(unorderedArray, InstanceComparer.INSTANCE);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(sortMode), sortMode, null);
        }

        __result = unorderedArray;
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

        __result = GetFilteredTeleports(includeInactive);
        return false;
    }

    private static EntranceTeleport[] GetFilteredTeleports(bool includeInactive)
    {
        using (ListPool<EntranceTeleport>.Get(out var tempList))
        {
            foreach (var teleport in EntranceTeleportPatches.TeleportList)
            {
                if (teleport == null)
                    continue;

                if (!includeInactive && !teleport.isActiveAndEnabled)
                    continue;

                tempList.Add(teleport);
            }

            return tempList.ToArray();
        }
    }

    private readonly struct InstanceComparer : IComparer<Object>
    {
        internal static readonly InstanceComparer INSTANCE = new();

        public int Compare(Object x, Object y)
        {
            if (y is null) return 1;
            if (x is null) return -1;
            return x.GetInstanceID().CompareTo(y.GetInstanceID());
        }
    }
}
