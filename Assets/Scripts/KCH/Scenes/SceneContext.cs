using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public abstract class SceneContext : MonoBehaviour
{
    [Header("Guide")]
    [SerializeField]
    private GuideCanvas _guideCanvas;

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

        _guideCanvas.Text = message;
        _guideCanvas.gameObject.SetActive(true);
    }

    private void RestartStage(InputAction.CallbackContext context)
    {
        Managers.Clear();
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
