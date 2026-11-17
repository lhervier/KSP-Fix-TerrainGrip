using System;
using System.Collections.Generic;
using System.Globalization;

namespace com.github.lhervier.ksp.fix.terraingrip
{
    /// <summary>
    /// The grip table, read from the game database (the .cfg files of GameData, once ModuleManager has patched them),
    /// the first time it is needed after the database is loaded:
    /// <list type="bullet">
    /// <item>TERRAINGRIP_MATERIAL: a material, and the grip it gives each class of wheel (tyre = 0.8, metal = 3...);</item>
    /// <item>TERRAINGRIP_SURFACES: the material of what is not terrain (statics, parts, asteroids, comets, rocks);</item>
    /// <item>TERRAINGRIP_PHYSICMATERIAL: the material of a part whose physic material the player picks (grip pads);</item>
    /// <item>TERRAINGRIP_PART: the wheel class of a part, when it is not the one deduced from its friction;</item>
    /// <item>TERRAINGRIP_BODY: the material of a body, and of some of its biomes. The mod changes nothing on a body
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
        private static readonly Dictionary<string, Dictionary<string, float>> materials =
            new Dictionary<string, Dictionary<string, float>>();
        private static readonly Dictionary<string, string> surfaces = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> physicMaterials = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> partClasses = new Dictionary<string, string>();
        private static readonly Dictionary<string, BodyEntry> bodies = new Dictionary<string, BodyEntry>();

        /// <summary>Forgets the table, read again the next time it is needed (after a reload of the database).</summary>
        public static void Invalidate()
        {
            loaded = false;
        }

        /// <summary>Whether a node of the table names this body: the mod acts on it.</summary>
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
        /// The grip a material gives a class of wheel; the stock grip when the material is unknown, or names no
        /// grip for that class.
        /// </summary>
        public static float Grip(string material, string wheelClass)
        {
            Dictionary<string, float> grips;
            float grip;
            if (material != null && Load() && materials.TryGetValue(material, out grips)
                && grips.TryGetValue(wheelClass, out grip))
            {
                return grip;
            }
            return StockGrip;
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
            materials.Clear();
            surfaces.Clear();
            physicMaterials.Clear();
            partClasses.Clear();
            bodies.Clear();
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("TERRAINGRIP_MATERIAL"))
            {
                string name = node.GetValue("name");
                if (name == null)
                {
                    Log.Warning("A TERRAINGRIP_MATERIAL without a name, ignored");
                    continue;
                }
                Dictionary<string, float> grips = new Dictionary<string, float>();
                foreach (ConfigNode.Value value in node.values)
                {
                    if (value.name == "name")
                    {
                        continue;
                    }
                    float grip;
                    if (float.TryParse(value.value, NumberStyles.Float, CultureInfo.InvariantCulture, out grip)
                        && grip >= 0f)
                    {
                        grips[value.name] = grip;
                    }
                    else
                    {
                        Log.Warning($"Material {name}: grip '{value.value}' of {value.name} is not a number, ignored");
                    }
                }
                materials[name] = grips;
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
            Log.Info($"Grip table read: {materials.Count} materials, {bodies.Count} bodies, " +
                $"{surfaces.Count} surfaces, {physicMaterials.Count} physic materials, {partClasses.Count} parts");
            CheckMaterials();
            return true;
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
            HashSet<string> named = new HashSet<string>(surfaces.Values);
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
                if (!materials.ContainsKey(material))
                {
                    Log.Warning($"Material {material} is used but not defined: the stock grip for it");
                }
            }
        }
    }
}
