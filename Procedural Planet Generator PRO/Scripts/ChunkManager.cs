namespace PlanetGeneration
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Global terrain budget broker.
    /// Naive planet LOD systems freeze because every nearby planet starts CPU mesh work
    /// on the same frame, pushes too many mesh uploads to the main thread, and has no
    /// prioritization. This manager keeps only the closest planets in close-terrain mode
    /// and constrains their build budget.
    /// </summary>
    [DefaultExecutionOrder(120)]
    public class ChunkManager : MonoBehaviour
    {
        public static ChunkManager Instance { get; private set; }

        [Header("Global Terrain Budget")]
        [Range(1, 3)]
        public int maxActiveTerrainPlanets = 2;
        [Range(1, 4)]
        public int maxPrewarmTerrainPlanets = 3;
        [Range(1, 8)]
        public int maxConcurrentChunkBuildsPerPlanet = 2;

        private readonly List<PlanetLODController> controllers = new List<PlanetLODController>(16);

        public static ChunkManager EnsureExists(Transform parent = null)
        {
            if (Instance != null)
                return Instance;

            var go = new GameObject("ChunkManager");
            if (parent != null)
                go.transform.SetParent(parent, false);

            return go.AddComponent<ChunkManager>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        public void Register(PlanetLODController controller)
        {
            if (controller == null || controllers.Contains(controller))
                return;

            controllers.Add(controller);
        }

        public void Unregister(PlanetLODController controller)
        {
            controllers.Remove(controller);
        }

        void LateUpdate()
        {
            controllers.RemoveAll(controller => controller == null);
            if (controllers.Count == 0)
                return;

            controllers.Sort(CompareControllers);

            int grantedTerrain = 0;
            int grantedPrewarm = 0;

            for (int i = 0; i < controllers.Count; i++)
            {
                PlanetLODController controller = controllers[i];
                if (controller == null || !controller.isActiveAndEnabled)
                    continue;

                bool wantsTerrain = controller.WantsTerrain;
                bool wantsPrewarm = controller.WantsTerrainPrewarm;

                bool terrainGrant = wantsTerrain && grantedTerrain < Mathf.Max(1, maxActiveTerrainPlanets);
                if (terrainGrant)
                    grantedTerrain++;

                bool prewarmGrant = (terrainGrant || wantsPrewarm) && grantedPrewarm < Mathf.Max(maxActiveTerrainPlanets, maxPrewarmTerrainPlanets);
                if (prewarmGrant)
                    grantedPrewarm++;

                int buildBudget = 0;
                if (terrainGrant)
                    buildBudget = Mathf.Max(1, maxConcurrentChunkBuildsPerPlanet);
                else if (prewarmGrant)
                    buildBudget = 1;

                controller.ApplyStreamingGrant(terrainGrant, prewarmGrant, buildBudget);
            }
        }

        static int CompareControllers(PlanetLODController a, PlanetLODController b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;

            if (a.HasForcedTerrain != b.HasForcedTerrain)
                return a.HasForcedTerrain ? -1 : 1;

            if (a.IsActivePlanet != b.IsActivePlanet)
                return a.IsActivePlanet ? -1 : 1;

            if (a.IsInFrustum != b.IsInFrustum)
                return a.IsInFrustum ? -1 : 1;

            if (a.WantsTerrain != b.WantsTerrain)
                return a.WantsTerrain ? -1 : 1;

            return a.CurrentSurfaceDistance.CompareTo(b.CurrentSurfaceDistance);
        }
    }
}
