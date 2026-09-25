using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using System;
using Object = UnityEngine.Object;


#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 메시 절단 정적 유틸리티. 이 파일은 평면(Plane) 절단,
/// 구체/임의 메시 모양 절단은 MeshSlicer.Shape.cs 참고.
/// <para>
/// - 절단면(캡)은 새 머터리얼을 만들지 않고 원본 머터리얼(지정한 서브메시)을 그대로 사용한다.<br/>
/// - 절단 결과는 각각 독립된 GameObject로 생성된다.<br/>
/// - 원본에 Rigidbody / Collider 가 있으면 조각에도 동일하게 붙여준다.
/// </para>
/// 제약: 메시는 Read/Write Enabled 상태여야 하고, 삼각형 토폴로지만 지원한다.
/// 절단면에 구멍이 있는 경우(도넛 단면 등)는 각 외곽선을 독립적으로 채운다.
/// </summary>
public static partial class MeshSlicer
{
    /// <summary>조각에 붙일 콜라이더 생성 방식.</summary>
    public enum ColliderMode
    {
        /// <summary>조각 메시로 Convex MeshCollider 생성 (모양이 가장 정확함).</summary>
        ConvexMesh,
        /// <summary>원본 콜라이더 타입(Box/Sphere/Capsule)을 유지하고 조각 바운즈에 맞춤.</summary>
        MatchOriginalType,
    }

    /// <summary>버텍스 사영 절단이 불가능할 때 처리 방법.</summary>
    public enum ProjectionFallback
    {
        /// <summary>버텍스를 옮기지 않고, 구에 걸린 삼각형을 원래 모양 그대로 떼어낸다 (가벼움, 구멍 벽면 없음)</summary>
        SplitAsIs,
        /// <summary>CSG 구체 절단으로 대신 처리 (정확, 닫힌 메시)</summary>
        Csg,
        /// <summary>자르지 않고 경고 후 실패 반환</summary>
        None,
    }

    /// <summary>절단 옵션.</summary>
    [Serializable]
    public struct Options
    {
        [Header("공통")]
        [Tooltip("절단면에 사용할 머터리얼의 서브메시 인덱스 (MeshRenderer.materials 순서). 기존 머터리얼을 그대로 사용한다.")]
        public int capSubmeshIndex;

        [Tooltip("절단면 UV 스케일 (로컬 1 유닛당 UV)")]
        public float capUVScale;
        [Tooltip("조각 콜라이더 생성 방식")]
        public ColliderMode colliderMode;
        [Tooltip("절단 후 원본 파괴 여부")]
        public bool destroyOriginal;
        [Tooltip("절단 후 조각을 밀어내는 임펄스 (평면: 법선 방향, 모양: 커터 중심에서 바깥)")]
        public float separationImpulse;

        [Header("모양(구체/메시) 절단")]
        [Tooltip("커터 안쪽 덩어리를 조각으로 남길지. 끄면 파먹힌 것처럼 사라진다")]
        public bool keepInside;
        [Tooltip("결과가 떨어진 덩어리로 나뉘면 각각 별도 GameObject 로 분리")]
        public bool separateIslands;
        [Tooltip("구체 커터 정밀도 (0=20면 ~ 4=5120면). 높을수록 매끈하지만 느림")]
        [Range(0, 4)]
        public int sphereSubdivisions;
        [Tooltip("버텍스 사영 절단이 불가능한 형태(구가 얇은 판을 관통, 표면이 구 중점을 감싸듯 휜 경우 등)일 때 처리 방법.\n" +
                 "SplitAsIs: 구에 걸린 삼각형을 원래 모양 그대로 떼어냄 / Csg: CSG 구체 절단 / None: 자르지 않음")]
        public ProjectionFallback projectionFallback;

        public bool addRigidBody;
        public bool addGravityController;

        public static Options Default => new Options
        {
            keepInside = true,
            separateIslands = true,
            sphereSubdivisions = 2,
            projectionFallback = ProjectionFallback.SplitAsIs,
            capSubmeshIndex = 0,
            capUVScale = 1f,
            colliderMode = ColliderMode.ConvexMesh,
            destroyOriginal = true,
            separationImpulse = 0.5f,
            addRigidBody = true,
            addGravityController = true,
        };
    }

    // 절단 교차점 용접(weld) 허용 오차. 교차점 좌표를 이 단위로 양자화해서 같은 점으로 취급한다.
    const float WeldEpsilon = 1e-4f;

    #region Public API

    /// <summary>
    /// 월드 공간 평면으로 대상 GameObject를 절단한다.
    /// </summary>
    /// <param name="target">MeshFilter 를 가진 대상.</param>
    /// <param name="worldPoint">평면 위의 한 점 (월드).</param>
    /// <param name="worldNormal">평면 법선 (월드). 법선이 가리키는 쪽이 positive 조각.</param>
    /// <param name="options">절단 옵션.</param>
    /// <param name="positive">법선 방향 쪽 조각.</param>
    /// <param name="negative">법선 반대쪽 조각.</param>
    /// <returns>평면이 메시를 실제로 가로질러 두 조각이 생겼으면 true.</returns>
    public static bool Slice(GameObject target, Vector3 worldPoint, Vector3 worldNormal, Options options,
        out GameObject positive, out GameObject negative)
    {
        positive = negative = null;
        if (target == null) return false;

        var filter = target.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null)
        {
            Debug.LogWarning($"[MeshSlicer] '{target.name}' 에 MeshFilter/Mesh 가 없습니다.", target);
            return false;
        }

        var srcMesh = filter.sharedMesh;
        if (!srcMesh.isReadable)
        {
            Debug.LogWarning($"[MeshSlicer] '{srcMesh.name}' 메시가 Read/Write Enabled 가 아닙니다.", target);
            return false;
        }

        // 월드 평면 → 로컬 평면 변환.
        // 비균등 스케일에서도 맞도록 법선은 localToWorld 의 전치행렬로 변환한다.
        // (월드: n·x = d, x = M·p  →  (Mᵀn)·p = d)
        var t = target.transform;
        Vector3 localPoint = t.InverseTransformPoint(worldPoint);
        Vector3 localNormal = t.localToWorldMatrix.transpose.MultiplyVector(worldNormal).normalized;
        var localPlane = new Plane(localNormal, localPoint);

        if (!SliceMesh(srcMesh, localPlane, options, out Mesh posMesh, out Mesh negMesh))
            return false;

        // 조각 질량 배분을 위해 (닫힌) 메시 부피 비율 계산.
        float posVol = Mathf.Abs(SignedVolume(posMesh));
        float negVol = Mathf.Abs(SignedVolume(negMesh));
        float total = posVol + negVol;
        float posRatio = total > 1e-8f ? posVol / total : 0.5f;

        positive = CreatePiece(target, posMesh, "_Pos", posRatio, options);
        negative = CreatePiece(target, negMesh, "_Neg", 1f - posRatio, options);

        // 서로 반대 방향으로 살짝 밀어내기.
        if (options.separationImpulse > 0f)
        {
            Vector3 n = worldNormal.normalized;
            AddImpulse(positive, n * options.separationImpulse);
            AddImpulse(negative, -n * options.separationImpulse);
        }

        if (options.destroyOriginal)
            DestroyObject(target);

        return true;
    }

    /// <summary>
    /// 로컬 공간 평면으로 메시 데이터만 절단한다 (GameObject 생성 없음).
    /// </summary>
    /// <returns>양쪽 모두 삼각형이 존재하면 true.</returns>
    public static bool SliceMesh(Mesh source, Plane localPlane, Options options, out Mesh positive, out Mesh negative)
    {
        positive = negative = null;

        int subCount = source.subMeshCount;
        for (int s = 0; s < subCount; s++)
        {
            if (source.GetTopology(s) != MeshTopology.Triangles)
            {
                Debug.LogWarning($"[MeshSlicer] '{source.name}' 의 서브메시 {s} 가 삼각형 토폴로지가 아닙니다.");
                return false;
            }
        }

        var src = new SourceData(source);
        var posB = new MeshBuilder(src, subCount);
        var negB = new MeshBuilder(src, subCount);

        // 각 정점의 평면까지 부호 있는 거리 (≥0 이면 positive 쪽)
        var dist = new float[src.positions.Length];
        for (int i = 0; i < dist.Length; i++)
            dist[i] = localPlane.GetDistanceToPoint(src.positions[i]);

        // 절단선 선분 목록 (캡 생성용)
        var segments = new List<(Vector3 a, Vector3 b)>();
        var tris = new List<int>();

        for (int s = 0; s < subCount; s++)
        {
            source.GetTriangles(tris, s);
            for (int i = 0; i < tris.Count; i += 3)
                SplitTriangle(tris[i], tris[i + 1], tris[i + 2], s, dist, posB, negB, segments);
        }

        // 한쪽에 삼각형이 없으면 평면이 메시를 가로지르지 않은 것.
        if (posB.IsEmpty || negB.IsEmpty)
            return false;

        // 절단면(캡) 생성. 원본 머터리얼을 쓰기 위해 기존 서브메시에 삼각형을 추가한다.
        int capSub = Mathf.Clamp(options.capSubmeshIndex, 0, subCount - 1);
        var loops = BuildLoops(segments);
        foreach (var loop in loops)
        {
            // positive 조각의 절단면은 평면 법선 반대쪽(-n)을 바라봐야 바깥을 향한다.
            posB.AddCap(loop, -localPlane.normal, capSub, options.capUVScale);
            negB.AddCap(loop, localPlane.normal, capSub, options.capUVScale);
        }

        positive = posB.ToMesh(source.name + "_Pos");
        negative = negB.ToMesh(source.name + "_Neg");
        return true;
    }

    #endregion

    #region Triangle split

    /// <summary>
    /// 삼각형 하나를 평면 기준으로 분류/분할해서 양쪽 빌더에 넣는다.
    /// </summary>
    static void SplitTriangle(int i0, int i1, int i2, int sub, float[] dist,
        MeshBuilder posB, MeshBuilder negB, List<(Vector3, Vector3)> segments)
    {
        bool s0 = dist[i0] >= 0f, s1 = dist[i1] >= 0f, s2 = dist[i2] >= 0f;

        // 세 정점이 모두 같은 쪽 → 그대로 복사
        if (s0 == s1 && s1 == s2)
        {
            (s0 ? posB : negB).AddOriginalTriangle(i0, i1, i2, sub);
            return;
        }

        // 혼자 반대편에 있는 정점(a)을 찾고, 감김 순서를 유지하도록 (a,b,c) 로 회전.
        int a, b, c;
        if (s0 != s1 && s0 != s2) { a = i0; b = i1; c = i2; }
        else if (s1 != s0 && s1 != s2) { a = i1; b = i2; c = i0; }
        else { a = i2; b = i0; c = i1; }

        bool aPositive = dist[a] >= 0f;
        var lone = aPositive ? posB : negB;   // a 만 있는 쪽 (삼각형 1개)
        var pair = aPositive ? negB : posB;   // b,c 가 있는 쪽 (사각형 → 삼각형 2개)

        // 엣지 a-b, c-a 위의 교차점. 양쪽 빌더가 각각 자기 정점으로 만든다.
        int loneAB = lone.GetEdgeVertex(a, b, dist);
        int loneCA = lone.GetEdgeVertex(c, a, dist);
        int pairAB = pair.GetEdgeVertex(a, b, dist);
        int pairCA = pair.GetEdgeVertex(c, a, dist);

        // 순서: a → ab → b → c → ca 가 원래 삼각형과 같은 감김 방향.
        lone.AddTriangle(lone.MapOriginal(a), loneAB, loneCA, sub);
        int pb = pair.MapOriginal(b), pc = pair.MapOriginal(c);
        pair.AddTriangle(pairAB, pb, pc, sub);
        pair.AddTriangle(pairAB, pc, pairCA, sub);

        segments.Add((lone.GetPosition(loneAB), lone.GetPosition(loneCA)));
    }

    #endregion

    #region Cap (절단면) 외곽선 구성

    /// <summary>
    /// 순서 없는 절단 선분들을 이어서 닫힌 외곽선(loop) 목록으로 만든다.
    /// </summary>
    static List<List<Vector3>> BuildLoops(List<(Vector3 a, Vector3 b)> segments)
    {
        // 1) 가까운 점들을 하나로 용접하고 인접 리스트 구성
        var keyToId = new Dictionary<Vector3Int, int>();
        var points = new List<Vector3>();
        var adjacency = new List<List<int>>();

        int GetId(Vector3 p)
        {
            var key = new Vector3Int(
                Mathf.RoundToInt(p.x / WeldEpsilon),
                Mathf.RoundToInt(p.y / WeldEpsilon),
                Mathf.RoundToInt(p.z / WeldEpsilon));
            if (!keyToId.TryGetValue(key, out int id))
            {
                id = points.Count;
                keyToId.Add(key, id);
                points.Add(p);
                adjacency.Add(new List<int>(2));
            }
            return id;
        }

        foreach (var (a, b) in segments)
        {
            int ia = GetId(a), ib = GetId(b);
            if (ia == ib) continue;                 // 길이 0 선분 무시
            if (adjacency[ia].Contains(ib)) continue; // 중복 선분 무시
            adjacency[ia].Add(ib);
            adjacency[ib].Add(ia);
        }

        // 2) 인접 리스트를 따라가며 loop 추출
        var loops = new List<List<Vector3>>();
        var usedEdge = new HashSet<long>();
        long EdgeKey(int x, int y) => x < y ? ((long)x << 32) | (uint)y : ((long)y << 32) | (uint)x;

        for (int start = 0; start < points.Count; start++)
        {
            foreach (int firstNext in adjacency[start])
            {
                if (usedEdge.Contains(EdgeKey(start, firstNext))) continue;

                var loop = new List<Vector3> { points[start] };
                int prev = start, cur = firstNext;
                usedEdge.Add(EdgeKey(prev, cur));

                // 시작점으로 돌아올 때까지 아직 안 쓴 엣지를 따라간다.
                while (cur != start)
                {
                    loop.Add(points[cur]);
                    int next = -1;
                    foreach (int n in adjacency[cur])
                    {
                        if (n != prev && !usedEdge.Contains(EdgeKey(cur, n))) { next = n; break; }
                    }
                    if (next < 0) break; // 열린 선분(비정상 메시) → 여기까지만 사용
                    usedEdge.Add(EdgeKey(cur, next));
                    prev = cur;
                    cur = next;
                }

                if (loop.Count >= 3) loops.Add(loop);
            }
        }
        return loops;
    }

    /// <summary>
    /// 2D 다각형 Ear-clipping 삼각분할. 반환값은 입력 인덱스 3개씩.
    /// 입력 다각형은 반시계(CCW) 방향이어야 한다.
    /// </summary>
    static List<int> Triangulate(List<Vector2> poly)
    {
        var result = new List<int>();
        int n = poly.Count;
        if (n < 3) return result;

        var idx = new List<int>(n);
        for (int i = 0; i < n; i++) idx.Add(i);

        int guard = n * n; // 무한루프 방지
        while (idx.Count > 3 && guard-- > 0)
        {
            bool clipped = false;
            int count = idx.Count;
            int collinear = -1;
            for (int i = 0; i < count; i++)
            {
                int ip = idx[(i - 1 + count) % count];
                int ic = idx[i];
                int inx = idx[(i + 1) % count];
                Vector2 p = poly[ip], c = poly[ic], nx = poly[inx];

                float turn = Cross(c - p, nx - c);

                // 일직선 위의 점은 귀가 될 수 없음. 나중에 막히면 제거하려고 기억만 해둔다.
                if (Mathf.Abs(turn) <= 1e-10f)
                {
                    if (collinear < 0) collinear = i;
                    continue;
                }

                // 볼록(convex) 꼭짓점만 귀(ear) 후보
                if (turn < 0f) continue;

                // 다른 꼭짓점이 이 삼각형 안에 있으면 귀가 아님
                bool contains = false;
                for (int j = 0; j < count; j++)
                {
                    int ij = idx[j];
                    if (ij == ip || ij == ic || ij == inx) continue;
                    if (PointInTriangle(poly[ij], p, c, nx)) { contains = true; break; }
                }
                if (contains) continue;

                result.Add(ip); result.Add(ic); result.Add(inx);
                idx.RemoveAt(i);
                clipped = true;
                break;
            }

            // 귀가 없으면 일직선 점을 하나 제거(넓이 0이라 삼각형 불필요)하고 재시도
            if (!clipped && collinear >= 0)
            {
                idx.RemoveAt(collinear);
                continue;
            }

            // 그래도 못 찾으면(자기교차 등 비정상) 남은 부분은 부채꼴로 채움
            if (!clipped) break;
        }

        for (int i = 1; i + 1 < idx.Count; i++)
        {
            result.Add(idx[0]); result.Add(idx[i]); result.Add(idx[i + 1]);
        }
        return result;
    }

    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(b - a, p - a);
        float d2 = Cross(c - b, p - b);
        float d3 = Cross(a - c, p - c);
        return d1 >= 0f && d2 >= 0f && d3 >= 0f;
    }

    #endregion

    #region Piece GameObject 생성

    /// <summary>
    /// 조각 메시로 새 GameObject를 만들고 원본의 렌더러/콜라이더/리지드바디 설정을 복사한다.
    /// </summary>
    static GameObject CreatePiece(GameObject src, Mesh mesh, string suffix, float massRatio, Options options, bool isIn = false)
    {
        var go = new GameObject(src.name + suffix)
        {
            layer = src.layer,
            tag = src.tag,
        };
        go.isStatic = false;

        // 트랜스폼: 같은 부모 아래 같은 위치/회전/스케일 → 로컬 좌표 메시가 그대로 맞는다.
        var st = src.transform;
        go.transform.SetParent(st.parent, false);
        go.transform.SetLocalPositionAndRotation(st.localPosition, st.localRotation);
        go.transform.localScale = st.localScale;

        // 메시 & 렌더러 (머터리얼은 원본 sharedMaterials 그대로 사용)
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var srcRenderer = src.GetComponent<MeshRenderer>();
        var renderer = go.AddComponent<MeshRenderer>();
        if (srcRenderer != null)
        {
            renderer.sharedMaterials = srcRenderer.sharedMaterials;
            renderer.shadowCastingMode = srcRenderer.shadowCastingMode;
            renderer.receiveShadows = srcRenderer.receiveShadows;
            renderer.renderingLayerMask = srcRenderer.renderingLayerMask;
        }

        // 콜라이더는 Rigidbody 보다 먼저 붙인다 (non-convex MeshCollider + 동적 RB 경고 방지).
        var srcRb = src.GetComponent<Rigidbody>();
        bool dynamicBody = srcRb != null && !srcRb.isKinematic;
        var srcCol = src.GetComponent<Collider>();
        if (srcCol != null)
            CopyCollider(srcCol, go, mesh, options.colliderMode, dynamicBody);

        if (srcRb != null)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(0.0001f, srcRb.mass * massRatio);
            rb.linearDamping = srcRb.linearDamping;
            rb.angularDamping = srcRb.angularDamping;
            rb.useGravity = srcRb.useGravity;
            rb.isKinematic = srcRb.isKinematic;
            rb.interpolation = srcRb.interpolation;
            rb.collisionDetectionMode = srcRb.collisionDetectionMode;
            rb.constraints = srcRb.constraints;

            if (!srcRb.isKinematic)
            {
                // 조각 중심 위치에서의 원본 속도를 물려받아 자연스럽게 이어지도록.
                Vector3 worldCenter = go.transform.TransformPoint(mesh.bounds.center);
                rb.linearVelocity = srcRb.GetPointVelocity(worldCenter);
                rb.angularVelocity = srcRb.angularVelocity;
            }
        }
        else if (options.addRigidBody && isIn)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(0.0001f, 1 * massRatio);
            rb.useGravity = true;
            rb.isKinematic = false;
        }

        if (options.addGravityController && isIn)
        {
            go.AddComponent<GravityController>();
        }

        // Sliceable 설정을 조각에도 복사 → 조각을 다시 자를 수 있다.
        var srcSliceable = src.GetComponent<Sliceable>();
        if (srcSliceable != null)
        {
            var dst = go.AddComponent<Sliceable>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(srcSliceable), dst);
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
            Undo.RegisterCreatedObjectUndo(go, "Mesh Slice");
#endif
        return go;
    }

    /// <summary>원본 콜라이더 설정을 조각에 맞게 복사.</summary>
    static void CopyCollider(Collider src, GameObject dst, Mesh mesh, ColliderMode mode, bool dynamicBody)
    {
        Collider col;
        Bounds b = mesh.bounds;

        if (mode == ColliderMode.MatchOriginalType && !(src is MeshCollider))
        {
            switch (src)
            {
                case BoxCollider _:
                    var box = dst.AddComponent<BoxCollider>();
                    box.center = b.center;
                    box.size = b.size;
                    col = box;
                    break;
                case SphereCollider _:
                    var sphere = dst.AddComponent<SphereCollider>();
                    sphere.center = b.center;
                    sphere.radius = Mathf.Max(b.extents.x, b.extents.y, b.extents.z);
                    col = sphere;
                    break;
                case CapsuleCollider _:
                    var cap = dst.AddComponent<CapsuleCollider>();
                    // 가장 긴 축을 캡슐 방향으로
                    Vector3 size = b.size;
                    int dir = size.x >= size.y && size.x >= size.z ? 0 : (size.y >= size.z ? 1 : 2);
                    cap.direction = dir;
                    cap.center = b.center;
                    cap.height = size[dir];
                    cap.radius = Mathf.Max(size[(dir + 1) % 3], size[(dir + 2) % 3]) * 0.5f;
                    col = cap;
                    break;
                default:
                    col = AddMeshCollider(dst, mesh, true);
                    break;
            }
        }
        else
        {
            // 원본이 MeshCollider 면 convex 설정 유지, 동적 RB 라면 convex 강제.
            bool convex = true;
            if (src is MeshCollider srcMesh && mode == ColliderMode.MatchOriginalType)
                convex = srcMesh.convex || dynamicBody;
            col = AddMeshCollider(dst, mesh, convex);
        }

        col.isTrigger = src.isTrigger;
        col.sharedMaterial = src.sharedMaterial;
        col.includeLayers = src.includeLayers;
        col.excludeLayers = src.excludeLayers;
        col.layerOverridePriority = src.layerOverridePriority;
        col.enabled = src.enabled;
    }

    static MeshCollider AddMeshCollider(GameObject go, Mesh mesh, bool convex)
    {
        var mc = go.AddComponent<MeshCollider>();
        mc.convex = convex;
        mc.sharedMesh = mesh;
        return mc;
    }

    static void AddImpulse(GameObject go, Vector3 impulse)
    {
        var rb = go.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
            rb.AddForce(impulse, ForceMode.Impulse);
    }

    static void DestroyObject(GameObject go)
    {
        if (Application.isPlaying)
        {
            Object.Destroy(go);
            return;
        }
#if UNITY_EDITOR
        Undo.DestroyObjectImmediate(go);
#else
        Object.DestroyImmediate(go);
#endif
    }

    /// <summary>닫힌 메시의 부호 있는 부피 (발산 정리, 원점 기준 사면체 합).</summary>
    static float SignedVolume(Mesh mesh)
    {
        var v = mesh.vertices;
        float vol = 0f;
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var t = mesh.GetTriangles(s);
            for (int i = 0; i < t.Length; i += 3)
                vol += Vector3.Dot(v[t[i]], Vector3.Cross(v[t[i + 1]], v[t[i + 2]])) / 6f;
        }
        return vol;
    }

    #endregion

    #region 내부 자료구조

    /// <summary>원본 메시 정점 속성 캐시. 없는 속성은 빈 배열.</summary>
    class SourceData
    {
        public readonly Vector3[] positions;
        public readonly Vector3[] normals;
        public readonly Vector4[] tangents;
        public readonly Vector2[] uv0;
        public readonly Color[] colors;
        public bool HasNormals => normals.Length > 0;
        public bool HasTangents => tangents.Length > 0;
        public bool HasUV => uv0.Length > 0;
        public bool HasColors => colors.Length > 0;

        public SourceData(Mesh m)
        {
            positions = m.vertices;
            normals = m.normals;
            tangents = m.tangents;
            uv0 = m.uv;
            colors = m.colors;
        }
    }

    /// <summary>
    /// 한쪽 조각의 정점/삼각형을 모으는 빌더.
    /// 원본 정점과 교차 정점을 캐시해서 공유(중복 정점 최소화)한다.
    /// </summary>
    class MeshBuilder
    {
        readonly SourceData src;
        readonly List<Vector3> positions = new();
        readonly List<Vector3> normals = new();
        readonly List<Vector4> tangents = new();
        readonly List<Vector2> uvs = new();
        readonly List<Color> colors = new();
        readonly List<int>[] submeshes;

        // 원본 정점 인덱스 → 새 인덱스
        readonly Dictionary<int, int> originalMap = new();
        // 원본 엣지(min,max) → 교차 정점 새 인덱스
        readonly Dictionary<long, int> edgeMap = new();

        public MeshBuilder(SourceData src, int subCount)
        {
            this.src = src;
            submeshes = new List<int>[subCount];
            for (int i = 0; i < subCount; i++) submeshes[i] = new List<int>();
        }

        public bool IsEmpty
        {
            get
            {
                foreach (var s in submeshes) if (s.Count > 0) return false;
                return true;
            }
        }

        public Vector3 GetPosition(int index) => positions[index];

        /// <summary>원본 정점을 이 빌더의 정점으로 매핑 (없으면 복사).</summary>
        public int MapOriginal(int i)
        {
            if (originalMap.TryGetValue(i, out int idx)) return idx;
            idx = positions.Count;
            positions.Add(src.positions[i]);
            if (src.HasNormals) normals.Add(src.normals[i]);
            if (src.HasTangents) tangents.Add(src.tangents[i]);
            if (src.HasUV) uvs.Add(src.uv0[i]);
            if (src.HasColors) colors.Add(src.colors[i]);
            originalMap.Add(i, idx);
            return idx;
        }

        /// <summary>
        /// 엣지 i-j 와 평면의 교차 정점. 속성은 선형 보간.
        /// 양쪽 빌더/인접 삼각형에서 완전히 같은 값이 나오도록 항상 작은 인덱스 → 큰 인덱스 방향으로 보간한다.
        /// </summary>
        public int GetEdgeVertex(int i, int j, float[] dist)
        {
            if (i > j) (i, j) = (j, i);
            long key = ((long)i << 32) | (uint)j;
            if (edgeMap.TryGetValue(key, out int idx)) return idx;

            float t = dist[i] / (dist[i] - dist[j]);
            idx = positions.Count;
            positions.Add(Vector3.Lerp(src.positions[i], src.positions[j], t));
            if (src.HasNormals) normals.Add(Vector3.Lerp(src.normals[i], src.normals[j], t).normalized);
            if (src.HasTangents) tangents.Add(Vector4.Lerp(src.tangents[i], src.tangents[j], t));
            if (src.HasUV) uvs.Add(Vector2.Lerp(src.uv0[i], src.uv0[j], t));
            if (src.HasColors) colors.Add(Color.Lerp(src.colors[i], src.colors[j], t));
            edgeMap.Add(key, idx);
            return idx;
        }

        public void AddOriginalTriangle(int a, int b, int c, int sub)
            => AddTriangle(MapOriginal(a), MapOriginal(b), MapOriginal(c), sub);

        public void AddTriangle(int a, int b, int c, int sub)
        {
            var list = submeshes[sub];
            list.Add(a); list.Add(b); list.Add(c);
        }

        /// <summary>
        /// 외곽선 하나를 삼각분할해서 절단면으로 추가.
        /// 절단면은 하드 엣지가 되도록 별도 정점을 만들고 capNormal 을 법선으로 쓴다.
        /// UV 는 평면에 투영한 평면 매핑.
        /// </summary>
        public void AddCap(List<Vector3> loop, Vector3 capNormal, int sub, float uvScale)
        {
            // 평면 위 2D 좌표계 (u, v) 구성
            Vector3 u = Vector3.Cross(capNormal, Mathf.Abs(capNormal.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(capNormal, u);

            var poly = new List<Vector2>(loop.Count);
            foreach (var p in loop) poly.Add(new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v)));

            // Ear-clipping 은 CCW 입력을 가정 → 필요 시 뒤집기
            float area = 0f;
            for (int i = 0; i < poly.Count; i++)
                area += Cross(poly[i], poly[(i + 1) % poly.Count]);
            var order = new List<Vector3>(loop);
            if (area < 0f) { poly.Reverse(); order.Reverse(); }

            var localTris = Triangulate(poly);
            if (localTris.Count == 0) return;

            // 캡 정점 추가
            int baseIndex = positions.Count;
            var tangent = new Vector4(u.x, u.y, u.z, 1f);
            for (int i = 0; i < order.Count; i++)
            {
                positions.Add(order[i]);
                if (src.HasNormals) normals.Add(capNormal);
                if (src.HasTangents) tangents.Add(tangent);
                if (src.HasUV) uvs.Add(poly[i] * uvScale);
                if (src.HasColors) colors.Add(Color.white);
            }

            // Unity 는 cross(b-a, c-a) 방향이 앞면 법선. capNormal 과 반대면 감김 뒤집기.
            for (int i = 0; i < localTris.Count; i += 3)
            {
                int a = baseIndex + localTris[i];
                int b = baseIndex + localTris[i + 1];
                int c = baseIndex + localTris[i + 2];
                Vector3 n = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                if (Vector3.Dot(n, capNormal) < 0f) (b, c) = (c, b);
                AddTriangle(a, b, c, sub);
            }
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (positions.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(positions);
            if (normals.Count == positions.Count) mesh.SetNormals(normals);
            if (tangents.Count == positions.Count) mesh.SetTangents(tangents);
            if (uvs.Count == positions.Count) mesh.SetUVs(0, uvs);
            if (colors.Count == positions.Count) mesh.SetColors(colors);

            mesh.subMeshCount = submeshes.Length;
            for (int i = 0; i < submeshes.Length; i++)
                mesh.SetTriangles(submeshes[i], i, false);

            if (normals.Count != positions.Count) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    #endregion
}
