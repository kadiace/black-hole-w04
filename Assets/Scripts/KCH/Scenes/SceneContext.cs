using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public abstract class SceneContext : MonoBehaviour
{
    [Header("Guide")]
    [SerializeField]
    private GameObject _guideCanvas;
    [SerializeField]
    private Text _text;

    [Header("Next Stage")]
    [SerializeField]
    private TriggerChecker _nextStage;

    protected virtual void Awake()
    {
        Managers.Input.SetInputMode(InputMode.Player);
        _nextStage.OnTriggerEntered += OnNextStageEnter;
        OnInitialize();
        Managers.Input.PlayerMap.Restart.performed += RestartStage;
    }

    protected abstract void OnInitialize();

    protected void ShowGuide(string message)
    {
        Managers.Input.SetInputMode(InputMode.UI);

        _text.text = Util.ReplaceBindingName(message);
        _guideCanvas.SetActive(true);
    }

    private void RestartStage(InputAction.CallbackContext context)
    {
        SceneManager.LoadScene(GetCurrentStage().ToString());
    }

    private void OnNextStageEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
            return;

        Managers.Clear();
        SceneManager.LoadScene(GetNextStage().ToString());
    }

    protected abstract SceneType GetCurrentStage();

    protected abstract SceneType GetNextStage();
}
