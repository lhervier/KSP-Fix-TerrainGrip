using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace com.github.lhervier.ksp.fix.terraingrip
{
    /// <summary>
    /// Gives the collider of each terrain quad the physic material of the material of its biome, so that what touches
    /// the terrain without wheels (a capsule on its side, a lander resting on its engines, a kerbal fallen over) gets the
    /// friction of that material: the game gives every quad of a body the same material, or none. The material combines
    /// by multiplication: an ordinary part on it gets the friction of the table, a part whose own material combines by
    /// its maximum (a grip pad) keeps its own grip.
    /// </summary>
    internal static class TerrainFriction
    {
        /// <summary>Whether the fix is installed (terrainFriction in the settings).</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>
        /// Patches the two places that give a quad its collider: <c>PQSMod_QuadMeshColliders.OnQuadBuilt</c> in the
        /// game, and <c>QuadColliderBaker.Apply</c> of BurstPQS when it is installed, which builds the colliders
        /// itself.
        /// </summary>
        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(PQSMod_QuadMeshColliders), nameof(PQSMod_QuadMeshColliders.OnQuadBuilt)),
                postfix: new HarmonyMethod(typeof(TerrainFriction), nameof(OnQuadBuiltPostfix)));
            MethodInfo burst = BurstApply();
            if (burst != null)
            {
                harmony.Patch(burst, postfix: new HarmonyMethod(typeof(TerrainFriction), nameof(BurstApplyPostfix)));
                Log.Info("Terrain friction also set on the colliders BurstPQS builds");
            }
        }

        /// <summary>QuadColliderBaker.Apply of BurstPQS, or null when BurstPQS is not there or has changed.</summary>
        private static MethodInfo BurstApply()
        {
            foreach (AssemblyLoader.LoadedAssembly loaded in AssemblyLoader.loadedAssemblies)
            {
                Type baker = loaded.assembly.GetType("BurstPQS.QuadColliderBaker", false);
                if (baker == null)
                {
                    continue;
                }
                MethodInfo apply = AccessTools.Method(baker, "Apply");
                if (apply == null)
                {
                    Log.Warning("BurstPQS has no QuadColliderBaker.Apply: its terrain keeps the game's friction");
                }
                return apply;
            }
            return null;
        }

        private static void OnQuadBuiltPostfix(PQ quad)
        {
            Apply(quad);
        }

        /// <summary>Postfix of <c>QuadColliderBaker.Apply(Entry entry)</c>: the quad is a field of the private entry.</summary>
        private static void BurstApplyPostfix(object[] __args)
        {
            Apply(Traverse.Create(__args[0]).Field("quad").GetValue<PQ>());
        }

        /// <summary>
        /// Gives a quad's collider the physic material of its biome, the one under its centre, when its body is in
        /// the grip table; leaves it as it is otherwise.
        /// </summary>
        private static void Apply(PQ quad)
        {
            if (quad == null || quad.meshCollider == null || !quad.meshCollider.enabled || quad.mesh == null)
            {
                return;
            }
            CelestialBody body = FlightGlobals.fetch != null ? FlightGlobals.GetBodyByName(quad.sphereRoot.name) : null;
            if (!GripTable.HasBody(body))
            {
                return;
            }
            Vector3d centre = quad.transform.TransformPoint(quad.mesh.bounds.center);
            string biome = ScienceUtil.GetExperimentBiome(body, body.GetLatitude(centre), body.GetLongitude(centre));
            string material = GripTable.BiomeMaterial(body, biome);
            PhysicMaterial physic = GripTable.TerrainMaterial(material);
            if (physic == null)
            {
                return;
            }
            quad.meshCollider.sharedMaterial = physic;
            if (Log.IsDebugEnabled)
            {
                Log.Debug($"Quad {quad.name} of {body.bodyName}, biome {biome}: {material} ({physic.staticFriction:F2})");
            }
        }
    }
}
