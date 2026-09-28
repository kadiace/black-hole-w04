using UnityEngine;

public class SandFall : MonoBehaviour
{
    public int range = 9999;

    public LayerMask groundMask;
    public LayerMask flowSandMask;

    public GameObject sandPrefab;

    public float fallTimer = 2f;

    private float m_timer;

    private GameObject m_currentSand;
    void Fall()
    {
        RaycastHit hit;
        if (Physics.Raycast(transform.position, Vector3.down, out  hit, range, groundMask))
        {
            if (m_currentSand == null)
            {

                if (hit.collider.gameObject.GetComponent<SandMesh>())
                {
                    m_currentSand = hit.collider.gameObject;
                }
                else
                {
                    m_currentSand = Instantiate(
                        sandPrefab,
                        hit.point,
                        Quaternion.identity
                    );
                }

                
                WhiteholeMeshMake meshMaker = m_currentSand.GetComponent<WhiteholeMeshMake>();
                if ((flowSandMask & (1 << hit.collider.gameObject.layer)) != 0)
                {
                    m_currentSand.GetComponent<SandMesh>().losesand = true;
                }

                meshMaker.GenerateOnGround(hit);
            }
            else
            {
                m_currentSand.GetComponent<SandMesh>().SandUp(hit);
                //Debug.Log("레이 호출");
            }
        }
        //else
        //{
        //    m_currentSand.GetComponent<SandMesh>().SandUp(hit);
        //    Debug.Log("호출");
        //}


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
}
