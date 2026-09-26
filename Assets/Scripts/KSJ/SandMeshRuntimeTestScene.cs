using UnityEngine;

/// <summary>
/// Playable smoke-test scene driver for SandMesh.
///
/// It creates a floor and a flat SandMesh at runtime, then drops payload
/// cubes in three lanes: onto the surface, near its edge, and onto an empty
/// floor position. This keeps the authored test scene small and leaves the
/// existing game scenes untouched.
/// </summary>
public sealed class SandMeshRuntimeTestScene : MonoBehaviour
{
    [SerializeField, Min(1)]
    private int totalDrops = 36;
    [SerializeField]
    private bool spawnDropsOnStart = true;

    private int m_spawnedDrops;
    private Material m_dropMaterial;
    private SandMesh m_sandMesh;

    private void Start()
    {
        // The gameplay bootstrap can leave timeScale at zero while waiting
        // for input. This isolated test scene should always advance.
        Time.timeScale = 1f;
        BuildTestScene();
        if (spawnDropsOnStart)
        {
            // Create the full stream immediately. The rigidbodies still fall
            // naturally in a running editor, while the scene remains
            // inspectable when play mode is started unfocused or headless.
            for (int i = 0; i < totalDrops; i++)
                SpawnDrop(m_spawnedDrops++);
        }
    }

    private void BuildTestScene()
    {
        CreateFloor();
        CreateSandSurface();
        CreateCamera();
        CreateLight();
        m_dropMaterial = CreateMaterial(new Color(0.76f, 0.44f, 0.18f, 1f));
    }

    private void CreateFloor()
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Test Floor (empty-floor target)";
        floor.transform.SetParent(transform, false);
        floor.transform.localScale = Vector3.one * 2.5f;
        floor.GetComponent<Renderer>().sharedMaterial = CreateMaterial(new Color(0.22f, 0.24f, 0.28f, 1f));
    }

    private void CreateSandSurface()
    {
        GameObject surface = new GameObject("Test SandMesh Surface");
        surface.transform.SetParent(transform, false);
        surface.transform.position = new Vector3(-8f, 0.03f, -8f);

        MeshFilter filter = surface.AddComponent<MeshFilter>();
        MeshRenderer renderer = surface.AddComponent<MeshRenderer>();
        MeshCollider collider = surface.AddComponent<MeshCollider>();
        MeshMake meshMake = surface.AddComponent<MeshMake>();
        meshMake.ConfigureRuntime(32, 32, 0.5f, 0f);

        m_sandMesh = surface.AddComponent<SandMesh>();
        m_sandMesh.ConfigureRuntime(32, 32, 0.5f, 0.1f);
        collider.sharedMesh = filter.sharedMesh;

        Material sandMaterial = Resources.Load<Material>("KSJ/Mat/SandFlowMat");
        renderer.sharedMaterial = sandMaterial != null
            ? sandMaterial
            : CreateMaterial(new Color(0.83f, 0.58f, 0.25f, 1f));
    }

    private void SpawnDrop(int index)
    {
        int lane = index % 3;
        int laneIndex = index / 3;
        Vector3 position;
        if (lane == 0)
        {
            // Existing mesh: the surface spans world X/Z from -8 to 8.
            position = new Vector3(-4f + (laneIndex % 7) * 1.25f, 8f, -2f + (laneIndex % 3) * 1.25f);
        }
        else if (lane == 1)
        {
            // Edge lane: repeated deposits build a lip that can spill down.
            position = new Vector3(7.25f, 8f, -4f + (laneIndex % 7) * 1.25f);
        }
        else
        {
            // Empty floor: outside the authored SandMesh, so a runtime patch is created.
            position = new Vector3(10f + (laneIndex % 3) * 0.65f, 8f, -3f + (laneIndex % 5) * 1.2f);
        }

        GameObject drop = GameObject.CreatePrimitive(PrimitiveType.Cube);
        drop.name = "Falling Sand Payload " + index;
        drop.transform.SetParent(transform, false);
        drop.transform.position = position;
        drop.transform.localScale = Vector3.one * 0.35f;
        drop.GetComponent<Renderer>().sharedMaterial = m_dropMaterial;

        Rigidbody body = drop.AddComponent<Rigidbody>();
        body.mass = 0.25f;
        body.collisionDetectionMode = CollisionDetectionMode.Continuous;

        SandVolumeDeposit deposit = drop.AddComponent<SandVolumeDeposit>();
        deposit.SetSandAmount(0.45f);
    }

    private void CreateCamera()
    {
        GameObject cameraObject = new GameObject("Test Camera");
        cameraObject.transform.SetParent(transform, false);
        cameraObject.transform.position = new Vector3(0f, 14f, -19f);
        cameraObject.transform.LookAt(new Vector3(0f, 0f, 0f));
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 48f;
        camera.tag = "MainCamera";
    }

    private void CreateLight()
    {
        GameObject lightObject = new GameObject("Test Directional Light");
        lightObject.transform.SetParent(transform, false);
        lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
    }

    private static Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            return null;

        Material material = new Material(shader);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        return material;
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(18f, 18f, 620f, 28f),
            "SandMesh Runtime Test  |  center: deposit  |  right edge: spill  |  far side: new patch");
    }
}
