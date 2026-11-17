using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace com.github.lhervier.ksp.fix.terraingrip
{
    /// <summary>
    /// The grip table, read from the game database (the .cfg files of GameData, once ModuleManager has patched them),
    /// the first time it is needed after the database is loaded:
    /// <list type="bullet">
    /// <item>TERRAINGRIP: the reference material (a tyre on it keeps the grip it has in the game), the friction of an
    /// ordinary part, and the ratio of dynamic to static friction of the terrain;</item>
    /// <item>TERRAINGRIP_MATERIAL: a material and its friction, the one an ordinary part has on it. The same value
    /// gives the grip of the wheels and the friction of the terrain's colliders;</item>
    /// <item>TERRAINGRIP_WHEELCLASS: a class of wheel and its factor on the grip (a tyre 1, the metal foot of a leg 3);</item>
    /// <item>TERRAINGRIP_SURFACES: the material of what is not terrain (statics, parts, asteroids, comets, rocks);</item>
    /// <item>TERRAINGRIP_PHYSICMATERIAL: the material of a part whose physic material the player picks (grip pads);</item>
    /// <item>TERRAINGRIP_PART: the wheel class of a part, when it is not the one deduced from its friction;</item>
    /// <item>TERRAINGRIP_BODY: the material of a body, and of some of its biomes. The fix changes nothing on a body
    /// no node names.</item>
    /// </list>
    /// </summary>
    internal static class GripTable
    {
        /// <summary>What every wheel gets from a ground the table does not know: the grip it has in the game.</summary>
        public const float StockGrip = 1f;

        private class BodyEntry
        {
            public string Material;
            public readonly Dictionary<string, string> Biomes = new Dictionary<string, string>();
        }

        private static bool loaded;
        private static string reference = "soil";
        private static float partFriction = 0.5f;
        private static PhysicMaterial partMaterial;
        private static float dynamicRatio = 0.85f;
        private static float patchFactor = 2f;
        private static readonly Dictionary<string, float> frictions = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> wheelFactors = new Dictionary<string, float>();
        private static readonly Dictionary<string, string> surfaces = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> physicMaterials = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> partClasses = new Dictionary<string, string>();
        private static readonly Dictionary<string, BodyEntry> bodies = new Dictionary<string, BodyEntry>();
        private static readonly Dictionary<string, PhysicMaterial> terrainMaterials = new Dictionary<string, PhysicMaterial>();

        /// <summary>Forgets the table, read again the next time it is needed (after a reload of the database).</summary>
        public static void Invalidate()
        {
            loaded = false;
        }

        /// <summary>Whether a node of the table names this body: the fix acts on it.</summary>
        public static bool HasBody(CelestialBody body)
        {
            return body != null && Load() && bodies.ContainsKey(body.bodyName);
        }

        /// <summary>
        /// The material of a biome of a body: the biome's own, else the body's; null for a body the table does not
        /// name.
        /// </summary>
        public static string BiomeMaterial(CelestialBody body, string biome)
        {
            BodyEntry entry;
            if (body == null || !Load() || !bodies.TryGetValue(body.bodyName, out entry))
            {
                return null;
            }
            string material;
            if (biome != null && entry.Biomes.TryGetValue(biome, out material))
            {
                return material;
            }
            return entry.Material;
        }

        /// <summary>The material of a kind of surface (static, part, asteroid, comet, rock), or null.</summary>
        public static string SurfaceMaterial(string surface)
        {
            string material;
            return Load() && surfaces.TryGetValue(surface, out material) ? material : null;
        }

        /// <summary>The material of a physic material a part's player picked (grip pads), or null.</summary>
        public static string PhysicMaterialMaterial(string physicMaterial)
        {
            string material;
            return physicMaterial != null && Load() && physicMaterials.TryGetValue(physicMaterial, out material)
                ? material : null;
        }

        /// <summary>The wheel class a node gives this part, or null.</summary>
        public static string PartClass(string partName)
        {
            string wheelClass;
            return partName != null && Load() && partClasses.TryGetValue(partName, out wheelClass) ? wheelClass : null;
        }

        /// <summary>
        /// The grip a material gives a class of wheel, as a factor on the wheel's own tyre curve: the factor of the
        /// class times the material's friction over the reference material's. The stock grip when the material or
        /// the reference is unknown; a class the table does not name has a factor of 1.
        /// </summary>
        public static float Grip(string material, string wheelClass)
        {
            float friction, referenceFriction, factor;
            if (material == null || !Load() || !frictions.TryGetValue(material, out friction)
                || !frictions.TryGetValue(reference, out referenceFriction) || referenceFriction <= 0f)
            {
                return StockGrip;
            }
            if (!wheelFactors.TryGetValue(wheelClass, out factor))
            {
                factor = 1f;
            }
            return factor * friction / referenceFriction;
        }

        /// <summary>
        /// The physic material of a part without one of its own: partFriction, combined by multiplication, so that two
        /// such parts rub at its square (0.25, the game's default "Metal") and a part on a terrain gets that
        /// terrain's friction. Null before the table is read.
        /// </summary>
        public static PhysicMaterial PartMaterial()
        {
            if (!Load())
            {
                return null;
            }
            if (partMaterial == null)
            {
                partMaterial = new PhysicMaterial("TerrainGrip-part")
                {
                    staticFriction = partFriction,
                    dynamicFriction = partFriction,
                    frictionCombine = PhysicMaterialCombine.Multiply,
                    bounciness = 0f,
                    bounceCombine = PhysicMaterialCombine.Multiply
                };
            }
            return partMaterial;
        }

        /// <summary>
        /// The physic material of a terrain of this material, made once, combined by multiplication: the material's
        /// friction over a part's, over patchFactor, so that a part without a physic material of its own resting flat on
        /// it gets the material's friction. PhysX caps the friction of each of the two anchors of a contact patch at the
        /// friction coefficient times the normal force of the whole patch (no improved patch friction in KSP): a flat
        /// contact rubs at twice the combined coefficient. Null for an unknown material.
        /// </summary>
        public static PhysicMaterial TerrainMaterial(string material)
        {
            float friction;
            if (material == null || !Load() || !frictions.TryGetValue(material, out friction))
            {
                return null;
            }
            PhysicMaterial physic;
            if (!terrainMaterials.TryGetValue(material, out physic))
            {
                float staticFriction = friction / (partFriction * patchFactor);
                physic = new PhysicMaterial("TerrainGrip-" + material)
                {
                    staticFriction = staticFriction,
                    dynamicFriction = staticFriction * dynamicRatio,
                    frictionCombine = PhysicMaterialCombine.Multiply,
                    bounciness = 0f,
                    bounceCombine = PhysicMaterialCombine.Average
                };
                terrainMaterials[material] = physic;
            }
            return physic;
        }

        /// <summary>
        /// Reads the table if it is not read yet and the database is ready; returns whether the table is there.
        /// </summary>
        private static bool Load()
        {
            if (loaded)
            {
                return true;
            }
            if (GameDatabase.Instance == null || !GameDatabase.Instance.IsReady())
            {
                return false;
            }
            frictions.Clear();
            wheelFactors.Clear();
            surfaces.Clear();
            physicMaterials.Clear();
            partClasses.Clear();
            bodies.Clear();
            terrainMaterials.Clear();
            partMaterial = null;
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("TERRAINGRIP"))
            {
                reference = node.GetValue("reference") ?? reference;
                partFriction = ReadPositive(node, "partFriction", partFriction);
                dynamicRatio = ReadPositive(node, "dynamicRatio", dynamicRatio);
                patchFactor = ReadPositive(node, "patchFactor", patchFactor);
            }
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("TERRAINGRIP_MATERIAL"))
            {
                AddNumber(node, "friction", frictions, "TERRAINGRIP_MATERIAL");
            }
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("TERRAINGRIP_WHEELCLASS"))
            {
                AddNumber(node, "factor", wheelFactors, "TERRAINGRIP_WHEELCLASS");
            }
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("TERRAINGRIP_SURFACES"))
            {
                foreach (ConfigNode.Value value in node.values)
                {
                    surfaces[value.name] = value.value;
                }
            }
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("TERRAINGRIP_PHYSICMATERIAL"))
            {
                AddPair(node, "material", physicMaterials, "TERRAINGRIP_PHYSICMATERIAL");
            }
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("TERRAINGRIP_PART"))
            {
                AddPair(node, "class", partClasses, "TERRAINGRIP_PART");
            }
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("TERRAINGRIP_BODY"))
            {
                string name = node.GetValue("name");
                if (name == null)
                {
                    Log.Warning("A TERRAINGRIP_BODY without a name, ignored");
                    continue;
                }
                BodyEntry entry;
                if (!bodies.TryGetValue(name, out entry))
                {
                    entry = new BodyEntry();
                    bodies[name] = entry;
                }
                entry.Material = node.GetValue("material") ?? entry.Material;
                foreach (ConfigNode biome in node.GetNodes("BIOME"))
                {
                    AddPair(biome, "material", entry.Biomes, "BIOME of " + name);
                }
            }
            loaded = true;
            Log.Info($"Grip table read: {frictions.Count} materials, {wheelFactors.Count} wheel classes, " +
                $"{bodies.Count} bodies, {surfaces.Count} surfaces, {physicMaterials.Count} physic materials, " +
                $"{partClasses.Count} parts; reference {reference}, part friction {partFriction}");
            CheckMaterials();
            return true;
        }

        /// <summary>Reads a positive number of a node, or keeps <paramref name="fallback"/> (with a warning).</summary>
        private static float ReadPositive(ConfigNode node, string key, float fallback)
        {
            string text = node.GetValue(key);
            if (text == null)
            {
                return fallback;
            }
            float value;
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0f)
            {
                return value;
            }
            Log.Warning($"TERRAINGRIP: {key} '{text}' is not a positive number, {fallback} kept");
            return fallback;
        }

        /// <summary>Adds the pair (name, number of <paramref name="key"/>) of a node to a map, or warns.</summary>
        private static void AddNumber(ConfigNode node, string key, Dictionary<string, float> map, string what)
        {
            string name = node.GetValue("name");
            string text = node.GetValue(key);
            float value;
            if (name == null || text == null
                || !float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value < 0f)
            {
                Log.Warning($"A {what} without a name or a {key} that is a number, ignored");
                return;
            }
            map[name] = value;
        }

        /// <summary>Adds the pair (name, value of <paramref name="key"/>) of a node to a map, or warns.</summary>
        private static void AddPair(ConfigNode node, string key, Dictionary<string, string> map, string what)
        {
            string name = node.GetValue("name");
            string value = node.GetValue(key);
            if (name == null || value == null)
            {
                Log.Warning($"A {what} without a name or a {key}, ignored");
                return;
            }
            map[name] = value;
        }

        /// <summary>Warns about every material named in the table that no TERRAINGRIP_MATERIAL defines.</summary>
        private static void CheckMaterials()
        {
            HashSet<string> named = new HashSet<string>(surfaces.Values) { reference };
            named.UnionWith(physicMaterials.Values);
            foreach (BodyEntry body in bodies.Values)
            {
                if (body.Material != null)
                {
                    named.Add(body.Material);
                }
                named.UnionWith(body.Biomes.Values);
            }
            foreach (string material in named)
            {
                if (!frictions.ContainsKey(material))
                {
                    Log.Warning($"Material {material} is used but not defined: the stock grip for it");
                }
            }
        }
    }
}
