using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VITS
{
    // Blood: every drop is a ballistic particle (gravity + light drag). When it hits something it
    // leaves a stain whose size comes from its volume and speed, elongated along the impact direction.
    // Floor stains accumulate volume; enough volume in one spot becomes a pool that spreads.
    public class Blood : MonoBehaviour
    {
        public static Blood I;
        const int MAXD = 12000, B = 1023, DEC_BATCHES = 24, MAXBODY = 400, MAXPOOLS = 2000;

        readonly Vector3[] dp = new Vector3[MAXD], dv = new Vector3[MAXD];
        readonly float[] dvol = new float[MAXD], dage = new float[MAXD];
        int dn;

        Mesh sphere, quad;
        Material dropMat, poolMat;
        Material[] decMat;
        Matrix4x4[][][] dec; // [variant][batch][i]
        int[] decN, decHead;
        Matrix4x4[][] dropBatches;
        int zSeq;
        struct SkinDec { public Transform t; public Matrix4x4 m; }
        const int SKIN_MAX = 8000;
        readonly SkinDec[][] skin = new SkinDec[6][];
        readonly int[] skinN = new int[6], skinHead = new int[6];
        Matrix4x4[][] skinBatches;

        class Pool { public Transform t; public float vol, r; public Vector3 p; }
        readonly List<Pool> pools = new List<Pool>();
        readonly Dictionary<Vector2Int, float> wet = new Dictionary<Vector2Int, float>();

        public static readonly Color Fresh = new Color(0.5f, 0.0f, 0.016f, 0.97f);   // deep red, not cartoon red   // real blood is dark red, almost maroon when it lies thick

        void Awake()
        {
            I = this;
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere); sphere = s.GetComponent<MeshFilter>().sharedMesh; Destroy(s);
            quad = new Mesh { name = "decalQuad" };
            quad.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0) };
            quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            quad.RecalculateNormals(); quad.RecalculateBounds();
            quad.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);

            dropMat = Mats.Lit(new Color(0.6f, 0.0f, 0.02f), 0.9f);
            decMat = new[]
            {
                Mats.Decal(Tex(0), Fresh),                           // round splat
                Mats.Decal(Tex(1), Fresh),                           // fast splat with spines
                Mats.Decal(Tex(2), Fresh),                           // elongated drop / drip
                Mats.Decal(Tex(3), new Color(0.5f, 0.53f, 0.58f, 0.7f)),  // bullet chip (light, small)
                Mats.Decal(Tex(2), new Color(0.32f, 0.008f, 0.02f, 0.95f)), // wall drip (darker)
                Mats.Decal(Tex(5), new Color(0.42f, 0.02f, 0.035f, 1f)),     // bullet wound on skin
                // smeared blood is a thin film: lighter and more see-through than a drop or a pool
                Mats.Decal(Tex(6), new Color(0.5f, 0.0f, 0.03f, 0.72f)),     // smear (dragged body, crawling)
                Mats.Decal(Tex(7), new Color(0.52f, 0.05f, 0.06f, 0.75f)),   // bloody footprint
                Mats.Decal(Tex(7), new Color(0.64f, 0.12f, 0.12f, 0.32f)),   // fading footprint
                Mats.Decal(Tex(6), new Color(0.55f, 0.02f, 0.05f, 0.38f)),    // thin smear
            };
            for (int v = 0; v < 6; v++) skin[v] = new SkinDec[SKIN_MAX];
            skinBatches = new Matrix4x4[12][]; for (int q = 0; q < 12; q++) skinBatches[q] = new Matrix4x4[B];
            poolMat = new Material(Mats.Find("VITS/Pool", "VITS/Decal")) { mainTexture = Tex(4), color = new Color(0.2f, 0.003f, 0.01f, 1f), enableInstancing = true };
            int nv = decMat.Length;
            dec = new Matrix4x4[nv][][]; decN = new int[nv]; decHead = new int[nv];
            for (int v = 0; v < nv; v++) { dec[v] = new Matrix4x4[DEC_BATCHES][]; for (int b = 0; b < DEC_BATCHES; b++) dec[v][b] = new Matrix4x4[B]; }
            dropBatches = new Matrix4x4[(MAXD + B - 1) / B][];
            for (int b = 0; b < dropBatches.Length; b++) dropBatches[b] = new Matrix4x4[B];
        }

        // ---------- textures (generated, white RGB, alpha = shape; tinted by the material)
        static Texture2D Tex(int kind)
        {
            const int S = 128;
            var a = new float[S * S];
            System.Action<float, float, float, float> disc = (cx, cy, r, str) =>
            {
                int x0 = Mathf.Max(0, (int)(cx - r - 2)), x1 = Mathf.Min(S - 1, (int)(cx + r + 2));
                int y0 = Mathf.Max(0, (int)(cy - r - 2)), y1 = Mathf.Min(S - 1, (int)(cy + r + 2));
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float v = Mathf.Clamp01((r - d) / (kind == 4 ? 9f : 1.5f)) * str;   // pools: wide soft rim = thin film
                    if (v > a[y * S + x]) a[y * S + x] = v;
                }
            };
            float c = S / 2f;
            var rng = new System.Random(kind * 7919 + 13);
            float R() => (float)rng.NextDouble();
            if (kind == 0 || kind == 1)
            {
                float r = kind == 0 ? S * 0.26f : S * 0.2f;
                disc(c, c, r, 1);
                int lobes = 5 + (int)(R() * 4);
                for (int i = 0; i < lobes; i++) { float an = R() * 6.283f, dd = r * (0.35f + R() * 0.4f); disc(c + Mathf.Cos(an) * dd, c + Mathf.Sin(an) * dd, r * (0.35f + R() * 0.3f), 1); }
                int spikes = kind == 1 ? 14 : 3;
                for (int i = 0; i < spikes; i++)
                {
                    float an = R() * 6.283f, len = r * (0.5f + R() * (kind == 1 ? 1.1f : 0.5f)), w = r * 0.16f;
                    for (float t = 0; t < 1; t += 0.06f) disc(c + Mathf.Cos(an) * (r * 0.8f + len * t), c + Mathf.Sin(an) * (r * 0.8f + len * t), w * (1 - t * 0.7f), 1);
                    disc(c + Mathf.Cos(an) * (r * 0.85f + len * 1.1f), c + Mathf.Sin(an) * (r * 0.85f + len * 1.1f), w * 0.9f, 1);
                }
                int sat = kind == 1 ? 16 : 8;
                for (int i = 0; i < sat; i++) { float an = R() * 6.283f, dd = r * (1.3f + R() * 0.9f); disc(c + Mathf.Cos(an) * dd, c + Mathf.Sin(an) * dd, 1.2f + R() * 3.5f, 1); }
            }
            else if (kind == 2)
            {
                // drop with a tail toward +Y (the direction it was travelling)
                disc(c, c - S * 0.18f, S * 0.2f, 1);
                for (float t = 0; t < 1; t += 0.04f) disc(c + Mathf.Sin(t * 5) * 1.5f, c - S * 0.18f + t * S * 0.5f, S * 0.2f * (1 - t * 0.8f), 1);
                disc(c, c + S * 0.4f, S * 0.05f, 1);
            }
            else if (kind == 3)
            {
                disc(c, c, S * 0.14f, 1);
                
            }
            else if (kind == 5)
            {
                // small gunshot wound: dark round core, ragged edge
                disc(c, c, S * 0.3f, 1);
                for (int i = 0; i < 9; i++) { float an = R() * 6.283f; disc(c + Mathf.Cos(an) * S * 0.26f, c + Mathf.Sin(an) * S * 0.26f, S * (0.06f + R() * 0.06f), 1); }
            }
            else if (kind == 6)
            {
                // wipe: parallel streaks along +Y (the direction it was dragged), ragged sides, thinner at both ends
                var col = new float[S]; float acc = 0.5f;
                for (int x = 0; x < S; x++) { acc = acc * 0.7f + R() * 0.3f; col[x] = acc; }
                for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    float fx = Mathf.Abs(x - c) / (S * 0.5f);
                    float edge = Mathf.Clamp01((0.92f - fx - col[(x * 7 + y / 9) % S] * 0.3f) * 6f);
                    float fy = y / (float)S;
                    float ends = Mathf.Clamp01(fy * 6f) * Mathf.Clamp01((1f - fy) * 5f);
                    float blot = Mathf.Clamp01(Mathf.PerlinNoise(x * 0.06f + kind, y * 0.05f) * 1.9f - 0.45f);   // wet patches and dry gaps
                    a[y * S + x] = Mathf.Clamp01(edge * ends * (0.45f + 0.55f * col[x]) * blot * 1.5f);
                }
            }
            else if (kind == 7)
            {
                // shoe print (the quad is stretched to ~15 x 28 cm): forefoot, heel, tread lines
                disc(c, c + S * 0.17f, S * 0.3f, 1);
                disc(c, c - S * 0.29f, S * 0.2f, 1);
                for (int y = 0; y < S; y++) if (Mathf.Sin(y * 0.9f) > 0.55f) for (int x = 0; x < S; x++) a[y * S + x] *= 0.35f;
            }
            else
            {
                // pool blob: many overlapping discs, organic edge
                disc(c, c, S * 0.3f, 1);
                for (int i = 0; i < 14; i++) { float an = R() * 6.283f, dd = S * (0.1f + R() * 0.16f); disc(c + Mathf.Cos(an) * dd, c + Mathf.Sin(an) * dd, S * (0.1f + R() * 0.12f), 1); }
            }
            var px = new Color32[S * S];
            for (int i = 0; i < px.Length; i++)
            {
                float al = a[i];
                // slightly darker rim (dried edge), lighter center: reads as liquid
                byte g = (byte)(kind == 5 ? (al > 0.9f ? 170 : 255) : (al > 0.9f ? 255 : 215));
                px[i] = new Color32(g, g, g, (byte)(al * 255));
            }
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            tex.SetPixels32(px); tex.Apply(true);
            return tex;
        }

        // ---------- emission
        public void Emit(Vector3 p, Vector3 v, float volMl)
        {
            if (dn >= MAXD) return;
            v = Vector3.ClampMagnitude(v, 6f);
            dp[dn] = p; dv[dn] = v; dvol[dn] = volMl; dage[dn] = 0; dn++;
        }

        public void Spray(Vector3 p, Vector3 dir, int n, float speed, float spread, float volMin = 0.08f, float volMax = 0.6f)
        {
            // fewer, bigger, clumped drops (same total volume): real spatter is streams and blobs, not a cloud of pixels
            int m = Mathf.Max(1, n / 2);
            for (int i = 0; i < m; i++)
            {
                var d = (dir + Random.insideUnitSphere * spread).normalized;
                float sp = speed * Random.Range(0.3f, 1f), vol = Random.Range(volMin, volMax) * 3.5f;
                Emit(p, d * sp, vol);
                // satellites trailing the main drop along the same path
                if (Random.value < 0.5f) Emit(p - d * 0.01f, d * sp * 0.9f + Random.insideUnitSphere * 0.15f, vol * 0.3f);
            }
        }

        public void Hole(Vector3 p, Vector3 n) => AddDecal(3, p, n, RandomTangent(n), 0.008f, 0.008f);

        // ---------- simulation
        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            Vector3 g = Physics.gravity * dt;
            float drag = 1f - 0.25f * dt;
            for (int i = 0; i < dn;)
            {
                Vector3 p = dp[i], v = (dv[i] + g) * drag, np = p + v * dt;
                dage[i] += dt;
                Vector3 seg = np - p; float L = seg.magnitude;
                if (L > 1e-5f && Physics.Raycast(p, seg / L, out RaycastHit h, L, ~(1 << 2), QueryTriggerInteraction.Ignore))
                {
                    Land(h, v, dvol[i]); Kill(i); continue;
                }
                if (dage[i] > 20f || np.y < -5f) { Kill(i); continue; }
                dp[i] = np; dv[i] = v; i++;
            }
        }

        void Kill(int i)
        {
            int l = --dn;
            if (i != l) { dp[i] = dp[l]; dv[i] = dv[l]; dvol[i] = dvol[l]; dage[i] = dage[l]; }
        }

        void Land(RaycastHit h, Vector3 v, float vol)
        {
            Vector3 n = h.normal;
            float vnS = Vector3.Dot(v, n), vn = Mathf.Abs(vnS);
            Vector3 vt = v - vnS * n;
            float sp = v.magnitude;
            float r = Mathf.Pow(vol * 1e-6f * 0.75f / Mathf.PI, 1f / 3f);          // drop radius (m)
            float size = Mathf.Clamp(r * 2f * (3.2f + sp * 0.6f), 0.018f, 0.2f);   // stain grows with impact speed
            float el = 1f + Mathf.Clamp(vt.magnitude / (vn + 1.2f), 0f, 1.3f);   // mostly round splats, only fast grazing drops stretch       // oblique impact -> ellipse
            Vector3 up = vt.sqrMagnitude > 1e-4f ? vt.normalized : RandomTangent(n);

            if (h.rigidbody != null)
            {
                // on a body or a severed piece: the stain sticks to the skin
                var part = h.collider.GetComponent<Part>();
                if (part != null)
                {
                    Vector3 lp = part.Project(part.transform.InverseTransformPoint(h.point));
                    Vector3 ln = part.Normal(lp);
                    Vector3 lu = part.transform.InverseTransformDirection(up);
                    AddSkin(part.transform, el > 1.6f ? 2 : 0, lp, ln, lu, size * 0.9f, size * 0.9f * (el > 1.6f ? el : 1f));
                }
                return;
            }
            int variant = sp > 4.5f ? 1 : (el > 1.6f ? 2 : 0);
            AddDecal(variant, h.point, n, up, size, size * el);
            // a drop hitting at speed runs on in a long thin line (the radiating streaks around a real spatter)
            float slide = vt.magnitude;
            if (slide > 2f && vol > 0.6f && Random.value < 0.35f)
            {
                // a tail, not a needle: short, as wide as a third of the drop
                float len = Mathf.Clamp(slide * 0.035f * (0.5f + vol * 0.5f), 0.04f, 0.22f);
                AddDecal(2, h.point + up * len * 0.45f, n, up, Mathf.Max(0.008f, size * 0.35f), len);
            }
            // fast spatter makes stains; puddles come from blood that keeps flowing
            if (n.y > 0.7f) { Wet(h.point, vol * (sp < 2.5f ? 1f : 0.25f)); var sc = Cell(h.point); stain.TryGetValue(sc, out float sv); stain[sc] = sv + vol; }
            else if (Mathf.Abs(n.y) < 0.35f && vol > 0.25f && Random.value < 0.5f)
            {
                // run down the wall
                float len = Mathf.Clamp(vol * 0.35f, 0.05f, 0.5f);
                AddDecal(4, h.point + Vector3.down * len * 0.5f, n, Vector3.down, size * 0.35f, len);
            }
        }

        static Vector3 RandomTangent(Vector3 n)
        {
            var t = Vector3.Cross(n, Random.onUnitSphere);
            if (t.sqrMagnitude < 1e-4f) t = Vector3.Cross(n, Vector3.right);
            return t.normalized;
        }

        void AddDecal(int v, Vector3 p, Vector3 n, Vector3 up, float sx, float sy)
        {
            // ~24 000 stains per kind; only past that do the oldest go (blood stays for the whole session)
            int cap = DEC_BATCHES * B;
            int i = decHead[v]; decHead[v] = (i + 1) % cap; if (decN[v] < cap) decN[v]++;
            if (Mathf.Abs(Vector3.Dot(up, n)) > 0.98f) up = RandomTangent(n);
            var rot = Quaternion.LookRotation(n, up);
            float off = 0.0015f + (zSeq++ % 60) * 0.00003f;
            dec[v][i / B][i % B] = Matrix4x4.TRS(p + n * off, rot, new Vector3(sx, sy, 1));
        }

        // decals glued to a body segment (local space of t)
        void AddSkin(Transform t, int v, Vector3 lp, Vector3 ln, Vector3 lup, float sx, float sy)
        {
            if (t == null) return;
            if (Mathf.Abs(Vector3.Dot(lup.normalized, ln)) > 0.98f || lup.sqrMagnitude < 1e-6f) lup = RandomTangent(ln);
            int i = skinHead[v]; skinHead[v] = (i + 1) % SKIN_MAX; if (skinN[v] < SKIN_MAX) skinN[v]++;
            skin[v][i] = new SkinDec { t = t, m = Matrix4x4.TRS(lp + ln * 0.0025f, Quaternion.LookRotation(ln, lup), new Vector3(sx, sy, 1f)) };
        }
        public void SkinDecal(Transform t, Vector3 lp, Vector3 ln, float size, int variant) => AddSkin(t, variant, lp, ln, RandomTangent(ln), size, size);
        public void SkinStreak(Transform t, Vector3 lp, Vector3 ln, Vector3 ltan, float width, float len) => AddSkin(t, 2, lp, ln, ltan, width, len);

        void Wet(Vector3 p, float vol)
        {
            foreach (var P in pools)
                if (Mathf.Abs(P.p.y - p.y) < 0.2f && (new Vector2(P.p.x - p.x, P.p.z - p.z)).magnitude < P.r * 0.95f) { Grow(P, vol); return; }
            var cell = new Vector2Int(Mathf.FloorToInt(p.x / 0.3f), Mathf.FloorToInt(p.z / 0.3f));
            wet.TryGetValue(cell, out float w); w += vol;
            if (w > 1.5f && pools.Count < MAXPOOLS)
            {
                wet[cell] = 0;
                var go = new GameObject("pool");
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = poolMat; mr.shadowCastingMode = ShadowCastingMode.Off;
                go.transform.SetPositionAndRotation(new Vector3(p.x, p.y + 0.004f + pools.Count * 0.00005f, p.z), Quaternion.LookRotation(Vector3.up, RandomTangent(Vector3.up)));
                var P = new Pool { t = go.transform, p = p };
                pools.Add(P); Grow(P, w);
            }
            else if (w > 1.5f && pools.Count > 0)
            {
                // too many puddles: feed the nearest one instead of losing the blood
                Pool best = pools[0]; float bd = 1e9f;
                foreach (var P in pools) { float dd = (P.p - p).sqrMagnitude; if (dd < bd) { bd = dd; best = P; } }
                wet[cell] = 0; Grow(best, w);
            }
            else wet[cell] = w;
        }

        public bool PoolAt(Vector3 p)
        {
            foreach (var P in pools)
                if (Mathf.Abs(P.p.y - p.y) < 0.3f && new Vector2(P.p.x - p.x, P.p.z - p.z).magnitude < P.r * 0.8f && P.r > 0.08f) return true;
            return false;
        }

        static void Grow(Pool P, float ml)
        {
            P.vol += ml;
            // ~2.5 mm thick layer: 100 ml covers a ~11 cm radius disc
            P.r = Mathf.Min(2.2f, Mathf.Sqrt(P.vol * 1e-6f / 0.0015f / Mathf.PI));
            float d = P.r * 2.3f;
            P.t.localScale = new Vector3(d, d, 1);
        }

        // ---------- smears: shoes, feet and bodies moving through fresh blood carry it and wipe it along
        public class Track { public Vector3 last; public float load; public int side; public bool init; }
        readonly Dictionary<Vector2Int, float> stain = new Dictionary<Vector2Int, float>();
        static Vector2Int Cell(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / 0.25f), Mathf.FloorToInt(p.z / 0.25f));
        const int WORLD = ~((1 << 2) | (1 << 8) | (1 << 9));

        // fresh blood under this spot (ml); whatever is picked up is partly taken away from the floor
        float Take(Vector3 p)
        {
            float ml = 0;
            var c = Cell(p);
            if (stain.TryGetValue(c, out float s)) { ml += s; stain[c] = s * 0.85f; }
            foreach (var P in pools)
                if (Mathf.Abs(P.p.y - p.y) < 0.3f && new Vector2(P.p.x - p.x, P.p.z - p.z).magnitude < P.r * 0.9f) { ml += 25f; break; }
            return ml;
        }

        // footprints: one print per step, alternating feet; the sole loads up in blood and prints fainter each step
        public void Step(Track t, Vector3 feet, bool grounded, float stride)
        {
            if (!t.init || !grounded) { t.last = feet; t.init = true; return; }
            Vector3 mv = feet - t.last; mv.y = 0;
            float L = mv.magnitude;
            if (L < stride) return;
            t.last = feet;
            if (L > stride * 3f) return;   // teleported / carried
            t.side = 1 - t.side;
            Vector3 f = mv / L, r = Vector3.Cross(Vector3.up, f);
            Vector3 fp = feet + r * (t.side == 0 ? -0.1f : 0.1f);
            if (!Physics.Raycast(fp + Vector3.up * 0.4f, Vector3.down, out RaycastHit h, 0.8f, WORLD, QueryTriggerInteraction.Ignore) || h.normal.y < 0.7f) return;
            t.load = Mathf.Min(1f, t.load * 0.8f + Take(h.point) / 6f);
            if (t.load < 0.04f) return;
            AddDecal(t.load > 0.35f ? 7 : 8, h.point, h.normal, f, 0.15f, 0.28f);
        }

        // a body part sliding on the floor (dragged, crawling, sliding after a fall) wipes blood along its path;
        // a bleeding part leaves its own trail
        public void Drag(Track t, Vector3 p, float width, float reach, float bleed)
        {
            if (!t.init) { t.last = p; t.init = true; return; }
            Vector3 mv = p - t.last; mv.y = 0; float L = mv.magnitude;
            if (L < 0.07f) return;
            Vector3 from = t.last; t.last = p;
            if (L > 1.2f) return;   // flew through the air
            if (!Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out RaycastHit h, 0.3f + reach, WORLD, QueryTriggerInteraction.Ignore) || h.normal.y < 0.7f) return;
            // real drag marks are broken and uneven: wet patches where the body pressed, gaps, thinning out fast
            t.load = Mathf.Min(1f, t.load * 0.78f + Take(h.point) / 12f + bleed * 0.3f);
            if (t.load < 0.08f || Random.value > 0.35f + t.load * 0.6f) return;
            Vector3 mid = Vector3.Lerp(from, p, Random.value); mid.y = h.point.y;
            Vector3 side = Vector3.Cross(Vector3.up, mv / L) * Random.Range(-0.3f, 0.3f) * width;
            float wdt = width * Random.Range(0.35f, 0.85f) * (0.5f + 0.5f * t.load);
            AddDecal(t.load > 0.45f ? 6 : 9, mid + side, h.normal, (mv / L + Random.insideUnitSphere * 0.15f).normalized, wdt, L * Random.Range(0.7f, 1.3f));
            if (bleed > 0f && Random.value < 0.3f) AddDecal(0, mid + side * 2f, h.normal, RandomTangent(h.normal), wdt * 0.4f, wdt * 0.4f);
        }

        // ---------- drawing
        // explicit huge bounds: DrawMeshInstanced culled whole batches by a guessed box, so stains vanished when you turned
        static readonly Bounds World = new Bounds(Vector3.zero, Vector3.one * 2000f);
        static void Draw(Mesh m, Material mat, Matrix4x4[] arr, int n)
        {
            if (n <= 0) return;
            var rp = new RenderParams(mat) { worldBounds = World, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            Graphics.RenderMeshInstanced(rp, m, 0, arr, n);
        }
        void LateUpdate()
        {
            for (int v = 0; v < decMat.Length; v++)
            {
                int n = decN[v];
                for (int b = 0; b * B < n; b++)
                    Draw(quad, decMat[v], dec[v][b], Mathf.Min(B, n - b * B));
            }
            for (int v = 0; v < 6; v++)
            {
                int n = 0, b = 0; var arr = skin[v];
                for (int i = 0; i < skinN[v]; i++)
                {
                    var t = arr[i].t;
                    if (t == null) continue;
                    skinBatches[v * 2 + b][n++] = t.localToWorldMatrix * arr[i].m;
                    if (n == B) { Draw(quad, decMat[v], skinBatches[v * 2 + b], n); n = 0; b = 1 - b; }
                }
                if (n > 0) Draw(quad, decMat[v], skinBatches[v * 2 + b], n);
            }
            for (int i = 0; i < dn; i++)
            {
                Vector3 v = dv[i]; float sp = v.magnitude;
                float r = Mathf.Pow(dvol[i] * 1e-6f * 0.75f / Mathf.PI, 1f / 3f) * 2.2f;
                var rot = sp > 0.01f ? Quaternion.LookRotation(v / sp) : Quaternion.identity;
                dropBatches[i / B][i % B] = Matrix4x4.TRS(dp[i], rot, new Vector3(r, r, r * (1f + Mathf.Min(sp * 0.08f, 1.5f))));
            }
            for (int b = 0; b * B < dn; b++)
                Draw(sphere, dropMat, dropBatches[b], Mathf.Min(B, dn - b * B));
        }
    }
}
