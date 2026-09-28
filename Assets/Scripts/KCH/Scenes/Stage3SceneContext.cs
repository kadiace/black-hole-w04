using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
public class Stage3SceneContext : MonoBehaviour
{
    private const string WHITE_HOLE_GUIDE = @"이제 직접 화이트홀을 생성할 수 있습니다!
    화이트홀은 한 번 생성하면 계속 유지되며, 다른 위치에 새로 생성하거나 회수할 수 있습니다.
    
    {WhiteHole}: 화이트홀 생성
    {Retrieve}: 화이트홀 회수";

    [SerializeField]
    private GameObject _whiteHoleGun;
    [SerializeField]
    private TriggerChecker _whiteHoleGunCollider;
    [SerializeField]
    private GameObject _guideCanvas;
    [SerializeField]
    private Text _text;
    [SerializeField]
    private TriggerChecker _nextStage;

    void Awake()
    {
        Managers.Input.SetInputMode(InputMode.Player);
        _whiteHoleGunCollider.OnTriggerEntered += OnWhiteHoleGunEnter;
        _nextStage.OnTriggerEntered += OnNextStageEnter;
    }

    private void OnWhiteHoleGunEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
            return;

        Managers.Input.SetInputMode(InputMode.UI);
        _text.text = Util.ReplaceBindingName(WHITE_HOLE_GUIDE);
        _guideCanvas.SetActive(true);


        _whiteHoleGun.SetActive(false);
        Managers.Gravity.CanFireWhiteHole = true;
    }

    private void OnNextStageEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
            return;

        Managers.Clear();
        SceneManager.LoadScene(SceneType.Stage4Test.ToString());
    }
}
