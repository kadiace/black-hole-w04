using UnityEngine;
using UnityEngine.UIElements;

public class MeshMake : MonoBehaviour
{
    Mesh m_mesh;

    Vector3[] m_vertices;
    int[] m_triangles;

    public int xSize = 20;
    public int zSize = 20;
    public float Divid = 1;

    private void Awake()
    {
        m_mesh = new Mesh();
        GetComponent<MeshFilter>().mesh = m_mesh;

        CreateShape();
        UpdateMesh();
    }

    void CreateShape()
    {
        m_vertices = new Vector3[(xSize +1)* (zSize+1)];

     

        for(int i =0, z =0; z <= zSize; z++)
        {
            for(int x =0; x <= xSize; x++)
            {
                //여기에서 y 계산
                float y = Mathf.PerlinNoise(x * .3f / Divid, z * .3f / Divid) * 2f;
                m_vertices[i] = new Vector3(x/Divid, y, z / Divid);
                i++;
            }
        }

        m_triangles = new int[xSize * zSize * 6];

        int vert = 0;
        int tris = 0;

        for (int z = 0; z < zSize; z++)
        {
            for (int x = 0; x < xSize; x++)
            {
                

                m_triangles[tris + 0] = vert + 0;
                m_triangles[tris + 1] = vert + xSize + 1;
                m_triangles[tris + 2] = vert + 1;
                m_triangles[tris + 3] = vert + 1;
                m_triangles[tris + 4] = vert + xSize + 1;
                m_triangles[tris + 5] = vert + xSize + 2;

                vert++;
                tris += 6;
            }
            vert++;
        }
    }

    public void UpdateMesh()
    {
        m_mesh.Clear();

        m_mesh.vertices = m_vertices;
        m_mesh.triangles = m_triangles;

        m_mesh.RecalculateBounds();
    }

    private void OnDrawGizmos()
    {

        if(m_vertices == null)
            return;

        for(int i = 0; i < m_vertices.Length; i++)
        {
            Gizmos.DrawSphere(m_vertices[i], .1f);
        }
    }
}
