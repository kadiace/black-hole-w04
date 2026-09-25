using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 구 전용 "버텍스 사영" 절단 (중점 + 반지름 방식).
/// <para>
/// 1) 구 중점에서 각 버텍스까지 거리를 구해 반지름보다 가까우면 편집 대상으로 표시<br/>
/// 2) 편집 대상 버텍스를 구 표면으로 사영(중점→버텍스 방향으로 반지름 위치까지 이동)<br/>
/// 3) 이동 후 위치로 만든 메시 = 움푹 파인 "남은 조각"<br/>
///    이동 전/후 위치를 합쳐 만든 닫힌 메시 = 떨어져 나간 "덩어리"<br/>
/// 4) 각각 새 GameObject 로 생성 (머터리얼/리지드바디/콜라이더 복사는 평면 절단과 동일)
/// </para>
/// 삼각형을 새로 자르지 않고 버텍스만 옮기므로 CSG 방식(<see cref="CutBySphere"/>)보다 훨씬 가볍지만,
/// 결과 품질은 메시의 버텍스 밀도에 좌우된다 (버텍스가 성긴 메시는 파인 모양이 각지게 나옴).
/// 구 중점이 메시 내부에 있으면 단순 사영은 메시 바깥쪽 구면으로 밀려 부풀어 오르므로,
/// 내부 여부를 검사해서 편집 영역을 구의 반대편(메시 안쪽 구면)으로 매핑한다 (<see cref="MapToInnerCap"/>).
/// 그래도 처리할 수 없는 형태(구가 얇은 판을 관통 등)는 Options.projectionFallback 에 따라
/// 원래 모양 그대로 떼어내거나(SplitAsIs), CSG 로 전환하거나(Csg), 중단한다(None).
/// </summary>
public static partial class MeshSlicer
{
    /// <summary>
    /// 버텍스 사영 방식으로 월드 구체 절단.
    /// </summary>
    /// <param name="worldCenter">구 중점 (월드)</param>
    /// <param name="worldRadius">구 반지름 (월드)</param>
    /// <param name="outside">버텍스가 구 표면으로 밀려 파인 남은 조각 (사영 방식이면 최대 1개, 전부 먹혔으면 비어 있음)</param>
    /// <param name="inside">떨어져 나간 덩어리들 (keepInside 가 false 면 빈 리스트)</param>
    /// <returns>반지름 안에 버텍스가 하나라도 있어서 편집이 일어났으면 true</returns>
    public static bool CutBySphereProjection(GameObject target, Vector3 worldCenter, float worldRadius, Options options,
        out List<GameObject> outside, out List<GameObject> inside)
    {
        outside = new List<GameObject>();
        inside = new List<GameObject>();
        if (worldRadius <= 0f || !TryGetReadableMesh(target, out Mesh src)) return false;

        // 빠른 거부: 바운즈가 안 겹치면 볼 필요 없음
        if (target.TryGetComponent(out Renderer rend) &&
            !rend.bounds.Intersects(new Bounds(worldCenter, Vector3.one * (worldRadius * 2f))))
            return false;

        Matrix4x4 l2w = target.transform.localToWorldMatrix;
        Matrix4x4 w2l = target.transform.worldToLocalMatrix;
        // 월드 법선 → 로컬 법선 변환 행렬 ( (w2l)^-T = l2w^T )
        Matrix4x4 normalW2L = l2w.transpose;

        var positions = src.vertices;
        var normals = src.normals;
        bool hasNormals = normals.Length == positions.Length;
        int n = positions.Length;

        // 1) 편집 대상 판정: 중점까지 거리 < 반지름 인 버텍스
        //    비균등 스케일에서도 진짜 구가 되도록 월드 공간에서 계산한 뒤 로컬로 되돌린다.
        var world = new Vector3[n];
        var edited = new bool[n];
        var dirs = new Vector3[n];                  // 중점 → 버텍스 방향 (월드, 단위벡터)
        int editedCount = 0;

        for (int i = 0; i < n; i++)
        {
            world[i] = l2w.MultiplyPoint3x4(positions[i]);
            Vector3 fromCenter = world[i] - worldCenter;
            float dist = fromCenter.magnitude;
            if (dist >= worldRadius) continue; // 반지름 밖 → 그대로

            // 중점과 정확히 겹친 버텍스는 방향이 없으므로 원래 법선의 반대 방향을 쓴다
            dirs[i] = dist > 1e-6f
                ? fromCenter / dist
                : (hasNormals ? -l2w.MultiplyVector(normals[i]).normalized : Vector3.up);
            edited[i] = true;
            editedCount++;
        }

        if (editedCount == 0) return false;

        // 2) 중점이 메시 내부인지 검사.
        //    - 밖: 중점→버텍스 방향 그대로 구 표면에 사영하면 "메시 안쪽 구면"에 닿는다 → 그대로 사용
        //    - 안: 그대로 사영하면 "메시 바깥쪽 구면(작은 캡)"에 닿아 부풀어 오른다
        //          → 반대편 "메시 안쪽 구면(큰 캡)"으로 보내도록 방향을 다시 매핑한다
        bool centerInside = editedCount < n && IsPointInsideMesh(src, w2l.MultiplyPoint3x4(worldCenter));
        if (centerInside && !MapToInnerCap(src, world, edited, dirs, worldCenter, worldRadius))
        {
            // 편집 영역이 한 덩어리 캡으로 볼 수 없는 경우 (예: 얇은 판을 구가 관통) → 사영 불가
            return FallbackOrFail(target, src, edited, worldCenter, worldRadius, options, out outside, out inside,
                "구가 메시를 관통하는 형태라 버텍스 사영 절단을 할 수 없습니다.");
        }

        // 3) 최종 방향으로 구 표면 위치/법선 계산
        var moved = (Vector3[])positions.Clone();   // 이동 후 위치 (로컬)
        var dentNormals = new Vector3[n];           // 이동한 버텍스의 새 법선 (구 중점을 향함, 로컬)
        for (int i = 0; i < n; i++)
        {
            if (!edited[i]) continue;
            moved[i] = w2l.MultiplyPoint3x4(worldCenter + dirs[i] * worldRadius);
            // 파인 면은 구 중점을 향해야 남은 조각 기준 바깥쪽
            dentNormals[i] = normalW2L.MultiplyVector(-dirs[i]).normalized;
        }


        // 모든 버텍스가 반지름 안 → 통째로 먹힘. 남는 조각 없음.
        bool fullyConsumed = editedCount == n;

        bool hasTangents = src.HasVertexAttribute(VertexAttribute.Tangent);
        Mesh remainingMesh = null;
        Mesh chunkMesh;
        if (fullyConsumed)
        {
            // 통째로 먹힌 경우 덩어리 = 원본 전체
            chunkMesh = Object.Instantiate(src);
            chunkMesh.name = src.name + "_Chunk";
        }
        else
        {
            chunkMesh = BuildChunkMesh(src, moved, edited, dentNormals, hasTangents);

            // 안전장치: 덩어리 부피가 0 이하 = 버텍스가 메시 바깥쪽으로 밀림 (내부 판정/매핑이 실패한 특이 형태)
            if (chunkMesh == null || SignedVolume(chunkMesh) <= 1e-9f)
                return FallbackOrFail(target, src, edited, worldCenter, worldRadius, options, out outside, out inside,
                    "사영 결과가 뒤집혀 버텍스 사영 절단을 할 수 없습니다.");

            remainingMesh = BuildRemainingMesh(src, moved, edited, dentNormals, hasTangents);
        }

        var chunkMeshes = new List<Mesh>();
        if (chunkMesh != null)
        {
            if (options.separateIslands) chunkMeshes.AddRange(SplitIslands(chunkMesh));
            else chunkMeshes.Add(chunkMesh);
        }

        // 질량 배분: 남은 조각 + 덩어리들 부피 비율
        float total = remainingMesh != null ? Mathf.Abs(SignedVolume(remainingMesh)) : 0f;
        foreach (var m in chunkMeshes) total += Mathf.Abs(SignedVolume(m));
        float Ratio(Mesh m) => total > 1e-8f ? Mathf.Abs(SignedVolume(m)) / total : 1f;

        if (remainingMesh != null)
        {
            var remaining = CreatePiece(target, remainingMesh, "_Rest", Ratio(remainingMesh), options);
            outside.Add(remaining);
            // 남은 조각은 구 중점 반대 방향으로 밀어냄
            if (options.separationImpulse > 0f)
            {
                Vector3 away = remaining.transform.TransformPoint(remainingMesh.bounds.center) - worldCenter;
                if (away.sqrMagnitude > 1e-8f) AddImpulse(remaining, away.normalized * options.separationImpulse);
            }
        }

        if (options.keepInside)
        {
            foreach (var m in chunkMeshes)
                inside.Add(CreatePiece(target, m, "_Chunk", Ratio(m), options));
        }

        if (options.destroyOriginal)
            DestroyObject(target);

        return true;
    }

    /// <summary>사영 불가 시 Options.projectionFallback 에 따라 처리.</summary>
    static bool FallbackOrFail(GameObject target, Mesh src, bool[] edited, Vector3 worldCenter, float worldRadius, Options options,
        out List<GameObject> outside, out List<GameObject> inside, string reason)
    {
        switch (options.projectionFallback)
        {
            case ProjectionFallback.SplitAsIs:
                return SplitAsIs(target, src, edited, worldCenter, options, out outside, out inside);
            case ProjectionFallback.Csg:
                return CutBySphere(target, worldCenter, worldRadius, options, out outside, out inside);
            default:
                outside = new List<GameObject>();
                inside = new List<GameObject>();
                Debug.LogWarning($"[MeshSlicer] '{target.name}': {reason}", target);
                return false;
        }
    }

    /// <summary>
    /// 사영 불가 시 대체 처리: 버텍스를 옮기지 않고 원래 모양 그대로 나눈다.
    /// - 남은 조각 = 편집 대상 버텍스를 하나도 안 쓰는 삼각형들 (구에 걸린 부분이 뚫림)
    /// - 덩어리   = 편집 대상 버텍스를 쓰는 삼각형들 (원래 위치 그대로)
    /// 새 면을 만들지 않으므로 뚫린 구멍에 벽면이 없는 "열린" 메시가 된다
    /// (얇은 판이면 거의 티가 안 나지만, 두꺼운 물체는 속이 비어 보임).
    /// 부피를 정의할 수 없으므로 질량은 표면적 비율로 나눈다.
    /// </summary>
    static bool SplitAsIs(GameObject target, Mesh src, bool[] edited, Vector3 worldCenter, Options options,
        out List<GameObject> outside, out List<GameObject> inside)
    {
        outside = new List<GameObject>();
        inside = new List<GameObject>();

        var positions = src.vertices;
        int subCount = src.subMeshCount;
        var keep = new List<int>[subCount];
        var cut = new List<int>[subCount];
        float keepArea = 0f, cutArea = 0f;
        var tris = new List<int>();

        // 삼각형 분류: 편집 대상 버텍스가 하나라도 있으면 떼어낼 쪽
        for (int s = 0; s < subCount; s++)
        {
            keep[s] = new List<int>();
            cut[s] = new List<int>();
            if (src.GetTopology(s) != MeshTopology.Triangles) continue;
            src.GetTriangles(tris, s);
            for (int t = 0; t < tris.Count; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                float area = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]).magnitude * 0.5f;
                bool isCut = edited[a] || edited[b] || edited[c];
                var list = isCut ? cut[s] : keep[s];
                list.Add(a); list.Add(b); list.Add(c);
                if (isCut) cutArea += area; else keepArea += area;
            }
        }

        if (cutArea <= 0f) return false;
        float total = keepArea + cutArea;

        // 원본 메시를 복제해서 삼각형만 바꿔 끼움 (버텍스/UV/법선은 그대로)
        Mesh Build(List<int>[] lists, string suffix)
        {
            var m = Object.Instantiate(src);
            m.name = src.name + suffix;
            for (int s = 0; s < subCount; s++) m.SetTriangles(lists[s], s, false);
            m.RecalculateBounds();
            return m;
        }

        if (keepArea > 0f)
        {
            var go = CreatePiece(target, Build(keep, "_Rest"), "_Rest", keepArea / total, options);
            outside.Add(go);
            if (options.separationImpulse > 0f)
            {
                Vector3 away = go.transform.TransformPoint(go.GetComponent<MeshFilter>().sharedMesh.bounds.center) - worldCenter;
                if (away.sqrMagnitude > 1e-8f) AddImpulse(go, away.normalized * options.separationImpulse);
            }
        }

        if (options.keepInside)
            inside.Add(CreatePiece(target, Build(cut, "_Chunk"), "_Chunk", cutArea / total, options));

        if (options.destroyOriginal)
            DestroyObject(target);

        return true;
    }

    /// <summary>
    /// 중점이 메시 내부일 때: 편집 영역(구 안에 들어온 표면 조각)을 구의 "메시 안쪽 면"으로 보낸다.
    /// <para>
    /// 편집 영역 방향들의 평균을 축(a)으로 잡으면, 편집 영역은 축 주위 극각 θ ∈ [0, θ0] 인 캡에 해당한다
    /// (θ0 = 표면과 구가 만나는 경계선의 극각). 메시 안쪽 구면은 나머지 θ ∈ [θ0, π] 부분이므로
    /// 방위각은 그대로 두고 θ → θ0 + (θ0 − θ)·(π − θ0)/θ0 로 뒤집어 매핑한다.
    /// → 경계(θ0) 버텍스는 제자리, 편집 영역 한가운데(θ=0)는 구 반대편 끝(π)으로 간다.
    /// θ 순서가 뒤집히므로 면의 감김도 자연스럽게 뒤집혀, 파인 면이 구 중점을 향하게 된다.
    /// </para>
    /// </summary>
    /// <param name="dirs">입력: 중점→버텍스 방향. 출력: 매핑된 방향 (편집 대상만 변경)</param>
    /// <returns>편집 영역을 하나의 캡으로 볼 수 있어 매핑에 성공하면 true</returns>
    static bool MapToInnerCap(Mesh src, Vector3[] world, bool[] edited, Vector3[] dirs, Vector3 center, float radius)
    {
        // 축: 편집 대상 방향의 평균
        Vector3 sum = Vector3.zero;
        int count = 0;
        for (int i = 0; i < dirs.Length; i++)
            if (edited[i]) { sum += dirs[i]; count++; }
        // 방향들이 사방으로 퍼져 평균이 거의 0 → 한쪽 캡이 아님 (구가 판을 관통하는 등)
        if (count == 0 || sum.magnitude / count < 0.2f) return false;
        Vector3 axis = sum.normalized;

        // θ0: 편집/비편집 버텍스를 잇는 엣지가 구 표면과 만나는 점들의 극각 평균
        double thetaSum = 0;
        int thetaCount = 0;
        var tris = src.triangles;
        for (int t = 0; t < tris.Length; t += 3)
        {
            for (int e = 0; e < 3; e++)
            {
                int a = tris[t + e], b = tris[t + (e + 1) % 3];
                if (edited[a] == edited[b]) continue;
                int inV = edited[a] ? a : b, outV = edited[a] ? b : a;

                // |in + s(out-in) - c| = r 인 s ∈ (0,1) (in 은 구 안, out 은 구 밖이라 해가 하나 존재)
                Vector3 d = world[outV] - world[inV];
                Vector3 f = world[inV] - center;
                float qa = Vector3.Dot(d, d), qb = Vector3.Dot(f, d), qc = Vector3.Dot(f, f) - radius * radius;
                float disc = qb * qb - qa * qc;
                if (qa < 1e-12f || disc < 0f) continue;
                float s = (-qb + Mathf.Sqrt(disc)) / qa;
                Vector3 hitDir = (world[inV] + d * s - center).normalized;
                thetaSum += Vector3.Angle(axis, hitDir) * Mathf.Deg2Rad;
                thetaCount++;
            }
        }
        if (thetaCount == 0) return false;

        // 경계가 정확한 원이 아닐 수 있으므로 편집 버텍스 중 가장 큰 θ 보다는 크게 잡는다
        float theta0 = (float)(thetaSum / thetaCount);
        for (int i = 0; i < dirs.Length; i++)
            if (edited[i]) theta0 = Mathf.Max(theta0, Vector3.Angle(axis, dirs[i]) * Mathf.Deg2Rad * 1.0001f);
        if (theta0 >= Mathf.PI * 0.999f) return false; // 캡이 구 전체를 덮음 → 매핑 불가

        // 축에 수직인 임의 기준 벡터 (방위각이 정의되지 않는 축 위 버텍스용)
        Vector3 anyPerp = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.99f ? Vector3.up : Vector3.right).normalized;

        for (int i = 0; i < dirs.Length; i++)
        {
            if (!edited[i]) continue;
            float theta = Vector3.Angle(axis, dirs[i]) * Mathf.Deg2Rad;
            float mapped = theta0 + (theta0 - theta) * (Mathf.PI - theta0) / theta0;

            // 방위각 방향 = dirs 에서 축 성분을 뺀 수직 성분
            Vector3 perp = dirs[i] - axis * Vector3.Dot(axis, dirs[i]);
            perp = perp.sqrMagnitude > 1e-10f ? perp.normalized : anyPerp;
            dirs[i] = axis * Mathf.Cos(mapped) + perp * Mathf.Sin(mapped);
        }
        return true;
    }

    /// <summary>
    /// 로컬 좌표 점이 닫힌 메시 내부인지 레이캐스트 홀짝 판정.
    /// 서로 다른 레이 3개를 쏴서 다수결 (레이가 모서리를 정확히 지나는 예외 대비).
    /// </summary>
    public static bool IsPointInsideMesh(Mesh mesh, Vector3 localPoint)
    {
        if (!mesh.bounds.Contains(localPoint)) return false;

        var v = mesh.vertices;
        var t = mesh.triangles;
        Vector3[] rays =
        {
            new Vector3(0.8123f, 0.4567f, 0.3627f).normalized,
            new Vector3(-0.3217f, 0.8531f, -0.4103f).normalized,
            new Vector3(0.2911f, -0.3977f, 0.8703f).normalized,
        };

        int votes = 0;
        foreach (var dir in rays)
        {
            int hits = 0;
            for (int i = 0; i < t.Length; i += 3)
                if (RayHitsTriangle(localPoint, dir, v[t[i]], v[t[i + 1]], v[t[i + 2]])) hits++;
            if ((hits & 1) == 1) votes++;
        }
        return votes >= 2;
    }

    /// <summary>Möller–Trumbore 레이-삼각형 교차 (양면, t &gt; 0 만).</summary>
    static bool RayHitsTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 e1 = b - a, e2 = c - a;
        Vector3 pv = Vector3.Cross(d, e2);
        float det = Vector3.Dot(e1, pv);
        if (Mathf.Abs(det) < 1e-12f) return false;
        float invDet = 1f / det;
        Vector3 tv = o - a;
        float u = Vector3.Dot(tv, pv) * invDet;
        if (u < 0f || u > 1f) return false;
        Vector3 qv = Vector3.Cross(tv, e1);
        float w = Vector3.Dot(d, qv) * invDet;
        if (w < 0f || u + w > 1f) return false;
        return Vector3.Dot(e2, qv) * invDet > 0f;
    }

    /// <summary>
    /// 남은 조각: 원본 메시에서 편집 대상 버텍스 위치만 사영 위치로 바꾼 메시.
    /// 삼각형 구성(토폴로지)은 원본 그대로.
    /// </summary>
    static Mesh BuildRemainingMesh(Mesh src, Vector3[] moved, bool[] edited, Vector3[] dentNormals, bool hasTangents)
    {
        var mesh = Object.Instantiate(src);
        mesh.name = src.name + "_Rest";
        mesh.SetVertices(moved);

        var normals = src.normals;
        if (normals.Length == moved.Length)
        {
            for (int i = 0; i < normals.Length; i++)
                if (edited[i]) normals[i] = dentNormals[i];
            mesh.SetNormals(normals);
        }
        else
        {
            mesh.RecalculateNormals();
        }

        if (hasTangents) mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// 떨어져 나간 덩어리: 편집 대상 버텍스를 하나라도 쓰는 삼각형들로 만든 닫힌 메시.
    /// - "이동 전" 면: 원래 위치/법선/감김 그대로 (원래 표면 쪽)
    /// - "이동 후" 면: 사영된 위치, 감김 반대, 법선은 구 바깥 방향 (구 표면 쪽)
    /// 편집되지 않은 버텍스는 이동 전/후가 같은 정점을 공유하므로 두 면이 테두리에서 맞물려 닫힌다.
    /// </summary>
    static Mesh BuildChunkMesh(Mesh src, Vector3[] moved, bool[] edited, Vector3[] dentNormals, bool hasTangents)
    {
        var positions = src.vertices;
        var normals = src.normals;
        var uvs = src.uv;
        var colors = src.colors;
        bool hasN = normals.Length == positions.Length;
        bool hasUV = uvs.Length == positions.Length;
        bool hasC = colors.Length == positions.Length;

        var outPos = new List<Vector3>();
        var outNrm = new List<Vector3>();
        var outUV = new List<Vector2>();
        var outCol = new List<Color>();

        // 원본 인덱스 → 덩어리 메시 인덱스 (이동 전 / 이동 후)
        var beforeMap = new Dictionary<int, int>();
        var afterMap = new Dictionary<int, int>();

        int AddVertex(Vector3 p, Vector3 nrm, int srcIndex)
        {
            int idx = outPos.Count;
            outPos.Add(p);
            outNrm.Add(nrm);
            if (hasUV) outUV.Add(uvs[srcIndex]);
            if (hasC) outCol.Add(colors[srcIndex]);
            return idx;
        }

        int Before(int i)
        {
            if (beforeMap.TryGetValue(i, out int idx)) return idx;
            idx = AddVertex(positions[i], hasN ? normals[i] : Vector3.zero, i);
            beforeMap.Add(i, idx);
            return idx;
        }

        int After(int i)
        {
            // 편집 안 된 버텍스는 이동 전과 같은 점 → 공유 (테두리가 맞물리는 핵심)
            if (!edited[i]) return Before(i);
            if (afterMap.TryGetValue(i, out int idx)) return idx;
            // 덩어리 기준 바깥 = 구 중점에서 멀어지는 방향 = 파인 면 법선의 반대
            idx = AddVertex(moved[i], -dentNormals[i], i);
            afterMap.Add(i, idx);
            return idx;
        }

        int subCount = src.subMeshCount;
        var subs = new List<int>[subCount];
        bool any = false;
        var tris = new List<int>();

        for (int s = 0; s < subCount; s++)
        {
            subs[s] = new List<int>();
            if (src.GetTopology(s) != MeshTopology.Triangles) continue;
            src.GetTriangles(tris, s);
            for (int t = 0; t < tris.Count; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                if (!edited[a] && !edited[b] && !edited[c]) continue;
                any = true;

                // 이동 전 면 (원래 감김)
                subs[s].Add(Before(a)); subs[s].Add(Before(b)); subs[s].Add(Before(c));
                // 이동 후 면 (감김 반대 → 덩어리 바깥을 향함)
                subs[s].Add(After(a)); subs[s].Add(After(c)); subs[s].Add(After(b));
            }
        }

        if (!any) return null;

        var mesh = new Mesh { name = src.name + "_Chunk" };
        if (outPos.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(outPos);
        if (hasUV) mesh.SetUVs(0, outUV);
        if (hasC) mesh.SetColors(outCol);
        mesh.subMeshCount = subCount;
        for (int s = 0; s < subCount; s++) mesh.SetTriangles(subs[s], s, false);

        if (hasN) mesh.SetNormals(outNrm);
        else mesh.RecalculateNormals();
        if (hasTangents && hasUV) mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }
}
