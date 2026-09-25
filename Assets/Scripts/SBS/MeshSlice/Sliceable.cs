using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 절단 가능한 오브젝트 표시 + 절단 설정.
/// MeshFilter 가 있는 오브젝트에 붙이고 Slice(평면) / CutSphere(구체) / CutByMesh(메시 모양) 를 호출한다.
/// 조각에도 이 컴포넌트가 복사되므로 계속 잘라낼 수 있다.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(Renderer))]
public class Sliceable : MonoBehaviour
{
    [Header("공통")]

    [Tooltip("이 크기(바운즈 최대 변, 월드)보다 작은 조각은 더 이상 자르지 않음. 0 이면 제한 없음")]
    [SerializeField] float m_minSliceSize = 0.05f;

    [SerializeField]
    MeshSlicer.Options option = MeshSlicer.Options.Default;
    /// <summary>현재 설정으로 절단 옵션 생성.</summary>
    public MeshSlicer.Options Options => option;

    /// <summary>너무 작은 조각은 무시 (무한 분할 방지)</summary>
    bool IsTooSmall()
    {
        if (m_minSliceSize <= 0f || !TryGetComponent(out Renderer r))
            return false;
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
        if (IsTooSmall()) 
            return false;
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
        if (IsTooSmall()) 
            return false;
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
        if (IsTooSmall()) 
            return false;
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
        if (IsTooSmall())
            return false;
        return MeshSlicer.CutByMesh(gameObject, cutterMesh, cutterLocalToWorld, Options, out outside, out inside);
    }

    public bool CutByMesh(Mesh cutterMesh, Matrix4x4 cutterLocalToWorld) => CutByMesh(cutterMesh, cutterLocalToWorld, out _, out _);

    #endregion
}
