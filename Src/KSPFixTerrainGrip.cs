using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace com.github.lhervier.ksp.fix.terraingrip
{
    /// <summary>
    /// Installs the fix once, when KSP starts, before any vessel loads: on the bodies of the grip table, the wheels get
    /// the grip of the terrain they stand on, and the tyre grip of automatic friction no longer drops with gravity.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class KSPFixTerrainGrip : MonoBehaviour
    {
        private const string HarmonyId = "com.github.lhervier.ksp.fix.terraingrip";

        private void Start()
        {
            Settings.Load();
            string version = typeof(KSPFixTerrainGrip).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            Log.Info($"Version {version}, log level {Log.Level}");
            if (AutoFrictionCurve.Enabled)
            {
                try
                {
                    AutoFrictionCurve.Install(new Harmony(HarmonyId));
                    Log.Info("Automatic friction set to its value on Kerbin, on every body of the grip table");
                }
                catch (Exception e)
                {
                    Log.Error($"Could not patch automatic friction, it drops with gravity as in the game: {e}");
                }
            }
            if (TerrainGripManager.Enabled)
            {
                try
                {
                    TerrainGripManager.Install();
                    Log.Info("Terrain grip installed");
                }
                catch (Exception e)
                {
                    Log.Error($"Could not install the terrain grip, every terrain has the same grip as in the game: {e}");
                }
            }
        }
    }
}
