using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 모양 절단: 구체 또는 임의의 닫힌 메시(커터) 모양대로 대상을 파내거나 도려낸다.
/// 내부적으로 <see cref="MeshCSG"/> 불리언 연산을 사용한다.
/// <para>
/// - outside = 원본 − 커터 (파인 나머지)<br/>
/// - inside  = 원본 ∩ 커터 (도려낸 덩어리, Options.keepInside 가 true 일 때만 생성)
/// </para>
/// 파낸 면(커터 표면)에는 원본 머터리얼(capSubmeshIndex)이 그대로 쓰인다.
/// 주의: 대상과 커터 모두 닫힌 메시여야 하며, 폴리곤 수에 비례해 비용이 커지므로
/// 고폴리 메시를 매 프레임 자르는 용도로는 적합하지 않다.
/// </summary>
public static partial class MeshSlicer
{
    #region Public API

    /// <summary>
    /// 월드 공간 구체로 대상을 절단한다.
    /// </summary>
    /// <param name="worldCenter">구 중심 (월드)</param>
    /// <param name="worldRadius">구 반지름 (월드)</param>
    /// <param name="outside">구 바깥에 남은 조각들 (떨어진 덩어리마다 1개)</param>
    /// <param name="inside">구 안쪽 조각들 (keepInside 가 false 면 빈 리스트)</param>
    /// <returns>구가 메시와 실제로 겹쳐서 절단이 일어났으면 true</returns>
    public static bool CutBySphere(GameObject target, Vector3 worldCenter, float worldRadius, Options options,
        out List<GameObject> outside, out List<GameObject> inside)
    {
        // IcoSphere 가져옴
        var sphere = MeshCSG.GetIcoSphere(options.sphereSubdivisions);

        // 단위 구를 원하는 위치와 크기로 옮기는 배치 행렬.
        //   - T (이동) = worldCenter: 원점에 있던 구를 구 중점으로 이동.
        //   - R (회전) = identity: 구는 돌려도 모양이 같아서 회전 불필요.
        //   - S (스케일) = worldRadius: 반지름 1을 원하는 반지름으로 늘림. 세 축 모두 같은 값이라 찌그러지지 않음.
        var cutterMatrix = Matrix4x4.TRS(worldCenter, Quaternion.identity, Vector3.one * worldRadius);

        // 메시 커팅 전달
        return CutByMesh(target, sphere, cutterMatrix, options, out outside, out inside);
    }

    /// <summary>
    /// 임의의 닫힌 메시 모양으로 대상을 절단한다.
    /// </summary>
    /// <param name="cutterMesh">커터 메시 (Read/Write Enabled, 닫힌 메시)</param>
    /// <param name="cutterLocalToWorld">커터 배치 행렬 (보통 cutterTransform.localToWorldMatrix)</param>
    public static bool CutByMesh(GameObject target, Mesh cutterMesh, Matrix4x4 cutterLocalToWorld, Options options, out List<GameObject> outside, out List<GameObject> inside)
    {
        outside = new List<GameObject>();
        inside = new List<GameObject>();

        // 타겟에서 메시 추출
        if (!TryGetReadableMesh(target, out Mesh srcMesh)) 
            return false;

        // 커터 메시 검증
        if (cutterMesh == null || !cutterMesh.isReadable)
        {
            Debug.LogWarning("[MeshSlicer] 커터 메시가 없거나 Read/Write Enabled 가 아닙니다.");
            return false;
        }

        // 1) 빠른 거부: 월드 바운즈가 안 겹치면 CSG 를 돌릴 필요 없음
        // 커터 메시의 바운드 가져오기
        Bounds cutterBounds = TransformBounds(cutterMesh.bounds, cutterLocalToWorld);
        // 바운드 겹치는지 체크
        if (target.TryGetComponent(out Renderer r) &&
            !r.bounds.Intersects(cutterBounds))
            return false;

        // 2) 모든 연산은 대상 로컬 공간에서 수행 (결과 메시를 그대로 대상 트랜스폼에 얹기 위함)
        // 커터를 로컬 좌표로 이동.
        Matrix4x4 cutterToTarget = target.transform.worldToLocalMatrix * cutterLocalToWorld;
        int subCount = srcMesh.subMeshCount; // 서브 메시 갯수
        int capSub = Mathf.Clamp(options.capSubmeshIndex, 0, subCount - 1); 

        // 대상 메시를 폴리곤 단위로 분할
        var targetPolys = MeshCSG.FromMesh(srcMesh);
        // 커터 메시를 폴리곤 단위로 분할
        var cutterPolys = MeshCSG.FromCutterMesh(cutterMesh, cutterToTarget, capSub, options.capUVScale);

        // 3) 차집합/교집합을 한 번에 계산. 교집합이 비어 있으면 실제로는 안 닿은 것
        MeshCSG.SubtractAndIntersect(targetPolys, cutterPolys, out var outsidePolys, out var insidePolys);
        if (insidePolys.Count == 0) 
            return false;

        bool hasTangents = srcMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent);
        var outMeshes = BuildPieceMeshes(outsidePolys, subCount, srcMesh.name + "_Out", hasTangents, options.separateIslands);
        var inMeshes = BuildPieceMeshes(insidePolys, subCount, srcMesh.name + "_In", hasTangents, options.separateIslands);

        // 4) 질량 배분: 생성된 모든 조각(버려지는 안쪽 포함) 부피 합 대비 비율
        float total = 0f;
        foreach (var m in outMeshes) 
            total += Mathf.Abs(SignedVolume(m));
        foreach (var m in inMeshes) 
            total += Mathf.Abs(SignedVolume(m));
        float Ratio(Mesh m) => total > 1e-8f ? Mathf.Abs(SignedVolume(m)) / total : 1f;

        // 5) GameObject 생성
        Vector3 cutterCenter = cutterBounds.center;
        foreach (var m in outMeshes)
        {
            var go = CreatePiece(target, m, "_Out", Ratio(m), options);
            outside.Add(go);
            // 바깥 조각은 커터 중심에서 멀어지는 방향으로 밀어냄
            if (options.separationImpulse > 0f)
            {
                Vector3 dir = go.transform.TransformPoint(m.bounds.center) - cutterCenter;
                if (dir.sqrMagnitude > 1e-8f) AddImpulse(go, dir.normalized * options.separationImpulse);
            }
        }

        if (options.keepInside)
        {
            foreach (var m in inMeshes)
                inside.Add(CreatePiece(target, m, "_In", Ratio(m), options));
        }

        if (options.destroyOriginal)
            DestroyObject(target);

        return true;
    }

    #endregion

    #region 내부 구현

    /// <summary>
    /// 타겟의 메시 추출 시도 메소드.
    /// </summary>
    /// <param name="target"></param>
    /// <param name="mesh"></param>
    /// <returns></returns>
    static bool TryGetReadableMesh(GameObject target, out Mesh mesh)
    {
        mesh = null;
        // 타겟이 없으면 에러
        if (target == null) 
            return false;
        // 타겟에 메시 필터 컴포 없으면 || 메시 필터 내 메시가 널이면 에러
        if (!target.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
        {
            Debug.LogWarning($"[MeshSlicer] '{target.name}' 에 MeshFilter/Mesh 가 없습니다.", target);
            return false;
        }
        // 결과값에 메시 세팅
        mesh = filter.sharedMesh;
        // 읽기 전용이 아니면 에러
        if (!mesh.isReadable)
        {
            Debug.LogWarning($"[MeshSlicer] '{mesh.name}' 메시가 Read/Write Enabled 가 아닙니다.", target);
            return false;
        }
        return true;
    }

    /// <summary>폴리곤 → 메시 변환 후, 필요하면 떨어진 덩어리별로 분리.</summary>
    static List<Mesh> BuildPieceMeshes(List<MeshCSG.Polygon> polys, int subCount, string name, bool tangents, bool separate)
    {
        var result = new List<Mesh>();
        if (polys.Count == 0) return result;

        var mesh = MeshCSG.ToMesh(polys, subCount, name, tangents);
        if (!separate)
        {
            result.Add(mesh);
            return result;
        }

        result.AddRange(SplitIslands(mesh));
        return result;
    }

    /// <summary>
    /// 연결되지 않은 덩어리(island)별로 메시를 분리한다.
    /// - 같은 위치의 정점(UV/법선 이음새)은 연결된 것으로 본다.
    /// - 부피가 음수인 껍질(= 속이 빈 공동의 안쪽 면)은 그것을 감싸는 덩어리에 합친다.
    ///   (구체가 물체 한가운데를 파내 내부 빈 공간이 생긴 경우 별도 조각으로 떨어지지 않도록)
    /// </summary>
    static List<Mesh> SplitIslands(Mesh mesh)
    {
        var verts = mesh.vertices;
        int n = verts.Length;

        // Union-Find
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        int Find(int x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }
        void Union(int x, int y)
        {
            x = Find(x); y = Find(y);
            if (x != y) parent[x] = y;
        }

        // 같은 위치 정점 연결
        var byPos = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < n; i++)
        {
            var key = new Vector3Int(
                Mathf.RoundToInt(verts[i].x / WeldEpsilon),
                Mathf.RoundToInt(verts[i].y / WeldEpsilon),
                Mathf.RoundToInt(verts[i].z / WeldEpsilon));
            if (byPos.TryGetValue(key, out int other)) Union(i, other);
            else byPos.Add(key, i);
        }

        // 삼각형으로 연결
        int subCount = mesh.subMeshCount;
        var subTris = new int[subCount][];
        for (int s = 0; s < subCount; s++)
        {
            var t = subTris[s] = mesh.GetTriangles(s);
            for (int i = 0; i < t.Length; i += 3) { Union(t[i], t[i + 1]); Union(t[i], t[i + 2]); }
        }

        // 루트별 island 수집 (서브메시별 삼각형 리스트 + 부피 + 바운즈)
        var islandOf = new Dictionary<int, int>();
        var islands = new List<Island>();
        for (int s = 0; s < subCount; s++)
        {
            var t = subTris[s];
            for (int i = 0; i < t.Length; i += 3)
            {
                int root = Find(t[i]);
                if (!islandOf.TryGetValue(root, out int id))
                {
                    id = islands.Count;
                    islandOf.Add(root, id);
                    islands.Add(new Island(subCount, verts[t[i]]));
                }
                islands[id].Add(s, t[i], t[i + 1], t[i + 2], verts);
            }
        }

        if (islands.Count <= 1) return new List<Mesh> { mesh };

        // 음수 부피 껍질(공동)을 감싸는 양수 덩어리에 병합
        var solids = islands.FindAll(x => x.volume >= 0f);
        if (solids.Count == 0) return new List<Mesh> { mesh };
        foreach (var hole in islands)
        {
            if (hole.volume >= 0f) continue;
            Island best = null;
            foreach (var s in solids)
            {
                if (!s.bounds.Contains(hole.bounds.center)) continue;
                if (best == null || s.bounds.size.sqrMagnitude < best.bounds.size.sqrMagnitude) best = s;
            }
            // 감싸는 덩어리를 못 찾으면 가장 큰 덩어리에 붙임
            if (best == null)
                foreach (var s in solids)
                    if (best == null || s.volume > best.volume) best = s;
            best.Merge(hole);
        }

        // 삼각형 4개 미만(닫힌 입체가 될 수 없는 부스러기)은 버림
        var result = new List<Mesh>();
        int index = 0;
        foreach (var s in solids)
        {
            if (s.TriangleCount < 4) continue;
            result.Add(ExtractIsland(mesh, s, $"{mesh.name}_{index++}"));
        }
        return result;
    }

    /// <summary>분리 중인 덩어리 하나의 정보.</summary>
    class Island
    {
        public readonly List<int>[] tris;
        public float volume;
        public Bounds bounds;

        public Island(int subCount, Vector3 firstPoint)
        {
            tris = new List<int>[subCount];
            for (int i = 0; i < subCount; i++) tris[i] = new List<int>();
            bounds = new Bounds(firstPoint, Vector3.zero);
        }

        public int TriangleCount
        {
            get { int c = 0; foreach (var t in tris) c += t.Count / 3; return c; }
        }

        public void Add(int sub, int a, int b, int c, Vector3[] v)
        {
            tris[sub].Add(a); tris[sub].Add(b); tris[sub].Add(c);
            volume += Vector3.Dot(v[a], Vector3.Cross(v[b], v[c])) / 6f;
            bounds.Encapsulate(v[a]); bounds.Encapsulate(v[b]); bounds.Encapsulate(v[c]);
        }

        public void Merge(Island other)
        {
            for (int i = 0; i < tris.Length; i++) tris[i].AddRange(other.tris[i]);
            volume += other.volume;
            bounds.Encapsulate(other.bounds);
        }
    }

    /// <summary>island 가 쓰는 정점만 골라 새 메시로 만든다.</summary>
    static Mesh ExtractIsland(Mesh src, Island island, string name)
    {
        var pos = src.vertices;
        var nrm = src.normals;
        var tan = src.tangents;
        var uv = src.uv;
        var col = src.colors;

        var remap = new Dictionary<int, int>();
        var p = new List<Vector3>(); var nl = new List<Vector3>(); var tl = new List<Vector4>();
        var ul = new List<Vector2>(); var cl = new List<Color>();
        int Map(int i)
        {
            if (remap.TryGetValue(i, out int idx)) return idx;
            idx = p.Count;
            p.Add(pos[i]);
            if (nrm.Length > 0) nl.Add(nrm[i]);
            if (tan.Length > 0) tl.Add(tan[i]);
            if (uv.Length > 0) ul.Add(uv[i]);
            if (col.Length > 0) cl.Add(col[i]);
            remap.Add(i, idx);
            return idx;
        }

        var subs = new List<int>[island.tris.Length];
        for (int s = 0; s < subs.Length; s++)
        {
            subs[s] = new List<int>(island.tris[s].Count);
            foreach (int i in island.tris[s]) subs[s].Add(Map(i));
        }

        var mesh = new Mesh { name = name, indexFormat = src.indexFormat };
        mesh.SetVertices(p);
        if (nl.Count == p.Count) mesh.SetNormals(nl);
        if (tl.Count == p.Count) mesh.SetTangents(tl);
        if (ul.Count == p.Count) mesh.SetUVs(0, ul);
        if (cl.Count == p.Count) mesh.SetColors(cl);
        mesh.subMeshCount = subs.Length;
        for (int s = 0; s < subs.Length; s++) mesh.SetTriangles(subs[s], s, false);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// 로컬 바운즈를 행렬로 변환한 월드 AABB.
    /// </summary>
    static Bounds TransformBounds(Bounds b, Matrix4x4 m)
    {
        // 변환한 중심점에 크기 0인 상자로 세팅
        var result = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);


        Vector3 e = b.extents;
        for (int i = 0; i < 8; i++)
        {
            // 비트 트릭으로 꼭짓점 8개를 순회.
            // i의 세 비트가 각각 x, y, z 방향이 −인지 +인지 정함.
            var corner = b.center + new Vector3
                (
                    (i & 1) == 0 ?
                        -e.x :
                        e.x,
                    (i & 2) == 0 ?
                        -e.y :
                        e.y,
                    (i & 4) == 0 ?
                        -e.z :
                        e.z
                );
            // 각 꼭지점을 행렬로 월드로 옮기고, Encapsulate 로 그 점까지 포함하도록 상자를 넓힘.
            // MultiplyPoint3x4는 이동·회전·스케일만 있는 일반 변환 행렬용. 투영 나눗셈을 하는 MultiplyPoint보다 빠름
            result.Encapsulate(m.MultiplyPoint3x4(corner));
        } // 8회 반복시 모든 곡지점 다 감싸는 월드 AABB 완성
        return result;
    }

    #endregion
}
