namespace PlanetGeneration
{
    using UnityEngine;
    using UnityEngine.Rendering;

    /// <summary>
    /// Cheap proxy renderer for always-visible planets.
    /// LOD0/LOD1 use displaced proxy meshes so planets keep visible landforms
    /// long before the expensive surface chunks finish streaming.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlanetRenderer : MonoBehaviour
    {
        public PlanetGenerator terrainGenerator;
        public Camera mainCamera;
        public Shader proxyShader;

        private GameObject lod0Object;
        private GameObject lod1Object;
        private GameObject billboardObject;
        private MeshRenderer lod0Renderer;
        private MeshRenderer lod1Renderer;
        private MeshRenderer billboardRenderer;
        private MeshFilter lod0Filter;
        private MeshFilter lod1Filter;
        private MeshFilter billboardFilter;
        private MaterialPropertyBlock lod0Props;
        private MaterialPropertyBlock lod1Props;
        private MaterialPropertyBlock billboardProps;
        private float lod0Opacity = 1f;
        private float lod1Opacity = 0f;
        private float lod0TargetOpacity = 1f;
        private float lod1TargetOpacity = 0f;
        private float fadeSpeed = 4.5f;
        private ProxyDetailLevel proxyDetailLevel = ProxyDetailLevel.High;
        private float visibilityThreshold = 260000f;
        private bool billboardModeActive;
        private bool initialized;

        private Mesh lod0ProxyMesh;
        private Mesh lod1ProxyMesh;
        private int lod0FaceResolution = -1;
        private int lod1FaceResolution = -1;

        private static Mesh billboardMesh;
        private static Material sharedFarMaterial;
        private static Material sharedMediumMaterial;
        private static Material sharedBillboardMaterial;

        public void Initialize(PlanetGenerator generator, Camera camera)
        {
            terrainGenerator = generator;
            mainCamera = camera != null ? camera : Camera.main;
            if (initialized && lod0Object != null && lod1Object != null && billboardObject != null)
            {
                SyncRuntimeProperties();
                return;
            }

            if (proxyShader == null)
                proxyShader = Shader.Find("Custom/PlanetProxyShader");

            EnsureSharedMaterials();
            EnsureProxyObjects();
            SyncRuntimeProperties();
            SetLodVisibility(true, false, 1f, 0f, 0f);
            initialized = true;
        }

        public void ConfigureRemoteLod(ProxyDetailLevel detailLevel, float billboardThreshold)
        {
            proxyDetailLevel = detailLevel;
            visibilityThreshold = Mathf.Max(0f, billboardThreshold);

            if (!initialized)
                return;

            RefreshProxyMeshes(forceRebuild: true);
            SyncRuntimeProperties();
        }

        public void SyncRuntimeProperties()
        {
            if (terrainGenerator == null || lod0Object == null || lod1Object == null)
                return;

            float proxyRadius = terrainGenerator.radius;
            float billboardDiameter = terrainGenerator.radius * 2f;

            lod0Object.transform.localPosition = Vector3.zero;
            lod1Object.transform.localPosition = Vector3.zero;
            lod0Object.transform.localRotation = Quaternion.identity;
            lod1Object.transform.localRotation = Quaternion.identity;
            lod0Object.transform.localScale = Vector3.one * (proxyRadius * 0.9995f);
            lod1Object.transform.localScale = Vector3.one * (proxyRadius * 0.9990f);

            if (billboardObject != null)
            {
                billboardObject.transform.localPosition = Vector3.zero;
                billboardObject.transform.localScale = Vector3.one * billboardDiameter;
            }

            RefreshProxyMeshes(forceRebuild: false);
            ApplyProxyState();
        }

        public void SetLodVisibility(bool showLod0, bool showLod1)
        {
            SetLodVisibility(showLod0, showLod1, showLod0 ? 1f : 0f, showLod1 ? 1f : 0f);
        }

        public void SetLodVisibility(bool showLod0, bool showLod1, float lod0OpacityTarget, float lod1OpacityTarget, float blendSpeed = -1f)
        {
            lod0TargetOpacity = showLod0 ? Mathf.Clamp01(lod0OpacityTarget) : 0f;
            lod1TargetOpacity = showLod1 ? Mathf.Clamp01(lod1OpacityTarget) : 0f;
            if (blendSpeed >= 0f)
                fadeSpeed = blendSpeed;

            if (!Application.isPlaying)
            {
                lod0Opacity = lod0TargetOpacity;
                lod1Opacity = lod1TargetOpacity;
            }

            ApplyProxyState();
        }

        public Bounds GetWorldBounds()
        {
            float radius = terrainGenerator != null ? terrainGenerator.radius * 2.4f : 1000f;
            return new Bounds(transform.position, Vector3.one * radius * 2f);
        }

        void Update()
        {
            if (!initialized)
                return;

            if (mainCamera == null)
                mainCamera = terrainGenerator != null && terrainGenerator.mainCamera != null ? terrainGenerator.mainCamera : Camera.main;

            float step = fadeSpeed <= 0f ? 1f : fadeSpeed * Time.deltaTime;
            float newLod0 = Mathf.MoveTowards(lod0Opacity, lod0TargetOpacity, step);
            float newLod1 = Mathf.MoveTowards(lod1Opacity, lod1TargetOpacity, step);

            if (!Mathf.Approximately(newLod0, lod0Opacity) || !Mathf.Approximately(newLod1, lod1Opacity))
            {
                lod0Opacity = newLod0;
                lod1Opacity = newLod1;
                ApplyProxyState();
            }

            bool shouldUseBillboard = ShouldUseBillboard();
            if (shouldUseBillboard != billboardModeActive)
                ApplyProxyState();

            UpdateBillboardOrientation();
        }

        void EnsureSharedMaterials()
        {
            if (proxyShader != null)
            {
                if (sharedFarMaterial == null)
                {
                    sharedFarMaterial = new Material(proxyShader);
                    sharedFarMaterial.name = "PlanetProxy_Far";
                }

                if (sharedMediumMaterial == null)
                {
                    sharedMediumMaterial = new Material(proxyShader);
                    sharedMediumMaterial.name = "PlanetProxy_Medium";
                }
            }

            if (sharedBillboardMaterial == null)
            {
                Shader billboardShader = Shader.Find("Sprites/Default");
                if (billboardShader == null)
                    billboardShader = Shader.Find("Unlit/Color");
                if (billboardShader == null)
                    billboardShader = Shader.Find("Standard");

                sharedBillboardMaterial = new Material(billboardShader);
                sharedBillboardMaterial.name = "PlanetProxy_Billboard";
            }
        }

        void EnsureProxyObjects()
        {
            if (lod0Object == null)
            {
                lod0Object = CreateProxyObject("LOD0Proxy", sharedFarMaterial, out lod0Renderer, out lod0Filter);
                lod0Props = new MaterialPropertyBlock();
            }

            if (lod1Object == null)
            {
                lod1Object = CreateProxyObject("LOD1Proxy", sharedMediumMaterial, out lod1Renderer, out lod1Filter);
                lod1Props = new MaterialPropertyBlock();
            }

            if (billboardObject == null)
            {
                billboardObject = CreateBillboardObject("BillboardProxy", sharedBillboardMaterial, out billboardRenderer, out billboardFilter);
                billboardProps = new MaterialPropertyBlock();
            }

            RefreshProxyMeshes(forceRebuild: true);
        }

        void RefreshProxyMeshes(bool forceRebuild)
        {
            if (lod0Filter != null)
            {
                int faceResolution = GetProxyFaceResolution(proxyDetailLevel, false);
                lod0Filter.sharedMesh = GetOrBuildProxyMesh(ref lod0ProxyMesh, ref lod0FaceResolution, "lod0", faceResolution, forceRebuild);
            }

            if (lod1Filter != null)
            {
                int faceResolution = GetProxyFaceResolution(proxyDetailLevel, true);
                lod1Filter.sharedMesh = GetOrBuildProxyMesh(ref lod1ProxyMesh, ref lod1FaceResolution, "lod1", faceResolution, forceRebuild);
            }

            if (billboardFilter != null)
                billboardFilter.sharedMesh = GetOrBuildBillboardMesh();
        }

        int GetProxyFaceResolution(ProxyDetailLevel detailLevel, bool mediumDetail)
        {
            switch (detailLevel)
            {
                case ProxyDetailLevel.Low:
                    return mediumDetail ? 20 : 12;
                case ProxyDetailLevel.Medium:
                    return mediumDetail ? 28 : 18;
                default:
                    return mediumDetail ? 40 : 24;
            }
        }

        Mesh GetOrBuildProxyMesh(ref Mesh targetMesh, ref int cachedResolution, string label, int faceResolution, bool forceRebuild)
        {
            if (!forceRebuild && targetMesh != null && cachedResolution == faceResolution)
                return targetMesh;

            DestroyGeneratedMesh(ref targetMesh);
            cachedResolution = faceResolution;

            targetMesh = BuildFallbackSphereMesh(label, faceResolution);
            return targetMesh;
        }

        static Mesh BuildFallbackSphereMesh(string label, int faceResolution)
        {
            int longitudeSegments = Mathf.Max(8, faceResolution * 2);
            int latitudeSegments = Mathf.Max(6, faceResolution + 2);
            return BuildSphereMesh($"proxy_fallback_{label}_{longitudeSegments}_{latitudeSegments}", longitudeSegments, latitudeSegments);
        }

        GameObject CreateProxyObject(string childName, Material sharedMaterial, out MeshRenderer meshRenderer, out MeshFilter meshFilter)
        {
            GameObject child = new GameObject(childName);
            child.transform.SetParent(transform, false);

            meshFilter = child.AddComponent<MeshFilter>();
            meshRenderer = child.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = sharedMaterial != null ? sharedMaterial : new Material(Shader.Find("Standard"));
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            meshRenderer.allowOcclusionWhenDynamic = false;

            return child;
        }

        GameObject CreateBillboardObject(string childName, Material sharedMaterial, out MeshRenderer meshRenderer, out MeshFilter meshFilter)
        {
            GameObject child = new GameObject(childName);
            child.transform.SetParent(transform, false);

            meshFilter = child.AddComponent<MeshFilter>();
            meshRenderer = child.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = sharedMaterial != null ? sharedMaterial : new Material(Shader.Find("Unlit/Color"));
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            meshRenderer.allowOcclusionWhenDynamic = false;

            return child;
        }

        void ApplyProxyState()
        {
            bool useBillboard = ShouldUseBillboard();
            billboardModeActive = useBillboard;
            float activeOpacity = Mathf.Max(Mathf.Max(lod0Opacity, lod1Opacity), Mathf.Max(lod0TargetOpacity, lod1TargetOpacity));

            if (lod0Object != null)
                lod0Object.SetActive(!useBillboard && (lod0Opacity > 0.001f || lod0TargetOpacity > 0.001f));
            if (lod1Object != null)
                lod1Object.SetActive(!useBillboard && (lod1Opacity > 0.001f || lod1TargetOpacity > 0.001f));
            if (billboardObject != null)
                billboardObject.SetActive(useBillboard && activeOpacity > 0.001f);

            UpdateProxyBlock(lod0Renderer, lod0Props, false, lod0Opacity);
            UpdateProxyBlock(lod1Renderer, lod1Props, true, lod1Opacity);
            UpdateBillboardBlock(activeOpacity);
        }

        bool ShouldUseBillboard()
        {
            if (visibilityThreshold <= 0f || terrainGenerator == null || mainCamera == null)
                return false;

            float surfaceDistance = Mathf.Max(0f, Vector3.Distance(mainCamera.transform.position, transform.position) - terrainGenerator.radius);
            return surfaceDistance >= visibilityThreshold;
        }

        void UpdateBillboardOrientation()
        {
            if (billboardObject == null || !billboardObject.activeSelf || mainCamera == null)
                return;

            Vector3 toCamera = mainCamera.transform.position - billboardObject.transform.position;
            if (toCamera.sqrMagnitude <= 1e-6f)
                return;

            billboardObject.transform.rotation = Quaternion.LookRotation(toCamera.normalized, mainCamera.transform.up);
        }

        void UpdateProxyBlock(Renderer targetRenderer, MaterialPropertyBlock props, bool mediumDetail, float opacity)
        {
            if (targetRenderer == null || props == null || terrainGenerator == null)
                return;

            props.Clear();
            props.SetVector("_PlanetCenter", transform.position);
            props.SetFloat("_PlanetRadius", terrainGenerator.radius);
            props.SetColor("_SeaColor", terrainGenerator.shallowWaterColor);
            props.SetColor("_LowlandColor", terrainGenerator.grassColor);
            props.SetColor("_HighlandColor", terrainGenerator.rockColor);
            props.SetColor("_SnowColor", terrainGenerator.enableSnow ? terrainGenerator.snowColor : terrainGenerator.rockColor);
            props.SetColor("_AtmosphereColor", terrainGenerator.atmosphereColor * 0.9f + terrainGenerator.cloudColor * 0.12f);
            props.SetFloat("_NoiseScale", mediumDetail ? 7.5f : 2.75f);
            props.SetFloat("_NoiseStrength", mediumDetail ? 0.32f : 0.14f);
            props.SetFloat("_NoiseContrast", mediumDetail ? 1.55f : 0.95f);
            props.SetFloat("_AtmosphereStrength", mediumDetail ? 0.18f : 0.12f);
            props.SetFloat("_Opacity", Mathf.Clamp01(opacity));
            targetRenderer.SetPropertyBlock(props);
        }

        void UpdateBillboardBlock(float opacity)
        {
            if (billboardRenderer == null || billboardProps == null || terrainGenerator == null)
                return;

            Color billboardColor = Color.Lerp(terrainGenerator.atmosphereColor, terrainGenerator.grassColor, 0.28f);
            billboardColor = Color.Lerp(billboardColor, terrainGenerator.shallowWaterColor, terrainGenerator.generateOcean ? 0.22f : 0f);
            billboardColor.a = Mathf.Clamp01(opacity);

            billboardProps.Clear();
            billboardProps.SetColor("_Color", billboardColor);
            billboardRenderer.SetPropertyBlock(billboardProps);
        }

        static Mesh GetOrBuildBillboardMesh()
        {
            if (billboardMesh != null)
                return billboardMesh;

            billboardMesh = new Mesh { name = "planet_billboard" };
            billboardMesh.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f)
            });
            billboardMesh.SetNormals(new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward });
            billboardMesh.SetUVs(0, new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            });
            billboardMesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            billboardMesh.RecalculateBounds();
            return billboardMesh;
        }

        static Mesh BuildSphereMesh(string meshName, int longitudeSegments, int latitudeSegments)
        {
            var mesh = new Mesh { name = meshName };
            var vertices = new System.Collections.Generic.List<Vector3>((longitudeSegments + 1) * (latitudeSegments + 1));
            var normals = new System.Collections.Generic.List<Vector3>((longitudeSegments + 1) * (latitudeSegments + 1));
            var uvs = new System.Collections.Generic.List<Vector2>((longitudeSegments + 1) * (latitudeSegments + 1));
            var indices = new System.Collections.Generic.List<int>(longitudeSegments * latitudeSegments * 6);

            for (int lat = 0; lat <= latitudeSegments; lat++)
            {
                float v = latitudeSegments == 0 ? 0f : (float)lat / latitudeSegments;
                float phi = Mathf.Lerp(0f, Mathf.PI, v);
                float y = Mathf.Cos(phi);
                float ring = Mathf.Sin(phi);

                for (int lon = 0; lon <= longitudeSegments; lon++)
                {
                    float u = longitudeSegments == 0 ? 0f : (float)lon / longitudeSegments;
                    float theta = u * Mathf.PI * 2f;
                    Vector3 vertex = new Vector3(Mathf.Cos(theta) * ring, y, Mathf.Sin(theta) * ring);
                    vertices.Add(vertex);
                    normals.Add(vertex.normalized);
                    uvs.Add(new Vector2(u, v));
                }
            }

            int stride = longitudeSegments + 1;
            for (int lat = 0; lat < latitudeSegments; lat++)
            {
                for (int lon = 0; lon < longitudeSegments; lon++)
                {
                    int i0 = lat * stride + lon;
                    int i1 = i0 + 1;
                    int i2 = i0 + stride;
                    int i3 = i2 + 1;

                    indices.Add(i0); indices.Add(i2); indices.Add(i1);
                    indices.Add(i1); indices.Add(i2); indices.Add(i3);
                }
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        void DestroyGeneratedMesh(ref Mesh mesh)
        {
            if (mesh == null)
                return;

            if (Application.isPlaying)
                Destroy(mesh);
            else
                DestroyImmediate(mesh);

            mesh = null;
        }

        void OnDestroy()
        {
            DestroyGeneratedMesh(ref lod0ProxyMesh);
            DestroyGeneratedMesh(ref lod1ProxyMesh);
        }
    }
}
