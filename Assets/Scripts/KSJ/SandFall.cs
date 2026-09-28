using UnityEngine;

public class SandFall : MonoBehaviour
{
    public int range = 9999;

    public LayerMask groundMask;
    public LayerMask flowSandMask;

    public GameObject sandPrefab;

    public float fallTimer = 1f;

    private float m_timer;

    private GameObject m_currentSand;

    public void Start()
    {
        m_timer = fallTimer;
    }
    void Fall()
    {
        RaycastHit[] hits = Physics.RaycastAll(transform.position + Vector3.up * range, Vector3.down, range * 2, groundMask);

        if (hits.Length == 0)
            return;

        // 맞은 것 중 가장 가까운 모래를 찾습니다.
        bool foundSand = false;
        RaycastHit sandHit = default;
        SandMesh hitSand = null;

        foreach (RaycastHit hit in hits)
        {
            SandMesh sand = hit.collider.GetComponent<SandMesh>();




            if (sand != null && (!foundSand || hit.distance < sandHit.distance))
            {
                foundSand = true;
                sandHit = hit;
                hitSand = sand;
            }
        }

        // 모래가 맞았으면 모래만 올리고 종료
        if (foundSand)
        {
            m_currentSand = hitSand.gameObject;
            hitSand.SandUp(sandHit);
            return;
        }

        // 모래가 없을 때만 가장 가까운 바닥
        RaycastHit groundHit = hits[0];

        foreach (RaycastHit hit in hits)
        {
            if (hit.distance < groundHit.distance)
                groundHit = hit;
        }

        m_currentSand = Instantiate(sandPrefab, groundHit.point, Quaternion.identity);

        SandMesh newSand = m_currentSand.GetComponent<SandMesh>();

        if ((flowSandMask & (1 << groundHit.collider.gameObject.layer)) != 0)
            newSand.loseSand = true;

        WhiteholeMeshMake meshMaker = m_currentSand.GetComponent<WhiteholeMeshMake>();

        meshMaker.GenerateOnGround(groundHit);
        newSand.SandUp(groundHit);




    }

    private void Update()
    {
        m_timer += Time.deltaTime;

        if (m_timer >= fallTimer)
        {
            Fall();
            m_timer = 0;
        }
    }

    public void Disable()
    {
        m_currentSand = null;
        m_timer = fallTimer;
        gameObject.SetActive(false);
    }

    public void Enable()
    {

        gameObject.SetActive(true);
    }
}
