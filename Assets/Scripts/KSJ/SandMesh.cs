using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A height-field sand surface.
///
/// The original mouse sculpting and repose relaxation are kept here. The
/// public AddSandAtWorld API is the bridge for falling sand and other systems.
/// A deposit outside an existing patch can create a small runtime patch on
/// the floor, so an empty floor does not need to be authored as a SandMesh.
/// </summary>
public class SandMesh : MonoBehaviour
{
    [Range(1.5f, 5f)]
    [SerializeField]
    private float radius = 2f;

    [Range(1.5f, 5f)]
    [SerializeField]
    private float deformationStength = 2f;

    [Header("Existing relaxation")]
    [SerializeField]
    private float simulateTime = 2f;
    [SerializeField]
    private int xSize;
    [SerializeField]
    private int zSize;
    [SerializeField]
    private float reposeAngle = 30f;
    [SerializeField]
    private float divid;
    [SerializeField]
    private float flowRate = 1f;

    [Header("Runtime sand")]
    [SerializeField]
    private bool receiveRuntimeSand = true;
    [SerializeField]
    private bool createPatchOnEmptyFloor = true;
    [SerializeField, Min(1)]
    private int runtimePatchXSize = 16;
    [SerializeField, Min(1)]
    private int runtimePatchZSize = 16;
    [SerializeField, Min(0.01f)]
    private float runtimePatchCellSize = 0.5f;
    [SerializeField]
    private LayerMask floorMask = ~0;
    [SerializeField, Min(0.1f)]
    private float floorRayDistance = 100f;

    [Header("Edge spill")]
    [SerializeField]
    private bool spillAtEdges = true;
    [SerializeField, Min(0f)]
    private float spillRate = 1f;
    [SerializeField, Min(0f)]
    private float spillHeightThreshold = 0.01f;
    [SerializeField, Min(0f)]
    private float floorLocalHeight;

    private Mesh m_mesh;
    private MeshFilter m_meshFilter;
    private MeshCollider m_meshCollider;
    private MeshRenderer m_meshRenderer;
    private Vector3[] m_verticies;
    private Vector3[] m_modifiedVerts;
    private Vector2[] m_flowDirections;
    private float[] heightChange;

    private float m_timer;
    private int m_stride;
    private float m_spacingX = 1f;
    private float m_spacingZ = 1f;
    private float m_minX;
    private float m_maxX;
    private float m_minZ;
    private float m_maxZ;
    private bool m_meshDirty;
    private bool m_isInitialized;

    private static readonly List<SandMesh> s_instances = new List<SandMesh>();

    private void OnEnable()
    {
        if (!s_instances.Contains(this))
            s_instances.Add(this);
    }

    private void OnDisable()
    {
        s_instances.Remove(this);
    }

    private void OnDestroy()
    {
        s_instances.Remove(this);
    }

    private void Start()
    {
        EnsureInitialized();
    }

    private bool EnsureInitialized()
    {
        if (m_isInitialized)
            return true;

        m_meshFilter = GetComponentInChildren<MeshFilter>();
        if (m_meshFilter == null || m_meshFilter.sharedMesh == null)
            return false;

        // Do not instantiate MeshFilter.mesh while the editor is inspecting a
        // temporary object. Runtime play uses the instance mesh; edit-mode
        // tools can safely operate on the shared mesh and destroy the object
        // before anything is saved.
        m_mesh = Application.isPlaying ? m_meshFilter.mesh : m_meshFilter.sharedMesh;
        m_meshCollider = GetComponentInChildren<MeshCollider>();
        m_meshRenderer = GetComponentInChildren<MeshRenderer>();
        m_verticies = m_mesh.vertices;
        if (m_verticies == null || m_verticies.Length == 0)
            return false;

        m_modifiedVerts = (Vector3[])m_verticies.Clone();
        int expectedVertexCount = (xSize + 1) * (zSize + 1);
        if (xSize < 1 || zSize < 1 || expectedVertexCount != m_modifiedVerts.Length)
            InferGridDimensions();

        m_stride = xSize + 1;
        m_flowDirections = new Vector2[m_modifiedVerts.Length];
        heightChange = new float[m_modifiedVerts.Length];
        CalculateGridBounds();
        m_mesh.MarkDynamic();
        m_meshDirty = false;
        m_isInitialized = true;
        return true;
    }

    private void InferGridDimensions()
    {
        int vertexCount = m_modifiedVerts.Length;
        int inferredStride = Mathf.RoundToInt(Mathf.Sqrt(vertexCount));
        while (inferredStride > 1 && vertexCount % inferredStride != 0)
            inferredStride--;

        if (inferredStride < 2)
            inferredStride = vertexCount;

        xSize = Mathf.Max(1, inferredStride - 1);
        zSize = Mathf.Max(1, vertexCount / inferredStride - 1);
    }

    private void CalculateGridBounds()
    {
        m_minX = m_maxX = m_modifiedVerts[0].x;
        m_minZ = m_maxZ = m_modifiedVerts[0].z;
        for (int i = 1; i < m_modifiedVerts.Length; i++)
        {
            Vector3 vertex = m_modifiedVerts[i];
            m_minX = Mathf.Min(m_minX, vertex.x);
            m_maxX = Mathf.Max(m_maxX, vertex.x);
            m_minZ = Mathf.Min(m_minZ, vertex.z);
            m_maxZ = Mathf.Max(m_maxZ, vertex.z);
        }

        if (xSize > 0)
            m_spacingX = Mathf.Abs(m_modifiedVerts[1].x - m_modifiedVerts[0].x);
        if (zSize > 0 && m_stride < m_modifiedVerts.Length)
            m_spacingZ = Mathf.Abs(m_modifiedVerts[m_stride].z - m_modifiedVerts[0].z);

        if (m_spacingX < 0.0001f)
            m_spacingX = divid > 0.0001f ? 1f / divid : 1f;
        if (m_spacingZ < 0.0001f)
            m_spacingZ = divid > 0.0001f ? 1f / divid : 1f;
    }

    private void Update()
    {
        if (!EnsureInitialized())
            return;

        if (Mouse.current != null && Camera.main != null)
        {
            if (Mouse.current.rightButton.isPressed)
                RightClick();
            else if (Mouse.current.leftButton.isPressed)
                LeftClick();
        }

        float interval = Mathf.Max(0.001f, simulateTime);
        m_timer += Time.deltaTime;
        while (m_timer >= interval)
        {
            SandRelaxation(interval);
            m_timer -= interval;
        }

        if (m_meshDirty)
            RecalculateMesh();
    }

    private void RecalculateMesh()
    {
        if (!EnsureInitialized() || !m_meshDirty)
            return;

        m_mesh.vertices = m_modifiedVerts;
        m_mesh.uv2 = m_flowDirections;
        m_mesh.RecalculateNormals();
        m_mesh.RecalculateBounds();

        if (m_meshCollider != null)
        {
            m_meshCollider.sharedMesh = null;
            m_meshCollider.sharedMesh = m_mesh;
        }

        m_meshDirty = false;
    }

    private bool TryGetMouseHit(out RaycastHit hit)
    {
        hit = default;
        if (Camera.main == null || Mouse.current == null)
            return false;

        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out hit, Mathf.Infinity, ~0, QueryTriggerInteraction.Ignore))
            return false;

        return hit.collider.GetComponentInParent<SandMesh>() == this;
    }

    private void RightClick()
    {
        if (TryGetMouseHit(out RaycastHit hit))
            ApplyBrush(hit.point, 1f);
    }

    private void LeftClick()
    {
        if (TryGetMouseHit(out RaycastHit hit))
            ApplyBrush(hit.point, -1f);
    }

    private void ApplyBrush(Vector3 worldPoint, float direction)
    {
        Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
        float radiusSquared = radius * radius;
        float force = deformationStength / (1f + worldPoint.sqrMagnitude);

        for (int i = 0; i < m_modifiedVerts.Length; i++)
        {
            Vector3 vertex = m_modifiedVerts[i];
            float dx = vertex.x - localPoint.x;
            float dz = vertex.z - localPoint.z;
            float distanceSquared = dx * dx + dz * dz;
            if (distanceSquared >= radiusSquared)
                continue;

            float falloff = 1f - Mathf.Sqrt(distanceSquared) / Mathf.Max(0.001f, radius);
            vertex.y += direction * force * falloff * 0.5f;
            if (direction < 0f)
                vertex.y = Mathf.Max(floorLocalHeight, vertex.y);
            m_modifiedVerts[i] = vertex;
        }

        m_meshDirty = true;
    }

    private void SandRelaxation(float deltaTime)
    {
        if (!EnsureInitialized())
            return;

        System.Array.Clear(m_flowDirections, 0, m_flowDirections.Length);
        System.Array.Clear(heightChange, 0, heightChange.Length);

        for (int x = 0; x < xSize; x++)
        {
            for (int z = 0; z < zSize; z++)
            {
                int index = z * m_stride + x;
                MeshHeightChange(index, index + 1, deltaTime, Vector2.right, m_spacingX);
                MeshHeightChange(index, index + m_stride, deltaTime, Vector2.up, m_spacingZ);
            }
        }

        for (int i = 0; i < m_modifiedVerts.Length; i++)
            m_modifiedVerts[i].y = Mathf.Max(floorLocalHeight, m_modifiedVerts[i].y + heightChange[i]);

        if (spillAtEdges)
            ProcessEdgeSpill(deltaTime);

        for (int i = 0; i < m_flowDirections.Length; i++)
        {
            if (m_flowDirections[i].sqrMagnitude > 0.000001f)
                m_flowDirections[i].Normalize();
        }

        m_meshDirty = true;
    }

    private void MeshHeightChange(int a, int b, float deltaTime, Vector2 direction, float spacing)
    {
        float distance = m_modifiedVerts[a].y - m_modifiedVerts[b].y;
        float maxSlope = Mathf.Tan(reposeAngle * Mathf.Deg2Rad);
        float allowedHeightDifference = maxSlope * spacing;
        float excess = Mathf.Abs(distance) - allowedHeightDifference;
        if (excess <= 0f)
            return;

        float amount = excess * deltaTime;
        amount = Mathf.Min(amount * Mathf.Max(0f, flowRate), excess / 8f);
        if (amount <= 0f)
            return;

        if (distance < 0f)
        {
            heightChange[a] += amount;
            heightChange[b] -= amount;
            m_flowDirections[b] += -direction * amount;
        }
        else
        {
            heightChange[b] += amount;
            heightChange[a] -= amount;
            m_flowDirections[a] += direction * amount;
        }
    }

    private void ProcessEdgeSpill(float deltaTime)
    {
        if (spillRate <= 0f || m_modifiedVerts.Length == 0)
            return;

        for (int z = 0; z <= zSize; z++)
        {
            TrySpillVertex(z * m_stride, -1, 0, deltaTime);
            if (xSize > 0)
                TrySpillVertex(z * m_stride + xSize, 1, 0, deltaTime);
        }

        for (int x = 1; x < xSize; x++)
        {
            TrySpillVertex(x, 0, -1, deltaTime);
            TrySpillVertex(zSize * m_stride + x, 0, 1, deltaTime);
        }
    }

    private void TrySpillVertex(int index, int edgeDirectionX, int edgeDirectionZ, float deltaTime)
    {
        float excess = m_modifiedVerts[index].y - floorLocalHeight - spillHeightThreshold;
        if (excess <= 0f)
            return;

        float amount = Mathf.Min(excess * spillRate * deltaTime, excess * 0.5f);
        Vector3 localVertex = m_modifiedVerts[index];
        Vector3 outsideLocal = localVertex + new Vector3(
            edgeDirectionX * m_spacingX,
            0f,
            edgeDirectionZ * m_spacingZ);
        if (!TryFindFloorPoint(outsideLocal, out Vector3 targetWorld))
            return;
        float consumed = AddSandAtWorldInternal(targetWorld, amount, this, createPatchOnEmptyFloor);
        if (consumed <= 0f)
            return;

        m_modifiedVerts[index].y = Mathf.Max(floorLocalHeight, m_modifiedVerts[index].y - consumed);
        m_meshDirty = true;
    }

    private Vector3 FindFloorPoint(Vector3 outsideLocal)
    {
        if (TryFindFloorPoint(outsideLocal, out Vector3 floorPoint))
            return floorPoint;

        return transform.TransformPoint(new Vector3(outsideLocal.x, floorLocalHeight, outsideLocal.z));
    }

    private bool TryFindFloorPoint(Vector3 outsideLocal, out Vector3 floorPoint)
    {
        Vector3 origin = transform.TransformPoint(outsideLocal + Vector3.up * Mathf.Max(0.5f, floorRayDistance * 0.05f));
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, floorRayDistance, floorMask, QueryTriggerInteraction.Ignore);
        float closestDistance = float.PositiveInfinity;
        floorPoint = default;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.collider.GetComponentInParent<SandMesh>() == this)
                continue;
            if (hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                floorPoint = hit.point;
            }
        }

        return closestDistance < float.PositiveInfinity;
    }

    /// <summary>
    /// Adds sand to this patch if the world XZ coordinate is inside it.
    /// Returns the amount accepted by the patch.
    /// </summary>
    public float AddSand(Vector3 worldPosition, float amount)
    {
        if (!receiveRuntimeSand || amount <= 0f || !EnsureInitialized() || !ContainsWorldPoint(worldPosition))
            return 0f;

        Vector3 local = transform.InverseTransformPoint(worldPosition);
        float x = Mathf.Clamp((local.x - m_minX) / Mathf.Max(0.0001f, m_spacingX), 0f, xSize);
        float z = Mathf.Clamp((local.z - m_minZ) / Mathf.Max(0.0001f, m_spacingZ), 0f, zSize);
        int x0 = Mathf.Min(xSize - 1, Mathf.FloorToInt(x));
        int z0 = Mathf.Min(zSize - 1, Mathf.FloorToInt(z));
        float tx = x - x0;
        float tz = z - z0;

        int i00 = z0 * m_stride + x0;
        int i10 = i00 + 1;
        int i01 = i00 + m_stride;
        int i11 = i01 + 1;
        m_modifiedVerts[i00].y += amount * (1f - tx) * (1f - tz);
        m_modifiedVerts[i10].y += amount * tx * (1f - tz);
        m_modifiedVerts[i01].y += amount * (1f - tx) * tz;
        m_modifiedVerts[i11].y += amount * tx * tz;
        m_meshDirty = true;
        return amount;
    }

    /// <summary>
    /// True when a world position projects onto this patch's local XZ bounds.
    /// The Y coordinate is intentionally ignored because falling sand can hit
    /// the patch from above.
    /// </summary>
    public bool ContainsWorldPoint(Vector3 worldPosition)
    {
        if (!EnsureInitialized())
            return false;

        Vector3 local = transform.InverseTransformPoint(worldPosition);
        return local.x >= m_minX - 0.001f && local.x <= m_maxX + 0.001f &&
               local.z >= m_minZ - 0.001f && local.z <= m_maxZ + 0.001f;
    }

    /// <summary>
    /// Deposits sand on the closest existing patch. If no patch contains the
    /// position, a flat runtime patch is created on the floor below it.
    /// Returns the amount accepted by the world.
    /// </summary>
    public static float AddSandAtWorld(Vector3 worldPosition, float amount, bool createPatch = true)
    {
        return AddSandAtWorldInternal(worldPosition, amount, null, createPatch);
    }

    private static float AddSandAtWorldInternal(Vector3 worldPosition, float amount, SandMesh excluded, bool createPatch)
    {
        if (amount <= 0f)
            return 0f;

        SandMesh target = FindPatch(worldPosition, excluded);
        if (target != null)
            return target.AddSand(worldPosition, amount);

        if (!createPatch)
            return 0f;

        SandMesh template = FindTemplate(excluded);
        if (template == null)
            template = excluded;
        SandMesh patch = CreateRuntimePatch(worldPosition, template);
        return patch == null ? 0f : patch.AddSand(worldPosition, amount);
    }

    private static SandMesh FindPatch(Vector3 worldPosition, SandMesh excluded)
    {
        for (int i = s_instances.Count - 1; i >= 0; i--)
        {
            SandMesh candidate = s_instances[i];
            if (candidate == null || candidate == excluded || !candidate.isActiveAndEnabled)
                continue;
            if (candidate.ContainsWorldPoint(worldPosition))
                return candidate;
        }

        return null;
    }

    private static SandMesh FindTemplate(SandMesh excluded)
    {
        for (int i = s_instances.Count - 1; i >= 0; i--)
        {
            SandMesh candidate = s_instances[i];
            if (candidate != null && candidate != excluded && candidate.isActiveAndEnabled)
                return candidate;
        }

        return null;
    }

    private static SandMesh CreateRuntimePatch(Vector3 worldPosition, SandMesh template)
    {
        Vector3 floorPoint = worldPosition;
        if (template != null)
            floorPoint = template.FindFloorPoint(template.transform.InverseTransformPoint(worldPosition));
        else
            floorPoint = FindFloorPointWithoutTemplate(worldPosition);

        GameObject patchObject = new GameObject("SandMesh Runtime Patch");
        int patchX = template != null ? template.runtimePatchXSize : 16;
        int patchZ = template != null ? template.runtimePatchZSize : 16;
        float cellSize = template != null ? template.runtimePatchCellSize : 0.5f;
        Vector3 patchOrigin = floorPoint - new Vector3(patchX * cellSize * 0.5f, 0f, patchZ * cellSize * 0.5f);
        patchObject.transform.SetPositionAndRotation(
            new Vector3(patchOrigin.x, floorPoint.y + 0.002f, patchOrigin.z),
            Quaternion.identity);

        MeshFilter filter = patchObject.AddComponent<MeshFilter>();
        MeshRenderer renderer = patchObject.AddComponent<MeshRenderer>();
        MeshCollider collider = patchObject.AddComponent<MeshCollider>();
        MeshMake meshMake = patchObject.AddComponent<MeshMake>();

        meshMake.ConfigureRuntime(patchX, patchZ, cellSize, 0f);

        if (template != null)
        {
            template.EnsureInitialized();
            renderer.sharedMaterial = template.m_meshRenderer != null
                ? template.m_meshRenderer.sharedMaterial
                : null;
        }
        if (renderer.sharedMaterial == null)
            renderer.sharedMaterial = Resources.Load<Material>("KSJ/Mat/SandFlowMat");

        SandMesh sandMesh = patchObject.AddComponent<SandMesh>();
        sandMesh.CopyRuntimeSettings(template, patchX, patchZ, cellSize);
        sandMesh.EnsureInitialized();
        collider.sharedMesh = filter.sharedMesh;
        return sandMesh;
    }

    private static Vector3 FindFloorPointWithoutTemplate(Vector3 worldPosition)
    {
        Vector3 origin = worldPosition + Vector3.up * 50f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 100f, ~0, QueryTriggerInteraction.Ignore);
        float closestDistance = float.PositiveInfinity;
        Vector3 closestPoint = worldPosition;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider == null || hits[i].collider.GetComponentInParent<SandMesh>() != null)
                continue;
            if (hits[i].distance < closestDistance)
            {
                closestDistance = hits[i].distance;
                closestPoint = hits[i].point;
            }
        }

        return closestPoint;
    }

    private void CopyRuntimeSettings(SandMesh template, int patchX, int patchZ, float cellSize)
    {
        xSize = patchX;
        zSize = patchZ;
        divid = cellSize > 0.0001f ? 1f / cellSize : 1f;
        simulateTime = template != null ? template.simulateTime : 0.1f;
        reposeAngle = template != null ? template.reposeAngle : 30f;
        flowRate = template != null ? template.flowRate : 1f;
        spillAtEdges = template == null || template.spillAtEdges;
        spillRate = template != null ? template.spillRate : 1f;
        spillHeightThreshold = template != null ? template.spillHeightThreshold : 0.01f;
        floorLocalHeight = 0f;
        receiveRuntimeSand = true;
        createPatchOnEmptyFloor = template == null || template.createPatchOnEmptyFloor;
    }

    /// <summary>
    /// Convenience instance entry point for emitters that already have a
    /// reference to a particular patch.
    /// </summary>
    public float DepositSand(Vector3 worldPosition, float amount)
    {
        return AddSand(worldPosition, amount);
    }

    /// <summary>
    /// Configures an authored or test object as a runtime height-field. The
    /// mesh geometry is created by MeshMake; this method only sets the
    /// SandMesh grid and simulation settings before it starts receiving sand.
    /// </summary>
    public void ConfigureRuntime(int runtimeXSize, int runtimeZSize, float cellSize, float simulationInterval = 0.1f)
    {
        xSize = Mathf.Max(1, runtimeXSize);
        zSize = Mathf.Max(1, runtimeZSize);
        divid = cellSize > 0.0001f ? 1f / cellSize : 1f;
        simulateTime = Mathf.Max(0.001f, simulationInterval);
        runtimePatchXSize = xSize;
        runtimePatchZSize = zSize;
        runtimePatchCellSize = Mathf.Max(0.01f, cellSize);
        receiveRuntimeSand = true;
        createPatchOnEmptyFloor = true;
        spillAtEdges = true;
        spillRate = 1f;
        floorLocalHeight = 0f;
        floorRayDistance = 100f;

        // Test scene objects are configured immediately after their mesh is
        // created, but this also supports calling the method at runtime.
        EnsureInitialized();
    }
}
