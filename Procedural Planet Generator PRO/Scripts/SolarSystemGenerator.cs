using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace PlanetGeneration
{
    /// <summary>
    /// Главный файл SolarSystemGenerator.
    /// Остальные части: .Warp, .UI.
    /// BiomeData (BiomePreset, GameUI) — отдельный файл BiomeData.cs.
    /// </summary>
    public partial class SolarSystemGenerator : MonoBehaviour
    {
        [Header("System")]
        public int systemSeed = 42;
        [Range(2, 8)] public int planetCount = 5;
        [Tooltip("Если включено, цвета планет генерируются полностью случайно")]
        public bool useRandomColors = false;

        [Header("Sun")]
        public float sunRadius = 2000f;
        public Color sunColor  = new Color(1f, 0.95f, 0.7f);
        public float sunLightIntensity = 1.4f;
        public Material sunMaterial;

        [Header("Orbit")]
        public float minOrbitRadius = 30000f;
        public float orbitSpacing   = 24000f;
        [Tooltip("Планеты не будут дальше этого расстояния от центра системы")]
        public float maxOrbitRadius = 70000f;
        [Range(0f, 30f)] public float maxOrbitInclination = 15f;
        public float orbitSpeedMultiplier = 0f;
        [Tooltip("Keeps the current planet stable in world space so the player does not fight orbit motion and moving terrain colliders.")]
        public bool stabilizeActivePlanet = true;

        [Header("Planet Sizes")]
        public float minPlanetRadius = 5000f;
        public float maxPlanetRadius = 10000f;

        [Header("References")]
        public Camera    mainCamera;
        public Transform player;
        public Material  planetMaterialTemplate;
        public Material  oceanMaterialTemplate;
        public GameObject treePrefab;
        public ChunkManager chunkManager;

        [Header("Atmosphere")]
        public Material atmosphereMaterial;

        [Header("Surface Detail")]
        public Texture2D terrainNormalMap;

        [Header("Streaming / Travel")]
        public bool  disableTrees    = true;
        public bool  seamlessWarp    = true;
        [Range(8f, 45f)] public float seamlessWarpTimeout = 22f;

        [Header("Warp UI")]
        public Canvas travelCanvas;
        public float  warpDuration = 2.5f;

        [System.Serializable]
        public class PlanetEntry
        {
            public string             planetName;
            public GameObject         root;
            public PlanetGenerator    generator;
            public SolarPlanet        solarPlanet;
            public PlanetLODController lodController;
            public float orbitRadius, orbitSpeed, orbitAngle, orbitInclination;
            public Color atmosphereColor;
            public bool  hasTrees, hasOcean;
        }

        public List<PlanetEntry> planets = new List<PlanetEntry>();
        public GameObject sun;

        // ── Internal ──────────────────────────────────────────────────────────
        private Light        _sunLight;
        private bool         _isWarping  = false;
        private bool         _isSpawning = false;
        private PlanetEntry  _currentPlanet;
        private Transform    _anchoredPlanetTransform;
        private Vector3      _anchoredPlanetWorldPosition;
        private bool         _orbitAnchorReady = false;
        private GameObject   _loadingScreen;
        private UnityEngine.UI.Text _loadingText;
        private bool         _showWarpHUD  = false;
        private GUIStyle     _hudBtnStyle;
        private GUIStyle     _hudLabelStyle;
        private bool         _stylesReady  = false;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        void Start()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (player == null && mainCamera != null) player = mainCamera.transform;
            if (terrainNormalMap == null)
                terrainNormalMap = Resources.Load<Texture2D>("PlanetSurfaceNormal");

            chunkManager = ChunkManager.EnsureExists(transform);
            chunkManager.maxActiveTerrainPlanets              = 1;
            chunkManager.maxPrewarmTerrainPlanets             = 3;
            chunkManager.maxConcurrentChunkBuildsPerPlanet    = 3;

            ConfigureRuntimeVisuals();
            Random.InitState(systemSeed);
            CreateSun();
            DisableSceneLightsExceptSun();
            GeneratePlanets();

            if (player != null) player.gameObject.SetActive(false);
            if (planets.Count > 0) StartCoroutine(SpawnPlayerOnPlanet(planets[0]));
            if (orbitSpeedMultiplier > 0.0001f)
                StartCoroutine(OrbitLoop());
        }

        void Update()
        {
            if (_isWarping || _isSpawning) return;
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                _showWarpHUD = !_showWarpHUD;
                GameUI.IsOpen = _showWarpHUD;
                Cursor.lockState = _showWarpHUD ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible   = _showWarpHUD;
            }
            if (_showWarpHUD && Input.GetKeyDown(KeyCode.Escape)) CloseWarpHUD();
            if (_showWarpHUD)
                for (int i = 0; i < planets.Count && i < 8; i++)
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i)) { CloseWarpHUD(); StartWarp(i); break; }
        }

        void OnDestroy()
        {
            StopAllCoroutines();
            if (sun != null) Destroy(sun);
            foreach (var p in planets) if (p != null && p.root != null) Destroy(p.root);
            planets.Clear();
        }

        void OnDrawGizmosSelected()
        {
            for (int i = 0; i < planetCount; i++)
            {
                Gizmos.color = new Color(1, 1, 0, 0.25f);
                DrawGizmoCircle(Vector3.zero, minOrbitRadius + i * orbitSpacing, 64);
            }
            foreach (var p in planets)
            {
                Gizmos.color = p.atmosphereColor;
                Gizmos.DrawWireSphere(p.root ? p.root.transform.position : Vector3.zero,
                    p.generator ? p.generator.radius : 200f);
            }
        }

        void DrawGizmoCircle(Vector3 center, float radius, int segments)
        {
            float step = 360f / segments;
            Vector3 prev = center + new Vector3(radius, 0, 0);
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * step * Mathf.Deg2Rad;
                Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }

        // ── Sun ───────────────────────────────────────────────────────────────

        void CreateSun()
        {
            sun = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sun.name = "Sun";
            sun.transform.parent        = transform;
            sun.transform.localPosition = Vector3.zero;
            sun.transform.localScale    = Vector3.one * (sunRadius * 2f);
            Destroy(sun.GetComponent<Collider>());

            if (sunMaterial != null)
            {
                var mr = sun.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = sunMaterial;
            }

            var lightGo = new GameObject("SunLight");
            lightGo.transform.parent   = sun.transform;
            lightGo.transform.localPosition = Vector3.zero;
            _sunLight = lightGo.AddComponent<Light>();
            _sunLight.type      = LightType.Directional;
            _sunLight.color     = sunColor;
            _sunLight.intensity = sunLightIntensity;
            _sunLight.shadows   = LightShadows.Soft;
            _sunLight.shadowStrength    = 0.82f;
            _sunLight.shadowBias        = 0.02f;
            _sunLight.shadowNormalBias  = 0.08f;
            _sunLight.shadowNearPlane   = 0.4f;
        }

        void DisableSceneLightsExceptSun()
        {
            foreach (var light in FindObjectsOfType<Light>())
                if (light != null && light != _sunLight) light.enabled = false;
        }

        // ── Visual configuration ──────────────────────────────────────────────

        void ConfigureRuntimeVisuals()
        {
            RenderSettings.skybox       = null;
            RenderSettings.ambientMode  = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor     = new Color(0.78f, 0.82f, 0.94f);
            RenderSettings.ambientEquatorColor = new Color(0.58f, 0.46f, 0.40f);
            RenderSettings.ambientGroundColor  = new Color(0.26f, 0.20f, 0.19f);
            RenderSettings.ambientIntensity    = 1.30f;
            RenderSettings.fog         = true;
            RenderSettings.fogMode     = FogMode.ExponentialSquared;
            RenderSettings.fogColor    = new Color(0.76f, 0.80f, 0.92f);
            RenderSettings.fogDensity  = 0.00020f;

            if (mainCamera != null)
            {
                mainCamera.clearFlags       = CameraClearFlags.SolidColor;
                mainCamera.backgroundColor  = new Color(0.76f, 0.80f, 0.92f);
                mainCamera.allowHDR         = true;
                mainCamera.farClipPlane     = Mathf.Max(mainCamera.farClipPlane, maxOrbitRadius * 2.35f);
            }

            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            QualitySettings.shadowCascades   = 4;
            QualitySettings.shadowDistance   = Mathf.Max(QualitySettings.shadowDistance, 2200f);
        }

        void ApplyPlanetVisualProfile(PlanetEntry p)
        {
            if (p == null || p.generator == null) return;
            Color atmo = p.atmosphereColor; atmo.a = 1f;
            Color sky  = Color.Lerp(atmo, Color.white, 0.22f);
            if (p.hasOcean) sky = Color.Lerp(sky, p.generator.shallowWaterColor, 0.22f);
            Color equator = Color.Lerp(p.generator.sandColor, p.generator.grassColor, 0.35f);
            Color ground  = Color.Lerp(p.generator.rockColor, Color.black, 0.30f);

            RenderSettings.skybox      = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor     = Color.Lerp(sky, Color.white, 0.24f);
            RenderSettings.ambientEquatorColor = equator * 0.88f;
            RenderSettings.ambientGroundColor  = ground * 0.62f;
            RenderSettings.ambientIntensity    = 1.34f;
            RenderSettings.fog        = true;
            RenderSettings.fogMode    = FogMode.ExponentialSquared;
            RenderSettings.fogColor   = Color.Lerp(sky, p.generator.sandColor, 0.22f);
            RenderSettings.fogDensity = Mathf.Lerp(0.00046f, 0.00012f,
                Mathf.InverseLerp(1500f, 10000f, p.generator.radius));

            if (mainCamera != null)
            {
                mainCamera.clearFlags      = CameraClearFlags.SolidColor;
                mainCamera.backgroundColor = sky;
                mainCamera.nearClipPlane   = 0.2f;
                mainCamera.farClipPlane    = Mathf.Max(1500f,
                    Mathf.Max(p.generator.unloadDistance * 1.15f, maxOrbitRadius * 2.35f));
            }

            if (_sunLight != null)
            {
                _sunLight.color         = Color.Lerp(sunColor, sky, 0.16f);
                _sunLight.intensity     = 1.36f;
                _sunLight.shadowStrength    = 0.8f;
                _sunLight.shadowBias        = 0.018f;
                _sunLight.shadowNormalBias  = 0.07f;
            }

            QualitySettings.shadowDistance = Mathf.Clamp(p.generator.radius * 2.4f, 900f, 5000f);
        }

        void SetActivePlanet(PlanetEntry activePlanet)
        {
            foreach (var planet in planets)
                if (planet?.solarPlanet != null)
                    planet.solarPlanet.SetActivePlanet(planet == activePlanet);
        }

        bool ShouldStabilizeActivePlanet()
        {
            return stabilizeActivePlanet &&
                !_isWarping &&
                !_isSpawning &&
                _currentPlanet != null &&
                _currentPlanet.root != null;
        }

        void RefreshOrbitAnchor(bool forceReset = false)
        {
            if (!ShouldStabilizeActivePlanet())
            {
                _anchoredPlanetTransform = null;
                _orbitAnchorReady = false;
                return;
            }

            Transform target = _currentPlanet.root.transform;
            if (!forceReset && _orbitAnchorReady && _anchoredPlanetTransform == target)
                return;

            _anchoredPlanetTransform = target;
            _anchoredPlanetWorldPosition = target.position;
            _orbitAnchorReady = true;
        }

        void MaintainActivePlanetAnchor()
        {
            if (!ShouldStabilizeActivePlanet())
            {
                _anchoredPlanetTransform = null;
                _orbitAnchorReady = false;
                return;
            }

            Transform target = _currentPlanet.root.transform;
            if (!_orbitAnchorReady || _anchoredPlanetTransform != target)
            {
                _anchoredPlanetTransform = target;
                _anchoredPlanetWorldPosition = target.position;
                _orbitAnchorReady = true;
                return;
            }

            Vector3 worldDelta = _anchoredPlanetWorldPosition - target.position;
            if (worldDelta.sqrMagnitude > 1e-8f)
                transform.position += worldDelta;

            _anchoredPlanetWorldPosition = target.position;
        }

        // ── Orbit ─────────────────────────────────────────────────────────────

        Vector3 OrbitPosition(float radius, float angle, float inclination)
        {
            float rad  = angle * Mathf.Deg2Rad;
            float incl = inclination * Mathf.Deg2Rad;
            return new Vector3(
                radius * Mathf.Cos(rad),
                radius * Mathf.Sin(rad) * Mathf.Sin(incl),
                radius * Mathf.Sin(rad) * Mathf.Cos(incl));
        }

        IEnumerator OrbitLoop()
        {
            while (true)
            {
                float dt = Time.deltaTime;
                foreach (var p in planets)
                {
                    if (p == null || p.root == null) continue;
                    p.orbitAngle += p.orbitSpeed * dt;
                    p.root.transform.localPosition =
                        OrbitPosition(p.orbitRadius, p.orbitAngle, p.orbitInclination);
                }
                MaintainActivePlanetAnchor();
                yield return null;
            }
        }

        // ── Planet generation ─────────────────────────────────────────────────

        void GeneratePlanets()
        {
            string[] names = GeneratePlanetNames(planetCount);
            for (int i = 0; i < planetCount; i++)
            {
                int planetSeed = systemSeed * 1000 + i * 137;
                Random.InitState(planetSeed);

                bool forceStarterLush = (i == 0);
                BiomePreset biome;
                if (useRandomColors && !forceStarterLush)
                {
                    float baseHue = Random.value;
                    float accentHue = Mathf.Repeat(baseHue + Random.Range(0.18f, 0.42f), 1f);
                    biome = new BiomePreset("Procedural",
                        Color.HSVToRGB(Mathf.Repeat(baseHue + 0.55f, 1f), Random.Range(0.72f, 0.95f), Random.Range(0.40f, 0.72f)),
                        Color.HSVToRGB(Mathf.Repeat(baseHue + 0.50f, 1f), Random.Range(0.62f, 0.88f), Random.Range(0.72f, 0.98f)),
                        Color.HSVToRGB(Mathf.Repeat(baseHue + Random.Range(-0.08f, 0.08f), 1f), Random.Range(0.45f, 0.72f), Random.Range(0.68f, 0.96f)),
                        Color.HSVToRGB(baseHue, Random.Range(0.70f, 0.96f), Random.Range(0.72f, 0.98f)),
                        Color.HSVToRGB(accentHue, Random.Range(0.72f, 0.98f), Random.Range(0.48f, 0.84f)),
                        Color.HSVToRGB(Mathf.Repeat(baseHue + Random.Range(-0.04f, 0.04f), 1f), Random.Range(0.18f, 0.42f), Random.Range(0.58f, 0.86f)),
                        Color.HSVToRGB(Mathf.Repeat(baseHue + 0.06f, 1f), Random.Range(0.04f, 0.18f), Random.Range(0.90f, 1.00f)),
                        Color.HSVToRGB(Mathf.Repeat(baseHue + 0.50f, 1f), Random.Range(0.35f, 0.70f), 1.00f));
                    biome.atmo.a = Random.Range(0.35f, 0.65f);
                }
                else
                {
                    biome = forceStarterLush
                        ? BiomeTable.All[Random.Range(0, 2) == 0 ? 0 : 5]
                        : BiomeTable.All[i % BiomeTable.All.Length];
                }

                float orbitRadius = Mathf.Min(
                    minOrbitRadius + i * orbitSpacing + Random.Range(-orbitSpacing * 0.2f, orbitSpacing * 0.2f),
                    maxOrbitRadius);
                float startAngle   = Random.Range(0f, 360f);
                float inclination  = Random.Range(-maxOrbitInclination, maxOrbitInclination);
                float orbitSpeed   = orbitSpeedMultiplier * (10f / Mathf.Sqrt(orbitRadius / 1000f));
                float pRadius      = Random.Range(minPlanetRadius, maxPlanetRadius);
                bool  hasOcean     = true;
                bool  hasTrees     = !disableTrees && (forceStarterLush || Random.value > 0.15f);

                var pRoot = new GameObject($"Planet_{names[i]}");
                pRoot.transform.parent        = transform;
                pRoot.transform.localPosition = OrbitPosition(orbitRadius, startAngle, inclination);

                var pg = pRoot.AddComponent<PlanetGenerator>();
                ConfigurePlanetGenerator(pg, planetSeed, pRadius, biome, hasOcean, hasTrees, forceStarterLush);

                var proxyRenderer  = pRoot.AddComponent<PlanetRenderer>();
                var lodController  = pRoot.AddComponent<PlanetLODController>();
                var solarPlanet    = pRoot.AddComponent<SolarPlanet>();

                lodController.terrainGenerator  = pg;
                lodController.planetRenderer    = proxyRenderer;
                lodController.mainCamera        = mainCamera;
                lodController.viewer            = player;
                lodController.farToMediumDistanceMultiplier    = 36f;
                lodController.mediumToPrewarmDistanceMultiplier = 12f;
                lodController.prewarmToTerrainDistanceMultiplier = 6.25f;
                lodController.hysteresis          = 0.24f;
                lodController.forcedTerrainSeconds = Mathf.Max(18f, seamlessWarpTimeout + 2f);
                lodController.solarPlanet         = solarPlanet;

                solarPlanet.terrainGenerator  = pg;
                solarPlanet.planetRenderer    = proxyRenderer;
                solarPlanet.lodController     = lodController;
                solarPlanet.activeLodSystem   = true;
                solarPlanet.lod0Radius        = 2.4f;
                solarPlanet.lodTransitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
                solarPlanet.distantRenderMode  = DistantRenderMode.FullTerrain;
                solarPlanet.proxyDetailLevel   = ProxyDetailLevel.High;
                solarPlanet.visibilityThreshold = Mathf.Max(pRadius * 42f, orbitSpacing * 4.0f);
                solarPlanet.SetActivePlanet(false);
                solarPlanet.ApplySettings();

                if (atmosphereMaterial != null)
                    SpawnAtmosphere(pRoot, names[i], pRadius, biome, forceStarterLush);

                planets.Add(new PlanetEntry
                {
                    planetName       = names[i],
                    root             = pRoot,
                    generator        = pg,
                    solarPlanet      = solarPlanet,
                    lodController    = lodController,
                    orbitRadius      = orbitRadius,
                    orbitSpeed       = orbitSpeed,
                    orbitAngle       = startAngle,
                    orbitInclination = inclination,
                    atmosphereColor  = biome.atmo,
                    hasTrees         = hasTrees,
                    hasOcean         = hasOcean,
                });
            }
        }

        void ConfigurePlanetGenerator(PlanetGenerator pg, int planetSeed, float pRadius,
                                      BiomePreset biome, bool hasOcean, bool hasTrees, bool forceStarterLush)
        {
            pg.autoStartStreaming = false;
            pg.mainCamera   = mainCamera;
            pg.player       = player;
            pg.seed         = planetSeed;
            pg.radius       = Mathf.Max(100f, pRadius);
            pg.planetSettings.radius         = pg.radius;
            pg.planetSettings.maxHeightRatio = forceStarterLush ? Random.Range(0.18f, 0.24f) : Random.Range(0.20f, 0.30f);
            pg.planetSettings.seaLevelRatio  = hasOcean ? Random.Range(0.003f, 0.012f) : 0.001f;
            pg.planetSettings.beachWidthRatio = Random.Range(0.005f, 0.018f);

            pg.sebPlateCount             = Random.Range(16, 34);
            pg.sebLandRatio              = hasOcean ? Random.Range(0.32f, 0.54f) : Random.Range(0.48f, 0.72f);
            pg.sebPlateJitter            = Random.Range(0.08f, 0.22f);
            pg.sebPlateBlend             = Random.Range(0.48f, 0.72f);
            pg.sebPlateMacroScale        = Random.Range(1.20f, 2.40f);
            pg.sebCoastNoiseScale        = Random.Range(1.60f, 3.20f);
            pg.sebCoastNoiseStrength     = Random.Range(0.10f, 0.22f);
            pg.sebBoundaryMountainStrength = Random.Range(1.25f, 2.10f);

            float radiusRatio = pRadius / 5000f;
            pg.heightMultiplier = Mathf.Max(120f, Random.Range(420f, 760f) * radiusRatio);
            pg.terrainNormalMap = terrainNormalMap;
            pg.treePrefab       = hasTrees ? treePrefab : null;
            pg.treeProbability  = hasTrees ? Random.Range(0.065f, 0.12f) : 0f;
            pg.maxTreeSlope     = hasTrees ? Random.Range(0.28f, 0.38f)  : 0.25f;
            pg.forestCoverage   = hasTrees ? Random.Range(0.34f, 0.56f)  : 0.94f;
            pg.forestDensityBoost = hasTrees ? Random.Range(0.95f, 1.35f) : 0f;
            pg.forestNoiseScale   = Random.Range(0.0013f, 0.0019f);
            pg.maxTreesPerChunk   = hasTrees ? Random.Range(90, 150) : 0;
            pg.generateOcean      = hasOcean;
            pg.seaLevelScale      = Random.Range(0.62f, 0.74f);
            pg.oceanResolution    = 32;

            pg.maxVertsPerChunk = Mathf.RoundToInt(Mathf.Lerp(144f, 184f,
                Mathf.InverseLerp(minPlanetRadius, maxPlanetRadius, pRadius)));
            pg.chunksPerFace   = 10;
            pg.lodLevels       = 4;

            pg.deepWaterColor   = biome.deep;
            pg.shallowWaterColor = biome.shallow;
            pg.sandColor        = biome.sand;
            pg.grassColor       = biome.grass;
            pg.forestColor      = biome.forest;
            pg.rockColor        = biome.rock;
            pg.mesaRockColor    = Color.Lerp(biome.rock, biome.sand, 0.32f);
            pg.snowColor        = biome.snow;
            pg.atmosphereColor  = biome.atmo;
            pg.rockDarkness     = Random.Range(0.12f, 0.28f);

            pg.baseScale             = Random.Range(0.88f, 1.36f);
            pg.ridgeStrength         = Random.Range(1.85f, 2.85f);
            pg.ridgeSharpness        = Random.Range(0.95f, 1.28f);
            pg.geometryOctaves       = 4;
            pg.geometryAmplitudeDecay = Random.Range(0.42f, 0.52f);
            pg.geometryLacunarity    = Random.Range(1.88f, 1.98f);
            pg.highFrequencyDamping  = Random.Range(0.16f, 0.34f);
            pg.maxSlopeDegrees       = Random.Range(42f, 54f);
            pg.slopeLimitPasses      = 1;
            pg.erosionPasses         = 1;
            pg.erosionStrength       = Random.Range(0.03f, 0.10f);
            pg.terrainSmoothing      = Random.Range(0.01f, 0.05f);
            pg.detailStrength        = Random.Range(0.10f, 0.16f);
            pg.microStrength         = Random.Range(0.026f, 0.050f);
            pg.megaRarity            = 0.80f;
            pg.megaStrength          = Random.Range(2.4f, 4.4f);
            pg.mesaStrength          = Random.Range(0.04f, 0.10f);
            pg.mesaFrequency         = Random.Range(0.46f, 0.62f);
            pg.canyonStrength        = Random.Range(0.04f, 0.12f);
            pg.riverStrength         = Random.Range(0.18f, 0.34f);
            pg.slopeRockBlend        = Random.Range(0.46f, 0.72f);
            pg.terrainNormalTiling        = Random.Range(36f, 56f);
            pg.terrainNormalStrength      = Random.Range(0.68f, 0.98f);
            pg.terrainMicroNormalTiling   = Random.Range(140f, 196f);
            pg.terrainMicroNormalStrength = Random.Range(0.24f, 0.42f);
            pg.terrainCavityStrength      = Random.Range(0.70f, 0.98f);
            pg.cloudCoverage  = Random.Range(0.50f, 0.66f);
            pg.cloudDensity   = Random.Range(0.62f, 0.82f);

            pg.noiseSettings.baseScale            = Random.Range(0.52f, 1.00f);
            pg.noiseSettings.mountainStrength     = Random.Range(1.80f, 2.60f);
            pg.noiseSettings.mountainFrequency    = Random.Range(1.9f, 2.9f);
            pg.noiseSettings.mountainMaskThreshold = Random.Range(0.34f, 0.54f);
            pg.noiseSettings.megaMaskThreshold    = Random.Range(0.88f, 0.96f);
            pg.noiseSettings.megaStrength         = Random.Range(1.15f, 1.80f);
            pg.noiseSettings.cliffStrength        = Random.Range(1.55f, 2.40f);
            pg.noiseSettings.cliffThreshold       = Random.Range(0.46f, 0.68f);
            pg.noiseSettings.peakSharpness        = Random.Range(1.45f, 2.10f);
            pg.continentThreshold = hasOcean ? Random.Range(0.26f, 0.42f) : Random.Range(0.30f, 0.52f);
            pg.oceanDepth         = Random.Range(-30f, -55f);

            pg.lodDistance0   = pRadius * 1.55f;
            pg.lodDistance1   = pRadius * 3.1f;
            pg.lodDistance2   = pRadius * 5.9f;
            pg.lodDistance3   = pRadius * 9.6f;
            pg.unloadDistance = pRadius * 12.5f;
            pg.colliderDistance = pRadius * 0.58f;
            pg.updateInterval   = 0.10f;
            pg.maxConcurrentGenerations = Mathf.Max(3, Mathf.Min(4,
                chunkManager != null ? chunkManager.maxConcurrentChunkBuildsPerPlanet : 3));
            pg.SetGenerationBudget(pg.maxConcurrentGenerations);

            if (planetMaterialTemplate != null)
            {
                var pm = new Material(planetMaterialTemplate) { name = $"Planet_{pg.seed}_Mat" };
                pg.planetMaterial = pm;
            }
            if (hasOcean && oceanMaterialTemplate != null)
            {
                var om = new Material(oceanMaterialTemplate) { name = $"Ocean_{pg.seed}_Mat" };
                if (om.HasProperty("_ShallowColor"))      om.SetColor("_ShallowColor",      biome.shallow);
                if (om.HasProperty("_DeepColor"))         om.SetColor("_DeepColor",         biome.deep);
                if (om.HasProperty("_NightShallowColor")) om.SetColor("_NightShallowColor", Color.Lerp(biome.shallow, Color.black, 0.82f));
                if (om.HasProperty("_NightDeepColor"))    om.SetColor("_NightDeepColor",    Color.Lerp(biome.deep, Color.black, 0.88f));
                pg.oceanMaterial = om;
            }

            Color cloudColor  = GenerateCloudColor(biome, forceStarterLush);
            pg.cloudColor       = cloudColor;
            pg.cloudShadowColor = GenerateCloudShadowColor(cloudColor, biome);
        }

        void SpawnAtmosphere(GameObject pRoot, string planetName, float pRadius,
                             BiomePreset biome, bool forceStarterLush)
        {
            var atmo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            atmo.name = "Atmosphere";
            atmo.transform.SetParent(pRoot.transform);
            atmo.transform.localPosition = Vector3.zero;
            float atmoRadius = pRadius * 1.16f;
            atmo.transform.localScale = Vector3.one * (atmoRadius / 0.5f);
            Destroy(atmo.GetComponent<Collider>());
            var mr = atmo.GetComponent<MeshRenderer>();
            if (mr == null) return;
            var atmoMat = new Material(atmosphereMaterial) { name = $"Atmosphere_{planetName}_Mat" };
            atmoMat.SetVector("_PlanetPos",        pRoot.transform.position);
            atmoMat.SetFloat("_PlanetRadius",      pRadius);
            atmoMat.SetFloat("_Intensity",         forceStarterLush ? 1.75f : 1.35f);
            atmoMat.SetFloat("_AtmoHeight",        pRadius * 0.10f);
            atmoMat.SetFloat("_DirectLightBoost",  1.45f);
            if (atmoMat.HasProperty("_Color")) atmoMat.SetColor("_Color", biome.atmo);
            mr.sharedMaterial     = atmoMat;
            mr.shadowCastingMode  = ShadowCastingMode.Off;
            mr.receiveShadows     = false;

            var atmoController = atmo.AddComponent<PlanetaryRendering.Atmosphere.AtmosphereController>();
            atmoController.planet = pRoot.transform;
            atmoController.player = player != null ? player : (mainCamera != null ? mainCamera.transform : null);
            atmoController.atmosphereMaterial = atmoMat;
            atmoController.planetRadius = pRadius;
            atmoController.atmosphereHeight = pRadius * 0.10f;
            atmoController.maxIntensity = forceStarterLush ? 1.75f : 1.35f;
            atmoController.UpdateMaterialImmediate();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        string[] GeneratePlanetNames(int count)
        {
            string[] prefixes = { "Ara", "Vel", "Kro", "Tor", "Zyn", "Eph", "Mal", "Syx" };
            string[] suffixes = { "ius", "on",  "ara", "eth", "yx",  "an",  "or",  "eon" };
            var result = new string[count];
            var rng = new System.Random(systemSeed);
            for (int i = 0; i < count; i++)
                result[i] = prefixes[rng.Next(prefixes.Length)] + suffixes[rng.Next(suffixes.Length)];
            return result;
        }

        Color GenerateCloudColor(BiomePreset biome, bool preferWhite)
        {
            float whiteBias = preferWhite ? 0.86f
                : (Random.value < 0.62f ? 0.84f : Random.Range(0.52f, 0.72f));
            Color theme = Color.Lerp(biome.atmo, biome.shallow, 0.35f);
            theme = Color.Lerp(theme, biome.sand, 0.12f); theme.a = 1f;
            return Color.Lerp(theme, Color.white, whiteBias);
        }

        Color GenerateCloudShadowColor(Color cloudColor, BiomePreset biome)
        {
            Color shadow = Color.Lerp(biome.atmo, biome.deep, 0.42f); shadow.a = 1f;
            return Color.Lerp(shadow, cloudColor * 0.75f, 0.55f);
        }
    }
}
