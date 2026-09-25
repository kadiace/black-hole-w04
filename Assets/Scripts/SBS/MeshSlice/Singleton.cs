using System;
using UnityEngine;

using UnityEngine.SceneManagement;

//********************************************************
// 싱글톤 구현체 모음 
// 싱글스레드용 하나
// 멀티스레드용 둘 (Lazy 방식 Lock방식).
// 멀티스레드
//********************************************************

//관련 문서
//Lock문 https://docs.microsoft.com/ko-kr/dotnet/csharp/language-reference/statements/lock
//Lazy문 https://docs.microsoft.com/ko-kr/dotnet/api/system.lazy-1?view=netcore-3.1
//여러가지 싱글톤 구현체 https://csharpindepth.com/Articles/Singleton

/// <summary>
/// 싱글 스레드 전용!!
/// </summary>
public class Singleton<T> where T : class, new()
{
    public static T Instance
    {
        get
        {
            if (Instance == null)
            {
                Instance = new T();
            }
            return Instance;
        }
        private set
        {
            Instance = value;
        }
    }
}

/// <summary>
/// 싱글톤 lazy 이용한 구현체.
/// 초기화를 지연시켜 접근시도 당시에 객체를 생성함.
/// Lock과 같이 생성시에만 안전이 보장됨. 그 뒤는 보장 불가능.
/// 생성 시간이 긴 오브젝트를 필요시에만 생성하거나.
/// 리소스 많이 사용하는 실행을 필요시에만 할떄.
/// 자원 생성이 멀티스레드 환경에서 안전하게 진행해야 할떄만 사용
/// </summary>
public class SingleTonLazy<T> where T : class, new()
{
    private static readonly Lazy<T> instance = new Lazy<T>(() => new T());

    public static T Instance
    {
        get
        {
            return instance.Value;
        }
    }
}
/// <summary>
/// 싱글톤 Lock 사용한 구현체.
/// 생성시 단 하나의 스레드만 생성할수 있어 여러 싱글톤 인스턴스 생성을 막음.
/// 단 그렇다해서 항상 모든 멀티 스레드 환경에서 안전한건 아님. 복수의 인스턴스가 생성되지 않는것이 한계
/// </summary>
public class SingletonLock<T> where T : class, new()
{
    private static T instance;
    private static object locker = new object();

    public static T Instance
    {
        get
        {
            if (instance == null)
            {
                lock (locker)
                {
                    if (instance == null)
                        instance = new T();
                }
            }
            return instance;
        }
    }
}

/// <summary>
/// 게임오브젝트 형식의 싱글톤 스레드
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class GameObjectSingleton<T> : MonoBehaviour where T : MonoBehaviour
{
    /// <summary>
    /// 인스턴스
    /// </summary>
    private static T instance;

    /// <summary>
    /// 인스턴스 받기
    /// </summary>
    /// <value>인스턴스.</value>
    public static T Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<T>();
                if (instance == null)
                {
                    GameObject obj = new GameObject();
                    obj.name = typeof(T).Name;
                    instance = obj.AddComponent<T>();
                }
            }
            return instance;
        }
    }

    /// <summary>
    /// 인스턴스 로드 중 호출
    /// </summary>
    protected virtual void Awake()
    {
        if (instance == null)
        {
            instance = this as T;
            SceneManager.sceneLoaded += OnSceneLoaded;

            DontDestroyOnLoad(gameObject);
        }
        else
        {
            //만약 instance가 이미 존재할 경우 파괴
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 객체 삭제직전 호출
    /// </summary>
    protected virtual void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>
    /// 씬 로드 완료시 호출
    /// </summary>
    protected virtual void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.buildIndex == 1)
            OnTitle();
    }

    /// <summary>
    /// 생성 후 씬로드 완료시 호출
    /// </summary>
    protected abstract void OnTitle();
}
