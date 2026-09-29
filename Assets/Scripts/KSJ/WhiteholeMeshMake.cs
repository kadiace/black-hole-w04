using UnityEngine;

public class WhiteholeMeshMake : MonoBehaviour
{
    Mesh m_mesh;

    Vector3[] m_vertices;
    Vector2[] uvs;
    int[] m_triangles;

    public int xSize = 20;
    public int zSize = 20;
    public float spaceX;

    public float spaceY;
    public float vertexSpacing = 0.25f;



    private void Awake()
    {
        m_mesh = new Mesh();
        GetComponent<MeshFilter>().mesh = m_mesh;
    }



    public void UpdateMesh()
    {
        m_mesh.Clear();
        CalcTriangles();
        m_mesh.vertices = m_vertices;
        m_mesh.triangles = m_triangles;
        m_mesh.uv = uvs;

        m_mesh.RecalculateBounds();
        m_mesh.RecalculateNormals();
        m_mesh.RecalculateTangents();
    }

    private void OnDrawGizmos()
    {

        if (m_vertices == null)
            return;

        for (int i = 0; i < m_vertices.Length; i++)
        {
            Gizmos.DrawSphere(m_vertices[i], .1f);
        }
    }

    public void GenerateOnGround(RaycastHit hit)
    {
        Collider ground = hit.collider;
        Bounds bounds = ground.bounds;

        xSize = Mathf.Max(
            1, Mathf.CeilToInt(bounds.size.x / vertexSpacing));
        zSize = Mathf.Max(
            1, Mathf.CeilToInt(bounds.size.z / vertexSpacing));

        float width = bounds.size.x;
        float depth = bounds.size.z;

        spaceX = width / xSize;
        spaceY = depth / zSize;

        m_vertices = new Vector3[(xSize + 1) * (zSize + 1)];
        uvs = new Vector2[m_vertices.Length];

        for (int z = 0; z <= zSize; z++)
        {
            for (int x = 0; x <= xSize; x++)
            {
                int i = z * (xSize + 1) + x;

                float localX = -width * 0.5f + width * x / xSize;
                float localZ = -depth * 0.5f + depth * z / zSize;

                m_vertices[i] = new Vector3(localX, 0f, localZ);
                uvs[i] = new Vector2((float)x / xSize, (float)z / zSize);
            }
        }

        transform.position = new Vector3(
       bounds.center.x,
       hit.point.y - 0.01f,
       bounds.center.z
   );


        UpdateMesh();


    }

    void CalcTriangles()
    {
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
}
