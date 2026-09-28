using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
public class Stage1SceneContext : MonoBehaviour
{
    private const string INITIAL_GUIDE = @"반갑습니다! 아래는 기본 조작에 대한 안내입니다.

    {Move}: 이동
    {Jump}: 점프
    {Sprint}: 달리기
    {Interact}: 상호작용
    {Restart}: 재시작
    {Pause}: 메뉴";

    [SerializeField]
    private GameObject _guideCanvas;
    [SerializeField]
    private Text _text;
    [SerializeField]
    private TriggerChecker _nextStage;

    void Awake()
    {
        Managers.Input.SetInputMode(InputMode.UI);
        _text.text = Util.ReplaceBindingName(INITIAL_GUIDE);
        _guideCanvas.SetActive(true);

        _nextStage.OnTriggerEntered += OnNextStageEnter;
    }

    private void OnNextStageEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
            return;

        Managers.Clear();
        SceneManager.LoadScene(SceneType.Stage2Test.ToString());
    }
}
