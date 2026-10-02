using UnityEngine;

namespace VITS
{
    // impacts on solid things: bullet holes, knife gouges, sparks, chips and dust (pooled, nothing allocated per shot)
    public class Fx : MonoBehaviour
    {
        static Fx I;
        const int DEC = 400, BITS = 320;
        Transform[] dec = new Transform[DEC]; int decHead;
        struct Bit { public Transform t; public Renderer r; public Vector3 v; public float life, max, size, grow; public bool spark; }
        Bit[] bits = new Bit[BITS]; int bitHead;
        Material holeM, gougeM, scorchM; MaterialPropertyBlock mpb;
        AudioSource au; AudioClip clink, thud;

        static Fx Get()
        {
            if (I == null) I = new GameObject("Fx").AddComponent<Fx>();
            return I;
        }

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            holeM = Mats.Decal(HoleTex(), Color.white, 2460);
            gougeM = Mats.Decal(GougeTex(), Color.white, 2460);
            var puff = Mats.Decal(PuffTex(), Color.white, 3050);
            scorchM = Mats.Decal(ScorchTex(), new Color(0.03f, 0.03f, 0.03f, 0.9f), 2455);
            for (int i = 0; i < DEC; i++)
            {
                var g = Mats.Vis(PrimitiveType.Quad, transform, Vector3.zero, Vector3.zero, holeM, false);
                g.GetComponent<Renderer>().receiveShadows = false; g.SetActive(false); dec[i] = g.transform;
            }
            for (int i = 0; i < BITS; i++)
            {
                var g = Mats.Vis(PrimitiveType.Quad, transform, Vector3.zero, Vector3.zero, puff, false);
                g.SetActive(false); bits[i].t = g.transform; bits[i].r = g.GetComponent<Renderer>();
            }
            au = gameObject.AddComponent<AudioSource>(); au.spatialBlend = 1f; au.minDistance = 2f; au.maxDistance = 40f;
            clink = Tone(2900f, 0.12f, 60f, 0.5f); thud = Tone(180f, 0.08f, 50f, 0.9f);
        }

        // ---------- public entry points
        public static void Bullet(RaycastHit h, Vector3 dir, float caliber)
        {
            var f = Get(); var col = SurfaceColor(h.collider); bool metal = IsMetal(h.collider);
            f.Mark(h, f.holeM, Random.Range(0.75f, 1.25f) * caliber * (metal ? 0.8f : 1.2f), dir, 1f);
            Vector3 refl = Vector3.Reflect(dir, h.normal);
            int n = metal ? 10 : 4;
            for (int i = 0; i < n; i++) f.Spawn(h.point + h.normal * 0.01f, (refl + Random.insideUnitSphere * 0.6f).normalized * Random.Range(3f, 9f), true, 0.012f, Random.Range(0.08f, 0.2f), 0f, new Color(1f, 0.8f, 0.4f, 1f));
            for (int i = 0; i < (metal ? 2 : 7); i++)   // chips of whatever was hit
                f.Spawn(h.point + h.normal * 0.01f, (h.normal + Random.insideUnitSphere * 0.7f).normalized * Random.Range(1.5f, 4f), false, Random.Range(0.008f, 0.02f), Random.Range(0.5f, 1f), 0f, col * 0.8f);
            for (int i = 0; i < 3; i++)   // dust puff that hangs and spreads
                f.Spawn(h.point + h.normal * 0.03f, (h.normal * Random.Range(0.3f, 0.9f) + Random.insideUnitSphere * 0.25f), false, 0.05f, Random.Range(0.7f, 1.3f), 0.35f, Color.Lerp(col, new Color(0.75f, 0.72f, 0.68f), 0.5f) * new Color(1, 1, 1, 0.55f));
            f.Play(h.point, metal ? f.clink : f.thud, metal ? 0.5f : 0.7f);
        }

        // a blade dragged or driven into a solid surface: a gouge along the stroke, sparks off hard things
        public static void Knife(RaycastHit h, Vector3 sweep, bool stab)
        {
            var f = Get(); bool metal = IsMetal(h.collider); var col = SurfaceColor(h.collider);
            Vector3 along = Vector3.ProjectOnPlane(sweep, h.normal); if (along.sqrMagnitude < 1e-4f) along = Vector3.ProjectOnPlane(Vector3.up, h.normal);
            if (stab) f.Mark(h, f.holeM, 0.012f, along, 2.2f);
            else f.Mark(h, f.gougeM, 0.012f, along, Random.Range(9f, 13f));
            for (int i = 0; i < (metal ? 9 : 3); i++)
                f.Spawn(h.point + h.normal * 0.01f, (along.normalized + h.normal * 0.5f + Random.insideUnitSphere * 0.5f).normalized * Random.Range(2f, 5f), metal, metal ? 0.01f : 0.008f, Random.Range(0.1f, metal ? 0.25f : 0.6f), 0f, metal ? new Color(1f, 0.85f, 0.5f, 1f) : col);
            f.Play(h.point, metal ? f.clink : f.thud, metal ? 0.8f : 0.6f);
        }

        // a grenade going off: fireball, sparks, chips, a smoke column that hangs and drifts, a scorch mark
        public static void Blast(Vector3 c, Vector3 up)
        {
            var f = Get();
            for (int i = 0; i < 14; i++)   // fireball: bright, quick, swelling
                f.Spawn(c + Random.insideUnitSphere * 0.3f + up * 0.2f, (up * 0.6f + Random.insideUnitSphere) * Random.Range(2f, 5f), false, Random.Range(0.4f, 0.8f), Random.Range(0.15f, 0.35f), 6f, new Color(1f, Random.Range(0.55f, 0.85f), 0.25f, 1f));
            for (int i = 0; i < 45; i++)
                f.Spawn(c + up * 0.1f, (Random.onUnitSphere + up * 0.6f).normalized * Random.Range(8f, 25f), true, 0.02f, Random.Range(0.1f, 0.4f), 0f, new Color(1f, 0.8f, 0.4f, 1f));
            for (int i = 0; i < 30; i++)   // dirt and gravel thrown up
                f.Spawn(c + up * 0.05f, (up * 1.2f + Random.insideUnitSphere).normalized * Random.Range(3f, 9f), false, Random.Range(0.01f, 0.035f), Random.Range(1f, 2f), 0f, new Color(0.25f, 0.22f, 0.18f, 1f));
            for (int i = 0; i < 30; i++)   // the smoke column: grey-brown, slow, lasting
                f.Spawn(c + Random.insideUnitSphere * 0.5f + up * Random.Range(0.2f, 1.2f), (up * Random.Range(0.4f, 1.6f) + Random.insideUnitSphere * 1.2f), false, Random.Range(0.4f, 0.9f), Random.Range(5f, 10f), Random.Range(0.25f, 0.6f), new Color(0.3f, 0.28f, 0.26f, 1f));
            if (Physics.Raycast(c + up * 0.3f, -up, out RaycastHit h, 1.2f, ~((1 << 2) | (1 << Mannequin.LayerWalk) | (1 << Mannequin.LayerRag)), QueryTriggerInteraction.Ignore))
                f.Mark(h, f.scorchM, Random.Range(1.6f, 2.2f), Random.onUnitSphere, 1f);
        }
        // a steel fragment striking a wall
        public static void Frag(RaycastHit h, Vector3 dir)
        {
            var f = Get();
            f.Mark(h, f.holeM, Random.Range(0.006f, 0.012f), dir, 1f);
            if (Random.value < 0.4f) f.Spawn(h.point + h.normal * 0.01f, (Vector3.Reflect(dir, h.normal) + Random.insideUnitSphere * 0.5f).normalized * 5f, true, 0.01f, 0.12f, 0f, new Color(1f, 0.8f, 0.4f, 1f));
        }

        // ---------- internals
        void Mark(RaycastHit h, Material m, float size, Vector3 along, float stretch)
        {
            var t = dec[decHead]; decHead = (decHead + 1) % DEC;
            t.gameObject.SetActive(true);
            t.GetComponent<Renderer>().sharedMaterial = m;
            Vector3 up = Vector3.ProjectOnPlane(along, h.normal);
            if (up.sqrMagnitude < 1e-4f) up = Vector3.ProjectOnPlane(Random.onUnitSphere, h.normal);
            // stuck to moving props, free in the world otherwise
            t.SetParent(h.rigidbody != null ? h.collider.transform : transform, true);
            t.SetPositionAndRotation(h.point + h.normal * (0.002f + decHead * 0.000004f), Quaternion.LookRotation(-h.normal, up));
            Vector3 ls = new Vector3(size, size * stretch, 1f);
            var ps = t.parent.lossyScale;
            t.localScale = new Vector3(ls.x / Mathf.Max(1e-4f, ps.x), ls.y / Mathf.Max(1e-4f, ps.y), 1f / Mathf.Max(1e-4f, ps.z));
            t.Rotate(0, 0, Random.Range(-8f, 8f), Space.Self);
        }

        void Spawn(Vector3 p, Vector3 v, bool spark, float size, float life, float grow, Color c)
        {
            ref Bit b = ref bits[bitHead]; bitHead = (bitHead + 1) % BITS;
            b.t.gameObject.SetActive(true); b.t.position = p; b.v = v; b.spark = spark; b.size = size; b.life = b.max = life; b.grow = grow;
            mpb.SetColor("_Color", c); b.r.SetPropertyBlock(mpb);
        }

        void Play(Vector3 p, AudioClip c, float vol) { au.transform.position = p; au.pitch = Random.Range(0.85f, 1.15f); au.PlayOneShot(c, vol); }

        void Update()
        {
            float dt = Time.deltaTime; var cam = Camera.main;
            for (int i = 0; i < BITS; i++)
            {
                ref Bit b = ref bits[i];
                if (b.life <= 0) continue;
                b.life -= dt;
                if (b.life <= 0) { b.t.gameObject.SetActive(false); continue; }
                if (b.grow > 0) { b.v *= 1f - dt * 2.5f; b.v.y += dt * (b.max > 3f ? 0.6f : 0.15f); b.size += b.grow * dt; }   // dust drifts and spreads
                else b.v += Physics.gravity * dt;
                Vector3 np = b.t.position + b.v * dt;
                if (b.grow <= 0 && Physics.Linecast(b.t.position, np, out RaycastHit hh, ~((1 << 2) | (1 << Mannequin.LayerWalk) | (1 << Mannequin.LayerRag)), QueryTriggerInteraction.Ignore))
                { np = hh.point + hh.normal * 0.005f; b.v = Vector3.Reflect(b.v, hh.normal) * 0.3f; }
                b.t.position = np;
                float k = b.life / b.max;
                if (cam != null)
                {
                    // sparks are stretched along their flight; chips and dust face the camera
                    Vector3 toCam = cam.transform.position - np;
                    if (b.spark && b.v.sqrMagnitude > 0.01f)
                    {
                        b.t.rotation = Quaternion.LookRotation(-toCam, Vector3.ProjectOnPlane(b.v, toCam));
                        b.t.localScale = new Vector3(b.size * 0.4f, b.size + b.v.magnitude * 0.012f, 1f) * Mathf.Clamp01(k * 2f);
                    }
                    else { b.t.rotation = Quaternion.LookRotation(-toCam); b.t.localScale = Vector3.one * b.size * (b.grow > 0 ? 1f : Mathf.Clamp01(k * 3f)); }
                }
                if (b.grow > 0) { mpb.Clear(); b.r.GetPropertyBlock(mpb); var c = mpb.GetColor("_Color"); c.a = (b.max > 3f ? 0.7f : b.max < 0.5f ? 1f : 0.55f) * Mathf.Min(1f, k * (b.max > 3f ? 1.6f : 1f)); mpb.SetColor("_Color", c); b.r.SetPropertyBlock(mpb); }
            }
        }

        static bool IsMetal(Collider c)
        {
            string n = c.name.ToUpper();
            if (n.Contains("CAR") || n.Contains("METAL") || n.Contains("SHELF") || n.Contains("FREEZER") || n.Contains("CART") || n.Contains("POLE") || n.Contains("LIFT") || n.Contains("GATE") || n.Contains("LAMP")) return true;
            var r = c.GetComponent<Renderer>();
            return r != null && r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Smoothness") && r.sharedMaterial.GetFloat("_Smoothness") > 0.6f;
        }
        static Color SurfaceColor(Collider c)
        {
            var r = c.GetComponent<Renderer>();
            Color col = r != null && r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor") ? r.sharedMaterial.GetColor("_BaseColor") : new Color(0.55f, 0.55f, 0.55f);
            col.a = 1f; return col;
        }

        // ---------- generated textures and sounds
        static Texture2D HoleTex()
        {
            // dark punched centre, a ring of crushed material and a few radial cracks fading out
            int N = 64; var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[N * N]; var rng = new System.Random(11);
            float[] crack = new float[12]; for (int i = 0; i < 12; i++) crack[i] = (float)rng.NextDouble() * 6.283f;
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2 - 1, dy = (y + 0.5f) / N * 2 - 1, r = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx);
                float core = Mathf.Clamp01((0.28f - r) * 20f);
                float ring = Mathf.Clamp01(1f - Mathf.Abs(r - 0.4f) * 6f) * 0.6f;
                float cr = 0; foreach (var ca in crack) { float d = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, ca * Mathf.Rad2Deg)); cr = Mathf.Max(cr, Mathf.Clamp01(1f - d / 4f) * Mathf.Clamp01(1f - r)); }
                float soot = Mathf.Clamp01(1f - r) * 0.35f;
                float al = Mathf.Max(core, Mathf.Max(ring, Mathf.Max(cr * 0.8f, soot)));
                float lum = Mathf.Lerp(0.18f, 0.02f, core);
                px[y * N + x] = new Color(lum, lum * 0.95f, lum * 0.9f, al);
            }
            t.SetPixels(px); t.Apply(); return t;
        }
        static Texture2D GougeTex()
        {
            // a thin scratch: bright scraped edges around a dark cut line, tapering at both ends
            int W = 16, H = 128; var t = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[W * H];
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W * 2 - 1, v = (y + 0.5f) / H * 2 - 1;
                float taper = Mathf.Clamp01((1f - Mathf.Abs(v)) * 3f);
                float w = 0.35f * taper + 0.01f;
                float core = Mathf.Clamp01((w * 0.5f - Mathf.Abs(u)) * 30f);
                float edge = Mathf.Clamp01(1f - Mathf.Abs(Mathf.Abs(u) - w * 0.8f) * 12f) * taper;
                px[y * W + x] = core > 0.01f ? new Color(0.05f, 0.05f, 0.05f, core) : new Color(0.85f, 0.85f, 0.82f, edge * 0.7f);
            }
            t.SetPixels(px); t.Apply(); return t;
        }
        static Texture2D ScorchTex()
        {
            int N = 64; var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2 - 1, dy = (y + 0.5f) / N * 2 - 1, r = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx);
                float rays = 0.75f + 0.25f * Mathf.PerlinNoise(a * 3f + 10f, r * 4f);
                float al = Mathf.Clamp01((rays - r) * 2.5f) * (0.6f + 0.4f * Mathf.PerlinNoise(x * 0.2f, y * 0.2f));
                px[y * N + x] = new Color(1, 1, 1, al);
            }
            t.SetPixels(px); t.Apply(); return t;
        }
        static Texture2D PuffTex()
        {
            int N = 32; var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2 - 1, dy = (y + 0.5f) / N * 2 - 1, r = Mathf.Sqrt(dx * dx + dy * dy);
                px[y * N + x] = new Color(1, 1, 1, Mathf.Clamp01(1f - r) * Mathf.Clamp01(1f - r) * 1.6f);
            }
            t.SetPixels(px); t.Apply(); return t;
        }
        static AudioClip Tone(float f, float len, float decay, float noise)
        {
            int sr = 44100, n = (int)(sr * len); var d = new float[n]; var rng = new System.Random(5); float lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr; lp += ((float)(rng.NextDouble() * 2 - 1) - lp) * 0.3f;
                d[i] = (Mathf.Sin(t * f * 6.283f) * (1f - noise) + Mathf.Sin(t * f * 1.51f * 6.283f) * (1f - noise) * 0.5f + lp * noise) * Mathf.Exp(-t * decay) * 0.6f;
            }
            var c = AudioClip.Create("impact", n, 1, sr, false); c.SetData(d, 0); return c;
        }
    }
}
