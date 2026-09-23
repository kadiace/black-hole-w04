using UnityEngine;

public class Managers : MonoBehaviour
{
    private static Managers _instance;

    private static Managers Instance
    {
        get
        {
            EnsureExists();
            return _instance;
        }
    }

    private readonly InputManager _inputManager = new();
    public static InputManager Input => Instance._inputManager;

    public static void EnsureExists()
    {
        if (_instance != null)
            return;

        Managers existing = FindAnyObjectByType<Managers>();
        if (existing != null)
        {
            _instance = existing;
            return;
        }

        GameObject go = GameObject.Find("@App");
        if (go == null)
            go = new GameObject("@App");

        Managers managers = go.GetComponent<Managers>();
        if (managers == null)
            managers = go.AddComponent<Managers>();

        _instance = managers;

        Instantiate(Resources.Load<GameObject>("KCH/Prefabs/UIs/EventSystem"));
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
        }

        DontDestroyOnLoad(gameObject);
        Input.Init();
    }

    void Update()
    {
        Input.Update();
    }

    public static void Clear()
    {
        Input.Clear();
    }
}