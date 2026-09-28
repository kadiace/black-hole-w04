using UnityEngine;

public class Managers : MonoBehaviour
{
    private static Managers _instance;
    public static Managers Instance { get { Init(); return _instance; } }

    private readonly InputManager _inputManager = new();
    public static InputManager Input => Instance._inputManager;
    private readonly GravityManager _gravityManager = new();
    public static GravityManager Gravity => Instance._gravityManager;

    void Start()
    {
        Init();
    }

    public static void Init()
    {
        if (_instance == null)
        {
            GameObject go = GameObject.Find("@Manager");
            if (go == null)
            {
                go = new GameObject { name = "@Manager" };
                go.AddComponent<Managers>();
            }
            DontDestroyOnLoad(go);
            _instance = go.GetComponent<Managers>();

            _instance._inputManager.Init();
            _instance._gravityManager.Init();

            GameObject eventSystem = Instantiate(Resources.Load<GameObject>("KCH/Prefabs/UIs/EventSystem"));

            eventSystem.transform.SetParent(_instance.transform);
        }
    }

    public static void Clear()
    {
        Input.Clear();
        Gravity.Clear();
    }
}
