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
        const int MAXD = 4000, B = 1023, DEC_BATCHES = 3, MAXBODY = 400, MAXPOOLS = 160;

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
        readonly Queue<GameObject> bodyDec = new Queue<GameObject>();

        class Pool { public Transform t; public float vol, r; public Vector3 p; }
        readonly List<Pool> pools = new List<Pool>();
        readonly Dictionary<Vector2Int, float> wet = new Dictionary<Vector2Int, float>();

        public static readonly Color Fresh = new Color(0.58f, 0.02f, 0.04f, 0.97f);

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

            dropMat = Mats.Lit(new Color(0.6f, 0.02f, 0.04f), 0.8f);
            decMat = new[]
            {
                Mats.Decal(Tex(0), Fresh),                           // round splat
                Mats.Decal(Tex(1), Fresh),                           // fast splat with spines
                Mats.Decal(Tex(2), Fresh),                           // elongated drop / drip
                Mats.Decal(Tex(3), new Color(0.08f, 0.08f, 0.09f, 0.9f)), // bullet hole
                Mats.Decal(Tex(2), new Color(0.45f, 0.01f, 0.03f, 0.95f)), // wall drip (darker)
            };
            poolMat = Mats.Decal(Tex(4), new Color(0.36f, 0.01f, 0.025f, 0.98f), 3001);
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
                    float v = Mathf.Clamp01((r - d) / 1.5f) * str;
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
                disc(c, c, S * 0.3f, 0.35f);
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
                byte g = (byte)(al > 0.9f ? 255 : 215);
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
            dp[dn] = p; dv[dn] = v; dvol[dn] = volMl; dage[dn] = 0; dn++;
        }

        public void Spray(Vector3 p, Vector3 dir, int n, float speed, float spread, float volMin = 0.08f, float volMax = 0.6f)
        {
            for (int i = 0; i < n; i++)
            {
                var d = (dir + Random.insideUnitSphere * spread).normalized;
                Emit(p, d * speed * Random.Range(0.3f, 1f), Random.Range(volMin, volMax));
            }
        }

        public void Hole(Vector3 p, Vector3 n) => AddDecal(3, p, n, RandomTangent(n), 0.012f, 0.012f);

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
                if (L > 1e-5f && Physics.Raycast(p, seg / L, out RaycastHit h, L, ~0, QueryTriggerInteraction.Ignore))
                {
                    Land(h, v, dvol[i]); Kill(i); continue;
                }
                if (dage[i] > 6f || np.y < -5f) { Kill(i); continue; }
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
            float size = Mathf.Clamp(r * 2f * (2.1f + sp * 0.25f), 0.007f, 0.09f);   // stain grows with impact speed
            float el = 1f + Mathf.Clamp(vt.magnitude / (vn + 0.6f), 0f, 2.2f);       // oblique impact -> ellipse
            Vector3 up = vt.sqrMagnitude > 1e-4f ? vt.normalized : RandomTangent(n);

            if (h.rigidbody != null)
            {
                // on a body or a severed piece: stain sticks to it
                if (Random.value < 0.75f) BodyDecal(h.collider.transform, h.point, n, size * 0.9f, el > 1.6f ? 2 : 0, up);
                return;
            }
            int variant = sp > 4.5f ? 1 : (el > 1.6f ? 2 : 0);
            AddDecal(variant, h.point, n, up, size, size * el);
            if (n.y > 0.7f) Wet(h.point, vol);
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
            int cap = DEC_BATCHES * B;
            int i = decHead[v]; decHead[v] = (i + 1) % cap; if (decN[v] < cap) decN[v]++;
            if (Mathf.Abs(Vector3.Dot(up, n)) > 0.98f) up = RandomTangent(n);
            var rot = Quaternion.LookRotation(n, up);
            float off = 0.0015f + (zSeq++ % 60) * 0.00003f;
            dec[v][i / B][i % B] = Matrix4x4.TRS(p + n * off, rot, new Vector3(sx, sy, 1));
        }

        public void BodyDecal(Transform t, Vector3 p, Vector3 n, float size, int variant, Vector3 up, float len = -1f)
        {
            var go = new GameObject("blood");
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = decMat[variant]; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            if (Mathf.Abs(Vector3.Dot(up, n)) > 0.98f) up = RandomTangent(n);
            go.transform.SetPositionAndRotation(p + n * 0.004f, Quaternion.LookRotation(n, up));
            go.transform.localScale = new Vector3(size, len > 0 ? len : size * (variant == 2 ? 2.2f : 1f), 1f);
            go.transform.SetParent(t, true);
            bodyDec.Enqueue(go);
            while (bodyDec.Count > MAXBODY) { var o = bodyDec.Dequeue(); if (o != null) Destroy(o); }
        }

        void Wet(Vector3 p, float vol)
        {
            foreach (var P in pools)
                if (Mathf.Abs(P.p.y - p.y) < 0.2f && (new Vector2(P.p.x - p.x, P.p.z - p.z)).magnitude < P.r * 0.95f) { Grow(P, vol); return; }
            var cell = new Vector2Int(Mathf.FloorToInt(p.x / 0.3f), Mathf.FloorToInt(p.z / 0.3f));
            wet.TryGetValue(cell, out float w); w += vol;
            if (w > 5f && pools.Count < MAXPOOLS)
            {
                wet[cell] = 0;
                var go = new GameObject("pool");
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = poolMat; mr.shadowCastingMode = ShadowCastingMode.Off;
                go.transform.SetPositionAndRotation(new Vector3(p.x, p.y + 0.004f + pools.Count * 0.00005f, p.z), Quaternion.LookRotation(Vector3.up, RandomTangent(Vector3.up)));
                var P = new Pool { t = go.transform, p = p };
                pools.Add(P); Grow(P, w);
            }
            else wet[cell] = w;
        }

        static void Grow(Pool P, float ml)
        {
            P.vol += ml;
            // ~2.5 mm thick layer: 100 ml covers a ~11 cm radius disc
            P.r = Mathf.Min(1.5f, Mathf.Sqrt(P.vol * 1e-6f / 0.0025f / Mathf.PI));
            float d = P.r * 2.3f;
            P.t.localScale = new Vector3(d, d, 1);
        }

        // ---------- drawing
        void LateUpdate()
        {
            for (int v = 0; v < decMat.Length; v++)
            {
                int n = decN[v];
                for (int b = 0; b * B < n; b++)
                    Graphics.DrawMeshInstanced(quad, 0, decMat[v], dec[v][b], Mathf.Min(B, n - b * B), null, ShadowCastingMode.Off, false);
            }
            for (int i = 0; i < dn; i++)
            {
                Vector3 v = dv[i]; float sp = v.magnitude;
                float r = Mathf.Pow(dvol[i] * 1e-6f * 0.75f / Mathf.PI, 1f / 3f) * 2.2f;
                var rot = sp > 0.01f ? Quaternion.LookRotation(v / sp) : Quaternion.identity;
                dropBatches[i / B][i % B] = Matrix4x4.TRS(dp[i], rot, new Vector3(r, r, r * (1f + Mathf.Min(sp * 0.08f, 1.5f))));
            }
            for (int b = 0; b * B < dn; b++)
                Graphics.DrawMeshInstanced(sphere, 0, dropMat, dropBatches[b], Mathf.Min(B, dn - b * B), null, ShadowCastingMode.Off, false);
        }
    }
}
