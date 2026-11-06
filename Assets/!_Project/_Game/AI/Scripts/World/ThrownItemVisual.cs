using Kehai.Store;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kehai.Karen
{
    // Decoration follows the real projectile. It never changes its hit test, shelf stock,
    // effects, or the seeded gameplay RNG. Products are the store's existing imported models.
    public sealed class ThrownItemVisual : MonoBehaviour, IMapTransient
    {
        public static string ProductId(ThrownItem item) => item switch
        {
            ThrownItem.Flour => "house_laundry",
            ThrownItem.Oil => "house_dishsoap",
            _ => "canned_tuna"
        };

        Transform model;
        Vector3 shelfStart;
        float born, life;
        int number;
        bool flying;
        Material ownedMaterial;
        Mesh ownedMesh;
        Texture2D ownedTexture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            ThrowEvents.Thrown -= Thrown;
            ThrowEvents.Landed -= Landed;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Application.isBatchMode) return;
            ThrowEvents.Thrown -= Thrown;
            ThrowEvents.Landed -= Landed;
            ThrowEvents.Thrown += Thrown;
            ThrowEvents.Landed += Landed;
        }

        static void Thrown(ThrowEvent e)
        {
            if (e.Projectile == null) return;
            var fx = e.Projectile.gameObject.AddComponent<ThrownItemVisual>();
            fx.flying = true;
            fx.born = Time.time;
            fx.number = e.Projectile.Number;
            fx.model = fx.BuildProduct(e.Item).transform;
            fx.shelfStart = e.From;
            // A short reach from a nearby matching facing. Never pull a product through a
            // wall or across the shop just because that SKU is sold in a distant aisle.
            float nearest = 2.5f * 2.5f;
            foreach (ShelfSlot slot in ShelfSlot.All)
            {
                if (!slot.isFilled || slot.StockedId != ProductId(e.Item) || slot.frame == null) continue;
                Vector3 at = slot.Position + Vector3.up * .15f;
                float sqr = (at - e.From).sqrMagnitude;
                if (sqr >= nearest || Physics.Linecast(at, e.From, ~0, QueryTriggerInteraction.Ignore)) continue;
                nearest = sqr;
                fx.shelfStart = at;
            }
        }

        GameObject BuildProduct(ThrownItem item)
        {
            var go = new GameObject(ProductId(item) + " (thrown visual)");
            go.transform.SetParent(transform, false);
            ProductLook.Look? look = ProductLook.For(ProductId(item));
            if (look.HasValue)
            {
                go.AddComponent<MeshFilter>().sharedMesh = look.Value.Mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = look.Value.Materials;
            }
            else
            {
                // Code-only checkout: preserve catalogue scale, never add physics to the art.
                GameObject primitive = GameObject.CreatePrimitive(item == ThrownItem.Tin ? PrimitiveType.Cylinder : PrimitiveType.Cube);
                primitive.transform.SetParent(go.transform, false);
                Collider collider = primitive.GetComponent<Collider>();
                collider.enabled = false;
                Destroy(collider);
                ProductDef product = ProductCatalog.Get(ProductId(item));
                Vector3 size = product.Size * ProductLook.ScaleFor(product);
                primitive.transform.localScale = item == ThrownItem.Tin ? new Vector3(size.x, size.y * .5f, size.z) : size;
                ownedMaterial = MakeMaterial(product.Primary);
                primitive.GetComponent<Renderer>().sharedMaterial = ownedMaterial;
            }
            return go;
        }

        static Material MakeMaterial(Color colour)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            return new Material(shader) { color = colour };
        }

        void LateUpdate()
        {
            if (!flying) return;
            float age = Time.time - born;
            float reach = Mathf.SmoothStep(0f, 1f, age / .10f);
            model.position = Vector3.Lerp(shelfStart, transform.position, reach);
            model.rotation = Quaternion.Euler(age * 440f, number * 47f + age * 180f, age * 230f);
        }

        static void Landed(ThrowEvent e)
        {
            var go = new GameObject("Thrown " + e.Item + " impact");
            go.transform.position = e.At;
            if (e.Projectile != null) SceneManager.MoveGameObjectToScene(go, e.Projectile.gameObject.scene);
            var fx = go.AddComponent<ThrownItemVisual>();
            fx.number = e.Projectile != null ? e.Projectile.Number : 1;
            switch (e.Item)
            {
                case ThrownItem.Flour: fx.Powder(); break;
                case ThrownItem.Oil: fx.Slick(e.At); break;
                default: fx.Tin(e.At); break;
            }
            Destroy(go, fx.life);
        }

        void Powder()
        {
            life = 1.8f;
            var ps = gameObject.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false;
            ps.randomSeed = (uint)Mathf.Max(1, number);
            var main = ps.main;
            main.loop = false; main.duration = .2f; main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.6f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.4f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(.12f, .35f);
            main.startColor = new Color(.9f, .88f, .78f, .65f);
            main.maxParticles = 32; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .15f;
            var fade = ps.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(.55f, 0), new GradientAlphaKey(0, 1) });
            fade.color = gradient;
            ownedMaterial = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit"));
            ownedMaterial.SetFloat("_Surface", 1f);
            ownedMaterial.SetFloat("_Blend", 0f);
            ownedMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            ownedMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            ownedMaterial.SetFloat("_ZWrite", 0f);
            ownedMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            ownedMaterial.renderQueue = 3000;
            ownedTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[32 * 32];
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                float radius = new Vector2((x - 15.5f) / 15.5f, (y - 15.5f) / 15.5f).magnitude;
                float alpha = Mathf.Clamp01(1f - radius);
                pixels[y * 32 + x] = new Color(1f, 1f, 1f, alpha * alpha);
            }
            ownedTexture.SetPixels(pixels); ownedTexture.Apply(false, true);
            ownedMaterial.mainTexture = ownedTexture;
            if (ownedMaterial.HasProperty("_BaseMap")) ownedMaterial.SetTexture("_BaseMap", ownedTexture);
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = ownedMaterial;
            ps.Play(); ps.Emit(28);
        }

        static bool Floor(Vector3 at, out Vector3 floor)
        {
            floor = at;
            // Impact may be against a person or a shelf. The puddle/tin belongs on the floor,
            // not on top of that person's collider. No patch when there is no floor below.
            RaycastHit[] hits = Physics.RaycastAll(at + Vector3.up * .1f, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            bool found = false;
            foreach (RaycastHit hit in hits)
            {
                if (hit.normal.y < .8f || hit.collider is CharacterController ||
                    hit.collider.GetComponentInParent<Item>() != null ||
                    hit.collider.GetComponentInParent<CustomerNPC>() != null) continue;
                if (hit.distance >= nearest) continue;
                nearest = hit.distance; floor = hit.point; found = true;
            }
            return found;
        }

        void Slick(Vector3 at)
        {
            life = 4f;
            if (!Floor(at, out Vector3 floor)) return;
            transform.position = floor + Vector3.up * .012f;
            var puddle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            puddle.transform.SetParent(transform, false);
            puddle.transform.localScale = new Vector3(.95f, .006f, .7f);
            puddle.GetComponent<Collider>().enabled = false;
            Destroy(puddle.GetComponent<Collider>());
            ownedMaterial = MakeMaterial(new Color(.16f, .28f, .10f));
            puddle.GetComponent<Renderer>().sharedMaterial = ownedMaterial;
        }

        void Tin(Vector3 at)
        {
            life = 5f;
            if (!Floor(at, out Vector3 floor)) return;
            GameObject tin = BuildProduct(ThrownItem.Tin);
            MeshFilter filter = tin.GetComponent<MeshFilter>();
            if (filter != null)
            {
                ownedMesh = Instantiate(filter.sharedMesh);
                Vector3[] vertices = ownedMesh.vertices;
                Bounds bounds = ownedMesh.bounds;
                for (int i = 0; i < vertices.Length; i++)
                    if (vertices[i].x > bounds.center.x && vertices[i].y > bounds.center.y)
                        vertices[i].x = Mathf.Lerp(vertices[i].x, bounds.center.x, .35f);
                ownedMesh.vertices = vertices; ownedMesh.RecalculateNormals(); ownedMesh.RecalculateBounds();
                filter.sharedMesh = ownedMesh;
            }
            transform.SetPositionAndRotation(floor + Vector3.up * .07f, Quaternion.Euler(90f, number * 41f, 0));
            OneShotAudio.PlayAt(ProceduralAudio.Tell(TellKind.Clunk), floor, .45f);
        }

        void OnDestroy()
        {
            if (ownedMaterial != null) Destroy(ownedMaterial);
            if (ownedMesh != null) Destroy(ownedMesh);
            if (ownedTexture != null) Destroy(ownedTexture);
        }
    }
}
