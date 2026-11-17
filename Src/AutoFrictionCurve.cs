using HarmonyLib;

namespace com.github.lhervier.ksp.fix.terraingrip
{
    /// <summary>
    /// Gives a wheel in automatic friction the tyre grip it has on Kerbin, on every body the grip table names. The game
    /// scales the tyre curve by geeBias × g + 0.4 (×2 on Kerbin, ×0.67 on the Mun, ×3.1 on Eve), on top of the weight
    /// the wheel carries, which already follows gravity: its grip went as the square of gravity.
    /// </summary>
    internal static class AutoFrictionCurve
    {
        /// <summary>Whether the fix is installed (kerbinAutoFriction in the settings).</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>Patches <c>ModuleWheelBase.ApplyGeeBias</c>.</summary>
        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(ModuleWheelBase), nameof(ModuleWheelBase.ApplyGeeBias)),
                prefix: new HarmonyMethod(typeof(AutoFrictionCurve), nameof(ApplyGeeBiasPrefix)));
        }

        /// <summary>
        /// Prefix of <c>ModuleWheelBase.ApplyGeeBias(float gee)</c>, which the game calls with the surface gravity of
        /// the wheel's body when the wheel is set up, when the player switches automatic friction on, and when the
        /// vessel changes sphere of influence: for a wheel of the friction model of the stock wheels, on a body the grip
        /// table names, that gravity is replaced by Kerbin's (1 g), so that automatic friction gets the value it has on
        /// Kerbin, geeBias of the part included. The old model (useNewFrictionModel false, some wheels of other mods)
        /// gives more grip at low gravity, not less: it is left as it is. Manual friction never reads gee.
        /// </summary>
        private static void ApplyGeeBiasPrefix(ModuleWheelBase __instance, ref float gee)
        {
            if (__instance.useNewFrictionModel && GripTable.HasBody(BodyOf(__instance, gee)))
            {
                gee = 1f;
            }
        }

        /// <summary>
        /// The body whose surface gravity the game passes: the body of that gravity among the vessel's and the others
        /// (on a change of sphere of influence, the game passes the new body's), else the vessel's.
        /// </summary>
        private static CelestialBody BodyOf(ModuleWheelBase wheel, float gee)
        {
            CelestialBody main = wheel.part != null && wheel.part.vessel != null ? wheel.part.vessel.mainBody : null;
            if (main != null && (float)main.GeeASL == gee)
            {
                return main;
            }
            foreach (CelestialBody body in FlightGlobals.Bodies)
            {
                if ((float)body.GeeASL == gee)
                {
                    return body;
                }
            }
            return main;
        }
    }
}
