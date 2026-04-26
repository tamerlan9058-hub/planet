namespace PlanetGeneration
{
    using UnityEngine;
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>
    /// Система деревьев: SpawnTreesForChunk, PlaceTreeOnSurface,
    /// GetPooledTree, RecycleTrees, ApplyTreeShader.
    /// </summary>
    public partial class PlanetGenerator
    {
        void RecycleTrees(ChunkData chunk)
        {
            if (chunk == null) return;
            foreach (var t in chunk.treePool)
                if (t != null) t.SetActive(false);
        }

        GameObject GetPooledTree(ChunkData chunk, int treeIndex)
        {
            if (chunk == null || chunk.obj == null) return null;
            foreach (var pooled in chunk.treePool)
            {
                if (pooled != null && !pooled.activeSelf)
                {
                    pooled.name = "TreeInstance_" + treeIndex;
                    pooled.SetActive(true);
                    return pooled;
                }
            }
            var created = Instantiate(treePrefab, chunk.obj.transform, false);
            created.name = "TreeInstance_" + treeIndex;
            chunk.treePool.Add(created);
            return created;
        }

        IEnumerator SpawnTreesForChunk(ChunkData chunk,
            float[,] hm, Vector3[,] sp, Vector3[,] pos, Vector3[] normals, int vEdge)
        {
            int count = 0;
            for (int y = 1; y < vEdge - 1; y += 2)
                for (int x = 1; x < vEdge - 1; x += 2)
                {
                    if (hm[x, y] <= oceanLevel) continue;
                    int   idx = y * vEdge + x;
                    float sl  = 1f - Vector3.Dot(normals[idx], sp[x, y].normalized);
                    if (sl > maxTreeSlope) continue;

                    float forestDensity = EvaluateForestDensity(sp[x, y], hm[x, y]);
                    if (forestDensity <= 0.16f) continue;

                    float spawnChance = Mathf.Clamp01(treeProbability * Mathf.Lerp(0.35f, 3.1f, forestDensity));
                    if (Random.value > spawnChance) continue;

                    var t = GetPooledTree(chunk, count);
                    if (t == null) continue;
                    float s = Random.Range(0.8f, 1.3f);
                    PlaceTreeOnSurface(t, pos[x, y], normals[idx], s, Random.Range(0f, 360f));
                    ApplyTreeShader(t);
                    count++;
                    if (count >= maxTreesPerChunk) yield break;
                    if (count % 20 == 0) yield return null;
                }
        }

        void PlaceTreeOnSurface(GameObject tree, Vector3 localSurfacePos, Vector3 localSurfaceNormal,
                                float uniformScale, float spinDegrees)
        {
            Vector3 safeNormal = localSurfaceNormal.sqrMagnitude > 1e-6f
                ? localSurfaceNormal.normalized
                : localSurfacePos.normalized;
            var tr = tree.transform;

            tr.localRotation =
                Quaternion.FromToRotation(Vector3.up, safeNormal) *
                Quaternion.AngleAxis(spinDegrees, Vector3.up);
            tr.localScale = Vector3.one * uniformScale;

            Vector3 worldPos    = transform.TransformPoint(localSurfacePos);
            Vector3 worldNormal = transform.TransformDirection(safeNormal).normalized;
            tr.position = worldPos;

            float bottomOffset = CalculateBottomOffsetAlongNormal(tree, worldPos, worldNormal);
            tr.position = worldPos + worldNormal * (bottomOffset + treeSurfaceOffset);
        }

        float CalculateBottomOffsetAlongNormal(GameObject obj, Vector3 surfacePoint, Vector3 surfaceNormal)
        {
            float minProj = float.MaxValue;
            bool  found   = false;

            foreach (var mf in obj.GetComponentsInChildren<MeshFilter>())
            {
                if (mf == null || mf.sharedMesh == null) continue;
                Bounds  b  = mf.sharedMesh.bounds;
                Vector3 c  = b.center;
                Vector3 e  = b.extents;
                var     tr = mf.transform;

                for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 corner = tr.TransformPoint(c + Vector3.Scale(e, new Vector3(sx, sy, sz)));
                    float   proj   = Vector3.Dot(corner - surfacePoint, surfaceNormal);
                    if (proj < minProj) minProj = proj;
                    found = true;
                }
            }

            if (!found || minProj >= 0f) return 0f;
            return -minProj;
        }

        void ApplyTreeShader(GameObject tree)
        {
            if (treeShader == null) treeShader = Shader.Find("Custom/PlanetTreeShader");
            if (treeShader == null) return;
            foreach (var mr in tree.GetComponentsInChildren<MeshRenderer>())
            {
                Color c = mr.sharedMaterial != null ? mr.sharedMaterial.color : Color.white;
                var mat = new Material(treeShader) { color = c };
                if (mat.HasProperty("_PlanetCenter")) mat.SetVector("_PlanetCenter", transform.position);
                mr.sharedMaterial = mat;
            }
        }
    }
}
