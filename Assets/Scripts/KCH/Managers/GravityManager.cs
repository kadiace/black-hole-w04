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
    }

    public void Clear()
    {
        DestroyHole(HoleType.Black);
        DestroyHole(HoleType.White);
    }

    public void CreateBlackHole(Vector3 position)
    {
        if (BlackHole == null)
        {
            GameObject hole = Object.Instantiate(LoadBlackHole);
            hole.transform.position = position;
            return;
        }
        BlackHole.transform.position = position;
        BlackHole.gameObject.SetActive(true);
    }

    public void CreateWhiteHole(Vector3 position)
    {
        if (WhiteHole == null)
        {
            GameObject hole = Object.Instantiate(LoadWhiteHole);
            hole.transform.position = position;
            return;
        }
        WhiteHole.transform.position = position;
        WhiteHole.gameObject.SetActive(true);
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
