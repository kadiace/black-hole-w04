using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class FluidLevelSensor : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private FluidSimulation fluidSimulation;
    [SerializeField] private ComputeShader sensorShader;
    [SerializeField] private LogicBase logicTarget;

    [Header("감지 영역")]
    [SerializeField, Min(0.01f)] private float radius = 1f;
    [SerializeField, Min(0.01f)] private float halfHeight = 0.2f;

    [Header("판정")]
    [SerializeField, Min(1)] private int minimumParticles = 8;
    [SerializeField, Range(1, 4)] private int minimumQuadrants = 3;
    [SerializeField, Min(1)] private int minimumParticlesPerQuadrant = 1;
    [SerializeField, Min(0.02f)] private float sampleInterval = 0.1f;
    [SerializeField, Min(0f)] private float onDelay = 0.75f;
    [SerializeField, Min(0f)] private float offDelay = 0.3f;

    public bool IsFilled { get; private set; }
    public bool IsOnDelayCounting => isActiveAndEnabled && candidateFilled && !IsFilled;

    public float OnDelayProgress
    {
        get
        {
            if (!IsOnDelayCounting)
            {
                return 0f;
            }

            if (onDelay <= 0f)
            {
                return 1f;
            }

            return Mathf.Clamp01((Time.time - candidateSince) / onDelay);
        }
    }

    private ComputeBuffer countBuffer;
    private AsyncGPUReadbackRequest readback;
    private int clearKernel;
    private int countKernel;
    private bool readbackPending;
    private bool ignoreReadback;
    private bool candidateFilled;
    private float candidateSince;
    private float nextSampleTime;

    // 센서 준비
    private void OnEnable()
    {
        if (fluidSimulation == null || sensorShader == null)
        {
            Debug.LogError("유체 수위 센서의 연결을 확인하세요.", this);
            enabled = false;
            return;
        }

        clearKernel = sensorShader.FindKernel("ClearCounts");
        countKernel = sensorShader.FindKernel("CountParticles");
        countBuffer = new ComputeBuffer(5, sizeof(uint));

        IsFilled = false;
        candidateFilled = false;
        candidateSince = Time.time;
        nextSampleTime = Time.time;

        fluidSimulation.ParticlePositionsUpdated += SampleParticles;
        fluidSimulation.SimulationStopped += HandleSimulationStopped;

        Debug.Log("유체 수위: OFF", this);
        logicTarget?.SetActive(IsFilled);
    }

    // 입자 집계
    private void SampleParticles(ComputeBuffer positions, int particleCount)
    {
        if (readbackPending || Time.time < nextSampleTime)
        {
            return;
        }

        nextSampleTime = Time.time + Mathf.Max(0.02f, sampleInterval);

        if (particleCount <= 0)
        {
            Evaluate(false);
            return;
        }

        sensorShader.SetBuffer(clearKernel, "_Counts", countBuffer);
        sensorShader.Dispatch(clearKernel, 1, 1, 1);

        sensorShader.SetBuffer(countKernel, "_Positions", positions);
        sensorShader.SetBuffer(countKernel, "_Counts", countBuffer);
        sensorShader.SetInt("_ParticleCount", particleCount);
        sensorShader.SetMatrix("_WorldToSensor", transform.worldToLocalMatrix);
        sensorShader.SetFloat("_SensorRadius", radius);
        sensorShader.SetFloat("_SensorHalfHeight", halfHeight);
        sensorShader.Dispatch(countKernel, (particleCount + 63) / 64, 1, 1);

        readback = AsyncGPUReadback.Request(countBuffer);
        readbackPending = true;
        ignoreReadback = false;
    }

    // 집계 결과
    private void Update()
    {
        if (!readbackPending || !readback.done)
        {
            return;
        }

        readbackPending = false;

        if (ignoreReadback)
        {
            ignoreReadback = false;
            return;
        }

        if (readback.hasError)
        {
            Evaluate(false);
            return;
        }

        var counts = readback.GetData<uint>();
        int occupiedQuadrants = 0;

        for (int i = 1; i <= 4; i++)
        {
            if (counts[i] >= (uint)minimumParticlesPerQuadrant)
            {
                occupiedQuadrants++;
            }
        }

        bool filled = counts[0] >= (uint)minimumParticles && occupiedQuadrants >= minimumQuadrants;
        Evaluate(filled);
    }

    // 상태 판정
    private void Evaluate(bool filled)
    {
        if (candidateFilled != filled)
        {
            candidateFilled = filled;
            candidateSince = Time.time;
        }

        if (IsFilled == filled)
        {
            return;
        }

        float delay = filled ? onDelay : offDelay;

        if (Time.time - candidateSince >= delay)
        {
            SetFilled(filled);
        }
    }

    // 상태 출력
    private void SetFilled(bool filled)
    {
        if (IsFilled == filled)
        {
            return;
        }

        IsFilled = filled;

        logicTarget?.SetActive(IsFilled);
        Debug.Log(filled ? "유체 수위: ON" : "유체 수위: OFF", this);
    }

    // 시뮬레이션 종료
    private void HandleSimulationStopped()
    {
        ignoreReadback = true;
        candidateFilled = false;
        candidateSince = Time.time;
        SetFilled(false);
    }

    // 센서 정리
    private void OnDisable()
    {
        if (fluidSimulation != null)
        {
            fluidSimulation.ParticlePositionsUpdated -= SampleParticles;
            fluidSimulation.SimulationStopped -= HandleSimulationStopped;
        }

        if (readbackPending)
        {
            readback.WaitForCompletion();
            readbackPending = false;
        }

        countBuffer?.Release();
        countBuffer = null;
        SetFilled(false);
    }

    // 감지 영역 표시
    private void OnDrawGizmosSelected()
    {
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = Color.cyan;

        for (int i = 0; i < 16; i++)
        {
            float angleA = i * Mathf.PI * 2f / 16f;
            float angleB = (i + 1) * Mathf.PI * 2f / 16f;

            Vector3 topA = new Vector3(Mathf.Cos(angleA) * radius, halfHeight, Mathf.Sin(angleA) * radius);
            Vector3 topB = new Vector3(Mathf.Cos(angleB) * radius, halfHeight, Mathf.Sin(angleB) * radius);
            Vector3 bottomA = new Vector3(topA.x, -halfHeight, topA.z);
            Vector3 bottomB = new Vector3(topB.x, -halfHeight, topB.z);

            Gizmos.DrawLine(topA, topB);
            Gizmos.DrawLine(bottomA, bottomB);

            if (i % 4 == 0)
            {
                Gizmos.DrawLine(topA, bottomA);
            }
        }

        Gizmos.matrix = previousMatrix;
    }
}