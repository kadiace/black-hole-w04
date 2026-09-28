using UnityEngine;
using UnityEngine.UI;
public class Stage1SceneContext : MonoBehaviour
{
    private const string INITIAL_GUIDE = @"안녕 클레오 파트라 세상에서 제일 가는 포테이토 칩";

    [SerializeField]
    private GameObject _guideCanvas;
    [SerializeField]
    private Text _text;

    void Awake()
    {
        Managers.Input.SetInputMode(InputMode.UI);
        _text.text = INITIAL_GUIDE;
        _guideCanvas.SetActive(true);
    }
}
