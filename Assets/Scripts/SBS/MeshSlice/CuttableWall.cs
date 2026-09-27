using UnityEngine;

/// <summary>
/// 블랙홀 등으로 잘리는 벽.
/// 벽 자체는 제자리에 남고(Options.keepOriginalObject), 잘린 조각에는 <see cref="GrabbablePiece"/> 가 붙어
/// 플레이어가 들고 옮길 수 있다. 떨어져 나간 바깥 파편은 CuttableWall 그대로 남는다.
/// </summary>
public class CuttableWall : Sliceable
{
    [Header("잘린 조각")]
    [Tooltip("잘린 조각(GrabbablePiece)에 적용할 들기 설정")]
    [SerializeField]
    GrabbablePiece.GrabSettings m_pieceGrab = GrabbablePiece.GrabSettings.Default;

    /// <summary>잘린 조각은 들고 옮길 수 있는 GrabbablePiece 로 만든다.</summary>
    public override System.Type InsidePieceType => typeof(GrabbablePiece);

    protected internal override void OnPieceCreated(Sliceable piece, bool isInside)
    {
        if (isInside && piece is GrabbablePiece grabbable)
            grabbable.Grab = m_pieceGrab;
    }
}
