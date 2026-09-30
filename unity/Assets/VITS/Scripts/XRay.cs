using System.Collections.Generic;
using UnityEngine;

namespace VITS
{
    // X-ray view (T): the skin becomes a transparent ghost and the inside shows:
    // bones (white), arteries (red), veins (blue), nerves (yellow), organs.
    public static class XRay
    {
        public static bool On;
        static Material ghost, bone, art, vein, nerve, lung, heart, liver, gut, brain, kidney, stomach, bladder;
        static readonly List<GameObject> roots = new List<GameObject>();

        public static Material Ghost
        {
            get
            {
                if (ghost == null)
                {
                    var sh = Shader.Find("VITS/XRay");
                    ghost = sh != null ? new Material(sh) : Mats.Decal(Texture2D.whiteTexture, new Color(0.55f, 0.8f, 1f, 0.15f));
                }
                return ghost;
            }
        }

        static void Init()
        {
            if (bone != null) return;
            bone = Mats.Lit(new Color(0.95f, 0.93f, 0.86f), 0.4f);
            art = Mats.Lit(new Color(0.85f, 0.08f, 0.1f), 0.6f);
            vein = Mats.Lit(new Color(0.2f, 0.32f, 0.75f), 0.6f);
            nerve = Mats.Lit(new Color(0.9f, 0.75f, 0.2f), 0.5f);
            lung = Mats.Lit(new Color(0.9f, 0.55f, 0.6f), 0.4f);
            heart = Mats.Lit(new Color(0.65f, 0.05f, 0.08f), 0.6f);
            liver = Mats.Lit(new Color(0.45f, 0.12f, 0.1f), 0.5f);
            gut = Mats.Lit(new Color(0.85f, 0.6f, 0.55f), 0.5f);
            brain = Mats.Lit(new Color(0.92f, 0.7f, 0.72f), 0.4f);
            kidney = Mats.Lit(new Color(0.5f, 0.1f, 0.12f), 0.5f);
            stomach = Mats.Lit(new Color(0.88f, 0.68f, 0.6f), 0.5f);
            bladder = Mats.Lit(new Color(0.9f, 0.85f, 0.5f), 0.5f);
        }

        public static void Toggle()
        {
            On = !On;
            roots.RemoveAll(g => g == null);
            foreach (var g in roots) g.SetActive(On);
            Skin.All.RemoveAll(s => s == null || s.r == null);
            foreach (var s in Skin.All) s.r.sharedMaterials = On ? new[] { Ghost } : s.mats;
        }

        static Transform root;
        static bool mirror;
        static Vector3 M(Vector3 v) => mirror ? new Vector3(-v.x, v.y, v.z) : v;
        static void Tube(Vector3 a, Vector3 b, float r, Material m)
        {
            a = M(a); b = M(b);
            var g = Mats.Vis(PrimitiveType.Cylinder, root, (a + b) * 0.5f, new Vector3(r * 2f, (b - a).magnitude * 0.5f, r * 2f), m, false);
            g.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
        }
        static void Blob(Vector3 c, Vector3 rad, Material m) => Mats.Vis(PrimitiveType.Sphere, root, M(c), rad * 2f, m, false);
        static void Path(Material m, float r, params Vector3[] pts) { for (int i = 0; i + 1 < pts.Length; i++) Tube(pts[i], pts[i + 1], r, m); }
        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        public static void AddInternals(Part p)
        {
            Init();
            var g = new GameObject("internals");
            g.transform.SetParent(p.transform, false);
            root = g.transform;
            mirror = p.key.EndsWith("R");
            switch (p.idx)
            {
                case BodyMesh.PEL:
                    Blob(V(-0.075f, 0.0f, -0.015f), V(0.085f, 0.075f, 0.04f), bone); Blob(V(0.075f, 0.0f, -0.015f), V(0.085f, 0.075f, 0.04f), bone);
                    Blob(V(0, -0.02f, -0.06f), V(0.035f, 0.06f, 0.025f), bone);
                    Blob(V(0, 0.04f, 0.04f), V(0.1f, 0.06f, 0.05f), gut); Blob(V(0, -0.06f, 0.05f), V(0.035f, 0.03f, 0.03f), bladder);
                    Path(art, 0.009f, V(-0.02f, 0.1f, 0.0f), V(-0.07f, 0.05f, 0.03f), V(-0.09f, -0.08f, 0.05f));
                    Path(art, 0.009f, V(0.02f, 0.1f, 0.0f), V(0.07f, 0.05f, 0.03f), V(0.09f, -0.08f, 0.05f));
                    Path(vein, 0.009f, V(-0.03f, 0.1f, -0.015f), V(-0.08f, 0.05f, 0.02f), V(-0.1f, -0.08f, 0.04f));
                    Path(vein, 0.009f, V(0.03f, 0.1f, -0.015f), V(0.08f, 0.05f, 0.02f), V(0.1f, -0.08f, 0.04f));
                    Tube(V(0, 0.12f, -0.07f), V(0, -0.05f, -0.07f), 0.012f, nerve);
                    break;
                case BodyMesh.CHE:
                    for (float y = 0.0f; y < 0.45f; y += 0.04f) Blob(V(0, y, -0.075f), V(0.018f, 0.014f, 0.018f), bone);
                    Tube(V(0, -0.02f, -0.06f), V(0, 0.45f, -0.06f), 0.007f, nerve);
                    for (float y = 0.13f; y <= 0.33f; y += 0.04f)
                        foreach (float sx in new[] { -1f, 1f })
                            Path(bone, 0.006f, V(0.02f * sx, y, -0.075f), V(0.12f * sx, y - 0.01f, -0.035f), V(0.135f * sx, y - 0.03f, 0.04f), V(0.04f * sx, y - 0.05f, 0.095f));
                    Tube(V(0, 0.14f, 0.1f), V(0, 0.33f, 0.1f), 0.012f, bone);
                    Path(bone, 0.008f, V(-0.02f, 0.36f, 0.08f), V(-0.17f, 0.37f, 0.02f)); Path(bone, 0.008f, V(0.02f, 0.36f, 0.08f), V(0.17f, 0.37f, 0.02f));
                    Blob(V(-0.07f, 0.24f, 0.0f), V(0.062f, 0.1f, 0.065f), lung); Blob(V(0.07f, 0.24f, 0.0f), V(0.062f, 0.1f, 0.065f), lung);
                    Blob(V(-0.025f, 0.2f, 0.045f), V(0.045f, 0.055f, 0.042f), heart);
                    Path(art, 0.012f, V(-0.015f, 0.22f, 0.03f), V(0f, 0.33f, 0.02f), V(0.03f, 0.3f, -0.03f), V(0.02f, 0.02f, -0.045f));
                    Path(art, 0.007f, V(-0.1f, 0.33f, 0.03f), V(-0.2f, 0.33f, 0.02f)); Path(art, 0.007f, V(0.1f, 0.33f, 0.03f), V(0.2f, 0.33f, 0.02f));
                    Path(vein, 0.012f, V(0.01f, 0.02f, -0.035f), V(0.015f, 0.2f, 0.02f), V(0.0f, 0.34f, 0.0f));
                    Blob(V(0.055f, 0.08f, 0.03f), V(0.08f, 0.045f, 0.06f), liver); Blob(V(-0.05f, 0.08f, 0.045f), V(0.05f, 0.04f, 0.04f), stomach);
                    Blob(V(-0.06f, 0.03f, -0.05f), V(0.025f, 0.04f, 0.02f), kidney); Blob(V(0.06f, 0.03f, -0.05f), V(0.025f, 0.04f, 0.02f), kidney);
                    Blob(V(0, 0.0f, 0.04f), V(0.09f, 0.035f, 0.05f), gut);
                    break;
                case BodyMesh.HEA:
                    Blob(V(0, 0.2f, 0.0f), V(0.085f, 0.09f, 0.095f), brain);
                    Blob(V(0, 0.09f, 0.06f), V(0.06f, 0.025f, 0.045f), bone);
                    for (float y = -0.05f; y < 0.09f; y += 0.03f) Blob(V(0, y, -0.02f), V(0.016f, 0.012f, 0.016f), bone);
                    Tube(V(0, -0.06f, -0.01f), V(0, 0.12f, -0.01f), 0.006f, nerve);
                    Path(art, 0.008f, V(-0.03f, -0.07f, 0.02f), V(-0.03f, 0.08f, 0.02f), V(-0.02f, 0.14f, 0.0f));
                    Path(art, 0.008f, V(0.03f, -0.07f, 0.02f), V(0.03f, 0.08f, 0.02f), V(0.02f, 0.14f, 0.0f));
                    Path(vein, 0.009f, V(-0.045f, -0.07f, 0.005f), V(-0.045f, 0.09f, 0.0f)); Path(vein, 0.009f, V(0.045f, -0.07f, 0.005f), V(0.045f, 0.09f, 0.0f));
                    break;
                case BodyMesh.UAL: case BodyMesh.UAR:
                    Blob(V(0, -0.01f, 0), V(0.025f, 0.025f, 0.025f), bone); Tube(V(0, -0.01f, 0), V(0, -0.28f, 0), 0.012f, bone);
                    Tube(V(0.03f, -0.03f, 0.01f), V(0.025f, -0.29f, 0.015f), 0.006f, art);
                    Tube(V(0.038f, -0.03f, -0.005f), V(0.033f, -0.29f, 0.0f), 0.006f, vein);
                    Tube(V(0.022f, -0.03f, -0.015f), V(0.02f, -0.29f, -0.01f), 0.004f, nerve);
                    break;
                case BodyMesh.FAL: case BodyMesh.FAR:
                    Tube(V(-0.012f, -0.01f, 0), V(-0.012f, -0.27f, 0), 0.007f, bone); Tube(V(0.012f, -0.01f, 0), V(0.012f, -0.27f, 0), 0.007f, bone);
                    for (int i = 0; i < 4; i++) Tube(V(-0.018f + i * 0.012f, -0.29f, 0.005f), V(-0.018f + i * 0.012f, -0.36f, 0.01f), 0.004f, bone);
                    Tube(V(0f, -0.02f, 0.03f), V(0f, -0.28f, 0.03f), 0.005f, art);
                    Tube(V(0.02f, -0.02f, 0.025f), V(0.02f, -0.28f, 0.025f), 0.005f, vein);
                    break;
                case BodyMesh.THL: case BodyMesh.THR:
                    Blob(V(0.02f, 0.0f, 0), V(0.028f, 0.028f, 0.028f), bone); Tube(V(0, 0.0f, 0), V(0, -0.43f, 0), 0.016f, bone);
                    Path(art, 0.008f, V(0.028f, -0.02f, 0.035f), V(0.015f, -0.38f, 0f), V(0f, -0.44f, -0.03f));
                    Path(vein, 0.008f, V(0.04f, -0.02f, 0.03f), V(0.027f, -0.38f, -0.005f), V(0.01f, -0.44f, -0.035f));
                    Tube(V(0f, -0.02f, -0.05f), V(0f, -0.43f, -0.03f), 0.006f, nerve);
                    break;
                case BodyMesh.SHL: case BodyMesh.SHR:
                    Tube(V(0, 0.0f, 0.012f), V(0, -0.39f, 0.012f), 0.013f, bone); Tube(V(0.03f, -0.02f, -0.005f), V(0.03f, -0.38f, -0.005f), 0.007f, bone);
                    Blob(V(0, -0.41f, 0.04f), V(0.035f, 0.025f, 0.09f), bone);
                    Tube(V(0f, -0.05f, -0.035f), V(0f, -0.38f, -0.02f), 0.005f, art);
                    Tube(V(0.012f, -0.05f, -0.04f), V(0.012f, -0.38f, -0.025f), 0.005f, vein);
                    break;
            }
            g.SetActive(On);
            roots.Add(g);
        }
    }
}
