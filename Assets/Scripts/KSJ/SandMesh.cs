using UnityEngine;
using UnityEngine.InputSystem;

public class SandMesh : MonoBehaviour
{

    [Range(1.5f, 5f)]
    [SerializeField]
    private float radius = 2f;
    [Range(1.5f, 5f)]
    [SerializeField]
    private float deformationStength = 2f;
    [SerializeField]

    private Mesh m_mesh;
    private Vector3[] m_verticies, m_modifiedVerts;

    float m_timer =0;
    [SerializeField]
    private float simulateTime = 2;
    [SerializeField]
    private int xSize;
    [SerializeField]
    private int zSize;

    [SerializeField] private float reposeAngle = 30;

    [SerializeField] private float divid;

    [SerializeField] private float flowRate =1;

    float[] heightChange;


    private void Start()
    {
        m_mesh = GetComponentInChildren<MeshFilter>().mesh;
        m_verticies = m_mesh.vertices;
        m_modifiedVerts = m_mesh.vertices;

        
    }

    void RecalculateMesh()
    {
        m_mesh.vertices = m_modifiedVerts;
        GetComponentInChildren<MeshCollider>().sharedMesh = m_mesh;
        m_mesh.RecalculateNormals();
    }

    private void Update()
    {
        if (Mouse.current.rightButton.isPressed)
        {
            RightClick();
            RecalculateMesh();
        }
        else if (Mouse.current.leftButton.isPressed)
        {
            LeftClick();
            RecalculateMesh();
        }
        m_timer += Time.deltaTime;
        if (m_timer >= simulateTime)
        {
            SandRelaxation(m_timer);
            RecalculateMesh();
            m_timer = 0;
        }
        

    }

    void RightClick()
    {
        RaycastHit hit;
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());


        if (Physics.Raycast(ray, out hit, Mathf.Infinity))
        {
            for (int v = 0; v < m_modifiedVerts.Length; v++)
            {
                Vector3 distance = m_modifiedVerts[v] - hit.point;
                float smoothingFactor = 2f;


                float force = deformationStength / (1f + hit.point.sqrMagnitude);

                if (distance.sqrMagnitude < radius)
                {
                   
                        m_modifiedVerts[v] = m_modifiedVerts[v] + (Vector3.up * force) / smoothingFactor;
                        
                    
                   
                }
            }
        }
    }

    void LeftClick()
    {
        RaycastHit hit;
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());


        if (Physics.Raycast(ray, out hit, Mathf.Infinity))
        {
            for (int v = 0; v < m_modifiedVerts.Length; v++)
            {
                Vector3 distance = m_modifiedVerts[v] - hit.point;
                float smoothingFactor = 2f;


                float force = deformationStength / (1f + hit.point.sqrMagnitude);

                if (distance.sqrMagnitude < radius)
                {
                    
                    
                        m_modifiedVerts[v] = m_modifiedVerts[v] + (Vector3.down * force) / smoothingFactor;
                        
                    
                }
            }


            
        }
    }

    void SandRelaxation(float _deltatime)
    {
        heightChange = new float[(xSize+1) * (zSize +1)];
       for(int x=0;x< xSize; x++)
        {
            for(int z=0; z< zSize; z++)
            {
                int index = z * (xSize + 1) + x;
                if (x < xSize)
                    MeshHeightChange(index, index +1,_deltatime);
                if (z < zSize)
                    MeshHeightChange(index, index+ zSize + 1,_deltatime);
            }
        }

        for (int i = 0; i < m_modifiedVerts.Length; i++)
        {
            m_modifiedVerts[i].y += heightChange[i];
        }
    }

    void MeshHeightChange(int _a, int _b, float _deltatime)
    {
        float distance = m_modifiedVerts[_a].y - m_modifiedVerts[_b].y;
        float maxSlope = Mathf.Tan(reposeAngle * Mathf.Deg2Rad);
        float allowedHeightDifference = maxSlope * 1/divid;
        float excess = Mathf.Abs(distance) - allowedHeightDifference;
        float amount = excess  * _deltatime;

        amount = Mathf.Min(amount * flowRate, excess / 8);
        if (excess > 0)
        {
            if (distance < 0)
            {
                heightChange[_a] += amount;
                heightChange[_b] += -amount;
                
            }
            if (distance > 0)
            {
                heightChange[_b] += amount;
                heightChange[_a] += - amount;
            }
        }

    }

}
