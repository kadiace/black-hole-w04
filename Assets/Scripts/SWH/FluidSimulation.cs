using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class FluidSimulation : MonoBehaviour
{
    private enum SolverMode { Sph, PicFlip }

    [Serializable]
    private struct SpawnPoint
    {
        public Transform point;
        [Min(0)] public int particleCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TileEntry
    {
        public int x;
        public int y;
        public int z;
        public int w;

        public TileEntry(int x, int y, int z, int w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }
    }

    private struct ObstaclePose
    {
        public Bounds bounds;
        public Matrix4x4 matrix;

        public ObstaclePose(Collider collider)
        {
            bounds = collider.bounds;
            matrix = collider.transform.localToWorldMatrix;
        }

        public bool Matches(ObstaclePose other)
        {
            return matrix.Equals(other.matrix) && bounds.center == other.bounds.center && bounds.size == other.bounds.size;
        }
    }

    private const int CellsPerTile = 8;
    private const int CellCountPerTile = 512;
    private const int FaceCountPerTile = 576;
    private const int Float4Stride = sizeof(float) * 4;
    private const int FaceStride = sizeof(float) * 2;
    private const float ParticleMass = 1f;

    // 입자 설정
    [SerializeField] private Vector3 spawnSize = new Vector3(2f, 1f, 2f);
    [SerializeField] private SpawnPoint[] spawnPoints;

    // 렌더링 설정
    [SerializeField] private MeshFilter sphereMeshSource;
    [SerializeField] private Material particleMaterial;

    // 물리 설정
    [SerializeField] private ComputeShader integrationShader;
    [SerializeField] private ComputeShader spatialGridShader;
    [SerializeField] private ComputeShader sparseGridShader;
    [SerializeField] private ComputeShader macGridShader;
    [SerializeField] private ComputeShader portalShader;
    [SerializeField] private SolverMode solverMode = SolverMode.PicFlip;
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private Vector3 externalAcceleration = Vector3.zero;
    [SerializeField] private Vector3 initialVelocity = Vector3.zero;
    [SerializeField] private float collisionRadius = 0.08f;
    [SerializeField] private float wallBounce = 0.5f;
    [SerializeField] private float cellSize = 0.4f;
    [SerializeField] private float restSpacing = 0.18f;
    [SerializeField] private float separationStrength = 1f;
    [SerializeField, Range(0, 8)] private int separationIterations = 3;
    [SerializeField] private float restDensity = 400f;
    [SerializeField] private float pressureStiffness = 1f;
    [SerializeField] private float viscosityStrength = 5f;
    [SerializeField, Range(0f, 1f)] private float flipRatio = 0.95f;
    [SerializeField, Range(2, 128)] private int pressureIterations = 40;
    [SerializeField] private float volumeCorrectionRate = 0.1f;
    [SerializeField] private float velocityDamping = 0.05f;
    [SerializeField] private float maxSpeed = 20f;
    [SerializeField] private bool despawnBelowY;
    [SerializeField] private float despawnY = -10f;

    // 타일 설정
    [SerializeField, Range(1, 3)] private int tilePadding = 1;
    [SerializeField, Min(-1)] private int maxActiveTiles = 512;
    [SerializeField, Min(0.02f)] private float tileRefreshInterval = 0.05f;

    // 장애물 설정
    [SerializeField] private bool autoCollectObstacles = true;
    [SerializeField] private LayerMask obstacleLayers = ~0;
    [SerializeField] private Collider[] staticObstacles;
    [SerializeField, Min(0f)] private float sandObstacleRefreshInterval = 0.1f;
    [SerializeField, Min(1)] private int maxSandTileRebuildsPerStep = 16;

    // 디버그 설정
    [SerializeField] private bool drawGridBounds = true;
    [SerializeField] private bool drawFluidCells;

    public event Action<ComputeBuffer, int> ParticlePositionsUpdated;
    public event Action SimulationStopped;

    private int GridCellCount => activeTiles.Count * CellCountPerTile;
    private int FaceCount => activeTiles.Count * FaceCountPerTile;
    private float TileWorldSize => CellsPerTile * cellSize;

    // 입자 버퍼
    private ComputeBuffer positionBuffer;
    private ComputeBuffer velocityBuffer;
    private ComputeBuffer particleCellBuffer;
    private ComputeBuffer particleNextBuffer;
    private ComputeBuffer neighborCountBuffer;
    private ComputeBuffer densityBuffer;
    private ComputeBuffer pressureBuffer;
    private ComputeBuffer viscosityAccelerationBuffer;
    private ComputeBuffer positionCorrectionBuffer;
    private ComputeBuffer particleTilesBuffer;
    private ComputeBuffer missingTileCountBuffer;

    // 격자 버퍼
    private ComputeBuffer tileHashBuffer;
    private ComputeBuffer tileCoordinatesBuffer;
    private ComputeBuffer cellCountBuffer;
    private ComputeBuffer cellHeadBuffer;
    private ComputeBuffer obstacleSdfBuffer;
    private ComputeBuffer cellStateBuffer;
    private ComputeBuffer divergenceBuffer;
    private ComputeBuffer pressureABuffer;
    private ComputeBuffer pressureBBuffer;
    private ComputeBuffer uFaceBuffer;
    private ComputeBuffer vFaceBuffer;
    private ComputeBuffer wFaceBuffer;
    private ComputeBuffer oldUFaceBuffer;
    private ComputeBuffer oldVFaceBuffer;
    private ComputeBuffer oldWFaceBuffer;

    // 실행 상태
    private MaterialPropertyBlock particleProperties;
    private FluidPortalBridge portalBridge;
    private readonly List<Vector3Int> activeTiles = new List<Vector3Int>();
    private readonly HashSet<Vector3Int> activeTileSet = new HashSet<Vector3Int>();
    private readonly Dictionary<Vector3Int, long> pendingPortalTiles = new Dictionary<Vector3Int, long>();
    private AsyncGPUReadbackRequest tileReadback;
    private bool tileReadbackPending;
    private float nextTileReadbackTime;
    private float allocatedCellSize;
    private Bounds renderBounds;
    private int[] debugCellStates;
    private int activeParticleCount;
    private int tileHashCapacity;
    private long simulationStep;
    private long tileReadbackStep;

    // 이동 장애물
    private readonly Dictionary<Collider, ObstaclePose> movingObstaclePoses = new Dictionary<Collider, ObstaclePose>();
    private readonly HashSet<Collider> observedMovingObstacles = new HashSet<Collider>();
    private readonly List<Collider> removedMovingObstacles = new List<Collider>();
    private readonly HashSet<int> dirtyObstacleTiles = new HashSet<int>();
    private float nextSandObstacleRefreshTime;
    private readonly HashSet<Vector3Int> pendingSandTiles = new HashSet<Vector3Int>();
    private readonly List<Vector3Int> sandTileBatch = new List<Vector3Int>();
    private static readonly Comparer<Vector3Int> TileComparer = Comparer<Vector3Int>.Create(CompareTiles);

    // 타일별 장애물 SDF 캐시
    private readonly Dictionary<Vector3Int, float[]> tileSdfCache = new Dictionary<Vector3Int, float[]>();
    private readonly List<Vector3Int> staleSdfTiles = new List<Vector3Int>();
    private int tileSdfCacheSignature;

    // 타일 커널
    private int clearMissingTilesKernel;
    private int collectParticleTilesKernel;

    // 공간 격자 커널
    private int clearCellsKernel;
    private int assignCellsKernel;
    private int buildCellListsKernel;
    private int countNeighborsKernel;
    private int computeViscosityKernel;
    private int applyPressureKernel;
    private int computeSeparationKernel;

    // 적분 커널
    private int applyForcesKernel;
    private int integrateKernel;
    private int applyPositionCorrectionsKernel;

    // MAC 격자 커널
    private int clearFacesKernel;
    private int particleToUKernel;
    private int particleToVKernel;
    private int particleToWKernel;
    private int normalizeFacesKernel;
    private int classifyCellsKernel;
    private int applyBoundariesKernel;
    private int saveFacesKernel;
    private int computeDivergenceKernel;
    private int jacobiAKernel;
    private int jacobiBKernel;
    private int projectFacesKernel;
    private int gridToParticlesKernel;

    // 포털 커널
    private int applySuctionKernel;
    private int transportKernel;

    // 초기화
    private void OnEnable()
    {
        cellSize = Mathf.Max(0.05f, cellSize);
        collisionRadius = Mathf.Clamp(collisionRadius, 0.001f, cellSize * 0.49f);
        restSpacing = Mathf.Clamp(restSpacing, collisionRadius * 2f, cellSize);
        wallBounce = Mathf.Clamp01(wallBounce);
        maxSpeed = Mathf.Max(0.1f, maxSpeed);
        pressureIterations = Mathf.Clamp(pressureIterations, 2, 128);
        separationIterations = Mathf.Clamp(separationIterations, 0, 8);
        tilePadding = Mathf.Clamp(tilePadding, 1, 3);
        maxActiveTiles = maxActiveTiles == -1 ? -1 : Mathf.Max(1, maxActiveTiles);
        tileRefreshInterval = Mathf.Max(0.02f, tileRefreshInterval);
        allocatedCellSize = cellSize;
        portalBridge = GetComponent<FluidPortalBridge>();

        if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
        {
            Debug.LogError("유체 계산에 필요한 GPU 기능을 지원하지 않습니다.", this);
            enabled = false;
            return;
        }

        if (integrationShader == null || spatialGridShader == null || sparseGridShader == null || macGridShader == null || (portalBridge != null && portalShader == null))
        {
            Debug.LogError("유체 계산에 필요한 Compute Shader 연결을 확인하세요.", this);
            enabled = false;
            return;
        }

        if (!CreateInitialParticles(out Vector4[] positions, out Vector4[] velocities))
        {
            Debug.LogError("Spawn Points 구성을 확인하세요.", this);
            enabled = false;
            return;
        }

        FindKernels();
        CreateParticleBuffers(positions, velocities);

        particleProperties = new MaterialPropertyBlock();
        particleProperties.SetBuffer("_Positions", positionBuffer);

        var occupied = new HashSet<Vector3Int>();
        foreach (Vector4 position in positions)
        {
            occupied.Add(WorldToTile(new Vector3(position.x, position.y, position.z)));
        }

        if (!SetActiveTiles(ExpandTiles(occupied)))
        {
            enabled = false;
            return;
        }

        nextTileReadbackTime = Time.time;
    }

    private void FindKernels()
    {
        clearMissingTilesKernel = sparseGridShader.FindKernel("ClearMissingTiles");
        collectParticleTilesKernel = sparseGridShader.FindKernel("CollectParticleTiles");

        clearCellsKernel = spatialGridShader.FindKernel("ClearCells");
        assignCellsKernel = spatialGridShader.FindKernel("AssignCells");
        buildCellListsKernel = spatialGridShader.FindKernel("BuildCellLists");
        countNeighborsKernel = spatialGridShader.FindKernel("CountNeighbors");
        computeViscosityKernel = spatialGridShader.FindKernel("ComputeViscosity");
        applyPressureKernel = spatialGridShader.FindKernel("ApplyPressure");
        computeSeparationKernel = spatialGridShader.FindKernel("ComputeSeparation");

        applyForcesKernel = integrationShader.FindKernel("ApplyForces");
        integrateKernel = integrationShader.FindKernel("Integrate");
        applyPositionCorrectionsKernel = integrationShader.FindKernel("ApplyPositionCorrections");

        clearFacesKernel = macGridShader.FindKernel("ClearFaces");
        particleToUKernel = macGridShader.FindKernel("ParticleToU");
        particleToVKernel = macGridShader.FindKernel("ParticleToV");
        particleToWKernel = macGridShader.FindKernel("ParticleToW");
        normalizeFacesKernel = macGridShader.FindKernel("NormalizeFaces");
        classifyCellsKernel = macGridShader.FindKernel("ClassifyCells");
        applyBoundariesKernel = macGridShader.FindKernel("ApplyBoundaries");
        saveFacesKernel = macGridShader.FindKernel("SaveFaces");
        computeDivergenceKernel = macGridShader.FindKernel("ComputeDivergence");
        jacobiAKernel = macGridShader.FindKernel("JacobiA");
        jacobiBKernel = macGridShader.FindKernel("JacobiB");
        projectFacesKernel = macGridShader.FindKernel("ProjectFaces");
        gridToParticlesKernel = macGridShader.FindKernel("GridToParticles");

        if (portalShader != null)
        {
            applySuctionKernel = portalShader.FindKernel("ApplySuction");
            transportKernel = portalShader.FindKernel("Transport");
        }
    }

    // 생성 지점
    private bool CreateInitialParticles(out Vector4[] positions, out Vector4[] velocities)
    {
        positions = null;
        velocities = null;

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            return false;
        }

        long total = 0;
        foreach (SpawnPoint spawn in spawnPoints)
        {
            if (spawn.particleCount <= 0)
            {
                continue;
            }

            if (spawn.point == null)
            {
                return false;
            }

            total += spawn.particleCount;
            if (total > int.MaxValue)
            {
                return false;
            }
        }

        if (total == 0)
        {
            return false;
        }

        activeParticleCount = (int)total;
        positions = new Vector4[activeParticleCount];
        velocities = new Vector4[activeParticleCount];

        var random = new System.Random(1234);
        int cursor = 0;

        foreach (SpawnPoint spawn in spawnPoints)
        {
            if (spawn.particleCount <= 0)
            {
                continue;
            }

            FillSpawn(spawn, positions, velocities, ref cursor, random);
        }

        return true;
    }

    private void FillSpawn(SpawnPoint spawn, Vector4[] positions, Vector4[] velocities, ref int cursor, System.Random random)
    {
        Vector3 size = new Vector3(Mathf.Max(Mathf.Abs(spawnSize.x), 0.001f), Mathf.Max(Mathf.Abs(spawnSize.y), 0.001f), Mathf.Max(Mathf.Abs(spawnSize.z), 0.001f));
        float densityRoot = Mathf.Pow(spawn.particleCount / (size.x * size.y * size.z), 1f / 3f);
        int countX = Mathf.Max(1, Mathf.CeilToInt(size.x * densityRoot));
        int countY = Mathf.Max(1, Mathf.CeilToInt(size.y * densityRoot));
        int countZ = Mathf.Max(1, Mathf.CeilToInt((float)spawn.particleCount / (countX * countY)));

        for (int i = 0; i < spawn.particleCount; i++)
        {
            int x = i % countX;
            int y = (i / countX) % countY;
            int z = i / (countX * countY);

            float jitterX = ((float)random.NextDouble() - 0.5f) * 0.2f;
            float jitterY = ((float)random.NextDouble() - 0.5f) * 0.2f;
            float jitterZ = ((float)random.NextDouble() - 0.5f) * 0.2f;

            Vector3 normalized = new Vector3((x + 0.5f + jitterX) / countX - 0.5f, (y + 0.5f + jitterY) / countY - 0.5f, (z + 0.5f + jitterZ) / countZ - 0.5f);
            Vector3 world = spawn.point.TransformPoint(Vector3.Scale(normalized, spawnSize));

            positions[cursor] = new Vector4(world.x, world.y, world.z, 1f);
            velocities[cursor] = new Vector4(initialVelocity.x, initialVelocity.y, initialVelocity.z, 0f);
            cursor++;
        }
    }

    // 입자 버퍼
    private void CreateParticleBuffers(Vector4[] positions, Vector4[] velocities)
    {
        positionBuffer = new ComputeBuffer(activeParticleCount, Float4Stride);
        velocityBuffer = new ComputeBuffer(activeParticleCount, Float4Stride);
        particleCellBuffer = new ComputeBuffer(activeParticleCount, sizeof(int));
        particleNextBuffer = new ComputeBuffer(activeParticleCount, sizeof(int));
        neighborCountBuffer = new ComputeBuffer(activeParticleCount, sizeof(int));
        densityBuffer = new ComputeBuffer(activeParticleCount, sizeof(float));
        pressureBuffer = new ComputeBuffer(activeParticleCount, sizeof(float));
        viscosityAccelerationBuffer = new ComputeBuffer(activeParticleCount, Float4Stride);
        positionCorrectionBuffer = new ComputeBuffer(activeParticleCount, Float4Stride);
        particleTilesBuffer = new ComputeBuffer(activeParticleCount, Float4Stride);
        missingTileCountBuffer = new ComputeBuffer(1, sizeof(int));

        positionBuffer.SetData(positions);
        velocityBuffer.SetData(velocities);
    }

    // 타일 좌표
    private Vector3Int WorldToTile(Vector3 position)
    {
        float size = TileWorldSize;
        return new Vector3Int(Mathf.FloorToInt(position.x / size), Mathf.FloorToInt(position.y / size), Mathf.FloorToInt(position.z / size));
    }

    private HashSet<Vector3Int> ExpandTiles(HashSet<Vector3Int> occupied)
    {
        var result = new HashSet<Vector3Int>();

        foreach (Vector3Int tile in occupied)
        {
            for (int z = -tilePadding; z <= tilePadding; z++)
            {
                for (int y = -tilePadding; y <= tilePadding; y++)
                {
                    for (int x = -tilePadding; x <= tilePadding; x++)
                    {
                        result.Add(tile + new Vector3Int(x, y, z));
                    }
                }
            }
        }

        return result;
    }

    // 출구 타일
    private bool EnsurePortalExitTiles(Vector3 exitCenter)
    {
        var outlet = new HashSet<Vector3Int> { WorldToTile(exitCenter) };
        HashSet<Vector3Int> needed = ExpandTiles(outlet);

        if (activeTileSet.IsSupersetOf(needed))
        {
            return true;
        }

        var required = new HashSet<Vector3Int>(activeTileSet);
        required.UnionWith(needed);
        return SetActiveTiles(required);
    }

    private static int CompareTiles(Vector3Int a, Vector3Int b)
    {
        int result = a.x.CompareTo(b.x);
        if (result != 0)
        {
            return result;
        }

        result = a.y.CompareTo(b.y);
        return result != 0 ? result : a.z.CompareTo(b.z);
    }

    private static uint HashTile(Vector3Int tile)
    {
        unchecked
        {
            uint hash = ((uint)tile.x * 73856093u) ^ ((uint)tile.y * 19349663u) ^ ((uint)tile.z * 83492791u);
            return hash ^ (hash >> 16);
        }
    }

    // 활성 타일
    private bool SetActiveTiles(HashSet<Vector3Int> required)
    {
        if (required.Count == 0 || (maxActiveTiles != -1 && required.Count > maxActiveTiles))
        {
            Debug.LogError($"활성 타일 수 {required.Count}, 최대 허용 {maxActiveTiles}", this);
            return false;
        }

        if (activeTileSet.SetEquals(required))
        {
            return true;
        }

        bool firstBuild = activeTiles.Count == 0;
        ReleaseTileBuffers();

        activeTiles.Clear();
        activeTiles.AddRange(required);
        activeTiles.Sort(CompareTiles);

        activeTileSet.Clear();
        foreach (Vector3Int tile in activeTiles)
        {
            activeTileSet.Add(tile);
        }

        CreateTileBuffers();
        BindBuffers();
        BakeObstacleField(firstBuild, !firstBuild);
        UpdateRenderBounds();
        debugCellStates = null;
        return true;
    }

    private void CreateTileBuffers()
    {
        int cellCount = GridCellCount;
        int faceCount = FaceCount;

        tileHashCapacity = 1;
        while (tileHashCapacity < activeTiles.Count * 2)
        {
            tileHashCapacity *= 2;
        }

        var coordinates = new TileEntry[activeTiles.Count];
        var hashEntries = new TileEntry[tileHashCapacity];

        for (int i = 0; i < hashEntries.Length; i++)
        {
            hashEntries[i].w = -1;
        }

        for (int i = 0; i < activeTiles.Count; i++)
        {
            Vector3Int tile = activeTiles[i];
            coordinates[i] = new TileEntry(tile.x, tile.y, tile.z, i);

            int slot = (int)(HashTile(tile) & (uint)(tileHashCapacity - 1));
            while (hashEntries[slot].w >= 0)
            {
                slot = (slot + 1) & (tileHashCapacity - 1);
            }

            hashEntries[slot] = coordinates[i];
        }

        tileHashBuffer = new ComputeBuffer(tileHashCapacity, Float4Stride);
        tileCoordinatesBuffer = new ComputeBuffer(activeTiles.Count, Float4Stride);
        cellCountBuffer = new ComputeBuffer(cellCount, sizeof(int));
        cellHeadBuffer = new ComputeBuffer(cellCount, sizeof(int));
        obstacleSdfBuffer = new ComputeBuffer(cellCount, sizeof(float));
        cellStateBuffer = new ComputeBuffer(cellCount, sizeof(int));
        divergenceBuffer = new ComputeBuffer(cellCount, sizeof(float));
        pressureABuffer = new ComputeBuffer(cellCount, sizeof(float));
        pressureBBuffer = new ComputeBuffer(cellCount, sizeof(float));
        uFaceBuffer = new ComputeBuffer(faceCount, FaceStride);
        vFaceBuffer = new ComputeBuffer(faceCount, FaceStride);
        wFaceBuffer = new ComputeBuffer(faceCount, FaceStride);
        oldUFaceBuffer = new ComputeBuffer(faceCount, FaceStride);
        oldVFaceBuffer = new ComputeBuffer(faceCount, FaceStride);
        oldWFaceBuffer = new ComputeBuffer(faceCount, FaceStride);

        tileHashBuffer.SetData(hashEntries);
        tileCoordinatesBuffer.SetData(coordinates);
    }

    private void UpdateRenderBounds()
    {
        Vector3 minimum = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 maximum = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        foreach (Vector3Int tile in activeTiles)
        {
            Vector3 tileMin = new Vector3(tile.x, tile.y, tile.z) * TileWorldSize;
            minimum = Vector3.Min(minimum, tileMin);
            maximum = Vector3.Max(maximum, tileMin + Vector3.one * TileWorldSize);
        }

        renderBounds = new Bounds();
        renderBounds.SetMinMax(minimum, maximum);
        renderBounds.Expand(TileWorldSize * 2f);
    }

    // 셰이더 연결
    private void BindBuffers()
    {
        sparseGridShader.SetInt("_ParticleCount", activeParticleCount);
        sparseGridShader.SetInt("_TileHashCapacity", tileHashCapacity);
        sparseGridShader.SetInt("_CellsPerTile", CellsPerTile);
        sparseGridShader.SetFloat("_CellSize", cellSize);
        sparseGridShader.SetBuffer(clearMissingTilesKernel, "_MissingTileCount", missingTileCountBuffer);
        sparseGridShader.SetBuffer(collectParticleTilesKernel, "_Positions", positionBuffer);
        sparseGridShader.SetBuffer(collectParticleTilesKernel, "_TileHash", tileHashBuffer);
        sparseGridShader.SetBuffer(collectParticleTilesKernel, "_ParticleTiles", particleTilesBuffer);
        sparseGridShader.SetBuffer(collectParticleTilesKernel, "_MissingTileCount", missingTileCountBuffer);

        spatialGridShader.SetInt("_ParticleCount", activeParticleCount);
        spatialGridShader.SetInt("_GridCellCount", GridCellCount);
        spatialGridShader.SetInt("_TileHashCapacity", tileHashCapacity);
        spatialGridShader.SetFloat("_CellSize", cellSize);
        spatialGridShader.SetFloat("_NeighborRadius", cellSize);
        spatialGridShader.SetFloat("_ParticleMass", ParticleMass);
        spatialGridShader.SetBuffer(clearCellsKernel, "_CellCounts", cellCountBuffer);
        spatialGridShader.SetBuffer(clearCellsKernel, "_CellHeads", cellHeadBuffer);
        spatialGridShader.SetBuffer(assignCellsKernel, "_Positions", positionBuffer);
        spatialGridShader.SetBuffer(assignCellsKernel, "_TileHash", tileHashBuffer);
        spatialGridShader.SetBuffer(assignCellsKernel, "_ParticleCell", particleCellBuffer);
        spatialGridShader.SetBuffer(buildCellListsKernel, "_ParticleCell", particleCellBuffer);
        spatialGridShader.SetBuffer(buildCellListsKernel, "_CellCounts", cellCountBuffer);
        spatialGridShader.SetBuffer(buildCellListsKernel, "_CellHeads", cellHeadBuffer);
        spatialGridShader.SetBuffer(buildCellListsKernel, "_ParticleNext", particleNextBuffer);

        BindSpatialSearch(countNeighborsKernel);
        spatialGridShader.SetBuffer(countNeighborsKernel, "_ParticleNeighborCounts", neighborCountBuffer);
        spatialGridShader.SetBuffer(countNeighborsKernel, "_ParticleDensities", densityBuffer);
        spatialGridShader.SetBuffer(countNeighborsKernel, "_ParticlePressures", pressureBuffer);

        BindSpatialSearch(computeViscosityKernel);
        spatialGridShader.SetBuffer(computeViscosityKernel, "_ParticleDensities", densityBuffer);
        spatialGridShader.SetBuffer(computeViscosityKernel, "_Velocities", velocityBuffer);
        spatialGridShader.SetBuffer(computeViscosityKernel, "_ViscosityAccelerations", viscosityAccelerationBuffer);

        BindSpatialSearch(applyPressureKernel);
        spatialGridShader.SetBuffer(applyPressureKernel, "_ParticleDensities", densityBuffer);
        spatialGridShader.SetBuffer(applyPressureKernel, "_ParticlePressures", pressureBuffer);
        spatialGridShader.SetBuffer(applyPressureKernel, "_Velocities", velocityBuffer);
        spatialGridShader.SetBuffer(applyPressureKernel, "_ViscosityAccelerations", viscosityAccelerationBuffer);

        BindSpatialSearch(computeSeparationKernel);
        spatialGridShader.SetBuffer(computeSeparationKernel, "_PositionCorrections", positionCorrectionBuffer);

        integrationShader.SetInt("_ParticleCount", activeParticleCount);
        integrationShader.SetInt("_TileHashCapacity", tileHashCapacity);
        integrationShader.SetFloat("_CellSize", cellSize);
        integrationShader.SetBuffer(applyForcesKernel, "_Velocities", velocityBuffer);
        BindIntegrationCollision(integrateKernel);
        BindIntegrationCollision(applyPositionCorrectionsKernel);
        integrationShader.SetBuffer(applyPositionCorrectionsKernel, "_PositionCorrections", positionCorrectionBuffer);

        macGridShader.SetInt("_ParticleCount", activeParticleCount);
        macGridShader.SetInt("_GridCellCount", GridCellCount);
        macGridShader.SetInt("_FaceCount", FaceCount);
        macGridShader.SetInt("_TileHashCapacity", tileHashCapacity);
        macGridShader.SetFloat("_CellSize", cellSize);

        BindMacFaces(clearFacesKernel);
        BindMacParticleToFace(particleToUKernel, "_UFaceData", uFaceBuffer);
        BindMacParticleToFace(particleToVKernel, "_VFaceData", vFaceBuffer);
        BindMacParticleToFace(particleToWKernel, "_WFaceData", wFaceBuffer);
        BindMacFaces(normalizeFacesKernel);

        macGridShader.SetBuffer(classifyCellsKernel, "_CellCounts", cellCountBuffer);
        macGridShader.SetBuffer(classifyCellsKernel, "_ObstacleSdf", obstacleSdfBuffer);
        macGridShader.SetBuffer(classifyCellsKernel, "_CellStates", cellStateBuffer);
        macGridShader.SetBuffer(classifyCellsKernel, "_Divergence", divergenceBuffer);
        macGridShader.SetBuffer(classifyCellsKernel, "_PressureA", pressureABuffer);
        macGridShader.SetBuffer(classifyCellsKernel, "_PressureB", pressureBBuffer);

        BindMacTopology(applyBoundariesKernel);
        BindMacFaces(applyBoundariesKernel);
        macGridShader.SetBuffer(applyBoundariesKernel, "_CellStates", cellStateBuffer);

        BindMacFaces(saveFacesKernel);
        BindMacOldFaces(saveFacesKernel);

        BindMacFaces(computeDivergenceKernel);
        macGridShader.SetBuffer(computeDivergenceKernel, "_CellStates", cellStateBuffer);
        macGridShader.SetBuffer(computeDivergenceKernel, "_CellCounts", cellCountBuffer);
        macGridShader.SetBuffer(computeDivergenceKernel, "_Divergence", divergenceBuffer);

        BindMacPressure(jacobiAKernel);
        BindMacPressure(jacobiBKernel);
        BindMacTopology(jacobiAKernel);
        BindMacTopology(jacobiBKernel);
        macGridShader.SetBuffer(jacobiAKernel, "_Divergence", divergenceBuffer);
        macGridShader.SetBuffer(jacobiBKernel, "_Divergence", divergenceBuffer);

        BindMacTopology(projectFacesKernel);
        BindMacFaces(projectFacesKernel);
        BindMacPressure(projectFacesKernel);

        macGridShader.SetBuffer(gridToParticlesKernel, "_TileHash", tileHashBuffer);
        macGridShader.SetBuffer(gridToParticlesKernel, "_Positions", positionBuffer);
        macGridShader.SetBuffer(gridToParticlesKernel, "_Velocities", velocityBuffer);
        BindMacFaces(gridToParticlesKernel);
        BindMacOldFaces(gridToParticlesKernel);

        if (portalShader != null)
        {
            portalShader.SetInt("_ParticleCount", activeParticleCount);
            portalShader.SetBuffer(applySuctionKernel, "_Positions", positionBuffer);
            portalShader.SetBuffer(applySuctionKernel, "_Velocities", velocityBuffer);
            portalShader.SetBuffer(transportKernel, "_Positions", positionBuffer);
        }
    }

    private void BindSpatialSearch(int kernel)
    {
        spatialGridShader.SetBuffer(kernel, "_Positions", positionBuffer);
        spatialGridShader.SetBuffer(kernel, "_TileHash", tileHashBuffer);
        spatialGridShader.SetBuffer(kernel, "_ParticleCell", particleCellBuffer);
        spatialGridShader.SetBuffer(kernel, "_CellHeads", cellHeadBuffer);
        spatialGridShader.SetBuffer(kernel, "_ParticleNext", particleNextBuffer);
    }

    private void BindIntegrationCollision(int kernel)
    {
        integrationShader.SetBuffer(kernel, "_Positions", positionBuffer);
        integrationShader.SetBuffer(kernel, "_Velocities", velocityBuffer);
        integrationShader.SetBuffer(kernel, "_TileHash", tileHashBuffer);
        integrationShader.SetBuffer(kernel, "_ObstacleSdf", obstacleSdfBuffer);
    }

    private void BindMacFaces(int kernel)
    {
        macGridShader.SetBuffer(kernel, "_UFaceData", uFaceBuffer);
        macGridShader.SetBuffer(kernel, "_VFaceData", vFaceBuffer);
        macGridShader.SetBuffer(kernel, "_WFaceData", wFaceBuffer);
    }

    private void BindMacOldFaces(int kernel)
    {
        macGridShader.SetBuffer(kernel, "_OldUFaceData", oldUFaceBuffer);
        macGridShader.SetBuffer(kernel, "_OldVFaceData", oldVFaceBuffer);
        macGridShader.SetBuffer(kernel, "_OldWFaceData", oldWFaceBuffer);
    }

    private void BindMacTopology(int kernel)
    {
        macGridShader.SetBuffer(kernel, "_TileHash", tileHashBuffer);
        macGridShader.SetBuffer(kernel, "_TileCoordinates", tileCoordinatesBuffer);
    }

    private void BindMacPressure(int kernel)
    {
        macGridShader.SetBuffer(kernel, "_CellStates", cellStateBuffer);
        macGridShader.SetBuffer(kernel, "_PressureA", pressureABuffer);
        macGridShader.SetBuffer(kernel, "_PressureB", pressureBBuffer);
    }

    private void BindMacParticleToFace(int kernel, string faceName, ComputeBuffer faceBuffer)
    {
        BindMacTopology(kernel);
        macGridShader.SetBuffer(kernel, "_Positions", positionBuffer);
        macGridShader.SetBuffer(kernel, "_Velocities", velocityBuffer);
        macGridShader.SetBuffer(kernel, "_CellHeads", cellHeadBuffer);
        macGridShader.SetBuffer(kernel, "_ParticleNext", particleNextBuffer);
        macGridShader.SetBuffer(kernel, faceName, faceBuffer);
    }

    // 셀 목록
    private void BuildCellLists(int particleGroups, int cellGroups)
    {
        spatialGridShader.Dispatch(clearCellsKernel, cellGroups, 1, 1);
        spatialGridShader.Dispatch(assignCellsKernel, particleGroups, 1, 1);
        spatialGridShader.Dispatch(buildCellListsKernel, particleGroups, 1, 1);
    }

    // 물리 갱신
    private void FixedUpdate()
    {
        if (positionBuffer == null || tileHashBuffer == null)
        {
            return;
        }

        simulationStep++;

        FluidPortalBridge.PortalState portal = default;
        bool portalActive = portalBridge != null && portalBridge.isActiveAndEnabled && portalShader != null && portalBridge.TryGetPortal(out portal);

        if (portalActive)
        {
            if (!EnsurePortalExitTiles(portal.ExitCenter))
            {
                enabled = false;
                return;
            }

            pendingPortalTiles[WorldToTile(portal.ExitCenter)] = simulationStep;
        }

        RefreshMovingObstacles();

        int particleGroups = (activeParticleCount + 63) / 64;
        int cellGroups = (GridCellCount + 63) / 64;
        int faceGroups = (FaceCount + 63) / 64;
        float deltaTime = Time.fixedDeltaTime;

        integrationShader.SetFloat("_DeltaTime", deltaTime);
        integrationShader.SetFloat("_Gravity", gravity);
        integrationShader.SetVector("_ExternalAcceleration", externalAcceleration);
        integrationShader.SetFloat("_CollisionRadius", Mathf.Clamp(collisionRadius, 0.001f, cellSize * 0.49f));
        integrationShader.SetFloat("_WallBounce", Mathf.Clamp01(wallBounce));

        if (portalActive)
        {
            portalShader.SetFloat("_DeltaTime", deltaTime);
            portalShader.SetFloat("_PullAcceleration", portal.PullAcceleration);
            portalShader.SetFloat("_InnerRadius", portal.InnerRadius);
            portalShader.SetFloat("_EventHorizonRadius", portal.EventHorizonRadius);
            portalShader.SetFloat("_ParticleRadius", Mathf.Clamp(collisionRadius, 0.001f, cellSize * 0.49f));
            portalShader.SetVector("_InnerCenter", portal.InnerCenter);
            portalShader.SetVector("_EventHorizonCenter", portal.EventHorizonCenter);
            portalShader.SetVector("_ExitCenter", portal.ExitCenter);
            portalShader.Dispatch(applySuctionKernel, particleGroups, 1, 1);
        }

        BuildCellLists(particleGroups, cellGroups);

        if (solverMode == SolverMode.Sph)
        {
            spatialGridShader.SetFloat("_RestDensity", restDensity);
            spatialGridShader.SetFloat("_PressureStiffness", pressureStiffness);
            spatialGridShader.SetFloat("_ViscosityStrength", viscosityStrength);
            spatialGridShader.SetFloat("_DeltaTime", deltaTime);
            spatialGridShader.Dispatch(countNeighborsKernel, particleGroups, 1, 1);
            spatialGridShader.Dispatch(computeViscosityKernel, particleGroups, 1, 1);
            spatialGridShader.Dispatch(applyPressureKernel, particleGroups, 1, 1);
            integrationShader.SetInt("_IntegrateForces", 1);
        }
        else
        {
            StepPicFlip(particleGroups, cellGroups, faceGroups, deltaTime);
            integrationShader.SetInt("_IntegrateForces", 0);
        }

        integrationShader.Dispatch(integrateKernel, particleGroups, 1, 1);

        if (portalActive)
        {
            portalShader.Dispatch(transportKernel, particleGroups, 1, 1);
        }

        if (separationStrength > 0f)
        {
            spatialGridShader.SetFloat("_SeparationRadius", Mathf.Clamp(restSpacing, collisionRadius * 2f, cellSize));
            spatialGridShader.SetFloat("_SeparationStrength", Mathf.Max(0f, separationStrength));

            for (int iteration = 0; iteration < separationIterations; iteration++)
            {
                BuildCellLists(particleGroups, cellGroups);
                spatialGridShader.Dispatch(computeSeparationKernel, particleGroups, 1, 1);
                integrationShader.Dispatch(applyPositionCorrectionsKernel, particleGroups, 1, 1);
            }
        }

        ParticlePositionsUpdated?.Invoke(positionBuffer, activeParticleCount);

        if (!tileReadbackPending && Time.time >= nextTileReadbackTime)
        {
            sparseGridShader.Dispatch(clearMissingTilesKernel, 1, 1, 1);
            sparseGridShader.SetInt("_DespawnBelowY", despawnBelowY ? 1 : 0);
            sparseGridShader.SetFloat("_DespawnY", despawnY);
            sparseGridShader.Dispatch(collectParticleTilesKernel, particleGroups, 1, 1);
            tileReadback = AsyncGPUReadback.Request(particleTilesBuffer);
            tileReadbackStep = simulationStep;
            tileReadbackPending = true;
            nextTileReadbackTime = Time.time + tileRefreshInterval;
        }
    }

    private void StepPicFlip(int particleGroups, int cellGroups, int faceGroups, float deltaTime)
    {
        integrationShader.Dispatch(applyForcesKernel, particleGroups, 1, 1);

        macGridShader.SetFloat("_DeltaTime", deltaTime);
        macGridShader.SetFloat("_TargetParticlesPerCell", Mathf.Pow(cellSize / Mathf.Clamp(restSpacing, collisionRadius * 2f, cellSize), 3f));
        macGridShader.SetFloat("_VolumeCorrectionRate", Mathf.Clamp01(volumeCorrectionRate));

        macGridShader.Dispatch(clearFacesKernel, faceGroups, 1, 1);
        macGridShader.Dispatch(particleToUKernel, faceGroups, 1, 1);
        macGridShader.Dispatch(particleToVKernel, faceGroups, 1, 1);
        macGridShader.Dispatch(particleToWKernel, faceGroups, 1, 1);
        macGridShader.Dispatch(normalizeFacesKernel, faceGroups, 1, 1);
        macGridShader.Dispatch(classifyCellsKernel, cellGroups, 1, 1);
        macGridShader.Dispatch(applyBoundariesKernel, faceGroups, 1, 1);
        macGridShader.Dispatch(saveFacesKernel, faceGroups, 1, 1);
        macGridShader.Dispatch(computeDivergenceKernel, cellGroups, 1, 1);

        bool pressureInA = true;
        for (int iteration = 0; iteration < pressureIterations; iteration++)
        {
            macGridShader.Dispatch(pressureInA ? jacobiAKernel : jacobiBKernel, cellGroups, 1, 1);
            pressureInA = !pressureInA;
        }

        macGridShader.SetInt("_PressureIsA", pressureInA ? 1 : 0);
        macGridShader.Dispatch(projectFacesKernel, faceGroups, 1, 1);
        macGridShader.Dispatch(applyBoundariesKernel, faceGroups, 1, 1);

        macGridShader.SetFloat("_FlipRatio", Mathf.Clamp01(flipRatio));
        macGridShader.SetFloat("_VelocityDamping", Mathf.Max(0f, velocityDamping));
        macGridShader.SetFloat("_MaxSpeed", Mathf.Min(Mathf.Max(0.1f, maxSpeed), cellSize * 0.9f / Mathf.Max(deltaTime, 0.000001f)));
        macGridShader.Dispatch(gridToParticlesKernel, particleGroups, 1, 1);
    }

    // 타일 읽기
    private void ProcessTileReadback()
    {
        if (!tileReadbackPending || !tileReadback.done)
        {
            return;
        }

        tileReadbackPending = false;

        if (tileReadback.hasError)
        {
            Debug.LogWarning("입자 타일 좌표를 GPU에서 읽지 못했습니다.", this);
            return;
        }

        var entries = tileReadback.GetData<TileEntry>();
        var occupied = new HashSet<Vector3Int>();
        bool needsCompaction = false;

        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].w == -2)
            {
                needsCompaction = true;
                continue;
            }

            occupied.Add(new Vector3Int(entries[i].x, entries[i].y, entries[i].z));
        }

        if (needsCompaction && !CompactParticlesBelowY(out occupied))
        {
            return;
        }

        var completedPortalTiles = new List<Vector3Int>();
        foreach (KeyValuePair<Vector3Int, long> entry in pendingPortalTiles)
        {
            if (entry.Value > tileReadbackStep)
            {
                occupied.Add(entry.Key);
            }
            else
            {
                completedPortalTiles.Add(entry.Key);
            }
        }

        foreach (Vector3Int tile in completedPortalTiles)
        {
            pendingPortalTiles.Remove(tile);
        }

        if (!SetActiveTiles(ExpandTiles(occupied)))
        {
            enabled = false;
            return;
        }

        if (needsCompaction)
        {
            BindBuffers();
        }
    }

    // 입자 렌더링
    private void Update()
    {
        ProcessTileReadback();

        if (!enabled || sphereMeshSource == null || sphereMeshSource.sharedMesh == null || particleMaterial == null)
        {
            return;
        }

        var drawParams = new RenderParams(particleMaterial)
        {
            matProps = particleProperties,
            worldBounds = renderBounds
        };

        Graphics.RenderMeshPrimitives(drawParams, sphereMeshSource.sharedMesh, 0, activeParticleCount);
    }

    // 입자 삭제
    private bool CompactParticlesBelowY(out HashSet<Vector3Int> occupied)
    {
        occupied = new HashSet<Vector3Int>();

        var positions = new Vector4[activeParticleCount];
        var velocities = new Vector4[activeParticleCount];
        positionBuffer.GetData(positions);
        velocityBuffer.GetData(velocities);

        int remaining = 0;

        for (int i = 0; i < activeParticleCount; i++)
        {
            Vector4 position = positions[i];

            if (despawnBelowY && position.y <= despawnY)
            {
                continue;
            }

            positions[remaining] = position;
            velocities[remaining] = velocities[i];
            occupied.Add(WorldToTile(new Vector3(position.x, position.y, position.z)));
            remaining++;
        }

        if (remaining == 0)
        {
            enabled = false;
            return false;
        }

        if (remaining == activeParticleCount)
        {
            return true;
        }

        Array.Resize(ref positions, remaining);
        Array.Resize(ref velocities, remaining);

        ReleaseParticleBuffers();
        activeParticleCount = remaining;
        CreateParticleBuffers(positions, velocities);
        particleProperties.SetBuffer("_Positions", positionBuffer);

        return true;
    }

    // 장애물 필드
    [ContextMenu("장애물 필드 갱신")]
    private void RebuildObstacleField()
    {
        BakeObstacleField(true);
    }

    private void BakeObstacleField(bool logResult, bool reuseCachedTiles = false)
    {
        if (obstacleSdfBuffer == null)
        {
            return;
        }

        Collider[] obstacles = CollectObstacles();
        int signature = ObstacleSignature(obstacles);

        // 장애물 구성이 같으면 기존 타일 SDF 재사용, 새 타일만 계산
        if (!reuseCachedTiles || signature != tileSdfCacheSignature)
        {
            tileSdfCache.Clear();
            reuseCachedTiles = false;
        }

        tileSdfCacheSignature = signature;
        var values = new float[GridCellCount];
        var tileResolution = new Vector3Int(CellsPerTile, CellsPerTile, CellsPerTile);

        for (int i = 0; i < activeTiles.Count; i++)
        {
            Vector3Int tile = activeTiles[i];

            if (!tileSdfCache.TryGetValue(tile, out float[] tileValues))
            {
                Vector3 tileMin = new Vector3(tile.x, tile.y, tile.z) * TileWorldSize;
                tileValues = FluidObstacleField.Build(tileMin, tileResolution, cellSize, obstacles);
                tileSdfCache[tile] = tileValues;
            }

            Array.Copy(tileValues, 0, values, i * CellCountPerTile, CellCountPerTile);
        }

        // 비활성 타일은 이동 장애물 갱신 대상이 아니므로 캐시에서 제거
        staleSdfTiles.Clear();
        foreach (Vector3Int tile in tileSdfCache.Keys)
        {
            if (!activeTileSet.Contains(tile))
            {
                staleSdfTiles.Add(tile);
            }
        }

        foreach (Vector3Int tile in staleSdfTiles)
        {
            tileSdfCache.Remove(tile);
        }

        obstacleSdfBuffer.SetData(values);

        // 재사용한 타일은 기존 자세 기준이므로 이동 장애물 추적을 유지
        if (reuseCachedTiles)
        {
            return;
        }

        movingObstaclePoses.Clear();
        foreach (Collider obstacle in obstacles)
        {
            if (obstacle.attachedRigidbody != null)
            {
                movingObstaclePoses[obstacle] = new ObstaclePose(obstacle);
            }
        }

        if (logResult)
        {
            Debug.Log($"유체 장애물 필드: Collider {obstacles.Length}개, 활성 타일 {activeTiles.Count}개", this);
        }
    }

    private static int ObstacleSignature(Collider[] obstacles)
    {
        unchecked
        {
            int hash = obstacles.Length;
            foreach (Collider obstacle in obstacles)
            {
                hash += obstacle.GetInstanceID() * 486187739;
            }

            return hash;
        }
    }

    // 이동 장애물 갱신
    private void RefreshMovingObstacles()
    {
        Collider[] obstacles = CollectObstacles();
        observedMovingObstacles.Clear();
        removedMovingObstacles.Clear();
        dirtyObstacleTiles.Clear();

        bool sandRefreshDue = Time.time >= nextSandObstacleRefreshTime;
        bool sandRefreshed = false;

        foreach (Collider obstacle in obstacles)
        {
            if (obstacle.attachedRigidbody == null)
            {
                continue;
            }

            observedMovingObstacles.Add(obstacle);
            ObstaclePose current = new ObstaclePose(obstacle);

            if (movingObstaclePoses.TryGetValue(obstacle, out ObstaclePose previous))
            {
                // 모래는 제자리에서 모양만 바뀜 → 바뀐 영역만, 주기 제한으로 갱신
                SandMesh sand = obstacle is MeshCollider ? obstacle.GetComponentInParent<SandMesh>() : null;

                if (sand != null && previous.matrix.Equals(current.matrix))
                {
                    if (sandRefreshDue && sand.TryConsumeChangedBounds(out Bounds changedBounds))
                    {
                        MarkObstacleTiles(changedBounds, pendingSandTiles);
                        movingObstaclePoses[obstacle] = current;
                        sandRefreshed = true;
                    }

                    continue;
                }

                if (previous.Matches(current))
                {
                    continue;
                }

                MarkObstacleTiles(previous.bounds);
            }

            MarkObstacleTiles(current.bounds);
            movingObstaclePoses[obstacle] = current;
        }

        foreach (KeyValuePair<Collider, ObstaclePose> entry in movingObstaclePoses)
        {
            if (observedMovingObstacles.Contains(entry.Key))
            {
                continue;
            }

            MarkObstacleTiles(entry.Value.bounds);
            removedMovingObstacles.Add(entry.Key);
        }

        foreach (Collider obstacle in removedMovingObstacles)
        {
            movingObstaclePoses.Remove(obstacle);
        }

        if (sandRefreshed)
        {
            nextSandObstacleRefreshTime = Time.time + sandObstacleRefreshInterval;
        }

        // 모래 변경 타일은 스텝당 개수를 제한해 여러 프레임에 나눠 갱신
        if (pendingSandTiles.Count > 0)
        {
            sandTileBatch.Clear();
            foreach (Vector3Int tile in pendingSandTiles)
            {
                if (sandTileBatch.Count >= maxSandTileRebuildsPerStep)
                {
                    break;
                }

                sandTileBatch.Add(tile);
            }

            foreach (Vector3Int tile in sandTileBatch)
            {
                pendingSandTiles.Remove(tile);
                int tileIndex = activeTiles.BinarySearch(tile, TileComparer);

                if (tileIndex >= 0)
                {
                    dirtyObstacleTiles.Add(tileIndex);
                }
            }
        }

        Vector3Int resolution = new Vector3Int(CellsPerTile, CellsPerTile, CellsPerTile);

        foreach (int tileIndex in dirtyObstacleTiles)
        {
            Vector3Int tile = activeTiles[tileIndex];
            Vector3 tileMin = new Vector3(tile.x, tile.y, tile.z) * TileWorldSize;
            float[] values = FluidObstacleField.Build(tileMin, resolution, cellSize, obstacles);
            tileSdfCache[tile] = values;
            obstacleSdfBuffer.SetData(values, 0, tileIndex * CellCountPerTile, CellCountPerTile);
        }
    }

    // 변경된 타일 찾기
    private void MarkObstacleTiles(Bounds bounds, HashSet<Vector3Int> coordinateTarget = null)
    {
        bounds.Expand(cellSize * 6f);
        Vector3 tileSize = Vector3.one * TileWorldSize;

        for (int i = 0; i < activeTiles.Count; i++)
        {
            Vector3Int tile = activeTiles[i];
            Vector3 tileMin = new Vector3(tile.x, tile.y, tile.z) * TileWorldSize;
            Bounds tileBounds = new Bounds(tileMin + tileSize * 0.5f, tileSize);

            if (!tileBounds.Intersects(bounds))
            {
                continue;
            }

            if (coordinateTarget != null)
            {
                coordinateTarget.Add(tile);
            }
            else
            {
                dirtyObstacleTiles.Add(i);
            }
        }
    }

    // 장애물 수집
    private Collider[] CollectObstacles()
    {
        Physics.SyncTransforms();
        var selected = new HashSet<Collider>();

        if (staticObstacles != null)
        {
            foreach (Collider obstacle in staticObstacles)
            {
                if (IsSupportedObstacle(obstacle))
                {
                    selected.Add(obstacle);
                }
            }
        }

        if (autoCollectObstacles)
        {
            Collider[] sceneColliders = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);

            foreach (Collider obstacle in sceneColliders)
            {
                if (obstacle.gameObject.scene.handle != gameObject.scene.handle)
                {
                    continue;
                }

                if ((obstacleLayers.value & (1 << obstacle.gameObject.layer)) == 0)
                {
                    continue;
                }

                if (IsSupportedObstacle(obstacle))
                {
                    selected.Add(obstacle);
                }
            }
        }

        var result = new Collider[selected.Count];
        selected.CopyTo(result);
        return result;
    }

    private bool IsSupportedObstacle(Collider obstacle)
    {
        if (obstacle == null || !obstacle.enabled || !obstacle.gameObject.activeInHierarchy || obstacle.isTrigger)
        {
            return false;
        }

        if (obstacle.transform == transform)
        {
            return false;
        }

        if (sphereMeshSource != null && obstacle.transform.IsChildOf(sphereMeshSource.transform))
        {
            return false;
        }

        return obstacle is BoxCollider || obstacle is SphereCollider || obstacle is CapsuleCollider || obstacle is MeshCollider;
    }

    // 진단
    [ContextMenu("셀 점유 확인")]
    private void LogGridOccupancy()
    {
        if (!Application.isPlaying || cellCountBuffer == null)
        {
            return;
        }

        var counts = new int[GridCellCount];
        cellCountBuffer.GetData(counts);

        int total = 0;
        int occupied = 0;
        int maximum = 0;

        foreach (int count in counts)
        {
            total += count;
            maximum = Mathf.Max(maximum, count);

            if (count > 0)
            {
                occupied++;
            }
        }

        Debug.Log($"셀 합계: {total}/{activeParticleCount}, 점유 셀: {occupied}, 최대 셀 인원: {maximum}, 활성 타일: {activeTiles.Count}", this);
    }

    [ContextMenu("입자 상태 확인")]
    private void LogParticleState()
    {
        if (!Application.isPlaying || positionBuffer == null)
        {
            return;
        }

        var positions = new Vector4[activeParticleCount];
        var velocities = new Vector4[activeParticleCount];
        positionBuffer.GetData(positions);
        velocityBuffer.GetData(velocities);

        int invalid = 0;
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        float highestSpeed = 0f;

        for (int i = 0; i < activeParticleCount; i++)
        {
            Vector4 position = positions[i];
            Vector4 velocity = velocities[i];

            if (!float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z) || !float.IsFinite(velocity.x) || !float.IsFinite(velocity.y) || !float.IsFinite(velocity.z))
            {
                invalid++;
                continue;
            }

            minY = Mathf.Min(minY, position.y);
            maxY = Mathf.Max(maxY, position.y);
            highestSpeed = Mathf.Max(highestSpeed, new Vector3(velocity.x, velocity.y, velocity.z).magnitude);
        }

        Debug.Log($"입자: {activeParticleCount}, 비정상 값: {invalid}, 높이 최소/최대: {minY:F2}/{maxY:F2}, 최고 속도: {highestSpeed:F2}", this);
    }

    [ContextMenu("셀 시각화 갱신")]
    private void CaptureGridCells()
    {
        if (!Application.isPlaying || cellCountBuffer == null)
        {
            return;
        }

        debugCellStates = new int[GridCellCount];

        if (solverMode == SolverMode.PicFlip)
        {
            cellStateBuffer.GetData(debugCellStates);
        }
        else
        {
            cellCountBuffer.GetData(debugCellStates);

            for (int i = 0; i < debugCellStates.Length; i++)
            {
                debugCellStates[i] = debugCellStates[i] > 0 ? 1 : 0;
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || activeTiles.Count == 0)
        {
            return;
        }

        float tileSize = TileWorldSize;

        if (drawGridBounds)
        {
            Gizmos.color = Color.cyan;

            foreach (Vector3Int tile in activeTiles)
            {
                Vector3 center = (new Vector3(tile.x, tile.y, tile.z) + Vector3.one * 0.5f) * tileSize;
                Gizmos.DrawWireCube(center, Vector3.one * tileSize);
            }
        }

        if (!drawFluidCells || debugCellStates == null || debugCellStates.Length != GridCellCount)
        {
            return;
        }

        for (int i = 0; i < debugCellStates.Length; i++)
        {
            int state = debugCellStates[i];

            if (state == 0)
            {
                continue;
            }

            int tileIndex = i / CellCountPerTile;
            int local = i - tileIndex * CellCountPerTile;
            int x = local & 7;
            int y = (local >> 3) & 7;
            int z = local >> 6;
            Vector3Int tile = activeTiles[tileIndex];
            Vector3 center = (new Vector3(tile.x * CellsPerTile + x + 0.5f, tile.y * CellsPerTile + y + 0.5f, tile.z * CellsPerTile + z + 0.5f)) * cellSize;

            Gizmos.color = state == 1 ? Color.blue : Color.gray;
            Gizmos.DrawWireCube(center, Vector3.one * cellSize * 0.8f);
        }
    }

    // 자원 해제
    private static ComputeBuffer ReleaseBuffer(ComputeBuffer buffer)
    {
        buffer?.Release();
        return null;
    }

    private void ReleaseTileBuffers()
    {
        tileHashBuffer = ReleaseBuffer(tileHashBuffer);
        tileCoordinatesBuffer = ReleaseBuffer(tileCoordinatesBuffer);
        cellCountBuffer = ReleaseBuffer(cellCountBuffer);
        cellHeadBuffer = ReleaseBuffer(cellHeadBuffer);
        obstacleSdfBuffer = ReleaseBuffer(obstacleSdfBuffer);
        cellStateBuffer = ReleaseBuffer(cellStateBuffer);
        divergenceBuffer = ReleaseBuffer(divergenceBuffer);
        pressureABuffer = ReleaseBuffer(pressureABuffer);
        pressureBBuffer = ReleaseBuffer(pressureBBuffer);
        uFaceBuffer = ReleaseBuffer(uFaceBuffer);
        vFaceBuffer = ReleaseBuffer(vFaceBuffer);
        wFaceBuffer = ReleaseBuffer(wFaceBuffer);
        oldUFaceBuffer = ReleaseBuffer(oldUFaceBuffer);
        oldVFaceBuffer = ReleaseBuffer(oldVFaceBuffer);
        oldWFaceBuffer = ReleaseBuffer(oldWFaceBuffer);
    }

    private void ReleaseParticleBuffers()
    {
        positionBuffer = ReleaseBuffer(positionBuffer);
        velocityBuffer = ReleaseBuffer(velocityBuffer);
        particleCellBuffer = ReleaseBuffer(particleCellBuffer);
        particleNextBuffer = ReleaseBuffer(particleNextBuffer);
        neighborCountBuffer = ReleaseBuffer(neighborCountBuffer);
        densityBuffer = ReleaseBuffer(densityBuffer);
        pressureBuffer = ReleaseBuffer(pressureBuffer);
        viscosityAccelerationBuffer = ReleaseBuffer(viscosityAccelerationBuffer);
        positionCorrectionBuffer = ReleaseBuffer(positionCorrectionBuffer);
        particleTilesBuffer = ReleaseBuffer(particleTilesBuffer);
        missingTileCountBuffer = ReleaseBuffer(missingTileCountBuffer);
    }

    private void OnDisable()
    {
        SimulationStopped?.Invoke();
        tileReadbackPending = false;
        ReleaseTileBuffers();
        ReleaseParticleBuffers();

        activeTiles.Clear();
        activeTileSet.Clear();
        pendingPortalTiles.Clear();
        tileSdfCache.Clear();
        pendingSandTiles.Clear();
        movingObstaclePoses.Clear();
        observedMovingObstacles.Clear();
        removedMovingObstacles.Clear();
        dirtyObstacleTiles.Clear();
        debugCellStates = null;
        particleProperties = null;
        activeParticleCount = 0;
        tileHashCapacity = 0;
        simulationStep = 0;
        tileReadbackStep = 0;
    }

    private void OnValidate()
    {
        if (Application.isPlaying && positionBuffer != null)
        {
            cellSize = allocatedCellSize;
        }
    }
}

// 콜라이더 거리
internal static class FluidObstacleField
{
    public static float[] Build(Vector3 gridMin, Vector3Int resolution, float cellSize, Collider[] colliders)
    {
        int count = resolution.x * resolution.y * resolution.z;
        var field = new float[count];

        for (int i = 0; i < count; i++)
        {
            field[i] = 10000f;
        }

        Vector3 gridSize = new Vector3(resolution.x, resolution.y, resolution.z) * cellSize;
        Bounds gridBounds = new Bounds(gridMin + gridSize * 0.5f, gridSize);

        foreach (Collider collider in colliders)
        {
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
            {
                continue;
            }

            Bounds bounds = collider.bounds;
            bounds.Expand(cellSize * 4f);

            if (!bounds.Intersects(gridBounds))
            {
                continue;
            }

            MeshCollider meshCollider = collider as MeshCollider;
            Vector3[] meshVertices = null;
            int[] triangles = null;

            // 모래는 높이맵이라 셀당 O(1) 샘플링 (삼각형 전수 검사는 타일당 수 초가 걸림)
            SandMesh sand = meshCollider != null ? meshCollider.GetComponentInParent<SandMesh>() : null;

            if (sand != null)
            {
                BuildSandField(field, gridMin, resolution, cellSize, bounds, sand);
                continue;
            }

            if (collider is BoxCollider boxCollider)
            {
                BuildBoxField(field, gridMin, resolution, cellSize, bounds, boxCollider);
                continue;
            }

            if (meshCollider != null)
            {
                Mesh mesh = meshCollider.sharedMesh;

                if (mesh == null || !mesh.isReadable)
                {
                    continue;
                }

                meshVertices = mesh.vertices;
                triangles = mesh.triangles;

                for (int i = 0; i < meshVertices.Length; i++)
                {
                    meshVertices[i] = meshCollider.transform.TransformPoint(meshVertices[i]);
                }
            }
            else if (!(collider is BoxCollider) && !(collider is SphereCollider) && !(collider is CapsuleCollider))
            {
                continue;
            }

            for (int z = 0; z < resolution.z; z++)
            {
                for (int y = 0; y < resolution.y; y++)
                {
                    for (int x = 0; x < resolution.x; x++)
                    {
                        Vector3 point = gridMin + new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * cellSize;

                        if (!bounds.Contains(point))
                        {
                            continue;
                        }

                        float distance;

                        if (collider is BoxCollider box)
                        {
                            distance = BoxDistance(box, point);
                        }
                        else if (collider is SphereCollider sphere)
                        {
                            distance = SphereDistance(sphere, point);
                        }
                        else if (collider is CapsuleCollider capsule)
                        {
                            distance = CapsuleDistance(capsule, point);
                        }
                        else
                        {
                            distance = MeshDistance(meshVertices, triangles, point, cellSize);
                        }

                        int index = x + resolution.x * (y + resolution.y * z);
                        field[index] = Mathf.Min(field[index], distance);
                    }
                }
            }
        }

        return field;
    }

    // bounds 안에 중심이 들어오는 셀 범위
    private static void CellRange(Bounds bounds, Vector3 gridMin, Vector3Int resolution, float cellSize, out Vector3Int from, out Vector3Int to)
    {
        Vector3 minimum = (bounds.min - gridMin) / cellSize - Vector3.one * 0.5f;
        Vector3 maximum = (bounds.max - gridMin) / cellSize - Vector3.one * 0.5f;
        from = new Vector3Int(Mathf.Max(0, Mathf.CeilToInt(minimum.x)), Mathf.Max(0, Mathf.CeilToInt(minimum.y)), Mathf.Max(0, Mathf.CeilToInt(minimum.z)));
        to = new Vector3Int(Mathf.Min(resolution.x - 1, Mathf.FloorToInt(maximum.x)), Mathf.Min(resolution.y - 1, Mathf.FloorToInt(maximum.y)), Mathf.Min(resolution.z - 1, Mathf.FloorToInt(maximum.z)));
    }

    // 박스 변환을 셀마다 다시 구하지 않도록 한 번만 계산 (BoxDistance와 같은 식)
    private static void BuildBoxField(float[] field, Vector3 gridMin, Vector3Int resolution, float cellSize, Bounds bounds, BoxCollider box)
    {
        Transform boxTransform = box.transform;
        Vector3 center = boxTransform.TransformPoint(box.center);
        Quaternion inverseRotation = Quaternion.Inverse(boxTransform.rotation);
        Vector3 scale = boxTransform.lossyScale;
        Vector3 half = Vector3.Scale(box.size * 0.5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));

        CellRange(bounds, gridMin, resolution, cellSize, out Vector3Int from, out Vector3Int to);

        for (int z = from.z; z <= to.z; z++)
        {
            for (int y = from.y; y <= to.y; y++)
            {
                for (int x = from.x; x <= to.x; x++)
                {
                    Vector3 point = gridMin + new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * cellSize;

                    if (!bounds.Contains(point))
                    {
                        continue;
                    }

                    Vector3 local = inverseRotation * (point - center);
                    Vector3 difference = new Vector3(Mathf.Abs(local.x) - half.x, Mathf.Abs(local.y) - half.y, Mathf.Abs(local.z) - half.z);
                    Vector3 outside = Vector3.Max(difference, Vector3.zero);
                    float inside = Mathf.Min(Mathf.Max(difference.x, Mathf.Max(difference.y, difference.z)), 0f);

                    int index = x + resolution.x * (y + resolution.y * z);
                    field[index] = Mathf.Min(field[index], outside.magnitude + inside);
                }
            }
        }
    }

    private static void BuildSandField(float[] field, Vector3 gridMin, Vector3Int resolution, float cellSize, Bounds bounds, SandMesh sand)
    {
        CellRange(bounds, gridMin, resolution, cellSize, out Vector3Int from, out Vector3Int to);

        for (int z = from.z; z <= to.z; z++)
        {
            for (int y = from.y; y <= to.y; y++)
            {
                for (int x = from.x; x <= to.x; x++)
                {
                    Vector3 point = gridMin + new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * cellSize;

                    if (!bounds.Contains(point) || !sand.TryGetSignedDistance(point, out float distance))
                    {
                        continue;
                    }

                    int index = x + resolution.x * (y + resolution.y * z);
                    field[index] = Mathf.Min(field[index], distance);
                }
            }
        }
    }

    private static float BoxDistance(BoxCollider box, Vector3 point)
    {
        Vector3 local = Quaternion.Inverse(box.transform.rotation) * (point - box.transform.TransformPoint(box.center));
        Vector3 scale = box.transform.lossyScale;
        Vector3 half = Vector3.Scale(box.size * 0.5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        Vector3 difference = new Vector3(Mathf.Abs(local.x) - half.x, Mathf.Abs(local.y) - half.y, Mathf.Abs(local.z) - half.z);
        Vector3 outside = Vector3.Max(difference, Vector3.zero);
        float inside = Mathf.Min(Mathf.Max(difference.x, Mathf.Max(difference.y, difference.z)), 0f);
        return outside.magnitude + inside;
    }

    private static float SphereDistance(SphereCollider sphere, Vector3 point)
    {
        Vector3 scale = sphere.transform.lossyScale;
        float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        return Vector3.Distance(point, sphere.transform.TransformPoint(sphere.center)) - radius;
    }

    // 캡슐 거리
    private static float CapsuleDistance(CapsuleCollider capsule, Vector3 point)
    {
        Vector3 scale = capsule.transform.lossyScale;
        Vector3 absoluteScale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

        Vector3 localAxis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
        Vector3 worldAxis = capsule.transform.TransformDirection(localAxis).normalized;

        float axisScale = capsule.direction == 0 ? absoluteScale.x : capsule.direction == 1 ? absoluteScale.y : absoluteScale.z;
        float radiusScale = capsule.direction == 0 ? Mathf.Max(absoluteScale.y, absoluteScale.z) : capsule.direction == 1 ? Mathf.Max(absoluteScale.x, absoluteScale.z) : Mathf.Max(absoluteScale.x, absoluteScale.y);

        float radius = capsule.radius * radiusScale;
        float halfSegment = Mathf.Max(0f, capsule.height * axisScale * 0.5f - radius);
        Vector3 center = capsule.transform.TransformPoint(capsule.center);
        Vector3 start = center - worldAxis * halfSegment;
        Vector3 end = center + worldAxis * halfSegment;
        Vector3 segment = end - start;

        float t = segment.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(point - start, segment) / segment.sqrMagnitude) : 0f;
        return Vector3.Distance(point, Vector3.Lerp(start, end, t)) - radius;
    }

    private static float MeshDistance(Vector3[] vertices, int[] triangles, Vector3 point, float cellSize)
    {
        float closest = float.MaxValue;
        int crossings = 0;
        Vector3 rayOrigin = point + new Vector3(0f, cellSize * 0.00037f, cellSize * 0.00061f);

        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = vertices[triangles[i]];
            Vector3 b = vertices[triangles[i + 1]];
            Vector3 c = vertices[triangles[i + 2]];

            if (Vector3.Cross(b - a, c - a).sqrMagnitude < 0.000000000001f)
            {
                continue;
            }

            closest = Mathf.Min(closest, PointTriangleDistanceSquared(point, a, b, c));

            if (RayHitsTriangle(rayOrigin, a, b, c))
            {
                crossings++;
            }
        }

        float distance = Mathf.Sqrt(closest);
        return (crossings & 1) != 0 ? -distance : distance;
    }

    private static float PointTriangleDistanceSquared(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        Vector3 ap = point - a;
        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);

        if (d1 <= 0f && d2 <= 0f)
        {
            return ap.sqrMagnitude;
        }

        Vector3 bp = point - b;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);

        if (d3 >= 0f && d4 <= d3)
        {
            return bp.sqrMagnitude;
        }

        float vc = d1 * d4 - d3 * d2;

        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            return (point - (a + ab * (d1 / (d1 - d3)))).sqrMagnitude;
        }

        Vector3 cp = point - c;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);

        if (d6 >= 0f && d5 <= d6)
        {
            return cp.sqrMagnitude;
        }

        float vb = d5 * d2 - d1 * d6;

        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            return (point - (a + ac * (d2 / (d2 - d6)))).sqrMagnitude;
        }

        float va = d3 * d6 - d5 * d4;

        if (va <= 0f && d4 >= d3 && d5 >= d6)
        {
            return (point - (b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))))).sqrMagnitude;
        }

        float denominator = 1f / (va + vb + vc);
        Vector3 projection = a + ab * (vb * denominator) + ac * (vc * denominator);
        return (point - projection).sqrMagnitude;
    }

    private static bool RayHitsTriangle(Vector3 origin, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 edge1 = b - a;
        Vector3 edge2 = c - a;
        Vector3 cross = Vector3.Cross(Vector3.right, edge2);
        float determinant = Vector3.Dot(edge1, cross);

        if (Mathf.Abs(determinant) < 0.0000001f)
        {
            return false;
        }

        float inverse = 1f / determinant;
        Vector3 offset = origin - a;
        float u = Vector3.Dot(offset, cross) * inverse;

        if (u < 0f || u > 1f)
        {
            return false;
        }

        Vector3 tangent = Vector3.Cross(offset, edge1);
        float v = Vector3.Dot(Vector3.right, tangent) * inverse;

        if (v < 0f || u + v > 1f)
        {
            return false;
        }

        return Vector3.Dot(edge2, tangent) * inverse > 0.000001f;
    }
}