using UnityEngine;

public sealed class FluidSimulation : MonoBehaviour
{
    private enum SolverMode { Sph, PicFlip }
    private const int Float4Stride = sizeof(float) * 4;

    // 입자 설정
    [SerializeField] private int particleCount = 512;
    [SerializeField] private Vector3 spawnCenter = new Vector3(0f, 3f, 0f);
    [SerializeField] private Vector3 spawnSize = new Vector3(2f, 1f, 2f);

    // 렌더링 설정
    [SerializeField] private MeshFilter sphereMeshSource;
    [SerializeField] private Material particleMaterial;

    // 물리 설정
    [SerializeField] private ComputeShader integrationShader;
    [SerializeField] private ComputeShader spatialGridShader;
    [SerializeField] private ComputeShader macGridShader;
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float collisionRadius = 0.08f;
    [SerializeField] private Vector3 initialVelocity = new Vector3(0f, 0f, 0f);
    [SerializeField] private float wallBounce = 0.5f;
    [SerializeField] private float restDensity = 400f;
    [SerializeField] private float pressureStiffness = 1f;
    [SerializeField] private float viscosityStrength = 5f;
    [SerializeField] private SolverMode solverMode = SolverMode.PicFlip;
    [SerializeField, Range(0f, 1f)] private float flipRatio = 0.95f;
    [SerializeField, Range(2, 128)] private int pressureIterations = 40;
    [SerializeField] private float velocityDamping = 0.05f;
    [SerializeField] private float maxSpeed = 20f;
    [SerializeField] private float separationStrength = 1f;
    [SerializeField] private Vector3Int gridResolution = new Vector3Int(20, 10, 20);
    [SerializeField] private Vector3 gridMin = new Vector3(-4f, 0f, -4f);
    [SerializeField] private float cellSize = 0.4f;
    [SerializeField] private Vector3 externalAcceleration = Vector3.zero;
    [SerializeField] private Collider[] staticObstacles;
    [SerializeField] private bool drawGridBounds = true;
    [SerializeField] private bool drawFluidCells;

    // GPU 자원
    private MaterialPropertyBlock particleProperties;
    private ComputeBuffer positionBuffer;
    private ComputeBuffer velocityBuffer;
    private ComputeBuffer particleCellBuffer;
    private ComputeBuffer cellCountBuffer;
    private ComputeBuffer cellHeadBuffer;
    private ComputeBuffer particleNextBuffer;
    private ComputeBuffer neighborCountBuffer;
    private ComputeBuffer densityBuffer;
    private ComputeBuffer pressureBuffer;
    private ComputeBuffer viscosityAccelerationBuffer;
    private ComputeBuffer uFaceBuffer;
    private ComputeBuffer vFaceBuffer;
    private ComputeBuffer wFaceBuffer;
    private ComputeBuffer oldUFaceBuffer;
    private ComputeBuffer oldVFaceBuffer;
    private ComputeBuffer oldWFaceBuffer;
    private ComputeBuffer cellStateBuffer;
    private ComputeBuffer divergenceBuffer;
    private ComputeBuffer pressureABuffer;
    private ComputeBuffer pressureBBuffer;
    private ComputeBuffer obstacleSdfBuffer;
    private ComputeBuffer positionCorrectionBuffer;
    private int[] debugCellStates;

    private int GridX => gridResolution.x;
    private int GridY => gridResolution.y;
    private int GridZ => gridResolution.z;
    private int GridCellCount => GridX * GridY * GridZ;
    private const int FaceStride = sizeof(float) * 2;
    private int UFaceCount => (GridX + 1) * GridY * GridZ;
    private int VFaceCount => GridX * (GridY + 1) * GridZ;
    private int WFaceCount => GridX * GridY * (GridZ + 1);
    private const float ParticleMass = 1f;
    private int integrateKernel = -1;
    private int applyForcesKernel = -1;
    private int countNeighborsKernel = -1;
    private int assignCellsKernel = -1;
    private int clearCellsKernel = -1;
    private int buildCellListsKernel = -1;
    private int applyPressureKernel = -1;
    private int computeViscosityKernel = -1;
    private int computeSeparationKernel = -1;
    private int clearFacesKernel = -1;
    private int particleToVKernel = -1;
    private int particleToUKernel = -1;
    private int particleToWKernel = -1;
    private int normalizeFacesKernel = -1;
    private int classifyCellsKernel = -1;
    private int applyBoundariesKernel = -1;
    private int saveFacesKernel = -1;
    private int computeDivergenceKernel = -1;
    private int jacobiAKernel = -1;
    private int jacobiBKernel = -1;
    private int projectFacesKernel = -1;
    private int gridToParticlesKernel = -1;
    private int allocatedParticleCount;
    private Vector3Int allocatedGridResolution;
    private Vector3 allocatedGridMin;
    private float allocatedCellSize;

    // 입자 초기화
    private void OnEnable()
    {
        particleCount = Mathf.Max(1, particleCount);
        gridResolution = new Vector3Int(Mathf.Clamp(GridX, 2, 64), Mathf.Clamp(GridY, 2, 64), Mathf.Clamp(GridZ, 2, 64));
        cellSize = Mathf.Max(0.05f, cellSize);
        collisionRadius = Mathf.Clamp(collisionRadius, 0.001f, cellSize * 0.49f);
        wallBounce = Mathf.Clamp01(wallBounce);
        maxSpeed = Mathf.Max(0.1f, maxSpeed);
        pressureIterations = Mathf.Clamp(pressureIterations, 2, 128);
        allocatedParticleCount = particleCount;
        allocatedGridResolution = gridResolution;
        allocatedGridMin = gridMin;
        allocatedCellSize = cellSize;

        if (integrationShader == null || spatialGridShader == null || (solverMode == SolverMode.PicFlip && macGridShader == null))
        {
            Debug.LogError("유체 Compute Shader 연결을 확인하세요.", this);
            enabled = false;
            return;
        }

        // 위치 생성
        var positions = new Vector4[particleCount];
        var velocities = new Vector4[particleCount];
        var random = new System.Random(1234);

        for (int i = 0; i < particleCount; i++)
        {
            var offset = new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f);

            Vector3 p = spawnCenter + Vector3.Scale(offset, spawnSize);
            positions[i] = new Vector4(p.x, p.y, p.z, 1f);

            velocities[i] = new Vector4(initialVelocity.x, initialVelocity.y, initialVelocity.z, 0f);
        }

        // 버퍼 생성
        positionBuffer = new ComputeBuffer(particleCount, Float4Stride);
        velocityBuffer = new ComputeBuffer(particleCount, Float4Stride);
        particleCellBuffer = new ComputeBuffer(particleCount, sizeof(int));
        cellCountBuffer = new ComputeBuffer(GridCellCount, sizeof(int));
        cellHeadBuffer = new ComputeBuffer(GridCellCount, sizeof(int));
        particleNextBuffer = new ComputeBuffer(particleCount, sizeof(int));
        neighborCountBuffer = new ComputeBuffer(particleCount, sizeof(int));
        densityBuffer = new ComputeBuffer(particleCount, sizeof(float));
        pressureBuffer = new ComputeBuffer(particleCount, sizeof(float));
        viscosityAccelerationBuffer = new ComputeBuffer(particleCount, Float4Stride);
        uFaceBuffer = new ComputeBuffer(UFaceCount, FaceStride);
        vFaceBuffer = new ComputeBuffer(VFaceCount, FaceStride);
        wFaceBuffer = new ComputeBuffer(WFaceCount, FaceStride);
        oldUFaceBuffer = new ComputeBuffer(UFaceCount, FaceStride);
        oldVFaceBuffer = new ComputeBuffer(VFaceCount, FaceStride);
        oldWFaceBuffer = new ComputeBuffer(WFaceCount, FaceStride);
        cellStateBuffer = new ComputeBuffer(GridCellCount, sizeof(int));
        divergenceBuffer = new ComputeBuffer(GridCellCount, sizeof(float));
        pressureABuffer = new ComputeBuffer(GridCellCount, sizeof(float));
        pressureBBuffer = new ComputeBuffer(GridCellCount, sizeof(float));
        obstacleSdfBuffer = new ComputeBuffer(GridCellCount, sizeof(float));
        positionCorrectionBuffer = new ComputeBuffer(particleCount, Float4Stride);

        positionBuffer.SetData(positions);
        velocityBuffer.SetData(velocities);
        RebuildObstacleField();

        if (spatialGridShader != null)
        {
            assignCellsKernel = spatialGridShader.FindKernel("AssignCells");
            clearCellsKernel = spatialGridShader.FindKernel("ClearCells");
            buildCellListsKernel = spatialGridShader.FindKernel("BuildCellLists");
            countNeighborsKernel = spatialGridShader.FindKernel("CountNeighbors");
            applyPressureKernel = spatialGridShader.FindKernel("ApplyPressure");
            computeViscosityKernel = spatialGridShader.FindKernel("ComputeViscosity");
            computeSeparationKernel = spatialGridShader.FindKernel("ComputeSeparation");

            spatialGridShader.SetBuffer(clearCellsKernel, "_CellCounts", cellCountBuffer);
            spatialGridShader.SetBuffer(clearCellsKernel, "_CellHeads", cellHeadBuffer);
            spatialGridShader.SetBuffer(buildCellListsKernel, "_ParticleCell", particleCellBuffer);
            spatialGridShader.SetBuffer(buildCellListsKernel, "_CellCounts", cellCountBuffer);
            spatialGridShader.SetBuffer(buildCellListsKernel, "_CellHeads", cellHeadBuffer);
            spatialGridShader.SetBuffer(buildCellListsKernel, "_ParticleNext", particleNextBuffer);
            spatialGridShader.SetInt("_GridCellCount", GridCellCount);
            spatialGridShader.SetBuffer(assignCellsKernel, "_Positions", positionBuffer);
            spatialGridShader.SetBuffer(assignCellsKernel, "_ParticleCell", particleCellBuffer);
            spatialGridShader.SetInt("_ParticleCount", particleCount);
            spatialGridShader.SetInt("_GridX", GridX);
            spatialGridShader.SetInt("_GridY", GridY);
            spatialGridShader.SetInt("_GridZ", GridZ);
            spatialGridShader.SetVector("_GridMin", new Vector4(gridMin.x, gridMin.y, gridMin.z, 0f));
            spatialGridShader.SetFloat("_CellSize", cellSize);
            spatialGridShader.SetBuffer(countNeighborsKernel, "_Positions", positionBuffer);
            spatialGridShader.SetBuffer(countNeighborsKernel, "_ParticleCell", particleCellBuffer);
            spatialGridShader.SetBuffer(countNeighborsKernel, "_CellHeads", cellHeadBuffer);
            spatialGridShader.SetBuffer(countNeighborsKernel, "_ParticleNext", particleNextBuffer);
            spatialGridShader.SetBuffer(countNeighborsKernel, "_ParticleNeighborCounts", neighborCountBuffer);
            spatialGridShader.SetFloat("_NeighborRadius", cellSize);
            spatialGridShader.SetBuffer(countNeighborsKernel, "_ParticleDensities", densityBuffer);
            spatialGridShader.SetFloat("_ParticleMass", ParticleMass);
            spatialGridShader.SetBuffer(countNeighborsKernel, "_ParticlePressures", pressureBuffer);
            spatialGridShader.SetBuffer(applyPressureKernel, "_Positions", positionBuffer);
            spatialGridShader.SetBuffer(applyPressureKernel, "_Velocities", velocityBuffer);
            spatialGridShader.SetBuffer(applyPressureKernel, "_CellHeads", cellHeadBuffer);
            spatialGridShader.SetBuffer(applyPressureKernel, "_ParticleNext", particleNextBuffer);
            spatialGridShader.SetBuffer(applyPressureKernel, "_ParticleDensities", densityBuffer);
            spatialGridShader.SetBuffer(applyPressureKernel, "_ParticlePressures", pressureBuffer);
            spatialGridShader.SetBuffer(computeViscosityKernel, "_Positions", positionBuffer);
            spatialGridShader.SetBuffer(computeViscosityKernel, "_Velocities", velocityBuffer);
            spatialGridShader.SetBuffer(computeViscosityKernel, "_CellHeads", cellHeadBuffer);
            spatialGridShader.SetBuffer(computeViscosityKernel, "_ParticleNext", particleNextBuffer);
            spatialGridShader.SetBuffer(computeViscosityKernel, "_ParticleDensities", densityBuffer);
            spatialGridShader.SetBuffer(computeViscosityKernel, "_ViscosityAccelerations", viscosityAccelerationBuffer);
            spatialGridShader.SetBuffer(applyPressureKernel, "_ViscosityAccelerations", viscosityAccelerationBuffer);
            spatialGridShader.SetBuffer(computeSeparationKernel, "_Positions", positionBuffer);
            spatialGridShader.SetBuffer(computeSeparationKernel, "_ParticleCell", particleCellBuffer);
            spatialGridShader.SetBuffer(computeSeparationKernel, "_CellHeads", cellHeadBuffer);
            spatialGridShader.SetBuffer(computeSeparationKernel, "_ParticleNext", particleNextBuffer);
            spatialGridShader.SetBuffer(computeSeparationKernel, "_PositionCorrections", positionCorrectionBuffer);
        }

        if (macGridShader != null)
        {
            clearFacesKernel = macGridShader.FindKernel("ClearFaces");
            particleToVKernel = macGridShader.FindKernel("ParticleToV");
            particleToUKernel = macGridShader.FindKernel("ParticleToU");
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

            macGridShader.SetBuffer(clearFacesKernel, "_UFaceData", uFaceBuffer);
            macGridShader.SetBuffer(clearFacesKernel, "_VFaceData", vFaceBuffer);
            macGridShader.SetBuffer(clearFacesKernel, "_WFaceData", wFaceBuffer);
            macGridShader.SetInt("_UFaceCount", UFaceCount);
            macGridShader.SetInt("_VFaceCount", VFaceCount);
            macGridShader.SetInt("_WFaceCount", WFaceCount);
            macGridShader.SetInt("_GridCellCount", GridCellCount);
            macGridShader.SetInt("_ParticleCount", particleCount);
            macGridShader.SetBuffer(particleToVKernel, "_Positions", positionBuffer);
            macGridShader.SetBuffer(particleToVKernel, "_Velocities", velocityBuffer);
            macGridShader.SetBuffer(particleToVKernel, "_CellHeads", cellHeadBuffer);
            macGridShader.SetBuffer(particleToVKernel, "_ParticleNext", particleNextBuffer);
            macGridShader.SetBuffer(particleToVKernel, "_VFaceData", vFaceBuffer);
            macGridShader.SetBuffer(particleToUKernel, "_Positions", positionBuffer);
            macGridShader.SetBuffer(particleToUKernel, "_Velocities", velocityBuffer);
            macGridShader.SetBuffer(particleToUKernel, "_CellHeads", cellHeadBuffer);
            macGridShader.SetBuffer(particleToUKernel, "_ParticleNext", particleNextBuffer);
            macGridShader.SetBuffer(particleToUKernel, "_UFaceData", uFaceBuffer);
            macGridShader.SetBuffer(particleToWKernel, "_Positions", positionBuffer);
            macGridShader.SetBuffer(particleToWKernel, "_Velocities", velocityBuffer);
            macGridShader.SetBuffer(particleToWKernel, "_CellHeads", cellHeadBuffer);
            macGridShader.SetBuffer(particleToWKernel, "_ParticleNext", particleNextBuffer);
            macGridShader.SetBuffer(particleToWKernel, "_WFaceData", wFaceBuffer);
            macGridShader.SetInt("_GridX", GridX);
            macGridShader.SetInt("_GridY", GridY);
            macGridShader.SetInt("_GridZ", GridZ);
            macGridShader.SetVector("_GridMin", new Vector4(gridMin.x, gridMin.y, gridMin.z, 0f));
            macGridShader.SetFloat("_CellSize", cellSize);
            macGridShader.SetBuffer(normalizeFacesKernel, "_UFaceData", uFaceBuffer);
            macGridShader.SetBuffer(normalizeFacesKernel, "_VFaceData", vFaceBuffer);
            macGridShader.SetBuffer(normalizeFacesKernel, "_WFaceData", wFaceBuffer);

            macGridShader.SetBuffer(classifyCellsKernel, "_CellCounts", cellCountBuffer);
            macGridShader.SetBuffer(classifyCellsKernel, "_ObstacleSdf", obstacleSdfBuffer);
            macGridShader.SetBuffer(classifyCellsKernel, "_CellStates", cellStateBuffer);
            macGridShader.SetBuffer(classifyCellsKernel, "_Divergence", divergenceBuffer);
            macGridShader.SetBuffer(classifyCellsKernel, "_PressureA", pressureABuffer);
            macGridShader.SetBuffer(classifyCellsKernel, "_PressureB", pressureBBuffer);

            BindCurrentFaces(applyBoundariesKernel);
            macGridShader.SetBuffer(applyBoundariesKernel, "_CellStates", cellStateBuffer);
            BindCurrentFaces(saveFacesKernel);
            BindOldFaces(saveFacesKernel);
            BindCurrentFaces(computeDivergenceKernel);
            macGridShader.SetBuffer(computeDivergenceKernel, "_CellStates", cellStateBuffer);
            macGridShader.SetBuffer(computeDivergenceKernel, "_Divergence", divergenceBuffer);

            BindPressure(jacobiAKernel);
            BindPressure(jacobiBKernel);
            macGridShader.SetBuffer(jacobiAKernel, "_Divergence", divergenceBuffer);
            macGridShader.SetBuffer(jacobiBKernel, "_Divergence", divergenceBuffer);
            BindCurrentFaces(projectFacesKernel);
            BindPressure(projectFacesKernel);
            BindCurrentFaces(gridToParticlesKernel);
            BindOldFaces(gridToParticlesKernel);
            macGridShader.SetBuffer(gridToParticlesKernel, "_Positions", positionBuffer);
            macGridShader.SetBuffer(gridToParticlesKernel, "_Velocities", velocityBuffer);
        }

        // 렌더링 연결
        particleProperties = new MaterialPropertyBlock();
        particleProperties.SetBuffer("_Positions", positionBuffer);

        // 커널 연결
        if (integrationShader != null)
        {
            integrateKernel = integrationShader.FindKernel("Integrate");
            applyForcesKernel = integrationShader.FindKernel("ApplyForces");
            integrationShader.SetBuffer(integrateKernel, "_Positions", positionBuffer);
            integrationShader.SetBuffer(integrateKernel, "_Velocities", velocityBuffer);
            integrationShader.SetBuffer(integrateKernel, "_ObstacleSdf", obstacleSdfBuffer);
            integrationShader.SetBuffer(integrateKernel, "_PositionCorrections", positionCorrectionBuffer);
            integrationShader.SetBuffer(applyForcesKernel, "_Velocities", velocityBuffer);
            integrationShader.SetInt("_ParticleCount", particleCount);
            integrationShader.SetInt("_GridX", GridX);
            integrationShader.SetInt("_GridY", GridY);
            integrationShader.SetInt("_GridZ", GridZ);
            integrationShader.SetVector("_GridMin", new Vector4(gridMin.x, gridMin.y, gridMin.z, 0f));
            Vector3 gridMax = gridMin + new Vector3(GridX, GridY, GridZ) * cellSize;
            integrationShader.SetVector("_GridMax", new Vector4(gridMax.x, gridMax.y, gridMax.z, 0f));
            integrationShader.SetFloat("_CellSize", cellSize);
        }
    }

    private void BindCurrentFaces(int kernel)
    {
        macGridShader.SetBuffer(kernel, "_UFaceData", uFaceBuffer);
        macGridShader.SetBuffer(kernel, "_VFaceData", vFaceBuffer);
        macGridShader.SetBuffer(kernel, "_WFaceData", wFaceBuffer);
    }

    private void BindOldFaces(int kernel)
    {
        macGridShader.SetBuffer(kernel, "_OldUFaceData", oldUFaceBuffer);
        macGridShader.SetBuffer(kernel, "_OldVFaceData", oldVFaceBuffer);
        macGridShader.SetBuffer(kernel, "_OldWFaceData", oldWFaceBuffer);
    }

    private void BindPressure(int kernel)
    {
        macGridShader.SetBuffer(kernel, "_CellStates", cellStateBuffer);
        macGridShader.SetBuffer(kernel, "_PressureA", pressureABuffer);
        macGridShader.SetBuffer(kernel, "_PressureB", pressureBBuffer);
    }

    // 물리 갱신
    private void FixedUpdate()
    {
        if (positionBuffer == null || integrationShader == null || spatialGridShader == null)
        {
            return;
        }

        int particleGroups = (particleCount + 63) / 64;
        int cellGroups = (GridCellCount + 63) / 64;
        int faceGroups = (Mathf.Max(UFaceCount, Mathf.Max(VFaceCount, WFaceCount)) + 63) / 64;
        float deltaTime = Time.fixedDeltaTime;
        float radius = Mathf.Clamp(collisionRadius, 0.001f, cellSize * 0.49f);

        integrationShader.SetFloat("_DeltaTime", deltaTime);
        integrationShader.SetFloat("_Gravity", gravity);
        integrationShader.SetVector("_ExternalAcceleration", externalAcceleration);
        integrationShader.SetFloat("_CollisionRadius", radius);
        integrationShader.SetFloat("_WallBounce", Mathf.Clamp01(wallBounce));

        // 셀 목록
        spatialGridShader.Dispatch(clearCellsKernel, cellGroups, 1, 1);
        spatialGridShader.Dispatch(assignCellsKernel, particleGroups, 1, 1);
        spatialGridShader.Dispatch(buildCellListsKernel, particleGroups, 1, 1);

        if (solverMode == SolverMode.Sph)
        {
            spatialGridShader.SetFloat("_RestDensity", restDensity);
            spatialGridShader.SetFloat("_PressureStiffness", pressureStiffness);
            spatialGridShader.Dispatch(countNeighborsKernel, particleGroups, 1, 1);
            spatialGridShader.SetFloat("_ViscosityStrength", viscosityStrength);
            spatialGridShader.Dispatch(computeViscosityKernel, particleGroups, 1, 1);
            spatialGridShader.SetFloat("_DeltaTime", deltaTime);
            spatialGridShader.Dispatch(applyPressureKernel, particleGroups, 1, 1);

            integrationShader.SetInt("_IntegrateForces", 1);
            integrationShader.SetInt("_ApplySeparation", 0);
            integrationShader.Dispatch(integrateKernel, particleGroups, 1, 1);
            return;
        }

        if (macGridShader == null)
        {
            return;
        }

        // 격자 속도
        integrationShader.Dispatch(applyForcesKernel, particleGroups, 1, 1);
        macGridShader.SetFloat("_DeltaTime", deltaTime);
        macGridShader.Dispatch(clearFacesKernel, faceGroups, 1, 1);
        macGridShader.Dispatch(particleToUKernel, (UFaceCount + 63) / 64, 1, 1);
        macGridShader.Dispatch(particleToVKernel, (VFaceCount + 63) / 64, 1, 1);
        macGridShader.Dispatch(particleToWKernel, (WFaceCount + 63) / 64, 1, 1);
        macGridShader.Dispatch(normalizeFacesKernel, faceGroups, 1, 1);
        macGridShader.Dispatch(classifyCellsKernel, cellGroups, 1, 1);
        macGridShader.Dispatch(applyBoundariesKernel, faceGroups, 1, 1);
        macGridShader.Dispatch(saveFacesKernel, faceGroups, 1, 1);
        macGridShader.Dispatch(computeDivergenceKernel, cellGroups, 1, 1);

        // 압력 보정
        bool pressureInA = true;
        for (int iteration = 0; iteration < pressureIterations; iteration++)
        {
            macGridShader.Dispatch(pressureInA ? jacobiAKernel : jacobiBKernel, cellGroups, 1, 1);
            pressureInA = !pressureInA;
        }

        macGridShader.SetInt("_PressureIsA", pressureInA ? 1 : 0);
        macGridShader.Dispatch(projectFacesKernel, faceGroups, 1, 1);
        macGridShader.Dispatch(applyBoundariesKernel, faceGroups, 1, 1);

        // 입자 속도
        macGridShader.SetFloat("_FlipRatio", flipRatio);
        macGridShader.SetFloat("_VelocityDamping", Mathf.Max(0f, velocityDamping));
        macGridShader.SetFloat("_MaxSpeed", Mathf.Min(maxSpeed, cellSize * 0.9f / Mathf.Max(deltaTime, 0.000001f)));
        macGridShader.Dispatch(gridToParticlesKernel, particleGroups, 1, 1);

        bool separate = separationStrength > 0f;
        if (separate)
        {
            spatialGridShader.SetFloat("_DeltaTime", deltaTime);
            spatialGridShader.SetFloat("_SeparationRadius", Mathf.Min(cellSize, radius * 2f));
            spatialGridShader.SetFloat("_SeparationStrength", separationStrength);
            spatialGridShader.Dispatch(computeSeparationKernel, particleGroups, 1, 1);
        }

        integrationShader.SetInt("_IntegrateForces", 0);
        integrationShader.SetInt("_ApplySeparation", separate ? 1 : 0);
        integrationShader.Dispatch(integrateKernel, particleGroups, 1, 1);
    }

    // 입자 렌더링
    private void Update()
    {
        if (positionBuffer == null || sphereMeshSource == null || sphereMeshSource.sharedMesh == null || particleMaterial == null)
        {
            return;
        }

        var drawParams = new RenderParams(particleMaterial)
        {
            matProps = particleProperties,
            worldBounds = new Bounds(gridMin + new Vector3(GridX, GridY, GridZ) * cellSize * 0.5f,
                new Vector3(GridX, GridY, GridZ) * cellSize + Vector3.one * collisionRadius * 2f)
        };

        Graphics.RenderMeshPrimitives(drawParams, sphereMeshSource.sharedMesh, 0, particleCount);
    }

    // 자원 해제
    private void OnDisable()
    {
        positionBuffer?.Release();
        velocityBuffer?.Release();
        particleCellBuffer?.Release();
        cellCountBuffer?.Release();
        cellHeadBuffer?.Release();
        particleNextBuffer?.Release();
        neighborCountBuffer?.Release();
        densityBuffer?.Release();
        pressureBuffer?.Release();
        viscosityAccelerationBuffer?.Release();
        uFaceBuffer?.Release();
        vFaceBuffer?.Release();
        wFaceBuffer?.Release();
        oldUFaceBuffer?.Release();
        oldVFaceBuffer?.Release();
        oldWFaceBuffer?.Release();
        cellStateBuffer?.Release();
        divergenceBuffer?.Release();
        pressureABuffer?.Release();
        pressureBBuffer?.Release();
        obstacleSdfBuffer?.Release();
        positionCorrectionBuffer?.Release();

        positionBuffer = null;
        velocityBuffer = null;
        particleCellBuffer = null;
        cellCountBuffer = null;
        cellHeadBuffer = null;
        particleNextBuffer = null;
        neighborCountBuffer = null;
        densityBuffer = null;
        pressureBuffer = null;
        viscosityAccelerationBuffer = null;
        uFaceBuffer = null;
        vFaceBuffer = null;
        wFaceBuffer = null;
        oldUFaceBuffer = null;
        oldVFaceBuffer = null;
        oldWFaceBuffer = null;
        cellStateBuffer = null;
        divergenceBuffer = null;
        pressureABuffer = null;
        pressureBBuffer = null;
        obstacleSdfBuffer = null;
        positionCorrectionBuffer = null;
        debugCellStates = null;
        particleProperties = null;
    }

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
        int maxCount = 0;
        int activeCells = 0;

        foreach (int count in counts)
        {
            total += count;
            maxCount = Mathf.Max(maxCount, count);
            if (count > 0)
            {
                activeCells++;
            }
        }

        Debug.Log($"셀 합계: {total}/{particleCount}, 점유 셀: {activeCells}, 최대 셀 인원: {maxCount}", this);
    }

    [ContextMenu("입자 상태 확인")]
    private void LogParticleState()
    {
        if (!Application.isPlaying || positionBuffer == null)
        {
            return;
        }

        var positions = new Vector4[particleCount];
        var velocities = new Vector4[particleCount];
        positionBuffer.GetData(positions);
        velocityBuffer.GetData(velocities);

        int invalid = 0;
        float highestSpeed = 0f;
        for (int i = 0; i < particleCount; i++)
        {
            Vector3 position = positions[i];
            Vector3 velocity = velocities[i];
            if (!float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z) ||
                !float.IsFinite(velocity.x) || !float.IsFinite(velocity.y) || !float.IsFinite(velocity.z))
            {
                invalid++;
                continue;
            }

            highestSpeed = Mathf.Max(highestSpeed, velocity.magnitude);
        }

        Debug.Log($"입자 {particleCount}개, 비정상 값: {invalid}, 최고 속도: {highestSpeed:F2}", this);
    }

    [ContextMenu("장애물 필드 갱신")]
    private void RebuildObstacleField()
    {
        if (obstacleSdfBuffer == null)
        {
            return;
        }

        obstacleSdfBuffer.SetData(FluidObstacleField.Build(gridMin, gridResolution, cellSize, staticObstacles));
    }

    [ContextMenu("격자 발산 확인")]
    private void LogGridDivergence()
    {
        if (!Application.isPlaying || solverMode != SolverMode.PicFlip || divergenceBuffer == null)
        {
            return;
        }

        macGridShader.Dispatch(computeDivergenceKernel, (GridCellCount + 63) / 64, 1, 1);
        var states = new int[GridCellCount];
        var divergence = new float[GridCellCount];
        cellStateBuffer.GetData(states);
        divergenceBuffer.GetData(divergence);

        int fluidCells = 0;
        float total = 0f;
        float maximum = 0f;
        for (int i = 0; i < GridCellCount; i++)
        {
            if (states[i] != 1)
            {
                continue;
            }

            float value = Mathf.Abs(divergence[i]);
            total += value;
            maximum = Mathf.Max(maximum, value);
            fluidCells++;
        }

        Debug.Log($"유체 셀: {fluidCells}, 투영 후 발산 평균/최대: {total / Mathf.Max(1, fluidCells):F4} / {maximum:F4}", this);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 size = new Vector3(Mathf.Max(2, gridResolution.x), Mathf.Max(2, gridResolution.y), Mathf.Max(2, gridResolution.z)) * Mathf.Max(0.05f, cellSize);
        if (drawGridBounds)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(gridMin + size * 0.5f, size);
        }

        if (!drawFluidCells || debugCellStates == null || debugCellStates.Length != GridCellCount)
        {
            return;
        }

        Vector3 cellExtent = Vector3.one * cellSize * 0.8f;
        for (int z = 0; z < GridZ; z++)
            for (int y = 0; y < GridY; y++)
                for (int x = 0; x < GridX; x++)
                {
                    int state = debugCellStates[x + GridX * (y + GridY * z)];
                    if (state == 0)
                    {
                        continue;
                    }

                    Gizmos.color = state == 1 ? Color.blue : Color.gray;
                    Gizmos.DrawWireCube(gridMin + new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * cellSize, cellExtent);
                }
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
                debugCellStates[i] = debugCellStates[i] > 0 ? 1 : 0;
        }
    }

    private void OnValidate()
    {
        if (!Application.isPlaying || positionBuffer == null)
        {
            return;
        }

        particleCount = allocatedParticleCount;
        gridResolution = allocatedGridResolution;
        gridMin = allocatedGridMin;
        cellSize = allocatedCellSize;
    }
}

internal static class FluidObstacleField
{
    public static float[] Build(Vector3 gridMin, Vector3Int resolution, float cellSize, Collider[] colliders)
    {
        int count = resolution.x * resolution.y * resolution.z;
        var field = new float[count];
        for (int i = 0; i < count; i++)
            field[i] = 10000f;

        if (colliders == null)
        {
            return field;
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
            if (meshCollider != null)
            {
                if (meshCollider.sharedMesh == null)
                {
                    continue;
                }

                try
                {
                    meshVertices = meshCollider.sharedMesh.vertices;
                    triangles = meshCollider.sharedMesh.triangles;
                }
                catch (UnityException)
                {
                    Debug.LogWarning($"메시 읽기 설정을 확인하세요: {meshCollider.name}", meshCollider);
                    continue;
                }

                for (int vertex = 0; vertex < meshVertices.Length; vertex++)
                    meshVertices[vertex] = meshCollider.transform.TransformPoint(meshVertices[vertex]);
            }
            else if (!(collider is BoxCollider) && !(collider is SphereCollider))
            {
                Debug.LogWarning($"지원하지 않는 장애물 Collider: {collider.name}", collider);
                continue;
            }

            for (int z = 0; z < resolution.z; z++)
                for (int y = 0; y < resolution.y; y++)
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
                        else
                        {
                            distance = MeshDistance(meshVertices, triangles, point, cellSize);
                        }

                        int index = x + resolution.x * (y + resolution.y * z);
                        field[index] = Mathf.Min(field[index], distance);
                    }
        }

        return field;
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