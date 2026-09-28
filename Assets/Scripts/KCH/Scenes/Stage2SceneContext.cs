using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
public class Stage2SceneContext : MonoBehaviour
{
    private const string BLACK_HOLE_GUIDE = @"이제 직접 블랙홀을 생성할 수 있습니다!
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

    void Start()
    {

        Managers.Input.SetInputMode(InputMode.Player);
    }

    private void OnBlackHoleGunEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
            return;

        Managers.Input.SetInputMode(InputMode.UI);
        _text.text = Util.ReplaceBindingName(BLACK_HOLE_GUIDE);
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
