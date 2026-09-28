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

        GameObject blackHole = Object.Instantiate(LoadBlackHole);
        blackHole.SetActive(false);
        BlackHole = blackHole.GetComponent<BlackHoleController>();

        GameObject whiteHole = Object.Instantiate(LoadWhiteHole);
        whiteHole.SetActive(false);
        WhiteHole = whiteHole.GetComponent<WhiteHoleController>();
    }

    public void Clear()
    {
        DestroyHoles();
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

    public void RetrieveWhiteHole()
    {
        WhiteHole.ProcessEliminate();
    }

    public void DestroyHoles()
    {
        if (BlackHole != null)
        {
            Object.Destroy(BlackHole.gameObject);
            BlackHole = null;
        }

        if (WhiteHole != null)
        {
            Object.Destroy(WhiteHole.gameObject);
            WhiteHole = null;
        }
    }
}
