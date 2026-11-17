using System.Collections.Generic;
using UnityEngine;

namespace com.github.lhervier.ksp.fix.terraingrip
{
    /// <summary>
    /// Puts what touches the terrain in the same combine mode as the terrain, by multiplication, so that the friction of
    /// a contact is the product of the two: the parts whose colliders have no physic material (they get the game's
    /// default, "Metal", 0.25 combined by its maximum, against which the terrain could only raise friction) get the
    /// fix's metal, 0.5, so that two parts still rub at 0.25; the physic materials of the grip pads keep their values
    /// and multiply too; the statics without a physic material (the buildings of the space centre) get concrete. The
    /// physic materials other mods give their parts, and those of the kerbals, are left as they are.
    /// </summary>
    internal class ContactMaterials : MonoBehaviour
    {
        /// <summary>Whether the fix is installed: it goes with the terrain friction (terrainFriction in the settings).</summary>
        public static bool Enabled => TerrainFriction.Enabled;

        private static readonly List<Collider> colliders = new List<Collider>();

        /// <summary>Puts the component in a game object kept across scenes.</summary>
        public static void Install()
        {
            GameObject host = new GameObject("KSPFixTerrainGrip.ContactMaterials");
            DontDestroyOnLoad(host);
            host.AddComponent<ContactMaterials>();
        }

        private void Awake()
        {
            GameEvents.onPartUnpack.Add(OnPartUnpack);
            GameEvents.onVesselWasModified.Add(OnVesselWasModified);
            GameEvents.onFlightReady.Add(OnFlightReady);
            GameEvents.OnGameDatabaseLoaded.Add(OnGameDatabaseLoaded);
            MultiplyGripPads();
        }

        private void OnDestroy()
        {
            GameEvents.onPartUnpack.Remove(OnPartUnpack);
            GameEvents.onVesselWasModified.Remove(OnVesselWasModified);
            GameEvents.onFlightReady.Remove(OnFlightReady);
            GameEvents.OnGameDatabaseLoaded.Remove(OnGameDatabaseLoaded);
        }

        private void OnPartUnpack(Part part)
        {
            SetPartMaterial(part);
        }

        private void OnVesselWasModified(Vessel vessel)
        {
            if (vessel == null || vessel.packed)
            {
                return;
            }
            foreach (Part part in vessel.parts)
            {
                SetPartMaterial(part);
            }
        }

        private void OnFlightReady()
        {
            MultiplyGripPads();
            SetStaticMaterials();
        }

        /// <summary>The database was loaded again (Reload Database): the grip pads' materials are made anew.</summary>
        private void OnGameDatabaseLoaded()
        {
            MultiplyGripPads();
        }

        /// <summary>
        /// Gives the fix's metal to every collider of a part that has no physic material of its own, but its wheels'
        /// (the wheel physics reads the ground its own way) and its triggers.
        /// </summary>
        private static void SetPartMaterial(Part part)
        {
            PhysicMaterial metal = GripTable.PartMaterial();
            if (part == null || metal == null)
            {
                return;
            }
            colliders.Clear();
            part.GetComponentsInChildren(true, colliders);
            int set = 0;
            foreach (Collider collider in colliders)
            {
                if (collider.sharedMaterial == null && !collider.isTrigger && !(collider is WheelCollider))
                {
                    collider.sharedMaterial = metal;
                    set++;
                }
            }
            if (set > 0 && Log.IsDebugEnabled)
            {
                Log.Debug($"{part.partInfo.title} ({part.persistentId}): metal on {set} collider(s)");
            }
        }

        /// <summary>
        /// Makes the physic materials of the library (the grip pads of Breaking Ground) combine by multiplication, their
        /// values kept: the pads apply them to their colliders when they start.
        /// </summary>
        private static void MultiplyGripPads()
        {
            if (PhysicMaterialLibrary.Instance == null || PhysicMaterialLibrary.Instance.physicMaterials == null)
            {
                return;
            }
            foreach (PhysicMaterialDefinition definition in PhysicMaterialLibrary.Instance.physicMaterials.Values)
            {
                if (definition.Material != null)
                {
                    definition.Material.frictionCombine = PhysicMaterialCombine.Multiply;
                }
            }
            if (PhysicMaterialLibrary.Instance.DefaultMaterial?.Material != null)
            {
                PhysicMaterialLibrary.Instance.DefaultMaterial.Material.frictionCombine = PhysicMaterialCombine.Multiply;
            }
        }

        /// <summary>
        /// Gives concrete to every collider of the statics of the bodies of the grip table (the buildings of the space
        /// centre, launch sites) that has no physic material of its own, inactive ones included.
        /// </summary>
        private static void SetStaticMaterials()
        {
            PhysicMaterial concrete = GripTable.TerrainMaterial(GripTable.SurfaceMaterial("static"));
            if (concrete == null)
            {
                return;
            }
            int set = 0;
            foreach (PQSSurfaceObject building in Resources.FindObjectsOfTypeAll<PQSSurfaceObject>())
            {
                if (!building.gameObject.scene.IsValid() || building.sphere == null
                    || !GripTable.HasBody(FlightGlobals.GetBodyByName(building.sphere.name)))
                {
                    continue;
                }
                colliders.Clear();
                building.GetComponentsInChildren(true, colliders);
                foreach (Collider collider in colliders)
                {
                    if (collider.sharedMaterial == null && !collider.isTrigger)
                    {
                        collider.sharedMaterial = concrete;
                        set++;
                    }
                }
            }
            if (set > 0)
            {
                Log.Debug($"Concrete on {set} collider(s) of statics");
            }
        }
    }
}
