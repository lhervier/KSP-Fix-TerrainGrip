using System.Collections.Generic;
using UnityEngine;
using VehiclePhysics;

namespace com.github.lhervier.ksp.fix.terraingrip
{
    /// <summary>
    /// The ground material manager of the wheels' physics (VPP), which the game never installs: each wheel asks it, at
    /// every physics step on the ground, what it stands on, and multiplies its whole tyre curve by the grip of the
    /// answer. The grip comes from <see cref="GripTable"/>: the material of what the wheel touches (the biome of the
    /// terrain, a static, a part...) and the class of the wheel (a tyre, the metal foot of a landing leg). On a body
    /// the table does not name, and on what it cannot tell, a wheel keeps the grip it has in the game.
    /// </summary>
    internal class TerrainGripManager : GroundMaterialManagerBase
    {
        /// <summary>Whether the fix is installed (terrainGrip in the settings).</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>How far a wheel's contact moves before its ground is looked up again: a quad covers several biomes.</summary>
        private const float LookupDistance = 5f;

        /// <summary>The material answered for each grip, shared by every wheel with that grip.</summary>
        private static readonly Dictionary<float, GroundMaterial> byGrip = new Dictionary<float, GroundMaterial>();

        /// <summary>
        /// Puts the manager in a game object kept across scenes. Each wheel looks for one once, when it is enabled
        /// (<c>VehicleBase.OnEnable</c>, by <c>FindObjectOfType</c>): it must exist before the first vessel loads.
        /// </summary>
        public static void Install()
        {
            GameObject host = new GameObject("KSPFixTerrainGrip");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<TerrainGripManager>();
        }

        private void Awake()
        {
            // An instance method: the game's events read the target of the delegate, which a static method has none of.
            GameEvents.OnGameDatabaseLoaded.Add(OnGameDatabaseLoaded);
        }

        private void OnDestroy()
        {
            GameEvents.OnGameDatabaseLoaded.Remove(OnGameDatabaseLoaded);
        }

        /// <summary>The database was loaded again (Reload Database): the table is read again when next needed.</summary>
        private void OnGameDatabaseLoaded()
        {
            GripTable.Invalidate();
        }

        public override GroundMaterial GetGroundMaterial(VehicleBase vehicle, GroundMaterialHit groundHit)
        {
            string what;
            float grip = Lookup(vehicle, groundHit, out what);
            if (Log.IsDebugEnabled)
            {
                Log.Debug($"{what}: grip {grip}");
            }
            return MaterialFor(grip);
        }

        /// <summary>
        /// Gives <paramref name="groundMaterial"/> the material of the ground <paramref name="groundHit"/> touches,
        /// looked up again only when the wheel touches another collider, or has moved <see cref="LookupDistance"/> on
        /// it. The game's version compares the colliders' physic material instead, which every terrain quad of a body
        /// shares (and the bodies of planet packs share Kerbin's): a wheel driving from one biome or body to another
        /// would keep its first answer.
        /// </summary>
        public override void GetGroundMaterialCached(VehicleBase vehicle, GroundMaterialHit groundHit,
            ref GroundMaterialHit cachedGroundHit, ref GroundMaterial groundMaterial)
        {
            if (groundMaterial != null && groundHit.collider == cachedGroundHit.collider
                && (groundHit.hitPoint - cachedGroundHit.hitPoint).sqrMagnitude < LookupDistance * LookupDistance)
            {
                return;
            }
            GroundMaterial previous = groundMaterial;
            bool sameCollider = groundHit.collider == cachedGroundHit.collider;
            cachedGroundHit = groundHit;
            string what;
            float grip = Lookup(vehicle, groundHit, out what);
            groundMaterial = MaterialFor(grip);
            if (Log.IsDebugEnabled && (groundMaterial != previous || !sameCollider))
            {
                Log.Debug($"{what}: grip {grip}");
            }
        }

        /// <summary>The material of grip <paramref name="grip"/>, made once.</summary>
        private static GroundMaterial MaterialFor(float grip)
        {
            GroundMaterial material;
            if (!byGrip.TryGetValue(grip, out material))
            {
                material = new GroundMaterial { grip = grip, drag = 0f, surfaceType = GroundMaterial.SurfaceType.Hard };
                byGrip[grip] = material;
            }
            return material;
        }

        /// <summary>
        /// The grip of the wheel of <paramref name="vehicle"/> on what <paramref name="groundHit"/> touches, and what
        /// it is, for the log. The stock grip on a body the table does not name, and on what it cannot tell.
        /// </summary>
        private static float Lookup(VehicleBase vehicle, GroundMaterialHit groundHit, out string what)
        {
            Part wheelPart = vehicle.GetComponentInParent<Part>();
            string wheel = wheelPart != null ? $"{wheelPart.partInfo.title} ({wheelPart.persistentId})" : vehicle.name;
            CelestialBody body = wheelPart != null && wheelPart.vessel != null ? wheelPart.vessel.mainBody : null;
            if (!GripTable.HasBody(body))
            {
                what = $"{wheel} on {(body != null ? body.bodyName : "no body")}, a body the table does not name";
                return GripTable.StockGrip;
            }
            string wheelClass = WheelClass(wheelPart);
            string surface;
            string material = GroundMaterialName(groundHit, body, out surface);
            what = $"{wheel}, a {wheelClass}, on {surface}: {material ?? "unknown material"}";
            return material != null ? GripTable.Grip(material, wheelClass) : GripTable.StockGrip;
        }

        /// <summary>
        /// The class of a wheel's part: the one a TERRAINGRIP_PART node gives it, else "tyre" for a wheel whose friction
        /// the player can set (rover wheels, landing gear), "metal" for one whose friction is fixed (landing legs).
        /// </summary>
        private static string WheelClass(Part part)
        {
            if (part == null)
            {
                return "tyre";
            }
            string wheelClass = GripTable.PartClass(part.partInfo.name);
            if (wheelClass != null)
            {
                return wheelClass;
            }
            ModuleWheelBase wheel = part.FindModuleImplementing<ModuleWheelBase>();
            return wheel != null && !wheel.autoFrictionAvailable ? "metal" : "tyre";
        }

        /// <summary>
        /// The material of what <paramref name="groundHit"/> touches, or null when the table does not say;
        /// <paramref name="surface"/> says what it is, for the log.
        /// </summary>
        private static string GroundMaterialName(GroundMaterialHit groundHit, CelestialBody wheelBody, out string surface)
        {
            Collider collider = groundHit.collider;
            if (collider == null)
            {
                surface = "nothing";
                return null;
            }
            PQ quad = collider.GetComponent<PQ>();
            if (quad != null)
            {
                CelestialBody body = FlightGlobals.GetBodyByName(quad.sphereRoot.name) ?? wheelBody;
                Vector3d point = groundHit.hitPoint;
                string biome = ScienceUtil.GetExperimentBiome(body, body.GetLatitude(point), body.GetLongitude(point));
                surface = $"the terrain of {body.bodyName}, biome {biome}";
                return GripTable.BiomeMaterial(body, biome);
            }
            Part part = collider.GetComponentInParent<Part>();
            if (part != null)
            {
                if (part.FindModuleImplementing<ModuleAsteroid>() != null)
                {
                    surface = $"the asteroid {part.vessel?.vesselName}";
                    return GripTable.SurfaceMaterial("asteroid");
                }
                if (part.FindModuleImplementing<ModuleComet>() != null)
                {
                    surface = $"the comet {part.vessel?.vesselName}";
                    return GripTable.SurfaceMaterial("comet");
                }
                ModulePhysicMaterial physic = part.FindModuleImplementing<ModulePhysicMaterial>();
                string picked = physic != null ? GripTable.PhysicMaterialMaterial(physic.activePhysicMaterialName) : null;
                if (picked != null)
                {
                    surface = $"the part {part.partInfo.title}, {physic.activePhysicMaterialName}";
                    return picked;
                }
                surface = $"the part {part.partInfo.title}";
                return GripTable.SurfaceMaterial("part");
            }
            if (collider.GetComponentInParent<ROC>() != null)
            {
                surface = $"the surface feature {collider.transform.root.name}";
                return GripTable.SurfaceMaterial("rock");
            }
            if (collider.GetComponentInParent<PQSMod_LandClassScatterQuad>() != null)
            {
                surface = $"a rock of the scatter ({collider.name})";
                return GripTable.SurfaceMaterial("rock");
            }
            PQSSurfaceObject building = collider.GetComponentInParent<PQSSurfaceObject>();
            if (building != null)
            {
                surface = $"the static {building.name} ({collider.name})";
                return GripTable.SurfaceMaterial("static");
            }
            surface = $"{collider.name}, under {collider.transform.root.name}";
            return null;
        }
    }
}
