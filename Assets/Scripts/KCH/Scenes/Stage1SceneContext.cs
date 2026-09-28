using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
public class Stage1SceneContext : MonoBehaviour
{
    private const string INITIAL_GUIDE = @"게임에서: 
    {Move}: 이동
    {Jump}: 점프
    {Sprint}: 달리기
    {Interact}: 상호작용
    {Escape}: 메뉴";

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
