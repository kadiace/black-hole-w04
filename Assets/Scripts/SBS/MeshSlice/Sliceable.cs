using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 절단 가능한 오브젝트 표시 + 절단 설정.
/// MeshFilter 가 있는 오브젝트에 붙이고 Slice(평면) / CutSphere(구체) / CutByMesh(메시 모양) 를 호출한다.
/// 조각에도 이 컴포넌트가 복사되므로 계속 잘라낼 수 있다.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
public class Sliceable : MonoBehaviour
{
    [Header("공통")]
    [Tooltip("절단면에 사용할 머터리얼의 서브메시 인덱스 (MeshRenderer.materials 순서). 기존 머터리얼을 그대로 사용한다.")]
    [SerializeField] int m_capSubmeshIndex = 0;

    [Tooltip("절단면 UV 스케일 (로컬 1 유닛당 UV)")]
    [SerializeField] float m_capUVScale = 1f;

    [Tooltip("조각 콜라이더 생성 방식")]
    [SerializeField] MeshSlicer.ColliderMode m_colliderMode = MeshSlicer.ColliderMode.ConvexMesh;

    [Tooltip("절단 후 조각을 밀어내는 임펄스 (평면: 법선 방향, 모양: 커터 중심에서 바깥)")]
    [SerializeField] float m_separationImpulse = 0.5f;

    [Tooltip("절단 후 원본 파괴 여부")]
    [SerializeField] bool m_destroyOriginal = true;

    [Tooltip("이 크기(바운즈 최대 변, 월드)보다 작은 조각은 더 이상 자르지 않음. 0 이면 제한 없음")]
    [SerializeField] float m_minSliceSize = 0.05f;

    [Header("모양(구체/메시) 절단")]
    [Tooltip("커터 안쪽 덩어리를 조각으로 남길지. 끄면 파먹힌 것처럼 사라진다")]
    [SerializeField] bool m_keepInside = true;

    [Tooltip("결과가 떨어진 덩어리로 나뉘면 각각 별도 GameObject 로 분리")]
    [SerializeField] bool m_separateIslands = true;

    [Tooltip("구체 커터 정밀도 (0=20면 ~ 4=5120면). 높을수록 매끈하지만 느림")]
    [Range(0, 4)]
    [SerializeField] int m_sphereSubdivisions = 2;

    [Tooltip("버텍스 사영 절단이 불가능한 형태(구가 얇은 판을 관통, 표면이 구 중점을 감싸듯 휜 경우 등)일 때 처리 방법.\n" +
             "SplitAsIs: 구에 걸린 삼각형을 원래 모양 그대로 떼어냄 / Csg: CSG 구체 절단 / None: 자르지 않음")]
    [SerializeField] MeshSlicer.ProjectionFallback m_projectionFallback = MeshSlicer.ProjectionFallback.SplitAsIs;

    /// <summary>현재 설정으로 절단 옵션 생성.</summary>
    public MeshSlicer.Options Options => new MeshSlicer.Options
    {
        capSubmeshIndex = m_capSubmeshIndex,
        capUVScale = m_capUVScale,
        colliderMode = m_colliderMode,
        destroyOriginal = m_destroyOriginal,
        separationImpulse = m_separationImpulse,
        keepInside = m_keepInside,
        separateIslands = m_separateIslands,
        sphereSubdivisions = m_sphereSubdivisions,
        projectionFallback = m_projectionFallback,
    };

    /// <summary>너무 작은 조각은 무시 (무한 분할 방지)</summary>
    bool IsTooSmall()
    {
        if (m_minSliceSize <= 0f || !TryGetComponent(out Renderer r)) return false;
        Vector3 s = r.bounds.size;
        return Mathf.Max(s.x, s.y, s.z) < m_minSliceSize;
    }

    #region 평면 절단

    /// <summary>
    /// 월드 평면으로 이 오브젝트를 절단한다.
    /// </summary>
    /// <param name="worldPoint">평면 위의 점 (월드)</param>
    /// <param name="worldNormal">평면 법선 (월드)</param>
    /// <returns>절단 성공 여부</returns>
    public bool Slice(Vector3 worldPoint, Vector3 worldNormal, out GameObject positive, out GameObject negative)
    {
        positive = negative = null;
        if (IsTooSmall()) return false;
        return MeshSlicer.Slice(gameObject, worldPoint, worldNormal, Options, out positive, out negative);
    }

    /// <summary>편의 오버로드.</summary>
    public bool Slice(Vector3 worldPoint, Vector3 worldNormal) => Slice(worldPoint, worldNormal, out _, out _);

    #endregion

    #region 모양 절단

    /// <summary>
    /// 월드 구체로 이 오브젝트를 파낸다/도려낸다.
    /// </summary>
    /// <param name="outside">구 바깥에 남은 조각들</param>
    /// <param name="inside">구 안쪽 조각들 (keepInside 꺼져 있으면 비어 있음)</param>
    public bool CutSphere(Vector3 worldCenter, float worldRadius, out List<GameObject> outside, out List<GameObject> inside)
    {
        outside = inside = null;
        if (IsTooSmall()) return false;
        return MeshSlicer.CutBySphere(gameObject, worldCenter, worldRadius, Options, out outside, out inside);
    }

    public bool CutSphere(Vector3 worldCenter, float worldRadius) => CutSphere(worldCenter, worldRadius, out _, out _);

    /// <summary>
    /// 구 전용 버텍스 사영 방식 절단 (중점 + 반지름).
    /// 반지름 안 버텍스를 구 표면으로 밀어 파인 "남은 조각"과, 이동 전/후 위치로 만든 "덩어리"로 나눈다.
    /// CSG 방식보다 가볍지만 결과 품질은 메시 버텍스 밀도에 좌우된다.
    /// </summary>
    /// <param name="outside">파인 남은 조각 (통째로 먹혔으면 비어 있음)</param>
    /// <param name="inside">떨어져 나간 덩어리들 (keepInside 꺼져 있으면 비어 있음)</param>
    public bool CutSphereByProjection(Vector3 worldCenter, float worldRadius, out List<GameObject> outside, out List<GameObject> inside)
    {
        outside = inside = null;
        if (IsTooSmall()) return false;
        return MeshSlicer.CutBySphereProjection(gameObject, worldCenter, worldRadius, Options, out outside, out inside);
    }

    public bool CutSphereByProjection(Vector3 worldCenter, float worldRadius) => CutSphereByProjection(worldCenter, worldRadius, out _, out _);

    /// <summary>
    /// 임의의 닫힌 메시 모양으로 절단한다.
    /// </summary>
    /// <param name="cutterMesh">커터 메시 (Read/Write Enabled)</param>
    /// <param name="cutterLocalToWorld">커터 배치 (예: cutter.transform.localToWorldMatrix)</param>
    public bool CutByMesh(Mesh cutterMesh, Matrix4x4 cutterLocalToWorld, out List<GameObject> outside, out List<GameObject> inside)
    {
        outside = inside = null;
        if (IsTooSmall()) return false;
        return MeshSlicer.CutByMesh(gameObject, cutterMesh, cutterLocalToWorld, Options, out outside, out inside);
    }

    public bool CutByMesh(Mesh cutterMesh, Matrix4x4 cutterLocalToWorld) => CutByMesh(cutterMesh, cutterLocalToWorld, out _, out _);

    #endregion

    #region 인스펙터 테스트 메뉴 (에디터/플레이 모두 가능, 에디터에선 Undo 지원)

    [ContextMenu("Test Slice/Horizontal (중심, 수평)")]
    void TestSliceHorizontal() => Slice(GetBounds().center, transform.up);

    [ContextMenu("Test Slice/Vertical (중심, 수직)")]
    void TestSliceVertical() => Slice(GetBounds().center, transform.right);

    [ContextMenu("Test Slice/Diagonal (중심, 대각)")]
    void TestSliceDiagonal() => Slice(GetBounds().center, (transform.up + transform.right + transform.forward).normalized);

    [ContextMenu("Test Cut/Sphere (위쪽 모서리 파내기)")]
    void TestSphereCorner()
    {
        var b = GetBounds();
        CutSphere(b.max, Mathf.Min(b.size.x, b.size.y, b.size.z) * 0.5f);
    }

    [ContextMenu("Test Cut/Sphere (윗면 가운데 파내기)")]
    void TestSphereTop()
    {
        var b = GetBounds();
        CutSphere(new Vector3(b.center.x, b.max.y, b.center.z), Mathf.Min(b.size.x, b.size.y, b.size.z) * 0.4f);
    }

    [ContextMenu("Test Cut/Sphere Projection (윗면 가운데, 버텍스 사영)")]
    void TestSphereProjectionTop()
    {
        var b = GetBounds();
        CutSphereByProjection(new Vector3(b.center.x, b.max.y, b.center.z), Mathf.Min(b.size.x, b.size.y, b.size.z) * 0.4f);
    }

    Bounds GetBounds() => TryGetComponent(out Renderer r) ? r.bounds : new Bounds(transform.position, Vector3.one);

    #endregion
}
