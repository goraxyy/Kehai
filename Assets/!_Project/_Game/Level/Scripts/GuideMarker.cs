using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// "Look here" markers built entirely in code, so nothing needs authoring in the scene:
// a flat ring that lies on the floor and a bobbing cone that hangs over a shelf or a head.
//
// Meshes and materials are shared between every marker in the level — a hundred of these
// cost one draw call each and no assets.
public class GuideMarker : MonoBehaviour
{
    [Tooltip("Track this transform, offset by followHeight. Leave null to sit still.")]
    public Transform follow;
    public float followHeight = 0.03f;

    public float bobAmplitude;
    public float bobSpeed = 2f;
    public float spinSpeed;

    Vector3 anchor;
    float phase;

    void Awake()
    {
        anchor = transform.position;
        phase = Random.value * 10f;
    }

    public void SetAnchor(Vector3 position) => anchor = position;

    void LateUpdate()
    {
        Vector3 position = follow != null ? follow.position + Vector3.up * followHeight : anchor;

        if (bobAmplitude > 0f)
            position.y += Mathf.Sin((Time.time + phase) * bobSpeed) * bobAmplitude;

        transform.position = position;

        if (spinSpeed != 0f)
            transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
    }

    // --- factories ----------------------------------------------------------

    // A flat ring lying on the floor. radius is in metres; innerRatio 0 gives a filled disc.
    public static GuideMarker CreateRing(string name, Color colour, float radius, float innerRatio)
    {
        GuideMarker marker = Build(name, RingMesh(innerRatio), colour, false);
        marker.SetRadius(radius);
        return marker;
    }

    // A downward-pointing cone that floats above something and can be seen from a distance.
    public static GuideMarker CreateBeacon(string name, Color colour, float size)
    {
        GuideMarker marker = Build(name, ConeMesh(), colour, true);
        marker.transform.localScale = new Vector3(size, size * 1.4f, size);
        marker.bobAmplitude = size * 0.35f;
        marker.spinSpeed = 55f;
        return marker;
    }

    // The same cone, but pinned over a moving target instead of a fixed point.
    public static GuideMarker CreateBeaconOver(string name, Color colour, float size, Transform target, float height)
    {
        GuideMarker marker = CreateBeacon(name, colour, size);
        marker.follow = target;
        marker.followHeight = height;
        marker.transform.position = target.position + Vector3.up * height;
        return marker;
    }

    public void SetRadius(float radius)
    {
        transform.localScale = new Vector3(radius, 1f, radius);
    }

    static GuideMarker Build(string name, Mesh mesh, Color colour, bool onTop)
    {
        GameObject go = new GameObject(name);
        if (!Edition.ShowUI) return go.AddComponent<GuideMarker>();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = MaterialFor(colour, onTop);
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;

        return go.AddComponent<GuideMarker>();
    }

    // --- shared materials ---------------------------------------------------

    static readonly Dictionary<int, Material> materials = new Dictionary<int, Material>();

    static Material MaterialFor(Color colour, bool onTop)
    {
        int key = colour.GetHashCode() * 2 + (onTop ? 1 : 0);
        if (materials.TryGetValue(key, out Material cached) && cached != null) return cached;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");

        Material material = new Material(shader) { hideFlags = HideFlags.DontSave };

        // Transparent, unlit, double-sided, no depth writes.
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Off);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");

        // A beacon is useless if a shelf hides it, so it draws over the world where the
        // pipeline lets us say so. Floor rings keep normal depth testing.
        if (onTop && material.HasProperty("_ZTest"))
            material.SetFloat("_ZTest", (float)CompareFunction.Always);

        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
        material.color = colour;
        material.renderQueue = (int)RenderQueue.Transparent + (onTop ? 50 : 10);

        materials[key] = material;
        return material;
    }

    // --- shared meshes ------------------------------------------------------

    static readonly Dictionary<int, Mesh> ringMeshes = new Dictionary<int, Mesh>();
    static Mesh coneMesh;

    // Unit-radius ring in the XZ plane. Scale the transform to size it.
    static Mesh RingMesh(float innerRatio)
    {
        int key = Mathf.RoundToInt(Mathf.Clamp01(innerRatio) * 100f);
        if (ringMeshes.TryGetValue(key, out Mesh cached) && cached != null) return cached;

        const int segments = 48;
        float inner = key / 100f;

        var vertices = new Vector3[segments * 2];
        var triangles = new int[segments * 6];

        for (int i = 0; i < segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

            vertices[i * 2] = direction * inner;
            vertices[i * 2 + 1] = direction;

            int next = (i + 1) % segments;
            int t = i * 6;
            triangles[t] = i * 2;
            triangles[t + 1] = i * 2 + 1;
            triangles[t + 2] = next * 2 + 1;
            triangles[t + 3] = i * 2;
            triangles[t + 4] = next * 2 + 1;
            triangles[t + 5] = next * 2;
        }

        Mesh mesh = new Mesh { name = $"GuideRing_{key}", hideFlags = HideFlags.DontSave };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        ringMeshes[key] = mesh;
        return mesh;
    }

    // A cone with its point at the origin, opening upwards.
    static Mesh ConeMesh()
    {
        if (coneMesh != null) return coneMesh;

        const int segments = 20;

        // tip, then the rim, then a centre vertex to cap the open end
        var vertices = new Vector3[segments + 2];
        vertices[0] = Vector3.zero;                  // tip, pointing down at the target
        for (int i = 0; i < segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            vertices[i + 1] = new Vector3(Mathf.Cos(angle) * 0.5f, 1f, Mathf.Sin(angle) * 0.5f);
        }
        vertices[segments + 1] = new Vector3(0f, 1f, 0f);

        var triangles = new int[segments * 6];
        for (int i = 0; i < segments; i++)
        {
            int rim = i + 1;
            int next = (i + 1) % segments + 1;

            int t = i * 6;
            triangles[t] = 0;                        // side
            triangles[t + 1] = next;
            triangles[t + 2] = rim;

            triangles[t + 3] = segments + 1;         // cap
            triangles[t + 4] = rim;
            triangles[t + 5] = next;
        }

        coneMesh = new Mesh { name = "GuideCone", hideFlags = HideFlags.DontSave };
        coneMesh.vertices = vertices;
        coneMesh.triangles = triangles;
        coneMesh.RecalculateNormals();
        coneMesh.RecalculateBounds();
        return coneMesh;
    }
}
