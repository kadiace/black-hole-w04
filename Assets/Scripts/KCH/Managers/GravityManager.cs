using UnityEngine;

public class GravityManager
{
    public GravityStat GravityStat { get; private set; }
    public BlackHoleController BlackHole { get; set; }
    public WhiteHoleController WhiteHole { get; set; }

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
        DestroyHole(HoleType.Black);
        DestroyHole(HoleType.White);
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

    public void DestroyHole(HoleType holeType)
    {
        switch (holeType)
        {
            case HoleType.Black:
                if (BlackHole == null)
                    return;
                Object.Destroy(BlackHole.gameObject);
                BlackHole = null;
                break;
            case HoleType.White:
                if (WhiteHole == null)
                    return;
                Object.Destroy(WhiteHole.gameObject);
                WhiteHole = null;
                break;
        }
    }
}
