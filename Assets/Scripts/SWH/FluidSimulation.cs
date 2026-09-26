using UnityEngine;

public sealed class FluidSimulation : MonoBehaviour
{
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
    [SerializeField] private float wallHalfExtent = 4f;
    [SerializeField] private float wallBounce = 0.5f;
    [SerializeField] private float restDensity = 400f;
    [SerializeField] private float pressureStiffness = 1f;
    [SerializeField] private float viscosityStrength = 5f;

    // GPU 자원
    private MaterialPropertyBlock particleProperties;
    private ComputeBuffer positionBuffer;
    private ComputeBuffer velocityBuffer;
    private ComputeBuffer particleCellBuffer;
    private ComputeBuffer cellParticleIndicesBuffer;
    private ComputeBuffer cellCountBuffer;
    private ComputeBuffer neighborCountBuffer;
    private ComputeBuffer densityBuffer;
    private ComputeBuffer pressureBuffer;
    private ComputeBuffer viscosityAccelerationBuffer;
    private ComputeBuffer uFaceBuffer;
    private ComputeBuffer vFaceBuffer;
    private ComputeBuffer wFaceBuffer;

    private const int GridX = 20;
    private const int GridY = 10;
    private const int GridZ = 20;
    private const int GridCellCount = GridX * GridY * GridZ;
    private const int FaceStride = sizeof(float) * 2;
    private const int UFaceCount = (GridX + 1) * GridY * GridZ;
    private const int VFaceCount = GridX * (GridY + 1) * GridZ;
    private const int WFaceCount = GridX * GridY * (GridZ + 1);
    private const float NeighborRadius = 0.4f;
    private const int CellCapacity = 64;
    private const float ParticleMass = 1f;
    private int integrateKernel = -1;
    private int countNeighborsKernel = -1;
    private int assignCellsKernel = -1;
    private int clearCellsKernel = -1;
    private int buildCellListsKernel = -1;
    private int applyPressureKernel = -1;
    private int computeViscosityKernel = -1;
    private int clearFacesKernel = -1;
    private int particleToVKernel = -1;
    private int particleToUKernel = -1;
    private int particleToWKernel = -1;
    private int normalizeFacesKernel = -1;

    // 입자 초기화
    private void OnEnable()
    {
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
        cellParticleIndicesBuffer = new ComputeBuffer(GridCellCount * CellCapacity, sizeof(int));
        neighborCountBuffer = new ComputeBuffer(particleCount, sizeof(int));
        densityBuffer = new ComputeBuffer(particleCount, sizeof(float));
        pressureBuffer = new ComputeBuffer(particleCount, sizeof(float));
        viscosityAccelerationBuffer = new ComputeBuffer(particleCount, Float4Stride);
        uFaceBuffer = new ComputeBuffer(UFaceCount, FaceStride);
        vFaceBuffer = new ComputeBuffer(VFaceCount, FaceStride);
        wFaceBuffer = new ComputeBuffer(WFaceCount, FaceStride);


        positionBuffer.SetData(positions);
        velocityBuffer.SetData(velocities);

        if (spatialGridShader != null)
        {
            assignCellsKernel = spatialGridShader.FindKernel("AssignCells");
            clearCellsKernel = spatialGridShader.FindKernel("ClearCells");
            buildCellListsKernel = spatialGridShader.FindKernel("BuildCellLists");
            countNeighborsKernel = spatialGridShader.FindKernel("CountNeighbors");
            applyPressureKernel = spatialGridShader.FindKernel("ApplyPressure");
            computeViscosityKernel = spatialGridShader.FindKernel("ComputeViscosity");

            spatialGridShader.SetBuffer(clearCellsKernel, "_CellCounts", cellCountBuffer);
            spatialGridShader.SetBuffer(buildCellListsKernel, "_ParticleCell", particleCellBuffer);
            spatialGridShader.SetBuffer(buildCellListsKernel, "_CellCounts", cellCountBuffer);
            spatialGridShader.SetBuffer(buildCellListsKernel, "_CellParticleIndices", cellParticleIndicesBuffer);
            spatialGridShader.SetInt("_GridCellCount", GridCellCount);
            spatialGridShader.SetInt("_CellCapacity", CellCapacity);
            spatialGridShader.SetBuffer(assignCellsKernel, "_Positions", positionBuffer);
            spatialGridShader.SetBuffer(assignCellsKernel, "_ParticleCell", particleCellBuffer);
            spatialGridShader.SetInt("_ParticleCount", particleCount);
            spatialGridShader.SetInt("_GridX", GridX);
            spatialGridShader.SetInt("_GridY", GridY);
            spatialGridShader.SetInt("_GridZ", GridZ);
            spatialGridShader.SetVector("_GridMin", new Vector4(-4f, 0f, -4f, 0f));
            spatialGridShader.SetFloat("_CellSize", 0.4f);
            spatialGridShader.SetBuffer(countNeighborsKernel, "_Positions", positionBuffer);
            spatialGridShader.SetBuffer(countNeighborsKernel, "_ParticleCell", particleCellBuffer);
            spatialGridShader.SetBuffer(countNeighborsKernel, "_CellCounts", cellCountBuffer);
            spatialGridShader.SetBuffer(countNeighborsKernel, "_CellParticleIndices", cellParticleIndicesBuffer);
            spatialGridShader.SetBuffer(countNeighborsKernel, "_ParticleNeighborCounts", neighborCountBuffer);
            spatialGridShader.SetFloat("_NeighborRadius", NeighborRadius);
            spatialGridShader.SetBuffer(countNeighborsKernel, "_ParticleDensities", densityBuffer);
            spatialGridShader.SetFloat("_ParticleMass", ParticleMass);
            spatialGridShader.SetBuffer(countNeighborsKernel, "_ParticlePressures", pressureBuffer);
            spatialGridShader.SetBuffer(applyPressureKernel, "_Positions", positionBuffer);
            spatialGridShader.SetBuffer(applyPressureKernel, "_Velocities", velocityBuffer);
            spatialGridShader.SetBuffer(applyPressureKernel, "_CellCounts", cellCountBuffer);
            spatialGridShader.SetBuffer(applyPressureKernel, "_CellParticleIndices", cellParticleIndicesBuffer);
            spatialGridShader.SetBuffer(applyPressureKernel, "_ParticleDensities", densityBuffer);
            spatialGridShader.SetBuffer(applyPressureKernel, "_ParticlePressures", pressureBuffer);
            spatialGridShader.SetBuffer(computeViscosityKernel, "_Positions", positionBuffer);
            spatialGridShader.SetBuffer(computeViscosityKernel, "_Velocities", velocityBuffer);
            spatialGridShader.SetBuffer(computeViscosityKernel, "_CellCounts", cellCountBuffer);
            spatialGridShader.SetBuffer(computeViscosityKernel, "_CellParticleIndices", cellParticleIndicesBuffer);
            spatialGridShader.SetBuffer(computeViscosityKernel, "_ParticleDensities", densityBuffer);
            spatialGridShader.SetBuffer(computeViscosityKernel, "_ViscosityAccelerations", viscosityAccelerationBuffer);
            spatialGridShader.SetBuffer(applyPressureKernel, "_ViscosityAccelerations", viscosityAccelerationBuffer);
        }

        if (macGridShader != null)
        {
            clearFacesKernel = macGridShader.FindKernel("ClearFaces");
            particleToVKernel = macGridShader.FindKernel("ParticleToV");
            particleToUKernel = macGridShader.FindKernel("ParticleToU");
            particleToWKernel = macGridShader.FindKernel("ParticleToW");
            normalizeFacesKernel = macGridShader.FindKernel("NormalizeFaces");            

            macGridShader.SetBuffer(clearFacesKernel, "_UFaceData", uFaceBuffer);
            macGridShader.SetBuffer(clearFacesKernel, "_VFaceData", vFaceBuffer);
            macGridShader.SetBuffer(clearFacesKernel, "_WFaceData", wFaceBuffer);
            macGridShader.SetInt("_UFaceCount", UFaceCount);
            macGridShader.SetInt("_VFaceCount", VFaceCount);
            macGridShader.SetInt("_WFaceCount", WFaceCount);
            macGridShader.SetBuffer(particleToVKernel, "_Positions", positionBuffer);
            macGridShader.SetBuffer(particleToVKernel, "_Velocities", velocityBuffer);
            macGridShader.SetBuffer(particleToVKernel, "_CellCounts", cellCountBuffer);
            macGridShader.SetBuffer(particleToVKernel, "_CellParticleIndices", cellParticleIndicesBuffer);
            macGridShader.SetBuffer(particleToVKernel, "_VFaceData", vFaceBuffer);
            macGridShader.SetBuffer(particleToUKernel, "_Positions", positionBuffer);
            macGridShader.SetBuffer(particleToUKernel, "_Velocities", velocityBuffer);
            macGridShader.SetBuffer(particleToUKernel, "_CellCounts", cellCountBuffer);
            macGridShader.SetBuffer(particleToUKernel, "_CellParticleIndices", cellParticleIndicesBuffer);
            macGridShader.SetBuffer(particleToUKernel, "_UFaceData", uFaceBuffer);
            macGridShader.SetBuffer(particleToWKernel, "_Positions", positionBuffer);
            macGridShader.SetBuffer(particleToWKernel, "_Velocities", velocityBuffer);
            macGridShader.SetBuffer(particleToWKernel, "_CellCounts", cellCountBuffer);
            macGridShader.SetBuffer(particleToWKernel, "_CellParticleIndices", cellParticleIndicesBuffer);
            macGridShader.SetBuffer(particleToWKernel, "_WFaceData", wFaceBuffer);
            macGridShader.SetInt("_GridX", GridX);
            macGridShader.SetInt("_GridY", GridY);
            macGridShader.SetInt("_GridZ", GridZ);
            macGridShader.SetInt("_CellCapacity", CellCapacity);
            macGridShader.SetVector("_GridMin", new Vector4(-4f, 0f, -4f, 0f));
            macGridShader.SetFloat("_CellSize", 0.4f);
            macGridShader.SetBuffer(normalizeFacesKernel, "_UFaceData", uFaceBuffer);
            macGridShader.SetBuffer(normalizeFacesKernel, "_VFaceData", vFaceBuffer);
            macGridShader.SetBuffer(normalizeFacesKernel, "_WFaceData", wFaceBuffer);
        }

        // 렌더링 연결
        particleProperties = new MaterialPropertyBlock();
        particleProperties.SetBuffer("_Positions", positionBuffer);

        // 커널 연결
        if (integrationShader != null)
        {
            integrateKernel = integrationShader.FindKernel("Integrate");
            integrationShader.SetBuffer(integrateKernel, "_Positions", positionBuffer);
            integrationShader.SetBuffer(integrateKernel, "_Velocities", velocityBuffer);
            integrationShader.SetInt("_ParticleCount", particleCount);
        }
    }

    // 물리 갱신
    private void FixedUpdate()
    {
        if (macGridShader != null && clearFacesKernel >= 0)
        {
            int maxFaces = Mathf.Max(UFaceCount, Mathf.Max(VFaceCount, WFaceCount));
            macGridShader.Dispatch(clearFacesKernel, (maxFaces + 63) / 64, 1, 1);
        }

        if (integrationShader == null || integrateKernel < 0 || positionBuffer == null)
        {
            return;
        }

        // 물리 매개변수
        integrationShader.SetFloat("_DeltaTime", Time.fixedDeltaTime);
        integrationShader.SetFloat("_Gravity", gravity);
        integrationShader.SetFloat("_FloorY", 0f);
        integrationShader.SetFloat("_CollisionRadius", collisionRadius);
        integrationShader.SetFloat("_WallHalfExtent", wallHalfExtent);
        integrationShader.SetFloat("_WallBounce", wallBounce);

        // GPU 실행
        if (spatialGridShader != null && clearCellsKernel >= 0 && assignCellsKernel >= 0 && buildCellListsKernel >= 0 && countNeighborsKernel >= 0 && computeViscosityKernel >= 0 && applyPressureKernel >= 0)
        {
            spatialGridShader.Dispatch(clearCellsKernel, (GridCellCount + 63) / 64, 1, 1);
            spatialGridShader.Dispatch(assignCellsKernel, (particleCount + 63) / 64, 1, 1);
            spatialGridShader.Dispatch(buildCellListsKernel, (particleCount + 63) / 64, 1, 1);

            spatialGridShader.SetFloat("_RestDensity", restDensity);
            spatialGridShader.SetFloat("_PressureStiffness", pressureStiffness);
            spatialGridShader.Dispatch(countNeighborsKernel, (particleCount + 63) / 64, 1, 1);

            spatialGridShader.SetFloat("_ViscosityStrength", viscosityStrength);
            spatialGridShader.Dispatch(computeViscosityKernel, (particleCount + 63) / 64, 1, 1);

            spatialGridShader.SetFloat("_DeltaTime", Time.fixedDeltaTime);
            spatialGridShader.Dispatch(applyPressureKernel, (particleCount + 63) / 64, 1, 1);

            if (macGridShader != null)
            {
                if (particleToUKernel >= 0)
                {
                    macGridShader.Dispatch(particleToUKernel, (UFaceCount + 63) / 64, 1, 1);
                }

                if (particleToVKernel >= 0)
                {
                    macGridShader.Dispatch(particleToVKernel, (VFaceCount + 63) / 64, 1, 1);
                }

                if (particleToWKernel >= 0)
                {
                    macGridShader.Dispatch(particleToWKernel, (WFaceCount + 63) / 64, 1, 1);
                }

                if (normalizeFacesKernel >= 0)
                {
                    int maxFaces = Mathf.Max(UFaceCount, Mathf.Max(VFaceCount, WFaceCount));
                    macGridShader.Dispatch(normalizeFacesKernel, (maxFaces + 63) / 64, 1, 1);
                }
            }
        }

        integrationShader.Dispatch(integrateKernel, (particleCount + 63) / 64, 1, 1);
    }

    // 입자 렌더링
    private void Update()
    {
        if (positionBuffer == null || sphereMeshSource == null || sphereMeshSource.sharedMesh == null || particleMaterial == null)
            return;

        var drawParams = new RenderParams(particleMaterial)
        {
            matProps = particleProperties,
            worldBounds = new Bounds(new Vector3(0f, 2f, 0f), new Vector3(9f, 5f, 9f))
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
        cellParticleIndicesBuffer?.Release();
        neighborCountBuffer?.Release();
        densityBuffer?.Release();
        pressureBuffer?.Release();
        viscosityAccelerationBuffer?.Release();
        uFaceBuffer?.Release();
        vFaceBuffer?.Release();
        wFaceBuffer?.Release();

        positionBuffer = null;
        velocityBuffer = null;
        particleCellBuffer = null;
        cellCountBuffer = null;
        cellParticleIndicesBuffer = null;
        neighborCountBuffer = null;
        densityBuffer = null;
        pressureBuffer = null;
        viscosityAccelerationBuffer = null;
        uFaceBuffer = null;
        vFaceBuffer = null;
        wFaceBuffer = null;
    }

    [ContextMenu("셀 용량 확인")]
    private void LogCellCapacity()
    {
        if (!Application.isPlaying || cellCountBuffer == null) return;

        var counts = new int[GridCellCount];
        cellCountBuffer.GetData(counts);

        int total = 0;
        int maxCount = 0;
        int omitted = 0;

        foreach (int count in counts)
        {
            total += count;
            maxCount = Mathf.Max(maxCount, count);
            omitted += Mathf.Max(0, count - CellCapacity);
        }

        Debug.Log($"셀 합계: {total}/{particleCount}, 최대: {maxCount}/{CellCapacity}, 누락: {omitted}", this);
    }
}
