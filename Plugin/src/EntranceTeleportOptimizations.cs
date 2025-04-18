using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EntranceTeleportOptimizations.Dependency;
using HarmonyLib;
using MonoMod.RuntimeDetour;

namespace EntranceTeleportOptimizations
{
    [BepInPlugin(GUID, NAME, VERSION)]
    [BepInDependency("BMX.LobbyCompatibility", Flags: BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("ainavt.lc.lethalconfig", Flags: BepInDependency.DependencyFlags.SoftDependency)]
    internal class EntranceTeleportOptimizations : BaseUnityPlugin
    {
        // ReSharper disable once CollectionNeverQueried.Global
        internal static readonly ISet<Hook> Hooks = new HashSet<Hook>();
        internal static readonly Harmony Harmony = new Harmony(GUID);

        public static EntranceTeleportOptimizations INSTANCE { get; private set; }

        public const string GUID = MyPluginInfo.PLUGIN_GUID;
        public const string NAME = MyPluginInfo.PLUGIN_NAME;
        public const string VERSION = MyPluginInfo.PLUGIN_VERSION;

        internal static ManualLogSource Log;

        private void Awake()
        {
            INSTANCE = this;
            Log = Logger;
            try
            {
                if (LobbyCompatibilityChecker.Enabled)
                    LobbyCompatibilityChecker.Init();

                Log.LogInfo("Initializing Configs");

                PluginConfig.Init();

                Log.LogInfo("Patching Methods");

                Harmony.PatchAll();

                Log.LogInfo(NAME + " v" + VERSION + " Loaded!");
            }
            catch (Exception ex)
            {
                Log.LogError("Exception while initializing: \n" + ex);
            }
        }

        internal static class PluginConfig
        {
            internal static ConfigEntry<bool> PatchPrefabIDsConfig;
            internal static ConfigEntry<bool> RenameInteriorGameObjectsConfig;

            internal static ConfigEntry<bool> DetectEnemyBothSidesConfig;
            internal static ConfigEntry<float> InsideEnemyDetectionRangeConfig;
            internal static ConfigEntry<float> OutsideEnemyDetectionRangeConfig;

            internal const float InsideEnemyDetectionRange = 7.7f;
            internal const float OutsideEnemyDetectionRange = 30f;

            internal static void Init()
            {
                var config = INSTANCE.Config;

                config.SaveOnConfigSet = false;
                //Initialize Configs

                PatchPrefabIDsConfig = config.Bind("Fixes", "fix_prefab_IDs", true,
                    "force all interior teleports ( except main ) to an ID of 1 on spawn");

                DetectEnemyBothSidesConfig = config.Bind("Extra", "detect_enemy_both_sides", false,
                    "allow interior teleports to detect enemies on the outside");

                InsideEnemyDetectionRangeConfig = config.Bind("Extra", "inside_enemy_detection_range",
                    InsideEnemyDetectionRange,
                    new ConfigDescription(
                        "how close an enemy has to be from the teleport for it to show [Near activity detected!]",
                        new AcceptableValueRange<float>(1f, 100f)));

                OutsideEnemyDetectionRangeConfig = config.Bind("Extra", "outside_enemy_detection_range",
                    OutsideEnemyDetectionRange,
                    new ConfigDescription(
                        "how close an enemy has to be from the teleport for it to show [Near activity detected!]",
                        new AcceptableValueRange<float>(1f, 100f)));

                RenameInteriorGameObjectsConfig = config.Bind("Debug", "rename_interior_gameobjects", false,
                    "rename interior teleports to match their connected exterior teleports");

                if (LethalConfigProxy.Enabled)
                {
                    LethalConfigProxy.AddConfig(PatchPrefabIDsConfig, true);

                    LethalConfigProxy.AddConfig(DetectEnemyBothSidesConfig, true);
                    LethalConfigProxy.AddConfig(InsideEnemyDetectionRangeConfig, false);
                    LethalConfigProxy.AddConfig(OutsideEnemyDetectionRangeConfig, false);

                    LethalConfigProxy.AddConfig(RenameInteriorGameObjectsConfig, true);
                }

                config.SaveOnConfigSet = true;
                CleanAndSave();
            }


            internal static void CleanAndSave()
            {
                var config = INSTANCE.Config;
                //remove unused options
                var orphanedEntriesProp = AccessTools.Property(config.GetType(), "OrphanedEntries");

                var orphanedEntries = (Dictionary<ConfigDefinition, string>)orphanedEntriesProp!.GetValue(config, null);

                orphanedEntries.Clear(); // Clear orphaned entries (Unbinded/Abandoned entries)
                config.Save(); // Save the config file
            }
        }
    }
}
