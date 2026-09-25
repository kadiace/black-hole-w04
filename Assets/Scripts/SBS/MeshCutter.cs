using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.ProBuilder.Csg;
using UnityEngine.Rendering;
using static UnityEditor.Progress;

public class TriData
{

}



public class MeshCutter : MonoBehaviour
{
    [SerializeField]
    MeshFilter m_targetMesh;

    private void Awake()
    {
    }

    [ContextMenu("DO")]
    private void Do()
    {
        var localPos = m_targetMesh.transform.InverseTransformPoint(transform.position);
        var mesh = m_targetMesh.mesh;
        Cut(ref mesh, localPos, 1f);
        m_targetMesh.mesh = mesh;
    }

    void CutCSG(GameObject lhs, GameObject rhs)
    {

    }

    private Model Union(GameObject lhs, GameObject rhs)
    {
        Model csg_model_a = new Model(lhs);
        Model csg_model_b = new Model(rhs);

        Node a = new Node(csg_model_a.ToPolygons());
        Node b = new Node(csg_model_b.ToPolygons());

        List<Polygon> polygons = Node.Union(a, b).AllPolygons();

        return new Model(polygons);
    }
    private Model Subtract(GameObject lhs, GameObject rhs)
    {
        Model csg_model_a = new Model(lhs);
        Model csg_model_b = new Model(rhs);

        Node a = new Node(csg_model_a.ToPolygons());
        Node b = new Node(csg_model_b.ToPolygons());

        List<Polygon> polygons = Node.Subtract(a, b).AllPolygons();

        return new Model(polygons);
    }

    private Model Intersect(GameObject lhs, GameObject rhs)
    {
        Model csg_model_a = new Model(lhs);
        Model csg_model_b = new Model(rhs);

        Node a = new Node(csg_model_a.ToPolygons());
        Node b = new Node(csg_model_b.ToPolygons());

        List<Polygon> polygons = Node.Intersect(a, b).AllPolygons();

        return new Model(polygons);
    }




    /// <summary>
    /// 메시 자르기. 중점은 메시의 로컬 좌표 기준.
    /// </summary>
    /// <param name="_target"></param>
    /// <param name="_cutCenterPos"></param>
    /// <param name="_radius"></param>
    void Cut(ref Mesh _target, Vector3 _cutCenterPos, float _radius)
    {
        if (_target == null) return;

        // 정점 정보 세팅
        var vertices = _target.vertices;
        var triangles = _target.triangles;

        // 삭제 리스트 초기화
        List<Vector3> deleteList = new();
        var delList = new List<int>();

        for (int i = 0; i < vertices.Length; i++)
        {
            // 각 정점부터 삭제 중점까지의 거리 계산
            float dist = Vector3.Distance(vertices[i], _cutCenterPos);
            // 반지름보다 가까우면 삭제 대상.
            if (_radius > dist)
            {
                delList.Add(i);
                deleteList.Add(vertices[i]);
            }
        }

        var verticsList = new List<Vector3>();
        var triangleList = new List<int>(triangles);


        for (int i =  delList.Count - 1; i > 0; i--)
        {
            var dvert = delList[i];

            var tempVertex = vertices[dvert];
            vertices[dvert] = Vector3.zero;
            Vector3 dir = tempVertex - _cutCenterPos;
            tempVertex = _cutCenterPos + dir.normalized * _radius;
        }

        _target.triangles = triangleList.ToArray();
        _target.vertices = vertices;
    }

    #region ProBuilder CSG 절단

    [Header("ProBuilder CSG 절단")]
    [SerializeField]
    float m_csgRadius = 1f;

    [ContextMenu("DO (ProBuilder CSG)")]
    private void DoProBuilderCsg()
    {
        CutWithProBuilderCsg(m_targetMesh.gameObject, transform.position, m_csgRadius, out _, out _);
    }

    /// <summary>
    /// ProBuilder CSG 로 구체 절단. 원본은 파괴되고 바깥/안쪽 조각이 새 GameObject 로 생성된다.
    /// </summary>
    /// <param name="_target">절단 대상 (MeshFilter + MeshRenderer 필요)</param>
    /// <param name="_worldCenter">구 중점 (월드)</param>
    /// <param name="_worldRadius">구 반지름 (월드)</param>
    /// <param name="_outside">원본 - 구 (파인 나머지)</param>
    /// <param name="_inside">원본 ∩ 구 (도려낸 덩어리)</param>
    public static bool CutWithProBuilderCsg(GameObject _target, Vector3 _worldCenter, float _worldRadius,
        out GameObject _outside, out GameObject _inside)
    {
        _outside = null;
        _inside = null;

        var targetRenderer = _target.GetComponent<MeshRenderer>();
        if (_target.GetComponent<MeshFilter>() == null || targetRenderer == null)
            return false;

        // 바운즈가 안 겹치면 CSG 를 돌릴 필요 없음
        if (!targetRenderer.bounds.Intersects(new Bounds(_worldCenter, Vector3.one * (_worldRadius * 2f))))
            return false;

        // 임시 구 커터. 대상의 첫 머터리얼을 씌워서 절단면이 기존 머터리얼 서브메시로 합쳐지게 한다.
        // (ProBuilder CSG 는 결과 서브메시를 머터리얼 기준으로 묶는다)
        var cutter = new GameObject("PB_CsgCutter") { hideFlags = HideFlags.HideAndDontSave };
        cutter.transform.position = _worldCenter;
        cutter.transform.localScale = Vector3.one * (_worldRadius * 2f); // 내장 구 메시는 반지름 0.5
        cutter.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
        cutter.AddComponent<MeshRenderer>().sharedMaterial = targetRenderer.sharedMaterial;

        // 대상이 구 안에 통째로 들어가면 차집합 결과가 비는데,
        // 이때 ProBuilder CSG 가 내부에서 NullReferenceException 을 내므로 차집합은 호출하지 않는다.
        bool fullyInside = IsBoundsInsideSphere(targetRenderer.bounds, _worldCenter, _worldRadius);

        Model outModel = null, inModel;
        try
        {
            inModel = CSG.Intersect(_target, cutter);
            if (!fullyInside)
                outModel = CSG.Subtract(_target, cutter);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[MeshCutter] ProBuilder CSG 실패: {e.Message}", _target);
            return false;
        }
        finally
        {
            DestroyImmediate(cutter);
        }

        GetModel(outModel, out Mesh outMesh, out Material[] outMats);
        GetModel(inModel, out Mesh inMesh, out Material[] inMats);

        // ProBuilder CSG(BSP) 결과 정리 (아직 월드 좌표 상태):
        // - 얇은 슬리버 삼각형과 잘못 분류된 파편이 섞여 나와 바운즈/Convex 콜라이더를 부풀리므로 제거한다.
        // - 커터가 구이므로 안쪽 조각은 구 밖, 바깥 조각은 구 깊숙이 정점이 있으면 파편으로 본다.
        //   (내장 구 메시는 다각형 근사라 면이 실제 구보다 조금 안쪽 → 여유 5%)
        float outerLimitSq = (_worldRadius * 1.05f) * (_worldRadius * 1.05f);
        float innerLimitSq = (_worldRadius * 0.95f) * (_worldRadius * 0.95f);
        if (outMesh != null)
            CleanupTriangles(outMesh, p => (p - _worldCenter).sqrMagnitude >= innerLimitSq);
        if (inMesh != null)
            CleanupTriangles(inMesh, p => (p - _worldCenter).sqrMagnitude <= outerLimitSq);

        // 교집합이 비어 있으면 안 닿은 것
        if (inMesh == null || inMesh.vertexCount == 0)
        {
            if (outMesh != null) DestroyImmediate(outMesh);
            if (inMesh != null) DestroyImmediate(inMesh);
            return false;
        }

        // 결과는 월드 좌표 → 원본 로컬 좌표로 되돌려서 원본과 같은 트랜스폼에 얹는다
        // (outMesh 가 null = 통째로 먹힘)
        if (outMesh != null) 
            WorldToLocal(outMesh, _target.transform);
        WorldToLocal(inMesh, _target.transform);

        // 질량은 부피 비율로 분배
        float outVol = outMesh != null ? Mathf.Abs(SignedVolume(outMesh)) : 0f;
        float inVol = Mathf.Abs(SignedVolume(inMesh));
        float total = outVol + inVol;

        if (outMesh != null)
            _outside = CreateCsgPiece(_target, outMesh, outMats, "_Out", total > 0f ? outVol / total : 0.5f);
        _inside = CreateCsgPiece(_target, inMesh, inMats, "_In", total > 0f ? inVol / total : 0.5f);

        if 
            (Application.isPlaying) Destroy(_target);
        else 
            DestroyImmediate(_target);

        return true;
    }

    /// <summary>ProBuilder CSG Model 에서 mesh / materials 꺼내기.</summary>
    static void GetModel(Model _model, out Mesh _mesh, out Material[] _materials)
    {
        _mesh = null;
        _materials = null;

        // 결과가 비었으면 ProBuilder 의 mesh 변환이 예외를 내므로 먼저 정점 수 확인
        if (_model == null || _model.vertices == null || _model.vertices.Count == 0)
            return;

        _mesh = _model.mesh;
        _materials = _model.materials.ToArray();
    }

    /// <summary>AABB 의 8 꼭짓점이 모두 구 안에 있는지 (= 대상이 통째로 구 안).</summary>
    static bool IsBoundsInsideSphere(Bounds _b, Vector3 _center, float _radius)
    {
        Vector3 e = _b.extents;
        for (int i = 0; i < 8; i++)
        {
            var corner = _b.center + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            if ((corner - _center).sqrMagnitude > _radius * _radius)
                return false;
        }
        return true;
    }

    /// <summary>
    /// 삼각형 정리 후 안 쓰는 정점을 제거한다.
    /// - 높이(넓이*2 / 가장 긴 변)가 거의 0 인 슬리버 삼각형 제거
    /// - 세 정점 중 하나라도 _vertexValid 를 만족하지 않으면 제거
    /// </summary>
    static void CleanupTriangles(Mesh _mesh, System.Func<Vector3, bool> _vertexValid)
    {
        var vertices = _mesh.vertices;
        float minHeight = _mesh.bounds.size.magnitude * 1e-4f;

        // 1) 서브메시별로 살릴 삼각형만 모음
        var subs = new List<int>[_mesh.subMeshCount];
        var used = new bool[vertices.Length];
        for (int s = 0; s < subs.Length; s++)
        {
            subs[s] = new List<int>();
            var tris = _mesh.GetTriangles(s);
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = vertices[tris[i]], b = vertices[tris[i + 1]], c = vertices[tris[i + 2]];
                float area2 = Vector3.Cross(b - a, c - a).magnitude;
                float longest = Mathf.Sqrt(Mathf.Max((b - a).sqrMagnitude, (c - b).sqrMagnitude, (a - c).sqrMagnitude));
                if (longest <= 0f || area2 / longest < minHeight)
                    continue;
                if (!_vertexValid(a) || !_vertexValid(b) || !_vertexValid(c))
                    continue;

                for (int k = 0; k < 3; k++)
                {
                    subs[s].Add(tris[i + k]);
                    used[tris[i + k]] = true;
                }
            }
        }

        // 2) 쓰는 정점만 남기도록 인덱스 재매핑
        var remap = new int[vertices.Length];
        int count = 0;
        for (int i = 0; i < vertices.Length; i++)
            remap[i] = used[i] ? count++ : -1;

        T[] Compact<T>(T[] src)
        {
            if (src == null || src.Length != vertices.Length)
                return null;
            var dst = new T[count];
            for (int i = 0; i < src.Length; i++)
                if (remap[i] >= 0) dst[remap[i]] = src[i];
            return dst;
        }

        var normals = Compact(_mesh.normals);
        var tangents = Compact(_mesh.tangents);
        var colors = Compact(_mesh.colors);
        var uv0 = Compact(_mesh.uv);
        var uv1 = Compact(_mesh.uv2);
        var newVertices = Compact(vertices);

        _mesh.Clear();
        _mesh.indexFormat = count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        _mesh.vertices = newVertices;
        if (normals != null) _mesh.normals = normals;
        if (tangents != null) _mesh.tangents = tangents;
        if (colors != null) _mesh.colors = colors;
        if (uv0 != null) _mesh.uv = uv0;
        if (uv1 != null) _mesh.uv2 = uv1;
        _mesh.subMeshCount = subs.Length;
        for (int s = 0; s < subs.Length; s++)
        {
            for (int i = 0; i < subs[s].Count; i++)
                subs[s][i] = remap[subs[s][i]];
            _mesh.SetTriangles(subs[s], s);
        }
    }

    /// <summary>월드 좌표 메시를 대상 로컬 좌표로 변환 (ProBuilder 가 적용한 변환의 역).</summary>
    static void WorldToLocal(Mesh _mesh, Transform _t)
    {
        var vertices = _mesh.vertices;
        var normals = _mesh.normals;
        for (int i = 0; i < vertices.Length; i++)
            vertices[i] = _t.InverseTransformPoint(vertices[i]);
        for (int i = 0; i < normals.Length; i++)
            normals[i] = _t.InverseTransformDirection(normals[i]);

        _mesh.vertices = vertices;
        if (normals.Length == vertices.Length)
            _mesh.normals = normals;
        else 
            _mesh.RecalculateNormals();
        if (_mesh.tangents.Length == vertices.Length)
            _mesh.RecalculateTangents();
        _mesh.RecalculateBounds();
    }

    /// <summary>원본 설정(트랜스폼/렌더러/콜라이더/리지드바디)을 복사한 조각 GameObject 생성.</summary>
    static GameObject CreateCsgPiece(GameObject _src, Mesh _mesh, Material[] _materials, string _suffix, float _massRatio)
    {
        var go = new GameObject(_src.name + _suffix) { layer = _src.layer, tag = _src.tag };
        go.transform.SetParent(_src.transform.parent, false);
        go.transform.SetLocalPositionAndRotation(_src.transform.localPosition, _src.transform.localRotation);
        go.transform.localScale = _src.transform.localScale;

        _mesh.name = _src.name + _suffix;
        go.AddComponent<MeshFilter>().sharedMesh = _mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = _materials;

        // 콜라이더: 모양이 바뀌었으므로 조각 메시로 Convex MeshCollider (동적 리지드바디는 convex 필수)
        var srcCol = _src.GetComponent<Collider>();
        if (srcCol != null)
        {
            var col = go.AddComponent<MeshCollider>();
            col.convex = true;
            col.sharedMesh = _mesh;
            col.isTrigger = srcCol.isTrigger;
            col.sharedMaterial = srcCol.sharedMaterial;
        }

        // 리지드바디: 설정 복사 + 질량 분배 + 속도 계승
        var srcRb = _src.GetComponent<Rigidbody>();
        if (srcRb != null)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(0.0001f, srcRb.mass * _massRatio);
            rb.linearDamping = srcRb.linearDamping;
            rb.angularDamping = srcRb.angularDamping;
            rb.useGravity = srcRb.useGravity;
            rb.isKinematic = srcRb.isKinematic;
            rb.interpolation = srcRb.interpolation;
            rb.collisionDetectionMode = srcRb.collisionDetectionMode;
            rb.constraints = srcRb.constraints;
            if (!srcRb.isKinematic)
            {
                rb.linearVelocity = srcRb.linearVelocity;
                rb.angularVelocity = srcRb.angularVelocity;
            }
        }

        return go;
    }

    /// <summary>닫힌 메시의 부호 있는 부피.</summary>
    static float SignedVolume(Mesh _mesh)
    {
        var v = _mesh.vertices;
        float vol = 0f;
        for (int s = 0; s < _mesh.subMeshCount; s++)
        {
            var t = _mesh.GetTriangles(s);
            for (int i = 0; i < t.Length; i += 3)
                vol += Vector3.Dot(v[t[i]], Vector3.Cross(v[t[i + 1]], v[t[i + 2]])) / 6f;
        }
        return vol;
    }

    #endregion
}
