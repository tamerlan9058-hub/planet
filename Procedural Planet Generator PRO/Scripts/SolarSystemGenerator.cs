using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace PlanetGeneration
{
    public class SolarSystemGenerator : MonoBehaviour
    {
        [Header("System")]
        public int systemSeed = 42;
        [Range(2, 8)]
        public int planetCount = 5;

        [Tooltip("Если включено, цвета планет генерируются полностью случайно")]
        public bool useRandomColors = true;

        [Header("Sun")]
        public float sunRadius = 2000f;
        public Color sunColor = new Color(1f, 0.95f, 0.7f);
        public float sunLightIntensity = 1.4f;
        public Material sunMaterial;

        [Header("Orbit")]
        public float minOrbitRadius = 30000f;
        public float orbitSpacing = 24000f;
        [Tooltip("Планеты не будут дальше этого расстояния от центра системы")]
        public float maxOrbitRadius = 70000f;
        [Range(0f, 30f)]
        public float maxOrbitInclination = 15f;
        public float orbitSpeedMultiplier = 0.5f;

        [Header("Planet Sizes")]
        public float minPlanetRadius = 5000f;
        public float maxPlanetRadius = 10000f;

        [Header("References")]
        public Camera mainCamera;
        public Transform player;
        public Material planetMaterialTemplate;
        public Material oceanMaterialTemplate;
        public GameObject treePrefab;
        public ChunkManager chunkManager;

        [Header("Atmosphere")]
        public Material atmosphereMaterial;

        [Header("Surface Detail")]
        public Texture2D terrainNormalMap;

        [Header("Streaming / Travel")]
        public bool disableTrees = true;
        public bool seamlessWarp = true;
        [Range(8f, 45f)]
        public float seamlessWarpTimeout = 22f;

        [Header("Warp UI")]
        public Canvas travelCanvas;
        public float warpDuration = 2.5f;

        [System.Serializable]
        public class PlanetEntry
        {
            public string planetName;
            public GameObject root;
            public PlanetGenerator generator;
            public SolarPlanet solarPlanet;
            public PlanetLODController lodController;
            public float orbitRadius;
            public float orbitSpeed;
            public float orbitAngle;
            public float orbitInclination;
            public Color atmosphereColor;
            public bool hasTrees;
            public bool hasOcean;
        }

        public List<PlanetEntry> planets = new List<PlanetEntry>();
        public GameObject sun;

        private Light _sunLight;
        private bool _isWarping = false;
        private bool _isSpawning = false;
        private PlanetEntry _currentPlanet;
        private GameObject _loadingScreen;
        private Text _loadingText;
        private bool _showWarpHUD = false;
        private GUIStyle _hudBtnStyle;
        private GUIStyle _hudLabelStyle;
        private bool _stylesReady = false;

        private static readonly BiomePreset[] Biomes = new BiomePreset[]
        {
            // deep,           shallow,          sand,               grass (primary),    forest (dark patches), rock,               snow,               atmo
            new BiomePreset("Terran",   new Color(0.05f,0.15f,0.45f), new Color(0.15f,0.42f,0.70f), new Color(0.78f,0.72f,0.52f), new Color(0.15f,0.58f,0.08f), new Color(0.03f,0.22f,0.03f), new Color(0.38f,0.35f,0.30f), new Color(0.95f,0.95f,1.00f), new Color(0.4f,0.6f,1.0f,0.5f)),
            new BiomePreset("Desert",   new Color(0.35f,0.22f,0.05f), new Color(0.60f,0.42f,0.12f), new Color(0.90f,0.72f,0.35f), new Color(0.88f,0.52f,0.08f), new Color(0.55f,0.25f,0.03f), new Color(0.52f,0.38f,0.22f), new Color(0.92f,0.85f,0.68f), new Color(1.0f,0.75f,0.4f,0.45f)),
            new BiomePreset("Volcanic", new Color(0.45f,0.06f,0.02f), new Color(0.72f,0.18f,0.02f), new Color(0.38f,0.18f,0.10f), new Color(0.75f,0.04f,0.01f), new Color(0.12f,0.04f,0.02f), new Color(0.28f,0.20f,0.16f), new Color(0.90f,0.84f,0.80f), new Color(1.0f,0.35f,0.1f,0.55f)),
            new BiomePreset("Arctic",   new Color(0.08f,0.22f,0.55f), new Color(0.28f,0.58f,0.88f), new Color(0.72f,0.82f,0.90f), new Color(0.48f,0.72f,0.95f), new Color(0.18f,0.40f,0.72f), new Color(0.42f,0.48f,0.55f), new Color(0.97f,0.97f,1.00f), new Color(0.6f,0.85f,1.0f,0.4f)),
            new BiomePreset("Alien",    new Color(0.28f,0.04f,0.48f), new Color(0.52f,0.12f,0.72f), new Color(0.65f,0.52f,0.15f), new Color(0.68f,0.04f,0.78f), new Color(0.22f,0.02f,0.32f), new Color(0.30f,0.22f,0.38f), new Color(0.92f,0.78f,1.00f), new Color(0.8f,0.3f,1.0f,0.5f)),
            new BiomePreset("Jungle",   new Color(0.02f,0.28f,0.18f), new Color(0.08f,0.55f,0.38f), new Color(0.62f,0.55f,0.25f), new Color(0.05f,0.68f,0.05f), new Color(0.01f,0.25f,0.01f), new Color(0.28f,0.32f,0.22f), new Color(0.88f,0.96f,0.82f), new Color(0.3f,0.9f,0.5f,0.45f)),
            new BiomePreset("Ocean",    new Color(0.01f,0.08f,0.45f), new Color(0.05f,0.38f,0.80f), new Color(0.70f,0.65f,0.48f), new Color(0.08f,0.62f,0.68f), new Color(0.02f,0.32f,0.42f), new Color(0.32f,0.38f,0.42f), new Color(0.92f,0.96f,1.00f), new Color(0.2f,0.5f,1.0f,0.6f)),
        };

        void Start()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (player == null && mainCamera != null) player = mainCamera.transform;
            if (terrainNormalMap == null)
                terrainNormalMap = Resources.Load<Texture2D>("PlanetSurfaceNormal");
            chunkManager = ChunkManager.EnsureExists(transform);
            chunkManager.maxActiveTerrainPlanets = 1;
            chunkManager.maxPrewarmTerrainPlanets = 3;
            chunkManager.maxConcurrentChunkBuildsPerPlanet = 3;
            ConfigureRuntimeVisuals();
            Random.InitState(systemSeed);
            CreateSun();
            DisableSceneLightsExceptSun();
            GeneratePlanets();
            if (player != null) player.gameObject.SetActive(false);
            if (planets.Count > 0) StartCoroutine(SpawnPlayerOnPlanet(planets[0]));
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
                Cursor.visible = _showWarpHUD;
            }
            if (_showWarpHUD && Input.GetKeyDown(KeyCode.Escape)) CloseWarpHUD();
            if (_showWarpHUD)
            {
                for (int i = 0; i < planets.Count && i < 8; i++)
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i)) { CloseWarpHUD(); StartWarp(i); break; }
            }
        }

        void CloseWarpHUD()
        {
            _showWarpHUD = false;
            GameUI.IsOpen = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void OnGUI()
        {
            if (!_showWarpHUD && !_isWarping && !_isSpawning && player != null)
            {
                bool inSpace = true;
                foreach (var p in planets)
                {
                    if (p.root == null || p.generator == null) continue;
                    float d = Vector3.Distance(player.position, p.root.transform.position);
                    if (d < p.generator.radius * 2.5f) { inSpace = false; break; }
                }
                if (inSpace)
                {
                    GUIStyle hint = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
                    hint.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
                    GUI.Label(new Rect(Screen.width * 0.5f - 150f, Screen.height - 55f, 300f, 30f), "[ TAB ] — варп к планете", hint);
                }
            }
            if (!_showWarpHUD) return;
            if (!_stylesReady)
            {
                _hudBtnStyle = new GUIStyle(GUI.skin.button) { fontSize = 14, fixedHeight = 38f };
                _hudBtnStyle.normal.textColor = Color.white;
                _hudLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _hudLabelStyle.normal.textColor = Color.white;
                _stylesReady = true;
            }
            float panelW = 340f, rowH = 44f, panelH = 70f + planets.Count * rowH;
            float panelX = (Screen.width - panelW) * 0.5f, panelY = (Screen.height - panelH) * 0.5f;
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(new Rect(panelX - 12f, panelY - 12f, panelW + 24f, panelH + 24f), Texture2D.whiteTexture);
            GUI.color = old;
            GUILayout.BeginArea(new Rect(panelX, panelY, panelW, panelH));
            GUILayout.Label("◈  ВАРП  ◈", _hudLabelStyle, GUILayout.Height(36f));
            GUIStyle sub = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            sub.normal.textColor = new Color(1f, 1f, 1f, 0.55f);
            GUILayout.Label("[TAB] закрыть  ·  [1–8] быстрый варп  ·  [ESC] отмена", sub, GUILayout.Height(18f));
            GUILayout.Space(6f);
            for (int i = 0; i < planets.Count; i++)
            {
                PlanetEntry p = planets[i];
                float dist = 0f;
                if (player != null && p.root != null && p.generator != null)
                    dist = Mathf.Max(0f, Vector3.Distance(player.position, p.root.transform.position) - p.generator.radius);
                string label = $"[{i + 1}]  {p.planetName}   ({dist:F0} ед.)";
                Color btnTint = p.atmosphereColor; btnTint.a = 1f;
                Color prevC = GUI.color;
                GUI.color = Color.Lerp(Color.white, btnTint, 0.5f);
                if (GUILayout.Button(label, _hudBtnStyle))
                {
                    GUI.color = prevC;
                    CloseWarpHUD();
                    StartWarp(i);
                    GUILayout.EndArea();
                    return;
                }
                GUI.color = prevC;
            }
            GUILayout.EndArea();
        }

        void CreateSun()
        {
            sun = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sun.name = "Sun";
            sun.transform.parent = transform;
            sun.transform.localPosition = Vector3.zero;
            sun.transform.localScale = Vector3.one * sunRadius * 2f;
            Destroy(sun.GetComponent<Collider>());
            Material mat = sunMaterial != null ? new Material(sunMaterial) : new Material(Shader.Find("Standard"));
            mat.color = sunColor;
            mat.SetColor("_EmissionColor", sunColor * 3f);
            mat.EnableKeyword("_EMISSION");
            var sunRenderer = sun.GetComponent<MeshRenderer>();
            sunRenderer.sharedMaterial = mat;
            sunRenderer.shadowCastingMode = ShadowCastingMode.Off;
            sunRenderer.receiveShadows = false;
            GameObject lightGO = new GameObject("SunLight");
            lightGO.transform.parent = sun.transform;
            _sunLight = lightGO.AddComponent<Light>();
            _sunLight.type = LightType.Directional;
            _sunLight.color = sunColor;
            _sunLight.intensity = sunLightIntensity;
            _sunLight.shadows = LightShadows.Soft;
            _sunLight.shadowStrength = 0.82f;
            _sunLight.shadowBias = 0.02f;
            _sunLight.shadowNormalBias = 0.08f;
            _sunLight.shadowNearPlane = 0.4f;
        }

        void ConfigureRuntimeVisuals()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.52f, 0.62f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.34f, 0.30f, 0.24f);
            RenderSettings.ambientGroundColor = new Color(0.14f, 0.12f, 0.11f);
            RenderSettings.ambientIntensity = 1.15f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.62f, 0.72f, 0.86f);
            RenderSettings.fogDensity = 0.00032f;

            if (mainCamera != null)
            {
                mainCamera.clearFlags = CameraClearFlags.SolidColor;
                mainCamera.backgroundColor = new Color(0.62f, 0.72f, 0.86f);
                mainCamera.allowHDR = true;
                mainCamera.farClipPlane = Mathf.Max(mainCamera.farClipPlane, maxOrbitRadius * 2.35f);
            }

            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowDistance = Mathf.Max(QualitySettings.shadowDistance, 2200f);
        }

        void DisableSceneLightsExceptSun()
        {
            foreach (var light in FindObjectsOfType<Light>())
            {
                if (light == null || light == _sunLight) continue;
                light.enabled = false;
            }
        }

        void ApplyPlanetVisualProfile(PlanetEntry p)
        {
            if (p == null || p.generator == null) return;

            Color atmo = p.atmosphereColor;
            atmo.a = 1f;

            Color sky = Color.Lerp(atmo, Color.white, 0.22f);
            if (p.hasOcean)
                sky = Color.Lerp(sky, p.generator.shallowWaterColor, 0.22f);

            Color equator = Color.Lerp(p.generator.sandColor, p.generator.grassColor, 0.35f);
            Color ground = Color.Lerp(p.generator.rockColor, Color.black, 0.30f);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(sky, Color.white, 0.12f);
            RenderSettings.ambientEquatorColor = equator * 0.65f;
            RenderSettings.ambientGroundColor = ground * 0.45f;
            RenderSettings.ambientIntensity = 1.2f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Color.Lerp(sky, p.generator.sandColor, 0.10f);
            RenderSettings.fogDensity = Mathf.Lerp(0.00075f, 0.00018f, Mathf.InverseLerp(1500f, 10000f, p.generator.radius));

            if (mainCamera != null)
            {
                mainCamera.clearFlags = CameraClearFlags.SolidColor;
                mainCamera.backgroundColor = sky;
                mainCamera.nearClipPlane = 0.2f;
                mainCamera.farClipPlane = Mathf.Max(1500f, Mathf.Max(p.generator.unloadDistance * 1.15f, maxOrbitRadius * 2.35f));
            }

            if (_sunLight != null)
            {
                _sunLight.color = Color.Lerp(sunColor, sky, 0.08f);
                _sunLight.intensity = 1.18f;
                _sunLight.shadowStrength = 0.8f;
                _sunLight.shadowBias = 0.018f;
                _sunLight.shadowNormalBias = 0.07f;
            }

            QualitySettings.shadowDistance = Mathf.Clamp(p.generator.radius * 2.4f, 900f, 5000f);
        }

        void SetActivePlanet(PlanetEntry activePlanet)
        {
            for (int i = 0; i < planets.Count; i++)
            {
                PlanetEntry planet = planets[i];
                if (planet == null || planet.solarPlanet == null)
                    continue;

                planet.solarPlanet.SetActivePlanet(planet == activePlanet);
            }
        }

        void GeneratePlanets()
        {
            string[] names = GeneratePlanetNames(planetCount);
            for (int i = 0; i < planetCount; i++)
            {
                int planetSeed = systemSeed * 1000 + i * 137;
                Random.InitState(planetSeed);
                
                BiomePreset biome;
                bool forceStarterLush = (i == 0);
                if (useRandomColors && !forceStarterLush)
                {
                    biome = new BiomePreset(
                        "Procedural",
                        Random.ColorHSV(0f, 1f, 0.5f, 1f, 0.1f, 0.4f),   // deep
                        Random.ColorHSV(0f, 1f, 0.4f, 0.9f, 0.5f, 0.9f), // shallow
                        Random.ColorHSV(0f, 1f, 0.1f, 0.6f, 0.5f, 0.9f), // sand
                        Random.ColorHSV(0f, 1f, 0.3f, 0.9f, 0.3f, 0.7f), // grass
                        Random.ColorHSV(0f, 1f, 0.4f, 1.0f, 0.1f, 0.4f), // forest
                        Random.ColorHSV(0f, 1f, 0.0f, 0.4f, 0.2f, 0.6f), // rock
                        Random.ColorHSV(0f, 1f, 0.0f, 0.2f, 0.8f, 1.0f), // snow
                        Random.ColorHSV(0f, 1f, 0.3f, 0.8f, 0.5f, 1.0f)  // atmo
                    );
                    biome.atmo.a = Random.Range(0.35f, 0.65f);
                }
                else
                {
                    biome = forceStarterLush ? Biomes[Random.Range(0, 2) == 0 ? 0 : 5] : Biomes[i % Biomes.Length];
                }

                float orbitRadius = minOrbitRadius + i * orbitSpacing + Random.Range(-orbitSpacing * 0.2f, orbitSpacing * 0.2f);
                orbitRadius = Mathf.Min(orbitRadius, maxOrbitRadius);
                float startAngle = Random.Range(0f, 360f);
                float inclination = Random.Range(-maxOrbitInclination, maxOrbitInclination);
                float orbitSpeed = orbitSpeedMultiplier * (10f / Mathf.Sqrt(orbitRadius / 1000f));
                float pRadius = Random.Range(minPlanetRadius, maxPlanetRadius);
                bool hasOcean = true;
                bool hasTrees = !disableTrees && (forceStarterLush || Random.value > 0.15f);
                GameObject pRoot = new GameObject($"Planet_{names[i]}");
                pRoot.transform.parent = transform;
                Vector3 orbitPos = OrbitPosition(orbitRadius, startAngle, inclination);
                pRoot.transform.localPosition = orbitPos;
                PlanetGenerator pg = pRoot.AddComponent<PlanetGenerator>();
                pg.autoStartStreaming = false;
                pg.mainCamera = mainCamera;
                pg.player = player;
                pg.seed = planetSeed;
                pg.radius = Mathf.Max(100f, pRadius);
                pg.planetSettings.radius = pg.radius;
                pg.planetSettings.maxHeightRatio = forceStarterLush
                    ? Random.Range(0.13f, 0.18f)
                    : Random.Range(0.15f, 0.24f);
                pg.planetSettings.seaLevelRatio = hasOcean ? Random.Range(0.003f, 0.012f) : 0.001f;
                pg.planetSettings.beachWidthRatio = Random.Range(0.005f, 0.018f);
                pg.sebPlateCount = Random.Range(16, 34);
                pg.sebLandRatio = hasOcean ? Random.Range(0.32f, 0.54f) : Random.Range(0.48f, 0.72f);
                pg.sebPlateJitter = Random.Range(0.08f, 0.22f);
                pg.sebPlateBlend = Random.Range(0.48f, 0.72f);
                pg.sebPlateMacroScale = Random.Range(0.90f, 1.85f);
                pg.sebCoastNoiseScale = Random.Range(1.60f, 3.20f);
                pg.sebCoastNoiseStrength = Random.Range(0.10f, 0.22f);
                pg.sebBoundaryMountainStrength = Random.Range(0.90f, 1.45f);
                float radiusRatio = pRadius / 5000f;
                pg.heightMultiplier = Mathf.Max(60f, Random.Range(250f, 400f) * radiusRatio);
                pg.terrainNormalMap = terrainNormalMap;
                pg.treePrefab = hasTrees ? treePrefab : null;
                pg.treeProbability = hasTrees ? Random.Range(0.065f, 0.12f) : 0f;
                pg.maxTreeSlope = hasTrees ? Random.Range(0.28f, 0.38f) : 0.25f;
                pg.forestCoverage = hasTrees ? Random.Range(0.34f, 0.56f) : 0.94f;
                pg.forestDensityBoost = hasTrees ? Random.Range(0.95f, 1.35f) : 0f;
                pg.forestNoiseScale = Random.Range(0.0013f, 0.0019f);
                pg.maxTreesPerChunk = hasTrees ? Random.Range(90, 150) : 0;
                pg.generateOcean = hasOcean;
                pg.seaLevelScale = Random.Range(0.62f, 0.74f);
                pg.oceanResolution = 32;
                pg.maxVertsPerChunk = Mathf.RoundToInt(Mathf.Lerp(144f, 184f, Mathf.InverseLerp(minPlanetRadius, maxPlanetRadius, pRadius)));
                pg.chunksPerFace = 10;
                pg.lodLevels = 4;
                pg.deepWaterColor = biome.deep;
                pg.shallowWaterColor = biome.shallow;
                pg.sandColor = biome.sand;
                pg.grassColor = biome.grass;
                pg.forestColor = biome.forest;
                pg.rockColor = biome.rock;
                pg.mesaRockColor = Color.Lerp(biome.rock, biome.sand, 0.32f);
                pg.snowColor = biome.snow;
                pg.atmosphereColor = biome.atmo;
                pg.rockDarkness = Random.Range(0.44f, 0.58f);
                pg.baseScale = Random.Range(0.72f, 1.18f);
                pg.ridgeStrength = Random.Range(1.35f, 2.05f);
                pg.ridgeSharpness = Random.Range(0.82f, 1.15f);
                pg.geometryOctaves = 4;
                pg.geometryAmplitudeDecay = Random.Range(0.42f, 0.52f);
                pg.geometryLacunarity = Random.Range(1.88f, 1.98f);
                pg.highFrequencyDamping = Random.Range(0.34f, 0.56f);
                pg.maxSlopeDegrees = Random.Range(38f, 46f);
                pg.slopeLimitPasses = 1;
                pg.erosionPasses = 1;
                pg.erosionStrength = Random.Range(0.10f, 0.22f);
                pg.terrainSmoothing = Random.Range(0.05f, 0.16f);
                pg.detailStrength = Random.Range(0.06f, 0.10f);
                pg.microStrength = Random.Range(0.015f, 0.032f);
                pg.megaRarity = 0.80f;
                pg.megaStrength = Random.Range(1.8f, 3.4f);
                pg.mesaStrength = Random.Range(0.06f, 0.14f);
                pg.mesaFrequency = Random.Range(0.46f, 0.62f);
                pg.canyonStrength = Random.Range(0.10f, 0.22f);
                pg.riverStrength = Random.Range(0.18f, 0.34f);
                // NMS-стиль: заметный камень на боках, но вершины остаются травянистыми.
                pg.slopeRockBlend = Random.Range(0.46f, 0.72f);
                pg.terrainNormalTiling = Random.Range(36f, 56f);
                pg.terrainNormalStrength = Random.Range(0.68f, 0.98f);
                pg.terrainMicroNormalTiling = Random.Range(140f, 196f);
                pg.terrainMicroNormalStrength = Random.Range(0.24f, 0.42f);
                pg.terrainCavityStrength = Random.Range(0.70f, 0.98f);
                pg.cloudCoverage = Random.Range(0.50f, 0.66f);
                pg.cloudDensity = Random.Range(0.62f, 0.82f);
                pg.noiseSettings.baseScale = Random.Range(0.35f, 0.8f);
                pg.noiseSettings.mountainStrength = Random.Range(1.30f, 1.95f);
                pg.noiseSettings.mountainFrequency = Random.Range(1.7f, 2.6f);
                pg.noiseSettings.mountainMaskThreshold = Random.Range(0.34f, 0.54f);
                pg.noiseSettings.megaMaskThreshold = Random.Range(0.88f, 0.96f);
                pg.noiseSettings.megaStrength = Random.Range(0.85f, 1.35f);
                pg.noiseSettings.cliffStrength = Random.Range(1.20f, 1.90f);
                pg.noiseSettings.cliffThreshold = Random.Range(0.46f, 0.68f);
                pg.noiseSettings.peakSharpness = Random.Range(1.20f, 1.75f);
                pg.continentThreshold = hasOcean ? Random.Range(0.26f, 0.42f) : Random.Range(0.30f, 0.52f);
                pg.oceanDepth = Random.Range(-30f, -55f);
                pg.lodDistance0 = pRadius * 1.55f;
                pg.lodDistance1 = pRadius * 3.1f;
                pg.lodDistance2 = pRadius * 5.9f;
                pg.lodDistance3 = pRadius * 9.6f;
                pg.unloadDistance = pRadius * 12.5f;
                pg.colliderDistance = pRadius * 0.58f;
                pg.updateInterval = 0.10f;
                pg.maxConcurrentGenerations = Mathf.Max(3, Mathf.Min(4, chunkManager != null ? chunkManager.maxConcurrentChunkBuildsPerPlanet : 3));
                pg.SetGenerationBudget(pg.maxConcurrentGenerations);
                if (planetMaterialTemplate != null)
                {
                    Material pm = new Material(planetMaterialTemplate);
                    pm.name = $"Planet_{names[i]}_Mat";
                    pg.planetMaterial = pm;
                }
                if (hasOcean && oceanMaterialTemplate != null)
                {
                    Material om = new Material(oceanMaterialTemplate);
                    om.name = $"Ocean_{names[i]}_Mat";
                    if (om.HasProperty("_ShallowColor")) om.SetColor("_ShallowColor", biome.shallow);
                    if (om.HasProperty("_DeepColor")) om.SetColor("_DeepColor", biome.deep);
                    if (om.HasProperty("_NightShallowColor")) om.SetColor("_NightShallowColor", Color.Lerp(biome.shallow, Color.black, 0.82f));
                    if (om.HasProperty("_NightDeepColor")) om.SetColor("_NightDeepColor", Color.Lerp(biome.deep, Color.black, 0.88f));
                    pg.oceanMaterial = om;
                }
                Color generatedCloudColor = GenerateCloudColor(biome, forceStarterLush);
                Color generatedCloudShadow = GenerateCloudShadowColor(generatedCloudColor, biome);
                pg.cloudColor = generatedCloudColor;
                pg.cloudShadowColor = generatedCloudShadow;
                PlanetRenderer proxyRenderer = pRoot.AddComponent<PlanetRenderer>();
                PlanetLODController lodController = pRoot.AddComponent<PlanetLODController>();
                SolarPlanet solarPlanet = pRoot.AddComponent<SolarPlanet>();
                lodController.terrainGenerator = pg;
                lodController.planetRenderer = proxyRenderer;
                lodController.mainCamera = mainCamera;
                lodController.viewer = player;
                lodController.farToMediumDistanceMultiplier = 36f;
                lodController.mediumToPrewarmDistanceMultiplier = 12f;
                lodController.prewarmToTerrainDistanceMultiplier = 6.25f;
                lodController.hysteresis = 0.24f;
                lodController.forcedTerrainSeconds = Mathf.Max(18f, seamlessWarpTimeout + 2f);
                lodController.solarPlanet = solarPlanet;
                solarPlanet.terrainGenerator = pg;
                solarPlanet.planetRenderer = proxyRenderer;
                solarPlanet.lodController = lodController;
                solarPlanet.activeLodSystem = true;
                solarPlanet.lod0Radius = 2.4f;
                solarPlanet.lodTransitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
                solarPlanet.distantRenderMode = DistantRenderMode.FullTerrain;
                solarPlanet.proxyDetailLevel = ProxyDetailLevel.High;
                solarPlanet.visibilityThreshold = Mathf.Max(pRadius * 42f, orbitSpacing * 4.0f);
                solarPlanet.SetActivePlanet(false);
                solarPlanet.ApplySettings();
                PlanetEntry entry = new PlanetEntry
                {
                    planetName = names[i],
                    root = pRoot,
                    generator = pg,
                    solarPlanet = solarPlanet,
                    lodController = lodController,
                    orbitRadius = orbitRadius,
                    orbitSpeed = orbitSpeed,
                    orbitAngle = startAngle,
                    orbitInclination = inclination,
                    atmosphereColor = biome.atmo,
                    hasTrees = hasTrees,
                    hasOcean = hasOcean,
                };
                planets.Add(entry);
                if (atmosphereMaterial != null)
                {
                    GameObject atmo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    atmo.name = "Atmosphere";
                    atmo.transform.SetParent(pRoot.transform);
                    atmo.transform.localPosition = Vector3.zero;
                    float atmoRadius = pRadius * 1.16f;
                    atmo.transform.localScale = Vector3.one * (atmoRadius / 0.5f);
                    Destroy(atmo.GetComponent<Collider>());
                    MeshRenderer mr = atmo.GetComponent<MeshRenderer>();
                    if (mr != null)
                    {
                        Material atmoMat = new Material(atmosphereMaterial);
                        atmoMat.name = $"Atmosphere_{names[i]}_Mat";
                        atmoMat.SetVector("_PlanetPos", pRoot.transform.position);
                        atmoMat.SetFloat("_PlanetRadius", pRadius);
                        atmoMat.SetFloat("_Intensity", forceStarterLush ? 1.2f : 0.95f);
                        atmoMat.SetFloat("_AtmoHeight", pRadius * 0.08f);
                        atmoMat.SetFloat("_DirectLightBoost", 1.15f);
                        atmoMat.SetColor("_Color", biome.atmo); 
                        
                        mr.material = atmoMat;
                        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        mr.receiveShadows = false;
                    }
                }

                Shader cloudShader = Shader.Find("Custom/PlanetCloudShader");
                if (cloudShader != null)
                {
                    GameObject cloudLayer = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    cloudLayer.name = "CloudLayer";
                    cloudLayer.transform.SetParent(pRoot.transform);
                    cloudLayer.transform.localPosition = Vector3.zero;
                    float cloudRadius = pRadius * Random.Range(1.045f, 1.078f);
                    cloudLayer.transform.localScale = Vector3.one * (cloudRadius / 0.5f);
                    Destroy(cloudLayer.GetComponent<Collider>());

                    MeshRenderer cloudRenderer = cloudLayer.GetComponent<MeshRenderer>();
                    if (cloudRenderer != null)
                    {
                        Material cloudMat = new Material(cloudShader);
                        cloudMat.name = $"Clouds_{names[i]}_Mat";
                        Vector3 windA = Random.onUnitSphere;
                        windA.y *= 0.25f;
                        if (windA.sqrMagnitude < 0.001f)
                            windA = new Vector3(0.88f, 0.18f, 0.32f);
                        windA.Normalize();

                        Vector3 windB = Vector3.Cross(Vector3.up, windA);
                        if (windB.sqrMagnitude < 0.001f)
                            windB = Vector3.Cross(Vector3.right, windA);
                        windB = (windB.normalized + Random.onUnitSphere * 0.18f).normalized;

                        cloudMat.SetVector("_PlanetCenter", pRoot.transform.position);
                        cloudMat.SetFloat("_CloudScale", Random.Range(3.4f, 5.8f));
                        cloudMat.SetFloat("_Coverage", pg.cloudCoverage);
                        cloudMat.SetFloat("_Softness", Random.Range(0.08f, 0.18f));
                        cloudMat.SetFloat("_Opacity", Mathf.Lerp(0.56f, 0.82f, pg.cloudDensity));
                        cloudMat.SetFloat("_ScrollSpeedA", Random.Range(0.02f, 0.05f));
                        cloudMat.SetFloat("_ScrollSpeedB", Random.Range(0.01f, 0.03f));
                        cloudMat.SetVector("_WindDirectionA", new Vector4(windA.x, windA.y, windA.z, 0f));
                        cloudMat.SetVector("_WindDirectionB", new Vector4(windB.x, windB.y, windB.z, 0f));
                        cloudMat.SetFloat("_LightWrap", Random.Range(0.28f, 0.46f));
                        cloudMat.SetFloat("_SilverLining", Random.Range(0.32f, 0.58f));
                        cloudMat.SetFloat("_InnerOpacity", Mathf.Lerp(0.84f, 0.94f, pg.cloudDensity));
                        cloudMat.SetFloat("_BacklightStrength", Random.Range(0.16f, 0.28f));
                        cloudMat.SetColor("_CloudColor", generatedCloudColor);
                        cloudMat.SetColor("_ShadowColor", generatedCloudShadow);
                        cloudRenderer.sharedMaterial = cloudMat;
                        cloudRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        cloudRenderer.receiveShadows = false;
                    }

                    PlanetCloudLayer cloudController = cloudLayer.AddComponent<PlanetCloudLayer>();
                    cloudController.planet = pRoot.transform;
                    cloudController.cloudMaterial = cloudRenderer != null ? cloudRenderer.sharedMaterial : null;
                    cloudController.rotationAxis = Random.onUnitSphere;
                    if (cloudController.rotationAxis.sqrMagnitude < 0.001f)
                        cloudController.rotationAxis = new Vector3(0.2f, 1f, 0.05f);
                    cloudController.rotationSpeed = Random.Range(0.3f, 1.0f);
                    if (cloudRenderer != null && cloudRenderer.sharedMaterial != null)
                    {
                        Vector4 windA = cloudRenderer.sharedMaterial.GetVector("_WindDirectionA");
                        Vector4 windB = cloudRenderer.sharedMaterial.GetVector("_WindDirectionB");
                        cloudController.windDirectionA = new Vector3(windA.x, windA.y, windA.z);
                        cloudController.windDirectionB = new Vector3(windB.x, windB.y, windB.z);
                    }
                    else
                    {
                        cloudController.windDirectionA = new Vector3(0.88f, 0.18f, 0.32f);
                        cloudController.windDirectionB = new Vector3(-0.36f, 0.08f, 0.92f);
                    }
                }
            }
        }

        IEnumerator SpawnPlayerOnPlanet(PlanetEntry p)
        {
            yield return null; yield return null;
            if (player == null) yield break;
            _isSpawning = true;
            ShowLoadingScreen("Загрузка планеты...");
            var controller = player.GetComponent<PlanetaryFirstPersonController.PlanetaryFirstPersonController>();
            bool hadControllerEnabled = controller != null && controller.enabled;
            if (controller != null)
                controller.enabled = false;
            Vector3 up = Vector3.up;
            Vector3 planetCenter = p.root.transform.position;
            float waitHeight = p.generator.radius + p.generator.heightMultiplier + 200f;
            player.position = planetCenter + up * waitHeight;
            player.rotation = AlignPlayerToSurface(player.rotation, up);
            if (!seamlessWarp)
                player.gameObject.SetActive(false);
            var rb = player.GetComponent<Rigidbody>();
            bool hadGravity = false;
            bool hadKinematic = false;
            if (rb != null)
            {
                hadGravity = rb.useGravity;
                hadKinematic = rb.isKinematic;
                rb.useGravity = false;
                rb.isKinematic = true;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            if (p.lodController != null) p.lodController.ForceTerrainStreaming(10f);
            p.generator.ResetLoadState();
            p.generator.SetFastGeneration(true);
            p.generator.ActivateStreaming(chunkManager != null ? chunkManager.maxConcurrentChunkBuildsPerPlanet : 4);
            yield return new WaitForSeconds(0.5f);
            float genTimeout = 25f;
            float genElapsed = 0f;
            while (!p.generator.IsReady && genElapsed < genTimeout)
            {
                yield return new WaitForSeconds(0.1f);
                genElapsed += 0.1f;
            }
            p.generator.SetFastGeneration(false);
            float rayStart = p.generator.radius + p.generator.heightMultiplier + 60f;
            Vector3 rayOrigin = planetCenter + up * rayStart;
            Vector3 spawnPos = planetCenter + up * (p.generator.radius + p.generator.heightMultiplier + 5f);
            RaycastHit hit;
            float timeout = 10f, elapsed = 0f;
            bool found = false;
            Vector3 spawnUp = up;
            while (elapsed < timeout)
            {
                if (Physics.Raycast(rayOrigin, -up, out hit, rayStart * 2f))
                {
                    spawnUp = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : up;
                    spawnPos = hit.point + spawnUp * 1.5f;
                    found = true;
                    break;
                }
                elapsed += 0.1f;
                yield return new WaitForSeconds(0.1f);
            }
            if (!found) Debug.LogWarning("[SpawnPlayer] Raycast не попал в планету за 10 сек.");
            player.position = spawnPos;
            player.rotation = AlignPlayerToSurface(player.rotation, spawnUp);
            if (rb != null)
            {
                rb.position = spawnPos;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = hadKinematic;
                rb.useGravity = hadGravity;
            }
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
            HideLoadingScreen();
            if (controller != null)
                controller.enabled = hadControllerEnabled;
            _isSpawning = false;
            player.gameObject.SetActive(true);
            _currentPlanet = p;
            SetActivePlanet(p);
            ApplyPlanetVisualProfile(p);
        }

        IEnumerator OrbitLoop()
        {
            while (true) { UpdateSunDirection(); yield return null; }
        }

        Vector3 OrbitPosition(float r, float angle, float inclination)
        {
            float rad = angle * Mathf.Deg2Rad;
            float incl = inclination * Mathf.Deg2Rad;
            float x = r * Mathf.Cos(rad);
            float z = r * Mathf.Sin(rad);
            float y = z * Mathf.Sin(incl);
            z *= Mathf.Cos(incl);
            return new Vector3(x, y, z);
        }

        void UpdateSunDirection()
        {
            if (_sunLight == null || sun == null) return;
            Vector3 target = _currentPlanet?.root != null ? _currentPlanet.root.transform.position : (player ? player.position : Vector3.zero);
            if (target == Vector3.zero) return;
            Vector3 toTarget = target - sun.transform.position;
            if (toTarget == Vector3.zero) return;
            _sunLight.transform.rotation = Quaternion.LookRotation(toTarget.normalized);
        }

        void EnsureCanvas()
        {
            if (travelCanvas != null) return;
            GameObject canvasGO = new GameObject("SystemCanvas");
            travelCanvas = canvasGO.AddComponent<Canvas>();
            travelCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGO.AddComponent<GraphicRaycaster>();
        }

        void ShowLoadingScreen(string message = "Загрузка...")
        {
            if (_loadingScreen != null) return;
            EnsureCanvas();
            GameUI.IsOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = false;
            _loadingScreen = new GameObject("LoadingScreen");
            _loadingScreen.transform.SetParent(travelCanvas.transform, false);
            Image bg = _loadingScreen.AddComponent<Image>();
            bg.color = Color.black;
            RectTransform rt = _loadingScreen.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            GameObject textGO = new GameObject("LoadingText");
            textGO.transform.SetParent(_loadingScreen.transform, false);
            _loadingText = textGO.AddComponent<Text>();
            _loadingText.text = message;
            _loadingText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _loadingText.fontSize = 28;
            _loadingText.color = Color.white;
            _loadingText.alignment = TextAnchor.MiddleCenter;
            RectTransform textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;
            StartCoroutine(AnimateLoadingDots());
        }

        void HideLoadingScreen()
        {
            if (_loadingScreen != null) { Destroy(_loadingScreen); _loadingScreen = null; _loadingText = null; }
            GameUI.IsOpen = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        IEnumerator AnimateLoadingDots()
        {
            string[] dots = { "Загрузка.", "Загрузка..", "Загрузка..." };
            int i = 0;
            while (_loadingScreen != null)
            {
                if (_loadingText != null) _loadingText.text = dots[i % 3];
                i++;
                yield return new WaitForSeconds(0.5f);
            }
        }

        public void StartWarp(int planetIndex)
        {
            if (_isWarping || planetIndex < 0 || planetIndex >= planets.Count) return;
            StartCoroutine(WarpCoroutine(planets[planetIndex]));
        }

        IEnumerator PreparePlanetForArrival(PlanetEntry target, float timeoutSeconds)
        {
            if (target == null || target.generator == null)
                yield break;

            if (target.lodController != null)
                target.lodController.ForceTerrainStreaming(Mathf.Max(10f, timeoutSeconds + 2f));

            target.generator.SetFastGeneration(true);
            target.generator.ActivateStreaming(chunkManager != null ? Mathf.Max(2, chunkManager.maxConcurrentChunkBuildsPerPlanet) : 2);

            float elapsed = 0f;
            while (!target.generator.IsReady && elapsed < timeoutSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            target.generator.SetFastGeneration(false);
        }

        IEnumerator WarpCoroutine(PlanetEntry target)
        {
            _isWarping = true;
            if (_showWarpHUD) CloseWarpHUD();
            if (player == null) { _isWarping = false; yield break; }
            if (!seamlessWarp)
                player.gameObject.SetActive(false);
            var rb = player.GetComponent<Rigidbody>();
            bool hadGravity = false;
            bool hadKinematic = false;
            if (rb != null)
            {
                hadGravity = rb.useGravity;
                hadKinematic = rb.isKinematic;
                rb.useGravity = false;
                rb.isKinematic = true;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            ShowLoadingScreen("Варп...");
            if (seamlessWarp)
                HideLoadingScreen();
            else
                yield return new WaitForSeconds(0.3f);
            var controller = player.GetComponent<PlanetaryFirstPersonController.PlanetaryFirstPersonController>();
            bool hadControllerEnabled = controller != null && controller.enabled;
            if (controller != null)
                controller.enabled = false;
            Vector3 up = Vector3.up;
            Vector3 planetCenter = target.root.transform.position;
            if (!seamlessWarp)
            {
                float waitHeight = target.generator.radius + target.generator.heightMultiplier + 200f;
                player.position = planetCenter + up * waitHeight;
                player.rotation = AlignPlayerToSurface(player.rotation, up);
            }
            yield return StartCoroutine(PreparePlanetForArrival(target, seamlessWarp ? seamlessWarpTimeout : 25f));
            float rayStart = target.generator.radius + target.generator.heightMultiplier + 60f;
            Vector3 rayOrigin = planetCenter + up * rayStart;
            Vector3 spawnPos = planetCenter + up * (target.generator.radius + target.generator.heightMultiplier + 5f);
            RaycastHit hit;
            float timeout = 10f, elapsed = 0f;
            Vector3 spawnUp = up;
            while (elapsed < timeout)
            {
                if (Physics.Raycast(rayOrigin, -up, out hit, rayStart * 2f))
                {
                    spawnUp = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : up;
                    spawnPos = hit.point + spawnUp * 1.5f;
                    break;
                }
                elapsed += 0.1f;
                yield return new WaitForSeconds(0.1f);
            }
            player.position = spawnPos;
            player.rotation = AlignPlayerToSurface(player.rotation, spawnUp);
            if (rb != null)
            {
                rb.position = spawnPos;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = hadKinematic;
                rb.useGravity = hadGravity;
            }
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
            if (!seamlessWarp)
            {
                HideLoadingScreen();
                player.gameObject.SetActive(true);
            }
            if (controller != null)
                controller.enabled = hadControllerEnabled;
            _currentPlanet = target;
            SetActivePlanet(target);
            ApplyPlanetVisualProfile(target);
            yield return new WaitForSeconds(0.2f);
            _isWarping = false;
        }

        Quaternion AlignPlayerToSurface(Quaternion currentRotation, Vector3 up)
        {
            up = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
            Vector3 forward = Vector3.ProjectOnPlane(currentRotation * Vector3.forward, up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(Vector3.forward, up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(Vector3.right, up);
            return Quaternion.LookRotation(forward.normalized, up);
        }

        string[] GeneratePlanetNames(int count)
        {
            string[] prefixes = { "Ara", "Vel", "Kro", "Tor", "Zyn", "Eph", "Mal", "Syx" };
            string[] suffixes = { "ius", "on", "ara", "eth", "yx", "an", "or", "eon" };
            var result = new string[count];
            System.Random rng = new System.Random(systemSeed);
            for (int i = 0; i < count; i++) result[i] = prefixes[rng.Next(prefixes.Length)] + suffixes[rng.Next(suffixes.Length)];
            return result;
        }

        Color GenerateCloudColor(BiomePreset biome, bool preferWhite)
        {
            float whiteBias = preferWhite ? 0.86f : (Random.value < 0.62f ? 0.84f : Random.Range(0.52f, 0.72f));
            Color themeColor = Color.Lerp(biome.atmo, biome.shallow, 0.35f);
            themeColor = Color.Lerp(themeColor, biome.sand, 0.12f);
            themeColor.a = 1f;
            return Color.Lerp(themeColor, Color.white, whiteBias);
        }

        Color GenerateCloudShadowColor(Color cloudColor, BiomePreset biome)
        {
            Color atmosphericShadow = Color.Lerp(biome.atmo, biome.deep, 0.42f);
            atmosphericShadow.a = 1f;
            return Color.Lerp(atmosphericShadow, cloudColor * 0.75f, 0.55f);
        }

        void OnDrawGizmosSelected()
        {
            for (int i = 0; i < planetCount; i++)
            {
                float r = minOrbitRadius + i * orbitSpacing;
                Gizmos.color = new Color(1, 1, 0, 0.25f);
                DrawGizmoCircle(Vector3.zero, r, 64);
            }
            foreach (var p in planets)
            {
                Gizmos.color = p.atmosphereColor;
                Gizmos.DrawWireSphere(p.root ? p.root.transform.position : Vector3.zero, p.generator ? p.generator.radius : 200f);
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

        void OnDestroy()
        {
            StopAllCoroutines();
            if (sun != null) Destroy(sun);
            foreach (var p in planets) if (p != null && p.root != null) Destroy(p.root);
            planets.Clear();
        }
    }

    public static class GameUI { public static bool IsOpen = false; }

    public struct BiomePreset
    {
        public string name;
        public Color deep, shallow, sand, grass, forest, rock, snow, atmo;
        public BiomePreset(string name, Color deep, Color shallow, Color sand, Color grass, Color forest, Color rock, Color snow, Color atmo)
        {
            this.name = name; this.deep = deep; this.shallow = shallow; this.sand = sand;
            this.grass = grass; this.forest = forest; this.rock = rock; this.snow = snow; this.atmo = atmo;
        }
    }
}
