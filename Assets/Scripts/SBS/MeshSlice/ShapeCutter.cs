using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 테스트/게임용 모양 커터. 구체 모드면 이 오브젝트 위치와 반지름으로 (CSG 또는 버텍스 사영 방식),
/// 메시 모드면 지정한 MeshFilter 의 모양/트랜스폼 그대로 겹친 <see cref="Sliceable"/> 을 절단한다.
/// 플레이 중 지정 키 또는 인스펙터 우클릭 "Cut Now" 로 실행.
/// </summary>
public class ShapeCutter : MonoBehaviour
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

    [Tooltip("메시 모드에서 모양으로 쓸 MeshFilter (닫힌 메시, Read/Write Enabled). 비우면 자기 자신의 MeshFilter")]
    [SerializeField] MeshFilter m_cutterMesh;

    [Tooltip("플레이 중 절단 키")]
    [SerializeField] Key m_cutKey = Key.C;

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb[m_cutKey].wasPressedThisFrame)
            CutNow();
    }

    /// <summary>커터와 겹친 모든 Sliceable 절단.</summary>
    [ContextMenu("Cut Now")]
    public void CutNow()
    {
        Bounds cutterBounds = GetCutterBounds(out Mesh mesh, out Matrix4x4 matrix);
        if (m_shape == Shape.Mesh && mesh == null)
        {
            Debug.LogWarning("[ShapeCutter] 커터 메시가 없습니다.", this);
            return;
        }

        // 순회 중 오브젝트가 생성/파괴되므로 대상 목록을 먼저 확정
        var targets = new List<Sliceable>();
        foreach (var s in FindObjectsByType<Sliceable>(FindObjectsSortMode.None))
        {
            if (s.gameObject == gameObject) continue; // 자기 자신은 제외
            if (s.TryGetComponent(out Renderer r) && r.bounds.Intersects(cutterBounds))
                targets.Add(s);
        }

        int count = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var s in targets)
        {
            bool ok = m_shape switch
            {
                Shape.Sphere => s.CutSphere(transform.position, m_radius),
                Shape.SphereProjection => s.CutSphereByProjection(transform.position, m_radius),
                _ => s.CutByMesh(mesh, matrix),
            };
            if (ok) count++;
        }
        Debug.Log($"[ShapeCutter] {count} 개 오브젝트 절단 ({sw.ElapsedMilliseconds} ms)");
    }

    Bounds GetCutterBounds(out Mesh mesh, out Matrix4x4 matrix)
    {
        mesh = null;
        matrix = Matrix4x4.identity;
        if (m_shape != Shape.Mesh)
            return new Bounds(transform.position, Vector3.one * (m_radius * 2f));

        var mf = m_cutterMesh != null ? m_cutterMesh : GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return default;
        mesh = mf.sharedMesh;
        matrix = mf.transform.localToWorldMatrix;
        return mf.TryGetComponent(out Renderer r) ? r.bounds : new Bounds(mf.transform.position, Vector3.zero);
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
