using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class FluidSubmersionSystem : MonoBehaviour
{
    private struct SampleBatch
    {
        public Collider collider;
        public int firstSample;
        public int sliceCount;
        public int directionCount;
        public float[] sliceWeights;

        public SampleBatch(Collider collider, int firstSample, int sliceCount, int directionCount, float[] sliceWeights)
        {
            this.collider = collider;
            this.firstSample = firstSample;
            this.sliceCount = sliceCount;
            this.directionCount = directionCount;
            this.sliceWeights = sliceWeights;
        }
    }

    // 연결
    [SerializeField] private FluidSimulation fluidSimulation;
    [SerializeField] private ComputeShader submersionShader;

    // 측정 설정
    [SerializeField, Range(4, 32)] private int verticalSlices = 16;
    [SerializeField, Range(4, 16)] private int directionsPerSlice = 8;
    [SerializeField, Min(0.01f)] private float probeRadius = 0.4f;
    [SerializeField, Min(0.001f)] private float shellOffset = 0.16f;
    [SerializeField, Min(1)] private int minimumParticlesPerProbe = 1;
    [SerializeField, Min(1)] private int minimumWetDirections = 4;
    [SerializeField, Min(0.02f)] private float sampleInterval = 0.1f;

    // 측정 대상
    private readonly HashSet<Collider> targets = new HashSet<Collider>();
    private readonly Dictionary<Collider, float> submersionRatios = new Dictionary<Collider, float>();
    private readonly List<Collider> removedTargets = new List<Collider>();
    private readonly List<Vector4> samplePoints = new List<Vector4>();
    private readonly List<SampleBatch> sampleBatches = new List<SampleBatch>();

    // GPU 자원
    private ComputeBuffer sampleBuffer;
    private ComputeBuffer countBuffer;
    private AsyncGPUReadbackRequest readback;
    private int sampleKernel;
    private int sampleCapacity;
    private bool readbackPending;
    private bool ignoreReadback;
    private float nextSampleTime;

    // 대상 등록
    public void Register(Collider collider)
    {
        if (collider == null)
        {
            return;
        }

        targets.Add(collider);
        submersionRatios[collider] = 0f;
    }

    public void Unregister(Collider collider)
    {
        if (collider == null)
        {
            return;
        }

        targets.Remove(collider);
        submersionRatios.Remove(collider);
    }

    public float GetSubmersion(Collider collider)
    {
        if (collider != null && submersionRatios.TryGetValue(collider, out float ratio))
        {
            return ratio;
        }

        return 0f;
    }

    // 초기화
    private void OnEnable()
    {
        if (fluidSimulation == null || submersionShader == null)
        {
            Debug.LogError("유체 잠김 측정의 Fluid Simulation과 Submersion Shader를 연결하세요.", this);
            enabled = false;
            return;
        }

        sampleKernel = submersionShader.FindKernel("CountNearbyParticles");
        nextSampleTime = Time.time;
        fluidSimulation.ParticlePositionsUpdated += SampleParticles;
        fluidSimulation.SimulationStopped += HandleSimulationStopped;
    }

    // 입자 측정
    private void SampleParticles(ComputeBuffer positions, int particleCount)
    {
        if (readbackPending || Time.time < nextSampleTime)
        {
            return;
        }

        nextSampleTime = Time.time + Mathf.Max(0.02f, sampleInterval);

        if (particleCount <= 0)
        {
            ResetRatios();
            return;
        }

        samplePoints.Clear();
        sampleBatches.Clear();
        removedTargets.Clear();

        foreach (Collider target in targets)
        {
            if (target == null)
            {
                removedTargets.Add(target);
                continue;
            }

            if (!target.enabled || !target.gameObject.activeInHierarchy || target.isTrigger || target.bounds.size.y <= 0f)
            {
                submersionRatios[target] = 0f;
                continue;
            }

            AddSamples(target);
        }

        foreach (Collider target in removedTargets)
        {
            targets.Remove(target);
            submersionRatios.Remove(target);
        }

        if (samplePoints.Count == 0)
        {
            return;
        }

        EnsureBuffers(samplePoints.Count);
        sampleBuffer.SetData(samplePoints.ToArray());

        submersionShader.SetInt("_ParticleCount", particleCount);
        submersionShader.SetInt("_SampleCount", samplePoints.Count);
        submersionShader.SetBuffer(sampleKernel, "_Positions", positions);
        submersionShader.SetBuffer(sampleKernel, "_Samples", sampleBuffer);
        submersionShader.SetBuffer(sampleKernel, "_SampleCounts", countBuffer);
        submersionShader.Dispatch(sampleKernel, (samplePoints.Count + 63) / 64, 1, 1);

        readback = AsyncGPUReadback.Request(countBuffer);
        readbackPending = true;
        ignoreReadback = false;
    }

    // 외곽 측정점
    private void AddSamples(Collider collider)
    {
        Bounds bounds = collider.bounds;
        int firstSample = samplePoints.Count;
        float[] weights = new float[verticalSlices];
        float distance = Mathf.Max(bounds.extents.x, bounds.extents.z) + probeRadius + shellOffset + 0.01f;

        for (int slice = 0; slice < verticalSlices; slice++)
        {
            float heightRatio = (slice + 0.5f) / verticalSlices;
            float y = Mathf.Lerp(bounds.min.y, bounds.max.y, heightRatio);
            weights[slice] = SliceWeight(collider, y);

            for (int directionIndex = 0; directionIndex < directionsPerSlice; directionIndex++)
            {
                float angle = directionIndex * Mathf.PI * 2f / directionsPerSlice;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 outer = new Vector3(bounds.center.x, y, bounds.center.z) + direction * distance;
                Vector3 surface = collider.ClosestPoint(outer);
                Vector3 outward = outer - surface;

                if (outward.sqrMagnitude < 0.000001f)
                {
                    outward = direction;
                }

                Vector3 sample = surface + outward.normalized * shellOffset;
                samplePoints.Add(new Vector4(sample.x, sample.y, sample.z, probeRadius));
            }
        }

        sampleBatches.Add(new SampleBatch(collider, firstSample, verticalSlices, directionsPerSlice, weights));
    }

    // 높이별 가중치
    private static float SliceWeight(Collider collider, float y)
    {
        if (collider is SphereCollider sphere)
        {
            Vector3 scale = sphere.transform.lossyScale;
            float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            float offset = y - sphere.transform.TransformPoint(sphere.center).y;
            return Mathf.Max(0f, radius * radius - offset * offset);
        }

        if (collider is CapsuleCollider capsule)
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
            float lowestSegmentY = Mathf.Min(start.y, end.y);
            float highestSegmentY = Mathf.Max(start.y, end.y);
            float offset = Mathf.Max(lowestSegmentY - y, y - highestSegmentY, 0f);

            return Mathf.Max(0f, radius * radius - offset * offset);
        }

        Bounds bounds = collider.bounds;
        return Mathf.Max(0.0001f, bounds.size.x * bounds.size.z);
    }

    // 버퍼 크기
    private void EnsureBuffers(int sampleCount)
    {
        if (sampleCapacity >= sampleCount)
        {
            return;
        }

        sampleBuffer?.Release();
        countBuffer?.Release();

        sampleCapacity = Mathf.NextPowerOfTwo(sampleCount);
        sampleBuffer = new ComputeBuffer(sampleCapacity, sizeof(float) * 4);
        countBuffer = new ComputeBuffer(sampleCapacity, sizeof(uint));
    }

    // 측정 결과
    private void Update()
    {
        if (!readbackPending || !readback.done)
        {
            return;
        }

        readbackPending = false;

        if (ignoreReadback || readback.hasError)
        {
            ignoreReadback = false;
            ResetRatios();
            return;
        }

        var counts = readback.GetData<uint>();
        int requiredDirections = Mathf.Clamp(minimumWetDirections, 1, directionsPerSlice);

        foreach (SampleBatch batch in sampleBatches)
        {
            if (batch.collider == null || !targets.Contains(batch.collider))
            {
                continue;
            }

            float submergedWeight = 0f;
            float totalWeight = 0f;

            for (int slice = 0; slice < batch.sliceCount; slice++)
            {
                int wetDirections = 0;

                for (int direction = 0; direction < batch.directionCount; direction++)
                {
                    int index = batch.firstSample + slice * batch.directionCount + direction;

                    if (counts[index] >= (uint)minimumParticlesPerProbe)
                    {
                        wetDirections++;
                    }
                }

                float coverage = Mathf.Clamp01((float)wetDirections / requiredDirections);
                float weight = batch.sliceWeights[slice];
                submergedWeight += coverage * weight;
                totalWeight += weight;
            }

            submersionRatios[batch.collider] = totalWeight > 0f ? submergedWeight / totalWeight : 0f;
        }
    }

    // 상태 초기화
    private void HandleSimulationStopped()
    {
        ignoreReadback = true;
        ResetRatios();
    }

    private void ResetRatios()
    {
        foreach (Collider target in targets)
        {
            if (target != null)
            {
                submersionRatios[target] = 0f;
            }
        }
    }

    // 자원 해제
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

        sampleBuffer?.Release();
        countBuffer?.Release();
        sampleBuffer = null;
        countBuffer = null;
        sampleCapacity = 0;
        ResetRatios();
    }
}