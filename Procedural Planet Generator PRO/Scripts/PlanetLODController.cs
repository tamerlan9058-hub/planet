namespace PlanetGeneration
{
    using UnityEngine;

    /// <summary>
    /// Per-planet LOD coordinator.
    /// Keeps an always-visible proxy in the sky and only enables the expensive
    /// terrain generator when the planet is close enough and the global budget allows it.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(110)]
    public class PlanetLODController : MonoBehaviour
    {
        public PlanetGenerator terrainGenerator;
        public PlanetRenderer planetRenderer;
        public SolarPlanet solarPlanet;
        public Camera mainCamera;
        public Transform viewer;

        [Header("Distance Multipliers")]
        [Range(6f, 80f)]
        public float farToMediumDistanceMultiplier = 36f;
        [Range(3f, 20f)]
        public float mediumToPrewarmDistanceMultiplier = 12f;
        [Range(2f, 10f)]
        public float prewarmToTerrainDistanceMultiplier = 6.25f;
        [Range(0.02f, 0.5f)]
        public float hysteresis = 0.24f;
        [Range(0.05f, 1.5f)]
        public float atmospherePriorityDistanceMultiplier = 0.12f;
        [Range(0.5f, 12f)]
        public float proxyFadeSpeed = 4.5f;

        [Header("Streaming Overrides")]
        public float forcedTerrainSeconds = 18f;

        private enum ProxyState
        {
            VeryFar,
            Medium
        }

        private ProxyState proxyState = ProxyState.VeryFar;
        private float forcedTerrainUntil = -1f;
        private float currentSurfaceDistance = float.MaxValue;
        private bool inFrustum = true;
        private bool initialized = false;

        public float CurrentSurfaceDistance => currentSurfaceDistance;
        public bool HasForcedTerrain => Time.time < forcedTerrainUntil;
        public bool IsInFrustum => inFrustum;
        public bool IsActivePlanet => solarPlanet != null && solarPlanet.IsActivePlanet;
        bool AllowsRemoteTerrain => solarPlanet == null || solarPlanet.distantRenderMode == DistantRenderMode.FullTerrain;

        public bool WantsTerrainPrewarm
        {
            get
            {
                if (terrainGenerator == null) return false;
                if (HasForcedTerrain)
                    return true;
                if (solarPlanet != null && solarPlanet.IsActivePlanet)
                    return currentSurfaceDistance <= terrainGenerator.radius * mediumToPrewarmDistanceMultiplier;
                if (!AllowsRemoteTerrain)
                    return false;

                return currentSurfaceDistance <= terrainGenerator.radius * mediumToPrewarmDistanceMultiplier;
            }
        }

        public bool WantsTerrain
        {
            get
            {
                if (terrainGenerator == null) return false;
                if (HasForcedTerrain)
                    return true;
                if (solarPlanet != null && solarPlanet.IsActivePlanet)
                    return currentSurfaceDistance <= terrainGenerator.radius * prewarmToTerrainDistanceMultiplier;
                if (!AllowsRemoteTerrain)
                    return false;

                return currentSurfaceDistance <= terrainGenerator.radius * prewarmToTerrainDistanceMultiplier;
            }
        }

        void Awake()
        {
            InitializeIfNeeded();
        }

        void Start()
        {
            InitializeIfNeeded();
        }

        void Update()
        {
            InitializeIfNeeded();
            if (ChunkManager.Instance == null)
                RefreshViewState();
        }

        public void ForceTerrainStreaming(float duration = -1f)
        {
            InitializeIfNeeded();
            forcedTerrainUntil = Time.time + (duration > 0f ? duration : forcedTerrainSeconds);

            if (terrainGenerator != null)
            {
                terrainGenerator.ActivateStreaming(ChunkManager.Instance != null ? ChunkManager.Instance.maxConcurrentChunkBuildsPerPlanet : 4);
                terrainGenerator.SetFastGeneration(true);
            }
        }

        internal void ApplyStreamingGrant(bool terrainGrant, bool prewarmGrant, int buildBudget)
        {
            InitializeIfNeeded();
            RefreshViewState();

            if (terrainGenerator == null || planetRenderer == null)
                return;

            float localSurfaceCoverage = terrainGenerator.GetLocalSurfaceCoverage01();

            if (prewarmGrant)
            {
                terrainGenerator.ActivateStreaming(Mathf.Max(1, buildBudget));
                bool atmospherePriority = terrainGrant &&
                    currentSurfaceDistance <= terrainGenerator.radius * atmospherePriorityDistanceMultiplier;
                terrainGenerator.SetFastGeneration(atmospherePriority || HasForcedTerrain);
            }
            else
            {
                terrainGenerator.SetFastGeneration(false);
                terrainGenerator.DeactivateStreaming();
            }

            bool activePlanetApproach = terrainGrant && solarPlanet != null && solarPlanet.IsActivePlanet;
            float terrainVisibleCoverage = activePlanetApproach ? 0.92f : 0.48f;
            bool terrainVisible = terrainGrant && (terrainGenerator.IsReady || localSurfaceCoverage >= terrainVisibleCoverage);

            if (terrainVisible)
            {
                terrainGenerator.SetFastGeneration(false);
                if (activePlanetApproach)
                {
                    planetRenderer.SetLodVisibility(false, true, 0f, 1f, proxyFadeSpeed);
                    return;
                }

                planetRenderer.SetLodVisibility(false, true, 0f, 0f, proxyFadeSpeed);
                return;
            }

            bool showFar = inFrustum && proxyState == ProxyState.VeryFar;
            bool showMedium = inFrustum && proxyState == ProxyState.Medium;

            if (prewarmGrant || terrainGrant)
            {
                if (activePlanetApproach)
                {
                    planetRenderer.SetLodVisibility(false, true, 0f, 1f, proxyFadeSpeed);
                    return;
                }

                float fadeStart = activePlanetApproach ? 0.72f : 0.08f;
                float fadeEnd = activePlanetApproach ? 0.98f : 0.42f;
                float proxyOpacity = 1f - Mathf.SmoothStep(fadeStart, fadeEnd, localSurfaceCoverage);
                if (activePlanetApproach && localSurfaceCoverage > 0.01f)
                {
                    float distanceOpacity = Mathf.InverseLerp(
                        terrainGenerator.radius * 0.08f,
                        terrainGenerator.radius * 0.42f,
                        currentSurfaceDistance);
                    proxyOpacity = Mathf.Max(proxyOpacity, distanceOpacity);
                }
                showFar = false;
                showMedium = inFrustum && proxyOpacity > 0.02f;
                planetRenderer.SetLodVisibility(
                    false,
                    showMedium,
                    0f,
                    showMedium ? proxyOpacity : 0f,
                    proxyFadeSpeed);
                return;
            }

            planetRenderer.SetLodVisibility(
                showFar,
                showMedium,
                showFar ? 1f : 0f,
                showMedium ? 1f : 0f,
                proxyFadeSpeed);
        }

        void InitializeIfNeeded()
        {
            if (initialized)
                return;

            if (terrainGenerator == null)
                terrainGenerator = GetComponent<PlanetGenerator>();
            if (terrainGenerator == null)
                return;

            if (mainCamera == null)
                mainCamera = terrainGenerator.mainCamera != null ? terrainGenerator.mainCamera : Camera.main;
            if (viewer == null)
                viewer = terrainGenerator.player != null ? terrainGenerator.player : (mainCamera != null ? mainCamera.transform : null);

            if (planetRenderer == null)
                planetRenderer = GetComponent<PlanetRenderer>();
            if (planetRenderer == null)
                planetRenderer = gameObject.AddComponent<PlanetRenderer>();
            if (solarPlanet == null)
                solarPlanet = GetComponent<SolarPlanet>();

            planetRenderer.Initialize(terrainGenerator, mainCamera);
            if (solarPlanet != null)
                solarPlanet.ApplySettings();
            terrainGenerator.autoStartStreaming = false;
            terrainGenerator.DeactivateStreaming();
            ChunkManager.EnsureExists(transform.root).Register(this);
            RefreshViewState();

            initialized = true;
        }

        void RefreshViewState()
        {
            if (terrainGenerator == null)
                return;

            if (mainCamera == null)
                mainCamera = terrainGenerator.mainCamera != null ? terrainGenerator.mainCamera : Camera.main;
            if (viewer == null)
                viewer = terrainGenerator.player != null ? terrainGenerator.player : (mainCamera != null ? mainCamera.transform : null);

            planetRenderer.SyncRuntimeProperties();

            if (viewer == null)
            {
                currentSurfaceDistance = float.MaxValue;
                inFrustum = true;
                return;
            }

            float centerDistance = Vector3.Distance(viewer.position, transform.position);
            currentSurfaceDistance = Mathf.Max(0f, centerDistance - terrainGenerator.radius);

            if (mainCamera != null)
            {
                Plane[] planes = GeometryUtility.CalculateFrustumPlanes(mainCamera);
                inFrustum = GeometryUtility.TestPlanesAABB(planes, planetRenderer.GetWorldBounds());
            }
            else
            {
                inFrustum = true;
            }

            UpdateProxyState();
        }

        void UpdateProxyState()
        {
            if (terrainGenerator == null)
                return;

            float mediumEnter = terrainGenerator.radius * farToMediumDistanceMultiplier;
            float mediumExit = mediumEnter * (1f + hysteresis);

            if (proxyState == ProxyState.VeryFar)
            {
                if (currentSurfaceDistance <= mediumEnter)
                    proxyState = ProxyState.Medium;
            }
            else
            {
                if (currentSurfaceDistance >= mediumExit)
                    proxyState = ProxyState.VeryFar;
            }
        }

        void OnDestroy()
        {
            if (ChunkManager.Instance != null)
                ChunkManager.Instance.Unregister(this);
        }
    }
}
