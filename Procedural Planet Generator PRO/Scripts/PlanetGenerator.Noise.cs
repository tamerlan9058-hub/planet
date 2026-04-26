namespace PlanetGeneration
{
    using UnityEngine;

    /// <summary>
    /// NMS Uber Noise Engine: примитивы шума — Value/Perlin, FBM, Ridge, Mesa, Canyon,
    /// Seb-noise и вспомогательные функции интерполяции.
    /// Все методы намеренно stateless (используют только параметры + поля seed/radius).
    /// </summary>
    public partial class PlanetGenerator
    {
        // ── Hash / interpolation primitives ──────────────────────────────────

        static double Hermite01(double t) => t * t * (3.0 - 2.0 * t);
        static double LerpDouble(double a, double b, double t) => a + (b - a) * t;

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

        static float HashSigned(int x, int y, int z) => (float)(Hash01(x, y, z) * 2.0 - 1.0);

        // ── Value noise (trilinear interpolation) ─────────────────────────────

        double ValueNoise3D(Vector3 p)
        {
            double x = p.x, y = p.y, z = p.z;
            int ix = (int)System.Math.Floor(x);
            int iy = (int)System.Math.Floor(y);
            int iz = (int)System.Math.Floor(z);

            double fx = Hermite01(x - ix);
            double fy = Hermite01(y - iy);
            double fz = Hermite01(z - iz);

            double n000 = Hash01(ix,   iy,   iz  ), n100 = Hash01(ix+1, iy,   iz  );
            double n010 = Hash01(ix,   iy+1, iz  ), n110 = Hash01(ix+1, iy+1, iz  );
            double n001 = Hash01(ix,   iy,   iz+1), n101 = Hash01(ix+1, iy,   iz+1);
            double n011 = Hash01(ix,   iy+1, iz+1), n111 = Hash01(ix+1, iy+1, iz+1);

            double nx00 = LerpDouble(n000, n100, fx), nx10 = LerpDouble(n010, n110, fx);
            double nx01 = LerpDouble(n001, n101, fx), nx11 = LerpDouble(n011, n111, fx);
            double nxy0 = LerpDouble(nx00, nx10, fy), nxy1 = LerpDouble(nx01, nx11, fy);
            return LerpDouble(nxy0, nxy1, fz) * 2.0 - 1.0;
        }

        float Perlin3D(Vector3 p) => (float)ValueNoise3D(p);

        // ── FBM ───────────────────────────────────────────────────────────────

        struct NoiseResult { public float value; public float deriv; }

        NoiseResult FBM_Derivative(Vector3 p, int octaves, float persistence, float lacunarity,
                                   int seedOff, float expWeight = 1.6f, float erosion = 0f)
        {
            var   off    = new Vector3(seedOff * 0.1173f, seedOff * 0.2341f, seedOff * 0.3512f);
            float amp    = 1f, freq = 1f, sum = 0f, ampSum = 0f;
            float dx     = 0f, dy = 0f, dz = 0f;
            const float derivStep = 0.00085f;

            for (int i = 0; i < octaves; i++)
            {
                Vector3 pp = p * freq + off;
                float n    = Perlin3D(pp);
                float ndxA = Perlin3D(pp + new Vector3(derivStep, 0, 0));
                float ndxB = Perlin3D(pp - new Vector3(derivStep, 0, 0));
                float ndyA = Perlin3D(pp + new Vector3(0, derivStep, 0));
                float ndyB = Perlin3D(pp - new Vector3(0, derivStep, 0));
                float ndzA = Perlin3D(pp + new Vector3(0, 0, derivStep));
                float ndzB = Perlin3D(pp - new Vector3(0, 0, derivStep));

                float gx = ((ndxA - ndxB) / (2f * derivStep)) * freq;
                float gy = ((ndyA - ndyB) / (2f * derivStep)) * freq;
                float gz = ((ndzA - ndzB) / (2f * derivStep)) * freq;

                float se = (erosion > 0f && i > 0)
                    ? 1f / (1f + Mathf.Sqrt(dx*dx + dy*dy + dz*dz) * erosion)
                    : 1f;

                float expAmp = Mathf.Pow(0.65f, i * Mathf.Max(0.25f, expWeight));
                float a = amp * se * expAmp;
                sum += n * a; ampSum += a;
                dx  += gx * a; dy += gy * a; dz += gz * a;
                amp  *= persistence; freq *= lacunarity;
            }

            float val   = ampSum > 0f ? sum / ampSum : 0f;
            float deriv = Mathf.Sqrt(dx*dx + dy*dy + dz*dz) / Mathf.Max(1f, ampSum);
            return new NoiseResult { value = val, deriv = deriv };
        }

        float FBM(Vector3 p, int octaves, float persistence, float lacunarity, int seedOff)
        {
            var   off    = new Vector3(seedOff * 0.1173f, seedOff * 0.2341f, seedOff * 0.3512f);
            float amp    = 1f, freq = 1f, sum = 0f, ampSum = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum    += Perlin3D(p * freq + off) * amp;
                ampSum += amp;
                amp *= persistence; freq *= lacunarity;
            }
            return ampSum > 0f ? sum / ampSum : 0f;
        }

        float RidgeFBM(Vector3 p, int octaves, float persistence, float lacunarity,
                       int seedOff, float gain = 1f, float sharpness = 0.65f)
        {
            var   off    = new Vector3(seedOff * 0.1173f, seedOff * 0.2341f, seedOff * 0.3512f);
            float amp    = 0.5f, freq = 1f, sum = 0f, ampSum = 0f, prev = 1f;
            for (int i = 0; i < octaves; i++)
            {
                float n     = Perlin3D(p * freq + off);
                float ridge = 1f - Mathf.Abs(n);
                ridge = Mathf.Pow(ridge, 1f + sharpness * 1.5f);
                ridge *= prev * gain; prev = ridge;
                sum    += ridge * amp; ampSum += amp;
                amp *= persistence; freq *= lacunarity;
            }
            return ampSum > 0f ? sum / ampSum : 0f;
        }

        // ── Terrace / Mesa / Canyon ───────────────────────────────────────────

        float ApplySoftTerrace(float value01, float steps, float blend)
        {
            value01 = Mathf.Clamp01(value01);
            blend   = Mathf.Clamp01(blend);
            steps   = Mathf.Max(1f, steps);
            if (blend <= 0.001f) return value01;
            float scaled  = value01 * steps;
            float lower   = Mathf.Floor(scaled);
            float upper   = Mathf.Min(steps, lower + 1f);
            float t       = scaled - lower;
            float smoothT = t * t * (3f - 2f * t);
            float terraced = Mathf.Lerp(lower, upper, smoothT) / steps;
            return Mathf.Lerp(value01, terraced, blend);
        }

        float MesaNoise(Vector3 p, int seedOff)
        {
            var   off  = new Vector3(seedOff * 0.1173f, seedOff * 0.2341f, seedOff * 0.3512f);
            float base01 = FBM(p * mesaScale + off, 3, 0.5f, 2.1f, seedOff) * 0.5f + 0.5f;
            float mesa   = Mathf.Clamp01(base01);
            float mesaMask = Mathf.SmoothStep(
                Mathf.Clamp01(mesaFrequency - 0.10f),
                Mathf.Clamp01(mesaFrequency + 0.16f), base01);
            float flattened = 1f - Mathf.Pow(1f - mesa, 1f + Mathf.Clamp(mesaSharpness, 1f, 20f) * 0.05f);
            float terraced  = ApplySoftTerrace(flattened,
                2.4f + Mathf.Clamp(mesaSharpness, 1f, 20f) * 0.12f,
                0.20f * mesaMask);
            return Mathf.Clamp01(Mathf.Lerp(mesa, terraced, mesaMask * 0.55f));
        }

        float CanyonNoise(Vector3 p, int seedOff)
        {
            var   off    = new Vector3(seedOff * 0.1173f, seedOff * 0.2341f, seedOff * 0.3512f);
            float n      = Perlin3D(p * canyonScale + off);
            float canyon = Mathf.Pow(Mathf.Abs(n), 0.5f);
            float mask   = FBM(p * canyonScale * 0.4f + off, 2, 0.5f, 2f, seedOff + 77) * 0.5f + 0.5f;
            mask = Mathf.SmoothStep(canyonFrequency - 0.15f, canyonFrequency + 0.15f, mask);
            return canyon * mask;
        }

        // ── Seb-specific noise ────────────────────────────────────────────────

        float SebSimpleNoise(Vector3 point, int numLayers, float lacunarity, float persistence,
                             float scale, float elevation, float verticalShift, int seedOff)
        {
            Vector3 offset    = new Vector3(seedOff * 0.1173f, seedOff * 0.2341f, seedOff * 0.3512f);
            float   sum       = 0f, amplitude = 1f, frequency = Mathf.Max(0.0001f, scale);
            for (int i = 0; i < Mathf.Max(1, numLayers); i++)
            {
                sum += Perlin3D(point * frequency + offset) * amplitude;
                amplitude *= persistence; frequency *= lacunarity;
            }
            return sum * elevation + verticalShift;
        }

        float SebRidgeNoise(Vector3 point, int numLayers, float lacunarity, float persistence,
                            float scale, float power, float elevation, float gain,
                            float verticalShift, int seedOff)
        {
            Vector3 offset      = new Vector3(seedOff * 0.1173f, seedOff * 0.2341f, seedOff * 0.3512f);
            float   sum         = 0f, amplitude = 1f;
            float   frequency   = Mathf.Max(0.0001f, scale);
            float   ridgeWeight = 1f;
            for (int i = 0; i < Mathf.Max(1, numLayers); i++)
            {
                float noiseVal = 1f - Mathf.Abs(Perlin3D(point * frequency + offset));
                noiseVal = Mathf.Pow(Mathf.Abs(noiseVal), Mathf.Max(0.0001f, power));
                noiseVal *= ridgeWeight;
                ridgeWeight = Mathf.Clamp01(noiseVal * Mathf.Max(0f, gain));
                sum += noiseVal * amplitude;
                amplitude *= persistence; frequency *= lacunarity;
            }
            return sum * elevation + verticalShift;
        }

        float SebSmoothedRidgeNoise(Vector3 point, int numLayers, float lacunarity, float persistence,
                                    float scale, float power, float elevation, float gain,
                                    float verticalShift, float peakSmoothing, int seedOff)
        {
            Vector3 radial = point.normalized;
            Vector3 axisA  = GetSafeTangent(radial);
            Vector3 axisB  = Vector3.Cross(radial, axisA).normalized;
            float offset   = Mathf.Max(0f, peakSmoothing) * 0.01f;

            float s0 = SebRidgeNoise(point, numLayers, lacunarity, persistence, scale, power, elevation, gain, verticalShift, seedOff);
            float s1 = SebRidgeNoise((point - axisA * offset).normalized, numLayers, lacunarity, persistence, scale, power, elevation, gain, verticalShift, seedOff);
            float s2 = SebRidgeNoise((point + axisA * offset).normalized, numLayers, lacunarity, persistence, scale, power, elevation, gain, verticalShift, seedOff);
            float s3 = SebRidgeNoise((point - axisB * offset).normalized, numLayers, lacunarity, persistence, scale, power, elevation, gain, verticalShift, seedOff);
            float s4 = SebRidgeNoise((point + axisB * offset).normalized, numLayers, lacunarity, persistence, scale, power, elevation, gain, verticalShift, seedOff);
            return (s0 + s1 + s2 + s3 + s4) * 0.2f;
        }

        // ── Smooth min/max, Seb blend ─────────────────────────────────────────

        float SmoothMaxValue(float a, float b, float smoothing) => -SmoothMinValue(-a, -b, smoothing);

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
    }
}
