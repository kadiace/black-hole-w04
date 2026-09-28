using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
public class Stage2SceneContext : MonoBehaviour
{
    private readonly string BLACK_HOLE_GUIDE = @"이제 직접 블랙홀을 생성할 수 있습니다!
    블랙홀은 생성 후 {ExistDuration}초 동안 유지된 후 사라집니다.
    블랙홀이 생성되어있더라도 다른 위치에 즉시 새로 생성할 수 있습니다.

    {BlackHole}: 블랙홀 생성";

    [SerializeField]
    private GameObject _blackHoleGun;
    [SerializeField]
    private TriggerChecker _blackHoleGunCollider;
    [SerializeField]
    private GameObject _guideCanvas;
    [SerializeField]
    private Text _text;
    [SerializeField]
    private TriggerChecker _nextStage;

    void Awake()
    {
        Managers.Input.SetInputMode(InputMode.Player);
        _blackHoleGunCollider.OnTriggerEntered += OnBlackHoleGunEnter;
        _nextStage.OnTriggerEntered += OnNextStageEnter;
    }

    private void OnBlackHoleGunEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
            return;

        Managers.Input.SetInputMode(InputMode.UI);

        string message = Util.ReplaceBindingName(BLACK_HOLE_GUIDE);
        message = message.Replace("{ExistDuration}", $"{Managers.Gravity.GravityStat.ExistDuration:f0}");
        _text.text = message;
        _guideCanvas.SetActive(true);

        _blackHoleGun.SetActive(false);
        Managers.Gravity.CanFireBlackHole = true;
    }

    private void OnNextStageEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
            return;

        Managers.Clear();
        SceneManager.LoadScene(SceneType.Stage3Test.ToString());
    }
}
