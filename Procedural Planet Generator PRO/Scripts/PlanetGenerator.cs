namespace PlanetGeneration
{
    using UnityEngine;
    using System.Collections;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using UnityEngine.Rendering;

    /// <summary>
    /// NMS-style Planet Generator — Uber Noise (GDC 2017, Sean Murray)
    /// </summary>
    public class PlanetGenerator : MonoBehaviour
    {
        public enum TerrainStyle
        {
            SebInspired = 0,
            HybridNms = 1,
        }

        [System.Serializable]
        public class PlanetSettings
        {
            [Min(100f)] public float radius = 5000f;
            [Range(0.01f, 0.5f)] public float maxHeightRatio = 0.16f;
            [Range(0f, 1f)] public float seaLevelRatio = 0.02f;
            [Range(0.001f, 0.05f)] public float beachWidthRatio = 0.01f;
        }

        [System.Serializable]
        public class NoiseSettings
        {
            [Header("Base Terrain")]
            public float baseScale = 0.5f;
            [Range(3, 9)] public int baseOctaves = 5;
            [Range(0.3f, 0.7f)] public float basePersistence = 0.5f;
            [Range(1.8f, 2.6f)] public float baseLacunarity = 2.05f;
            [Range(0f, 1f)] public float continentThreshold = 0.45f;

            [Header("Mountain Layer")]
            public float mountainFrequency = 1.9f;
            [Range(2, 8)] public int mountainOctaves = 5;
            [Range(0.3f, 0.7f)] public float mountainPersistence = 0.5f;
            [Range(1.8f, 2.8f)] public float mountainLacunarity = 2.2f;
            [Range(0f, 2f)] public float mountainStrength = 0.75f;
            [Range(0f, 1f)] public float mountainMaskThreshold = 0.6f;
            [Range(0f, 2f)] public float ridgeSharpness = 1.1f;

            [Header("Detail Layer")]
            public float detailScale = 14f;
            [Range(2, 6)] public int detailOctaves = 3;
            [Range(0.2f, 0.7f)] public float detailPersistence = 0.45f;
            [Range(1.8f, 3f)] public float detailLacunarity = 2.4f;
            [Range(0f, 0.2f)] public float detailStrength = 0.06f;

            [Header("Mega Mountains")]
            public float megaMaskScale = 0.38f;
            [Range(0f, 1f)] public float megaMaskThreshold = 0.82f;
            [Range(0f, 2f)] public float megaStrength = 1.15f;

            [Header("Cliffs / Spikes")]
            public float cliffScale = 2.8f;
            [Range(0f, 1f)] public float cliffThreshold = 0.62f;
            [Range(0f, 3f)] public float cliffStrength = 0.45f;
            [Range(0f, 2f)] public float peakSharpness = 0.45f;
        }

        [Header("Planet")]
        public float radius = 5000f;
        public Material planetMaterial;
        public PlanetSettings planetSettings = new PlanetSettings();
        public NoiseSettings noiseSettings = new NoiseSettings();

        [Header("Terrain Style")]
        [Tooltip("SebInspired gives broader continents and cleaner ridge chains. HybridNms keeps the previous terrain stack.")]
        public TerrainStyle terrainStyle = TerrainStyle.SebInspired;

        [Header("Seb Continental Plates")]
        [Range(8, 64)] public int sebPlateCount = 23;
        [Range(0.05f, 0.95f)] public float sebLandRatio = 0.42f;
        [Range(0f, 0.45f)] public float sebPlateJitter = 0.18f;
        [Range(0f, 1f)] public float sebPlateBlend = 0.58f;
        [Range(0.5f, 3f)] public float sebPlateMacroScale = 1.3f;
        [Range(0.5f, 5f)] public float sebCoastNoiseScale = 2.2f;
        [Range(0f, 0.5f)] public float sebCoastNoiseStrength = 0.15f;
        [Range(0f, 2f)] public float sebBoundaryMountainStrength = 0.9f;

        [Header("Ocean")]
        public bool generateOcean = false;
        public Material oceanMaterial;
        public float oceanLevel = 35f;
        [Range(0.5f, 1.05f)]
        public float seaLevelScale = 0.68f;
        [Range(0.85f, 1.05f)]
        public float oceanSurfaceRadiusScale = 0.97f;
        [Range(4, 128)]
        public int oceanResolution = 24;

        [Header("Chunks and LOD")]
        [Range(2, 256)]
        public int maxVertsPerChunk = 64;
        [Range(1, 64)]
        public int chunksPerFace = 8;
        [Range(1, 4)]
        public int lodLevels = 3;
        [HideInInspector]
        public bool activeLodSystem = true;
        [HideInInspector]
        public float lod0RadiusInChunks = 2f;
        [HideInInspector]
        public AnimationCurve lodTransitionCurve = null;

        [Header("NMS Terrain Scale")]
        public float heightMultiplier = 700f;
        public float continentScale = 0.18f;
        [Range(0f, 1f)]
        public float continentThreshold = 0.44f;
        public float oceanDepth = -180f;

        [Header("NMS Base Terrain (Exponential FBM)")]
        public float baseScale = 0.55f;
        [Range(4, 10)]
        public int baseOctaves = 5;
        [Range(0.3f, 0.7f)]
        public float basePersistence = 0.40f;
        public float baseLacunarity = 2.1f;
        [Range(1f, 3f)]
        public float octaveExponent = 1.6f;

        [Header("NMS Ridge Mountains")]
        public float ridgeScale = 1.4f;
        [Range(2, 8)]
        public int ridgeOctaves = 5;
        [Range(0f, 1f)]
        public float ridgePersistence = 0.46f;
        [Range(0f, 2f)]
        public float ridgeStrength = 1.45f;
        [Range(0f, 1f)]
        public float ridgeSharpness = 0.92f;
        [Range(0f, 2f)]
        public float ridgeGain = 0.9f;

        [Header("NMS Mesa / Plateau (NMS-стиль)")]
        public float mesaScale = 0.45f;
        [Range(1f, 20f)]
        public float mesaSharpness = 10f;
        [Range(0f, 1f)]
        public float mesaStrength = 0.28f;
        [Range(0f, 1f)]
        public float mesaFrequency = 0.38f;

        [Header("NMS Canyon Layer")]
        public bool enableCanyons = true;
        public float canyonScale = 2.2f;
        [Range(0f, 1f)]
        public float canyonStrength = 0.35f;
        [Range(0f, 1f)]
        public float canyonFrequency = 0.4f;

        [Header("NMS Mega Structures")]
        public float megaScale = 0.25f;
        [Range(0f, 1f)]
        public float megaRarity = 0.80f;
        [Range(0f, 3f)]
        public float megaStrength = 0.8f;

        [Header("NMS Slope Erosion")]
        [Range(0f, 4f)]
        public float slopeErosion = 1.4f;
        [Range(0f, 1f)]
        public float altitudeDamping = 0.55f;

        [Header("NMS Domain Warp")]
        // Legacy: раньше warpScale был множителем для нормализованных координат.
        // Для NMS-стиля варп должен работать в мировых координатах (см. NMS_Terrain_Generation_FULL.txt).
        public float warpScale = 0.7f;
        [Range(0.0001f, 0.01f)]
        public float warpFrequency = 0.0016f;
        [Range(0.0001f, 0.01f)]
        public float warp2Frequency = 0.0011f;
        [Range(0.001f, 0.08f)]
        public float warpAmplitudeRatio = 0.008f;
        [Range(0f, 1f)]
        public float warpStrength = 0.18f;
        [Range(0f, 1f)]
        public float warp2Strength = 0.08f;

        [Header("NMS Rivers (шумовые реки)")]
        public bool enableRivers = true;
        [Range(0.001f, 0.01f)]
        public float riverScale = 0.0035f;       // масштаб сети рек (частота)
        [Range(0f, 1f)]
        public float riverStrength = 0.55f;      // глубина прорезки каналов
        [Range(0.05f, 0.45f)]
        public float riverNarrow = 0.18f;        // ширина: меньше = уже (0.05 = ручьи, 0.35 = широко)
        [Range(10f, 200f)]
        public float riverMaxElevation = 80f;    // выше этого (над уровнем моря) рек нет
        [Range(0f, 1f)]
        public float riverBranchMix = 0.35f;     // смешение двух слоёв → ветвистость притоков

        [Header("NMS Detail Layer")]
        public float detailScale = 28f;
        [Range(0f, 0.3f)]
        public float detailStrength = 0.04f;
        [Range(0f, 1f)]
        public float detailSlopeBoost = 0.8f;

        [Header("NMS Micro Noise (мелкий рельеф)")]
        public float microScale = 80f;
        [Range(0f, 0.15f)]
        public float microStrength = 0.008f;

        [Header("AAA Terrain Pipeline")]
        [Range(2, 6)]
        public int geometryOctaves = 4;
        [Range(0.2f, 0.7f)]
        public float geometryAmplitudeDecay = 0.42f;
        [Range(1.7f, 2.2f)]
        public float geometryLacunarity = 1.94f;
        [Range(0f, 1f)]
        public float highFrequencyDamping = 0.46f;
        [Range(10f, 55f)]
        public float maxSlopeDegrees = 42f;
        [Range(0, 4)]
        public int slopeLimitPasses = 1;
        [Range(0, 4)]
        public int erosionPasses = 1;
        [Range(0f, 1f)]
        public float erosionStrength = 0.18f;

        [Header("NMS Biome Mask (без полос!)")]
        [Range(0f, 5f)]
        public float biomeContrast = 4.0f;
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
        [Range(0f, 1f)]
        public float rockDarkness = 0.72f;
        [Header("Snow")]
        public bool enableSnow = false;
        public float snowHeightStart   = 180f;
        [Range(0f, 1f)]
        public float polarNoiseBlend = 0.6f;

        [Header("LOD Distances")]
        public float lodDistance0 = 1500f;
        public float lodDistance1 = 4000f;
        public float lodDistance2 = 9000f;
        public float lodDistance3 = 18000f;
        public float unloadDistance = 22000f;

        [Header("Camera")]
        public Camera mainCamera;
        public Transform player;

        [Header("Streaming")]
        public bool autoStartStreaming = true;

        [Header("Performance")]
        public int maxConcurrentGenerations = 4;
        public float updateInterval = 0.2f;
        public bool useColliders = true;
        public float colliderDistance = 3000f;
        [Header("Visual")]
        [Range(0f, 2f)]
        public float slopeRockBlend = 0.34f;   // NMS-стиль: камень заметен на боках гор, но не съедает вершины
        [Range(0f, 1f)]
        public float terrainSmoothing = 0.22f;
        [Range(0f, 1f)]
        public float ambientOcclusion = 0.45f;
        public float normalSampleDistance = 1.5f;

        [Header("Surface Detail")]
        public Texture2D terrainNormalMap;
        public string terrainNormalResourcePath = "PlanetSurfaceNormal";
        [Range(1f, 128f)] public float terrainNormalTiling = 48f;
        [Range(0f, 2f)] public float terrainNormalStrength = 0.92f;
        [Range(1f, 256f)] public float terrainMicroNormalTiling = 164f;
        [Range(0f, 2f)] public float terrainMicroNormalStrength = 0.28f;
        [Range(0f, 2f)] public float terrainCavityStrength = 0.82f;
        [Range(0f, 1f)] public float cloudCoverage = 0.58f;
        [Range(0f, 1f)] public float cloudDensity = 0.72f;

        [Header("Trees")]
        public GameObject treePrefab;
        [Range(0f, 0.1f)]
        public float treeProbability = 0.025f;
        [Range(0f, 1f)]
        public float maxTreeSlope = 0.25f;
        public float treeSurfaceOffset = 0.15f;
        public float forestNoiseScale = 0.0019f;
        [Range(0f, 1f)] public float forestCoverage = 0.58f;
        [Range(0f, 1f)] public float forestDensityBoost = 0.85f;
        [Range(10, 240)] public int maxTreesPerChunk = 110;
        public Shader treeShader;

        [Header("UI")]
        public GameObject waterPanel;
        public GameObject loadingPanel;
        public GameObject extraObjectToHide;

        [Header("Scalable Objects")]
        public GameObject firstObject;
        public GameObject secondObject;

        [Header("Custom Spawn")]
        public bool enableSurfaceSpawn = false;
        public GameObject spawnPrefab;

        // Internal fields, methods, chunk logic, noise, etc.
        private Dictionary<Vector3Int, ChunkData> chunks = new Dictionary<Vector3Int, ChunkData>();
        private List<ChunkData> generateQueue = new List<ChunkData>();
        private Vector3 seedOffset;
        private GameObject oceanObject;
        private int activeGenerations = 0;
        private bool initialLoadComplete = false;
        private bool hasStartedLoading   = false;
        private readonly Dictionary<Vector3Int, float> heightCache = new Dictionary<Vector3Int, float>(16384);
        private readonly object heightCacheLock = new object();
        private readonly object sebPlateDataLock = new object();
        private const float HeightCacheQuant = 10000f;
        private float normalizedMaxHeight = 700f;
        private float seaLevelHeight = 0f;
        private float beachWidthHeight = 20f;
        private float observedMinHeight = float.MaxValue;
        private float observedMaxHeight = float.MinValue;
        private float lastHeightLogTime = -999f;
        private float lastSlopeLogTime = -999f;
        private bool streamingActive = false;
        private bool runtimeResourcesReady = false;
        private bool chunkStructureInitialized = false;
        private bool visualProfileApplied = false;
        private int defaultConcurrentGenerations = 4;
        private int streamingVersion = 0;
        private float nextQueueSortTime = -999f;
        private Coroutine updateChunksCoroutine;
        private Coroutine generateChunksCoroutine;
        private bool sebPlateDataReady = false;
        private Vector3[] sebPlateCenters;
        private float[] sebPlateMacroValues;

        private enum Face { Up, Down, Left, Right, Front, Back }

        struct SebPlateSample
        {
            public float primaryValue;
            public float secondaryValue;
            public float blendedValue;
            public float boundary;
            public float primaryDistance;
            public float secondaryDistance;
        }

        private class ChunkData
        {
            public GameObject obj;
            public Face face;
            public int x, y;
            public int currentLOD = -1;
            public bool isGenerating = false;
            public Mesh mesh;
            public Vector3 centerWorldPos;
            public readonly List<GameObject> treePool = new List<GameObject>(16);
            public readonly List<Vector3Int> neighborKeys = new List<Vector3Int>(8);
        }

        // ── Public API ───────────────────────────────────────────────────────
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

                int minMeshReady = Mathf.Max(1, Mathf.CeilToInt(guardedChunks * 0.72f));
                int minColliderReady = Mathf.Max(1, Mathf.CeilToInt(guardedChunks * 0.52f));
                bool surfaceRingReady = readyChunks >= minMeshReady && colliderReadyChunks >= minColliderReady;
                if (surfaceRingReady)
                    return true;

                return activeGenerations == 0 && generateQueue.Count == 0 && readyChunks == guardedChunks;
            }
        }

        public bool IsStreamingActive => streamingActive;

        public float MaxTerrainHeight => normalizedMaxHeight;
        public float SeaLevelHeight => seaLevelHeight;
        public float BeachWidthHeight => beachWidthHeight;

        public float GetLocalSurfaceCoverage01()
        {
            if (!streamingActive || !hasStartedLoading)
                return 0f;

            EvaluateSurfaceReadiness(out int guardedChunks, out int readyChunks, out _);
            if (guardedChunks <= 0)
                return 0f;

            return Mathf.Clamp01((float)readyChunks / guardedChunks);
        }

        public void ConfigureSurfaceLod(bool enabled, float lod0RadiusChunks, AnimationCurve transitionCurve)
        {
            activeLodSystem = enabled;
            lod0RadiusInChunks = Mathf.Max(0f, lod0RadiusChunks);
            if (transitionCurve != null && transitionCurve.length > 0)
                lodTransitionCurve = new AnimationCurve(transitionCurve.keys);
            else
                lodTransitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        }

        public Mesh BuildProxyMesh(int faceResolution, string meshName = null)
        {
            EnsureRuntimeResources();

            int resolution = Mathf.Clamp(faceResolution, 2, 48);
            int vertsPerEdge = resolution + 1;
            float worldCellSize = Mathf.Max(1f, radius * (2f / resolution) * 0.68f);

            var mesh = new Mesh
            {
                name = string.IsNullOrWhiteSpace(meshName) ? $"PlanetProxy_{resolution}" : meshName
            };

            int estimatedVertexCount = 6 * vertsPerEdge * vertsPerEdge;
            if (estimatedVertexCount >= 65535)
                mesh.indexFormat = IndexFormat.UInt32;

            var vertices = new List<Vector3>(estimatedVertexCount);
            var normals = new List<Vector3>(estimatedVertexCount);
            var colors = new List<Color>(estimatedVertexCount);
            var uvs = new List<Vector2>(estimatedVertexCount);
            var triangles = new List<int>(6 * resolution * resolution * 6);

            for (int faceIndex = 0; faceIndex < 6; faceIndex++)
            {
                Face face = (Face)faceIndex;
                int baseIndex = vertices.Count;

                for (int y = 0; y < vertsPerEdge; y++)
                {
                    float v = -1f + (float)y / resolution * 2f;
                    for (int x = 0; x < vertsPerEdge; x++)
                    {
                        float u = -1f + (float)x / resolution * 2f;
                        Vector3 dir = GetCubePoint(face, u, v).normalized;
                        float height = EvaluateMacroHeightCached(dir);
                        float localScale = 1f + ClampTerrainHeight(height) / Mathf.Max(1f, radius);

                        vertices.Add(dir * localScale);
                        normals.Add(ComputeSurfaceNormal(dir, height, worldCellSize));
                        colors.Add(GetBiomeColor(dir, height));
                        uvs.Add(new Vector2((float)x / resolution, (float)y / resolution));
                    }
                }

                for (int y = 0; y < resolution; y++)
                {
                    for (int x = 0; x < resolution; x++)
                    {
                        int i0 = baseIndex + y * vertsPerEdge + x;
                        int i1 = i0 + 1;
                        int i2 = i0 + vertsPerEdge;
                        int i3 = i2 + 1;

                        triangles.Add(i0); triangles.Add(i1); triangles.Add(i2);
                        triangles.Add(i1); triangles.Add(i3); triangles.Add(i2);
                    }
                }
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        void EvaluateSurfaceReadiness(out int guardedChunks, out int readyChunks, out int colliderReadyChunks)
        {
            float guardDistance = GetSurfaceSafetyDistance();
            guardedChunks = 0;
            readyChunks = 0;
            colliderReadyChunks = 0;

            foreach (var kvp in chunks)
            {
                var cd = kvp.Value;
                if (cd == null || cd.obj == null)
                    continue;

                if (player != null)
                {
                    float d = Vector3.Distance(player.position, transform.TransformPoint(cd.centerWorldPos));
                    if (d > guardDistance)
                        continue;
                }

                guardedChunks++;
                if (ChunkHasBuiltMesh(cd))
                    readyChunks++;
                if (!useColliders || ChunkHasReadyCollider(cd))
                    colliderReadyChunks++;
            }
        }

        float GetChunkWorldSpan()
        {
            float faceCell = radius * (2f / Mathf.Max(1, chunksPerFace));
            return Mathf.Max(faceCell * 0.95f, radius * 0.08f);
        }

        float GetChunkHorizonBias()
        {
            return Mathf.Clamp(GetChunkWorldSpan() / Mathf.Max(1f, radius), 0.04f, 0.18f);
        }

        float GetSurfaceSafetyDistance()
        {
            return Mathf.Max(radius * 0.24f, colliderDistance * 0.55f) + GetChunkWorldSpan() * 1.20f;
        }

        float GetColliderSafetyDistance()
        {
            return Mathf.Max(colliderDistance * 0.82f, radius * 0.26f) + GetChunkWorldSpan() * 0.55f;
        }

        float GetBacksideCullStartDistance()
        {
            float baseDistance = terrainStyle == TerrainStyle.SebInspired
                ? radius * 0.90f
                : radius * 0.42f;
            return Mathf.Max(GetSurfaceSafetyDistance() * 1.45f, baseDistance);
        }

        bool IsChunkBacksideCulled(ChunkData chunk, Vector3 viewerPosition)
        {
            if (chunk == null)
                return false;

            Vector3 viewerFromCenter = viewerPosition - transform.position;
            if (viewerFromCenter.sqrMagnitude <= 1e-6f)
                return false;

            float viewerSurfaceDistance = Mathf.Max(0f, viewerFromCenter.magnitude - radius);
            float cullStartDistance = GetBacksideCullStartDistance();
            if (viewerSurfaceDistance <= cullStartDistance)
                return false;

            Vector3 viewerDir = viewerFromCenter.normalized;
            float horizonBias = GetChunkHorizonBias();
            float cullBlend = Mathf.InverseLerp(cullStartDistance, cullStartDistance + radius * 1.35f, viewerSurfaceDistance);
            float nearSurfaceSlack = Mathf.Lerp(0.24f, 0.02f, cullBlend);
            float dot = Vector3.Dot(chunk.centerWorldPos.normalized, viewerDir);
            return dot < -(horizonBias + nearSurfaceSlack);
        }

        void DeactivateChunk(ChunkData chunk, bool releaseResources)
        {
            if (chunk == null)
                return;

            if (chunk.obj != null)
                chunk.obj.SetActive(false);

            chunk.currentLOD = -1;

            if (releaseResources && !chunk.isGenerating)
                ReleaseChunkResources(chunk);
        }

        bool ChunkHasBuiltMesh(ChunkData chunk)
        {
            if (chunk == null || chunk.obj == null || !chunk.obj.activeSelf)
                return false;

            var mf = chunk.obj.GetComponent<MeshFilter>();
            return mf != null && mf.sharedMesh != null;
        }

        bool ChunkHasReadyCollider(ChunkData chunk)
        {
            if (chunk == null || chunk.obj == null || !chunk.obj.activeSelf)
                return false;

            var mc = chunk.obj.GetComponent<MeshCollider>();
            return mc != null && mc.enabled && mc.sharedMesh != null;
        }

        public void ResetLoadState()
        {
            initialLoadComplete = false;
            hasStartedLoading = false;
            nextQueueSortTime = -999f;
            foreach (var kvp in chunks)
                kvp.Value.currentLOD = -1;
        }

        public void SetFastGeneration(bool fast)
        {
            if (!fast)
            {
                maxConcurrentGenerations = Mathf.Max(1, defaultConcurrentGenerations);
                return;
            }

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

            if (maxChunkBuilds > 0)
                SetGenerationBudget(maxChunkBuilds);
            else
                maxConcurrentGenerations = Mathf.Max(1, defaultConcurrentGenerations);

            if (streamingActive)
            {
                if (generateOcean && oceanObject != null)
                    oceanObject.SetActive(true);
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

            if (updateChunksCoroutine == null)
                updateChunksCoroutine = StartCoroutine(UpdateChunksLoop());
            if (generateChunksCoroutine == null)
                generateChunksCoroutine = StartCoroutine(GenerateChunksLoop());
        }

        public void DeactivateStreaming()
        {
            if (!streamingActive && (oceanObject == null || !oceanObject.activeSelf))
                return;

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

            if (oceanObject != null)
                oceanObject.SetActive(false);
        }

        // ── Unity lifecycle ──────────────────────────────────────────────────
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
                if (loadingPanel != null) loadingPanel.SetActive(false);
                if (extraObjectToHide != null) extraObjectToHide.SetActive(false);
                return;
            }
            if (runtimeResourcesReady)
            {
                if (TreesEnabled() && treePrefab == null)
                    treePrefab = CreateSimpleTreePrefab();
                if (loadingPanel != null) loadingPanel.SetActive(true);
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
                // Если шаблон не задан, используем шейдер, который читает vertex colors.
                var sh = Shader.Find("Custom/TerrainHeightShader");
                planetMaterial = sh != null ? new Material(sh) : new Material(Shader.Find("Standard"));
            }
            else if (planetMaterial.shader != null && planetMaterial.shader.name == "Standard")
            {
                // Standard не использует vertex colors → все планеты выглядят одинаково.
                // Переключаемся на шейдер планеты, если он есть.
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

            // Гарантируем, что даже похожие палитры будут отличаться визуально.
            // Это НЕ меняет шум/рельеф — только сдвигает оттенок биомных цветов по seed.
            ApplySeedHueShift();
            ApplyDarkRock();
            UpdateShaderParams();

            if (TreesEnabled() && treePrefab == null)
                treePrefab = CreateSimpleTreePrefab();
            if (loadingPanel != null) loadingPanel.SetActive(true);
            if (extraObjectToHide != null) extraObjectToHide.SetActive(true);

            InitializeChunkStructure();
            foreach (var kvp in chunks) { kvp.Value.currentLOD = -1; generateQueue.Add(kvp.Value); }
            hasStartedLoading = true;

            // ✅ ФИКС: включаем depth texture на камере — без этого вода не читает глубину
            if (mainCamera != null)
                mainCamera.depthTextureMode |= DepthTextureMode.Depth;

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

        void EnsureRuntimeResources()
        {
            if (runtimeResourcesReady)
                return;

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

            if (TreesEnabled() && treePrefab == null)
                treePrefab = CreateSimpleTreePrefab();
            if (mainCamera != null)
                mainCamera.depthTextureMode |= DepthTextureMode.Depth;

            runtimeResourcesReady = true;
        }

        void QueueAllChunksForRefresh()
        {
            EnsureChunkStructure();
            generateQueue.Clear();
            nextQueueSortTime = -999f;
            foreach (var kvp in chunks)
            {
                kvp.Value.currentLOD = -1;
                if (!generateQueue.Contains(kvp.Value))
                    generateQueue.Add(kvp.Value);
            }
            hasStartedLoading = true;
        }

        void EnsureChunkStructure()
        {
            if (chunkStructureInitialized)
                return;

            InitializeChunkStructure();
            chunkStructureInitialized = true;
        }

        bool TreesEnabled()
        {
            return treeProbability > 0.0001f && maxTreesPerChunk > 0;
        }

        void ApplyDarkRock()
        {
            // Камень — только тёмный: сохраняем оттенок, но сжимаем яркость.
            rockColor = Darken(rockColor, rockDarkness);
            mesaRockColor = Darken(mesaRockColor, rockDarkness);
        }

        static Color Darken(Color c, float amount01)
        {
            amount01 = Mathf.Clamp01(amount01);
            Color.RGBToHSV(c, out float h, out float s, out float v);
            // Сохраняем читаемость камня: затемняем умеренно, без ухода в почти чёрный.
            v = Mathf.Lerp(v, Mathf.Max(0.28f, v * 0.72f), amount01 * 0.82f);
            s = Mathf.Clamp01(s * Mathf.Lerp(1f, 1.10f, amount01));
            return Color.HSVToRGB(h, s, v);
        }

        void ApplySeedHueShift()
        {
            // Сдвиг оттенка (Hue) в диапазоне примерно ±0.18 вокруг исходного цвета.
            // Делает планеты визуально различимыми даже если случайные палитры похожи.
            float hShift = Mathf.Repeat((seed * 0.0001237f) + 0.37f, 1f);
            float delta = (hShift - 0.5f) * 0.36f;

            Color Shift(Color c)
            {
                Color.RGBToHSV(c, out float h, out float s, out float v);
                h = Mathf.Repeat(h + delta, 1f);
                // чуть усиливаем насыщенность, чтобы разница была заметней из космоса
                s = Mathf.Clamp01(s * 1.10f);
                Color shifted = Color.HSVToRGB(h, s, v);
                shifted.a = c.a;
                return shifted;
            }

            deepWaterColor = Shift(deepWaterColor);
            shallowWaterColor = Shift(shallowWaterColor);
            sandColor = Shift(sandColor);
            grassColor = Shift(grassColor);
            forestColor = Shift(forestColor);
            rockColor = Shift(rockColor);
            snowColor = Shift(snowColor);
            atmosphereColor = Shift(atmosphereColor);
            cloudColor = Shift(cloudColor);
            cloudShadowColor = Shift(cloudShadowColor);
        }

        void EnsureUniqueRuntimeMaterials()
        {
            // ВАЖНО: sharedMaterial/один и тот же asset-материал приводит к тому,
            // что параметры (цвета/векторы) перетираются у всех планет сразу.
            // Поэтому в Play Mode всегда работаем с инстансами материалов.
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
            if (terrainNormalMap != null || string.IsNullOrWhiteSpace(terrainNormalResourcePath))
                return;

            terrainNormalMap = Resources.Load<Texture2D>(terrainNormalResourcePath);
        }

        void ApplyBiomeTintToMaterial(Material mat)
        {
            if (mat == null) return;

            // 1) Базовый tint (на случай Standard/PBR материалов).
            Color tint = Color.Lerp(grassColor, forestColor, 0.35f);
            tint = Color.Lerp(tint, rockColor, 0.10f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", tint);

            // 2) Если используется кастомный шейдер с палитрой — прокидываем биомные цвета.
            // Делается безопасно: только если свойство реально существует.
            void SetIf(string prop, Color c)
            {
                if (mat.HasProperty(prop)) mat.SetColor(prop, c);
            }

            // Частые варианты имён свойств в планетных шейдерах:
            SetIf("_SandColor", sandColor);
            SetIf("_BeachColor", sandColor);
            SetIf("_GrassColor", grassColor);
            SetIf("_ForestColor", forestColor);
            SetIf("_RockColor", rockColor);
            SetIf("_SnowColor", snowColor);
            SetIf("_RimColor", atmosphereColor);

            // Вода/глубина иногда тоже часть планетного шейдера (на суше может влиять на прибрежную зону).
            SetIf("_ShallowColor", shallowWaterColor);
            SetIf("_DeepColor", deepWaterColor);

            EnsureSurfaceNormalMapLoaded();
            if (terrainNormalMap != null)
            {
                if (mat.HasProperty("_DetailNormalMap")) mat.SetTexture("_DetailNormalMap", terrainNormalMap);
                if (mat.HasProperty("_SurfaceNormalMap")) mat.SetTexture("_SurfaceNormalMap", terrainNormalMap);
            }

            if (mat.HasProperty("_DetailNormalTiling")) mat.SetFloat("_DetailNormalTiling", terrainNormalTiling);
            if (mat.HasProperty("_DetailNormalStrength")) mat.SetFloat("_DetailNormalStrength", terrainNormalStrength);
            if (mat.HasProperty("_DetailMicroNormalTiling")) mat.SetFloat("_DetailMicroNormalTiling", terrainMicroNormalTiling);
            if (mat.HasProperty("_DetailMicroNormalStrength")) mat.SetFloat("_DetailMicroNormalStrength", terrainMicroNormalStrength);
            if (mat.HasProperty("_DetailCavityStrength")) mat.SetFloat("_DetailCavityStrength", terrainCavityStrength);
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

        void UpdateShaderParams()
        {
            if (planetMaterial != null)
            {
                ApplyBiomeTintToMaterial(planetMaterial);
                planetMaterial.SetVector("_PlanetCenter", transform.position);
                planetMaterial.SetFloat("_PlanetRadius",  radius);
                float maxH = normalizedMaxHeight;
                planetMaterial.SetFloat("_MaxHeight",  maxH);
                planetMaterial.SetFloat("_WaterLevel", seaLevelHeight);
                planetMaterial.SetFloat("_SandLevel",  seaLevelHeight + beachWidthHeight);
                planetMaterial.SetFloat("_GrassLevel", normalizedMaxHeight * 0.2f);
                planetMaterial.SetFloat("_RockLevel",  normalizedMaxHeight * 0.55f);
                if (planetMaterial.HasProperty("_NormalBlend")) planetMaterial.SetFloat("_NormalBlend", 0.32f);
                if (planetMaterial.HasProperty("_WrapLighting")) planetMaterial.SetFloat("_WrapLighting", 0.42f);
                if (planetMaterial.HasProperty("_RimColor")) planetMaterial.SetColor("_RimColor", atmosphereColor);
                if (planetMaterial.HasProperty("_RimStrength")) planetMaterial.SetFloat("_RimStrength", 0.028f);
                if (planetMaterial.HasProperty("_RimPower")) planetMaterial.SetFloat("_RimPower", 4.2f);
                if (planetMaterial.HasProperty("_SaturationBoost")) planetMaterial.SetFloat("_SaturationBoost", 1.12f);
                if (planetMaterial.HasProperty("_ShadowTint")) planetMaterial.SetColor("_ShadowTint", new Color(0.46f, 0.46f, 0.52f, 1f));
            }
            UpdateOceanMaterialParams();
        }

        void UpdateObjectScales()
        {
            float s1 = 2f * (radius + 3f * heightMultiplier);
            if (firstObject  != null) firstObject.transform.localScale  = new Vector3(s1, s1, s1);
            float s2 = 2f * radius;
            if (secondObject != null) secondObject.transform.localScale = new Vector3(s2, s2, s2);
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

        void SyncDerivedSettings()
        {
            if (planetSettings == null) planetSettings = new PlanetSettings();
            if (noiseSettings == null) noiseSettings = new NoiseSettings();
            UpgradeSurfaceVisualDefaults();
            radius = Mathf.Max(100f, planetSettings.radius);
            planetSettings.radius = radius;
            normalizedMaxHeight = Mathf.Max(10f, radius * planetSettings.maxHeightRatio);
            heightMultiplier = normalizedMaxHeight;

            float seaLevel01 = Mathf.InverseLerp(0.002f, 0.05f, Mathf.Max(0.002f, planetSettings.seaLevelRatio));
            float seaLevelBase = Mathf.Lerp(-normalizedMaxHeight * 0.22f, -normalizedMaxHeight * 0.06f, seaLevel01);
            float oceanRadiusScale = Mathf.Clamp(oceanSurfaceRadiusScale <= 0.001f ? 0.97f : oceanSurfaceRadiusScale, 0.85f, 1.05f);
            float seaSurfaceRadius = (radius + seaLevelBase * Mathf.Clamp(seaLevelScale, 0.5f, 1.05f)) * oceanRadiusScale;
            seaLevelHeight = seaSurfaceRadius - radius;
            oceanLevel = seaLevelHeight;
            oceanDepth = -Mathf.Lerp(normalizedMaxHeight * 0.34f, normalizedMaxHeight * 0.60f, seaLevel01);

            beachWidthHeight = Mathf.Max(normalizedMaxHeight * 0.05f, normalizedMaxHeight * (planetSettings.beachWidthRatio * 3.5f));

            InvalidateSebPlateData();
            lock (heightCacheLock) heightCache.Clear();
        }

        void InvalidateSebPlateData()
        {
            lock (sebPlateDataLock)
            {
                sebPlateDataReady = false;
                sebPlateCenters = null;
                sebPlateMacroValues = null;
            }
        }

        void UpdateOceanMaterialParams()
        {
            if (oceanMaterial == null) return;

            float radius01 = Mathf.InverseLerp(1500f, 10000f, radius);
            float waveAmplitude = Mathf.Clamp(normalizedMaxHeight * 0.0048f, 0.12f, 1.65f);
            float waveFrequency = Mathf.Lerp(0.0060f, 0.0021f, radius01);
            float foamDistance = Mathf.Clamp(normalizedMaxHeight * 0.026f, 0.45f, 3.8f);
            float depthGradient = Mathf.Lerp(0.042f, 0.016f, radius01);

            if (oceanMaterial.HasProperty("_ShallowColor"))
                oceanMaterial.SetColor("_ShallowColor", new Color(shallowWaterColor.r, shallowWaterColor.g, shallowWaterColor.b, 0.72f));
            if (oceanMaterial.HasProperty("_DeepColor"))
                oceanMaterial.SetColor("_DeepColor", new Color(deepWaterColor.r, deepWaterColor.g, deepWaterColor.b, 0.86f));
            if (oceanMaterial.HasProperty("_NightShallowColor"))
                oceanMaterial.SetColor("_NightShallowColor", new Color(shallowWaterColor.r * 0.10f, shallowWaterColor.g * 0.09f, shallowWaterColor.b * 0.12f, 0.84f));
            if (oceanMaterial.HasProperty("_NightDeepColor"))
                oceanMaterial.SetColor("_NightDeepColor", new Color(deepWaterColor.r * 0.07f, deepWaterColor.g * 0.06f, deepWaterColor.b * 0.09f, 0.94f));
            if (oceanMaterial.HasProperty("_FoamColor"))
                oceanMaterial.SetColor("_FoamColor", Color.Lerp(Color.white, shallowWaterColor, 0.08f));

            if (oceanMaterial.HasProperty("_WaveAmplitude"))
                oceanMaterial.SetFloat("_WaveAmplitude", waveAmplitude);
            if (oceanMaterial.HasProperty("_WaveFrequency"))
                oceanMaterial.SetFloat("_WaveFrequency", waveFrequency);
            if (oceanMaterial.HasProperty("_WaveSpeed"))
                oceanMaterial.SetFloat("_WaveSpeed", Mathf.Lerp(0.42f, 0.68f, radius01));
            if (oceanMaterial.HasProperty("_FoamDistance"))
                oceanMaterial.SetFloat("_FoamDistance", foamDistance);
            if (oceanMaterial.HasProperty("_FoamAmount"))
                oceanMaterial.SetFloat("_FoamAmount", 0.24f);
            if (oceanMaterial.HasProperty("_FoamSpeed"))
                oceanMaterial.SetFloat("_FoamSpeed", Mathf.Lerp(0.20f, 0.48f, 1f - radius01));
            if (oceanMaterial.HasProperty("_DepthGradient"))
                oceanMaterial.SetFloat("_DepthGradient", depthGradient);
            if (oceanMaterial.HasProperty("_Smoothness"))
                oceanMaterial.SetFloat("_Smoothness", 0.74f);
            if (oceanMaterial.HasProperty("_Transparency"))
                oceanMaterial.SetFloat("_Transparency", 0.52f);
            if (oceanMaterial.HasProperty("_FresnelPower"))
                oceanMaterial.SetFloat("_FresnelPower", 4.2f);
            if (oceanMaterial.HasProperty("_SparkleStrength"))
                oceanMaterial.SetFloat("_SparkleStrength", 0.08f);
            if (oceanMaterial.HasProperty("_Distortion"))
                oceanMaterial.SetFloat("_Distortion", 0.0025f);
            if (oceanMaterial.HasProperty("_NightAmbient"))
                oceanMaterial.SetFloat("_NightAmbient", 0.012f);
            if (oceanMaterial.HasProperty("_HorizonSoftness"))
                oceanMaterial.SetFloat("_HorizonSoftness", 4.0f);
            if (oceanMaterial.HasProperty("_SpecularStrength"))
                oceanMaterial.SetFloat("_SpecularStrength", 0.88f);
            if (oceanMaterial.HasProperty("_WaveChop"))
                oceanMaterial.SetFloat("_WaveChop", Mathf.Lerp(0.42f, 0.68f, 1f - radius01));
            if (oceanMaterial.HasProperty("_DeepAbsorption"))
                oceanMaterial.SetFloat("_DeepAbsorption", Mathf.Lerp(0.58f, 0.82f, radius01));
            if (oceanMaterial.HasProperty("_FoamSharpness"))
                oceanMaterial.SetFloat("_FoamSharpness", 0.72f);
            if (oceanMaterial.HasProperty("_EdgeFade"))
                oceanMaterial.SetFloat("_EdgeFade", 0.18f);

            UpdateOceanRuntimeParams();
        }

        void UpdateOceanRuntimeParams()
        {
            if (oceanMaterial == null) return;

            if (oceanMaterial.HasProperty("_PlanetCenter"))
                oceanMaterial.SetVector("_PlanetCenter", transform.position);
            if (oceanMaterial.HasProperty("_PlanetRadius"))
                oceanMaterial.SetFloat("_PlanetRadius", radius);
            if (oceanMaterial.HasProperty("_WaterLevel"))
                oceanMaterial.SetFloat("_WaterLevel", radius + oceanLevel);
        }

        void UpgradeSurfaceVisualDefaults()
        {
            if (terrainNormalTiling <= 0.001f || Mathf.Approximately(terrainNormalTiling, 24f))
                terrainNormalTiling = 48f;
            if (terrainMicroNormalTiling <= 0.001f || Mathf.Approximately(terrainMicroNormalTiling, 82f))
                terrainMicroNormalTiling = 164f;
            if (terrainNormalStrength <= 0.001f || Mathf.Approximately(terrainNormalStrength, 0.72f))
                terrainNormalStrength = 0.92f;
            if (terrainMicroNormalStrength <= 0.001f || Mathf.Approximately(terrainMicroNormalStrength, 0.34f))
                terrainMicroNormalStrength = 0.28f;
            if (terrainCavityStrength <= 0.001f)
                terrainCavityStrength = 0.82f;
            if (oceanSurfaceRadiusScale <= 0.001f)
                oceanSurfaceRadiusScale = 0.97f;
        }

        // ── Chunk infrastructure ─────────────────────────────────────────────
        GameObject CreateSimpleTreePrefab()
        {
            var tree  = new GameObject("SimpleTree");
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.transform.parent        = tree.transform;
            trunk.transform.localPosition = new Vector3(0, 1f, 0);
            trunk.transform.localScale    = new Vector3(0.2f, 1f, 0.2f);
            var wm = new Material(Shader.Find("Standard"));
            wm.color = new Color(0.35f, 0.18f, 0.08f);
            trunk.GetComponent<MeshRenderer>().sharedMaterial = wm;
            for (int i = 0; i < 3; i++)
            {
                var f = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                f.transform.parent        = tree.transform;
                f.transform.localPosition = new Vector3(0, 2f + i * 0.6f, 0);
                float s = 1.1f - i * 0.25f;
                f.transform.localScale    = new Vector3(s, s, s);
                var lm = new Material(Shader.Find("Standard"));
                lm.color = new Color(0.08f, 0.38f, 0.10f);
                f.GetComponent<MeshRenderer>().sharedMaterial = lm;
            }
            return tree;
        }

        void InitializeChunkStructure()
        {
            if (chunkStructureInitialized)
                return;

            for (int f = 0; f < 6; f++)
                for (int y = 0; y < chunksPerFace; y++)
                    for (int x = 0; x < chunksPerFace; x++)
                    {
                        var key = new Vector3Int(f, x, y);
                        float cs = 2f / chunksPerFace;
                        float u  = -1f + (x + 0.5f) * cs;
                        float v  = -1f + (y + 0.5f) * cs;
                        chunks[key] = new ChunkData
                        {
                            face = (Face)f, x = x, y = y,
                            centerWorldPos = GetCubePoint((Face)f, u, v).normalized * radius
                        };
                    }
            BuildChunkNeighbors();
            chunkStructureInitialized = true;
        }

        GameObject EnsureChunkObject(ChunkData chunk)
        {
            if (chunk == null)
                return null;

            if (chunk.obj != null)
                return chunk.obj;

            chunk.obj = new GameObject($"Chunk_F{(int)chunk.face}_{chunk.x}_{chunk.y}");
            chunk.obj.transform.SetParent(transform, false);
            chunk.obj.transform.localPosition = Vector3.zero;
            chunk.obj.SetActive(false);
            return chunk.obj;
        }

        void ReleaseChunkResources(ChunkData chunk)
        {
            if (chunk == null)
                return;

            RecycleTrees(chunk);
            chunk.treePool.Clear();

            if (chunk.obj != null)
            {
                Destroy(chunk.obj);
                chunk.obj = null;
            }

            if (chunk.mesh != null)
            {
                Destroy(chunk.mesh);
                chunk.mesh = null;
            }
        }

        IEnumerator UpdateChunksLoop()
        {
            var wait = new WaitForSeconds(updateInterval);
            while (streamingActive)
            {
                if (player == null) { yield return wait; continue; }
                Vector3 pp = player.position;
                float colliderSafetyDistance = GetColliderSafetyDistance();
                foreach (var kvp in chunks)
                {
                    var cd  = kvp.Value;
                    var cwp = transform.TransformPoint(cd.centerWorldPos);
                    float d = Vector3.Distance(pp, cwp);
                    if (d > unloadDistance)
                    {
                        DeactivateChunk(cd, releaseResources: true);
                        continue;
                    }

                    if (IsChunkBacksideCulled(cd, pp))
                    {
                        DeactivateChunk(cd, releaseResources: false);
                        continue;
                    }

                    int tl = GetTargetLOD(cd, d, cwp);
                    if (tl < 0) continue;
                    if (tl != cd.currentLOD && !cd.isGenerating && !generateQueue.Contains(cd))
                    { generateQueue.Add(cd); hasStartedLoading = true; }
                    if (useColliders && cd.obj != null)
                    {
                        var mc = cd.obj.GetComponent<MeshCollider>();
                        bool nc = d < colliderSafetyDistance;
                        if (mc != null && mc.enabled != nc) mc.enabled = nc;
                    }
                }
                yield return wait;
            }
            updateChunksCoroutine = null;
        }

        IEnumerator GenerateChunksLoop()
        {
            while (streamingActive)
            {
                if (generateQueue.Count > 0 && player != null)
                {
                    Vector3 pp = player.position;
                    float safetyDistance = GetSurfaceSafetyDistance();
                    if (generateQueue.Count > 1 && Time.time >= nextQueueSortTime)
                    {
                        nextQueueSortTime = Time.time + 0.08f;
                        generateQueue.Sort((a, b) =>
                        {
                            float da = (transform.TransformPoint(a.centerWorldPos) - pp).sqrMagnitude;
                            float db = (transform.TransformPoint(b.centerWorldPos) - pp).sqrMagnitude;
                            float sa = Mathf.Sqrt(da);
                            float sb = Mathf.Sqrt(db);
                            float span = Mathf.Max(1f, GetChunkWorldSpan());
                            float ra = sa / span;
                            float rb = sb / span;
                            bool aCritical = da <= safetyDistance * safetyDistance;
                            bool bCritical = db <= safetyDistance * safetyDistance;
                            if (aCritical != bCritical)
                                return aCritical ? -1 : 1;

                            bool aMissingMesh = !ChunkHasBuiltMesh(a);
                            bool bMissingMesh = !ChunkHasBuiltMesh(b);
                            if (aMissingMesh != bMissingMesh)
                                return aMissingMesh ? -1 : 1;

                            int ratioCmp = ra.CompareTo(rb);
                            if (ratioCmp != 0)
                                return ratioCmp;

                            return da.CompareTo(db);
                        });
                    }
                    int idx = 0;
                    while (idx < generateQueue.Count && activeGenerations < maxConcurrentGenerations)
                    {
                        var cd = generateQueue[idx];
                        if (cd == null || cd.isGenerating) { idx++; continue; }
                        float d = Vector3.Distance(pp, transform.TransformPoint(cd.centerWorldPos));
                        int tl  = GetTargetLOD(cd, d, transform.TransformPoint(cd.centerWorldPos));
                        if (tl < 0 || tl == cd.currentLOD) { generateQueue.RemoveAt(idx); continue; }
                        generateQueue.RemoveAt(idx);
                        StartCoroutine(GenerateChunkMesh(cd, tl));
                    }
                }
                if (generateQueue.Count == 0 && activeGenerations == 0 && hasStartedLoading && !initialLoadComplete)
                {
                    initialLoadComplete = true;
                    if (loadingPanel != null) loadingPanel.SetActive(false);
                    if (extraObjectToHide != null) extraObjectToHide.SetActive(false);
                }
                yield return null;
            }
            generateChunksCoroutine = null;
        }

        int GetLODForDistance(float d)
        {
            if (d > unloadDistance) return -1;
            if (d < lodDistance0)   return 0;
            if (d < lodDistance1)   return 1;
            if (d < lodDistance2)   return 2;
            if (d < lodDistance3)   return 3;
            return -1;
        }

        int GetVertsForLOD(int lod)
        {
            int maxLod = Mathf.Max(0, lodLevels - 1);
            int minHighDetailVerts = terrainStyle == TerrainStyle.SebInspired ? 192 : 160;
            int highDetailVerts = Mathf.Max(minHighDetailVerts, maxVertsPerChunk);
            int lodDivisor = 1 << Mathf.Clamp(maxLod, 0, 6);

            // Keep every LOD edge vertex aligned to the next one.
            // Non-divisible resolutions were producing different edge samples and visible chunk trenches.
            highDetailVerts = Mathf.CeilToInt(highDetailVerts / (float)lodDivisor) * lodDivisor;

            int clampedLod = Mathf.Clamp(lod, 0, maxLod);
            int verts = highDetailVerts / (1 << clampedLod);
            return Mathf.Max(12, verts);
        }

        void AddNeighborKey(ChunkData chunk, Vector3Int key)
        {
            if (chunk == null || !chunks.ContainsKey(key) || chunk.neighborKeys.Contains(key))
                return;

            chunk.neighborKeys.Add(key);
        }

        Vector3 GetChunkCenterDirection(Face face, int x, int y)
        {
            float cs = 2f / chunksPerFace;
            float u = -1f + (x + 0.5f) * cs;
            float v = -1f + (y + 0.5f) * cs;
            return GetCubePoint(face, u, v).normalized;
        }

        void BuildChunkNeighbors()
        {
            float cs = 2f / chunksPerFace;
            float outsideOffset = cs * 0.55f;
            foreach (var kvp in chunks)
            {
                var chunk = kvp.Value;
                if (chunk == null)
                    continue;

                chunk.neighborKeys.Clear();

                Vector3Int[] sameFaceKeys =
                {
                    new Vector3Int((int)chunk.face, chunk.x - 1, chunk.y),
                    new Vector3Int((int)chunk.face, chunk.x + 1, chunk.y),
                    new Vector3Int((int)chunk.face, chunk.x, chunk.y - 1),
                    new Vector3Int((int)chunk.face, chunk.x, chunk.y + 1)
                };

                foreach (var key in sameFaceKeys)
                    AddNeighborKey(chunk, key);

                float startU = -1f + chunk.x * cs;
                float startV = -1f + chunk.y * cs;
                float midU = startU + cs * 0.5f;
                float midV = startV + cs * 0.5f;

                void AddClosestCrossFaceNeighbor(Vector3 direction)
                {
                    float bestDot = -1f;
                    Vector3Int bestKey = default;
                    bool found = false;
                    foreach (var otherKvp in chunks)
                    {
                        if (otherKvp.Value == null || otherKvp.Value.face == chunk.face)
                            continue;

                        float dot = Vector3.Dot(otherKvp.Value.centerWorldPos.normalized, direction);
                        if (!found || dot > bestDot)
                        {
                            found = true;
                            bestDot = dot;
                            bestKey = otherKvp.Key;
                        }
                    }

                    if (found)
                        AddNeighborKey(chunk, bestKey);
                }

                if (chunk.x == 0)
                    AddClosestCrossFaceNeighbor(GetCubePoint(chunk.face, startU - outsideOffset, midV).normalized);
                if (chunk.x == chunksPerFace - 1)
                    AddClosestCrossFaceNeighbor(GetCubePoint(chunk.face, startU + cs + outsideOffset, midV).normalized);
                if (chunk.y == 0)
                    AddClosestCrossFaceNeighbor(GetCubePoint(chunk.face, midU, startV - outsideOffset).normalized);
                if (chunk.y == chunksPerFace - 1)
                    AddClosestCrossFaceNeighbor(GetCubePoint(chunk.face, midU, startV + cs + outsideOffset).normalized);
            }
        }

        IEnumerable<ChunkData> EnumerateNeighbors(ChunkData chunk)
        {
            if (chunk == null) yield break;

            foreach (var key in chunk.neighborKeys)
                if (chunks.TryGetValue(key, out var neighbor))
                    yield return neighbor;
        }

        int GetTargetLOD(ChunkData chunk, float distance, Vector3 worldPos)
        {
            if (player != null && IsChunkBacksideCulled(chunk, player.position))
                return -1;

            int maxLod = Mathf.Max(0, lodLevels - 1);
            if (maxLod == 0)
                return 0;

            float surfaceDistance = distance;
            float chunkSpan = Mathf.Max(1f, GetChunkWorldSpan());
            float detailRatio = surfaceDistance / chunkSpan;
            int target;
            if (activeLodSystem)
            {
                float lod0RadiusChunks = Mathf.Max(0f, lod0RadiusInChunks);
                if (detailRatio <= lod0RadiusChunks)
                {
                    target = 0;
                }
                else
                {
                    float farRatio = Mathf.Max(lod0RadiusChunks + 0.01f, unloadDistance / chunkSpan);
                    float t = Mathf.Clamp01((detailRatio - lod0RadiusChunks) / Mathf.Max(0.01f, farRatio - lod0RadiusChunks));
                    float curveT = lodTransitionCurve != null && lodTransitionCurve.length > 0
                        ? Mathf.Clamp01(lodTransitionCurve.Evaluate(t))
                        : t;
                    target = Mathf.Clamp(Mathf.CeilToInt(curveT * maxLod), 1, maxLod);
                }
            }
            else
            {
                target = Mathf.Clamp(Mathf.FloorToInt(detailRatio), 0, maxLod);
            }
            float safetyDistance = GetSurfaceSafetyDistance();
            bool inSafetyBand = distance <= safetyDistance;

            if (activeLodSystem && detailRatio < Mathf.Max(0.5f, lod0RadiusInChunks))
            {
                target = 0;
            }
            else if (!activeLodSystem && detailRatio < 10f)
            {
                int midLod = 1 + Mathf.FloorToInt((detailRatio - 3f) / 3.5f);
                target = Mathf.Clamp(midLod, 1, Mathf.Min(maxLod, 2));
            }

            if (!inSafetyBand && target <= 1 && !IsChunkInFrustum(worldPos))
                target = Mathf.Min(maxLod, target + 1);

            foreach (var neighbor in EnumerateNeighbors(chunk))
            {
                if (neighbor == null || neighbor.currentLOD < 0)
                    continue;

                if (terrainStyle == TerrainStyle.SebInspired && target <= 1 && neighbor.currentLOD <= 1)
                    target = Mathf.Min(target, neighbor.currentLOD);
                else
                    target = Mathf.Min(target, neighbor.currentLOD + 1);
            }

            if (inSafetyBand)
                target = terrainStyle == TerrainStyle.SebInspired ? 0 : Mathf.Min(target, Mathf.Min(1, maxLod));

            return target;
        }

        bool IsChunkInFrustum(Vector3 wp)
        {
            if (mainCamera == null) return true;
            Vector3 vp = mainCamera.WorldToViewportPoint(wp);
            return vp.z > 0f && vp.x > -0.3f && vp.x < 1.3f && vp.y > -0.3f && vp.y < 1.3f;
        }

        // ── Chunk mesh generation ────────────────────────────────────────────
        IEnumerator GenerateChunkMesh(ChunkData chunk, int lod)
        {
            int buildVersion = streamingVersion;
            bool sebStyle = terrainStyle == TerrainStyle.SebInspired;
            chunk.isGenerating = true;
            activeGenerations++;
            try
            {
                RecycleTrees(chunk);

                int vpe   = GetVertsForLOD(lod);
                int vEdge = vpe + 1;
                int sampleLod = Mathf.Max(0, lod - 1);
                int sampleVpe = Mathf.Max(vpe, GetVertsForLOD(sampleLod));
                int sampleStep = sampleVpe % Mathf.Max(1, vpe) == 0
                    ? Mathf.Max(1, sampleVpe / Mathf.Max(1, vpe))
                    : 1;
                if (sampleStep == 1)
                    sampleVpe = vpe;

                int samplePadding = sampleStep;
                int sampleVEdge = sampleVpe + 1;
                int sampledWidth = sampleVEdge + samplePadding * 2;

                var vertices  = new List<Vector3>(vEdge * vEdge);
                var uvs       = new List<Vector2>(vEdge * vEdge);
                var colors    = new List<Color>(vEdge * vEdge);
                var triangles = new List<int>(vpe * vpe * 6);

                float cs     = 2f / chunksPerFace;
                float startU = -1f + chunk.x * cs;
                float startV = -1f + chunk.y * cs;
                float cellSize = Mathf.Max(1f, radius * cs / Mathf.Max(1, vpe));
                float sampleCellSize = Mathf.Max(1f, radius * cs / Mathf.Max(1, sampleVpe));

                var sampledHeightMap   = new float[sampledWidth, sampledWidth];
                var sampledSpherePoints = new Vector3[sampledWidth, sampledWidth];
                var paddedHeightMap    = new float[vEdge + 2, vEdge + 2];
                var paddedSpherePoints = new Vector3[vEdge + 2, vEdge + 2];
                var heightMap          = new float[vEdge, vEdge];
                var spherePoints       = new Vector3[vEdge, vEdge];
                var positions          = new Vector3[vEdge, vEdge];
                var buildTask = Task.Run(() =>
                {
                    for (int py = 0; py < sampledWidth; py++)
                    {
                        int sy = py - samplePadding;
                        for (int px = 0; px < sampledWidth; px++)
                        {
                            int sx = px - samplePadding;
                            float u = startU + (float)sx / sampleVpe * cs;
                            float v = startV + (float)sy / sampleVpe * cs;
                            Vector3 sp = GetCubePoint(chunk.face, u, v).normalized;

                            sampledSpherePoints[px, py] = sp;
                            sampledHeightMap[px, py] = EvaluateMacroHeightCached(sp);
                        }
                    }
                });
                while (!buildTask.IsCompleted) yield return null;
                if (buildTask.IsFaulted)
                {
                    Debug.LogException(buildTask.Exception);
                    yield break;
                }

                if (sebStyle)
                {
                    RelaxSebHeightmap(sampledHeightMap, sampleCellSize);
                }
                else
                {
                    SmoothHeightmap(sampledHeightMap, sampleCellSize, terrainSmoothing);
                    LimitSlope(sampledHeightMap, sampleCellSize);
                    ApplyErosion(sampledHeightMap, sampleCellSize);
                    ClampSpikeHeights(sampledHeightMap, sampleCellSize);
                }

                for (int py = 0; py < vEdge + 2; py++)
                {
                    int sampledPy = py * sampleStep;
                    for (int px = 0; px < vEdge + 2; px++)
                    {
                        int sampledPx = px * sampleStep;
                        paddedSpherePoints[px, py] = sampledSpherePoints[sampledPx, sampledPy];
                        paddedHeightMap[px, py] = sampledHeightMap[sampledPx, sampledPy];
                    }
                }

                for (int y = 0; y < vEdge; y++)
                {
                    for (int x = 0; x < vEdge; x++)
                    {
                        spherePoints[x, y] = paddedSpherePoints[x + 1, y + 1];
                        heightMap[x, y] = ClampTerrainHeight(paddedHeightMap[x + 1, y + 1]);
                        positions[x, y] = BuildStableSurfacePosition(spherePoints[x, y], heightMap[x, y]);
                    }
                }

                for (int y = 0; y < vEdge; y++)
                    for (int x = 0; x < vEdge; x++)
                    {
                        vertices.Add(positions[x, y]);
                        uvs.Add(new Vector2((float)x / vpe, (float)y / vpe));
                    }

                float localMinH = float.MaxValue;
                float localMaxH = float.MinValue;
                for (int y = 0; y < vEdge; y++)
                    for (int x = 0; x < vEdge; x++)
                    {
                        float h = heightMap[x, y];
                        if (h < localMinH) localMinH = h;
                        if (h > localMaxH) localMaxH = h;
                    }
                if (localMinH < observedMinHeight) observedMinHeight = localMinH;
                if (localMaxH > observedMaxHeight) observedMaxHeight = localMaxH;
                if (Time.time - lastHeightLogTime > 2.5f)
                {
                    lastHeightLogTime = Time.time;
                    Debug.Log($"[PlanetGenerator] Height range (global): min={observedMinHeight:F1}, max={observedMaxHeight:F1}, radius={radius:F1}");
                }

                for (int y = 0; y < vpe; y++)
                    for (int x = 0; x < vpe; x++)
                    {
                        int i0 = y * vEdge + x, i1 = i0 + 1, i2 = i0 + vEdge, i3 = i2 + 1;
                        float diag03 = Mathf.Abs(heightMap[x, y] - heightMap[x + 1, y + 1]);
                        float diag12 = Mathf.Abs(heightMap[x + 1, y] - heightMap[x, y + 1]);
                        bool useAltDiag = sebStyle ? false : diag03 < diag12;

                        if (!sebStyle && Mathf.Abs(diag03 - diag12) < normalizedMaxHeight * 0.0006f)
                            useAltDiag = ((x + y) & 1) == 0;

                        if (useAltDiag)
                        {
                            triangles.Add(i0); triangles.Add(i1); triangles.Add(i3);
                            triangles.Add(i0); triangles.Add(i3); triangles.Add(i2);
                        }
                        else
                        {
                            triangles.Add(i0); triangles.Add(i1); triangles.Add(i2);
                            triangles.Add(i1); triangles.Add(i3); triangles.Add(i2);
                        }
                    }

                var normals = new List<Vector3>(vEdge * vEdge);
                var macroNormals = new List<Vector3>(vEdge * vEdge);
                for (int i = 0; i < vEdge * vEdge; i++)
                {
                    normals.Add(Vector3.up);
                    macroNormals.Add(Vector3.up);
                }
                bool highQualityNormals = lod <= 1;

                for (int y = 0; y < vEdge; y++)
                    for (int x = 0; x < vEdge; x++)
                    {
                        int idx = y * vEdge + x;
                        Vector3 gridNormal = ComputeGridNormal(paddedSpherePoints, paddedHeightMap, x + 1, y + 1);
                        Vector3 n = sebStyle
                            ? gridNormal
                            : (highQualityNormals
                                ? AverageNormals(ComputeSurfaceNormal(spherePoints[x, y], heightMap[x, y], cellSize), gridNormal)
                                : gridNormal);
                        macroNormals[idx] = n;
                        normals[idx] = ApplyMicroNormal(spherePoints[x, y], n, heightMap[x, y], lod);
                    }

                if (Time.time - lastSlopeLogTime > 3f)
                {
                    int under30 = 0;
                    int under45 = 0;
                    int total = vEdge * vEdge;
                    for (int y = 0; y < vEdge; y++)
                        for (int x = 0; x < vEdge; x++)
                        {
                            int idx = y * vEdge + x;
                            float slopeAngle = Vector3.Angle(macroNormals[idx], spherePoints[x, y]);
                            if (slopeAngle < 30f) under30++;
                            if (slopeAngle < 45f) under45++;
                        }
                    float p30 = total > 0 ? (under30 * 100f / total) : 0f;
                    float p45 = total > 0 ? (under45 * 100f / total) : 0f;
                    lastSlopeLogTime = Time.time;
                    Debug.Log($"[PlanetGenerator] Slope distribution: <30deg={p30:F1}% <45deg={p45:F1}%");
                }

                for (int y = 0; y < vEdge; y++)
                {
                    for (int x = 0; x < vEdge; x++)
                    {
                        int idx  = y * vEdge + x;
                        float h  = heightMap[x, y];
                        float sl = 1f - Mathf.Clamp01(Vector3.Dot(macroNormals[idx], spherePoints[x, y]));
                        var bc   = GetBiomeColor(spherePoints[x, y], h);
                        float slope01 = Mathf.Clamp01(sl);
                        float altitude01 = Mathf.InverseLerp(seaLevelHeight + beachWidthHeight, normalizedMaxHeight, h);
                        float slopeRockMask = Mathf.SmoothStep(0.10f, 0.30f, slope01);
                        float mountainMask = Mathf.SmoothStep(0.18f, 0.82f, altitude01);
                        float peakCap = 1f - Mathf.SmoothStep(0.84f, 0.98f, altitude01) * (1f - Mathf.SmoothStep(0.24f, 0.48f, slope01));
                        float rockBlend = Mathf.Clamp01(slopeRockMask * mountainMask * peakCap * (0.35f + Mathf.Clamp01(slopeRockBlend) * 2.4f));
                        Color sideRockColor = Color.Lerp(rockColor, mesaRockColor, Mathf.SmoothStep(0.58f, 0.92f, altitude01) * 0.16f);
                        var wsl  = Color.Lerp(bc, sideRockColor, rockBlend);
                        float ao = CalculateAO(heightMap, x, y, vEdge, h);
                        colors.Add(wsl * (1f - ao * ambientOcclusion));
                    }
                }
                AddSkirts(lod, vpe, vEdge, vertices, triangles, normals, colors, uvs);

                if (chunk.mesh == null) chunk.mesh = new Mesh();
                else chunk.mesh.Clear();
                chunk.mesh.name = $"Chunk_{chunk.face}_{chunk.x}_{chunk.y}_LOD{lod}";
                if (vertices.Count >= 65535) chunk.mesh.indexFormat = IndexFormat.UInt32;
                chunk.mesh.SetVertices(vertices);
                chunk.mesh.SetUVs(0, uvs);
                chunk.mesh.SetColors(colors);
                chunk.mesh.SetTriangles(triangles, 0);
                chunk.mesh.SetNormals(normals);
                chunk.mesh.RecalculateBounds();

                var chunkObject = EnsureChunkObject(chunk);
                var mf = chunkObject.GetComponent<MeshFilter>();
                if (mf == null) mf = chunkObject.AddComponent<MeshFilter>();
                var mr = chunkObject.GetComponent<MeshRenderer>();
                if (mr == null) mr = chunkObject.AddComponent<MeshRenderer>();
                mf.sharedMesh     = chunk.mesh;
                mr.sharedMaterial = planetMaterial;

                if (useColliders)
                {
                    var mc = chunkObject.GetComponent<MeshCollider>();
                    if (mc == null) mc = chunkObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = chunk.mesh;
                    mc.convex     = false;
                    mc.enabled = player != null &&
                        Vector3.Distance(player.position, transform.TransformPoint(chunk.centerWorldPos)) < GetColliderSafetyDistance();
                }

                bool chunkStillVisible = player == null || !IsChunkBacksideCulled(chunk, player.position);
                bool streamingStillValid = streamingActive && buildVersion == streamingVersion && chunkStillVisible;
                chunkObject.SetActive(streamingStillValid);

                if (streamingStillValid && lod == 0 && TreesEnabled() && treePrefab != null)
                {
                    Random.InitState(seed + (int)chunk.face * 10000 + chunk.x * 100 + chunk.y);
                    yield return StartCoroutine(SpawnTreesForChunk(chunk, heightMap, spherePoints, positions, normals.ToArray(), vEdge));
                }

                chunk.currentLOD = streamingStillValid ? lod : -1;
                if (!streamingStillValid)
                    ReleaseChunkResources(chunk);

                foreach (var neighbor in EnumerateNeighbors(chunk))
                {
                    if (neighbor == null || neighbor.currentLOD < 0 || neighbor.isGenerating)
                        continue;

                    if (Mathf.Abs(neighbor.currentLOD - chunk.currentLOD) > 1 && !generateQueue.Contains(neighbor))
                        generateQueue.Add(neighbor);
                }
            }
            finally
            {
                chunk.isGenerating = false;
                activeGenerations = Mathf.Max(0, activeGenerations - 1);
            }
            yield return null;
        }

        void RecycleTrees(ChunkData chunk)
        {
            if (chunk == null) return;

            for (int i = 0; i < chunk.treePool.Count; i++)
            {
                var pooledTree = chunk.treePool[i];
                if (pooledTree != null)
                    pooledTree.SetActive(false);
            }
        }

        GameObject GetPooledTree(ChunkData chunk, int treeIndex)
        {
            if (chunk == null || treePrefab == null)
                return null;

            var chunkObject = EnsureChunkObject(chunk);
            if (chunkObject == null)
                return null;

            for (int i = 0; i < chunk.treePool.Count; i++)
            {
                var pooledTree = chunk.treePool[i];
                if (pooledTree != null && !pooledTree.activeSelf)
                {
                    pooledTree.name = "TreeInstance_" + treeIndex;
                    pooledTree.SetActive(true);
                    return pooledTree;
                }
            }

            var createdTree = Instantiate(treePrefab, chunkObject.transform, false);
            createdTree.name = "TreeInstance_" + treeIndex;
            chunk.treePool.Add(createdTree);
            return createdTree;
        }

        IEnumerator SpawnTreesForChunk(ChunkData chunk, float[,] hm,
            Vector3[,] sp, Vector3[,] pos, Vector3[] normals, int vEdge)
        {
            int count = 0;
            for (int y = 1; y < vEdge - 1; y += 2)
                for (int x = 1; x < vEdge - 1; x += 2)
                {
                    if (hm[x, y] <= oceanLevel) continue;
                    int idx   = y * vEdge + x;
                    float sl  = 1f - Vector3.Dot(normals[idx], sp[x, y].normalized);
                    if (sl > maxTreeSlope) continue;

                    float forestDensity = EvaluateForestDensity(sp[x, y], hm[x, y]);
                    if (forestDensity <= 0.16f) continue;

                    float spawnChance = Mathf.Clamp01(treeProbability * Mathf.Lerp(0.35f, 3.1f, forestDensity));
                    if (Random.value > spawnChance) continue;

                    var t = GetPooledTree(chunk, count);
                    if (t == null) continue;
                    float s = Random.Range(0.8f, 1.3f);
                    PlaceTreeOnSurface(t, pos[x, y], normals[idx], s, Random.Range(0f, 360f));
                    ApplyTreeShader(t);
                    count++;
                    if (count >= maxTreesPerChunk) yield break;
                    if (count % 20 == 0) yield return null;
                }
        }

        void PlaceTreeOnSurface(GameObject tree, Vector3 localSurfacePos, Vector3 localSurfaceNormal, float uniformScale, float spinDegrees)
        {
            Vector3 safeNormal = localSurfaceNormal.sqrMagnitude > 1e-6f ? localSurfaceNormal.normalized : localSurfacePos.normalized;
            Transform treeTransform = tree.transform;

            treeTransform.localRotation =
                Quaternion.FromToRotation(Vector3.up, safeNormal) *
                Quaternion.AngleAxis(spinDegrees, Vector3.up);
            treeTransform.localScale = Vector3.one * uniformScale;

            Vector3 worldSurfacePos = transform.TransformPoint(localSurfacePos);
            Vector3 worldSurfaceNormal = transform.TransformDirection(safeNormal).normalized;

            treeTransform.position = worldSurfacePos;
            float bottomOffset = CalculateBottomOffsetAlongNormal(tree, worldSurfacePos, worldSurfaceNormal);
            treeTransform.position = worldSurfacePos + worldSurfaceNormal * (bottomOffset + treeSurfaceOffset);
        }

        float CalculateBottomOffsetAlongNormal(GameObject obj, Vector3 surfacePointWorld, Vector3 surfaceNormalWorld)
        {
            float minProjection = float.MaxValue;
            bool foundAny = false;

            foreach (var mf in obj.GetComponentsInChildren<MeshFilter>())
            {
                if (mf == null || mf.sharedMesh == null) continue;

                Bounds b = mf.sharedMesh.bounds;
                Vector3 c = b.center;
                Vector3 e = b.extents;
                Transform tr = mf.transform;

                for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 localCorner = c + Vector3.Scale(e, new Vector3(sx, sy, sz));
                    Vector3 worldCorner = tr.TransformPoint(localCorner);
                    float projection = Vector3.Dot(worldCorner - surfacePointWorld, surfaceNormalWorld);
                    if (projection < minProjection) minProjection = projection;
                    foundAny = true;
                }
            }

            if (!foundAny || minProjection >= 0f)
                return 0f;

            return -minProjection;
        }

        void ApplyTreeShader(GameObject tree)
        {
            if (treeShader == null) treeShader = Shader.Find("Custom/PlanetTreeShader");
            if (treeShader == null) return;
            foreach (var mr in tree.GetComponentsInChildren<MeshRenderer>())
            {
                Color c = mr.sharedMaterial != null ? mr.sharedMaterial.color : Color.white;
                var mat = new Material(treeShader);
                mat.color = c;
                if (mat.HasProperty("_PlanetCenter"))
                    mat.SetVector("_PlanetCenter", transform.position);
                mr.sharedMaterial = mat;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  NMS UBER NOISE ENGINE
        // ════════════════════════════════════════════════════════════════════

        static double Hermite01(double t)
        {
            return t * t * (3.0 - 2.0 * t);
        }

        static double LerpDouble(double a, double b, double t)
        {
            return a + (b - a) * t;
        }

        static double Hash01(int x, int y, int z)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)x) * 16777619u;
                h = (h ^ (uint)y) * 16777619u;
                h = (h ^ (uint)z) * 16777619u;
                h ^= h >> 13;
                h *= 1274126177u;
                h ^= h >> 16;
                return (h & 0x00FFFFFFu) / 16777215.0;
            }
        }

        static float HashSigned(int x, int y, int z)
        {
            return (float)(Hash01(x, y, z) * 2.0 - 1.0);
        }

        double ValueNoise3D(Vector3 p)
        {
            double x = p.x;
            double y = p.y;
            double z = p.z;

            int ix = (int)System.Math.Floor(x);
            int iy = (int)System.Math.Floor(y);
            int iz = (int)System.Math.Floor(z);

            double fx = Hermite01(x - ix);
            double fy = Hermite01(y - iy);
            double fz = Hermite01(z - iz);

            double n000 = Hash01(ix, iy, iz);
            double n100 = Hash01(ix + 1, iy, iz);
            double n010 = Hash01(ix, iy + 1, iz);
            double n110 = Hash01(ix + 1, iy + 1, iz);
            double n001 = Hash01(ix, iy, iz + 1);
            double n101 = Hash01(ix + 1, iy, iz + 1);
            double n011 = Hash01(ix, iy + 1, iz + 1);
            double n111 = Hash01(ix + 1, iy + 1, iz + 1);

            double nx00 = LerpDouble(n000, n100, fx);
            double nx10 = LerpDouble(n010, n110, fx);
            double nx01 = LerpDouble(n001, n101, fx);
            double nx11 = LerpDouble(n011, n111, fx);
            double nxy0 = LerpDouble(nx00, nx10, fy);
            double nxy1 = LerpDouble(nx01, nx11, fy);
            return LerpDouble(nxy0, nxy1, fz) * 2.0 - 1.0;
        }

        float Perlin3D(Vector3 p)
        {
            return (float)ValueNoise3D(p);
        }

        struct NoiseResult { public float value; public float deriv; }

        NoiseResult FBM_Derivative(Vector3 p, int octaves, float persistence, float lacunarity,
                                   int seedOff, float expWeight = 1.6f, float erosion = 0f)
        {
            var off   = new Vector3(seedOff * 0.1173f, seedOff * 0.2341f, seedOff * 0.3512f);
            float amp = 1f, freq = 1f, sum = 0f, ampSum = 0f;
            float dx  = 0f, dy = 0f, dz = 0f;
            const float derivStep = 0.00085f;

            for (int i = 0; i < octaves; i++)
            {
                Vector3 pp = p * freq + off;
                float n    = Perlin3D(pp);
                float ndxA = Perlin3D(pp + new Vector3(derivStep, 0f, 0f));
                float ndxB = Perlin3D(pp - new Vector3(derivStep, 0f, 0f));
                float ndyA = Perlin3D(pp + new Vector3(0f, derivStep, 0f));
                float ndyB = Perlin3D(pp - new Vector3(0f, derivStep, 0f));
                float ndzA = Perlin3D(pp + new Vector3(0f, 0f, derivStep));
                float ndzB = Perlin3D(pp - new Vector3(0f, 0f, derivStep));

                float gx = ((ndxA - ndxB) / (2f * derivStep)) * freq;
                float gy = ((ndyA - ndyB) / (2f * derivStep)) * freq;
                float gz = ((ndzA - ndzB) / (2f * derivStep)) * freq;

                float se = (erosion > 0f && i > 0)
                    ? 1f / (1f + Mathf.Sqrt(dx*dx + dy*dy + dz*dz) * erosion)
                    : 1f;

                float expAmp = Mathf.Pow(0.65f, i * Mathf.Max(0.25f, expWeight));
                float a = amp * se * expAmp;

                sum    += n * a;
                ampSum += a;
                dx     += gx * a; dy += gy * a; dz += gz * a;
                amp    *= persistence;
                freq   *= lacunarity;
            }

            float val   = ampSum > 0f ? sum / ampSum : 0f;
            float deriv = Mathf.Sqrt(dx*dx + dy*dy + dz*dz) / Mathf.Max(1f, ampSum);
            return new NoiseResult { value = val, deriv = deriv };
        }

        float FBM(Vector3 p, int octaves, float persistence, float lacunarity, int seedOff)
        {
            var off   = new Vector3(seedOff * 0.1173f, seedOff * 0.2341f, seedOff * 0.3512f);
            float amp = 1f, freq = 1f, sum = 0f, ampSum = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum    += Perlin3D(p * freq + off) * amp;
                ampSum += amp;
                amp  *= persistence;
                freq *= lacunarity;
            }
            return ampSum > 0f ? sum / ampSum : 0f;
        }

        float RidgeFBM(Vector3 p, int octaves, float persistence, float lacunarity,
                       int seedOff, float gain = 1f, float sharpness = 0.65f)
        {
            var off   = new Vector3(seedOff * 0.1173f, seedOff * 0.2341f, seedOff * 0.3512f);
            float amp = 0.5f, freq = 1f, sum = 0f, ampSum = 0f, prev = 1f;

            for (int i = 0; i < octaves; i++)
            {
                float n    = Perlin3D(p * freq + off);
                float ridge = 1f - Mathf.Abs(n);
                ridge = Mathf.Pow(ridge, 1f + sharpness * 1.5f);
                ridge *= prev * gain;
                prev   = ridge;

                sum    += ridge * amp;
                ampSum += amp;
                amp    *= persistence;
                freq   *= lacunarity;
            }
            return ampSum > 0f ? sum / ampSum : 0f;
        }

        float ApplySoftTerrace(float value01, float steps, float blend)
        {
            value01 = Mathf.Clamp01(value01);
            blend = Mathf.Clamp01(blend);
            steps = Mathf.Max(1f, steps);
            if (blend <= 0.001f)
                return value01;

            float scaled = value01 * steps;
            float lower = Mathf.Floor(scaled);
            float upper = Mathf.Min(steps, lower + 1f);
            float t = scaled - lower;
            float smoothT = t * t * (3f - 2f * t);
            float terraced = Mathf.Lerp(lower, upper, smoothT) / steps;
            return Mathf.Lerp(value01, terraced, blend);
        }

        float MesaNoise(Vector3 p, int seedOff)
        {
            var off = new Vector3(seedOff * 0.1173f, seedOff * 0.2341f, seedOff * 0.3512f);
            float base01 = FBM(p * mesaScale + off, 3, 0.5f, 2.1f, seedOff) * 0.5f + 0.5f;
            float mesa = Mathf.Clamp01(base01);
            float mesaMask = Mathf.SmoothStep(
                Mathf.Clamp01(mesaFrequency - 0.10f),
                Mathf.Clamp01(mesaFrequency + 0.16f),
                base01);
            float flattened = 1f - Mathf.Pow(1f - mesa, 1f + Mathf.Clamp(mesaSharpness, 1f, 20f) * 0.05f);
            float terraced = ApplySoftTerrace(flattened, 2.4f + Mathf.Clamp(mesaSharpness, 1f, 20f) * 0.12f, 0.20f * mesaMask);
            return Mathf.Clamp01(Mathf.Lerp(mesa, terraced, mesaMask * 0.55f));
        }

        float CanyonNoise(Vector3 p, int seedOff)
        {
            var off = new Vector3(seedOff * 0.1173f, seedOff * 0.2341f, seedOff * 0.3512f);
            float n = Perlin3D(p * canyonScale + off);
            float canyon = Mathf.Abs(n);
            canyon = Mathf.Pow(canyon, 0.5f);
            float mask = FBM(p * canyonScale * 0.4f + off, 2, 0.5f, 2f, seedOff + 77) * 0.5f + 0.5f;
            mask = Mathf.SmoothStep(canyonFrequency - 0.15f, canyonFrequency + 0.15f, mask);
            return canyon * mask;
        }

        Vector3 ApplyDomainWarp(Vector3 point)
        {
            return ApplyDomainWarp(point, 1f);
        }

        Vector3 ApplyDomainWarp(Vector3 point, float strengthMultiplier)
        {
            // NMS Mega doc: Double domain warp (q then r), в мировых координатах.
            Vector3 worldP = point * radius;
            float wf1 = Mathf.Max(0.00001f, warpFrequency);
            float wf2 = Mathf.Max(0.00001f, warp2Frequency);
            float warpAmp = Mathf.Max(1f, radius * Mathf.Clamp(warpAmplitudeRatio, 0.001f, 0.08f));
            float strength = Mathf.Max(0f, strengthMultiplier);

            Vector3 p1 = worldP * wf1 + seedOffset * 0.002f;
            Vector3 q = new Vector3(
                FBM(p1 + new Vector3(1.7f, 9.2f, 3.8f), 3, 0.5f, 2.05f, seed + 5100),
                FBM(p1 + new Vector3(8.3f, 2.8f, 5.1f), 3, 0.5f, 2.05f, seed + 5200),
                FBM(p1 + new Vector3(4.1f, 7.3f, 2.9f), 3, 0.5f, 2.05f, seed + 5300)
            ) * (warpStrength * strength * warpAmp);

            Vector3 p2 = (worldP + q * warpScale) * wf2 + seedOffset * 0.002f;
            Vector3 r = new Vector3(
                FBM(p2 + new Vector3(0.3f, 6.7f, 1.4f), 2, 0.5f, 2.05f, seed + 6100),
                FBM(p2 + new Vector3(5.2f, 1.3f, 8.6f), 2, 0.5f, 2.05f, seed + 6200),
                FBM(p2 + new Vector3(2.9f, 4.1f, 0.7f), 2, 0.5f, 2.05f, seed + 6300)
            ) * (warp2Strength * strength * warpAmp);

            Vector3 warpedWorld = worldP + r;
            return warpedWorld.normalized;
        }

        Vector3Int QuantizeDir(Vector3 point)
        {
            return new Vector3Int(
                Mathf.RoundToInt(point.x * HeightCacheQuant),
                Mathf.RoundToInt(point.y * HeightCacheQuant),
                Mathf.RoundToInt(point.z * HeightCacheQuant));
        }

        float EvaluateMacroHeightCached(Vector3 point)
        {
            var key = QuantizeDir(point);
            lock (heightCacheLock)
            {
                if (heightCache.TryGetValue(key, out float cached))
                    return cached;
            }
            float value = GenerateHeight(point);
            lock (heightCacheLock)
            {
                if (!heightCache.ContainsKey(key))
                {
                    if (heightCache.Count > 120000) heightCache.Clear();
                    heightCache[key] = value;
                }
            }
            return value;
        }

        float EvaluateHeight(Vector3 point)
        {
            return EvaluateMacroHeightCached(point);
        }

        float EvaluateHeightWithDetail(Vector3 point, float detailFactor)
        {
            // Geometry stays macro-only. Fine detail is injected into normals, not vertices.
            return EvaluateMacroHeightCached(point);
        }

        float GenerateHeight(Vector3 point)
        {
            float hMax = Mathf.Max(10f, normalizedMaxHeight);

            if (terrainStyle == TerrainStyle.SebInspired)
                return GenerateSebInspiredHeight(point.normalized, hMax);

            Vector3 warpedPoint = ApplyDomainWarp(point);
            Vector3 worldP = warpedPoint * radius + seedOffset * (radius * 0.0025f);
            float styleScale = GetStylizedFrequencyScale();

            // Low-frequency continent mask defines only broad landmass placement.
            float continentNoise = FBM(worldP * (0.00042f * styleScale), 4, 0.50f, 1.92f, seed + 11);
            float continent01 = continentNoise * 0.5f + 0.5f;
            continent01 = Mathf.SmoothStep(0.18f, 0.84f, continent01);
            float landMask = Mathf.SmoothStep(
                Mathf.Clamp01(continentThreshold - 0.18f),
                Mathf.Clamp01(continentThreshold + 0.14f),
                continent01);

            // Keep macro forms dominant and let later octaves add shape instead of
            // turning the whole surface into uniform high-frequency chatter.
            var baseRes = FBM_Derivative(
                worldP * (0.00068f * styleScale),
                Mathf.Clamp(geometryOctaves, 2, 6),
                Mathf.Clamp(geometryAmplitudeDecay, 0.2f, 0.7f),
                Mathf.Clamp(geometryLacunarity, 1.7f, 2.2f),
                seed + 101,
                expWeight: octaveExponent * 0.82f,
                erosion: slopeErosion * 0.55f);

            float baseNoise01 = baseRes.value * 0.5f + 0.5f;
            baseNoise01 = Mathf.SmoothStep(0.12f, 0.88f, baseNoise01);

            float gradientDamp = 1f / (1f + baseRes.deriv * Mathf.Lerp(0.04f, 0.15f, highFrequencyDamping));
            float continentLift = Mathf.Lerp(-hMax * 0.34f, hMax * 0.18f, landMask);
            float shelfLift = Mathf.Lerp(-hMax * 0.18f, -hMax * 0.02f, landMask);
            float baseHeight = (baseNoise01 - 0.50f) * hMax * 0.54f * gradientDamp;
            baseHeight *= Mathf.Lerp(0.72f, 1f, landMask);

            float plainsMask = 1f - Mathf.SmoothStep(0.38f, 0.76f, baseNoise01);

            float hillsNoise = FBM(worldP * (0.00145f * styleScale), 3, 0.42f, 1.95f, seed + 401) * 0.5f + 0.5f;
            hillsNoise = Mathf.SmoothStep(0.20f, 0.85f, hillsNoise);
            float hillsHeight = (hillsNoise - 0.5f) * hMax * 0.28f;
            hillsHeight *= Mathf.Lerp(0.22f, 1f, landMask);
            hillsHeight *= Mathf.Lerp(0.24f, 1f, 1f - plainsMask * 0.75f);

            float mesaMaskNoise = MesaNoise(worldP * (0.00072f * styleScale), seed + 501);
            float mesaMask = Mathf.SmoothStep(
                Mathf.Clamp01(mesaFrequency - 0.18f),
                Mathf.Clamp01(mesaFrequency + 0.08f),
                mesaMaskNoise);
            mesaMask *= Mathf.SmoothStep(0.30f, 0.75f, landMask) * Mathf.Clamp01(mesaStrength);

            // Soft terraces preserve the stylized NMS feeling without creating razor peaks.
            float mesaTerraceSource = FBM(worldP * (0.0012f * styleScale), 2, 0.42f, 1.9f, seed + 521) * 0.5f + 0.5f;
            float mesaProfile = Mathf.SmoothStep(0.18f, 0.86f, mesaTerraceSource);
            float mesaRounded = ApplySoftTerrace(mesaProfile, 3.2f, 0.16f);
            float mesaHeight = (mesaRounded - 0.5f) * hMax * 0.18f;

            float mountainMaskNoise = FBM(worldP * (0.00056f * styleScale), 3, 0.45f, 1.90f, seed + 307) * 0.5f + 0.5f;
            float mountainMask = Mathf.SmoothStep(0.52f, 0.80f, mountainMaskNoise);
            mountainMask *= Mathf.SmoothStep(0.24f, 0.82f, landMask);

            float ridgeNoise = RidgeFBM(
                worldP * (Mathf.Max(0.25f, ridgeScale) * 0.00115f * styleScale),
                Mathf.Clamp(ridgeOctaves, 2, 5),
                Mathf.Min(Mathf.Clamp01(ridgePersistence), 0.45f),
                1.92f,
                seed + 203,
                gain: Mathf.Max(0.35f, ridgeGain * 0.9f),
                sharpness: Mathf.Clamp(ridgeSharpness, 0.18f, 1.05f));
            ridgeNoise = Mathf.SmoothStep(0.10f, 0.82f, Mathf.Clamp01(ridgeNoise));

            float mountainStrengthMul = Mathf.Sqrt(Mathf.Max(0.2f, ridgeStrength) * Mathf.Max(0.2f, noiseSettings.mountainStrength));
            mountainStrengthMul = Mathf.Clamp(mountainStrengthMul, 0.45f, 1.45f);
            float mountainHeight = ridgeNoise * hMax * 0.34f * mountainStrengthMul;
            mountainHeight *= Mathf.Lerp(0.78f, 1f, gradientDamp);

            float megaMaskNoise = FBM(
                worldP * (Mathf.Max(0.08f, megaScale) * 0.00085f * styleScale),
                3, 0.5f, 2f, seed + 811) * 0.5f + 0.5f;
            float megaMask = Mathf.SmoothStep(
                Mathf.Clamp01(megaRarity - 0.18f),
                Mathf.Clamp01(megaRarity),
                megaMaskNoise) * mountainMask;
            float megaHeight = megaMask * megaStrength * hMax * 0.055f * gradientDamp;

            float cliffNoise = RidgeFBM(
                worldP * (Mathf.Max(0.25f, noiseSettings.cliffScale) * 0.00185f * styleScale),
                3, 0.42f, 2.08f, seed + 1603,
                gain: Mathf.Max(0.55f, ridgeGain),
                sharpness: Mathf.Clamp(noiseSettings.peakSharpness * 1.15f, 0.5f, 2.2f));
            float cliffMask = Mathf.SmoothStep(
                Mathf.Clamp01(noiseSettings.cliffThreshold - 0.18f),
                Mathf.Clamp01(noiseSettings.cliffThreshold + 0.08f),
                cliffNoise);
            cliffMask *= mountainMask * Mathf.SmoothStep(0.14f, 0.52f, landMask);
            float cliffProfile = Mathf.SmoothStep(0.22f, 0.88f, Mathf.Clamp01(cliffNoise));
            float cliffHeight = Mathf.Pow(cliffProfile, 1f + Mathf.Max(0f, noiseSettings.peakSharpness) * 0.65f);
            cliffHeight *= hMax * 0.075f * Mathf.Max(0f, noiseSettings.cliffStrength) * gradientDamp;

            float finalHeight = continentLift + shelfLift + baseHeight + hillsHeight;
            finalHeight = Mathf.Lerp(finalHeight, finalHeight + mesaHeight, mesaMask * 0.60f);
            finalHeight += mountainHeight * mountainMask;
            finalHeight += megaHeight;
            finalHeight += cliffHeight * cliffMask;

            if (enableCanyons && canyonStrength > 0.001f)
            {
                float canyon = CanyonNoise(worldP * (0.00082f * styleScale), seed + 701);
                float canyonMask = Mathf.SmoothStep(0.24f, 0.78f, landMask) * (1f - mountainMask * 0.65f);
                finalHeight -= canyon * canyonStrength * hMax * 0.18f * canyonMask;
            }

            // Не даём суше проваливаться ниже уровня воды (фикс синих пятен)
            if (generateOcean && landMask > 0.3f)
                finalHeight = Mathf.Max(finalHeight, seaLevelHeight + beachWidthHeight * 0.4f);

            finalHeight *= Mathf.Lerp(0.94f, 1f, landMask);

            float absHeight01 = Mathf.Clamp01(Mathf.Abs(finalHeight) / hMax);
            float peakCompression = 1f - Mathf.SmoothStep(0.66f, 1f, absHeight01) * 0.22f;
            finalHeight *= peakCompression;

            if (enableRivers && riverStrength > 0.001f)
            {
                float rv1 = FBM(worldP * riverScale + new Vector3(7.7f, 31.3f, 13.9f), 3, 0.5f, 1.95f, seed + 13131);
                float rv2 = FBM(worldP * riverScale * 2.3f + new Vector3(53.1f, 17.4f, 42.6f), 2, 0.45f, 1.92f, seed + 14242);

                float riverRaw = Mathf.Abs(rv1) * (1f - riverBranchMix) + Mathf.Abs(rv2) * riverBranchMix;
                float riverChannel = 1f - Mathf.SmoothStep(0f, riverNarrow, riverRaw);
                float elevMask = 1f - Mathf.SmoothStep(seaLevelHeight, seaLevelHeight + riverMaxElevation, finalHeight);
                elevMask = Mathf.Clamp01(elevMask);
                float landRiverMask = Mathf.SmoothStep(0.1f, 0.55f, landMask);
                float carve = riverChannel * riverStrength * hMax * 0.12f;
                finalHeight -= carve * elevMask * landRiverMask;
                // Не даём рекам прорезать ниже уровня моря
                if (generateOcean)
                    finalHeight = Mathf.Max(finalHeight, seaLevelHeight + 0.5f);
            }

            finalHeight = Mathf.Clamp(finalHeight, -hMax, hMax);
            return finalHeight;
        }

        float GenerateSebInspiredHeight(Vector3 spherePoint, float hMax)
        {
            var plateSample = SampleSebPlateField(spherePoint);
            float landThreshold = 1f - Mathf.Clamp01(sebLandRatio);
            float coastNoise = SebSimpleNoise(
                spherePoint,
                numLayers: 4,
                lacunarity: 2.1f,
                persistence: 0.5f,
                scale: Mathf.Max(0.5f, sebCoastNoiseScale),
                elevation: 1f,
                verticalShift: 0f,
                seedOff: seed + 4101);

            float warpedLandValue = plateSample.blendedValue + coastNoise * sebCoastNoiseStrength;
            float coastWidth = Mathf.Lerp(0.10f, 0.22f, Mathf.Clamp01(sebPlateBlend));
            float landMask = Mathf.SmoothStep(landThreshold - coastWidth, landThreshold + coastWidth, warpedLandValue);
            float secondLandMask = Mathf.SmoothStep(landThreshold - coastWidth, landThreshold + coastWidth, plateSample.secondaryValue);
            float continentCore = Mathf.SmoothStep(
                landThreshold + coastWidth * 0.25f,
                landThreshold + coastWidth * 2.6f,
                warpedLandValue);
            float continentalShelf = Mathf.SmoothStep(
                landThreshold - coastWidth * 1.6f,
                landThreshold + coastWidth * 0.4f,
                warpedLandValue);

            float continentalReliefA = SebSimpleNoise(
                spherePoint,
                numLayers: 4,
                lacunarity: 2f,
                persistence: 0.48f,
                scale: 0.92f,
                elevation: 1f,
                verticalShift: 0f,
                seedOff: seed + 4151);
            float continentalReliefB = SebSimpleNoise(
                spherePoint,
                numLayers: 3,
                lacunarity: 2.35f,
                persistence: 0.52f,
                scale: 1.55f,
                elevation: 1f,
                verticalShift: 0f,
                seedOff: seed + 4173);
            float inlandMaskNoise = SebSimpleNoise(
                spherePoint,
                numLayers: 3,
                lacunarity: 1.72f,
                persistence: 0.55f,
                scale: 1.08f,
                elevation: 1f,
                verticalShift: 0.02f,
                seedOff: seed + 4201);

            float basinNoise = SebSimpleNoise(
                spherePoint,
                numLayers: 2,
                lacunarity: 2.4f,
                persistence: 0.45f,
                scale: 2.05f,
                elevation: 1f,
                verticalShift: 0f,
                seedOff: seed + 4219);

            float inlandMountainMask = SebBlend(0.08f, 0.90f, inlandMaskNoise) * continentCore;
            float plateBoundaryMask =
                plateSample.boundary *
                Mathf.Lerp(0.22f, 1f, secondLandMask) *
                Mathf.Lerp(0.35f, 1f, landMask);
            float mountainMask = Mathf.Clamp01(Mathf.Max(
                inlandMountainMask * 0.34f,
                plateBoundaryMask * (0.55f + sebBoundaryMountainStrength * 0.45f)));

            float ridgeNoise = SebSmoothedRidgeNoise(
                spherePoint,
                numLayers: 4,
                lacunarity: 3.15f,
                persistence: 0.5f,
                scale: 1.18f,
                power: 1.88f,
                elevation: 4.3f,
                gain: 0.66f,
                verticalShift: 0.02f,
                peakSmoothing: 1.45f,
                seedOff: seed + 4301);

            float continentRelief = continentalReliefA * 0.65f + continentalReliefB * 0.35f;
            float continentReliefSigned = (continentRelief - 0.5f) * 2f;
            float ridge01 = Mathf.SmoothStep(0.18f, 2.15f, ridgeNoise);
            float inlandPlateauMask = Mathf.SmoothStep(0.28f, 0.86f, inlandMaskNoise) * continentCore;
            float shelfMask = Mathf.Clamp01(continentalShelf * (1f - landMask * 0.72f));
            float plateauTerrace = ApplySoftTerrace(Mathf.Clamp01(continentRelief), 3.2f, 0.22f) - Mathf.Clamp01(continentRelief);
            float basinMask = Mathf.SmoothStep(0.54f, 0.84f, 1f - basinNoise) * continentCore * (1f - mountainMask * 0.78f);

            float oceanBase = Mathf.Lerp(
                seaLevelHeight - hMax * 0.58f,
                seaLevelHeight - hMax * 0.16f,
                continentalShelf);
            oceanBase += continentReliefSigned * hMax * 0.04f * (1f - landMask);
            oceanBase -= plateBoundaryMask * hMax * 0.10f * (1f - landMask * 0.45f);

            float landBase = seaLevelHeight + beachWidthHeight * 1.15f;
            landBase += continentCore * hMax * 0.24f;
            landBase += continentReliefSigned * hMax * Mathf.Lerp(0.08f, 0.23f, continentCore);
            landBase += inlandPlateauMask * hMax * 0.16f;
            landBase += plateauTerrace * hMax * 0.24f * inlandPlateauMask;

            float shorelineBench = Mathf.SmoothStep(0.05f, 0.68f, landMask) * hMax * 0.08f;
            float continentalShelfLift = shelfMask * hMax * 0.10f;
            float inlandRidges = ridge01 * inlandMountainMask * hMax * 0.18f;
            float boundaryRanges = plateBoundaryMask * landMask * hMax * (0.10f + sebBoundaryMountainStrength * 0.10f);
            float majorMountains = ridge01 * mountainMask * hMax * Mathf.Lerp(0.18f, 0.52f, mountainMask);
            float innerBasins = basinMask * hMax * 0.05f;
            float boundaryTrench = plateBoundaryMask * (1f - landMask * 0.78f) * hMax * Mathf.Lerp(0.05f, 0.02f, continentalShelf);

            float finalHeight = Mathf.Lerp(oceanBase + continentalShelfLift, landBase + shorelineBench, landMask);
            finalHeight += inlandRidges + majorMountains + boundaryRanges;
            finalHeight -= innerBasins + boundaryTrench;

            float seaRelativeHeight = finalHeight - seaLevelHeight;
            float compression = 1f - Mathf.SmoothStep(hMax * 0.58f, hMax * 0.96f, Mathf.Abs(seaRelativeHeight)) * 0.22f;
            finalHeight = seaLevelHeight + seaRelativeHeight * compression;

            if (generateOcean && landMask > 0.45f)
                finalHeight = Mathf.Max(finalHeight, seaLevelHeight + beachWidthHeight * 0.70f);

            return Mathf.Clamp(finalHeight, -hMax, hMax);
        }

        void EnsureSebPlateData()
        {
            if (sebPlateDataReady)
                return;

            lock (sebPlateDataLock)
            {
                if (sebPlateDataReady)
                    return;

                int count = Mathf.Clamp(sebPlateCount, 8, 64);
                sebPlateCenters = new Vector3[count];
                sebPlateMacroValues = new float[count];
                float seedPhase = Mathf.Repeat(seed * 0.1234567f, 1f) * Mathf.PI * 2f;
                float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));

                for (int i = 0; i < count; i++)
                {
                    float t = (i + 0.5f) / count;
                    float y = 1f - 2f * t;
                    float radial = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                    float theta = goldenAngle * i + seedPhase;
                    Vector3 point = new Vector3(Mathf.Cos(theta) * radial, y, Mathf.Sin(theta) * radial);
                    Vector3 jitter = new Vector3(
                        HashSigned(seed + 701, i, 0),
                        HashSigned(seed + 702, i, 1),
                        HashSigned(seed + 703, i, 2)) * sebPlateJitter;
                    point = (point + jitter).normalized;
                    sebPlateCenters[i] = point;

                    float macro = FBM(
                        point * Mathf.Max(0.5f, sebPlateMacroScale),
                        4, 0.5f, 2.0f,
                        seed + 4801) * 0.5f + 0.5f;
                    float secondaryMacro = FBM(
                        point * Mathf.Max(0.35f, sebPlateMacroScale * 0.62f),
                        2, 0.52f, 1.9f,
                        seed + 4901) * 0.5f + 0.5f;
                    sebPlateMacroValues[i] = Mathf.Clamp01(Mathf.Lerp(macro, secondaryMacro, 0.28f));
                }

                sebPlateDataReady = true;
            }
        }

        SebPlateSample SampleSebPlateField(Vector3 spherePoint)
        {
            EnsureSebPlateData();

            float nearestDist = float.MaxValue;
            float secondDist = float.MaxValue;
            float primaryValue = 0f;
            float secondaryValue = 0f;

            for (int i = 0; i < sebPlateCenters.Length; i++)
            {
                float d = 1f - Vector3.Dot(spherePoint, sebPlateCenters[i]);
                if (d < nearestDist)
                {
                    secondDist = nearestDist;
                    secondaryValue = primaryValue;
                    nearestDist = d;
                    primaryValue = sebPlateMacroValues[i];
                }
                else if (d < secondDist)
                {
                    secondDist = d;
                    secondaryValue = sebPlateMacroValues[i];
                }
            }

            float gap = Mathf.Max(0f, secondDist - nearestDist);
            float boundaryWidth = Mathf.Lerp(0.12f, 0.035f, Mathf.InverseLerp(8f, 64f, sebPlateCount));
            float boundary = 1f - Mathf.Clamp01(gap / Mathf.Max(0.0001f, boundaryWidth));
            float blend = boundary * Mathf.Clamp01(sebPlateBlend) * 0.5f;
            float blendedValue = Mathf.Lerp(primaryValue, secondaryValue, blend);

            return new SebPlateSample
            {
                primaryValue = primaryValue,
                secondaryValue = secondaryValue,
                blendedValue = blendedValue,
                boundary = boundary,
                primaryDistance = nearestDist,
                secondaryDistance = secondDist
            };
        }

        float SebSimpleNoise(Vector3 point, int numLayers, float lacunarity, float persistence, float scale, float elevation, float verticalShift, int seedOff)
        {
            Vector3 offset = new Vector3(seedOff * 0.1173f, seedOff * 0.2341f, seedOff * 0.3512f);
            float sum = 0f;
            float amplitude = 1f;
            float frequency = Mathf.Max(0.0001f, scale);

            for (int i = 0; i < Mathf.Max(1, numLayers); i++)
            {
                sum += Perlin3D(point * frequency + offset) * amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return sum * elevation + verticalShift;
        }

        float SebRidgeNoise(Vector3 point, int numLayers, float lacunarity, float persistence, float scale, float power, float elevation, float gain, float verticalShift, int seedOff)
        {
            Vector3 offset = new Vector3(seedOff * 0.1173f, seedOff * 0.2341f, seedOff * 0.3512f);
            float sum = 0f;
            float amplitude = 1f;
            float frequency = Mathf.Max(0.0001f, scale);
            float ridgeWeight = 1f;

            for (int i = 0; i < Mathf.Max(1, numLayers); i++)
            {
                float noiseVal = 1f - Mathf.Abs(Perlin3D(point * frequency + offset));
                noiseVal = Mathf.Pow(Mathf.Abs(noiseVal), Mathf.Max(0.0001f, power));
                noiseVal *= ridgeWeight;
                ridgeWeight = Mathf.Clamp01(noiseVal * Mathf.Max(0f, gain));

                sum += noiseVal * amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return sum * elevation + verticalShift;
        }

        float SebSmoothedRidgeNoise(Vector3 point, int numLayers, float lacunarity, float persistence, float scale, float power, float elevation, float gain, float verticalShift, float peakSmoothing, int seedOff)
        {
            Vector3 radial = point.normalized;
            Vector3 axisA = GetSafeTangent(radial);
            Vector3 axisB = Vector3.Cross(radial, axisA).normalized;
            float offsetDistance = Mathf.Max(0f, peakSmoothing) * 0.01f;

            float sample0 = SebRidgeNoise(point, numLayers, lacunarity, persistence, scale, power, elevation, gain, verticalShift, seedOff);
            float sample1 = SebRidgeNoise((point - axisA * offsetDistance).normalized, numLayers, lacunarity, persistence, scale, power, elevation, gain, verticalShift, seedOff);
            float sample2 = SebRidgeNoise((point + axisA * offsetDistance).normalized, numLayers, lacunarity, persistence, scale, power, elevation, gain, verticalShift, seedOff);
            float sample3 = SebRidgeNoise((point - axisB * offsetDistance).normalized, numLayers, lacunarity, persistence, scale, power, elevation, gain, verticalShift, seedOff);
            float sample4 = SebRidgeNoise((point + axisB * offsetDistance).normalized, numLayers, lacunarity, persistence, scale, power, elevation, gain, verticalShift, seedOff);
            return (sample0 + sample1 + sample2 + sample3 + sample4) * 0.2f;
        }

        float SmoothMaxValue(float a, float b, float smoothing)
        {
            return -SmoothMinValue(-a, -b, smoothing);
        }

        float SmoothMinValue(float a, float b, float smoothing)
        {
            float k = Mathf.Max(0.0001f, smoothing);
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        float SebBlend(float startHeight, float blendDistance, float height)
        {
            float halfBlend = Mathf.Max(0.0001f, blendDistance) * 0.5f;
            return Mathf.SmoothStep(startHeight - halfBlend, startHeight + halfBlend, height);
        }

        float EvaluateMacroHeight(Vector3 point)
        {
            return GenerateHeight(point);
        }

        void RelaxSebHeightmap(float[,] heightMap, float cellSize)
        {
            int width = heightMap.GetLength(0);
            int height = heightMap.GetLength(1);
            float[,] temp = new float[width, height];
            float curvatureThreshold = Mathf.Max(cellSize * 0.10f, normalizedMaxHeight * 0.0045f);

            for (int pass = 0; pass < 2; pass++)
            {
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        temp[x, y] = heightMap[x, y];

                for (int y = 1; y < height - 1; y++)
                {
                    for (int x = 1; x < width - 1; x++)
                    {
                        float center = heightMap[x, y];
                        float left = heightMap[x - 1, y];
                        float right = heightMap[x + 1, y];
                        float down = heightMap[x, y - 1];
                        float up = heightMap[x, y + 1];
                        float avg4 = (left + right + down + up) * 0.25f;
                        float avg8 =
                            (left + right + down + up +
                             heightMap[x - 1, y - 1] + heightMap[x + 1, y - 1] +
                             heightMap[x - 1, y + 1] + heightMap[x + 1, y + 1]) * 0.125f;

                        float curvature = Mathf.Max(Mathf.Abs(avg4 - center), Mathf.Abs(avg8 - center));
                        float spikeMask = Mathf.SmoothStep(curvatureThreshold, curvatureThreshold * 5f, curvature);
                        float relax = Mathf.Lerp(0.02f, 0.16f, spikeMask);
                        temp[x, y] = Mathf.Lerp(center, Mathf.Lerp(avg4, avg8, 0.35f), relax);
                    }
                }

                for (int y = 1; y < height - 1; y++)
                    for (int x = 1; x < width - 1; x++)
                        heightMap[x, y] = temp[x, y];
            }
        }

        void SmoothHeightmap(float[,] heightMap, float cellSize, float strength)
        {
            if (strength <= 0.001f) return;

            int width = heightMap.GetLength(0);
            int height = heightMap.GetLength(1);
            int passes = strength > 0.70f ? 2 : 1;
            float relaxStrength = Mathf.Lerp(0.08f, 0.24f, Mathf.Clamp01(strength));
            float curvatureThreshold = Mathf.Max(cellSize * 0.16f, normalizedMaxHeight * 0.009f);
            float[,] temp = new float[width, height];

            for (int pass = 0; pass < passes; pass++)
            {
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        temp[x, y] = heightMap[x, y];

                for (int y = 1; y < height - 1; y++)
                {
                    for (int x = 1; x < width - 1; x++)
                    {
                        float center = heightMap[x, y];
                        float left = heightMap[x - 1, y];
                        float right = heightMap[x + 1, y];
                        float down = heightMap[x, y - 1];
                        float up = heightMap[x, y + 1];
                        float diag = (heightMap[x - 1, y - 1] + heightMap[x + 1, y - 1] + heightMap[x - 1, y + 1] + heightMap[x + 1, y + 1]) * 0.25f;

                        // Curvature-limited relaxation: acts on second derivative only.
                        // Broad slopes survive, but isolated spikes have very high curvature and get relaxed.
                        float avg4 = (left + right + down + up) * 0.25f;
                        float laplacian = avg4 - center;
                        float curvature = Mathf.Max(Mathf.Abs(laplacian), Mathf.Abs(diag - center));
                        float spikeMask = Mathf.SmoothStep(curvatureThreshold, curvatureThreshold * 4f, curvature);

                        temp[x, y] = center + laplacian * spikeMask * relaxStrength;
                    }
                }

                for (int y = 1; y < height - 1; y++)
                    for (int x = 1; x < width - 1; x++)
                        heightMap[x, y] = temp[x, y];
            }
        }

        void LimitSlope(float[,] heightMap, float cellSize)
        {
            if (slopeLimitPasses <= 0) return;

            int width = heightMap.GetLength(0);
            int height = heightMap.GetLength(1);
            float[,] delta = new float[width, height];
            float maxDelta = Mathf.Tan(Mathf.Clamp(maxSlopeDegrees, 5f, 80f) * Mathf.Deg2Rad) * Mathf.Max(0.001f, cellSize);

            for (int pass = 0; pass < slopeLimitPasses; pass++)
            {
                System.Array.Clear(delta, 0, delta.Length);

                for (int y = 1; y < height - 1; y++)
                {
                    for (int x = 1; x < width - 1; x++)
                    {
                        float center = heightMap[x, y];

                        float exLeft = Mathf.Max(0f, center - heightMap[x - 1, y] - maxDelta);
                        float exRight = Mathf.Max(0f, center - heightMap[x + 1, y] - maxDelta);
                        float exDown = Mathf.Max(0f, center - heightMap[x, y - 1] - maxDelta);
                        float exUp = Mathf.Max(0f, center - heightMap[x, y + 1] - maxDelta);
                        float totalExcess = exLeft + exRight + exDown + exUp;

                        if (totalExcess <= 0f)
                            continue;

                        // Redistribution limits local slope instead of blurring the whole terrain.
                        float moveTotal = totalExcess * 0.14f;
                        delta[x, y] -= moveTotal;

                        if (exLeft > 0f) delta[x - 1, y] += moveTotal * (exLeft / totalExcess);
                        if (exRight > 0f) delta[x + 1, y] += moveTotal * (exRight / totalExcess);
                        if (exDown > 0f) delta[x, y - 1] += moveTotal * (exDown / totalExcess);
                        if (exUp > 0f) delta[x, y + 1] += moveTotal * (exUp / totalExcess);
                    }
                }

                for (int y = 1; y < height - 1; y++)
                    for (int x = 1; x < width - 1; x++)
                        heightMap[x, y] += delta[x, y];
            }
        }

        void ApplyErosion(float[,] heightMap, float cellSize)
        {
            if (erosionPasses <= 0 || erosionStrength <= 0.001f) return;

            int width = heightMap.GetLength(0);
            int height = heightMap.GetLength(1);
            float[,] delta = new float[width, height];
            float talus = Mathf.Tan(Mathf.Max(5f, maxSlopeDegrees - 6f) * Mathf.Deg2Rad) * Mathf.Max(0.001f, cellSize);
            float maxTransport = normalizedMaxHeight * 0.006f;

            for (int pass = 0; pass < erosionPasses; pass++)
            {
                System.Array.Clear(delta, 0, delta.Length);

                for (int y = 1; y < height - 1; y++)
                {
                    for (int x = 1; x < width - 1; x++)
                    {
                        float center = heightMap[x, y];
                        int bestX = x;
                        int bestY = y;
                        float steepestDrop = 0f;

                        for (int oy = -1; oy <= 1; oy++)
                        {
                            for (int ox = -1; ox <= 1; ox++)
                            {
                                if (ox == 0 && oy == 0) continue;
                                float drop = center - heightMap[x + ox, y + oy];
                                if (drop > steepestDrop)
                                {
                                    steepestDrop = drop;
                                    bestX = x + ox;
                                    bestY = y + oy;
                                }
                            }
                        }

                        if (steepestDrop <= talus)
                            continue;

                        // Lightweight thermal erosion rounds needle peaks by moving a small
                        // amount of material toward the steepest lower neighbor.
                        float transport = Mathf.Min((steepestDrop - talus) * 0.12f, maxTransport) * erosionStrength;
                        delta[x, y] -= transport;
                        delta[bestX, bestY] += transport;
                    }
                }

                for (int y = 1; y < height - 1; y++)
                    for (int x = 1; x < width - 1; x++)
                        heightMap[x, y] += delta[x, y];
            }
        }

        void ClampSpikeHeights(float[,] heightMap, float cellSize)
        {
            int width = heightMap.GetLength(0);
            int height = heightMap.GetLength(1);
            if (width < 3 || height < 3)
                return;

            float[,] temp = new float[width, height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    temp[x, y] = Mathf.Clamp(heightMap[x, y], -normalizedMaxHeight, normalizedMaxHeight);

            float spikeThreshold = Mathf.Max(cellSize * 1.4f, normalizedMaxHeight * 0.018f);
            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    float center = temp[x, y];
                    float avg4 = (temp[x - 1, y] + temp[x + 1, y] + temp[x, y - 1] + temp[x, y + 1]) * 0.25f;
                    float diag = (temp[x - 1, y - 1] + temp[x + 1, y - 1] + temp[x - 1, y + 1] + temp[x + 1, y + 1]) * 0.25f;
                    float localAverage = avg4 * 0.75f + diag * 0.25f;
                    float deviation = center - localAverage;

                    if (Mathf.Abs(deviation) <= spikeThreshold)
                        continue;

                    float clampedHeight = localAverage + Mathf.Sign(deviation) * spikeThreshold;
                    heightMap[x, y] = Mathf.Lerp(center, clampedHeight, 0.72f);
                }
            }

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    heightMap[x, y] = Mathf.Clamp(heightMap[x, y], -normalizedMaxHeight, normalizedMaxHeight);
        }

        float ClampTerrainHeight(float height)
        {
            return Mathf.Clamp(height, -normalizedMaxHeight, normalizedMaxHeight);
        }

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

        Vector3 AverageNormals(Vector3 a, Vector3 b)
        {
            Vector3 avg = (a + b).normalized;
            if (avg.sqrMagnitude <= 1e-6f)
                return a.sqrMagnitude > 1e-6f ? a.normalized : Vector3.up;

            return avg;
        }

        Vector3 GetSafeTangent(Vector3 normal)
        {
            Vector3 axis = Mathf.Abs(normal.y) < 0.95f ? Vector3.up : Vector3.right;
            return Vector3.Cross(axis, normal).normalized;
        }

        Vector3 ComputeGridNormal(Vector3[,] paddedSpherePoints, float[,] paddedHeightMap, int px, int py)
        {
            Vector3 SamplePaddedPosition(int sx, int sy)
                => BuildStableSurfacePosition(paddedSpherePoints[sx, sy], paddedHeightMap[sx, sy]);

            Vector3 left = SamplePaddedPosition(px - 1, py);
            Vector3 right = SamplePaddedPosition(px + 1, py);
            Vector3 down = SamplePaddedPosition(px, py - 1);
            Vector3 up = SamplePaddedPosition(px, py + 1);

            Vector3 n = Vector3.Cross(right - left, up - down).normalized;
            Vector3 radial = paddedSpherePoints[px, py];
            if (Vector3.Dot(n, radial) < 0f)
                n = -n;

            return n;
        }

        Vector3 OffsetSphereDirection(Vector3 spherePoint, float height, Vector3 tangent, Vector3 bitangent, float tangentOffset, float bitangentOffset)
        {
            float sampleRadius = radius + ClampTerrainHeight(height);
            Vector3 offsetPos =
                spherePoint.normalized * sampleRadius +
                tangent * tangentOffset +
                bitangent * bitangentOffset;

            return offsetPos.normalized;
        }

        Vector3 ComputeSurfaceNormal(Vector3 spherePoint, float height, float cellSize)
        {
            Vector3 radial = spherePoint.normalized;
            Vector3 tangent = GetSafeTangent(radial);
            Vector3 bitangent = Vector3.Cross(radial, tangent).normalized;
            float sampleDistance = Mathf.Max(normalSampleDistance, cellSize * 0.85f);

            Vector3 leftDir = OffsetSphereDirection(radial, height, tangent, bitangent, -sampleDistance, 0f);
            Vector3 rightDir = OffsetSphereDirection(radial, height, tangent, bitangent, sampleDistance, 0f);
            Vector3 downDir = OffsetSphereDirection(radial, height, tangent, bitangent, 0f, -sampleDistance);
            Vector3 upDir = OffsetSphereDirection(radial, height, tangent, bitangent, 0f, sampleDistance);

            Vector3 left = BuildStableSurfacePosition(leftDir, EvaluateMacroHeightCached(leftDir));
            Vector3 right = BuildStableSurfacePosition(rightDir, EvaluateMacroHeightCached(rightDir));
            Vector3 down = BuildStableSurfacePosition(downDir, EvaluateMacroHeightCached(downDir));
            Vector3 up = BuildStableSurfacePosition(upDir, EvaluateMacroHeightCached(upDir));

            Vector3 n = Vector3.Cross(right - left, up - down).normalized;
            if (Vector3.Dot(n, radial) < 0f)
                n = -n;

            return n;
        }

        Vector3 ApplyMicroNormal(Vector3 spherePoint, Vector3 baseNormal, float height, int lod)
        {
            if (terrainStyle == TerrainStyle.SebInspired)
                return baseNormal;

            float lodFactor = lod == 0 ? 1f : (lod == 1 ? 0.55f : (lod == 2 ? 0.28f : 0.14f));
            float microNormalAmount = Mathf.Clamp01((detailStrength * 1.1f + microStrength * 1.4f) * lodFactor);
            if (microNormalAmount <= 0.001f)
                return baseNormal;

            Vector3 radial = spherePoint.normalized;
            float slope01 = 1f - Mathf.Clamp01(Vector3.Dot(baseNormal, radial));
            float landMask = Mathf.SmoothStep(
                seaLevelHeight + beachWidthHeight * 0.25f,
                seaLevelHeight + beachWidthHeight + normalizedMaxHeight * 0.04f,
                height);
            float slopeMask = Mathf.SmoothStep(0.05f, 0.32f, slope01);
            float slopeBias = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(detailSlopeBoost));
            float altitudeMask = Mathf.SmoothStep(
                seaLevelHeight + beachWidthHeight,
                normalizedMaxHeight * 0.62f,
                height);
            float mask = landMask * Mathf.Lerp(0.20f, 1f, slopeMask * slopeBias) * Mathf.Lerp(0.55f, 1f, altitudeMask);
            if (mask <= 0.001f)
                return baseNormal;

            Vector3 tangent = GetSafeTangent(radial);
            Vector3 bitangent = Vector3.Cross(radial, tangent).normalized;

            float styleScale = GetStylizedFrequencyScale();
            float macroFreq = 0.00115f * styleScale * (28f / Mathf.Max(8f, detailScale));
            float fineFreq = 0.0028f * styleScale * (80f / Mathf.Max(16f, microScale));
            float epsMacro = Mathf.Max(3.5f, radius * 0.0012f);
            float epsFine = Mathf.Max(1.8f, radius * 0.00055f);
            Vector3 worldP = spherePoint * radius + seedOffset * 91.37f;

            float baseMacro = FBM(worldP * macroFreq, 2, 0.45f, 1.92f, seed + 9001);
            float macroX = FBM((worldP + tangent * epsMacro) * macroFreq, 2, 0.45f, 1.92f, seed + 9001);
            float macroY = FBM((worldP + bitangent * epsMacro) * macroFreq, 2, 0.45f, 1.92f, seed + 9001);

            float baseFine = Perlin3D(worldP * fineFreq);
            float fineX = Perlin3D((worldP + tangent * epsFine) * fineFreq);
            float fineY = Perlin3D((worldP + bitangent * epsFine) * fineFreq);

            float dx = (macroX - baseMacro) * 0.75f + (fineX - baseFine) * 0.25f;
            float dy = (macroY - baseMacro) * 0.75f + (fineY - baseFine) * 0.25f;

            float normalStrength = microNormalAmount * mask * Mathf.Lerp(0.65f, 1.10f, slopeMask * slopeBias);
            Vector3 detailNormal = (
                baseNormal
                - tangent * dx * normalStrength * 6.2f
                - bitangent * dy * normalStrength * 6.2f).normalized;

            return Vector3.Slerp(baseNormal, detailNormal, mask * Mathf.Lerp(0.35f, 0.85f, slopeMask * slopeBias));
        }

        float GetStylizedFrequencyScale()
        {
            return Mathf.Lerp(1.22f, 0.92f, Mathf.InverseLerp(1800f, 10000f, radius));
        }

        float EvaluateForestDensity(Vector3 dir, float height)
        {
            float beachTop = seaLevelHeight + beachWidthHeight;
            if (height <= beachTop || height >= normalizedMaxHeight * 0.82f)
                return 0f;

            float styleScale = GetStylizedFrequencyScale();
            Vector3 worldDir = dir * radius;

            float cluster = FBM(
                worldDir * (forestNoiseScale * styleScale) + seedOffset * 0.004f,
                4, 0.54f, 2.08f, seed + 77777) * 0.5f + 0.5f;

            float moisture = FBM(
                worldDir * (forestNoiseScale * 0.58f * styleScale) + seedOffset * 0.002f,
                3, 0.5f, 2.0f, seed + 88888) * 0.5f + 0.5f;

            float forestMask = Mathf.Lerp(cluster, moisture, 0.38f);
            forestMask = Mathf.SmoothStep(
                Mathf.Clamp01(forestCoverage - 0.18f),
                Mathf.Clamp01(forestCoverage + 0.18f),
                forestMask);

            float lowlandMask = Mathf.SmoothStep(beachTop, beachTop + normalizedMaxHeight * 0.05f, height);
            float treeLineMask = 1f - Mathf.SmoothStep(normalizedMaxHeight * 0.50f, normalizedMaxHeight * 0.82f, height);

            return Mathf.Clamp01(forestMask * lowlandMask * treeLineMask * (0.90f + forestDensityBoost * 1.1f));
        }

        Color GetBiomeColor(Vector3 dir, float height)
        {
            // ── 1. Вода / дно ──────────────────────────────────────────────────────
            // Подводный ландшафт не должен "синеть/зеленеть" от воды — его перекрывает океан-шейдер.
            // Поэтому дно красим в песок (слегка темнее в глубине).
            Color floorDeepColor    = generateOcean ? Color.Lerp(sandColor, rockColor, 0.35f) : new Color(0.52f, 0.42f, 0.26f);
            Color floorShallowColor = generateOcean ? sandColor : new Color(0.68f, 0.58f, 0.38f);

            float deepThreshold = Mathf.Lerp(oceanDepth, seaLevelHeight, 0.35f);
            if (height < deepThreshold) return floorDeepColor;
            if (height < seaLevelHeight)
                return Color.Lerp(floorDeepColor, floorShallowColor,
                    Mathf.InverseLerp(oceanDepth, seaLevelHeight, height));

            // ── 2. Пляж ────────────────────────────────────────────────────────────
            float beachTop = seaLevelHeight + beachWidthHeight;
            if (height < beachTop)
                return Color.Lerp(floorShallowColor, sandColor,
                    Mathf.InverseLerp(seaLevelHeight, beachTop, height));

            // ── 3. NMS-стиль: крупные региональные пятна (масштаб континентов) ────
            //
            // ПРИЧИНА прежнего провала: p = dir + seedOffset, |seedOffset| ≈ 100.
            // После умножения на biomeNoiseScale ≈ 0.22 входная точка имела магнитуду ~22
            // → шум семплировался на очень высокой частоте → крошечная рябь, не пятна.
            //
            // РЕШЕНИЕ: используем dir * radius — те же мировые координаты, что и
            // continent noise в EvaluateMacroHeight (частота 0.0006f).
            // При radius ≈ 5000–10000 на планету приходится 2–4 огромных цветных пятна.
            Vector3 worldDir = dir * radius;
            float styleScale = GetStylizedFrequencyScale();

            // Макро-регион (2–3 пятна на полушарие) — определяет основной цвет биома
            float macroRaw = FBM(worldDir * (0.00055f * styleScale) + seedOffset * 0.002f,
                                 4, 0.52f, 2.05f, seed + 55555);
            float macro01 = Mathf.SmoothStep(0.22f, 0.78f, macroRaw * 0.5f + 0.5f);

            // Средний слой (5–8 пятен) — тёмные выступы внутри макро-региона
            float midRaw = FBM(worldDir * (0.0018f * styleScale) + seedOffset * 0.003f,
                               3, 0.50f, 2.10f, seed + 66666);
            float mid01 = Mathf.SmoothStep(0.38f, 0.72f, midRaw * 0.5f + 0.5f);

            // Финальный вес тёмных пятен: macro + mid
            float darkWeight = macro01 * 0.70f + mid01 * 0.30f;
            float forestDensity = EvaluateForestDensity(dir, height);

            // Базовый цвет суши: grassColor (светлый/насыщенный) ↔ forestColor (тёмный)
            // Именно это создаёт NMS-характерные «острова» тёмного цвета на ярком фоне
            Color primaryColor = Color.Lerp(grassColor, forestColor, Mathf.Clamp01(Mathf.Max(darkWeight, forestDensity * 0.95f)));

            // ── 4. Высотные переходы — вершины остаются травянистыми, камень задаём по уклону ──
            float landT = Mathf.InverseLerp(beachTop, normalizedMaxHeight, height);
            Color highlandGrass = Color.Lerp(primaryColor, grassColor, 0.42f);
            highlandGrass = Color.Lerp(highlandGrass, sandColor, 0.08f);
            Color baseColor = Color.Lerp(primaryColor, highlandGrass, Mathf.SmoothStep(0.58f, 0.88f, landT));
            float exposedRock = Mathf.SmoothStep(0.62f, 0.96f, landT);
            Color highRockColor = Color.Lerp(rockColor, mesaRockColor, Mathf.SmoothStep(0.55f, 0.92f, landT) * 0.30f);
            baseColor = Color.Lerp(baseColor, highRockColor, exposedRock * 0.28f);

            // ── 5. Полярные шапки ─────────────────────────────────────────────────
            if (enableSnow)
            {
                float lat = Mathf.Abs(dir.y);
                float polarSnow = Mathf.SmoothStep(0.58f, 0.80f, lat);
                baseColor = Color.Lerp(baseColor, snowColor, polarSnow);
            }

            return baseColor;
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

        // PERF: SmoothHeightMapInPlace() удалён — слишком дорогой для генерации в рантайме.

        void AddSkirts(int lod, int vpe, int vEdge, List<Vector3> vertices, List<int> triangles, List<Vector3> normals, List<Color> colors, List<Vector2> uvs)
        {
            if (terrainStyle == TerrainStyle.SebInspired && lod <= 0)
                return;

            float skirtDepth = terrainStyle == TerrainStyle.SebInspired
                ? Mathf.Max(0.35f, normalizedMaxHeight * 0.004f)
                : Mathf.Max(12f, normalizedMaxHeight * 0.14f);
            void AddEdge(int idxA, int idxB)
            {
                Vector3 va = vertices[idxA];
                Vector3 vb = vertices[idxB];
                Vector3 da = va.normalized;
                Vector3 db = vb.normalized;
                int start = vertices.Count;
                vertices.Add(va - da * skirtDepth);
                vertices.Add(vb - db * skirtDepth);
                uvs.Add(uvs[idxA]);
                uvs.Add(uvs[idxB]);
                colors.Add(colors[idxA]);
                colors.Add(colors[idxB]);
                normals.Add(normals[idxA]);
                normals.Add(normals[idxB]);
                triangles.Add(idxA); triangles.Add(idxB); triangles.Add(start);
                triangles.Add(idxB); triangles.Add(start + 1); triangles.Add(start);
            }

            for (int x = 0; x < vpe; x++) AddEdge(x, x + 1);
            for (int y = 0; y < vpe; y++) AddEdge(y * vEdge + vpe, (y + 1) * vEdge + vpe);
            for (int x = vpe; x > 0; x--) AddEdge(vpe * vEdge + x, vpe * vEdge + x - 1);
            for (int y = vpe; y > 0; y--) AddEdge(y * vEdge, (y - 1) * vEdge);
        }

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
            float r = radius + oceanLevel;
            int ve  = oceanResolution + 1;
            for (int f = 0; f < 6; f++)
            {
                int bi = verts.Count;
                for (int y = 0; y < ve; y++)
                    for (int x = 0; x < ve; x++)
                    {
                        float u = -1f + (float)x / oceanResolution * 2f;
                        float v = -1f + (float)y / oceanResolution * 2f;
                        var sp  = GetCubePoint((Face)f, u, v).normalized;
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
                Gizmos.color = new Color(0f, 1f, 0f, 0.15f); Gizmos.DrawWireSphere(transform.position, lodDistance0);
                Gizmos.color = new Color(1f, 1f, 0f, 0.15f); Gizmos.DrawWireSphere(transform.position, lodDistance1);
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.15f); Gizmos.DrawWireSphere(transform.position, lodDistance2);
                Gizmos.color = new Color(1f, 0f, 0f, 0.15f); Gizmos.DrawWireSphere(transform.position, lodDistance3);
            }
        }

        void OnDestroy()
        {
            foreach (var kvp in chunks)
                if (kvp.Value.mesh != null) Destroy(kvp.Value.mesh);
            chunks.Clear();
        }
    }
} // конец namespace PlanetGeneration
