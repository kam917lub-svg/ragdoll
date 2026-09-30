using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VITS
{
    // The mannequin is made like the original: smooth rigid segments (pelvis, chest, head, upper arm,
    // forearm+hand, thigh, shin+foot) that overlap at the joints. Each segment is a signed distance
    // field (rounded cones / capsules / ellipsoids blended with smooth-min) meshed once with "surface nets".
    // The same field is used at runtime to put wounds and running blood exactly on the skin.
    public static class BodyMesh
    {
        public const int NB = 11;
        public const int PEL = 0, CHE = 1, HEA = 2, UAL = 3, FAL = 4, UAR = 5, FAR = 6, THL = 7, SHL = 8, THR = 9, SHR = 10;
        public static readonly Mesh[] Meshes = new Mesh[NB];
        public static readonly Vector3[][] Verts = new Vector3[NB][];
        public static readonly int[][] Tris = new int[NB][];

        static int Kind(int b) { switch (b) { case 3: case 5: return 3; case 4: case 6: return 4; case 7: case 9: return 5; case 8: case 10: return 6; default: return b; } }

        static readonly Vector3[] BMin =
        {
            new Vector3(-0.24f, -0.22f, -0.14f), new Vector3(-0.27f, -0.1f, -0.14f), new Vector3(-0.13f, -0.13f, -0.14f),
            new Vector3(-0.09f, -0.37f, -0.09f), new Vector3(-0.08f, -0.42f, -0.08f), new Vector3(-0.12f, -0.53f, -0.12f), new Vector3(-0.1f, -0.49f, -0.12f),
        };
        static readonly Vector3[] BMax =
        {
            new Vector3(0.24f, 0.17f, 0.14f), new Vector3(0.27f, 0.47f, 0.14f), new Vector3(0.13f, 0.35f, 0.15f),
            new Vector3(0.09f, 0.09f, 0.09f), new Vector3(0.08f, 0.08f, 0.08f), new Vector3(0.12f, 0.14f, 0.12f), new Vector3(0.1f, 0.09f, 0.21f),
        };

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

        // signed distance to the skin of bone b, p in that bone's local space
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

        // move a point onto the skin
        public static Vector3 Project(int b, Vector3 p)
        {
            for (int i = 0; i < 4; i++) { float d = Sdf(b, p); if (Mathf.Abs(d) < 0.0005f) break; p -= Normal(b, p) * d; }
            return p;
        }

        public static void Build()
        {
            var cache = new Dictionary<int, int>();
            for (int b = 0; b < NB; b++)
            {
                int k = Kind(b);
                if (cache.TryGetValue(k, out int src)) { Meshes[b] = Meshes[src]; Verts[b] = Verts[src]; Tris[b] = Tris[src]; continue; }
                Nets(b, BMin[k], BMax[k]);
                cache[k] = b;
            }
        }

        static void Nets(int bone, Vector3 mn, Vector3 mx)
        {
            const float h = 0.012f;
            int nx = Mathf.CeilToInt((mx.x - mn.x) / h) + 1, ny = Mathf.CeilToInt((mx.y - mn.y) / h) + 1, nz = Mathf.CeilToInt((mx.z - mn.z) / h) + 1;
            var f = new float[nx * ny * nz];
            int Id(int i, int j, int k) => (k * ny + j) * nx + i;
            for (int k = 0; k < nz; k++) for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++)
                f[Id(i, j, k)] = Sdf(bone, mn + new Vector3(i, j, k) * h);

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
                    sum += Vector3.Lerp(new Vector3(corner[a, 0], corner[a, 1], corner[a, 2]), new Vector3(corner[b, 0], corner[b, 1], corner[b, 2]), t); cnt++;
                }
                cellV[Id(i, j, k)] = verts.Count;
                // snap exactly onto the surface: smooth, no staircase
                verts.Add(Project(bone, mn + (new Vector3(i, j, k) + sum / cnt) * h));
            }

            var tris = new List<int>();
            void Tri(int a, int b, int c)
            {
                Vector3 A = verts[a], B = verts[b], C = verts[c];
                if (Vector3.Dot(Vector3.Cross(B - A, C - A), Normal(bone, (A + B + C) / 3f)) < 0) { tris.Add(a); tris.Add(c); tris.Add(b); }
                else { tris.Add(a); tris.Add(b); tris.Add(c); }
            }
            void Quad(int a, int b, int c, int d)
            {
                if (a < 0 || b < 0 || c < 0 || d < 0) return;
                // split along the shorter diagonal
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

            var normals = new Vector3[verts.Count];
            for (int v = 0; v < verts.Count; v++) normals[v] = Normal(bone, verts[v]);
            var m = new Mesh { name = "seg" + bone, indexFormat = IndexFormat.UInt32 };
            m.SetVertices(verts); m.normals = normals; m.SetTriangles(tris, 0); m.RecalculateBounds();
            Meshes[bone] = m; Verts[bone] = verts.ToArray(); Tris[bone] = tris.ToArray();
        }
    }
}
