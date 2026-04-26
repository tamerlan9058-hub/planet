namespace PlanetGeneration
{
    using UnityEngine;
    using System.Collections.Generic;

    /// <summary>
    /// Генерация рельефа: GenerateHeight (NMS + SebInspired), DomainWarp,
    /// SmoothHeightmap, LimitSlope, ApplyErosion, ClampSpikeHeights, RelaxSebHeightmap,
    /// EvaluateMacroHeightCached, SebPlateField.
    /// </summary>
    public partial class PlanetGenerator
    {
        // ── Cache helpers ─────────────────────────────────────────────────────

        Vector3Int QuantizeDir(Vector3 dir)
        {
            Vector3 n = dir.normalized;
            return new Vector3Int(
                Mathf.RoundToInt(n.x * HeightCacheQuant),
                Mathf.RoundToInt(n.y * HeightCacheQuant),
                Mathf.RoundToInt(n.z * HeightCacheQuant));
        }

        float EvaluateMacroHeightCached(Vector3 point)
        {
            var key = QuantizeDir(point);
            lock (heightCacheLock)
            {
                if (heightCache.TryGetValue(key, out float cached)) return cached;
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

        float EvaluateHeight(Vector3 point)           => EvaluateMacroHeightCached(point);
        float EvaluateHeightWithDetail(Vector3 point, float _) => EvaluateMacroHeightCached(point);

        // ── Main height dispatcher ────────────────────────────────────────────

        float GenerateHeight(Vector3 point)
        {
            float hMax = Mathf.Max(10f, normalizedMaxHeight);
            if (terrainStyle == TerrainStyle.SebInspired)
                return GenerateSebInspiredHeight(point.normalized, hMax);
            return GenerateNmsHeight(point, hMax);
        }

        // ── NMS Hybrid height ─────────────────────────────────────────────────

        float GenerateNmsHeight(Vector3 point, float hMax)
        {
            Vector3 warpedPoint = ApplyDomainWarp(point);
            Vector3 worldP = warpedPoint * radius + seedOffset * (radius * 0.0025f);
            float styleScale = GetStylizedFrequencyScale();

            float continentNoise = FBM(worldP * (0.00042f * styleScale), 4, 0.50f, 1.92f, seed + 11);
            float continent01 = Mathf.SmoothStep(0.18f, 0.84f, continentNoise * 0.5f + 0.5f);
            float landMask = Mathf.SmoothStep(
                Mathf.Clamp01(continentThreshold - 0.18f),
                Mathf.Clamp01(continentThreshold + 0.14f),
                continent01);

            var baseRes = FBM_Derivative(
                worldP * (0.00068f * styleScale),
                Mathf.Clamp(geometryOctaves, 2, 6),
                Mathf.Clamp(geometryAmplitudeDecay, 0.2f, 0.7f),
                Mathf.Clamp(geometryLacunarity, 1.7f, 2.2f),
                seed + 101,
                expWeight: octaveExponent * 0.82f,
                erosion: slopeErosion * 0.55f);

            float baseNoise01 = Mathf.SmoothStep(0.12f, 0.88f, baseRes.value * 0.5f + 0.5f);
            float gradientDamp = 1f / (1f + baseRes.deriv * Mathf.Lerp(0.04f, 0.15f, highFrequencyDamping));
            float continentLift = Mathf.Lerp(-hMax * 0.34f, hMax * 0.18f, landMask);
            float shelfLift = Mathf.Lerp(-hMax * 0.18f, -hMax * 0.02f, landMask);
            float baseHeight = (baseNoise01 - 0.50f) * hMax * 0.54f * gradientDamp
                             * Mathf.Lerp(0.72f, 1f, landMask);
            float plainsMask = 1f - Mathf.SmoothStep(0.38f, 0.76f, baseNoise01);

            float hillsNoise = Mathf.SmoothStep(0.20f, 0.85f, FBM(worldP * (0.00145f * styleScale), 3, 0.42f, 1.95f, seed + 401) * 0.5f + 0.5f);
            float hillsHeight = (hillsNoise - 0.5f) * hMax * 0.28f
                              * Mathf.Lerp(0.22f, 1f, landMask)
                              * Mathf.Lerp(0.24f, 1f, 1f - plainsMask * 0.75f);

            float mesaMaskNoise = MesaNoise(worldP * (0.00072f * styleScale), seed + 501);
            float mesaMask = Mathf.SmoothStep(Mathf.Clamp01(mesaFrequency - 0.18f), Mathf.Clamp01(mesaFrequency + 0.08f), mesaMaskNoise);
            mesaMask *= Mathf.SmoothStep(0.30f, 0.75f, landMask) * Mathf.Clamp01(mesaStrength);
            float mesaProfile = Mathf.SmoothStep(0.18f, 0.86f, FBM(worldP * (0.0012f * styleScale), 2, 0.42f, 1.9f, seed + 521) * 0.5f + 0.5f);
            float mesaHeight = (ApplySoftTerrace(mesaProfile, 3.2f, 0.16f) - 0.5f) * hMax * 0.18f;

            float mountainMaskNoise = FBM(worldP * (0.00056f * styleScale), 3, 0.45f, 1.90f, seed + 307) * 0.5f + 0.5f;
            float mountainMask = Mathf.SmoothStep(0.52f, 0.80f, mountainMaskNoise)
                               * Mathf.SmoothStep(0.24f, 0.82f, landMask);

            float ridgeNoise = Mathf.SmoothStep(0.10f, 0.82f, Mathf.Clamp01(RidgeFBM(
                worldP * (Mathf.Max(0.25f, ridgeScale) * 0.00115f * styleScale),
                Mathf.Clamp(ridgeOctaves, 2, 5),
                Mathf.Min(Mathf.Clamp01(ridgePersistence), 0.45f),
                1.92f, seed + 203,
                gain: Mathf.Max(0.35f, ridgeGain * 0.9f),
                sharpness: Mathf.Clamp(ridgeSharpness, 0.18f, 1.05f))));
            float mountainStrengthMul = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(0.2f, ridgeStrength) * Mathf.Max(0.2f, noiseSettings.mountainStrength)), 0.45f, 1.45f);
            float mountainHeight = ridgeNoise * hMax * 0.34f * mountainStrengthMul * Mathf.Lerp(0.78f, 1f, gradientDamp);

            float megaMaskNoise = FBM(worldP * (Mathf.Max(0.08f, megaScale) * 0.00085f * styleScale), 3, 0.5f, 2f, seed + 811) * 0.5f + 0.5f;
            float megaMask = Mathf.SmoothStep(Mathf.Clamp01(megaRarity - 0.18f), Mathf.Clamp01(megaRarity), megaMaskNoise) * mountainMask;
            float megaHeight = megaMask * megaStrength * hMax * 0.055f * gradientDamp;

            float cliffNoise = RidgeFBM(worldP * (Mathf.Max(0.25f, noiseSettings.cliffScale) * 0.00185f * styleScale),
                3, 0.42f, 2.08f, seed + 1603,
                gain: Mathf.Max(0.55f, ridgeGain),
                sharpness: Mathf.Clamp(noiseSettings.peakSharpness * 1.15f, 0.5f, 2.2f));
            float cliffMask = Mathf.SmoothStep(Mathf.Clamp01(noiseSettings.cliffThreshold - 0.18f), Mathf.Clamp01(noiseSettings.cliffThreshold + 0.08f), cliffNoise)
                            * mountainMask * Mathf.SmoothStep(0.14f, 0.52f, landMask);
            float cliffHeight = Mathf.Pow(Mathf.SmoothStep(0.22f, 0.88f, Mathf.Clamp01(cliffNoise)), 1f + Mathf.Max(0f, noiseSettings.peakSharpness) * 0.65f)
                              * hMax * 0.075f * Mathf.Max(0f, noiseSettings.cliffStrength) * gradientDamp;

            float finalHeight = continentLift + shelfLift + baseHeight + hillsHeight;
            finalHeight = Mathf.Lerp(finalHeight, finalHeight + mesaHeight, mesaMask * 0.60f);
            finalHeight += mountainHeight * mountainMask + megaHeight + cliffHeight * cliffMask;

            if (enableCanyons && canyonStrength > 0.001f)
            {
                float canyon = CanyonNoise(worldP * (0.00082f * styleScale), seed + 701);
                finalHeight -= canyon * canyonStrength * hMax * 0.18f
                             * Mathf.SmoothStep(0.24f, 0.78f, landMask) * (1f - mountainMask * 0.65f);
            }

            if (generateOcean && landMask > 0.3f)
                finalHeight = Mathf.Max(finalHeight, seaLevelHeight + beachWidthHeight * 0.4f);

            finalHeight *= Mathf.Lerp(0.94f, 1f, landMask);
            float absH01 = Mathf.Clamp01(Mathf.Abs(finalHeight) / hMax);
            finalHeight *= 1f - Mathf.SmoothStep(0.66f, 1f, absH01) * 0.22f;

            if (enableRivers && riverStrength > 0.001f)
            {
                float rv1 = FBM(worldP * riverScale + new Vector3(7.7f, 31.3f, 13.9f), 3, 0.5f, 1.95f, seed + 13131);
                float rv2 = FBM(worldP * riverScale * 2.3f + new Vector3(53.1f, 17.4f, 42.6f), 2, 0.45f, 1.92f, seed + 14242);
                float riverChannel = 1f - Mathf.SmoothStep(0f, riverNarrow, Mathf.Abs(rv1) * (1f - riverBranchMix) + Mathf.Abs(rv2) * riverBranchMix);
                float elevMask = Mathf.Clamp01(1f - Mathf.SmoothStep(seaLevelHeight, seaLevelHeight + riverMaxElevation, finalHeight));
                finalHeight -= riverChannel * riverStrength * hMax * 0.12f * elevMask * Mathf.SmoothStep(0.1f, 0.55f, landMask);
                if (generateOcean) finalHeight = Mathf.Max(finalHeight, seaLevelHeight + 0.5f);
            }

            return Mathf.Clamp(finalHeight, -hMax, hMax);
        }

        // ── Seb-Inspired height ───────────────────────────────────────────────

        float GenerateSebInspiredHeight(Vector3 spherePoint, float hMax)
        {
            var plateSample = SampleSebPlateField(spherePoint);
            float landThreshold = 1f - Mathf.Clamp01(sebLandRatio);
            float coastNoise = SebSimpleNoise(spherePoint, 4, 2.1f, 0.5f,
                Mathf.Max(0.001f, sebCoastNoiseScale) * 0.008f,
                Mathf.Max(0f, sebCoastNoiseStrength),
                0f, seed + 77);
            float landRaw = plateSample.blendedValue + coastNoise;
            bool isLand = landRaw > landThreshold;

            float continentHeight = SebSimpleNoise(spherePoint, 5, 2.05f, 0.50f,
                Mathf.Max(0.001f, sebPlateMacroScale) * 0.0018f,
                hMax * 0.32f, -hMax * 0.18f, seed + 11);
            float mountainMask = Mathf.SmoothStep(0.45f, 0.82f, plateSample.boundary + plateSample.blendedValue * 0.35f);
            float mountainHeight = SebSmoothedRidgeNoise(spherePoint,
                5, 2.2f, 0.5f,
                Mathf.Max(0.001f, sebPlateMacroScale) * 0.018f,
                1.8f, hMax * Mathf.Max(0f, sebBoundaryMountainStrength) * 0.48f,
                0.95f, 0f, 0.22f, seed + 201) * mountainMask;
            float hillyDetail = SebSimpleNoise(spherePoint, 4, 2.1f, 0.5f,
                Mathf.Max(0.001f, sebPlateMacroScale) * 0.0055f,
                hMax * 0.22f, 0f, seed + 301);
            float microDetail = SebSimpleNoise(spherePoint, 3, 2.2f, 0.5f, 0.018f, hMax * 0.06f, 0f, seed + 401);

            float landHeight;
            if (isLand)
            {
                float landBlend = Mathf.SmoothStep(landThreshold, landThreshold + 0.12f, landRaw);
                landHeight = Mathf.Lerp(-hMax * 0.06f, continentHeight, landBlend);
                landHeight += mountainHeight * landBlend;
                landHeight += hillyDetail * Mathf.Lerp(0.28f, 1f, landBlend);
                landHeight += microDetail * Mathf.Lerp(0.18f, 0.85f, landBlend);
            }
            else
            {
                float oceanDepthBlend = 1f - Mathf.SmoothStep(landThreshold - 0.22f, landThreshold, landRaw);
                landHeight = Mathf.Lerp(-hMax * 0.06f, oceanDepth * 1.2f, oceanDepthBlend);
                landHeight += microDetail * 0.12f;
            }

            float peakCompress = 1f - Mathf.SmoothStep(hMax * 0.72f, hMax, Mathf.Abs(landHeight)) * 0.18f;
            landHeight *= peakCompress;
            return Mathf.Clamp(landHeight, -hMax, hMax);
        }

        // ── Domain warp ───────────────────────────────────────────────────────

        float GetStylizedFrequencyScale() =>
            Mathf.Lerp(1.22f, 0.92f, Mathf.InverseLerp(1800f, 10000f, radius));

        Vector3 ApplyDomainWarp(Vector3 normalizedPoint)
        {
            if (warpStrength <= 0.001f && warp2Strength <= 0.001f) return normalizedPoint;
            float styleScale = GetStylizedFrequencyScale();
            Vector3 worldP = normalizedPoint * radius;
            float freq1 = Mathf.Max(0.0001f, warpFrequency) * styleScale;
            float freq2 = Mathf.Max(0.0001f, warp2Frequency) * styleScale;
            float amp   = Mathf.Max(0.0001f, warpAmplitudeRatio) * radius;

            float wx = FBM(worldP * freq1 + new Vector3(17.3f, 11.1f, 3.7f), 2, 0.5f, 2f, seed + 9901);
            float wy = FBM(worldP * freq1 + new Vector3(5.2f,  2.3f,  14.1f),2, 0.5f, 2f, seed + 9902);
            float wz = FBM(worldP * freq1 + new Vector3(3.9f,  19.5f, 8.8f), 2, 0.5f, 2f, seed + 9903);
            Vector3 warpOffset1 = new Vector3(wx, wy, wz) * amp * warpStrength;

            Vector3 warpedP = (normalizedPoint + warpOffset1 / radius).normalized;
            Vector3 worldP2 = warpedP * radius;
            float wx2 = FBM(worldP2 * freq2 + new Vector3(29.1f, 7.4f,  22.8f), 2, 0.5f, 2f, seed + 9904);
            float wy2 = FBM(worldP2 * freq2 + new Vector3(11.8f, 25.3f, 4.6f),  2, 0.5f, 2f, seed + 9905);
            float wz2 = FBM(worldP2 * freq2 + new Vector3(8.4f,  3.9f,  17.2f), 2, 0.5f, 2f, seed + 9906);
            Vector3 warpOffset2 = new Vector3(wx2, wy2, wz2) * amp * warp2Strength;

            return (warpedP + warpOffset2 / radius).normalized;
        }

        // ── Seb Plate Field ───────────────────────────────────────────────────

        private struct SebPlateSample
        {
            public float primaryValue, secondaryValue, blendedValue, boundary;
            public float primaryDistance, secondaryDistance;
        }

        void EnsureSebPlateData()
        {
            lock (sebPlateDataLock)
            {
                if (sebPlateDataReady) return;
                int count = Mathf.Max(2, sebPlateCount);
                sebPlateCenters = new Vector3[count];
                sebPlateMacroValues = new float[count];
                var prng = new System.Random(seed + 5555);
                for (int i = 0; i < count; i++)
                {
                    float u = (float)(prng.NextDouble() * 2.0 - 1.0);
                    float phi = (float)(prng.NextDouble() * System.Math.PI * 2.0);
                    float sinTheta = Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));
                    sebPlateCenters[i]    = new Vector3(sinTheta * Mathf.Cos(phi), u, sinTheta * Mathf.Sin(phi)).normalized;
                    sebPlateMacroValues[i] = (float)prng.NextDouble();
                }
                sebPlateDataReady = true;
            }
        }

        SebPlateSample SampleSebPlateField(Vector3 spherePoint)
        {
            EnsureSebPlateData();
            Vector3 p = spherePoint.normalized;
            float jitter = Mathf.Max(0f, sebPlateJitter);
            float macroF = Mathf.Max(0.001f, sebPlateMacroScale);

            float nearestDist = float.MaxValue, secondDist = float.MaxValue;
            float primaryValue = 0f, secondaryValue = 0f;

            for (int i = 0; i < sebPlateCenters.Length; i++)
            {
                float jx = FBM(sebPlateCenters[i] * macroF * 3.1f, 2, 0.5f, 2f, seed + i * 17);
                float jy = FBM(sebPlateCenters[i] * macroF * 3.1f + new Vector3(5.1f, 11.3f, 3.7f), 2, 0.5f, 2f, seed + i * 17 + 99);
                float jz = FBM(sebPlateCenters[i] * macroF * 3.1f + new Vector3(9.2f, 3.8f, 17.1f), 2, 0.5f, 2f, seed + i * 17 + 198);
                Vector3 jitteredCenter = (sebPlateCenters[i] + new Vector3(jx, jy, jz) * jitter).normalized;
                float d = Vector3.Distance(p, jitteredCenter);
                if (d < nearestDist)
                {
                    secondDist      = nearestDist;
                    secondaryValue  = primaryValue;
                    nearestDist     = d;
                    primaryValue    = sebPlateMacroValues[i];
                }
                else if (d < secondDist)
                {
                    secondDist     = d;
                    secondaryValue = sebPlateMacroValues[i];
                }
            }

            float gap = secondDist - nearestDist;
            float boundaryWidth = Mathf.Lerp(0.12f, 0.035f, Mathf.InverseLerp(8f, 64f, sebPlateCount));
            float boundary = 1f - Mathf.Clamp01(gap / Mathf.Max(0.0001f, boundaryWidth));
            float blend = boundary * Mathf.Clamp01(sebPlateBlend) * 0.5f;
            return new SebPlateSample
            {
                primaryValue   = primaryValue,
                secondaryValue = secondaryValue,
                blendedValue   = Mathf.Lerp(primaryValue, secondaryValue, blend),
                boundary       = boundary,
                primaryDistance   = nearestDist,
                secondaryDistance = secondDist
            };
        }

        // ── Post-process passes ───────────────────────────────────────────────

        void RelaxSebHeightmap(float[,] heightMap, float cellSize)
        {
            int w = heightMap.GetLength(0), h = heightMap.GetLength(1);
            float threshold = Mathf.Max(cellSize * 0.10f, normalizedMaxHeight * 0.0045f);
            var temp = new float[w, h];
            for (int pass = 0; pass < 2; pass++)
            {
                System.Array.Copy(heightMap, temp, w * h);
                for (int y = 1; y < h - 1; y++) for (int x = 1; x < w - 1; x++)
                {
                    float center = heightMap[x, y];
                    float avg4 = (heightMap[x-1,y] + heightMap[x+1,y] + heightMap[x,y-1] + heightMap[x,y+1]) * 0.25f;
                    float avg8 = avg4 * 0.5f + (heightMap[x-1,y-1] + heightMap[x+1,y-1] + heightMap[x-1,y+1] + heightMap[x+1,y+1]) * 0.125f;
                    float curvature = Mathf.Max(Mathf.Abs(avg4 - center), Mathf.Abs(avg8 - center));
                    float relax = Mathf.Lerp(0.02f, 0.16f, Mathf.SmoothStep(threshold, threshold * 5f, curvature));
                    temp[x, y] = Mathf.Lerp(center, Mathf.Lerp(avg4, avg8, 0.35f), relax);
                }
                for (int y = 1; y < h - 1; y++) for (int x = 1; x < w - 1; x++) heightMap[x, y] = temp[x, y];
            }
        }

        void SmoothHeightmap(float[,] heightMap, float cellSize, float strength)
        {
            if (strength <= 0.001f) return;
            int w = heightMap.GetLength(0), h = heightMap.GetLength(1);
            int passes = strength > 0.70f ? 2 : 1;
            float relaxStrength  = Mathf.Lerp(0.08f, 0.24f, Mathf.Clamp01(strength));
            float threshold      = Mathf.Max(cellSize * 0.16f, normalizedMaxHeight * 0.009f);
            var temp = new float[w, h];
            for (int pass = 0; pass < passes; pass++)
            {
                System.Array.Copy(heightMap, temp, w * h);
                for (int y = 1; y < h - 1; y++) for (int x = 1; x < w - 1; x++)
                {
                    float center = heightMap[x, y];
                    float avg4 = (heightMap[x-1,y] + heightMap[x+1,y] + heightMap[x,y-1] + heightMap[x,y+1]) * 0.25f;
                    float diag = (heightMap[x-1,y-1] + heightMap[x+1,y-1] + heightMap[x-1,y+1] + heightMap[x+1,y+1]) * 0.25f;
                    float laplacian  = avg4 - center;
                    float curvature  = Mathf.Max(Mathf.Abs(laplacian), Mathf.Abs(diag - center));
                    float spikeMask  = Mathf.SmoothStep(threshold, threshold * 4f, curvature);
                    temp[x, y] = center + laplacian * spikeMask * relaxStrength;
                }
                for (int y = 1; y < h - 1; y++) for (int x = 1; x < w - 1; x++) heightMap[x, y] = temp[x, y];
            }
        }

        void LimitSlope(float[,] heightMap, float cellSize)
        {
            if (slopeLimitPasses <= 0) return;
            int w = heightMap.GetLength(0), h = heightMap.GetLength(1);
            float maxDelta = Mathf.Tan(Mathf.Clamp(maxSlopeDegrees, 5f, 80f) * Mathf.Deg2Rad) * Mathf.Max(0.001f, cellSize);
            var delta = new float[w, h];
            for (int pass = 0; pass < slopeLimitPasses; pass++)
            {
                System.Array.Clear(delta, 0, delta.Length);
                for (int y = 1; y < h - 1; y++) for (int x = 1; x < w - 1; x++)
                {
                    float center = heightMap[x, y];
                    float eL = Mathf.Max(0f, center - heightMap[x-1, y] - maxDelta);
                    float eR = Mathf.Max(0f, center - heightMap[x+1, y] - maxDelta);
                    float eD = Mathf.Max(0f, center - heightMap[x, y-1] - maxDelta);
                    float eU = Mathf.Max(0f, center - heightMap[x, y+1] - maxDelta);
                    float total = eL + eR + eD + eU;
                    if (total <= 0f) continue;
                    float move = total * 0.14f;
                    delta[x, y] -= move;
                    if (eL > 0f) delta[x-1, y] += move * (eL / total);
                    if (eR > 0f) delta[x+1, y] += move * (eR / total);
                    if (eD > 0f) delta[x, y-1] += move * (eD / total);
                    if (eU > 0f) delta[x, y+1] += move * (eU / total);
                }
                for (int y = 1; y < h - 1; y++) for (int x = 1; x < w - 1; x++) heightMap[x, y] += delta[x, y];
            }
        }

        void ApplyErosion(float[,] heightMap, float cellSize)
        {
            if (erosionPasses <= 0 || erosionStrength <= 0.001f) return;
            int w = heightMap.GetLength(0), h = heightMap.GetLength(1);
            float talus = Mathf.Tan(Mathf.Max(5f, maxSlopeDegrees - 6f) * Mathf.Deg2Rad) * Mathf.Max(0.001f, cellSize);
            float maxTransport = normalizedMaxHeight * 0.006f;
            var delta = new float[w, h];
            for (int pass = 0; pass < erosionPasses; pass++)
            {
                System.Array.Clear(delta, 0, delta.Length);
                for (int y = 1; y < h - 1; y++) for (int x = 1; x < w - 1; x++)
                {
                    float center = heightMap[x, y];
                    int bx = x, by = y; float steepest = 0f;
                    for (int oy = -1; oy <= 1; oy++) for (int ox = -1; ox <= 1; ox++)
                    {
                        if (ox == 0 && oy == 0) continue;
                        float drop = center - heightMap[x+ox, y+oy];
                        if (drop > steepest) { steepest = drop; bx = x+ox; by = y+oy; }
                    }
                    if (steepest <= talus) continue;
                    float transport = Mathf.Min((steepest - talus) * 0.12f, maxTransport) * erosionStrength;
                    delta[x, y] -= transport;
                    delta[bx, by] += transport;
                }
                for (int y = 1; y < h - 1; y++) for (int x = 1; x < w - 1; x++) heightMap[x, y] += delta[x, y];
            }
        }

        void ClampSpikeHeights(float[,] heightMap, float cellSize)
        {
            int w = heightMap.GetLength(0), h = heightMap.GetLength(1);
            if (w < 3 || h < 3) return;
            var temp = new float[w, h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                temp[x, y] = Mathf.Clamp(heightMap[x, y], -normalizedMaxHeight, normalizedMaxHeight);
            float spikeThreshold = Mathf.Max(cellSize * 1.4f, normalizedMaxHeight * 0.018f);
            for (int y = 1; y < h - 1; y++) for (int x = 1; x < w - 1; x++)
            {
                float center = temp[x, y];
                float avg4 = (temp[x-1,y] + temp[x+1,y] + temp[x,y-1] + temp[x,y+1]) * 0.25f;
                float diag = (temp[x-1,y-1] + temp[x+1,y-1] + temp[x-1,y+1] + temp[x+1,y+1]) * 0.25f;
                float localAverage = avg4 * 0.75f + diag * 0.25f;
                float deviation = center - localAverage;
                if (Mathf.Abs(deviation) <= spikeThreshold) continue;
                heightMap[x, y] = Mathf.Lerp(center, localAverage + Mathf.Sign(deviation) * spikeThreshold, 0.72f);
            }
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                heightMap[x, y] = Mathf.Clamp(heightMap[x, y], -normalizedMaxHeight, normalizedMaxHeight);
        }

    }
}
