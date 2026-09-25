using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 메시 불리언 연산(CSG: 차집합/교집합).
/// <para>
/// 방식: AABB 로 상대와 가까운 면만 골라, 실제로 교차하는 상대 면의 평면으로 분할한 뒤
/// 각 조각이 상대 메시 안에 있는지 winding number 로 판정한다.
/// (BSP 트리 방식은 볼록 메시에서 트리가 일자로 깊어지고 먼 면까지 잘게 쪼개져 매우 느려서 대체함.)
/// </para>
/// <para>
/// 입력 메시는 "닫힌(watertight)" 형태여야 결과가 올바르다.
/// 모든 연산은 같은 좌표계(보통 절단 대상의 로컬 공간)에서 수행한다.
/// </para>
/// </summary>
public static class MeshCSG
{
    // 평면 분류 허용 오차
    const float Epsilon = 1e-5f;

    #region 자료구조

    /// <summary>CSG 정점. 위치/법선/UV 만 보간한다 (탄젠트는 결과 메시에서 재계산).</summary>
    public struct Vertex : IEquatable<Vertex>
    {
        public Vector3 pos;
        public Vector3 normal;
        public Vector2 uv;

        public Vertex(Vector3 pos, Vector3 normal, Vector2 uv)
        {
            this.pos = pos; this.normal = normal; this.uv = uv;
        }

        public Vertex Lerp(Vertex other, float t) => new Vertex(
            Vector3.Lerp(pos, other.pos, t),
            Vector3.Lerp(normal, other.normal, t).normalized,
            Vector2.Lerp(uv, other.uv, t));

        // 결과 메시에서 정점 중복 제거용 (정확히 같은 값만 합침)
        public bool Equals(Vertex o) => pos == o.pos && normal == o.normal && uv == o.uv;
        public override bool Equals(object obj) => obj is Vertex v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(pos, normal, uv);
    }

    /// <summary>n·x = w 형태의 평면.</summary>
    public struct CsgPlane
    {
        public Vector3 normal;
        public float w;

        public static bool TryFromPoints(Vector3 a, Vector3 b, Vector3 c, out CsgPlane plane)
        {
            // Unity 앞면 기준 법선 = cross(b-a, c-a)
            Vector3 n = Vector3.Cross(b - a, c - a);
            float len = n.magnitude;
            if (len < 1e-12f) { plane = default; return false; } // 넓이 0 삼각형
            n /= len;
            plane = new CsgPlane { normal = n, w = Vector3.Dot(n, a) };
            return true;
        }

        public CsgPlane Flipped => new CsgPlane { normal = -normal, w = -w };

        const int Coplanar = 0, Front = 1, Back = 2, Spanning = 3;

        /// <summary>
        /// 폴리곤을 이 평면 기준으로 분류/분할해서 4개 리스트 중 하나(또는 둘)에 넣는다.
        /// 같은 평면 위 폴리곤은 방향에 따라 coplanarFront / coplanarBack 으로 간다.
        /// </summary>
        public void SplitPolygon(Polygon poly, List<Polygon> coplanarFront, List<Polygon> coplanarBack,
            List<Polygon> front, List<Polygon> back)
        {
            var verts = poly.vertices;
            int n = verts.Count;
            int polygonType = 0;
            Span<int> types = n <= 64 ? stackalloc int[n] : new int[n];

            for (int i = 0; i < n; i++)
            {
                float t = Vector3.Dot(normal, verts[i].pos) - w;
                int type = t < -Epsilon ? Back : (t > Epsilon ? Front : Coplanar);
                polygonType |= type;
                types[i] = type;
            }

            switch (polygonType)
            {
                case Coplanar:
                    (Vector3.Dot(normal, poly.plane.normal) > 0f ? coplanarFront : coplanarBack).Add(poly);
                    break;
                case Front:
                    front.Add(poly);
                    break;
                case Back:
                    back.Add(poly);
                    break;
                default: // Spanning: 평면이 폴리곤을 가로지름 → 둘로 자름
                    var f = new List<Vertex>(n + 1);
                    var b = new List<Vertex>(n + 1);
                    for (int i = 0; i < n; i++)
                    {
                        int j = (i + 1) % n;
                        int ti = types[i], tj = types[j];
                        Vertex vi = verts[i], vj = verts[j];
                        if (ti != Back) f.Add(vi);
                        if (ti != Front) b.Add(vi);
                        if ((ti | tj) == Spanning)
                        {
                            float t = (w - Vector3.Dot(normal, vi.pos)) / Vector3.Dot(normal, vj.pos - vi.pos);
                            var v = vi.Lerp(vj, t);
                            f.Add(v);
                            b.Add(v);
                        }
                    }
                    // 잘린 조각은 원래 폴리곤과 같은 평면 위에 있으므로 평면을 그대로 물려준다 (얇은 조각에서 법선 오차 방지)
                    if (f.Count >= 3) front.Add(new Polygon(f, poly.plane, poly.submesh));
                    if (b.Count >= 3) back.Add(new Polygon(b, poly.plane, poly.submesh));
                    break;
            }
        }
    }

    /// <summary>볼록 폴리곤. submesh 는 결과 메시에서 들어갈 서브메시(=머터리얼) 인덱스.</summary>
    public class Polygon
    {
        public List<Vertex> vertices;
        public CsgPlane plane;
        public int submesh;

        public Polygon(List<Vertex> vertices, CsgPlane plane, int submesh)
        {
            this.vertices = vertices; this.plane = plane; this.submesh = submesh;
        }

        public Polygon Clone() => new Polygon(new List<Vertex>(vertices), plane, submesh);

        /// <summary>앞뒤 뒤집기: 정점 순서 반전 + 법선 반전.</summary>
        public void Flip()
        {
            vertices.Reverse();
            for (int i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                v.normal = -v.normal;
                vertices[i] = v;
            }
            plane = plane.Flipped;
        }
    }

    #endregion

    #region 불리언 연산

    // 안/밖 판정 시 조각 중심을 면 법선 방향으로 살짝 밀어서 검사하는 거리.
    // 커터 면과 대상 면이 정확히 겹치는(동일 평면) 경우에도 결과가 한쪽으로 정해지게 한다.
    const float CLASSIFY_OFFSET = 1e-4f;

    /// <summary>A − B (A 에서 B 영역을 파낸다). 파낸 면(B 표면)은 뒤집혀서 들어간다.</summary>
    public static List<Polygon> Subtract(List<Polygon> a, List<Polygon> b)
    {
        SubtractAndIntersect(a, b, out var sub, out _);
        return sub;
    }

    /// <summary>A ∩ B (A 중 B 안에 들어있는 부분).</summary>
    public static List<Polygon> Intersect(List<Polygon> a, List<Polygon> b)
    {
        SubtractAndIntersect(a, b, out _, out var inter);
        return inter;
    }

    /// <summary>
    /// A − B 와 A ∩ B 를 한 번의 분할/판정으로 같이 구한다 (절단은 보통 둘 다 필요하므로 따로 구하는 것보다 2배 빠름).
    /// <para>
    /// 1) 상대 메시 AABB 와 안 겹치는 폴리곤은 자르지도, 판정하지도 않고 바로 결정 (대부분의 면이 여기서 끝남)<br/>
    /// 2) 겹치는 폴리곤은 "실제로 교차하는" 상대 폴리곤의 평면들로만 분할<br/>
    /// 3) 분할된 조각마다 상대 메시 안에 있는지 판정해서 결과에 배분
    /// </para>
    /// </summary>
    public static void SubtractAndIntersect(List<Polygon> a, List<Polygon> b, out List<Polygon> subtract, out List<Polygon> intersect)
    {
        var solidA = new Solid(a); // A
        var solidB = new Solid(b); // B
        var sub = new List<Polygon>(a.Count + b.Count); // A − B
        var inter = new List<Polygon>(); // A ∩ B

        // A 조각: B 밖이면 차집합, B 안이면 교집합.
        // 안쪽(-n)으로 밀어서 판정 → 동일 평면으로 겹친 면이 결과에 한 번만 남는다.
        ProcessSide(solidA, solidB, -CLASSIFY_OFFSET, (frag, inside) =>
        {
            if (inside)
                inter.Add(frag.Clone());
            else
                sub.Add(frag.Clone());
        });

        // B 조각: A 안에 있는 부분만 사용. 차집합에선 뒤집어서(파낸 구멍의 안쪽 면) 넣는다.
        ProcessSide(solidB, solidA, +CLASSIFY_OFFSET, (frag, inside) =>
        {
            if (!inside)
                return;
            inter.Add(frag.Clone());
            var flipped = frag.Clone();
            flipped.Flip();
            sub.Add(flipped);
        });

        subtract = sub;
        intersect = inter;
    }

    /// <summary>src 의 각 폴리곤을 other 기준으로 분할하고, 조각마다 (조각, other 내부 여부) 를 전달한다.</summary>
    static void ProcessSide(Solid src, Solid other, float offset, Action<Polygon, bool> emit)
    {
        var frags = new List<Polygon>();
        var front = new List<Polygon>();
        var back = new List<Polygon>();
        var candidates = new List<int>();

        for (int i = 0; i < src.polys.Count; i++)
        {
            var poly = src.polys[i];
            Bounds pb = src.bounds[i];

            // 1) 상대 메시와 멀리 떨어진 면 → 상대 밖에 있음이 확실
            if (!pb.Intersects(other.totalBounds))
            {
                emit(poly, false);
                continue;
            }

            // 2) 실제로 교차하는 상대 폴리곤 평면으로만 분할
            frags.Clear();
            frags.Add(poly);
            other.QueryOverlap(pb.min, pb.max, candidates);
            foreach (int j in candidates)
            {
                var op = other.polys[j];

                // 상대 폴리곤 정점들이 poly 평면 기준 어느 쪽에 있는지
                int posCount = 0, negCount = 0;
                var ov = op.vertices;
                for (int k = 0; k < ov.Count; k++)
                {
                    float d = Vector3.Dot(poly.plane.normal, ov[k].pos) - poly.plane.w;
                    if (d > Epsilon) posCount++;
                    else if (d < -Epsilon) negCount++;
                }

                if (posCount > 0 && negCount > 0)
                {
                    // 상대 폴리곤이 poly 평면을 관통 → 교선은 op 평면 위. (poly 도 op 평면을 가로질러야 실제 교차)
                    if (Straddles(poly, op.plane)) SplitAll(frags, op.plane, front, back);
                }
                else
                {
                    // 관통하진 않지만 상대 폴리곤의 모서리가 poly 평면 위에 정확히 놓인 경우
                    // (예: 구 커터 중심이 큐브 면 위에 있을 때). 그 모서리가 표면 경계선이므로
                    // "모서리를 지나고 poly 에 수직인 평면"으로 분할해야 한다.
                    for (int k = 0; k < ov.Count; k++)
                    {
                        Vector3 a = ov[k].pos, b = ov[(k + 1) % ov.Count].pos;
                        if (Mathf.Abs(Vector3.Dot(poly.plane.normal, a) - poly.plane.w) > Epsilon) continue;
                        if (Mathf.Abs(Vector3.Dot(poly.plane.normal, b) - poly.plane.w) > Epsilon) continue;
                        Vector3 n = Vector3.Cross(b - a, poly.plane.normal);
                        float len = n.magnitude;
                        if (len < 1e-9f) continue;
                        n /= len;
                        var edgePlane = new CsgPlane { normal = n, w = Vector3.Dot(n, a) };
                        if (Straddles(poly, edgePlane)) SplitAll(frags, edgePlane, front, back);
                    }
                }
            }

            // 3) 조각별 안/밖 판정
            foreach (var f in frags)
            {
                Vector3 probe = Centroid(f) + poly.plane.normal * offset;
                emit(f, other.Contains(probe));
            }
        }
    }

    /// <summary>frags 의 모든 조각을 plane 으로 분할 (결과는 frags 에 다시 담김).</summary>
    static void SplitAll(List<Polygon> frags, CsgPlane plane, List<Polygon> front, List<Polygon> back)
    {
        front.Clear(); back.Clear();
        foreach (var f in frags) plane.SplitPolygon(f, front, back, front, back);
        frags.Clear();
        frags.AddRange(front);
        frags.AddRange(back);
    }

    /// <summary>폴리곤 정점이 평면 양쪽에 모두 있는지.</summary>
    static bool Straddles(Polygon p, CsgPlane plane)
    {
        bool f = false, b = false;
        foreach (var v in p.vertices)
        {
            float d = Vector3.Dot(plane.normal, v.pos) - plane.w;
            if (d > Epsilon) f = true;
            else if (d < -Epsilon) b = true;
            if (f && b) return true;
        }
        return false;
    }

    static Vector3 Centroid(Polygon p)
    {
        Vector3 c = Vector3.zero;
        foreach (var v in p.vertices) c += v.pos;
        return c / p.vertices.Count;
    }

    /// <summary>
    /// 닫힌 폴리곤 집합. 폴리곤 AABB 로 BVH(바운딩 볼륨 계층)를 만들어
    /// "겹치는 폴리곤 찾기"와 "점 내부 판정(레이캐스트)"을 O(log n) 수준으로 처리한다.
    /// </summary>
    class Solid
    {
        public readonly List<Polygon> polys;
        public readonly Bounds[] bounds;
        public readonly Bounds totalBounds;

        // 폴리곤별 AABB (Bounds 프로퍼티 호출 비용을 피하려고 min/max 를 따로 보관)
        readonly Vector3[] pMin, pMax;

        // BVH 노드. count > 0 이면 리프: order[start .. start+count) 가 폴리곤 인덱스.
        // BVH(Bounding Volume Hierarchy, 바운딩 볼륨 계층)는 폴리곤들을 상자로 묶고, 그 상자들을 다시 큰 상자로 묶는 트리
        struct BvhNode
        {
            public Vector3 min, max;
            public int left, right, start, count;
        }
        // 노드 리스트
        readonly List<BvhNode> nodes = new List<BvhNode>();
        readonly int[] order;
        const int LeafSize = 4;

        // 탐색용 스택 재사용 (할당 줄이기)
        readonly Stack<int> stack = new Stack<int>();

        public Solid(List<Polygon> polys)
        {
            this.polys = polys;
            int n = polys.Count;
            // 폴리곤 갯수만큼 세팅
            bounds = new Bounds[n];
            pMin = new Vector3[n];
            pMax = new Vector3[n];
            var centers = new Vector3[n];
            // 전체 감싸는 바운딩
            var total = new Bounds();

            // 값 세팅
            for (int i = 0; i < n; i++)
            {
                var vs = polys[i].vertices;
                var b = new Bounds(vs[0].pos, Vector3.zero);

                for (int k = 1; k < vs.Count; k++)
                    b.Encapsulate(vs[k].pos);
                b.Expand(Epsilon * 4f); // 경계에 딱 붙은 경우 놓치지 않도록

                bounds[i] = b;
                pMin[i] = b.min;
                pMax[i] = b.max;
                centers[i] = b.center;

                if (i == 0)
                    total = b;
                else
                    total.Encapsulate(b);
            }
            totalBounds = total;

            order = new int[n];
            for (int i = 0; i < n; i++)
                order[i] = i;
            if (n > 0)
                Build(0, n, centers);
        }

        /// <summary>order[start..end) 로 BVH 노드를 만들고 인덱스를 반환 (가장 긴 축 기준 중앙값 분할).</summary>
        int Build(int start, int end, Vector3[] centers)
        {
            // 
            Vector3 min = pMin[order[start]], max = pMax[order[start]];
            for (int i = start + 1; i < end; i++)
            {
                min = Vector3.Min(min, pMin[order[i]]);
                max = Vector3.Max(max, pMax[order[i]]);
            }

            int index = nodes.Count;
            int count = end - start;
            if (count <= LeafSize)
            {
                nodes.Add(new BvhNode { min = min, max = max, start = start, count = count });
                return index;
            }
            nodes.Add(default); // 자식 인덱스를 알고 나서 채움

            Vector3 size = max - min;
            int axis = size.x >= size.y && size.x >= size.z ? 0 : (size.y >= size.z ? 1 : 2);
            Array.Sort(order, start, count, Comparer<int>.Create((x, y) => centers[x][axis].CompareTo(centers[y][axis])));
            int mid = start + count / 2;

            int left = Build(start, mid, centers);
            int right = Build(mid, end, centers);
            nodes[index] = new BvhNode { min = min, max = max, left = left, right = right };
            return index;
        }

        /// <summary>AABB(min,max) 와 겹치는 폴리곤 인덱스를 result 에 담는다.</summary>
        public void QueryOverlap(Vector3 min, Vector3 max, List<int> result)
        {
            result.Clear();
            if (nodes.Count == 0) return;
            stack.Clear();
            stack.Push(0);
            while (stack.Count > 0)
            {
                var node = nodes[stack.Pop()];
                if (!Overlaps(node.min, node.max, min, max)) continue;
                if (node.count == 0)
                {
                    stack.Push(node.left);
                    stack.Push(node.right);
                    continue;
                }
                for (int i = node.start; i < node.start + node.count; i++)
                {
                    int p = order[i];
                    if (Overlaps(pMin[p], pMax[p], min, max)) result.Add(p);
                }
            }
        }

        static bool Overlaps(Vector3 aMin, Vector3 aMax, Vector3 bMin, Vector3 bMax) =>
            aMin.x <= bMax.x && aMax.x >= bMin.x &&
            aMin.y <= bMax.y && aMax.y >= bMin.y &&
            aMin.z <= bMax.z && aMax.z >= bMin.z;

        // 판정용 레이 방향 3개 (축과 어긋난 임의 방향 → 모서리/꼭짓점을 정확히 지날 확률 최소화)
        static readonly Vector3[] s_rayDirs =
        {
            new Vector3(0.8123f, 0.4567f, 0.3627f).normalized,
            new Vector3(-0.3217f, 0.8531f, -0.4103f).normalized,
            new Vector3(0.2911f, -0.3977f, 0.8703f).normalized,
        };

        /// <summary>
        /// 점이 닫힌 메시 내부인지 레이캐스트 홀짝 판정.
        /// 레이 3개를 쏴서 다수결 → 레이가 모서리를 정확히 지나 한 번 잘못 세는 경우에도 안전.
        /// </summary>
        public bool Contains(Vector3 p)
        {
            if (!totalBounds.Contains(p)) return false;

            int votes = 0;
            for (int r = 0; r < s_rayDirs.Length; r++)
            {
                if ((CountHits(p, s_rayDirs[r]) & 1) == 1) votes++;
                // 이미 결과가 정해졌으면 조기 종료
                if (votes >= 2) return true;
                if (votes + (s_rayDirs.Length - 1 - r) < 2) return false;
            }
            return votes >= 2;
        }

        /// <summary>BVH 를 따라가며 레이가 맞는 삼각형 수를 센다.</summary>
        int CountHits(Vector3 origin, Vector3 dir)
        {
            var inv = new Vector3(1f / dir.x, 1f / dir.y, 1f / dir.z);
            int hits = 0;
            stack.Clear();
            stack.Push(0);
            while (stack.Count > 0)
            {
                var node = nodes[stack.Pop()];
                if (!RayHitsBox(origin, inv, node.min, node.max)) continue;
                if (node.count == 0)
                {
                    stack.Push(node.left);
                    stack.Push(node.right);
                    continue;
                }
                for (int i = node.start; i < node.start + node.count; i++)
                {
                    int p = order[i];
                    if (!RayHitsBox(origin, inv, pMin[p], pMax[p])) continue;
                    var vs = polys[p].vertices;
                    for (int k = 1; k + 1 < vs.Count; k++)
                        if (RayHitsTriangle(origin, dir, vs[0].pos, vs[k].pos, vs[k + 1].pos)) hits++;
                }
            }
            return hits;
        }

        /// <summary>레이(반직선) vs AABB 슬랩 테스트.</summary>
        static bool RayHitsBox(Vector3 o, Vector3 inv, Vector3 min, Vector3 max)
        {
            float t1 = (min.x - o.x) * inv.x, t2 = (max.x - o.x) * inv.x;
            float tmin = Math.Min(t1, t2), tmax = Math.Max(t1, t2);
            t1 = (min.y - o.y) * inv.y; t2 = (max.y - o.y) * inv.y;
            tmin = Math.Max(tmin, Math.Min(t1, t2)); tmax = Math.Min(tmax, Math.Max(t1, t2));
            t1 = (min.z - o.z) * inv.z; t2 = (max.z - o.z) * inv.z;
            tmin = Math.Max(tmin, Math.Min(t1, t2)); tmax = Math.Min(tmax, Math.Max(t1, t2));
            return tmax >= Math.Max(tmin, 0f);
        }

        /// <summary>Möller–Trumbore 레이-삼각형 교차 (양면, t &gt; 0 만).</summary>
        static bool RayHitsTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 e1 = b - a, e2 = c - a;
            Vector3 pv = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, pv);
            if (Math.Abs(det) < 1e-12f) return false; // 레이와 평행
            float invDet = 1f / det;
            Vector3 tv = o - a;
            float u = Vector3.Dot(tv, pv) * invDet;
            if (u < 0f || u > 1f) return false;
            Vector3 qv = Vector3.Cross(tv, e1);
            float v = Vector3.Dot(d, qv) * invDet;
            if (v < 0f || u + v > 1f) return false;
            return Vector3.Dot(e2, qv) * invDet > 0f;
        }
    }

    #endregion

    #region Mesh ↔ Polygon 변환

    /// <summary>
    /// 대상 메시를 폴리곤 목록으로 변환 (서브메시 인덱스 유지).
    /// </summary>
    public static List<Polygon> FromMesh(Mesh mesh)
    {
        // Unity Mesh를 CSG가 다루기 쉬운 "폴리곤 목록"으로 바꾸는 함수

        // Unity Mesh는 정점 배열 + 인덱스 배열 구조라서, 삼각형들이 정점을 인덱스로 공유
        // CSG는 삼각형을 평면으로 계속 쪼개면서 새 정점을 만듬.
        // 공유 인덱스 구조에서 이걸 하면 인덱스 관리가 매우 복잡해짐.

        // 그래서 삼각형마다 자기 정점을 값으로 직접 들고 있는 독립 폴리곤으로 풀어내고. (폴리곤 수프).
        // 이러면 폴리곤 하나를 자르더라도 다른 폴리곤에 영향이 X.
        // 풀면서 늘어난 중복 정점은 나중에 ToMesh에서 다시 합치면 됨

        // 메시 정보
        Vector3[] verts = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Vector2[] uvs = mesh.uv;

        // 노말 UV 검증
        bool hasN = normals.Length == verts.Length;
        bool hasUV = uvs.Length == verts.Length;

        var result = new List<Polygon>();
        // 폴리곤 리스트
        var tris = new List<int>();
        // 메시 서브메시 순회
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            // 메시 폴리곤 가져옴
            mesh.GetTriangles(tris, s);
            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i];
                int b = tris[i + 1];
                int c = tris[i + 2];

                // 세 점으로 삼각형이 놓인 평면(법선 n, 거리 w)을 미리 계산. CSG에서 분할과 안/밖 판정에 계속 쓰기 때문.
                // 넓이가 0인 삼각형(세 점이 한 점이거나 일직선)은 법선을 정의할 수 없어서 여기서 버림.
                // 모델링 툴에서 가끔 섞여 나오는 이런 삼각형이 CSG 계산을 망가뜨리는 것을 막음.
                if (!CsgPlane.TryFromPoints(verts[a], verts[b], verts[c], out CsgPlane plane))
                    continue;

                var list = new List<Vertex>(3);
                // 세 정점을 값으로 복사. 법선이 없다 -> 면 법선, UV가 없으면 (0,0)을 대신 넣음. 순서는 a b c 순으로 유지해서, 원래 앞면 방향이 보존됨.
                foreach (int k in new[] { a, b, c })
                {
                    list.Add
                        (
                            new Vertex
                            (
                                verts[k],
                                hasN ?
                                    normals[k] :
                                    plane.normal,
                                hasUV ?
                                    uvs[k] :
                                    Vector2.zero
                            )
                        );
                }

                // 버텍스, 미리 계산한 평면, 서브메시 인덱스 추가
                result.Add(new Polygon(list, plane, s));
            }
        }
        return result;
    }

    /// <summary>
    /// 절단 도구(커터) 메시를 폴리곤 목록으로 변환.
    /// 대상 로컬 공간으로 옮기고, 모든 면을 capSubmesh(=원본 머터리얼)에 배정한다.
    /// UV 는 대상 로컬 좌표 기준 박스 매핑(면 법선의 주축 방향 평면 투영).
    /// </summary>
    /// <param name="cutterToTarget">커터 로컬 → 대상 로컬 변환 행렬.</param>
    public static List<Polygon> FromCutterMesh(Mesh mesh, Matrix4x4 cutterToTarget, int capSubmesh, float uvScale)
    {
        // 커터 메시 정보 (커터 자기 로컬 좌표 기준)
        Vector3[] verts = mesh.vertices;
        Vector3[] normals = mesh.normals;

        // 노말 검증 (UV 는 커터 것을 쓰지 않고 아래에서 새로 만들기 때문에 검사 안 함)
        bool hasN = normals.Length == verts.Length;

        // 법선 변환 행렬: 위치 변환 행렬의 역전치.
        // 위치 행렬을 그대로 쓰면 비균등 스케일에서 법선이 면에 수직이 아니게 된다.
        Matrix4x4 normalMatrix = cutterToTarget.inverse.transpose;

        // 정점/법선을 대상 로컬 공간으로 미리 변환 (정점은 여러 삼각형이 공유하므로 한 번씩만 계산)
        Vector3[] targetVerts = new Vector3[verts.Length];
        Vector3[] targetNormals = new Vector3[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            targetVerts[i] = cutterToTarget.MultiplyPoint3x4(verts[i]);

            if (hasN)
                targetNormals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
        }

        // 행렬에 뒤집기(음수 스케일)가 있으면 앞면/뒷면이 바뀐다 → 감김 순서를 뒤집어 바깥 방향 유지
        bool mirrored = cutterToTarget.determinant < 0f;

        var result = new List<Polygon>();
        // 삼각형 인덱스 버퍼 (서브메시마다 재사용)
        var tris = new List<int>();
        // 커터 서브메시 순회 (커터 머터리얼은 무시하고 전부 capSubmesh 로 모은다)
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            // 삼각형이 아닌 서브메시(선/점)는 건너뜀
            if (mesh.GetTopology(s) != MeshTopology.Triangles)
                continue;

            // 서브메시 삼각형 인덱스 가져옴
            mesh.GetTriangles(tris, s);
            for (int i = 0; i < tris.Count; i += 3)
            {
                // 삼각형 정점 인덱스 (뒤집힌 행렬이면 b, c 를 바꿔 감김 반전)
                int a = tris[i];
                int b = mirrored ? tris[i + 2] : tris[i + 1];
                int c = mirrored ? tris[i + 1] : tris[i + 2];

                // 평면 계산. 넓이 0 삼각형은 제외
                if (!CsgPlane.TryFromPoints(targetVerts[a], targetVerts[b], targetVerts[c], out CsgPlane plane))
                    continue;

                var list = new List<Vertex>(3);
                // 정점 3개를 값으로 복사해 폴리곤 구성
                // - 법선: 변환된 정점 법선 (없으면 면 법선)
                // - UV: 박스 매핑으로 새로 생성 (파낸 면에 원본 머터리얼 텍스처가 자연스럽게 깔리도록)
                foreach (int k in new[] { a, b, c })
                {
                    list.Add
                        (
                            new Vertex
                            (
                                targetVerts[k],
                                hasN ?
                                    targetNormals[k] :
                                    plane.normal,
                                BoxUV(targetVerts[k], plane.normal) * uvScale
                            )
                        );
                }

                // 서브메시는 커터 것이 아니라 capSubmesh → 파낸 면에 원본 머터리얼 적용
                result.Add(new Polygon(list, plane, capSubmesh));
            }
        }
        return result;
    }

    /// <summary>법선의 가장 큰 성분 축을 버리고 나머지 두 축으로 UV 투영.</summary>
    static Vector2 BoxUV(Vector3 p, Vector3 n)
    {
        float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
        if (ax >= ay && ax >= az) return new Vector2(p.z, p.y);
        if (ay >= az) return new Vector2(p.x, p.z);
        return new Vector2(p.x, p.y);
    }

    /// <summary>
    /// 폴리곤 목록을 Mesh 로 변환. 볼록 폴리곤은 부채꼴로 삼각분할하고 동일 정점은 합친다.
    /// </summary>
    /// <param name="subMeshCount">결과 서브메시 수 (원본 머터리얼 수와 맞춤).</param>
    /// <param name="recalcTangents">원본에 탄젠트가 있었다면 true (노멀맵 대응).</param>
    public static Mesh ToMesh(List<Polygon> polys, int subMeshCount, string name, bool recalcTangents)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var subs = new List<int>[subMeshCount];
        for (int i = 0; i < subMeshCount; i++) subs[i] = new List<int>();

        var indexOf = new Dictionary<Vertex, int>();
        int Index(Vertex v)
        {
            if (indexOf.TryGetValue(v, out int idx)) return idx;
            idx = positions.Count;
            positions.Add(v.pos); normals.Add(v.normal); uvs.Add(v.uv);
            indexOf.Add(v, idx);
            return idx;
        }

        foreach (var p in polys)
        {
            var list = subs[Mathf.Clamp(p.submesh, 0, subMeshCount - 1)];
            int i0 = Index(p.vertices[0]);
            for (int i = 1; i + 1 < p.vertices.Count; i++)
            {
                list.Add(i0);
                list.Add(Index(p.vertices[i]));
                list.Add(Index(p.vertices[i + 1]));
            }
        }

        var mesh = new Mesh { name = name };
        if (positions.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(positions);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = subMeshCount;
        for (int i = 0; i < subMeshCount; i++) mesh.SetTriangles(subs[i], i, false);
        mesh.RecalculateBounds();
        if (recalcTangents) mesh.RecalculateTangents();
        return mesh;
    }

    #endregion

    #region 커터 메시 생성

    static readonly Dictionary<int, Mesh> s_icoSphereCache = new Dictionary<int, Mesh>();

    // 아이코스피어 : 정이십면체(Icosahedron)의 삼각형 면들을 세분하고 정규화하여 만든 정삼각형 기반의 구형 다면체
    /// <summary>
    /// 반지름 1 짜리 아이코스피어(정이십면체 분할 구) 메시. 극점 찌그러짐이 없어 CSG 커터로 적합.
    /// subdivisions: 0=20면, 1=80면, 2=320면, 3=1280면. 결과는 캐시된다.
    /// </summary>
    public static Mesh GetIcoSphere(int subdivisions)
    {
        // subdivisions 범위 강제
        subdivisions = Mathf.Clamp(subdivisions, 0, 4);

        // 만약 캐싱된게 있으면 캐시
        if (s_icoSphereCache.TryGetValue(subdivisions, out Mesh cached) && cached != null)
            return cached;

        // 정20면체를 위한 황금비 세팅
        float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
        // 정 20면체를 위한 버텍스 세팅
        var verts = new List<Vector3>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
            new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };

        // 각 정점 위치를 정규화
        for (int i = 0; i < verts.Count; i++)
            verts[i] = verts[i].normalized;

        // 정 20면체 각 면 정의
        var faces = new List<int>
        {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
            1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
            4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
        };

        // 각 삼각형을 4개로 쪼개고 새 정점을 구면으로 밀어냄
        for (int level = 0; level < subdivisions; level++)
        {
            // 임시 캐싱
            var midCache = new Dictionary<long, int>();
            // 버텍스 a와 b를 잇는 모서리의 중점을 만들어서 그 인덱스를 돌려주는 함수
            int Mid(int a, int b)
            {
                long key = a < b ?
                    ((long)a << 32) | (uint)b :
                    ((long)b << 32) | (uint)a;

                // 캐싱된거 불러오기
                if (midCache.TryGetValue(key, out int idx))
                    return idx;

                idx = verts.Count;
                // 두 점의 중점을 정규화 한다
                // ((verts[a] + verts[b]) * 0.5f) = 두 점의 중점
                // 이 중점은 구 안쪽에 위치하므로. 구 표면으로 올리기 위해 정규화 적용
                verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
                // 캐시에 저장
                midCache.Add(key, idx);
                return idx;
            }

            // 면 분할
            var next = new List<int>(faces.Count * 4);
            for (int i = 0; i < faces.Count; i += 3)
            {
                // 삼각형 각 꼭지점 abc 세팅
                int a = faces[i];
                int b = faces[i + 1];
                int c = faces[i + 2];
                // 각 꼭지점 중간지점 ab bc ca 세팅
                int ab = Mid(a, b);
                int bc = Mid(b, c);
                int ca = Mid(c, a);
                // 각 꼭지점과 중간지점끼리 이어서. 삼각형 1개를 4개로 쪼개기
                next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            faces = next;
        }

        // 모든 면이 바깥(= Unity 앞면 법선 cross(b-a,c-a) 가 중심 반대쪽)을 향하도록 감김 정리
        for (int i = 0; i < faces.Count; i += 3)
        {
            Vector3 a = verts[faces[i]], b = verts[faces[i + 1]], c = verts[faces[i + 2]];
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0f)
                (faces[i + 1], faces[i + 2]) = (faces[i + 2], faces[i + 1]);
        }

        var mesh = new Mesh
        {
            name = $"IcoSphere_{subdivisions}",
            hideFlags = HideFlags.DontSave
        };
        mesh.SetVertices(verts);
        mesh.SetNormals(verts); // 단위구이므로 위치 = 법선 (부드러운 절단면)
        mesh.SetTriangles(faces, 0);
        mesh.RecalculateBounds();
        s_icoSphereCache[subdivisions] = mesh;
        return mesh;
    }

    #endregion
}


