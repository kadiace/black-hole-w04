using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 테스트용 칼날. 이 오브젝트의 위치/transform.up 이 절단 평면이 된다.
/// 플레이 중 지정 키를 누르거나, 인스펙터 우클릭 메뉴 "Slice Now" 로
/// 평면이 지나가는 모든 <see cref="Sliceable"/> 을 절단한다.
/// </summary>
public class SliceBlade : MonoBehaviour
{
    [Tooltip("플레이 중 절단 키")]
    [SerializeField] Key m_sliceKey = Key.Space;

    [Tooltip("칼날 크기(로컬 XZ). 이 사각형 범위 안에 걸친 오브젝트만 자른다. 0 이하이면 무한 평면")]
    [SerializeField] Vector2 m_bladeSize = new Vector2(5f, 5f);

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb[m_sliceKey].wasPressedThisFrame)
            SliceNow();
    }

    /// <summary>평면에 걸친 모든 Sliceable 절단.</summary>
    [ContextMenu("Slice Now")]
    public void SliceNow()
    {
        Vector3 point = transform.position;
        Vector3 normal = transform.up;

        // 순회 중 오브젝트가 생성/파괴되므로 대상 목록을 먼저 확정한다.
        var targets = new List<Sliceable>();
        foreach (var s in FindObjectsByType<Sliceable>(FindObjectsSortMode.None))
        {
            if (s.TryGetComponent(out Renderer r) && IntersectsBlade(r.bounds, point, normal))
                targets.Add(s);
        }

        int count = 0;
        foreach (var s in targets)
            if (s.Slice(point, normal)) count++;

        Debug.Log($"[SliceBlade] {count} 개 오브젝트 절단");
    }

    /// <summary>AABB 가 칼날 평면(및 사각형 범위)에 걸치는지 검사.</summary>
    bool IntersectsBlade(Bounds b, Vector3 point, Vector3 normal)
    {
        // AABB 를 평면 법선에 투영한 반경과 중심 거리 비교
        Vector3 e = b.extents;
        float r = e.x * Mathf.Abs(normal.x) + e.y * Mathf.Abs(normal.y) + e.z * Mathf.Abs(normal.z);
        float d = Vector3.Dot(normal, b.center - point);
        if (Mathf.Abs(d) > r) return false;

        if (m_bladeSize.x <= 0f || m_bladeSize.y <= 0f) return true;

        // 칼날 로컬 XZ 범위 검사 (대략적인 판정: 바운즈 중심 + 반경)
        Vector3 local = transform.InverseTransformPoint(b.center);
        float margin = e.magnitude;
        return Mathf.Abs(local.x) <= m_bladeSize.x * 0.5f + margin
            && Mathf.Abs(local.z) <= m_bladeSize.y * 0.5f + margin;
    }

    // 씬 뷰에 칼날 평면 표시
    void OnDrawGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.25f);
        Vector2 size = m_bladeSize.x > 0f && m_bladeSize.y > 0f ? m_bladeSize : new Vector2(10f, 10f);
        Gizmos.DrawCube(Vector3.zero, new Vector3(size.x, 0.001f, size.y));
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, 0f, size.y));
        Gizmos.DrawLine(Vector3.zero, Vector3.up * 0.5f);
    }
}
