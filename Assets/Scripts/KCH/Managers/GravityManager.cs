using UnityEngine;

public class GravityManager
{
    public bool CanFireBlackHole { get; set; }
    public bool CanFireWhiteHole { get; set; }

    public GravityStat GravityStat { get; private set; }
    public BlackHoleController BlackHole { get; set; }
    public WhiteHoleController WhiteHole { get; set; }

    public bool IsBlackHoleActive => BlackHole.gameObject.activeSelf;
    public bool IsWhiteHoleActive => WhiteHole.gameObject.activeSelf;
    public bool ProcessWhiteHoleEliminate { get; set; }

    public GameObject LoadBlackHole => Resources.Load<GameObject>("KCH/Prefabs/BlackHole");
    public GameObject LoadWhiteHole => Resources.Load<GameObject>("KCH/Prefabs/WhiteHole");

    public void Init()
    {
        GravityStat = Resources.Load<GravityStat>("KCH/Datas/GravityStat");
        InstantiateBlackHole();
        InstantiateWhiteHole();
        CanFireBlackHole = true;
        CanFireWhiteHole = true;
    }

    public void Clear()
    {
        BlackHole.SetActive(false);
        BlackHole.gameObject.SetActive(false);
        WhiteHole.SetActive(false);
        WhiteHole.gameObject.SetActive(false);
    }

    public void CreateBlackHole(Vector3 position)
    {
        BlackHole.gameObject.SetActive(false);
        BlackHole.transform.position = position;
        BlackHole.gameObject.SetActive(true);
        WhiteHole.SetActive(true);
    }

    public void CreateWhiteHole(Vector3 position)
    {
        WhiteHole.gameObject.SetActive(false);
        WhiteHole.transform.position = position;
        WhiteHole.gameObject.SetActive(true);
        BlackHole.SetActive(true);
    }

    private void InstantiateBlackHole()
    {
        GameObject blackHole = Object.Instantiate(LoadBlackHole);
        blackHole.transform.SetParent(Managers.Instance.gameObject.transform);
        blackHole.SetActive(false);
        BlackHole = blackHole.GetComponent<BlackHoleController>();
    }

    private void InstantiateWhiteHole()
    {
        GameObject whiteHole = Object.Instantiate(LoadWhiteHole);
        whiteHole.transform.SetParent(Managers.Instance.gameObject.transform);
        whiteHole.SetActive(false);
        WhiteHole = whiteHole.GetComponent<WhiteHoleController>();
    }

    public void RetrieveWhiteHole()
    {
        WhiteHole.ProcessEliminate();
    }
}
