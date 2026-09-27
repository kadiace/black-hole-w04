using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 벽 절단기. 스캔 범위 안의 <see cref="CuttableWall"/> 을 찾아 지정한 모양으로 절단한다.
/// 구체 모드면 이 오브젝트 위치와 반지름으로 (CSG 또는 버텍스 사영 방식),
/// 메시 모드면 지정한 MeshFilter 의 모양/트랜스폼 그대로 절단한다.
/// </summary>
public class WallCutter : MonoBehaviour
{
    public enum Shape
    {
        /// <summary>구체 CSG 절단 (정확, 삼각형을 새로 자름)</summary>
        Sphere,
        /// <summary>구체 버텍스 사영 절단 (가벼움, 버텍스 밀도에 따라 품질 결정)</summary>
        SphereProjection,
        /// <summary>임의 메시 모양 CSG 절단</summary>
        Mesh,
    }

    [SerializeField] Shape m_shape = Shape.Sphere;

    [Tooltip("구체 반지름 (월드). transform 스케일과 무관")]
    [SerializeField] float m_radius = 0.5f;

    [Tooltip("절단 대상을 찾을 범위 반지름 (월드). 0 이하면 구체 반지름을 사용")]
    [SerializeField] float m_scanRadius = 1f;

    [Tooltip("메시 모드에서 모양으로 쓸 MeshFilter (닫힌 메시, Read/Write Enabled). 비우면 자기 자신의 MeshFilter")]
    [SerializeField] MeshFilter m_cutterMesh;

    [Tooltip("스캔할 레이어")]
    [SerializeField] LayerMask m_scanMask = ~0;

    // OverlapSphere 결과 버퍼 (매번 할당하지 않도록 재사용)
    readonly Collider[] m_hitBuffer = new Collider[64];

    float ScanRadius => m_scanRadius > 0f ? m_scanRadius : m_radius;

    /// <summary>스캔 범위 안의 모든 CuttableWall 절단.</summary>
    /// <returns>실제로 잘린 벽 개수</returns>
    [ContextMenu("Cut Now")]
    public int CutNow()
    {
        Mesh mesh = null;
        Matrix4x4 matrix = Matrix4x4.identity;
        if (m_shape == Shape.Mesh && !TryGetCutterMesh(out mesh, out matrix))
        {
            Debug.LogWarning("[WallCutter] 커터 메시가 없습니다.", this);
            return 0;
        }

        // 순회 중 오브젝트가 생성/파괴되므로 대상 목록을 먼저 확정.
        // 한 벽에 콜라이더가 여러 개거나 자식에 콜라이더가 있어도 벽 하나당 한 번만 자르도록 HashSet 사용.
        var targets = new HashSet<CuttableWall>();
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, ScanRadius, m_hitBuffer, m_scanMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            var wall = m_hitBuffer[i].GetComponentInParent<CuttableWall>();
            if (wall != null)
                targets.Add(wall);
        }

        int count = 0;
        foreach (var wall in targets)
        {
            if (wall == null)
                continue;

            bool cut = m_shape switch
            {
                Shape.Sphere => wall.CutSphere(transform.position, m_radius),
                Shape.SphereProjection => wall.CutSphereByProjection(transform.position, m_radius),
                _ => wall.CutByMesh(mesh, matrix),
            };

            if (cut)
                count++;
        }
        return count;
    }

    /// <summary>메시 모드 커터의 메시와 배치 행렬.</summary>
    bool TryGetCutterMesh(out Mesh mesh, out Matrix4x4 matrix)
    {
        var meshFilter = m_cutterMesh != null ? m_cutterMesh : GetComponent<MeshFilter>();
        mesh = meshFilter != null ? meshFilter.sharedMesh : null;
        matrix = meshFilter != null ? meshFilter.transform.localToWorldMatrix : Matrix4x4.identity;
        return mesh != null;
    }

    // 씬 뷰에 커터 모양(보라)과 스캔 범위(회색) 표시
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, ScanRadius);

        Gizmos.color = new Color(0.6f, 0.2f, 1f, 0.8f);
        if (m_shape != Shape.Mesh)
        {
            Gizmos.DrawWireSphere(transform.position, m_radius);
            return;
        }

        if (TryGetCutterMesh(out var mesh, out var matrix))
        {
            Gizmos.matrix = matrix;
            Gizmos.DrawWireMesh(mesh);
        }
    }
}
