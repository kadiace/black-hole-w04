using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
/// <summary>
/// 테스트/게임용 모양 커터. 구체 모드면 이 오브젝트 위치와 반지름으로 (CSG 또는 버텍스 사영 방식),
/// 메시 모드면 지정한 MeshFilter 의 모양/트랜스폼 그대로 겹친 <see cref="Sliceable"/> 을 절단한다.
/// 플레이 중 지정 키 또는 인스펙터 우클릭 "Cut Now" 로 실행.
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
    [Tooltip("구체 반지름 (월드). transform 스케일과 무관")]
    [SerializeField] float m_scanRadius = 1f;

    [Tooltip("메시 모드에서 모양으로 쓸 MeshFilter (닫힌 메시, Read/Write Enabled). 비우면 자기 자신의 MeshFilter")]
    [SerializeField] MeshFilter m_cutterMesh;

    /// <summary>커터와 겹친 모든 Sliceable 절단.</summary>
    [ContextMenu("Cut Now")]
    public void CutNow()
    {
        // 커터 범위 가져옴
        Bounds cutterBounds = GetCutterBounds(out Mesh mesh, out Matrix4x4 matrix);
        if (m_shape == Shape.Mesh && mesh == null)
        {
            Debug.LogWarning("[ShapeCutter] 커터 메시가 없습니다.", this);
            return;
        }

        // 순회 중 오브젝트가 생성/파괴되므로 대상 목록을 먼저 확정
        var targets = new List<Sliceable>();
        foreach (var target in Physics.OverlapSphere(transform.position, m_scanRadius))
        {
            if (target != null && target.TryGetComponent<Sliceable>(out var sliceable))
            {
                targets.Add(sliceable);
            }
        }
        // 절단 시작
        int count = 0;
        foreach (var sliceTarget in targets)
        {
            bool finish = false;
            switch (m_shape)
            {
                case Shape.Sphere:
                    {
                        finish = sliceTarget.CutSphere(transform.position, m_radius);
                    }
                    break;
                case Shape.SphereProjection:
                    {
                        finish = sliceTarget.CutSphereByProjection(transform.position, m_radius);
                    }
                    break;
                default:
                    {
                        finish = sliceTarget.CutByMesh(mesh, matrix, out var outobjs, out var inobjs);
                    }
                    break;
            }

            if (finish)
            {
                count++;
            }
        }
    }

    /// <summary>
    /// 커터 바운딩 구하기
    /// </summary>
    /// <param name="mesh"></param>
    /// <param name="matrix"></param>
    /// <returns></returns>
    Bounds GetCutterBounds(out Mesh mesh, out Matrix4x4 matrix)
    {
        mesh = null;
        matrix = Matrix4x4.identity;

        if (m_shape != Shape.Mesh)
            return new Bounds(transform.position, Vector3.one * (m_radius * 2f));

        var meshFilter = m_cutterMesh != null ? m_cutterMesh : GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
            return default;

        mesh = meshFilter.sharedMesh;
        matrix = meshFilter.transform.localToWorldMatrix;
        return meshFilter.TryGetComponent(out Renderer renderer) ? renderer.bounds : new Bounds(meshFilter.transform.position, Vector3.zero);
    }

    // 씬 뷰에 커터 범위 표시
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.6f, 0.2f, 1f, 0.8f);
        if (m_shape != Shape.Mesh)
        {
            Gizmos.DrawWireSphere(transform.position, m_radius);
            return;
        }
        var mf = m_cutterMesh != null ? m_cutterMesh : GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            Gizmos.matrix = mf.transform.localToWorldMatrix;
            Gizmos.DrawWireMesh(mf.sharedMesh);
        }
    }
}
