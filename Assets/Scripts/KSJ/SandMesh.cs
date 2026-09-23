using UnityEngine;
using UnityEngine.InputSystem;

public class SandMesh : MonoBehaviour
{

    [Range(1.5f, 5f)]
    public float radius = 2f;
    [Range(1.5f, 5f)]
    public float deformationStength = 2f;

    public float relaxRange = 1;

    private Mesh m_mesh;
    private Vector3[] m_verticies, m_modifiedVerts;

    float m_timer =0;

    public float simulateTime = 2;

    public int xSize;
    public int zSize;

    public float heightLimit = 4;



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
            SandRelaxation(simulateTime);
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

    void SandRelaxation(float deltatime)
    {
        heightChange = new float[xSize * zSize];
       for(int x=0;x< xSize; x++)
        {
            for(int z=0; z< zSize; z++)
            {
                int index = z * (xSize + 1) + x;
                if (x < xSize)
                    MeshHeightChange(index, index +1);
                if (z < zSize)
                    MeshHeightChange(index, index+ zSize + 1);
            }
        }
    }

    void MeshHeightChange(int a, int b)
    {
        float distance = m_modifiedVerts[a].y - m_modifiedVerts[b].y;



        if (Mathf.Abs( distance) >= heightLimit)
        {
            if (distance > 0)
            {
                m_modifiedVerts[a].y -= distance / 2;
                m_modifiedVerts[b].y += distance / 2;
                
            }
            if (distance < 0)
            {
                m_modifiedVerts[b].y += distance / 2;
                m_modifiedVerts[a].y -= distance / 2;
            }
        }

    }

}
