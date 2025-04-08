using HarmonyLib;

namespace EntranceTeleportOptimizations.Patches;

public static class MenuManagerPatches
{
    [HarmonyFinalizer]
    [HarmonyPatch(typeof(MenuManager), nameof(MenuManager.Awake))]
    private static void OnMainMenu()
    {
        //sanity cleanup
        EntranceTeleportPatches.TeleportMap.Clear();
        EntranceTeleportPatches.TeleportList.Clear();
    }
}