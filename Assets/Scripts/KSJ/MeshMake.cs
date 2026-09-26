using UnityEngine;

public class MeshMake : MonoBehaviour
{
    Mesh m_mesh;

    Vector3[] m_vertices;
    Vector2[] uvs;
    int[] m_triangles;

    public int xSize = 20;
    public int zSize = 20;
    public float Divid = 1;
    [SerializeField] private bool usePerlinNoise = true;
    [SerializeField] private float baseHeight;

    private void Awake()
    {
        m_mesh = new Mesh();
        MeshFilter filter = GetComponent<MeshFilter>();
        if (filter != null)
        {
            if (Application.isPlaying)
                filter.mesh = m_mesh;
            else
                filter.sharedMesh = m_mesh;
        }

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
                float y = usePerlinNoise
                    ? Mathf.PerlinNoise(x * .3f / Mathf.Max(0.0001f, Divid), z * .3f / Mathf.Max(0.0001f, Divid)) * 2f
                    : baseHeight;
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

        uvs = new Vector2[m_vertices.Length];

        for (int i =0,z = 0; z <= zSize; z++)
        {
            for (int x = 0; x <= xSize; x++)
            {
                uvs[i] = new Vector2((float)x / xSize, (float)z / zSize);

                i++;
            }
            
        }

    }

    public void UpdateMesh()
    {
        m_mesh.Clear();

        m_mesh.vertices = m_vertices;
        m_mesh.triangles = m_triangles;
        m_mesh.uv = uvs;

        m_mesh.RecalculateNormals();
        m_mesh.RecalculateBounds();
    }

    /// <summary>
    /// Rebuilds a flat mesh for a SandMesh patch created at runtime. Existing
    /// authored meshes keep their original Perlin-noise path.
    /// </summary>
    public void ConfigureRuntime(int runtimeXSize, int runtimeZSize, float cellSize, float runtimeBaseHeight)
    {
        xSize = Mathf.Max(1, runtimeXSize);
        zSize = Mathf.Max(1, runtimeZSize);
        Divid = cellSize > 0.0001f ? 1f / cellSize : 1f;
        baseHeight = runtimeBaseHeight;
        usePerlinNoise = false;

        if (m_mesh == null)
        {
            m_mesh = new Mesh();
            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter != null)
                filter.mesh = m_mesh;
        }

        CreateShape();
        UpdateMesh();
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
