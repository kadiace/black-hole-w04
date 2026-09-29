using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class UITitle : MonoBehaviour
{
    [SerializeField]
    Button btnEnter;
    [SerializeField]
    Button btnExit;

    public System.Action OnClicked_Enter;
    public System.Action OnClicked_Exit;

    private void Awake()
    {
        Managers.Input.SetInputMode(InputMode.UI);

        btnEnter.onClick.AddListener(EnterScene);
        btnExit.onClick.AddListener(() => Application.Quit());
    }

    private void EnterScene()
    {
        Managers.Clear();
        SceneManager.LoadScene(SceneType.Stage1.ToString());
    }
}
