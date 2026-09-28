using UnityEngine;

/// <summary>
/// 플레이어가 들고 옮길 수 있는 절단 조각.
/// CubeGimmick(들기/놓기, 놓을 때 바닥 보정, 발판 인식)과 Sliceable(다시 자르기)을 합친 컴포넌트.
/// <see cref="CuttableWall"/> 을 자르면 잘린 조각(안쪽)에 자동으로 붙는다.
/// 상호작용: 들고 있지 않으면 들고, 들고 있으면 놓는다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class GrabbablePiece : Sliceable, IPressable, IInteractable
{
    /// <summary>들기/놓기 설정. CuttableWall 에서 조각으로 넘겨줄 수 있도록 묶어 둔다.</summary>
    [System.Serializable]
    public struct GrabSettings
    {
        [Tooltip("놓을 때 바닥(Ground 태그)을 찾는 추가 거리")]
        public float groundCheckDistance;
        [Tooltip("놓을 때 바닥 위로 띄우는 간격")]
        public float groundSkin;
        [Tooltip("놓은 뒤 다시 잡을 수 있기까지 대기 시간(초)")]
        public float regrabDelay;
        [Tooltip("플레이어 상호작용 레이캐스트에 걸리도록 설정할 레이어 이름 (비우면 레이어를 바꾸지 않음)")]
        public string interactLayer;

        public static GrabSettings Default => new GrabSettings
        {
            groundCheckDistance = 2f,
            groundSkin = 0.02f,
            regrabDelay = 0.5f,
            interactLayer = "Ground",
        };
    }

    [Header("들기")]
    [SerializeField]
    GrabSettings m_grab = GrabSettings.Default;

    /// <summary>들기 설정. 바꾸면 레이어도 다시 적용된다.</summary>
    public GrabSettings Grab
    {
        get => m_grab;
        set
        {
            m_grab = value;
            ApplyInteractLayer();
        }
    }

    Rigidbody m_rb;
    float m_nextInteractTime;

    // Awake 가 불리지 않는 경우(에디터에서 AddComponent 등)에도 안전하도록 필요할 때 가져온다
    Rigidbody Body => m_rb != null ? m_rb : (m_rb = GetComponent<Rigidbody>());

    void Awake()
    {
        // 동적으로 움직일 조각이므로 MeshCollider 는 convex 여야 한다
        if (TryGetComponent(out MeshCollider meshCol) && !meshCol.convex)
            meshCol.convex = true;

        ApplyInteractLayer();
    }

    void ApplyInteractLayer()
    {
        if (string.IsNullOrEmpty(m_grab.interactLayer))
            return;

        int layer = LayerMask.NameToLayer(m_grab.interactLayer);
        if (layer >= 0)
            gameObject.layer = layer;
    }

    /// <summary>이 상호작용자가 들고 있는 중인지.</summary>
    public bool IsHeldBy(IInteractor interactor) =>
        interactor != null && interactor.SnapAt != null && transform.IsChildOf(interactor.SnapAt);

    public void Interact(IInteractor interactor)
    {
        if (interactor == null || interactor.SnapAt == null)
            return;

        // 들고 있으면 놓기 (플레이어는 Interact 만 호출하므로 같은 키로 놓을 수 있게)
        if (IsHeldBy(interactor))
        {
            Release(interactor);
            return;
        }

        if (Time.time < m_nextInteractTime)
            return;

        // 키네마틱으로 바꾸기 전에 속도 초기화 (키네마틱 바디에 속도를 넣으면 경고)
        Body.linearVelocity = Vector3.zero;
        Body.angularVelocity = Vector3.zero;
        Body.isKinematic = true;

        // 조각의 피벗은 원래 벽 중심에 있으므로, 피벗이 아니라 조각의 실제 중심이 손 위치에 오게 맞춘다
        transform.SetParent(interactor.SnapAt);
        transform.position += interactor.SnapAt.position - GetWorldCenter();
    }

    public void Release(IInteractor interactor)
    {
        if (!IsHeldBy(interactor))
            return;

        transform.SetParent(null);

        ResolveGroundOverlap();

        Body.isKinematic = false;
        Body.linearVelocity = Vector3.zero;
        Body.angularVelocity = Vector3.zero;

        m_nextInteractTime = Time.time + m_grab.regrabDelay;
    }

    /// <summary>조각 메시의 실제 중심 (월드).</summary>
    Vector3 GetWorldCenter()
    {
        if (TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
            return transform.TransformPoint(filter.sharedMesh.bounds.center);
        return transform.position;
    }

    /// <summary>놓을 때 바닥(Ground 태그)에 파묻혔으면 위로 올려준다. (CubeGimmick 과 동일한 방식)</summary>
    void ResolveGroundOverlap()
    {
        var col = GetComponent<Collider>();
        if (col == null)
            return;

        Vector3 origin = col.bounds.center + Vector3.up * 0.1f;
        float castDistance = col.bounds.extents.y + m_grab.groundCheckDistance;

        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, castDistance, ~0, QueryTriggerInteraction.Ignore);

        RaycastHit? groundHit = null;
        foreach (RaycastHit hit in hits)
        {
            if (!hit.collider.CompareTag("Ground"))
                continue;

            if (groundHit == null || hit.distance < groundHit.Value.distance)
                groundHit = hit;
        }

        if (groundHit == null)
            return;

        float bottom = col.bounds.min.y;
        float groundY = groundHit.Value.point.y + m_grab.groundSkin;
        if (bottom >= groundY)
            return;

        transform.position += Vector3.up * (groundY - bottom);
    }
}
