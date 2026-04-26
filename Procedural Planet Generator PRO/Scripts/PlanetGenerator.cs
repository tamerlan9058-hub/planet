namespace PlanetGeneration
{
    using UnityEngine;
    using System.Collections;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using UnityEngine.Rendering;

    /// <summary>
    /// NMS-style Planet Generator — Uber Noise (GDC 2017, Sean Murray)
    /// Главный файл: поля, настройки, Public API, Unity lifecycle, материалы, океан.
    /// Остальные части: .Chunks, .Terrain, .Noise, .Normals, .Biomes, .Trees
    /// </summary>
    public partial class PlanetGenerator : MonoBehaviour
    {
        public enum TerrainStyle
        {
            SebInspired = 0,
            HybridNms   = 1,
        }

        [System.Serializable]
        public class PlanetSettings
        {
            [Min(100f)] public float radius = 5000f;
            [Range(0.01f, 0.5f)]  public float maxHeightRatio  = 0.16f;
            [Range(0f, 1f)]       public float seaLevelRatio   = 0.02f;
            [Range(0.001f, 0.05f)]public float beachWidthRatio = 0.01f;
        }

        [System.Serializable]
        public class NoiseSettings
        {
            [Header("Base Terrain")]
            public float baseScale = 0.5f;
            [Range(3, 9)]         public int   baseOctaves         = 5;
            [Range(0.3f, 0.7f)]   public float basePersistence     = 0.5f;
            [Range(1.8f, 2.6f)]   public float baseLacunarity      = 2.05f;
            [Range(0f, 1f)]       public float continentThreshold  = 0.45f;

            [Header("Mountain Layer")]
            public float mountainFrequency = 1.9f;
            [Range(2, 8)]         public int   mountainOctaves        = 5;
            [Range(0.3f, 0.7f)]   public float mountainPersistence    = 0.5f;
            [Range(1.8f, 2.8f)]   public float mountainLacunarity     = 2.2f;
            [Range(0f, 2f)]       public float mountainStrength       = 0.75f;
            [Range(0f, 1f)]       public float mountainMaskThreshold  = 0.6f;
            [Range(0f, 2f)]       public float ridgeSharpness         = 1.1f;

            [Header("Detail Layer")]
            public float detailScale = 14f;
            [Range(2, 6)]         public int   detailOctaves    = 3;
            [Range(0.2f, 0.7f)]   public float detailPersistence= 0.45f;
            [Range(1.8f, 3f)]     public float detailLacunarity = 2.4f;
            [Range(0f, 0.2f)]     public float detailStrength   = 0.06f;

            [Header("Mega Mountains")]
            public float megaMaskScale = 0.38f;
            [Range(0f, 1f)] public float megaMaskThreshold = 0.82f;
            [Range(0f, 2f)] public float megaStrength      = 1.15f;

            [Header("Cliffs / Spikes")]
            public float cliffScale = 2.8f;
            [Range(0f, 1f)] public float cliffThreshold = 0.62f;
            [Range(0f, 3f)] public float cliffStrength  = 0.45f;
            [Range(0f, 2f)] public float peakSharpness  = 0.45f;
        }

        // ── Inspector fields ──────────────────────────────────────────────────

        [Header("Planet")]
        public float    radius = 5000f;
        public Material planetMaterial;
        public PlanetSettings planetSettings = new PlanetSettings();
        public NoiseSettings  noiseSettings  = new NoiseSettings();

        [Header("Terrain Style")]
        [Tooltip("SebInspired gives broader continents and cleaner ridge chains. HybridNms keeps the previous terrain stack.")]
        public TerrainStyle terrainStyle = TerrainStyle.SebInspired;

        [Header("Seb Continental Plates")]
        [Range(8, 64)]   public int   sebPlateCount              = 23;
        [Range(0.05f, 0.95f)] public float sebLandRatio          = 0.42f;
        [Range(0f, 0.45f)]    public float sebPlateJitter        = 0.18f;
        [Range(0f, 1f)]       public float sebPlateBlend         = 0.58f;
        [Range(0.5f, 3f)]     public float sebPlateMacroScale    = 1.3f;
        [Range(0.5f, 5f)]     public float sebCoastNoiseScale    = 2.2f;
        [Range(0f, 0.5f)]     public float sebCoastNoiseStrength = 0.15f;
        [Range(0f, 2f)]       public float sebBoundaryMountainStrength = 0.9f;

        [Header("Ocean")]
        public bool     generateOcean   = false;
        public Material oceanMaterial;
        public float    oceanLevel      = 35f;
        [Range(0.5f, 1.05f)]  public float seaLevelScale          = 0.68f;
        [Range(0.85f, 1.05f)] public float oceanSurfaceRadiusScale = 0.97f;
        [Range(4, 128)]       public int   oceanResolution         = 24;

        [Header("Chunks and LOD")]
        [Range(2, 256)] public int maxVertsPerChunk = 64;
        [Range(1, 64)]  public int chunksPerFace    = 8;
        [Range(1, 4)]   public int lodLevels        = 3;
        [HideInInspector] public bool           activeLodSystem      = true;
        [HideInInspector] public float          lod0RadiusInChunks   = 2f;
        [HideInInspector] public AnimationCurve lodTransitionCurve   = null;

        [Header("NMS Terrain Scale")]
        public float heightMultiplier  = 700f;
        public float continentScale    = 0.18f;
        [Range(0f, 1f)] public float continentThreshold = 0.44f;
        public float oceanDepth = -180f;

        [Header("NMS Base Terrain (Exponential FBM)")]
        public float baseScale    = 0.55f;
        [Range(4, 10)]      public int   baseOctaves    = 5;
        [Range(0.3f, 0.7f)] public float basePersistence = 0.40f;
        public float baseLacunarity = 2.1f;
        [Range(1f, 3f)] public float octaveExponent = 1.6f;

        [Header("NMS Ridge Mountains")]
        public float ridgeScale = 1.4f;
        [Range(2, 8)]  public int   ridgeOctaves    = 5;
        [Range(0f, 1f)]public float ridgePersistence= 0.46f;
        [Range(0f, 2f)]public float ridgeStrength   = 1.45f;
        [Range(0f, 1f)]public float ridgeSharpness  = 0.92f;
        [Range(0f, 2f)]public float ridgeGain       = 0.9f;

        [Header("NMS Mesa / Plateau (NMS-стиль)")]
        public float mesaScale = 0.45f;
        [Range(1f, 20f)] public float mesaSharpness = 10f;
        [Range(0f, 1f)]  public float mesaStrength  = 0.28f;
        [Range(0f, 1f)]  public float mesaFrequency = 0.38f;

        [Header("NMS Canyon Layer")]
        public bool  enableCanyons   = true;
        public float canyonScale     = 2.2f;
        [Range(0f, 1f)] public float canyonStrength  = 0.35f;
        [Range(0f, 1f)] public float canyonFrequency = 0.4f;

        [Header("NMS Mega Structures")]
        public float megaScale = 0.25f;
        [Range(0f, 1f)] public float megaRarity   = 0.80f;
        [Range(0f, 3f)] public float megaStrength  = 0.8f;

        [Header("NMS Slope Erosion")]
        [Range(0f, 4f)] public float slopeErosion    = 1.4f;
        [Range(0f, 1f)] public float altitudeDamping = 0.55f;

        [Header("NMS Domain Warp")]
        public float warpScale = 0.7f;
        [Range(0.0001f, 0.01f)] public float warpFrequency       = 0.0016f;
        [Range(0.0001f, 0.01f)] public float warp2Frequency      = 0.0011f;
        [Range(0.001f, 0.08f)]  public float warpAmplitudeRatio  = 0.008f;
        [Range(0f, 1f)]         public float warpStrength        = 0.18f;
        [Range(0f, 1f)]         public float warp2Strength       = 0.08f;

        [Header("NMS Rivers (шумовые реки)")]
        public bool  enableRivers    = true;
        [Range(0.001f, 0.01f)]  public float riverScale        = 0.0035f;
        [Range(0f, 1f)]         public float riverStrength     = 0.55f;
        [Range(0.05f, 0.45f)]   public float riverNarrow       = 0.18f;
        [Range(10f, 200f)]      public float riverMaxElevation = 80f;
        [Range(0f, 1f)]         public float riverBranchMix    = 0.35f;

        [Header("NMS Detail Layer")]
        public float detailScale    = 28f;
        [Range(0f, 0.3f)] public float detailStrength    = 0.04f;
        [Range(0f, 1f)]   public float detailSlopeBoost  = 0.8f;

        [Header("NMS Micro Noise (мелкий рельеф)")]
        public float microScale    = 80f;
        [Range(0f, 0.15f)] public float microStrength = 0.008f;

        [Header("AAA Terrain Pipeline")]
        [Range(2, 6)]         public int   geometryOctaves        = 4;
        [Range(0.2f, 0.7f)]   public float geometryAmplitudeDecay = 0.42f;
        [Range(1.7f, 2.2f)]   public float geometryLacunarity     = 1.94f;
        [Range(0f, 1f)]       public float highFrequencyDamping   = 0.46f;
        [Range(10f, 55f)]     public float maxSlopeDegrees        = 42f;
        [Range(0, 4)]         public int   slopeLimitPasses       = 1;
        [Range(0, 4)]         public int   erosionPasses          = 1;
        [Range(0f, 1f)]       public float erosionStrength        = 0.18f;

        [Header("NMS Biome Mask (без полос!)")]
        [Range(0f, 5f)] public float biomeContrast    = 4.0f;
        public float biomeNoiseScale = 0.22f;

        [Header("Noise Seed")]
        public int seed = 42;

        [Header("Biome Colors (шумовые, не широтные)")]
        public Color deepWaterColor    = new Color(0.05f, 0.18f, 0.38f);
        public Color shallowWaterColor = new Color(0.15f, 0.38f, 0.60f);
        public Color sandColor         = new Color(0.80f, 0.72f, 0.52f);
        public Color grassColor        = new Color(0.22f, 0.50f, 0.18f);
        public Color forestColor       = new Color(0.10f, 0.32f, 0.12f);
        public Color rockColor         = new Color(0.42f, 0.40f, 0.38f);
        public Color mesaRockColor     = new Color(0.55f, 0.38f, 0.22f);
        public Color snowColor         = new Color(0.92f, 0.94f, 1.00f);
        public Color atmosphereColor   = new Color(0.48f, 0.70f, 1.00f, 0.46f);
        public Color cloudColor        = Color.white;
        public Color cloudShadowColor  = new Color(0.68f, 0.74f, 0.82f, 1f);
        [Range(0f, 1f)] public float rockDarkness = 0.72f;

        [Header("Snow")]
        public bool  enableSnow      = false;
        public float snowHeightStart = 180f;
        [Range(0f, 1f)] public float polarNoiseBlend = 0.6f;

        [Header("LOD Distances")]
        public float lodDistance0   = 1500f;
        public float lodDistance1   = 4000f;
        public float lodDistance2   = 9000f;
        public float lodDistance3   = 18000f;
        public float unloadDistance = 22000f;

        [Header("Camera")]
        public Camera    mainCamera;
        public Transform player;

        [Header("Streaming")]
        public bool autoStartStreaming = true;

        [Header("Performance")]
        public int   maxConcurrentGenerations = 4;
        public float updateInterval           = 0.2f;
        public bool  useColliders             = true;
        public float colliderDistance         = 3000f;

        [Header("Visual")]
        [Range(0f, 2f)] public float slopeRockBlend    = 0.34f;
        [Range(0f, 1f)] public float terrainSmoothing  = 0.22f;
        [Range(0f, 1f)] public float ambientOcclusion  = 0.45f;
        public float normalSampleDistance = 1.5f;

        [Header("Surface Detail")]
        public Texture2D terrainNormalMap;
        public string terrainNormalResourcePath       = "PlanetSurfaceNormal";
        [Range(1f, 128f)]  public float terrainNormalTiling          = 48f;
        [Range(0f, 2f)]    public float terrainNormalStrength        = 0.92f;
        [Range(1f, 256f)]  public float terrainMicroNormalTiling     = 164f;
        [Range(0f, 2f)]    public float terrainMicroNormalStrength   = 0.28f;
        [Range(0f, 2f)]    public float terrainCavityStrength        = 0.82f;
        [Range(0f, 1f)]    public float cloudCoverage                = 0.58f;
        [Range(0f, 1f)]    public float cloudDensity                 = 0.72f;

        [Header("Trees")]
        public GameObject treePrefab;
        [Range(0f, 0.1f)] public float treeProbability  = 0.025f;
        [Range(0f, 1f)]   public float maxTreeSlope      = 0.25f;
        public float treeSurfaceOffset = 0.15f;
        public float forestNoiseScale  = 0.0019f;
        [Range(0f, 1f)]   public float forestCoverage      = 0.58f;
        [Range(0f, 1f)]   public float forestDensityBoost  = 0.85f;
        [Range(10, 240)]  public int   maxTreesPerChunk     = 110;
        public Shader treeShader;

        [Header("UI")]
        public GameObject waterPanel;
        public GameObject loadingPanel;
        public GameObject extraObjectToHide;

        [Header("Scalable Objects")]
        public GameObject firstObject;
        public GameObject secondObject;

        [Header("Custom Spawn")]
        public bool       enableSurfaceSpawn = false;
        public GameObject spawnPrefab;

        // ── Internal state ────────────────────────────────────────────────────
        // (shared across all partial files)
        private Dictionary<Vector3Int, ChunkData> chunks       = new Dictionary<Vector3Int, ChunkData>();
        private List<ChunkData>                   generateQueue = new List<ChunkData>();
        private Vector3    seedOffset;
        private GameObject oceanObject;
        private int  activeGenerations      = 0;
        private bool initialLoadComplete    = false;
        private bool hasStartedLoading      = false;
        private readonly Dictionary<Vector3Int, float> heightCache     = new Dictionary<Vector3Int, float>(16384);
        private readonly object heightCacheLock  = new object();
        private readonly object sebPlateDataLock = new object();
        private const float HeightCacheQuant = 10000f;
        private float normalizedMaxHeight    = 700f;
        private float seaLevelHeight         = 0f;
        private float beachWidthHeight       = 20f;
        private float observedMinHeight      = float.MaxValue;
        private float observedMaxHeight      = float.MinValue;
        private float lastHeightLogTime      = -999f;
        private float lastSlopeLogTime       = -999f;
        private bool  streamingActive        = false;
        private bool  runtimeResourcesReady  = false;
        private bool  chunkStructureInitialized = false;
        private bool  visualProfileApplied   = false;
        private int   defaultConcurrentGenerations = 4;
        private int   streamingVersion       = 0;
        private float nextQueueSortTime      = -999f;
        private Coroutine updateChunksCoroutine;
        private Coroutine generateChunksCoroutine;
        private bool      sebPlateDataReady  = false;
        private Vector3[] sebPlateCenters;
        private float[]   sebPlateMacroValues;

        private enum Face { Up, Down, Left, Right, Front, Back }

        // ── Public API ────────────────────────────────────────────────────────

        public bool IsReady
        {
            get
            {
                if (!streamingActive) return false;
                if (initialLoadComplete) return true;
                if (!hasStartedLoading) return false;

                EvaluateSurfaceReadiness(out int guardedChunks, out int readyChunks, out int colliderReadyChunks);

                if (guardedChunks <= 0)
                    return activeGenerations == 0 && generateQueue.Count == 0;

                int minMeshReady     = Mathf.Max(1, Mathf.CeilToInt(guardedChunks * 0.72f));
                int minColliderReady = Mathf.Max(1, Mathf.CeilToInt(guardedChunks * 0.52f));
                bool surfaceRingReady = readyChunks >= minMeshReady && colliderReadyChunks >= minColliderReady;
                if (surfaceRingReady) return true;

                return activeGenerations == 0 && generateQueue.Count == 0 && readyChunks == guardedChunks;
            }
        }

        public bool  IsStreamingActive  => streamingActive;
        public float MaxTerrainHeight   => normalizedMaxHeight;
        public float SeaLevelHeight     => seaLevelHeight;
        public float BeachWidthHeight   => beachWidthHeight;

        public float GetLocalSurfaceCoverage01()
        {
            if (!streamingActive || !hasStartedLoading) return 0f;
            EvaluateSurfaceReadiness(out int guardedChunks, out int readyChunks, out _);
            if (guardedChunks <= 0) return 0f;
            return Mathf.Clamp01((float)readyChunks / guardedChunks);
        }

        public void ConfigureSurfaceLod(bool enabled, float lod0RadiusChunks, AnimationCurve transitionCurve)
        {
            activeLodSystem    = enabled;
            lod0RadiusInChunks = Mathf.Max(0f, lod0RadiusChunks);
            lodTransitionCurve = (transitionCurve != null && transitionCurve.length > 0)
                ? new AnimationCurve(transitionCurve.keys)
                : AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        }

        public void ResetLoadState()
        {
            initialLoadComplete = false;
            hasStartedLoading   = false;
            nextQueueSortTime   = -999f;
            foreach (var kvp in chunks) kvp.Value.currentLOD = -1;
        }

        public void SetFastGeneration(bool fast)
        {
            if (!fast) { maxConcurrentGenerations = Mathf.Max(1, defaultConcurrentGenerations); return; }
            int fastBudget = Mathf.Clamp(defaultConcurrentGenerations + 1, 2, 6);
            maxConcurrentGenerations = Mathf.Max(defaultConcurrentGenerations, fastBudget);
        }

        public void SetGenerationBudget(int maxChunkBuilds)
        {
            defaultConcurrentGenerations = Mathf.Max(1, maxChunkBuilds);
            if (!streamingActive)
                maxConcurrentGenerations = defaultConcurrentGenerations;
            else
                maxConcurrentGenerations = Mathf.Max(1, Mathf.Min(maxConcurrentGenerations, defaultConcurrentGenerations));
        }

        public void ActivateStreaming(int maxChunkBuilds = -1)
        {
            SyncDerivedSettings();
            EnsureRuntimeResources();

            if (maxChunkBuilds > 0) SetGenerationBudget(maxChunkBuilds);
            else maxConcurrentGenerations = Mathf.Max(1, defaultConcurrentGenerations);

            if (streamingActive)
            {
                if (generateOcean && oceanObject != null) oceanObject.SetActive(true);
                return;
            }

            streamingVersion++;
            streamingActive = true;
            ResetLoadState();
            QueueAllChunksForRefresh();

            if (generateOcean)
            {
                if (oceanObject == null) GenerateOcean();
                else oceanObject.SetActive(true);
            }

            if (updateChunksCoroutine  == null) updateChunksCoroutine  = StartCoroutine(UpdateChunksLoop());
            if (generateChunksCoroutine == null) generateChunksCoroutine = StartCoroutine(GenerateChunksLoop());
        }

        public void DeactivateStreaming()
        {
            if (!streamingActive && (oceanObject == null || !oceanObject.activeSelf)) return;

            streamingVersion++;
            streamingActive = false;
            ResetLoadState();
            generateQueue.Clear();

            foreach (var kvp in chunks)
            {
                var chunk = kvp.Value;
                if (chunk.obj != null) chunk.obj.SetActive(false);
                ReleaseChunkResources(chunk);
                chunk.currentLOD = -1;
            }

            if (oceanObject != null) oceanObject.SetActive(false);
        }

        // ── Unity lifecycle ───────────────────────────────────────────────────

        void Awake()
        {
            SyncDerivedSettings();
            defaultConcurrentGenerations = Mathf.Max(1, maxConcurrentGenerations);
        }

        void Start()
        {
            EnsureRuntimeResources();
            if (!autoStartStreaming)
            {
                if (loadingPanel       != null) loadingPanel.SetActive(false);
                if (extraObjectToHide  != null) extraObjectToHide.SetActive(false);
                return;
            }
            if (runtimeResourcesReady)
            {
                if (TreesEnabled() && treePrefab == null) treePrefab = CreateSimpleTreePrefab();
                if (loadingPanel      != null) loadingPanel.SetActive(true);
                if (extraObjectToHide != null) extraObjectToHide.SetActive(true);
                ActivateStreaming(defaultConcurrentGenerations);

                if (enableSurfaceSpawn && spawnPrefab != null)
                {
                    var sp = Vector3.up.normalized;
                    float h = EvaluateHeight(sp);
                    Instantiate(spawnPrefab, sp * (radius + h),
                        Quaternion.FromToRotation(Vector3.up, sp), transform);
                }
                return;
            }

            if (planetMaterial == null)
            {
                var sh = Shader.Find("Custom/TerrainHeightShader");
                planetMaterial = sh != null ? new Material(sh) : new Material(Shader.Find("Standard"));
            }
            else if (planetMaterial.shader != null && planetMaterial.shader.name == "Standard")
            {
                var sh = Shader.Find("Custom/TerrainHeightShader");
                if (sh != null) planetMaterial = new Material(sh);
            }

            EnsureUniqueRuntimeMaterials();
            UpdateShaderParams();
            UpdateObjectScales();

            var prng = new System.Random(seed);
            seedOffset = new Vector3(
                prng.Next(-10000, 10000) * 0.01f,
                prng.Next(-10000, 10000) * 0.01f,
                prng.Next(-10000, 10000) * 0.01f);

            ApplySeedHueShift();
            ApplyDarkRock();
            UpdateShaderParams();

            if (TreesEnabled() && treePrefab == null) treePrefab = CreateSimpleTreePrefab();
            if (loadingPanel      != null) loadingPanel.SetActive(true);
            if (extraObjectToHide != null) extraObjectToHide.SetActive(true);

            InitializeChunkStructure();
            foreach (var kvp in chunks) { kvp.Value.currentLOD = -1; generateQueue.Add(kvp.Value); }
            hasStartedLoading = true;

            if (mainCamera != null) mainCamera.depthTextureMode |= DepthTextureMode.Depth;
            if (generateOcean) GenerateOcean();

            if (enableSurfaceSpawn && spawnPrefab != null)
            {
                var sp = Vector3.up.normalized;
                float h = EvaluateHeight(sp);
                Instantiate(spawnPrefab, sp * (radius + h),
                    Quaternion.FromToRotation(Vector3.up, sp), transform);
            }

            StartCoroutine(UpdateChunksLoop());
            StartCoroutine(GenerateChunksLoop());
        }

        void Update()
        {
            if (mainCamera != null && waterPanel != null)
            {
                float distToCenter = Vector3.Distance(mainCamera.transform.position, transform.position);
                waterPanel.SetActive(distToCenter - radius <= oceanLevel + 5f);
            }
            if (planetMaterial != null)
                planetMaterial.SetVector("_PlanetCenter", transform.position);

            UpdateOceanRuntimeParams();
        }

        void OnValidate()
        {
            SyncDerivedSettings();
            radius           = Mathf.Max(100f, radius);
            maxVertsPerChunk = Mathf.Max(4, maxVertsPerChunk);
            chunksPerFace    = Mathf.Max(1, chunksPerFace);
            ApplyDarkRock();
            ApplyBiomeTintToMaterial(planetMaterial);
            UpdateShaderParams();
            UpdateObjectScales();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, radius);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, radius + heightMultiplier * 3f);
            if (generateOcean)
            {
                Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.4f);
                Gizmos.DrawWireSphere(transform.position, radius + oceanLevel);
            }
            if (player != null)
            {
                Gizmos.color = new Color(0f, 1f, 0f, 0.15f);  Gizmos.DrawWireSphere(transform.position, lodDistance0);
                Gizmos.color = new Color(1f, 1f, 0f, 0.15f);  Gizmos.DrawWireSphere(transform.position, lodDistance1);
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.15f);Gizmos.DrawWireSphere(transform.position, lodDistance2);
                Gizmos.color = new Color(1f, 0f, 0f, 0.15f);  Gizmos.DrawWireSphere(transform.position, lodDistance3);
            }
        }

        void OnDestroy()
        {
            foreach (var kvp in chunks)
                if (kvp.Value.mesh != null) Destroy(kvp.Value.mesh);
            chunks.Clear();
        }

        // ── Runtime resource initialisation ──────────────────────────────────

        void EnsureRuntimeResources()
        {
            if (runtimeResourcesReady) return;
            SyncDerivedSettings();
            if (mainCamera == null) mainCamera = Camera.main;
            if (player == null && mainCamera != null) player = mainCamera.transform;

            if (planetMaterial == null)
            {
                var sh = Shader.Find("Custom/TerrainHeightShader");
                planetMaterial = sh != null ? new Material(sh) : new Material(Shader.Find("Standard"));
            }
            else if (planetMaterial.shader != null && planetMaterial.shader.name == "Standard")
            {
                var sh = Shader.Find("Custom/TerrainHeightShader");
                if (sh != null) planetMaterial = new Material(sh);
            }

            EnsureUniqueRuntimeMaterials();
            UpdateShaderParams();
            UpdateObjectScales();

            var prng = new System.Random(seed);
            seedOffset = new Vector3(
                prng.Next(-10000, 10000) * 0.01f,
                prng.Next(-10000, 10000) * 0.01f,
                prng.Next(-10000, 10000) * 0.01f);

            if (!visualProfileApplied)
            {
                ApplySeedHueShift();
                ApplyDarkRock();
                UpdateShaderParams();
                visualProfileApplied = true;
            }

            if (TreesEnabled() && treePrefab == null) treePrefab = CreateSimpleTreePrefab();
            if (mainCamera != null) mainCamera.depthTextureMode |= DepthTextureMode.Depth;
            runtimeResourcesReady = true;
        }

        void SyncDerivedSettings()
        {
            if (planetSettings == null) planetSettings = new PlanetSettings();
            if (noiseSettings  == null) noiseSettings  = new NoiseSettings();
            UpgradeSurfaceVisualDefaults();
            radius = Mathf.Max(100f, planetSettings.radius);
            planetSettings.radius = radius;
            normalizedMaxHeight = Mathf.Max(10f, radius * planetSettings.maxHeightRatio);
            heightMultiplier    = normalizedMaxHeight;

            float seaLevel01 = Mathf.InverseLerp(0.002f, 0.05f, Mathf.Max(0.002f, planetSettings.seaLevelRatio));
            float seaLevelBase = Mathf.Lerp(-normalizedMaxHeight * 0.22f, -normalizedMaxHeight * 0.06f, seaLevel01);
            float oceanRadiusScale = Mathf.Clamp(oceanSurfaceRadiusScale <= 0.001f ? 0.97f : oceanSurfaceRadiusScale, 0.85f, 1.05f);
            float seaSurfaceRadius = (radius + seaLevelBase * Mathf.Clamp(seaLevelScale, 0.5f, 1.05f)) * oceanRadiusScale;
            seaLevelHeight = seaSurfaceRadius - radius;
            oceanLevel     = seaLevelHeight;
            oceanDepth     = -Mathf.Lerp(normalizedMaxHeight * 0.34f, normalizedMaxHeight * 0.60f, seaLevel01);
            beachWidthHeight = Mathf.Max(normalizedMaxHeight * 0.05f, normalizedMaxHeight * (planetSettings.beachWidthRatio * 3.5f));

            InvalidateSebPlateData();
            lock (heightCacheLock) heightCache.Clear();
        }

        void InvalidateSebPlateData()
        {
            lock (sebPlateDataLock)
            {
                sebPlateDataReady  = false;
                sebPlateCenters    = null;
                sebPlateMacroValues = null;
            }
        }

        void UpgradeSurfaceVisualDefaults()
        {
            if (terrainNormalTiling      <= 0.001f || Mathf.Approximately(terrainNormalTiling, 24f))       terrainNormalTiling      = 48f;
            if (terrainMicroNormalTiling <= 0.001f || Mathf.Approximately(terrainMicroNormalTiling, 82f))  terrainMicroNormalTiling = 164f;
            if (terrainNormalStrength    <= 0.001f || Mathf.Approximately(terrainNormalStrength, 0.72f))   terrainNormalStrength    = 0.92f;
            if (terrainMicroNormalStrength <= 0.001f || Mathf.Approximately(terrainMicroNormalStrength, 0.34f)) terrainMicroNormalStrength = 0.28f;
            if (terrainCavityStrength    <= 0.001f) terrainCavityStrength    = 0.82f;
            if (oceanSurfaceRadiusScale  <= 0.001f) oceanSurfaceRadiusScale  = 0.97f;
        }

        void UpdateObjectScales()
        {
            float s1 = 2f * (radius + 3f * heightMultiplier);
            if (firstObject  != null) firstObject.transform.localScale  = new Vector3(s1, s1, s1);
            float s2 = 2f * radius;
            if (secondObject != null) secondObject.transform.localScale = new Vector3(s2, s2, s2);
        }

        bool TreesEnabled() => treeProbability > 0.0001f && maxTreesPerChunk > 0;

        // ── Material helpers ──────────────────────────────────────────────────

        void EnsureUniqueRuntimeMaterials()
        {
            if (Application.isPlaying)
            {
                if (planetMaterial != null) planetMaterial = new Material(planetMaterial);
                if (oceanMaterial  != null) oceanMaterial  = new Material(oceanMaterial);
            }
            EnsureSurfaceNormalMapLoaded();
            UpgradeSurfaceVisualDefaults();
            ApplyBiomeTintToMaterial(planetMaterial);
            UpdateOceanMaterialParams();
        }

        void EnsureSurfaceNormalMapLoaded()
        {
            if (terrainNormalMap != null || string.IsNullOrWhiteSpace(terrainNormalResourcePath)) return;
            terrainNormalMap = Resources.Load<Texture2D>(terrainNormalResourcePath);
        }

        void ApplyBiomeTintToMaterial(Material mat)
        {
            if (mat == null) return;
            Color tint = Color.Lerp(grassColor, forestColor, 0.35f);
            tint = Color.Lerp(tint, rockColor, 0.10f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     tint);

            void SetIf(string prop, Color c) { if (mat.HasProperty(prop)) mat.SetColor(prop, c); }
            SetIf("_SandColor",    sandColor);        SetIf("_BeachColor",   sandColor);
            SetIf("_GrassColor",   grassColor);       SetIf("_ForestColor",  forestColor);
            SetIf("_RockColor",    rockColor);        SetIf("_SnowColor",    snowColor);
            SetIf("_RimColor",     atmosphereColor);  SetIf("_ShallowColor", shallowWaterColor);
            SetIf("_DeepColor",    deepWaterColor);

            EnsureSurfaceNormalMapLoaded();
            if (terrainNormalMap != null)
            {
                if (mat.HasProperty("_DetailNormalMap"))  mat.SetTexture("_DetailNormalMap",  terrainNormalMap);
                if (mat.HasProperty("_SurfaceNormalMap")) mat.SetTexture("_SurfaceNormalMap", terrainNormalMap);
            }
            if (mat.HasProperty("_DetailNormalTiling"))          mat.SetFloat("_DetailNormalTiling",          terrainNormalTiling);
            if (mat.HasProperty("_DetailNormalStrength"))        mat.SetFloat("_DetailNormalStrength",        terrainNormalStrength);
            if (mat.HasProperty("_DetailMicroNormalTiling"))     mat.SetFloat("_DetailMicroNormalTiling",     terrainMicroNormalTiling);
            if (mat.HasProperty("_DetailMicroNormalStrength"))   mat.SetFloat("_DetailMicroNormalStrength",   terrainMicroNormalStrength);
            if (mat.HasProperty("_DetailCavityStrength"))        mat.SetFloat("_DetailCavityStrength",        terrainCavityStrength);
        }

        void UpdateShaderParams()
        {
            if (planetMaterial != null)
            {
                ApplyBiomeTintToMaterial(planetMaterial);
                planetMaterial.SetVector("_PlanetCenter", transform.position);
                planetMaterial.SetFloat("_PlanetRadius", radius);
                float maxH = normalizedMaxHeight;
                planetMaterial.SetFloat("_MaxHeight",  maxH);
                planetMaterial.SetFloat("_WaterLevel", seaLevelHeight);
                planetMaterial.SetFloat("_SandLevel",  seaLevelHeight + beachWidthHeight);
                planetMaterial.SetFloat("_GrassLevel", normalizedMaxHeight * 0.2f);
                planetMaterial.SetFloat("_RockLevel",  normalizedMaxHeight * 0.55f);
                if (planetMaterial.HasProperty("_NormalBlend"))   planetMaterial.SetFloat("_NormalBlend",   0.32f);
                if (planetMaterial.HasProperty("_WrapLighting"))  planetMaterial.SetFloat("_WrapLighting",  0.42f);
                if (planetMaterial.HasProperty("_RimColor"))      planetMaterial.SetColor("_RimColor",      atmosphereColor);
                if (planetMaterial.HasProperty("_RimStrength"))   planetMaterial.SetFloat("_RimStrength",   0.028f);
                if (planetMaterial.HasProperty("_RimPower"))      planetMaterial.SetFloat("_RimPower",      4.2f);
                if (planetMaterial.HasProperty("_SaturationBoost"))planetMaterial.SetFloat("_SaturationBoost", 1.12f);
                if (planetMaterial.HasProperty("_ShadowTint"))    planetMaterial.SetColor("_ShadowTint",    new Color(0.46f, 0.46f, 0.52f, 1f));
            }
            UpdateOceanMaterialParams();
        }

        // ── Ocean ─────────────────────────────────────────────────────────────

        void GenerateOcean()
        {
            if (oceanMaterial == null) { Debug.LogWarning("Ocean material not assigned!"); return; }
            if (oceanObject != null) Destroy(oceanObject);
            oceanObject = new GameObject("Ocean");
            oceanObject.transform.parent        = transform;
            oceanObject.transform.localPosition = Vector3.zero;
            var mf = oceanObject.AddComponent<MeshFilter>();
            var mr = oceanObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = oceanMaterial;
            mf.sharedMesh     = BuildOceanSphere();
            if (oceanMaterial.HasProperty("_WaterLevel"))
                oceanMaterial.SetFloat("_WaterLevel", radius + oceanLevel);
        }

        Mesh BuildOceanSphere()
        {
            var mesh  = new Mesh { name = "Ocean" };
            var verts = new List<Vector3>();
            var uvs   = new List<Vector2>();
            var tris  = new List<int>();
            float r  = radius + oceanLevel;
            int   ve = oceanResolution + 1;
            for (int f = 0; f < 6; f++)
            {
                int bi = verts.Count;
                for (int y = 0; y < ve; y++)
                    for (int x = 0; x < ve; x++)
                    {
                        float u  = -1f + (float)x / oceanResolution * 2f;
                        float v  = -1f + (float)y / oceanResolution * 2f;
                        var   sp = GetCubePoint((Face)f, u, v).normalized;
                        verts.Add(sp * r);
                        uvs.Add(new Vector2(sp.x * 0.5f + 0.5f, sp.z * 0.5f + 0.5f));
                    }
                for (int y = 0; y < oceanResolution; y++)
                    for (int x = 0; x < oceanResolution; x++)
                    {
                        int i0 = bi + y * ve + x, i1 = i0+1, i2 = i0+ve, i3 = i2+1;
                        tris.Add(i0); tris.Add(i1); tris.Add(i2);
                        tris.Add(i1); tris.Add(i3); tris.Add(i2);
                    }
            }
            if (verts.Count >= 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        void UpdateOceanMaterialParams()
        {
            if (oceanMaterial == null) return;
            float radius01      = Mathf.InverseLerp(1500f, 10000f, radius);
            float waveAmplitude = Mathf.Clamp(normalizedMaxHeight * 0.0048f, 0.12f, 1.65f);
            float waveFrequency = Mathf.Lerp(0.0060f, 0.0021f, radius01);
            float foamDistance  = Mathf.Clamp(normalizedMaxHeight * 0.026f, 0.45f, 3.8f);
            float depthGradient = Mathf.Lerp(0.042f, 0.016f, radius01);

            void SC(string p, Color c)  { if (oceanMaterial.HasProperty(p)) oceanMaterial.SetColor(p, c); }
            void SF(string p, float v)  { if (oceanMaterial.HasProperty(p)) oceanMaterial.SetFloat(p, v); }

            SC("_ShallowColor",    new Color(shallowWaterColor.r, shallowWaterColor.g, shallowWaterColor.b, 0.72f));
            SC("_DeepColor",       new Color(deepWaterColor.r,    deepWaterColor.g,    deepWaterColor.b,    0.86f));
            SC("_NightShallowColor", new Color(shallowWaterColor.r*0.10f, shallowWaterColor.g*0.09f, shallowWaterColor.b*0.12f, 0.84f));
            SC("_NightDeepColor",  new Color(deepWaterColor.r*0.07f, deepWaterColor.g*0.06f, deepWaterColor.b*0.09f, 0.94f));
            SC("_FoamColor",       Color.Lerp(Color.white, shallowWaterColor, 0.08f));
            SF("_WaveAmplitude",   waveAmplitude);
            SF("_WaveFrequency",   waveFrequency);
            SF("_WaveSpeed",       Mathf.Lerp(0.42f, 0.68f, radius01));
            SF("_FoamDistance",    foamDistance);
            SF("_FoamAmount",      0.24f);
            SF("_FoamSpeed",       Mathf.Lerp(0.20f, 0.48f, 1f - radius01));
            SF("_DepthGradient",   depthGradient);
            SF("_Smoothness",      0.74f);
            SF("_Transparency",    0.52f);
            SF("_FresnelPower",    4.2f);
            SF("_SparkleStrength", 0.08f);
            SF("_Distortion",      0.0025f);
            SF("_NightAmbient",    0.012f);
            SF("_HorizonSoftness", 4.0f);
            SF("_SpecularStrength",0.88f);
            SF("_WaveChop",        Mathf.Lerp(0.42f, 0.68f, 1f - radius01));
            SF("_DeepAbsorption",  Mathf.Lerp(0.58f, 0.82f, radius01));
            SF("_FoamSharpness",   0.72f);
            SF("_EdgeFade",        0.18f);
            UpdateOceanRuntimeParams();
        }

        void UpdateOceanRuntimeParams()
        {
            if (oceanMaterial == null) return;
            if (oceanMaterial.HasProperty("_PlanetCenter")) oceanMaterial.SetVector("_PlanetCenter", transform.position);
            if (oceanMaterial.HasProperty("_PlanetRadius")) oceanMaterial.SetFloat ("_PlanetRadius", radius);
            if (oceanMaterial.HasProperty("_WaterLevel"))   oceanMaterial.SetFloat ("_WaterLevel",   radius + oceanLevel);
        }

        // ── Geometry utility ──────────────────────────────────────────────────

        Vector3 GetCubePoint(Face face, float u, float v)
        {
            switch (face)
            {
                case Face.Up:    return new Vector3( u,  1f, -v);
                case Face.Down:  return new Vector3( u, -1f,  v);
                case Face.Left:  return new Vector3(-1f,  v,  u);
                case Face.Right: return new Vector3( 1f,  v, -u);
                case Face.Front: return new Vector3( u,   v,  1f);
                case Face.Back:  return new Vector3(-u,   v, -1f);
                default:         return Vector3.up;
            }
        }

        float ClampTerrainHeight(float height) =>
            Mathf.Clamp(height, -normalizedMaxHeight, normalizedMaxHeight);

        Vector3 BuildStableSurfacePosition(Vector3 spherePoint, float height)
        {
            double clampedHeight = ClampTerrainHeight(height);
            double surfaceRadius = radius + clampedHeight;
            Vector3 dir = spherePoint.normalized;
            return new Vector3(
                (float)(dir.x * surfaceRadius),
                (float)(dir.y * surfaceRadius),
                (float)(dir.z * surfaceRadius));
        }

        float CalculateAO(float[,] hm, int x, int y, int size, float ch)
        {
            float sum = 0f; int cnt = 0;
            if (x > 0)        { sum += hm[x-1, y]; cnt++; }
            if (x < size - 1) { sum += hm[x+1, y]; cnt++; }
            if (y > 0)        { sum += hm[x, y-1]; cnt++; }
            if (y < size - 1) { sum += hm[x, y+1]; cnt++; }
            if (cnt == 0) return 0f;
            float diff = Mathf.Clamp01((sum / cnt - ch) / (heightMultiplier * 0.35f));
            return Mathf.Pow(diff, 0.65f);
        }
    }
} // end namespace PlanetGeneration
