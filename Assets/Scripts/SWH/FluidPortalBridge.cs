using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(FluidSimulation))]
public sealed class FluidPortalBridge : MonoBehaviour
{
    public struct PortalState
    {
        public Vector3 InnerCenter;
        public float InnerRadius;
        public Vector3 EventHorizonCenter;
        public float EventHorizonRadius;
        public Vector3 ExitCenter;
        public float PullAcceleration;
    }

    [SerializeField, Min(0f)] private float pullAcceleration = 15f;

    // 홀 상태
    public bool TryGetPortal(out PortalState state)
    {
        state = default;

        GravityManager gravityManager = Managers.Gravity;
        BlackHoleController blackHole = gravityManager.BlackHole;
        WhiteHoleController whiteHole = gravityManager.WhiteHole;

        if (blackHole == null || whiteHole == null || !blackHole.IsFullyExpanded || !whiteHole.isActiveAndEnabled)
        {
            return false;
        }

        SphereCollider inner = blackHole.InnerCollider;
        SphereCollider eventHorizon = blackHole.EventHorizonCollider;

        if (inner == null || eventHorizon == null || !inner.enabled || !eventHorizon.enabled)
        {
            return false;
        }

        state = new PortalState
        {
            InnerCenter = inner.transform.TransformPoint(inner.center),
            InnerRadius = WorldRadius(inner),
            EventHorizonCenter = eventHorizon.transform.TransformPoint(eventHorizon.center),
            EventHorizonRadius = WorldRadius(eventHorizon),
            ExitCenter = whiteHole.transform.position,
            PullAcceleration = pullAcceleration
        };

        return true;
    }

    // 실제 반경
    private static float WorldRadius(SphereCollider sphere)
    {
        Vector3 scale = sphere.transform.lossyScale;
        float largestScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        return sphere.radius * largestScale;
    }
}