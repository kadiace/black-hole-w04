using UnityEngine;

public sealed class FluidSimulation : MonoBehaviour
{
    private const int Float4Stride = sizeof(float) * 4;

    [SerializeField] private int particleCount = 512;
    [SerializeField] private Vector3 spawnCenter = new Vector3(0f, 3f, 0f);
    [SerializeField] private Vector3 spawnSize = new Vector3(2f, 1f, 2f);
    [SerializeField] private MeshFilter sphereMeshSource;
    [SerializeField] private Material particleMaterial;
    [SerializeField] private ComputeShader integrationShader;
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float collisionRadius = 0.08f;
    private int integrateKernel = -1;
    private MaterialPropertyBlock particleProperties;

    private ComputeBuffer positionBuffer;
    private ComputeBuffer velocityBuffer;

    private void OnEnable()
    {
        var positions = new Vector4[particleCount];
        var velocities = new Vector4[particleCount];
        var random = new System.Random(1234);

        for (int i = 0; i < particleCount; i++)
        {
            var offset = new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f);

            Vector3 p = spawnCenter + Vector3.Scale(offset, spawnSize);
            positions[i] = new Vector4(p.x, p.y, p.z, 1f);
        }

        positionBuffer = new ComputeBuffer(particleCount, Float4Stride);
        velocityBuffer = new ComputeBuffer(particleCount, Float4Stride);

        positionBuffer.SetData(positions);
        velocityBuffer.SetData(velocities);

        particleProperties = new MaterialPropertyBlock();
        particleProperties.SetBuffer("_Positions", positionBuffer);

        if (integrationShader != null)
        {
            integrateKernel = integrationShader.FindKernel("Integrate");
            integrationShader.SetBuffer(integrateKernel, "_Positions", positionBuffer);
            integrationShader.SetBuffer(integrateKernel, "_Velocities", velocityBuffer);
            integrationShader.SetInt("_ParticleCount", particleCount);
        }

        Debug.Log($"particle ready: {particleCount}개", this);
    }

    private void FixedUpdate()
    {
        if (integrationShader == null || integrateKernel < 0 || positionBuffer == null)
        {
            return;
        }

        integrationShader.SetFloat("_DeltaTime", Time.fixedDeltaTime);
        integrationShader.SetFloat("_Gravity", gravity);
        integrationShader.SetFloat("_FloorY", 0f);
        integrationShader.SetFloat("_CollisionRadius", collisionRadius);
        integrationShader.Dispatch(integrateKernel, (particleCount + 63) / 64, 1, 1);
    }

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

    private void OnDisable()
    {
        positionBuffer?.Release();
        velocityBuffer?.Release();
        positionBuffer = null;
        velocityBuffer = null;
    }
}