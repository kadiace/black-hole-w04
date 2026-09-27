using NUnit.Framework.Internal;
using System;
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

    [SerializeField] private float spaceX;

    [SerializeField] private float spaceY;

    

    [SerializeField] private float flowRate =1;

    float[] heightChange;

    public bool losesand = false;


    private void Start()
    {
        m_mesh = GetComponentInChildren<MeshFilter>().mesh;
        m_verticies = m_mesh.vertices;
        m_modifiedVerts = m_mesh.vertices;
        

        if (GetComponent<MeshMake>())
        {
            spaceX = 1 / GetComponent<MeshMake>().Divid;
            spaceY = 1 / GetComponent<MeshMake>().Divid;
            xSize = GetComponent<MeshMake>().xSize;
            zSize = GetComponent<MeshMake>().zSize;
        }
        else if (GetComponent<WhiteholeMeshMake>())
        {
            spaceX = GetComponent<WhiteholeMeshMake>().spaceX;
            spaceY = GetComponent<WhiteholeMeshMake>().spaceY;
            xSize = GetComponent<WhiteholeMeshMake>().xSize;
            zSize = GetComponent<WhiteholeMeshMake>().zSize;
        }
        
    }

    void RecalculateMesh()
    {
        if (losesand)
        {
            KeepBound();
        }
        m_mesh.vertices = m_modifiedVerts;
        
        GetComponentInChildren<MeshCollider>().sharedMesh = m_mesh;
        m_mesh.RecalculateNormals();

        
    }

    private void Update()
    {
        //if (Mouse.current.rightButton.isPressed)
        //{
        //    RightClick();
        //    RecalculateMesh();
        //}
        //else if (Mouse.current.leftButton.isPressed)
        //{
        //    LeftClick();
        //    RecalculateMesh();
        //}
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

    //public void SandUp(RaycastHit hit)
    //{


    //    Vector3 hitLocal = GetComponentInChildren<MeshFilter>().transform.InverseTransformPoint(hit.point);


    //    for (int v = 0; v < m_modifiedVerts.Length; v++)
    //        {
    //            Vector3 distance = m_modifiedVerts[v] - hitLocal;
    //            float smoothingFactor = 2f;


    //            float force = deformationStength / (1f + hitLocal.sqrMagnitude);

    //            if (distance.sqrMagnitude < radius)
    //            {

    //                m_modifiedVerts[v] = m_modifiedVerts[v] + (Vector3.up * force) / smoothingFactor;



    //            }
    //        }

    //    RecalculateMesh();
        
    //}

    public void SandUp(RaycastHit hit)
    {
        if (m_modifiedVerts == null || GetComponentInChildren<MeshFilter>() == null)
            return;

        Vector3 hitLocal = GetComponentInChildren<MeshFilter>().transform.InverseTransformPoint(hit.point);
        float radiusSqr = radius * radius;

        for (int v = 0; v < m_modifiedVerts.Length; v++)
        {
            Vector3 offset = m_modifiedVerts[v] - hitLocal;
            float distanceSqr = offset.sqrMagnitude;

            if (distanceSqr >= radiusSqr)
                continue;

            // 중심에서 가장 많이 올라가고, 반경 끝에서 0이 되는 부드러운 가중치
            float distance01 = Mathf.Sqrt(distanceSqr) / radius;
            float weight = 1f - Mathf.SmoothStep(0f, 1f, distance01);

            m_modifiedVerts[v].y += deformationStength * weight * 0.5f;
        }

        RecalculateMesh();
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
                    MeshHeightChange(index, index +1,_deltatime,Vector2.right,spaceX);
                if (z < zSize)
                    MeshHeightChange(index, index+ zSize + 1,_deltatime,Vector2.up, spaceY);

                
            }
        }
        

                for (int i = 0; i < m_modifiedVerts.Length; i++)
        {
            m_modifiedVerts[i].y += heightChange[i];
        }

        
    }

    void MeshHeightChange(int _a, int _b, float _deltatime, Vector2 direction,float Spacing)
    {
        float distance = m_modifiedVerts[_a].y - m_modifiedVerts[_b].y;
        float maxSlope = Mathf.Tan(reposeAngle * Mathf.Deg2Rad);
        float allowedHeightDifference = maxSlope * Spacing;
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

   

    void KeepBound()
    {
        for (int x = 0; x <= xSize; x++)
        {
            for (int z = 0; z <= zSize; z++)
            {
                int index = z * (xSize + 1) + x;

                if (x == 0)
                    m_modifiedVerts[index] = m_verticies[index];

                if (x == xSize)
                    m_modifiedVerts[index] = m_verticies[index];

                if (z == 0)
                    m_modifiedVerts[index] = m_verticies[index];

                if (z == zSize)
                    m_modifiedVerts[index] = m_verticies[index];

            }
        }
    }

}
