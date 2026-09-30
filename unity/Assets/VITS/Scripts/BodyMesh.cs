using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VITS
{
    // One smooth skinned body, generated once: a signed distance field made of capsules/ellipsoids
    // blended with smooth-min, turned into triangles with "surface nets", skinned to the 11 ragdoll bones.
    public static class BodyMesh
    {
        public const int NB = 11;
        public const int PEL = 0, CHE = 1, HEA = 2, UAL = 3, FAL = 4, UAR = 5, FAR = 6, THL = 7, SHL = 8, THR = 9, SHR = 10;
        public static readonly int[] Parent = { -1, 0, 1, 1, 3, 1, 5, 0, 7, 0, 9 };
        // rest pivots in body space (must match Mannequin.DEFS)
        public static readonly Vector3[] Pivot =
        {
            new Vector3(0, 0.95f, 0), new Vector3(0, 1.05f, 0), new Vector3(0, 1.51f, 0),
            new Vector3(-0.245f, 1.44f, 0), new Vector3(-0.245f, 1.14f, 0),
            new Vector3(0.245f, 1.44f, 0), new Vector3(0.245f, 1.14f, 0),
            new Vector3(-0.1f, 0.9f, 0), new Vector3(-0.1f, 0.45f, 0),
            new Vector3(0.1f, 0.9f, 0), new Vector3(0.1f, 0.45f, 0),
        };

        public static Mesh Mesh;
        public static int[] Dom;          // dominant bone of each vertex
        public static int[] TrisPerBone;  // triangle count per bone (for "how much flesh is left")

        static readonly float[] bd = new float[NB];

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
        static float Smin(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }
        static void Bd(int i, float d) { if (d < bd[i]) bd[i] = d; }

        static float Arm(Vector3 p, float s, int u, int f)
        {
            float x = 0.245f * s;
            float ua = Cap(p, new Vector3(x, 1.43f, 0), new Vector3(x, 1.16f, 0), 0.056f);
            float fa = Cap(p, new Vector3(x, 1.13f, 0), new Vector3(x, 0.9f, 0), 0.047f);
            float ha = Ell(p, new Vector3(x, 0.83f, 0.005f), new Vector3(0.032f, 0.065f, 0.045f));
            Bd(u, ua); Bd(f, Mathf.Min(fa, ha));
            return Smin(Smin(ua, fa, 0.025f), ha, 0.02f);
        }
        static float Leg(Vector3 p, float s, int th, int sh)
        {
            float x = 0.1f * s;
            float t = Cap(p, new Vector3(x, 0.9f, 0), new Vector3(x, 0.47f, 0), 0.082f);
            float n = Cap(p, new Vector3(x, 0.45f, 0), new Vector3(x, 0.09f, 0), 0.06f);
            float f = Cap(p, new Vector3(x, 0.045f, -0.03f), new Vector3(x, 0.045f, 0.14f), 0.045f);
            Bd(th, t); Bd(sh, Mathf.Min(n, f));
            return Smin(Smin(t, n, 0.03f), f, 0.02f);
        }

        public static float Eval(Vector3 p)
        {
            for (int i = 0; i < NB; i++) bd[i] = 1e9f;
            float pel = Cap(p, new Vector3(-0.075f, 0.95f, 0), new Vector3(0.075f, 0.95f, 0), 0.125f, 0.8f); Bd(PEL, pel);
            float c1 = Cap(p, new Vector3(-0.06f, 1.12f, 0), new Vector3(0.06f, 1.12f, 0), 0.125f, 0.75f);
            float c2 = Cap(p, new Vector3(0, 1.12f, 0), new Vector3(0, 1.34f, 0), 0.135f, 0.75f);
            float c3 = Cap(p, new Vector3(-0.14f, 1.36f, 0), new Vector3(0.14f, 1.36f, 0), 0.11f, 0.8f);
            float shL = Ell(p, new Vector3(-0.215f, 1.39f, 0), new Vector3(0.075f, 0.07f, 0.07f));
            float shR = Ell(p, new Vector3(0.215f, 1.39f, 0), new Vector3(0.075f, 0.07f, 0.07f));
            Bd(CHE, Mathf.Min(Mathf.Min(c1, c2), Mathf.Min(c3, Mathf.Min(shL, shR))));
            float nk = Cap(p, new Vector3(0, 1.46f, 0), new Vector3(0, 1.6f, 0), 0.052f);
            float hd = Ell(p, new Vector3(0, 1.68f, 0.005f), new Vector3(0.1f, 0.132f, 0.115f));
            Bd(HEA, Mathf.Min(nk, hd));
            float torso = Smin(Smin(pel, Smin(c1, c2, 0.05f), 0.05f), c3, 0.05f);
            torso = Smin(torso, Mathf.Min(shL, shR), 0.03f);
            torso = Smin(Smin(torso, nk, 0.03f), hd, 0.025f);
            float aL = Arm(p, -1, UAL, FAL), aR = Arm(p, 1, UAR, FAR);
            float lL = Leg(p, -1, THL, SHL), lR = Leg(p, 1, THR, SHR);
            float d = Mathf.Min(Smin(torso, lL, 0.03f), Smin(torso, lR, 0.03f));
            d = Mathf.Min(d, Smin(aL, shL, 0.03f));
            d = Mathf.Min(d, Smin(aR, shR, 0.03f));
            return d;
        }

        public static void Build()
        {
            const float h = 0.02f;
            var o = new Vector3(-0.37f, -0.03f, -0.21f);
            int nx = 38, ny = 95, nz = 23;
            var f = new float[nx * ny * nz];
            int Id(int i, int j, int k) => (k * ny + j) * nx + i;
            for (int k = 0; k < nz; k++) for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++)
                f[Id(i, j, k)] = Eval(o + new Vector3(i, j, k) * h);

            var verts = new List<Vector3>();
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
                    var pa = new Vector3(corner[a, 0], corner[a, 1], corner[a, 2]);
                    var pb = new Vector3(corner[b, 0], corner[b, 1], corner[b, 2]);
                    sum += Vector3.Lerp(pa, pb, t); cnt++;
                }
                cellV[Id(i, j, k)] = verts.Count;
                verts.Add(o + (new Vector3(i, j, k) + sum / cnt) * h);
            }

            var tris = new List<int>();
            void Quad(int a, int b, int c, int d)
            {
                if (a < 0 || b < 0 || c < 0 || d < 0) return;
                Tri(a, b, c); Tri(a, c, d);
            }
            void Tri(int a, int b, int c)
            {
                Vector3 A = verts[a], B = verts[b], C = verts[c];
                Vector3 cen = (A + B + C) / 3f;
                Vector3 g = Grad(cen);
                if (Vector3.Dot(Vector3.Cross(B - A, C - A), g) < 0) { tris.Add(a); tris.Add(c); tris.Add(b); }
                else { tris.Add(a); tris.Add(b); tris.Add(c); }
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

            // normals, skin weights
            int nv = verts.Count;
            var normals = new Vector3[nv];
            var bw = new BoneWeight[nv];
            Dom = new int[nv];
            for (int v = 0; v < nv; v++)
            {
                normals[v] = Grad(verts[v]).normalized;
                Eval(verts[v]);
                int b0 = 0; for (int b = 1; b < NB; b++) if (bd[b] < bd[b0]) b0 = b;
                int b1 = -1;
                for (int b = 0; b < NB; b++)
                {
                    if (b == b0 || (Parent[b] != b0 && Parent[b0] != b)) continue;
                    if (b1 < 0 || bd[b] < bd[b1]) b1 = b;
                }
                Dom[v] = b0;
                float w1 = b1 < 0 ? 0 : Mathf.Exp(-(bd[b1] - bd[b0]) / 0.015f);
                float s = 1f + w1;
                bw[v] = new BoneWeight { boneIndex0 = b0, weight0 = 1f / s, boneIndex1 = Mathf.Max(0, b1), weight1 = w1 / s };
            }
            TrisPerBone = new int[NB];
            for (int t = 0; t < tris.Count; t += 3) TrisPerBone[Dom[tris[t]]]++;

            var bind = new Matrix4x4[NB];
            for (int b = 0; b < NB; b++) bind[b] = Matrix4x4.Translate(-Pivot[b]);

            Mesh = new Mesh { name = "MannequinBody", indexFormat = IndexFormat.UInt32 };
            Mesh.SetVertices(verts);
            Mesh.normals = normals;
            Mesh.SetTriangles(tris, 0);
            Mesh.boneWeights = bw;
            Mesh.bindposes = bind;
            Mesh.RecalculateBounds();
        }

        static Vector3 Grad(Vector3 p)
        {
            const float e = 0.004f;
            return new Vector3(Eval(p + new Vector3(e, 0, 0)) - Eval(p - new Vector3(e, 0, 0)),
                               Eval(p + new Vector3(0, e, 0)) - Eval(p - new Vector3(0, e, 0)),
                               Eval(p + new Vector3(0, 0, e)) - Eval(p - new Vector3(0, 0, e)));
        }
    }
}
