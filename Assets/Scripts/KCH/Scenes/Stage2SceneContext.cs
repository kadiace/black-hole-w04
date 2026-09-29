using UnityEngine;

public class Stage2SceneContext : SceneContext
{
    private const string BLACK_HOLE_GUIDE = @"이제 직접 블랙홀을 생성할 수 있습니다!
블랙홀은 생성 후 {ExistDuration}초 동안 유지된 후 사라집니다.
블랙홀이 생성되어있더라도 다른 위치에 즉시 새로 생성할 수 있습니다.

{BlackHole}: 블랙홀 생성";

    [Header("Black Hole Gun")]
    [SerializeField]
    private GameObject _blackHoleGun;
    [SerializeField]
    private TriggerChecker _blackHoleGunCollider;
    [Header("WhiteHole Move Collider")]
    [SerializeField]
    private TriggerChecker _whiteHoleMoveCollider;

    protected override void OnInitialize()
    {
        _blackHoleGunCollider.OnTriggerEntered += OnBlackHoleGunEnter;
        _whiteHoleMoveCollider.OnTriggerEntered += OnWhiteHoleMoveEnter;

        Managers.Gravity.CreateWhiteHole(new Vector3(-8, 5, -5));

        Managers.Gravity.CanFireBlackHole = false;
        Managers.Gravity.CanFireWhiteHole = false;
    }

    private void OnBlackHoleGunEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
            return;

        string message = BLACK_HOLE_GUIDE.Replace(
            "{ExistDuration}",
            $"{Managers.Gravity.GravityStat.ExistDuration:f0}");

        ShowGuide(message);

        _blackHoleGun.SetActive(false);
        Managers.Gravity.CanFireBlackHole = true;
    }

    private void OnWhiteHoleMoveEnter(Collider other)
    {
        Managers.Gravity.CreateWhiteHole(new Vector3(5, 0.7f, -80));
    }

    protected override SceneType GetCurrentStage() => SceneType.Stage2;

    protected override SceneType GetNextStage() => SceneType.Stage3;
}
