namespace PlanetGeneration
{
    using UnityEngine;

    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class PlanetCloudLayer : MonoBehaviour
    {
        public Transform planet;
        public Material cloudMaterial;
        public Vector3 rotationAxis = new Vector3(0.2f, 1f, 0.05f);
        public float rotationSpeed = 0.55f;
        public Vector3 windDirectionA = new Vector3(0.88f, 0.18f, 0.32f);
        public Vector3 windDirectionB = new Vector3(-0.36f, 0.08f, 0.92f);

        Vector3 SafeDirection(Vector3 dir, Vector3 fallback)
        {
            return dir.sqrMagnitude > 1e-5f ? dir.normalized : fallback;
        }

        void LateUpdate()
        {
            if (rotationAxis.sqrMagnitude < 1e-5f)
                rotationAxis = new Vector3(0.2f, 1f, 0.05f);

            if (cloudMaterial != null)
            {
                if (planet != null && cloudMaterial.HasProperty("_PlanetCenter"))
                    cloudMaterial.SetVector("_PlanetCenter", planet.position);

                if (cloudMaterial.HasProperty("_WindDirectionA"))
                {
                    Vector3 windA = SafeDirection(windDirectionA, new Vector3(0.88f, 0.18f, 0.32f));
                    cloudMaterial.SetVector("_WindDirectionA", new Vector4(windA.x, windA.y, windA.z, 0f));
                }

                if (cloudMaterial.HasProperty("_WindDirectionB"))
                {
                    Vector3 windB = SafeDirection(windDirectionB, new Vector3(-0.36f, 0.08f, 0.92f));
                    cloudMaterial.SetVector("_WindDirectionB", new Vector4(windB.x, windB.y, windB.z, 0f));
                }
            }

            if (!Application.isPlaying)
                return;

            transform.Rotate(rotationAxis.normalized, rotationSpeed * Time.deltaTime, Space.Self);
        }
    }
}
