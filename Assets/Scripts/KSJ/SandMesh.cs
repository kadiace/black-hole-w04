using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class SandMesh : MonoBehaviour
{

    [Range(1.5f, 5f)]
    [SerializeField]
    private float radius = 2f;
    [Range(1.5f, 5f)]
    [SerializeField]
    private float deformationStength = 2f;
    [SerializeField]

    private Mesh m_mesh;
    private Vector3[] m_verticies, m_modifiedVerts;

    float m_timer = 0;
    [SerializeField]
    private float simulateTime = 2;
    [SerializeField]
    private int xSize;
    [SerializeField]
    private int zSize;

    [SerializeField] private float reposeAngle = 30;

    [SerializeField] private float spaceX;

    [SerializeField] private float spaceY;

    public SphereCollider flowTrigger;
    [SerializeField] private float lowerSpeed = 100f;

    [SerializeField] private float minimumHeight = -5f;



    [SerializeField] private float flowRate = 1;


    float[] heightChange;

    public bool loseSand = false;

    private float storedSandAmount;

    // 이 값 이상 높이가 바뀌어야 메시/콜라이더를 다시 올림
    private const float MeshUploadThreshold = 0.001f;

    private Transform m_meshTransform;
    private MeshCollider m_meshCollider;
    private float m_pendingChange;

    // 바뀐 격자 영역 (업로드 대기 / 유체에 알릴 영역)
    private const float DirtyEpsilon = 0.00001f;
    private int m_pendingMinX = int.MaxValue, m_pendingMinZ = int.MaxValue, m_pendingMaxX = -1, m_pendingMaxZ = -1;
    private int m_changedMinX = int.MaxValue, m_changedMinZ = int.MaxValue, m_changedMaxX = -1, m_changedMaxZ = -1;

    // 모양이 바뀔 때마다 증가 (유체 장애물 갱신용)
    public int ShapeVersion { get; private set; }

    private void Awake()
    {
        // 여러 모래의 틱이 같은 프레임에 몰리지 않게 분산
        m_timer = UnityEngine.Random.Range(0f, simulateTime);
    }

    private void Start()
    {
        MeshFilter meshFilter = GetComponentInChildren<MeshFilter>();
        m_meshTransform = meshFilter.transform;
        m_meshCollider = GetComponentInChildren<MeshCollider>();
        m_mesh = meshFilter.mesh;
        m_verticies = m_mesh.vertices;
        m_modifiedVerts = (Vector3[])m_verticies.Clone();


        if (GetComponent<MeshMake>())
        {
            spaceX = 1 / GetComponent<MeshMake>().Divid;
            spaceY = 1 / GetComponent<MeshMake>().Divid;
            xSize = GetComponent<MeshMake>().xSize;
            zSize = GetComponent<MeshMake>().zSize;
        }
        else if (GetComponent<WhiteholeMeshMake>())
        {
            spaceX = GetComponent<WhiteholeMeshMake>().spaceX;
            spaceY = GetComponent<WhiteholeMeshMake>().spaceY;
            xSize = GetComponent<WhiteholeMeshMake>().xSize;
            zSize = GetComponent<WhiteholeMeshMake>().zSize;
        }

        heightChange = new float[(xSize + 1) * (zSize + 1)];
        m_mesh.MarkDynamic();
    }

    void RecalculateMesh()
    {
        if (loseSand)
        {
            KeepBound();
        }
        m_mesh.vertices = m_modifiedVerts;

        // sharedMesh 재할당 = PhysX 재쿠킹이므로 바뀐 경우에만 호출됨
        if (m_meshCollider != null)
        {
            m_meshCollider.sharedMesh = m_mesh;
        }
        m_mesh.RecalculateNormals();

        m_pendingChange = 0f;
        ShapeVersion++;

        if (m_pendingMaxX >= 0)
        {
            m_changedMinX = Mathf.Min(m_changedMinX, m_pendingMinX);
            m_changedMinZ = Mathf.Min(m_changedMinZ, m_pendingMinZ);
            m_changedMaxX = Mathf.Max(m_changedMaxX, m_pendingMaxX);
            m_changedMaxZ = Mathf.Max(m_changedMaxZ, m_pendingMaxZ);
            m_pendingMinX = m_pendingMinZ = int.MaxValue;
            m_pendingMaxX = m_pendingMaxZ = -1;
        }
    }

    private void MarkDirty(int index)
    {
        int x = index % (xSize + 1);
        int z = index / (xSize + 1);
        if (x < m_pendingMinX) m_pendingMinX = x;
        if (x > m_pendingMaxX) m_pendingMaxX = x;
        if (z < m_pendingMinZ) m_pendingMinZ = z;
        if (z > m_pendingMaxZ) m_pendingMaxZ = z;
    }

    // 마지막 호출 이후 바뀐 영역의 월드 bounds (유체 장애물 부분 갱신용)
    public bool TryConsumeChangedBounds(out Bounds worldBounds)
    {
        worldBounds = default;

        if (m_changedMaxX < 0 || m_meshTransform == null || m_mesh == null)
        {
            return false;
        }

        Vector3 origin = m_modifiedVerts[0];
        Bounds meshBounds = m_mesh.bounds;
        Vector3 localMin = new Vector3(origin.x + m_changedMinX * spaceX, Mathf.Min(meshBounds.min.y, minimumHeight), origin.z + m_changedMinZ * spaceY);
        Vector3 localMax = new Vector3(origin.x + m_changedMaxX * spaceX, meshBounds.max.y, origin.z + m_changedMaxZ * spaceY);

        worldBounds = new Bounds(m_meshTransform.TransformPoint(localMin), Vector3.zero);
        for (int corner = 1; corner < 8; corner++)
        {
            worldBounds.Encapsulate(m_meshTransform.TransformPoint(new Vector3(
                (corner & 1) != 0 ? localMax.x : localMin.x,
                (corner & 2) != 0 ? localMax.y : localMin.y,
                (corner & 4) != 0 ? localMax.z : localMin.z)));
        }

        m_changedMinX = m_changedMinZ = int.MaxValue;
        m_changedMaxX = m_changedMaxZ = -1;
        return true;
    }

    private void Update()
    {

        m_timer += Time.deltaTime;
        if (m_timer >= simulateTime)
        {
            LowerVerticesInsideTrigger();
            SandRelaxation(m_timer);

            // 모래가 안정되면 메시·콜라이더 갱신 생략
            if (m_pendingChange >= MeshUploadThreshold)
            {
                RecalculateMesh();
            }
            m_timer = 0;
        }


    }

    // 월드 좌표 기준 모래 표면까지의 부호 거리 (아래면 음수), 격자 밖이면 false
    public bool TryGetSignedDistance(Vector3 worldPoint, out float distance)
    {
        distance = 0f;

        // 유체가 Start 전에 장애물을 굽는 경우 대비
        if (m_modifiedVerts == null)
        {
            Start();
        }

        if (m_modifiedVerts == null || m_meshTransform == null || xSize <= 0 || zSize <= 0
            || spaceX <= 0f || spaceY <= 0f || m_modifiedVerts.Length != (xSize + 1) * (zSize + 1))
        {
            return false;
        }

        Vector3 local = m_meshTransform.InverseTransformPoint(worldPoint);
        Vector3 origin = m_modifiedVerts[0];
        float gx = (local.x - origin.x) / spaceX;
        float gz = (local.z - origin.z) / spaceY;

        if (gx < 0f || gz < 0f || gx > xSize || gz > zSize)
        {
            return false;
        }

        int x0 = Mathf.Min((int)gx, xSize - 1);
        int z0 = Mathf.Min((int)gz, zSize - 1);
        float tx = gx - x0;
        float tz = gz - z0;
        int row = xSize + 1;
        int i00 = z0 * row + x0;

        float h00 = m_modifiedVerts[i00].y;
        float h10 = m_modifiedVerts[i00 + 1].y;
        float h01 = m_modifiedVerts[i00 + row].y;
        float h11 = m_modifiedVerts[i00 + row + 1].y;

        float height = Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
        float slopeX = Mathf.Lerp(h10 - h00, h11 - h01, tz) / spaceX;
        float slopeZ = Mathf.Lerp(h01 - h00, h11 - h10, tx) / spaceY;

        // 경사를 반영한 표면 수직 거리
        float localDistance = (local.y - height) / Mathf.Sqrt(1f + slopeX * slopeX + slopeZ * slopeZ);
        distance = localDistance * Mathf.Abs(m_meshTransform.lossyScale.y);
        return true;
    }

    void RightClick()
    {
        RaycastHit hit;
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());


        if (Physics.Raycast(ray, out hit, Mathf.Infinity))
        {
            for (int v = 0; v < m_modifiedVerts.Length; v++)
            {
                Vector3 distance = m_modifiedVerts[v] - hit.point;
                float smoothingFactor = 2f;


                float force = deformationStength / (1f + hit.point.sqrMagnitude);

                if (distance.sqrMagnitude < radius)
                {

                    m_modifiedVerts[v] = m_modifiedVerts[v] + (Vector3.up * force) / smoothingFactor;



                }
            }
        }
    }

    void LeftClick()
    {
        RaycastHit hit;
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());


        if (Physics.Raycast(ray, out hit, Mathf.Infinity))
        {
            for (int v = 0; v < m_modifiedVerts.Length; v++)
            {
                Vector3 distance = m_modifiedVerts[v] - hit.point;
                float smoothingFactor = 2f;


                float force = deformationStength / (1f + hit.point.sqrMagnitude);

                if (distance.sqrMagnitude < radius)
                {


                    m_modifiedVerts[v] = m_modifiedVerts[v] + (Vector3.down * force) / smoothingFactor;


                }
            }



        }
    }

    //public void SandUp(RaycastHit hit)
    //{


    //    Vector3 hitLocal = GetComponentInChildren<MeshFilter>().transform.InverseTransformPoint(hit.point);


    //    for (int v = 0; v < m_modifiedVerts.Length; v++)
    //        {
    //            Vector3 distance = m_modifiedVerts[v] - hitLocal;
    //            float smoothingFactor = 2f;


    //            float force = deformationStength / (1f + hitLocal.sqrMagnitude);

    //            if (distance.sqrMagnitude < radius)
    //            {

    //                m_modifiedVerts[v] = m_modifiedVerts[v] + (Vector3.up * force) / smoothingFactor;



    //            }
    //        }

    //    RecalculateMesh();

    //}

    public bool SandUp(RaycastHit hit)
    {

        float amountToPour = Managers.Gravity.savedSand;
        if (Managers.Gravity.savedSand <= 50)
        {
            Managers.Gravity.savedSand = 0;
            return false;
        }
        if (m_modifiedVerts == null || GetComponentInChildren<MeshFilter>() == null)
            Start();
        Debug.Log(Managers.Gravity.savedSand);
        Vector3 hitLocal = GetComponentInChildren<MeshFilter>().transform.InverseTransformPoint(hit.point);
        float radiusSqr = radius * radius;
        float totalWeight = 0;

        for (int v = 0; v < m_modifiedVerts.Length; v++)
        {
            Vector3 offset = m_modifiedVerts[v] - hitLocal;
            float distanceSqr = offset.sqrMagnitude;

            if (distanceSqr >= radiusSqr)
                continue;

            // 중심에서 가장 많이 올라가고, 반경 끝에서 0이 되는 부드러운 가중치
            float distance01 = Mathf.Sqrt(distanceSqr) / radius;
            float weight = 1f - Mathf.SmoothStep(0f, 1f, distance01);

            totalWeight += weight;
        }

        for (int v = 0; v < m_modifiedVerts.Length; v++)
        {
            Vector3 offset = m_modifiedVerts[v] - hitLocal;
            float distanceSqr = offset.sqrMagnitude;

            if (distanceSqr >= radiusSqr)
                continue;

            // 중심에서 가장 많이 올라가고, 반경 끝에서 0이 되는 부드러운 가중치
            float distance01 = Mathf.Sqrt(distanceSqr) / radius;
            float weight = 1f - Mathf.SmoothStep(0f, 1f, distance01);

            m_modifiedVerts[v].y += amountToPour * weight / totalWeight;
            MarkDirty(v);
        }

        Managers.Gravity.savedSand = 0;

        RecalculateMesh();

        return true;
    }

    void SandRelaxation(float _deltatime)
    {
        int vertexCount = (xSize + 1) * (zSize + 1);
        if (heightChange == null || heightChange.Length != vertexCount)
        {
            heightChange = new float[vertexCount];
        }
        else
        {
            Array.Clear(heightChange, 0, vertexCount);
        }

        for (int x = 0; x < xSize; x++)
        {
            for (int z = 0; z < zSize; z++)
            {
                int a = z * (xSize + 1) + x;
                int b = a + xSize + 1;
                int c = a + 1;

                // 가로 이웃
                MeshHeightChange(
                    a, c, _deltatime, Vector2.right, spaceX);

                // 다음 z 행의 이웃
                MeshHeightChange(
                    a, b, _deltatime, Vector2.up, spaceY);

                // 삼각형의 b-c 대각선 이웃
                //MeshHeightChange(
                //    b, c, _deltatime,
                //    new Vector2(1f, -1f).normalized,
                //    diagonalSpacing);


            }
        }


        float maxChange = 0f;

        for (int i = 0; i < m_modifiedVerts.Length; i++)
        {
            float before = m_modifiedVerts[i].y;
            m_modifiedVerts[i].y += heightChange[i];


            m_modifiedVerts[i].y = Mathf.Max(m_modifiedVerts[i].y, minimumHeight);
            float change = Mathf.Abs(m_modifiedVerts[i].y - before);
            if (change > DirtyEpsilon)
            {
                MarkDirty(i);
                maxChange = Mathf.Max(maxChange, change);
            }
        }

        m_pendingChange += maxChange;


    }

    void MeshHeightChange(int _a, int _b, float _deltatime, Vector2 direction, float Spacing)
    {
        float distance = m_modifiedVerts[_a].y - m_modifiedVerts[_b].y;
        float maxSlope = Mathf.Tan(reposeAngle * Mathf.Deg2Rad);
        float allowedHeightDifference = maxSlope * Spacing;
        float excess = Mathf.Abs(distance) - allowedHeightDifference;
        float amount = excess * _deltatime;

        amount = Mathf.Min(amount * flowRate, excess / 8);
        if (excess > 0)
        {
            if (distance < 0)
            {
                heightChange[_a] += amount;
                heightChange[_b] += -amount;




            }
            if (distance > 0)
            {
                heightChange[_b] += amount;
                heightChange[_a] += -amount;


            }
        }

    }



    void KeepBound()
    {
        for (int x = 0; x <= xSize; x++)
        {
            for (int z = 0; z <= zSize; z++)
            {
                int index = z * (xSize + 1) + x;

                if (x == 0)
                    m_modifiedVerts[index] = m_verticies[index];

                if (x == xSize)
                    m_modifiedVerts[index] = m_verticies[index];

                if (z == 0)
                    m_modifiedVerts[index] = m_verticies[index];

                if (z == zSize)
                    m_modifiedVerts[index] = m_verticies[index];

            }
        }
    }




    private void LowerVerticesInsideTrigger()
    {

        if (flowTrigger == null)
            return;
        storedSandAmount = 0;
        Vector3 centerWorld = flowTrigger.transform.TransformPoint(flowTrigger.center);
        float radiusWorld = flowTrigger.radius *
            Mathf.Max(
                flowTrigger.transform.lossyScale.x,
                flowTrigger.transform.lossyScale.y,
                flowTrigger.transform.lossyScale.z);

        Transform meshTransform = m_meshTransform;

        for (int i = 0; i < m_modifiedVerts.Length; i++)
        {
            Vector3 vertexWorld = meshTransform.TransformPoint(m_modifiedVerts[i]);

            if (Vector3.Distance(vertexWorld, centerWorld) <= radiusWorld)
            {
                // 구체 안에 있는 정점을 월드 아래 방향으로 내림
                Vector3 loweredWorld = vertexWorld + Vector3.down * lowerSpeed * Time.deltaTime;



                // 내려간 월드 위치를 메시 로컬 좌표로 변환
                Vector3 loweredLocal = meshTransform.InverseTransformPoint(loweredWorld);

                loweredLocal.y = Mathf.Max(loweredLocal.y, minimumHeight);

                // 이번에 내려간 높이를 총량에 저장
                float loweredBy = m_modifiedVerts[i].y - loweredLocal.y;
                if (loweredBy > 0f)
                    storedSandAmount += loweredBy;
                m_pendingChange = Mathf.Max(m_pendingChange, Mathf.Abs(loweredBy));
                if (Mathf.Abs(loweredBy) > DirtyEpsilon)
                    MarkDirty(i);

                // 변경된 로컬 위치 적용
                m_modifiedVerts[i] = loweredLocal;




            }
        }
        storedSandAmount /= 5;
        Managers.Gravity.savedSand += storedSandAmount;
    }



}
