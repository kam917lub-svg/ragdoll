using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VITS
{
    // The mannequin body: one solid, smooth skinned mesh. Each segment (pelvis, chest, head, upper arm,
    // forearm+hand, thigh, shin+foot) is a signed distance field; the segments are blended with smooth-min
    // into one body, meshed once with "surface nets" and skinned to the 11 ragdoll bones.
    // The per-segment fields are also used at runtime to put wounds and running blood exactly on the skin
    // and to decide what a bullet really hits.
    public static class BodyMesh
    {
        public const int NB = 11;
        public const int PEL = 0, CHE = 1, HEA = 2, UAL = 3, FAL = 4, UAR = 5, FAR = 6, THL = 7, SHL = 8, THR = 9, SHR = 10;

        static int Kind(int b) { switch (b) { case 3: case 5: return 3; case 4: case 6: return 4; case 7: case 9: return 5; case 8: case 10: return 6; default: return b; } }

        static float Cap(Vector3 p, Vector3 a, Vector3 b, float r, float sq = 1f)
        {
            p.z /= sq; a.z /= sq; b.z /= sq;
            Vector3 pa = p - a, ba = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(pa, ba) / Mathf.Max(1e-6f, ba.sqrMagnitude));
            return ((pa - ba * t).magnitude - r) * sq;
        }
        static float Ell(Vector3 p, Vector3 c, Vector3 r)
        {
            var q = new Vector3((p.x - c.x) / r.x, (p.y - c.y) / r.y, (p.z - c.z) / r.z);
            return (q.magnitude - 1f) * Mathf.Min(r.x, Mathf.Min(r.y, r.z));
        }
        static float Sg(float x) => x > 0 ? 1f : (x < 0 ? -1f : 0f);
        // rounded cone between a (radius r1) and b (radius r2) - Inigo Quilez
        static float RCone(Vector3 p, Vector3 a, Vector3 b, float r1, float r2)
        {
            Vector3 ba = b - a; float l2 = Vector3.Dot(ba, ba), rr = r1 - r2, a2 = l2 - rr * rr, il2 = 1f / l2;
            Vector3 pa = p - a; float y = Vector3.Dot(pa, ba), z = y - l2;
            Vector3 xv = pa * l2 - ba * y; float x2 = Vector3.Dot(xv, xv), y2 = y * y * l2, z2 = z * z * l2;
            float k = Sg(rr) * rr * rr * x2;
            if (Sg(z) * a2 * z2 > k) return Mathf.Sqrt(x2 + z2) * il2 - r2;
            if (Sg(y) * a2 * y2 < k) return Mathf.Sqrt(x2 + y2) * il2 - r1;
            return (Mathf.Sqrt(x2 * a2 * il2) + y * rr) * il2 - r1;
        }
        static float Smin(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        // signed distance to the skin of segment b, p in that bone's local space
        public static float Sdf(int b, Vector3 p)
        {
            switch (Kind(b))
            {
                case 0:
                    return Smin(Cap(p, new Vector3(-0.07f, 0, 0), new Vector3(0.07f, 0, 0), 0.13f, 0.78f),
                                Cap(p, new Vector3(-0.06f, -0.07f, 0), new Vector3(0.06f, -0.07f, 0), 0.105f, 0.8f), 0.04f);
                case 1:
                {
                    float c1 = Cap(p, new Vector3(-0.035f, 0.05f, 0), new Vector3(0.035f, 0.05f, 0), 0.115f, 0.74f);
                    float c2 = RCone(new Vector3(p.x, p.y, p.z / 0.72f), new Vector3(0, 0.08f, 0), new Vector3(0, 0.27f, 0), 0.125f, 0.15f) * 0.72f;
                    float c3 = Cap(p, new Vector3(-0.145f, 0.315f, 0), new Vector3(0.145f, 0.315f, 0), 0.095f, 0.8f);
                    float nb = Cap(p, new Vector3(0, 0.36f, 0), new Vector3(0, 0.44f, 0), 0.058f);
                    return Smin(Smin(Smin(c1, c2, 0.06f), c3, 0.06f), nb, 0.03f);
                }
                case 2:
                    return Smin(Cap(p, new Vector3(0, -0.05f, 0), new Vector3(0, 0.07f, 0), 0.055f),
                                Ell(p, new Vector3(0, 0.175f, 0.005f), new Vector3(0.105f, 0.14f, 0.118f)), 0.03f);
                case 3:
                    return RCone(p, new Vector3(0, -0.005f, 0), new Vector3(0, -0.29f, 0), 0.066f, 0.055f);
                case 4:
                    return Smin(RCone(p, Vector3.zero, new Vector3(0, -0.27f, 0), 0.054f, 0.043f),
                                Ell(p, new Vector3(0, -0.32f, 0.005f), new Vector3(0.037f, 0.065f, 0.048f)), 0.025f);
                case 5:
                    return RCone(p, new Vector3(0, 0.02f, 0), new Vector3(0, -0.43f, 0), 0.096f, 0.074f);
                default:
                    return Smin(RCone(p, Vector3.zero, new Vector3(0, -0.39f, 0), 0.072f, 0.054f),
                                Cap(p, new Vector3(0, -0.4f, -0.04f), new Vector3(0, -0.4f, 0.13f), 0.05f), 0.03f);
            }
        }

        public static Vector3 Normal(int b, Vector3 p)
        {
            const float e = 0.002f;
            var g = new Vector3(Sdf(b, p + new Vector3(e, 0, 0)) - Sdf(b, p - new Vector3(e, 0, 0)),
                                Sdf(b, p + new Vector3(0, e, 0)) - Sdf(b, p - new Vector3(0, e, 0)),
                                Sdf(b, p + new Vector3(0, 0, e)) - Sdf(b, p - new Vector3(0, 0, e)));
            return g.sqrMagnitude > 1e-12f ? g.normalized : Vector3.up;
        }

        // move a point onto the skin of one segment
        public static Vector3 Project(int b, Vector3 p)
        {
            for (int i = 0; i < 4; i++) { float d = Sdf(b, p); if (Mathf.Abs(d) < 0.0005f) break; p -= Normal(b, p) * d; }
            return p;
        }

        // ---------- one smooth skinned body ----------
        public static readonly int[] Parent = { -1, 0, 1, 1, 3, 1, 5, 0, 7, 0, 9 };
        public static readonly Vector3[] LocalPos =
        {
            new Vector3(0, 0.95f, 0), new Vector3(0, 0.10f, 0), new Vector3(0, 0.46f, 0),
            new Vector3(-0.235f, 0.39f, 0), new Vector3(0, -0.3f, 0), new Vector3(0.235f, 0.39f, 0), new Vector3(0, -0.3f, 0),
            new Vector3(-0.1f, -0.05f, 0), new Vector3(0, -0.45f, 0), new Vector3(0.1f, -0.05f, 0), new Vector3(0, -0.45f, 0),
        };
        public const float APose = 14f; // arms slightly out while building, so hands never melt into the hips
        public static readonly Matrix4x4[] Rest = new Matrix4x4[NB], Bind = new Matrix4x4[NB];
        public static Mesh Body;
        public static int[] Dom;
        public static BoneWeight[] Weights;
        public static Vector3[] BodyVerts;
        public static int[] BodyTris;
        static readonly float[] sd = new float[NB];

        static void InitRest()
        {
            for (int b = 0; b < NB; b++)
            {
                var rot = b == UAL ? Quaternion.Euler(0, 0, -APose) : b == UAR ? Quaternion.Euler(0, 0, APose) : Quaternion.identity;
                var local = Matrix4x4.TRS(LocalPos[b], rot, Vector3.one);
                Rest[b] = Parent[b] < 0 ? local : Rest[Parent[b]] * local;
                Bind[b] = Rest[b].inverse;
            }
        }

        // whole body in body space; fills sd[] with each segment's distance
        public static float BodySdf(Vector3 p)
        {
            for (int b = 0; b < NB; b++) sd[b] = Sdf(b, Bind[b].MultiplyPoint3x4(p));
            float torso = Smin(Smin(sd[PEL], sd[CHE], 0.035f), sd[HEA], 0.02f);
            float legL = Smin(sd[THL], sd[SHL], 0.02f), legR = Smin(sd[THR], sd[SHR], 0.02f);
            float armL = Smin(sd[UAL], sd[FAL], 0.02f), armR = Smin(sd[UAR], sd[FAR], 0.02f);
            float d = Mathf.Min(Smin(torso, legL, 0.025f), Smin(torso, legR, 0.025f));
            d = Mathf.Min(d, Smin(torso, armL, 0.02f));
            return Mathf.Min(d, Smin(torso, armR, 0.02f));
        }
        static Vector3 BodyNormal(Vector3 p)
        {
            const float e = 0.002f;
            var g = new Vector3(BodySdf(p + new Vector3(e, 0, 0)) - BodySdf(p - new Vector3(e, 0, 0)),
                                BodySdf(p + new Vector3(0, e, 0)) - BodySdf(p - new Vector3(0, e, 0)),
                                BodySdf(p + new Vector3(0, 0, e)) - BodySdf(p - new Vector3(0, 0, e)));
            return g.sqrMagnitude > 1e-12f ? g.normalized : Vector3.up;
        }
        static Vector3 BodyProject(Vector3 p)
        {
            for (int i = 0; i < 4; i++) { float d = BodySdf(p); if (Mathf.Abs(d) < 0.0005f) break; p -= BodyNormal(p) * d; }
            return p;
        }

        public static void Build()
        {
            InitRest();
            var verts = new List<Vector3>(); var tris = new List<int>();
            Nets(BodySdf, BodyNormal, BodyProject, new Vector3(-0.47f, -0.03f, -0.16f), new Vector3(0.47f, 1.86f, 0.23f), verts, tris);
            int nv = verts.Count;
            var normals = new Vector3[nv];
            Weights = new BoneWeight[nv]; Dom = new int[nv];
            for (int v = 0; v < nv; v++)
            {
                normals[v] = BodyNormal(verts[v]);
                BodySdf(verts[v]);
                int b0 = 0; for (int b = 1; b < NB; b++) if (sd[b] < sd[b0]) b0 = b;
                int b1 = -1;
                for (int b = 0; b < NB; b++)
                {
                    if (b == b0 || (Parent[b] != b0 && Parent[b0] != b)) continue;
                    if (b1 < 0 || sd[b] < sd[b1]) b1 = b;
                }
                Dom[v] = b0;
                float w1 = b1 < 0 ? 0f : Mathf.Min(1f, Mathf.Exp(-(sd[b1] - sd[b0]) / 0.012f));
                float s = 1f + w1;
                Weights[v] = new BoneWeight { boneIndex0 = b0, weight0 = 1f / s, boneIndex1 = Mathf.Max(0, b1), weight1 = w1 / s };
            }
            BodyVerts = verts.ToArray(); BodyTris = tris.ToArray();
            Body = new Mesh { name = "MannequinBody", indexFormat = IndexFormat.UInt32 };
            Body.SetVertices(verts); Body.normals = normals; Body.SetTriangles(tris, 0);
            Body.boneWeights = Weights; Body.bindposes = Bind; Body.RecalculateBounds();
        }

        static void Nets(System.Func<Vector3, float> F, System.Func<Vector3, Vector3> N, System.Func<Vector3, Vector3> P,
                         Vector3 mn, Vector3 mx, List<Vector3> verts, List<int> tris)
        {
            const float h = 0.012f;
            int nx = Mathf.CeilToInt((mx.x - mn.x) / h) + 1, ny = Mathf.CeilToInt((mx.y - mn.y) / h) + 1, nz = Mathf.CeilToInt((mx.z - mn.z) / h) + 1;
            var f = new float[nx * ny * nz];
            int Id(int i, int j, int k) => (k * ny + j) * nx + i;
            for (int k = 0; k < nz; k++) for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++)
                f[Id(i, j, k)] = F(mn + new Vector3(i, j, k) * h);

            var cellV = new int[nx * ny * nz];
            for (int n = 0; n < cellV.Length; n++) cellV[n] = -1;
            int[,] corner = { { 0, 0, 0 }, { 1, 0, 0 }, { 0, 1, 0 }, { 1, 1, 0 }, { 0, 0, 1 }, { 1, 0, 1 }, { 0, 1, 1 }, { 1, 1, 1 } };
            int[,] edges = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
            var cv = new float[8];
            for (int k = 0; k < nz - 1; k++) for (int j = 0; j < ny - 1; j++) for (int i = 0; i < nx - 1; i++)
            {
                int inside = 0;
                for (int c = 0; c < 8; c++) { cv[c] = f[Id(i + corner[c, 0], j + corner[c, 1], k + corner[c, 2])]; if (cv[c] < 0) inside++; }
                if (inside == 0 || inside == 8) continue;
                Vector3 sum = Vector3.zero; int cnt = 0;
                for (int e = 0; e < 12; e++)
                {
                    int a = edges[e, 0], b = edges[e, 1];
                    if ((cv[a] < 0) == (cv[b] < 0)) continue;
                    float t = cv[a] / (cv[a] - cv[b]);
                    sum += Vector3.Lerp(new Vector3(corner[a, 0], corner[a, 1], corner[a, 2]), new Vector3(corner[b, 0], corner[b, 1], corner[b, 2]), t); cnt++;
                }
                cellV[Id(i, j, k)] = verts.Count;
                verts.Add(P(mn + (new Vector3(i, j, k) + sum / cnt) * h));
            }

            void Tri(int a, int b, int c)
            {
                Vector3 A = verts[a], B = verts[b], C = verts[c];
                if (Vector3.Dot(Vector3.Cross(B - A, C - A), N((A + B + C) / 3f)) < 0) { tris.Add(a); tris.Add(c); tris.Add(b); }
                else { tris.Add(a); tris.Add(b); tris.Add(c); }
            }
            void Quad(int a, int b, int c, int d)
            {
                if (a < 0 || b < 0 || c < 0 || d < 0) return;
                if ((verts[a] - verts[c]).sqrMagnitude < (verts[b] - verts[d]).sqrMagnitude) { Tri(a, b, c); Tri(a, c, d); }
                else { Tri(a, b, d); Tri(b, c, d); }
            }
            for (int k = 1; k < nz - 1; k++) for (int j = 1; j < ny - 1; j++) for (int i = 0; i < nx - 1; i++)
                if ((f[Id(i, j, k)] < 0) != (f[Id(i + 1, j, k)] < 0))
                    Quad(cellV[Id(i, j - 1, k - 1)], cellV[Id(i, j, k - 1)], cellV[Id(i, j, k)], cellV[Id(i, j - 1, k)]);
            for (int k = 1; k < nz - 1; k++) for (int j = 0; j < ny - 1; j++) for (int i = 1; i < nx - 1; i++)
                if ((f[Id(i, j, k)] < 0) != (f[Id(i, j + 1, k)] < 0))
                    Quad(cellV[Id(i - 1, j, k - 1)], cellV[Id(i, j, k - 1)], cellV[Id(i, j, k)], cellV[Id(i - 1, j, k)]);
            for (int k = 0; k < nz - 1; k++) for (int j = 1; j < ny - 1; j++) for (int i = 1; i < nx - 1; i++)
                if ((f[Id(i, j, k)] < 0) != (f[Id(i, j, k + 1)] < 0))
                    Quad(cellV[Id(i - 1, j - 1, k)], cellV[Id(i, j - 1, k)], cellV[Id(i, j, k)], cellV[Id(i - 1, j, k)]);
        }
    }
}
