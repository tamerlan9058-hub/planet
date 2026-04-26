namespace PlanetGeneration
{
    using UnityEngine;
    using System.Collections;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using UnityEngine.Rendering;

    /// <summary>
    /// Chunk infrastructure: структура данных, инициализация, LOD-логика, генерация меша.
    /// </summary>
    public partial class PlanetGenerator
    {
        // ── Chunk data ────────────────────────────────────────────────────────

        private class ChunkData
        {
            public GameObject  obj;
            public Face        face;
            public int         x, y;
            public int         currentLOD    = -1;
            public bool        isGenerating  = false;
            public Mesh        mesh;
            public Vector3     centerWorldPos;
            public readonly List<GameObject>  treePool     = new List<GameObject>(16);
            public readonly List<Vector3Int>  neighborKeys = new List<Vector3Int>(8);
        }

        // ── Chunk state helpers ───────────────────────────────────────────────

        bool ChunkHasBuiltMesh(ChunkData chunk)
        {
            if (chunk == null || chunk.obj == null || !chunk.obj.activeSelf) return false;
            var mf = chunk.obj.GetComponent<MeshFilter>();
            return mf != null && mf.sharedMesh != null;
        }

        bool ChunkHasReadyCollider(ChunkData chunk)
        {
            if (chunk == null || chunk.obj == null || !chunk.obj.activeSelf) return false;
            var mc = chunk.obj.GetComponent<MeshCollider>();
            return mc != null && mc.enabled && mc.sharedMesh != null;
        }

        void EvaluateSurfaceReadiness(out int guardedChunks, out int readyChunks, out int colliderReadyChunks)
        {
            float guardDistance = GetSurfaceSafetyDistance();
            guardedChunks = 0; readyChunks = 0; colliderReadyChunks = 0;
            foreach (var kvp in chunks)
            {
                var cd = kvp.Value;
                if (cd == null || cd.obj == null) continue;
                if (player != null)
                {
                    float d = Vector3.Distance(player.position, transform.TransformPoint(cd.centerWorldPos));
                    if (d > guardDistance) continue;
                }
                guardedChunks++;
                if (ChunkHasBuiltMesh(cd))        readyChunks++;
                if (!useColliders || ChunkHasReadyCollider(cd)) colliderReadyChunks++;
            }
        }

        // ── Distance helpers ──────────────────────────────────────────────────

        float GetChunkWorldSpan()           => Mathf.Max(radius * (2f / Mathf.Max(1, chunksPerFace)) * 0.95f, radius * 0.08f);
        float GetChunkHorizonBias()         => Mathf.Clamp(GetChunkWorldSpan() / Mathf.Max(1f, radius), 0.04f, 0.18f);
        float GetSurfaceSafetyDistance()    => Mathf.Max(radius * 0.36f, colliderDistance * 0.70f) + GetChunkWorldSpan() * 1.65f;
        float GetColliderSafetyDistance()   => Mathf.Max(colliderDistance * 0.95f, radius * 0.34f) + GetChunkWorldSpan() * 0.85f;

        float GetBacksideCullStartDistance()
        {
            float baseDistance = terrainStyle == TerrainStyle.SebInspired ? radius * 0.90f : radius * 0.42f;
            return Mathf.Max(GetSurfaceSafetyDistance() * 1.45f, baseDistance);
        }

        bool IsChunkBacksideCulled(ChunkData chunk, Vector3 viewerPosition)
        {
            if (chunk == null) return false;
            Vector3 viewerFromCenter = viewerPosition - transform.position;
            if (viewerFromCenter.sqrMagnitude <= 1e-6f) return false;

            float viewerSurfaceDistance = Mathf.Max(0f, viewerFromCenter.magnitude - radius);
            float cullStartDistance     = GetBacksideCullStartDistance();
            if (viewerSurfaceDistance <= cullStartDistance) return false;

            Vector3 viewerDir = viewerFromCenter.normalized;
            float horizonBias = GetChunkHorizonBias();
            float cullBlend   = Mathf.InverseLerp(cullStartDistance, cullStartDistance + radius * 1.35f, viewerSurfaceDistance);
            float nearSurfaceSlack = Mathf.Lerp(0.24f, 0.02f, cullBlend);
            float dot = Vector3.Dot(chunk.centerWorldPos.normalized, viewerDir);
            return dot < -(horizonBias + nearSurfaceSlack);
        }

        bool IsChunkInFrustum(Vector3 wp)
        {
            if (mainCamera == null) return true;
            Vector3 vp = mainCamera.WorldToViewportPoint(wp);
            return vp.z > 0f && vp.x > -0.3f && vp.x < 1.3f && vp.y > -0.3f && vp.y < 1.3f;
        }

        // ── Chunk lifecycle ───────────────────────────────────────────────────

        void DeactivateChunk(ChunkData chunk, bool releaseResources)
        {
            if (chunk == null) return;
            if (chunk.obj != null) chunk.obj.SetActive(false);
            chunk.currentLOD = -1;
            if (releaseResources && !chunk.isGenerating) ReleaseChunkResources(chunk);
        }

        GameObject EnsureChunkObject(ChunkData chunk)
        {
            if (chunk == null) return null;
            if (chunk.obj != null) return chunk.obj;
            chunk.obj = new GameObject($"Chunk_F{(int)chunk.face}_{chunk.x}_{chunk.y}");
            chunk.obj.transform.SetParent(transform, false);
            chunk.obj.transform.localPosition = Vector3.zero;
            chunk.obj.SetActive(false);
            return chunk.obj;
        }

        void ReleaseChunkResources(ChunkData chunk)
        {
            if (chunk == null) return;
            RecycleTrees(chunk);
            chunk.treePool.Clear();
            if (chunk.obj != null)  { Destroy(chunk.obj);  chunk.obj  = null; }
            if (chunk.mesh != null) { Destroy(chunk.mesh); chunk.mesh = null; }
        }

        // ── Chunk structure ───────────────────────────────────────────────────

        void QueueAllChunksForRefresh()
        {
            EnsureChunkStructure();
            generateQueue.Clear();
            nextQueueSortTime = -999f;
            foreach (var kvp in chunks) { kvp.Value.currentLOD = -1; if (!generateQueue.Contains(kvp.Value)) generateQueue.Add(kvp.Value); }
            hasStartedLoading = true;
        }

        void EnsureChunkStructure()
        {
            if (chunkStructureInitialized) return;
            InitializeChunkStructure();
            chunkStructureInitialized = true;
        }

        void InitializeChunkStructure()
        {
            if (chunkStructureInitialized) return;
            for (int f = 0; f < 6; f++)
                for (int y = 0; y < chunksPerFace; y++)
                    for (int x = 0; x < chunksPerFace; x++)
                    {
                        var key = new Vector3Int(f, x, y);
                        float cs = 2f / chunksPerFace;
                        float u  = -1f + (x + 0.5f) * cs;
                        float v  = -1f + (y + 0.5f) * cs;
                        chunks[key] = new ChunkData
                        {
                            face = (Face)f, x = x, y = y,
                            centerWorldPos = GetCubePoint((Face)f, u, v).normalized * radius
                        };
                    }
            BuildChunkNeighbors();
            chunkStructureInitialized = true;
        }

        void AddNeighborKey(ChunkData chunk, Vector3Int key)
        {
            if (chunk == null || !chunks.ContainsKey(key) || chunk.neighborKeys.Contains(key)) return;
            chunk.neighborKeys.Add(key);
        }

        Vector3 GetChunkCenterDirection(Face face, int x, int y)
        {
            float cs = 2f / chunksPerFace;
            float u  = -1f + (x + 0.5f) * cs;
            float v  = -1f + (y + 0.5f) * cs;
            return GetCubePoint(face, u, v).normalized;
        }

        void BuildChunkNeighbors()
        {
            float cs            = 2f / chunksPerFace;
            float outsideOffset = cs * 0.55f;
            foreach (var kvp in chunks)
            {
                var chunk = kvp.Value;
                if (chunk == null) continue;
                chunk.neighborKeys.Clear();

                Vector3Int[] sameFaceKeys =
                {
                    new Vector3Int((int)chunk.face, chunk.x - 1, chunk.y),
                    new Vector3Int((int)chunk.face, chunk.x + 1, chunk.y),
                    new Vector3Int((int)chunk.face, chunk.x, chunk.y - 1),
                    new Vector3Int((int)chunk.face, chunk.x, chunk.y + 1)
                };
                foreach (var key in sameFaceKeys) AddNeighborKey(chunk, key);

                float startU = -1f + chunk.x * cs;
                float startV = -1f + chunk.y * cs;
                float midU   = startU + cs * 0.5f;
                float midV   = startV + cs * 0.5f;

                void AddClosestCrossFaceNeighbor(Vector3 direction)
                {
                    float bestDot = -1f; Vector3Int bestKey = default; bool found = false;
                    foreach (var otherKvp in chunks)
                    {
                        if (otherKvp.Value == null || otherKvp.Value.face == chunk.face) continue;
                        float dot = Vector3.Dot(otherKvp.Value.centerWorldPos.normalized, direction);
                        if (!found || dot > bestDot) { found = true; bestDot = dot; bestKey = otherKvp.Key; }
                    }
                    if (found) AddNeighborKey(chunk, bestKey);
                }

                if (chunk.x == 0)              AddClosestCrossFaceNeighbor(GetCubePoint(chunk.face, startU - outsideOffset, midV).normalized);
                if (chunk.x == chunksPerFace-1)AddClosestCrossFaceNeighbor(GetCubePoint(chunk.face, startU + cs + outsideOffset, midV).normalized);
                if (chunk.y == 0)              AddClosestCrossFaceNeighbor(GetCubePoint(chunk.face, midU, startV - outsideOffset).normalized);
                if (chunk.y == chunksPerFace-1)AddClosestCrossFaceNeighbor(GetCubePoint(chunk.face, midU, startV + cs + outsideOffset).normalized);
            }
        }

        IEnumerable<ChunkData> EnumerateNeighbors(ChunkData chunk)
        {
            if (chunk == null) yield break;
            foreach (var key in chunk.neighborKeys)
                if (chunks.TryGetValue(key, out var neighbor))
                    yield return neighbor;
        }

        // ── LOD ───────────────────────────────────────────────────────────────

        int GetLODForDistance(float d)
        {
            if (d > unloadDistance) return -1;
            if (d < lodDistance0)   return 0;
            if (d < lodDistance1)   return 1;
            if (d < lodDistance2)   return 2;
            if (d < lodDistance3)   return 3;
            return -1;
        }

        int GetVertsForLOD(int lod)
        {
            int maxLod = Mathf.Max(0, lodLevels - 1);
            int minHighDetailVerts = terrainStyle == TerrainStyle.SebInspired ? 192 : 160;
            int highDetailVerts    = Mathf.Max(minHighDetailVerts, maxVertsPerChunk);
            int lodDivisor         = 1 << Mathf.Clamp(maxLod, 0, 6);
            highDetailVerts = Mathf.CeilToInt(highDetailVerts / (float)lodDivisor) * lodDivisor;
            int clampedLod  = Mathf.Clamp(lod, 0, maxLod);
            int verts       = highDetailVerts / (1 << clampedLod);
            return Mathf.Max(12, verts);
        }

        int GetTargetLOD(ChunkData chunk, float distance, Vector3 worldPos)
        {
            if (player != null && IsChunkBacksideCulled(chunk, player.position)) return -1;
            int maxLod = Mathf.Max(0, lodLevels - 1);
            if (maxLod == 0) return 0;

            float surfaceDistance = distance;
            float chunkSpan       = Mathf.Max(1f, GetChunkWorldSpan());
            float detailRatio     = surfaceDistance / chunkSpan;
            int target;

            if (activeLodSystem)
            {
                float lod0RadiusChunks = Mathf.Max(0f, lod0RadiusInChunks);
                if (detailRatio <= lod0RadiusChunks)
                {
                    target = 0;
                }
                else
                {
                    float farRatio = Mathf.Max(lod0RadiusChunks + 0.01f, unloadDistance / chunkSpan);
                    float t = Mathf.Clamp01((detailRatio - lod0RadiusChunks) / Mathf.Max(0.01f, farRatio - lod0RadiusChunks));
                    float curveT = lodTransitionCurve != null && lodTransitionCurve.length > 0
                        ? Mathf.Clamp01(lodTransitionCurve.Evaluate(t))
                        : t;
                    target = Mathf.Clamp(Mathf.CeilToInt(curveT * maxLod), 1, maxLod);
                }
            }
            else
            {
                target = Mathf.Clamp(Mathf.FloorToInt(detailRatio), 0, maxLod);
            }

            float safetyDistance = GetSurfaceSafetyDistance();
            bool  inSafetyBand   = distance <= safetyDistance;

            if (activeLodSystem && detailRatio < Mathf.Max(0.5f, lod0RadiusInChunks))
                target = 0;
            else if (!activeLodSystem && detailRatio < 10f)
            {
                int midLod = 1 + Mathf.FloorToInt((detailRatio - 3f) / 3.5f);
                target = Mathf.Clamp(midLod, 1, Mathf.Min(maxLod, 2));
            }

            if (!inSafetyBand && target <= 1 && !IsChunkInFrustum(worldPos))
                target = Mathf.Min(maxLod, target + 1);

            foreach (var neighbor in EnumerateNeighbors(chunk))
            {
                if (neighbor == null || neighbor.currentLOD < 0) continue;
                if (terrainStyle == TerrainStyle.SebInspired && target <= 1 && neighbor.currentLOD <= 1)
                    target = Mathf.Min(target, neighbor.currentLOD);
                else
                    target = Mathf.Min(target, neighbor.currentLOD + 1);
            }

            if (inSafetyBand)
                target = terrainStyle == TerrainStyle.SebInspired ? 0 : Mathf.Min(target, Mathf.Min(1, maxLod));

            return target;
        }

        // ── Coroutine loops ───────────────────────────────────────────────────

        IEnumerator UpdateChunksLoop()
        {
            var wait = new WaitForSeconds(updateInterval);
            while (streamingActive)
            {
                if (player == null) { yield return wait; continue; }
                Vector3 pp = player.position;
                float colliderSafetyDistance = GetColliderSafetyDistance();
                foreach (var kvp in chunks)
                {
                    var cd  = kvp.Value;
                    var cwp = transform.TransformPoint(cd.centerWorldPos);
                    float d = Vector3.Distance(pp, cwp);
                    if (d > unloadDistance)                { DeactivateChunk(cd, releaseResources: true);  continue; }
                    if (IsChunkBacksideCulled(cd, pp))     { DeactivateChunk(cd, releaseResources: false); continue; }

                    int tl = GetTargetLOD(cd, d, cwp);
                    if (tl < 0) continue;
                    if (tl != cd.currentLOD && !cd.isGenerating && !generateQueue.Contains(cd))
                    { generateQueue.Add(cd); hasStartedLoading = true; }

                    if (useColliders && cd.obj != null)
                    {
                        var mc = cd.obj.GetComponent<MeshCollider>();
                        bool nc = d < colliderSafetyDistance;
                        if (mc != null && mc.enabled != nc) mc.enabled = nc;
                    }
                }
                yield return wait;
            }
            updateChunksCoroutine = null;
        }

        IEnumerator GenerateChunksLoop()
        {
            while (streamingActive)
            {
                if (generateQueue.Count > 0 && player != null)
                {
                    Vector3 pp = player.position;
                    float safetyDistance = GetSurfaceSafetyDistance();
                    if (generateQueue.Count > 1 && Time.time >= nextQueueSortTime)
                    {
                        nextQueueSortTime = Time.time + 0.08f;
                        generateQueue.Sort((a, b) =>
                        {
                            float da = (transform.TransformPoint(a.centerWorldPos) - pp).sqrMagnitude;
                            float db = (transform.TransformPoint(b.centerWorldPos) - pp).sqrMagnitude;
                            float sa = Mathf.Sqrt(da), sb = Mathf.Sqrt(db);
                            float span = Mathf.Max(1f, GetChunkWorldSpan());
                            float ra = sa / span, rb = sb / span;
                            bool aCritical = da <= safetyDistance * safetyDistance;
                            bool bCritical = db <= safetyDistance * safetyDistance;
                            if (aCritical != bCritical) return aCritical ? -1 : 1;
                            bool aMissingMesh = !ChunkHasBuiltMesh(a);
                            bool bMissingMesh = !ChunkHasBuiltMesh(b);
                            if (aMissingMesh != bMissingMesh) return aMissingMesh ? -1 : 1;
                            int ratioCmp = ra.CompareTo(rb);
                            if (ratioCmp != 0) return ratioCmp;
                            return da.CompareTo(db);
                        });
                    }
                    int idx = 0;
                    while (idx < generateQueue.Count && activeGenerations < maxConcurrentGenerations)
                    {
                        var cd = generateQueue[idx];
                        if (cd == null || cd.isGenerating) { idx++; continue; }
                        float d  = Vector3.Distance(pp, transform.TransformPoint(cd.centerWorldPos));
                        int   tl = GetTargetLOD(cd, d, transform.TransformPoint(cd.centerWorldPos));
                        if (tl < 0 || tl == cd.currentLOD) { generateQueue.RemoveAt(idx); continue; }
                        generateQueue.RemoveAt(idx);
                        StartCoroutine(GenerateChunkMesh(cd, tl));
                    }
                }
                if (generateQueue.Count == 0 && activeGenerations == 0 && hasStartedLoading && !initialLoadComplete)
                {
                    initialLoadComplete = true;
                    if (loadingPanel      != null) loadingPanel.SetActive(false);
                    if (extraObjectToHide != null) extraObjectToHide.SetActive(false);
                }
                yield return null;
            }
            generateChunksCoroutine = null;
        }

        // ── Chunk mesh generation ─────────────────────────────────────────────

        IEnumerator GenerateChunkMesh(ChunkData chunk, int lod)
        {
            int  buildVersion = streamingVersion;
            bool sebStyle     = terrainStyle == TerrainStyle.SebInspired;
            chunk.isGenerating = true;
            activeGenerations++;
            try
            {
                RecycleTrees(chunk);

                int vpe   = GetVertsForLOD(lod);
                int vEdge = vpe + 1;
                int sampleLod  = Mathf.Max(0, lod - 1);
                int sampleVpe  = Mathf.Max(vpe, GetVertsForLOD(sampleLod));
                int sampleStep = sampleVpe % Mathf.Max(1, vpe) == 0 ? Mathf.Max(1, sampleVpe / Mathf.Max(1, vpe)) : 1;
                if (sampleStep == 1) sampleVpe = vpe;

                int samplePadding = sampleStep;
                int sampleVEdge   = sampleVpe + 1;
                int sampledWidth  = sampleVEdge + samplePadding * 2;

                var vertices  = new List<Vector3>(vEdge * vEdge);
                var uvs       = new List<Vector2>(vEdge * vEdge);
                var colors    = new List<Color>(vEdge * vEdge);
                var triangles = new List<int>(vpe * vpe * 6);

                float cs     = 2f / chunksPerFace;
                float startU = -1f + chunk.x * cs;
                float startV = -1f + chunk.y * cs;
                float cellSize       = Mathf.Max(1f, radius * cs / Mathf.Max(1, vpe));
                float sampleCellSize = Mathf.Max(1f, radius * cs / Mathf.Max(1, sampleVpe));

                var sampledHeightMap    = new float[sampledWidth, sampledWidth];
                var sampledSpherePoints = new Vector3[sampledWidth, sampledWidth];
                var paddedHeightMap     = new float[vEdge + 2, vEdge + 2];
                var paddedSpherePoints  = new Vector3[vEdge + 2, vEdge + 2];
                var heightMap    = new float[vEdge, vEdge];
                var spherePoints = new Vector3[vEdge, vEdge];
                var positions    = new Vector3[vEdge, vEdge];

                var buildTask = Task.Run(() =>
                {
                    for (int py = 0; py < sampledWidth; py++)
                    {
                        int sy = py - samplePadding;
                        for (int px = 0; px < sampledWidth; px++)
                        {
                            int   sx = px - samplePadding;
                            float u  = startU + (float)sx / sampleVpe * cs;
                            float v  = startV + (float)sy / sampleVpe * cs;
                            Vector3 sp = GetCubePoint(chunk.face, u, v).normalized;
                            sampledSpherePoints[px, py] = sp;
                            sampledHeightMap[px, py]    = EvaluateMacroHeightCached(sp);
                        }
                    }
                });
                while (!buildTask.IsCompleted) yield return null;
                if (buildTask.IsFaulted) { Debug.LogException(buildTask.Exception); yield break; }

                if (sebStyle)  RelaxSebHeightmap(sampledHeightMap, sampleCellSize);
                else
                {
                    SmoothHeightmap(sampledHeightMap, sampleCellSize, terrainSmoothing);
                    LimitSlope(sampledHeightMap, sampleCellSize);
                    ApplyErosion(sampledHeightMap, sampleCellSize);
                    ClampSpikeHeights(sampledHeightMap, sampleCellSize);
                }

                for (int py = 0; py < vEdge + 2; py++)
                {
                    int sampledPy = py * sampleStep;
                    for (int px = 0; px < vEdge + 2; px++)
                    {
                        int sampledPx = px * sampleStep;
                        paddedSpherePoints[px, py] = sampledSpherePoints[sampledPx, sampledPy];
                        paddedHeightMap[px, py]    = sampledHeightMap[sampledPx, sampledPy];
                    }
                }

                for (int y = 0; y < vEdge; y++)
                    for (int x = 0; x < vEdge; x++)
                    {
                        spherePoints[x, y] = paddedSpherePoints[x + 1, y + 1];
                        heightMap[x, y]    = ClampTerrainHeight(paddedHeightMap[x + 1, y + 1]);
                        positions[x, y]    = BuildStableSurfacePosition(spherePoints[x, y], heightMap[x, y]);
                    }

                for (int y = 0; y < vEdge; y++)
                    for (int x = 0; x < vEdge; x++)
                    {
                        vertices.Add(positions[x, y]);
                        uvs.Add(new Vector2((float)x / vpe, (float)y / vpe));
                    }

                float localMinH = float.MaxValue, localMaxH = float.MinValue;
                for (int y = 0; y < vEdge; y++) for (int x = 0; x < vEdge; x++) { float h = heightMap[x,y]; if (h < localMinH) localMinH = h; if (h > localMaxH) localMaxH = h; }
                if (localMinH < observedMinHeight) observedMinHeight = localMinH;
                if (localMaxH > observedMaxHeight) observedMaxHeight = localMaxH;
                if (Time.time - lastHeightLogTime > 2.5f) { lastHeightLogTime = Time.time; Debug.Log($"[PlanetGenerator] Height range: min={observedMinHeight:F1}, max={observedMaxHeight:F1}"); }

                for (int y = 0; y < vpe; y++)
                    for (int x = 0; x < vpe; x++)
                    {
                        int i0 = y * vEdge + x, i1 = i0+1, i2 = i0+vEdge, i3 = i2+1;
                        float diag03 = Mathf.Abs(heightMap[x, y] - heightMap[x+1, y+1]);
                        float diag12 = Mathf.Abs(heightMap[x+1, y] - heightMap[x, y+1]);
                        bool useAltDiag = sebStyle ? false : diag03 < diag12;
                        if (!sebStyle && Mathf.Abs(diag03 - diag12) < normalizedMaxHeight * 0.0006f)
                            useAltDiag = ((x + y) & 1) == 0;
                        if (useAltDiag) { triangles.Add(i0); triangles.Add(i1); triangles.Add(i3); triangles.Add(i0); triangles.Add(i3); triangles.Add(i2); }
                        else            { triangles.Add(i0); triangles.Add(i1); triangles.Add(i2); triangles.Add(i1); triangles.Add(i3); triangles.Add(i2); }
                    }

                var normals      = new List<Vector3>(vEdge * vEdge);
                var macroNormals = new List<Vector3>(vEdge * vEdge);
                for (int i = 0; i < vEdge * vEdge; i++) { normals.Add(Vector3.up); macroNormals.Add(Vector3.up); }

                bool highQualityNormals = lod <= 1;
                for (int y = 0; y < vEdge; y++)
                    for (int x = 0; x < vEdge; x++)
                    {
                        int     idx        = y * vEdge + x;
                        Vector3 gridNormal = ComputeGridNormal(paddedSpherePoints, paddedHeightMap, x + 1, y + 1);
                        Vector3 n = sebStyle
                            ? gridNormal
                            : (highQualityNormals
                                ? AverageNormals(ComputeSurfaceNormal(spherePoints[x, y], heightMap[x, y], cellSize), gridNormal)
                                : gridNormal);
                        macroNormals[idx] = n;
                        normals[idx]      = ApplyMicroNormal(spherePoints[x, y], n, heightMap[x, y], lod);
                    }

                if (Time.time - lastSlopeLogTime > 3f)
                {
                    int under30 = 0, under45 = 0, total = vEdge * vEdge;
                    for (int y = 0; y < vEdge; y++) for (int x = 0; x < vEdge; x++) { int idx = y*vEdge+x; float a = Vector3.Angle(macroNormals[idx], spherePoints[x,y]); if (a < 30f) under30++; if (a < 45f) under45++; }
                    lastSlopeLogTime = Time.time;
                    Debug.Log($"[PlanetGenerator] Slope: <30deg={(under30*100f/total):F1}% <45deg={(under45*100f/total):F1}%");
                }

                for (int y = 0; y < vEdge; y++)
                    for (int x = 0; x < vEdge; x++)
                    {
                        int   idx      = y * vEdge + x;
                        float h        = heightMap[x, y];
                        float sl       = 1f - Mathf.Clamp01(Vector3.Dot(macroNormals[idx], spherePoints[x, y]));
                        var   bc       = GetBiomeColor(spherePoints[x, y], h);
                        float slope01  = Mathf.Clamp01(sl);
                        float alt01    = Mathf.InverseLerp(seaLevelHeight + beachWidthHeight, normalizedMaxHeight, h);
                        float slopeRockMask = Mathf.SmoothStep(0.10f, 0.30f, slope01);
                        float mountainMask  = Mathf.SmoothStep(0.18f, 0.82f, alt01);
                        float peakCap       = 1f - Mathf.SmoothStep(0.84f, 0.98f, alt01) * (1f - Mathf.SmoothStep(0.24f, 0.48f, slope01));
                        float rockBlend     = Mathf.Clamp01(slopeRockMask * mountainMask * peakCap * (0.35f + Mathf.Clamp01(slopeRockBlend) * 2.4f));
                        Color sideRockColor = Color.Lerp(rockColor, mesaRockColor, Mathf.SmoothStep(0.58f, 0.92f, alt01) * 0.16f);
                        var   wsl           = Color.Lerp(bc, sideRockColor, rockBlend);
                        float ao            = CalculateAO(heightMap, x, y, vEdge, h);
                        colors.Add(wsl * (1f - ao * ambientOcclusion));
                    }

                AddSkirts(lod, vpe, vEdge, vertices, triangles, normals, colors, uvs);

                if (chunk.mesh == null) chunk.mesh = new Mesh();
                else chunk.mesh.Clear();
                chunk.mesh.name = $"Chunk_{chunk.face}_{chunk.x}_{chunk.y}_LOD{lod}";
                if (vertices.Count >= 65535) chunk.mesh.indexFormat = IndexFormat.UInt32;
                chunk.mesh.SetVertices(vertices); chunk.mesh.SetUVs(0, uvs); chunk.mesh.SetColors(colors);
                chunk.mesh.SetTriangles(triangles, 0); chunk.mesh.SetNormals(normals);
                chunk.mesh.RecalculateBounds();

                var chunkObject = EnsureChunkObject(chunk);
                if (chunkObject == null) yield break;

                // Use explicit Unity-null-aware checks instead of the C# ?? operator.
                // ?? uses ReferenceEquals internally and does NOT call Unity's overloaded ==,
                // so it can miss "fake null" destroyed components and leave mf/mr invalid.
                var mf = chunkObject.GetComponent<MeshFilter>();
                if (mf == null) mf = chunkObject.AddComponent<MeshFilter>();
                var mr = chunkObject.GetComponent<MeshRenderer>();
                if (mr == null) mr = chunkObject.AddComponent<MeshRenderer>();
                if (mf == null || mr == null) yield break;

                mf.sharedMesh     = chunk.mesh;
                mr.sharedMaterial = planetMaterial;

                if (useColliders)
                {
                    var mc = chunkObject.GetComponent<MeshCollider>();
                    if (mc == null) mc = chunkObject.AddComponent<MeshCollider>();
                    if (mc == null) yield break;
                    mc.sharedMesh = chunk.mesh;
                    mc.convex     = false;
                    mc.enabled    = player != null &&
                        Vector3.Distance(player.position, transform.TransformPoint(chunk.centerWorldPos)) < GetColliderSafetyDistance();
                }

                bool chunkStillVisible   = player == null || !IsChunkBacksideCulled(chunk, player.position);
                bool streamingStillValid = streamingActive && buildVersion == streamingVersion && chunkStillVisible;
                chunkObject.SetActive(streamingStillValid);

                if (streamingStillValid && lod == 0 && TreesEnabled() && treePrefab != null)
                {
                    Random.InitState(seed + (int)chunk.face * 10000 + chunk.x * 100 + chunk.y);
                    yield return StartCoroutine(SpawnTreesForChunk(chunk, heightMap, spherePoints, positions, normals.ToArray(), vEdge));
                }

                chunk.currentLOD = streamingStillValid ? lod : -1;
                if (!streamingStillValid) ReleaseChunkResources(chunk);

                foreach (var neighbor in EnumerateNeighbors(chunk))
                {
                    if (neighbor == null || neighbor.currentLOD < 0 || neighbor.isGenerating) continue;
                    if (Mathf.Abs(neighbor.currentLOD - chunk.currentLOD) > 1 && !generateQueue.Contains(neighbor))
                        generateQueue.Add(neighbor);
                }
            }
            finally
            {
                chunk.isGenerating  = false;
                activeGenerations   = Mathf.Max(0, activeGenerations - 1);
            }
            yield return null;
        }

        // ── Skirts ────────────────────────────────────────────────────────────

        void AddSkirts(int lod, int vpe, int vEdge,
            List<Vector3> vertices, List<int> triangles, List<Vector3> normals,
            List<Color> colors, List<Vector2> uvs)
        {
            return;
        }

        // ── Proxy mesh (used by LOD controller) ───────────────────────────────

        public Mesh BuildProxyMesh(int faceResolution, string meshName = null)
        {
            EnsureRuntimeResources();
            bool sebStyle    = terrainStyle == TerrainStyle.SebInspired;
            int resolution   = Mathf.Clamp(faceResolution, 2, 48);
            int vertsPerEdge = resolution + 1;
            float worldCellSize = Mathf.Max(1f, radius * (2f / resolution) * 0.68f);

            var mesh = new Mesh { name = string.IsNullOrWhiteSpace(meshName) ? $"PlanetProxy_{resolution}" : meshName };
            int estimatedVertexCount = 6 * vertsPerEdge * vertsPerEdge;
            if (estimatedVertexCount >= 65535) mesh.indexFormat = IndexFormat.UInt32;

            var vertices  = new List<Vector3>(estimatedVertexCount);
            var normals   = new List<Vector3>(estimatedVertexCount);
            var colors    = new List<Color>(estimatedVertexCount);
            var uvs       = new List<Vector2>(estimatedVertexCount);
            var triangles = new List<int>(6 * resolution * resolution * 6);

            for (int faceIndex = 0; faceIndex < 6; faceIndex++)
            {
                Face face      = (Face)faceIndex;
                int  baseIndex = vertices.Count;

                var paddedHeightMap = new float[vertsPerEdge + 2, vertsPerEdge + 2];
                var paddedSpherePoints = new Vector3[vertsPerEdge + 2, vertsPerEdge + 2];

                for (int py = 0; py < vertsPerEdge + 2; py++)
                {
                    int sy = py - 1;
                    float v = -1f + (float)sy / resolution * 2f;
                    for (int px = 0; px < vertsPerEdge + 2; px++)
                    {
                        int sx = px - 1;
                        float u = -1f + (float)sx / resolution * 2f;
                        Vector3 dir = GetCubePoint(face, u, v).normalized;
                        paddedSpherePoints[px, py] = dir;
                        paddedHeightMap[px, py] = EvaluateMacroHeightCached(dir);
                    }
                }

                if (sebStyle)
                {
                    RelaxSebHeightmap(paddedHeightMap, worldCellSize);
                }
                else
                {
                    SmoothHeightmap(paddedHeightMap, worldCellSize, terrainSmoothing);
                    LimitSlope(paddedHeightMap, worldCellSize);
                    ApplyErosion(paddedHeightMap, worldCellSize);
                    ClampSpikeHeights(paddedHeightMap, worldCellSize);
                }

                for (int y = 0; y < vertsPerEdge; y++)
                {
                    for (int x = 0; x < vertsPerEdge; x++)
                    {
                        Vector3 dir = paddedSpherePoints[x + 1, y + 1];
                        float height = ClampTerrainHeight(paddedHeightMap[x + 1, y + 1]);
                        float localScale = 1f + ClampTerrainHeight(height) / Mathf.Max(1f, radius);
                        vertices.Add(dir * localScale);
                        normals.Add(ComputeGridNormal(paddedSpherePoints, paddedHeightMap, x + 1, y + 1));
                        colors.Add(GetBiomeColor(dir, height));
                        uvs.Add(new Vector2((float)x / resolution, (float)y / resolution));
                    }
                }
                for (int y = 0; y < resolution; y++)
                    for (int x = 0; x < resolution; x++)
                    {
                        int i0 = baseIndex + y * vertsPerEdge + x; int i1 = i0+1; int i2 = i0+vertsPerEdge; int i3 = i2+1;
                        triangles.Add(i0); triangles.Add(i1); triangles.Add(i2);
                        triangles.Add(i1); triangles.Add(i3); triangles.Add(i2);
                    }
            }
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetColors(colors);
            mesh.SetUVs(0, uvs); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
