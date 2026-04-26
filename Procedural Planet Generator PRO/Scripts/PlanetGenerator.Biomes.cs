namespace PlanetGeneration
{
    using UnityEngine;

    /// <summary>
    /// Biome system: surface colors, forest masks, seed hue shift, and
    /// controlled rock darkening for a more stylized sci-fi look.
    /// </summary>
    public partial class PlanetGenerator
    {
        Color GetBiomeColor(Vector3 dir, float height)
        {
            Color floorDeep = generateOcean
                ? Color.Lerp(sandColor, rockColor, 0.22f)
                : new Color(0.66f, 0.46f, 0.24f);
            Color floorShallow = generateOcean
                ? Color.Lerp(sandColor, grassColor, 0.12f)
                : new Color(0.82f, 0.62f, 0.36f);
            float deepThreshold = Mathf.Lerp(oceanDepth, seaLevelHeight, 0.35f);

            if (height < deepThreshold) return floorDeep;
            if (height < seaLevelHeight)
                return Color.Lerp(floorDeep, floorShallow, Mathf.InverseLerp(oceanDepth, seaLevelHeight, height));

            float beachTop = seaLevelHeight + beachWidthHeight;
            if (height < beachTop)
                return Color.Lerp(floorShallow, sandColor, Mathf.InverseLerp(seaLevelHeight, beachTop, height));

            Vector3 worldDir = dir * radius;
            float styleScale = GetStylizedFrequencyScale();

            float macroRaw = FBM(worldDir * (0.00055f * styleScale) + seedOffset * 0.002f,
                4, 0.52f, 2.05f, seed + 55555);
            float macro01 = Mathf.SmoothStep(0.22f, 0.78f, macroRaw * 0.5f + 0.5f);

            float midRaw = FBM(worldDir * (0.0018f * styleScale) + seedOffset * 0.003f,
                3, 0.50f, 2.10f, seed + 66666);
            float mid01 = Mathf.SmoothStep(0.38f, 0.72f, midRaw * 0.5f + 0.5f);

            float darkWeight = macro01 * 0.58f + mid01 * 0.22f;
            float forestDensity = EvaluateForestDensity(dir, height);

            Color primaryColor = Color.Lerp(
                grassColor,
                forestColor,
                Mathf.Clamp01(Mathf.Max(darkWeight, forestDensity * 0.82f)));

            float landT = Mathf.InverseLerp(beachTop, normalizedMaxHeight, height);
            Color highlandGrass = Color.Lerp(
                Color.Lerp(primaryColor, grassColor, 0.55f),
                sandColor,
                0.14f);
            Color baseColor = Color.Lerp(primaryColor, highlandGrass, Mathf.SmoothStep(0.58f, 0.88f, landT));

            float exposedRock = Mathf.SmoothStep(0.62f, 0.96f, landT);
            Color highRockColor = Color.Lerp(
                rockColor,
                mesaRockColor,
                Mathf.SmoothStep(0.55f, 0.92f, landT) * 0.42f);
            baseColor = Color.Lerp(baseColor, highRockColor, exposedRock * 0.18f);

            float accentMask = Mathf.SmoothStep(0.70f, 0.92f, macro01) * Mathf.SmoothStep(0.24f, 0.84f, mid01);
            Color accentColor = Color.Lerp(forestColor, sandColor, 0.22f);
            baseColor = Color.Lerp(baseColor, accentColor, accentMask * 0.22f);

            if (enableSnow)
            {
                float polarSnow = Mathf.SmoothStep(0.58f, 0.80f, Mathf.Abs(dir.y));
                baseColor = Color.Lerp(baseColor, snowColor, polarSnow);
            }

            return baseColor;
        }

        float EvaluateForestDensity(Vector3 dir, float height)
        {
            float beachTop = seaLevelHeight + beachWidthHeight;
            if (height <= beachTop || height >= normalizedMaxHeight * 0.82f) return 0f;

            float styleScale = GetStylizedFrequencyScale();
            Vector3 worldDir = dir * radius;

            float cluster = FBM(worldDir * (forestNoiseScale * styleScale) + seedOffset * 0.004f,
                4, 0.54f, 2.08f, seed + 77777) * 0.5f + 0.5f;
            float moisture = FBM(worldDir * (forestNoiseScale * 0.58f * styleScale) + seedOffset * 0.002f,
                3, 0.5f, 2.0f, seed + 88888) * 0.5f + 0.5f;

            float forestMask = Mathf.SmoothStep(
                Mathf.Clamp01(forestCoverage - 0.18f),
                Mathf.Clamp01(forestCoverage + 0.18f),
                Mathf.Lerp(cluster, moisture, 0.38f));

            float lowlandMask = Mathf.SmoothStep(beachTop, beachTop + normalizedMaxHeight * 0.05f, height);
            float treeLineMask = 1f - Mathf.SmoothStep(normalizedMaxHeight * 0.50f, normalizedMaxHeight * 0.82f, height);
            return Mathf.Clamp01(forestMask * lowlandMask * treeLineMask * (0.90f + forestDensityBoost * 1.1f));
        }

        void ApplySeedHueShift()
        {
            if (!Application.isPlaying) return;
            var prng = new System.Random(seed ^ 0x5A5A5A5A);
            float hueShift = (float)(prng.NextDouble() * 0.10 - 0.05f);

            Color ShiftHue(Color c)
            {
                Color.RGBToHSV(c, out float h, out float s, out float v);
                h = (h + hueShift + 1f) % 1f;
                Color shifted = Color.HSVToRGB(h, s, v);
                shifted.a = c.a;
                return shifted;
            }

            grassColor = ShiftHue(grassColor);
            forestColor = ShiftHue(forestColor);
            sandColor = ShiftHue(sandColor);
            rockColor = ShiftHue(rockColor);
            mesaRockColor = ShiftHue(mesaRockColor);
        }

        void ApplyDarkRock()
        {
            float darkness = Mathf.Clamp01(rockDarkness);
            Color.RGBToHSV(rockColor, out float h, out float s, out float v);
            v = Mathf.Lerp(v, v * 0.78f, darkness);
            rockColor = Color.HSVToRGB(h, Mathf.Lerp(s, Mathf.Min(s + 0.06f, 1f), darkness * 0.22f), v);

            Color.RGBToHSV(mesaRockColor, out h, out s, out v);
            v = Mathf.Lerp(v, v * 0.84f, darkness * 0.55f);
            mesaRockColor = Color.HSVToRGB(h, s, v);
        }

        GameObject CreateSimpleTreePrefab()
        {
            var root = new GameObject("SimpleTreePrefab");
            root.hideFlags = HideFlags.HideAndDontSave;
            root.transform.localScale = Vector3.one;

            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Trunk";
            trunk.transform.SetParent(root.transform);
            trunk.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            trunk.transform.localScale = new Vector3(0.18f, 0.6f, 0.18f);
            if (trunk.GetComponent<Collider>()) Destroy(trunk.GetComponent<Collider>());
            var trunkMr = trunk.GetComponent<MeshRenderer>();
            if (trunkMr) trunkMr.sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.38f, 0.25f, 0.12f) };

            var foliage = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            foliage.name = "Foliage";
            foliage.transform.SetParent(root.transform);
            foliage.transform.localPosition = new Vector3(0f, 1.55f, 0f);
            foliage.transform.localScale = new Vector3(0.85f, 1.05f, 0.85f);
            if (foliage.GetComponent<Collider>()) Destroy(foliage.GetComponent<Collider>());
            var foliageMr = foliage.GetComponent<MeshRenderer>();
            if (foliageMr) foliageMr.sharedMaterial = new Material(Shader.Find("Standard")) { color = forestColor };

            return root;
        }
    }
}
