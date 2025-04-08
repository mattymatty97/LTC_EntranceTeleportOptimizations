using System;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using LobbyCompatibility.Enums;
using LobbyCompatibility.Features;

namespace EntranceTeleportOptimizations.Dependency
{
    public static class LobbyCompatibilityChecker
    {
        private static bool? _enabled;

        public static bool Enabled
        {
            get
            {
                _enabled ??= Chainloader.PluginInfos.ContainsKey("BMX.LobbyCompatibility");
                return _enabled.Value;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void Init()
        {
            PluginHelper.RegisterPlugin(EntranceTeleportOptimizations.GUID,
                Version.Parse(EntranceTeleportOptimizations.VERSION), CompatibilityLevel.Everyone,
                VersionStrictness.Minor);
        }
    }
}