using UnityEngine;

namespace PlanetaryRendering.Atmosphere
{
    /// <summary>
    /// Controls atmosphere material parameters (position, radius, height, intensity).
    /// Wrapped in a namespace to avoid polluting global type names.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class AtmosphereController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Center of the planet (Transform)")]
        public Transform planet;

        [Tooltip("Player or camera Transform")]
        public Transform player;

        [Tooltip("Material that uses the atmosphere shader")]
        public Material atmosphereMaterial;

        [Header("Setup")]
        public float planetRadius = 5000f;
        public float atmosphereHeight = 300f;
        [Range(0f, 2f)] public float maxIntensity = 1.0f;

        [Header("Fade")]
        [Range(0f, 1f)] public float fadeStart = 0.9f; 
        public float fadeSpeed = 6f;

        [SerializeField, HideInInspector]
        private float currentIntensity = 0f;

        void Start()
        {
            if (atmosphereMaterial == null)
            {
                var rend = GetComponent<MeshRenderer>();
                if (rend) atmosphereMaterial = rend.sharedMaterial;
            }

            UpdateMaterialImmediate();
        }

        void LateUpdate()
        {
            if (atmosphereMaterial == null)
            {
                var rend = GetComponent<MeshRenderer>();
                if (rend) atmosphereMaterial = rend.sharedMaterial;
            }

            if (player == null && Camera.main != null)
                player = Camera.main.transform;

            if (planet == null || atmosphereMaterial == null) return;

            atmosphereMaterial.SetVector("_PlanetPos", planet.position);
            atmosphereMaterial.SetFloat("_PlanetRadius", planetRadius);
            atmosphereMaterial.SetFloat("_AtmoHeight", atmosphereHeight);

            float target = maxIntensity;

            if (player != null)
            {
                float dist = Vector3.Distance(player.position, planet.position);
                float height = Mathf.Max(0f, dist - planetRadius);
                float t = Mathf.Clamp01(height / Mathf.Max(0.001f, atmosphereHeight));

                target = maxIntensity * (1f - t);

                if (t > fadeStart)
                {
                    float localT = Mathf.InverseLerp(fadeStart, 1f, t);
                    target = maxIntensity * (1f - localT);
                }
            }

            currentIntensity = Application.isPlaying
                ? Mathf.Lerp(currentIntensity, target, 1f - Mathf.Exp(-fadeSpeed * Time.deltaTime))
                : target;
            atmosphereMaterial.SetFloat("_Intensity", currentIntensity);
        }

        /// <summary>
        /// Immediate update of parameters in the material (useful in editor).
        /// </summary>
        public void UpdateMaterialImmediate()
        {
            if (atmosphereMaterial == null || planet == null) return;

            atmosphereMaterial.SetVector("_PlanetPos", planet.position);
            atmosphereMaterial.SetFloat("_PlanetRadius", planetRadius);
            atmosphereMaterial.SetFloat("_AtmoHeight", atmosphereHeight);
            atmosphereMaterial.SetFloat("_Intensity", currentIntensity);
        }

        void OnValidate()
        {
            if (this == null) return;
            UpdateMaterialImmediate();
        }
    }
}
