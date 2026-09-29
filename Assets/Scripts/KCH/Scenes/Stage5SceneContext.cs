using UnityEngine;
using UnityEngine.SceneManagement;

public class Stage5SceneContext : SceneContext
{
    private const string WHITE_HOLE_GUIDE = @"이제 직접 화이트홀을 생성할 수 있습니다!
화이트홀은 한 번 생성하면 계속 유지되며, 다른 위치에 새로 생성하거나 회수할 수 있습니다.

{WhiteHole}: 화이트홀 생성
{Retrieve}: 화이트홀 회수";

    [Header("White Hole Gun")]
    [SerializeField]
    private GameObject _whiteHoleGun;
    [SerializeField]
    private TriggerChecker _whiteHoleGunCollider;

    protected override void OnInitialize()
    {
        _whiteHoleGunCollider.OnTriggerEntered += OnWhiteHoleGunEnter;

        Managers.Gravity.CanFireBlackHole = true;
        Managers.Gravity.CanFireWhiteHole = true;
    }

    private void OnWhiteHoleGunEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
            return;

        ShowGuide(WHITE_HOLE_GUIDE);

        _whiteHoleGun.SetActive(false);
        Managers.Gravity.CanFireWhiteHole = true;
    }

    protected override SceneType GetCurrentStage() => SceneType.Stage4;

    protected override SceneType GetNextStage() => SceneType.Stage5;

    public void RestartAfterDrowning()
    {
        Managers.Clear();
        SceneManager.LoadScene(SceneType.Stage5.ToString());
    }
}
