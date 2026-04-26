namespace PlanetGeneration
{
    using UnityEngine;

    public enum DistantRenderMode
    {
        FullTerrain,
        SimpleSphere
    }

    public enum ProxyDetailLevel
    {
        Low,
        Medium,
        High
    }

    [DisallowMultipleComponent]
    public class SolarPlanet : MonoBehaviour
    {
        [Header("References")]
        public PlanetGenerator terrainGenerator;
        public PlanetRenderer planetRenderer;
        public PlanetLODController lodController;

        [Header("Surface LOD (Local Context)")]
        public bool activeLodSystem = true;
        [Min(0f)]
        public float lod0Radius = 2.4f;
        public AnimationCurve lodTransitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Celestial LOD (Remote Context)")]
        public DistantRenderMode distantRenderMode = DistantRenderMode.FullTerrain;
        public ProxyDetailLevel proxyDetailLevel = ProxyDetailLevel.High;
        [Min(0f)]
        public float visibilityThreshold = 260000f;

        [SerializeField, HideInInspector]
        private bool isActivePlanet;

        public bool IsActivePlanet => isActivePlanet;

        void Awake()
        {
            ResolveReferences();
            ApplySettings();
        }

        void Start()
        {
            ResolveReferences();
            ApplySettings();
        }

        void OnValidate()
        {
            ResolveReferences();
            ApplySettings();
        }

        public void SetActivePlanet(bool active)
        {
            isActivePlanet = active;
            ApplySettings();
        }

        public void ResolveReferences()
        {
            if (terrainGenerator == null)
                terrainGenerator = GetComponent<PlanetGenerator>();
            if (planetRenderer == null)
                planetRenderer = GetComponent<PlanetRenderer>();
            if (lodController == null)
                lodController = GetComponent<PlanetLODController>();
        }

        public void ApplySettings()
        {
            if (terrainGenerator != null)
                terrainGenerator.ConfigureSurfaceLod(activeLodSystem, lod0Radius, lodTransitionCurve);

            if (planetRenderer != null)
                planetRenderer.ConfigureRemoteLod(proxyDetailLevel, visibilityThreshold);

            if (lodController != null)
                lodController.solarPlanet = this;
        }
    }
}
