namespace PlanetGeneration
{
    using UnityEngine;

    /// <summary>
    /// Биомная система: GetBiomeColor, EvaluateForestDensity,
    /// ApplySeedHueShift (случайный сдвиг оттенка по seed),
    /// ApplyDarkRock (контроль яркости камня по rockDarkness).
    /// </summary>
    public partial class PlanetGenerator
    {
        Color GetBiomeColor(Vector3 dir, float height)
        {
            // ── Вода / дно ────────────────────────────────────────────────────
            Color floorDeep    = generateOcean ? Color.Lerp(sandColor, rockColor, 0.35f) : new Color(0.52f, 0.42f, 0.26f);
            Color floorShallow = generateOcean ? sandColor : new Color(0.68f, 0.58f, 0.38f);
            float deepThreshold = Mathf.Lerp(oceanDepth, seaLevelHeight, 0.35f);

            if (height < deepThreshold) return floorDeep;
            if (height < seaLevelHeight)
                return Color.Lerp(floorDeep, floorShallow, Mathf.InverseLerp(oceanDepth, seaLevelHeight, height));

            // ── Пляж ──────────────────────────────────────────────────────────
            float beachTop = seaLevelHeight + beachWidthHeight;
            if (height < beachTop)
                return Color.Lerp(floorShallow, sandColor, Mathf.InverseLerp(seaLevelHeight, beachTop, height));

            // ── Шумовые NMS-биомные пятна ─────────────────────────────────────
            Vector3 worldDir   = dir * radius;
            float styleScale   = GetStylizedFrequencyScale();

            float macroRaw = FBM(worldDir * (0.00055f * styleScale) + seedOffset * 0.002f,
                                 4, 0.52f, 2.05f, seed + 55555);
            float macro01  = Mathf.SmoothStep(0.22f, 0.78f, macroRaw * 0.5f + 0.5f);

            float midRaw = FBM(worldDir * (0.0018f * styleScale) + seedOffset * 0.003f,
                               3, 0.50f, 2.10f, seed + 66666);
            float mid01  = Mathf.SmoothStep(0.38f, 0.72f, midRaw * 0.5f + 0.5f);

            float darkWeight   = macro01 * 0.70f + mid01 * 0.30f;
            float forestDensity = EvaluateForestDensity(dir, height);

            Color primaryColor = Color.Lerp(grassColor, forestColor,
                Mathf.Clamp01(Mathf.Max(darkWeight, forestDensity * 0.95f)));

            // ── Высотные переходы ─────────────────────────────────────────────
            float landT        = Mathf.InverseLerp(beachTop, normalizedMaxHeight, height);
            Color highlandGrass = Color.Lerp(Color.Lerp(primaryColor, grassColor, 0.42f), sandColor, 0.08f);
            Color baseColor    = Color.Lerp(primaryColor, highlandGrass, Mathf.SmoothStep(0.58f, 0.88f, landT));
            float exposedRock  = Mathf.SmoothStep(0.62f, 0.96f, landT);
            Color highRockColor = Color.Lerp(rockColor, mesaRockColor, Mathf.SmoothStep(0.55f, 0.92f, landT) * 0.30f);
            baseColor = Color.Lerp(baseColor, highRockColor, exposedRock * 0.28f);

            // ── Полярные шапки ────────────────────────────────────────────────
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

            float lowlandMask  = Mathf.SmoothStep(beachTop, beachTop + normalizedMaxHeight * 0.05f, height);
            float treeLineMask = 1f - Mathf.SmoothStep(normalizedMaxHeight * 0.50f, normalizedMaxHeight * 0.82f, height);
            return Mathf.Clamp01(forestMask * lowlandMask * treeLineMask * (0.90f + forestDensityBoost * 1.1f));
        }

        // ── Seed-based hue shift ──────────────────────────────────────────────

        void ApplySeedHueShift()
        {
            if (!Application.isPlaying) return;
            var prng = new System.Random(seed ^ 0x5A5A5A5A);
            float hueShift = (float)(prng.NextDouble() * 0.22 - 0.11);   // ±11 degrees

            Color ShiftHue(Color c)
            {
                Color.RGBToHSV(c, out float h, out float s, out float v);
                h = (h + hueShift + 1f) % 1f;
                return Color.HSVToRGB(h, s, v) * c.a + new Color(0, 0, 0, c.a - 1f);
            }

            grassColor      = ShiftHue(grassColor);
            forestColor     = ShiftHue(forestColor);
            sandColor       = ShiftHue(sandColor);
            rockColor       = ShiftHue(rockColor);
            mesaRockColor   = ShiftHue(mesaRockColor);
        }

        void ApplyDarkRock()
        {
            float darkness = Mathf.Clamp01(rockDarkness);
            Color.RGBToHSV(rockColor, out float h, out float s, out float v);
            v = Mathf.Lerp(v, v * 0.35f, darkness);
            rockColor = Color.HSVToRGB(h, Mathf.Lerp(s, Mathf.Min(s + 0.08f, 1f), darkness * 0.4f), v);
            Color.RGBToHSV(mesaRockColor, out h, out s, out v);
            v = Mathf.Lerp(v, v * 0.45f, darkness * 0.65f);
            mesaRockColor = Color.HSVToRGB(h, s, v);
        }

        // ── Tree prefab fallback ──────────────────────────────────────────────

        GameObject CreateSimpleTreePrefab()
        {
            var root = new GameObject("SimpleTreePrefab");
            root.hideFlags = HideFlags.HideAndDontSave;
            root.transform.localScale = Vector3.one;

            // Trunk
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Trunk";
            trunk.transform.SetParent(root.transform);
            trunk.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            trunk.transform.localScale    = new Vector3(0.18f, 0.6f, 0.18f);
            if (trunk.GetComponent<Collider>()) Destroy(trunk.GetComponent<Collider>());
            var trunkMr = trunk.GetComponent<MeshRenderer>();
            if (trunkMr) trunkMr.sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.38f, 0.25f, 0.12f) };

            // Foliage
            var foliage = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            foliage.name = "Foliage";
            foliage.transform.SetParent(root.transform);
            foliage.transform.localPosition = new Vector3(0f, 1.55f, 0f);
            foliage.transform.localScale    = new Vector3(0.85f, 1.05f, 0.85f);
            if (foliage.GetComponent<Collider>()) Destroy(foliage.GetComponent<Collider>());
            var foliageMr = foliage.GetComponent<MeshRenderer>();
            if (foliageMr) foliageMr.sharedMaterial = new Material(Shader.Find("Standard")) { color = forestColor };

            return root;
        }
    }
}
