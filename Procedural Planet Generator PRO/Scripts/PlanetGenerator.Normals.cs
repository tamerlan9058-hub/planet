namespace PlanetGeneration
{
    using UnityEngine;

    /// <summary>
    /// Вычисление нормалей поверхности: ComputeSurfaceNormal, ComputeGridNormal,
    /// ApplyMicroNormal (detail + micro layer), AverageNormals, GetSafeTangent.
    /// </summary>
    public partial class PlanetGenerator
    {
        Vector3 AverageNormals(Vector3 a, Vector3 b)
        {
            Vector3 avg = (a + b).normalized;
            return avg.sqrMagnitude > 1e-6f ? avg : (a.sqrMagnitude > 1e-6f ? a.normalized : Vector3.up);
        }

        Vector3 GetSafeTangent(Vector3 normal)
        {
            Vector3 axis = Mathf.Abs(normal.y) < 0.95f ? Vector3.up : Vector3.right;
            return Vector3.Cross(axis, normal).normalized;
        }

        Vector3 ComputeGridNormal(Vector3[,] paddedSpherePoints, float[,] paddedHeightMap, int px, int py)
        {
            Vector3 SamplePos(int sx, int sy) =>
                BuildStableSurfacePosition(paddedSpherePoints[sx, sy], paddedHeightMap[sx, sy]);

            Vector3 left  = SamplePos(px - 1, py);
            Vector3 right = SamplePos(px + 1, py);
            Vector3 down  = SamplePos(px, py - 1);
            Vector3 up    = SamplePos(px, py + 1);

            Vector3 n = Vector3.Cross(right - left, up - down).normalized;
            if (Vector3.Dot(n, paddedSpherePoints[px, py]) < 0f) n = -n;
            return n;
        }

        Vector3 OffsetSphereDirection(Vector3 spherePoint, float height, Vector3 tangent, Vector3 bitangent,
                                      float tOffset, float bOffset)
        {
            float sampleRadius = radius + ClampTerrainHeight(height);
            return (spherePoint.normalized * sampleRadius + tangent * tOffset + bitangent * bOffset).normalized;
        }

        Vector3 ComputeSurfaceNormal(Vector3 spherePoint, float height, float cellSize)
        {
            Vector3 radial    = spherePoint.normalized;
            Vector3 tangent   = GetSafeTangent(radial);
            Vector3 bitangent = Vector3.Cross(radial, tangent).normalized;
            float sampleDist  = Mathf.Max(normalSampleDistance, cellSize * 0.85f);

            Vector3 leftDir  = OffsetSphereDirection(radial, height, tangent, bitangent, -sampleDist, 0f);
            Vector3 rightDir = OffsetSphereDirection(radial, height, tangent, bitangent,  sampleDist, 0f);
            Vector3 downDir  = OffsetSphereDirection(radial, height, tangent, bitangent, 0f, -sampleDist);
            Vector3 upDir    = OffsetSphereDirection(radial, height, tangent, bitangent, 0f,  sampleDist);

            Vector3 left  = BuildStableSurfacePosition(leftDir,  EvaluateMacroHeightCached(leftDir));
            Vector3 right = BuildStableSurfacePosition(rightDir, EvaluateMacroHeightCached(rightDir));
            Vector3 down  = BuildStableSurfacePosition(downDir,  EvaluateMacroHeightCached(downDir));
            Vector3 up2   = BuildStableSurfacePosition(upDir,    EvaluateMacroHeightCached(upDir));

            Vector3 n = Vector3.Cross(right - left, up2 - down).normalized;
            if (Vector3.Dot(n, radial) < 0f) n = -n;
            return n;
        }

        Vector3 ApplyMicroNormal(Vector3 spherePoint, Vector3 baseNormal, float height, int lod)
        {
            if (terrainStyle == TerrainStyle.SebInspired) return baseNormal;

            float lodFactor = lod == 0 ? 1f : (lod == 1 ? 0.55f : (lod == 2 ? 0.28f : 0.14f));
            float microNormalAmount = Mathf.Clamp01((detailStrength * 1.1f + microStrength * 1.4f) * lodFactor);
            if (microNormalAmount <= 0.001f) return baseNormal;

            Vector3 radial  = spherePoint.normalized;
            float   slope01 = 1f - Mathf.Clamp01(Vector3.Dot(baseNormal, radial));
            float   landMask = Mathf.SmoothStep(
                seaLevelHeight + beachWidthHeight * 0.25f,
                seaLevelHeight + beachWidthHeight + normalizedMaxHeight * 0.04f,
                height);
            float slopeMask  = Mathf.SmoothStep(0.05f, 0.32f, slope01);
            float slopeBias  = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(detailSlopeBoost));
            float altitudeMask = Mathf.SmoothStep(seaLevelHeight + beachWidthHeight, normalizedMaxHeight * 0.62f, height);
            float mask = landMask * Mathf.Lerp(0.20f, 1f, slopeMask * slopeBias) * Mathf.Lerp(0.55f, 1f, altitudeMask);
            if (mask <= 0.001f) return baseNormal;

            Vector3 tangent   = GetSafeTangent(radial);
            Vector3 bitangent = Vector3.Cross(radial, tangent).normalized;

            float styleScale = GetStylizedFrequencyScale();
            float macroFreq  = 0.00115f * styleScale * (28f / Mathf.Max(8f, detailScale));
            float fineFreq   = 0.0028f  * styleScale * (80f / Mathf.Max(16f, microScale));
            float epsMacro   = Mathf.Max(3.5f, radius * 0.0012f);
            float epsFine    = Mathf.Max(1.8f, radius * 0.00055f);
            Vector3 worldP   = spherePoint * radius + seedOffset * 91.37f;

            float baseMacro = FBM(worldP * macroFreq, 2, 0.45f, 1.92f, seed + 9001);
            float macroX    = FBM((worldP + tangent   * epsMacro) * macroFreq, 2, 0.45f, 1.92f, seed + 9001);
            float macroY    = FBM((worldP + bitangent * epsMacro) * macroFreq, 2, 0.45f, 1.92f, seed + 9001);
            float baseFine  = Perlin3D(worldP * fineFreq);
            float fineX     = Perlin3D((worldP + tangent   * epsFine) * fineFreq);
            float fineY     = Perlin3D((worldP + bitangent * epsFine) * fineFreq);

            float dx = (macroX - baseMacro) * 0.75f + (fineX - baseFine) * 0.25f;
            float dy = (macroY - baseMacro) * 0.75f + (fineY - baseFine) * 0.25f;
            float normalStrength = microNormalAmount * mask * Mathf.Lerp(0.65f, 1.10f, slopeMask * slopeBias);

            Vector3 detailNormal = (baseNormal - tangent * dx * normalStrength * 6.2f
                                               - bitangent * dy * normalStrength * 6.2f).normalized;
            return Vector3.Slerp(baseNormal, detailNormal, mask * Mathf.Lerp(0.35f, 0.85f, slopeMask * slopeBias));
        }
    }
}
