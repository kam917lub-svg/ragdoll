using UnityEngine;
using UnityEngine.Rendering;

namespace VITS
{
    // "Vertical Impact Testsite": open light-blue test area, white grid floor,
    // pale platforms with orange edges, stairs, low cover walls.
    public static class Level
    {
        public static readonly Color Sky = new Color(0.66f, 0.73f, 0.82f);
        public const float HX = 26f, HZ = 26f;
        public struct CoverWall { public Vector3 pos, normal, along; }
        public static readonly System.Collections.Generic.List<CoverWall> Covers = new System.Collections.Generic.List<CoverWall>();
        static Material plat, orange, wall;

        public static void Build()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.58f, 0.62f, 0.68f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Sky; RenderSettings.fogStartDistance = 45f; RenderSettings.fogEndDistance = 160f;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.15f; sun.color = new Color(1f, 0.98f, 0.95f);
            sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.75f;
            sun.transform.rotation = Quaternion.Euler(52f, -35f, 0);

            plat = Mats.Lit(new Color(0.86f, 0.89f, 0.93f), 0.1f);
            orange = Mats.Lit(new Color(1f, 0.45f, 0.18f), 0.2f);
            wall = Mats.Lit(new Color(0.52f, 0.57f, 0.63f), 0.1f);

            // floor with white grid every 2.5 m
            var floorMat = Mats.Lit(Color.white, 0.2f);
            floorMat.mainTexture = GridTex();
            floorMat.mainTextureScale = new Vector2(HX * 2f / 2.5f, HZ * 2f / 2.5f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.position = new Vector3(0, -0.5f, 0);
            floor.transform.localScale = new Vector3(HX * 2f, 1f, HZ * 2f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;

            // outer walls
            Box(new Vector3(0, 4f, HZ + 0.5f), new Vector3(HX * 2f, 8f, 1f), wall, false);
            Box(new Vector3(0, 4f, -HZ - 0.5f), new Vector3(HX * 2f, 8f, 1f), wall, false);
            Box(new Vector3(HX + 0.5f, 4f, 0), new Vector3(1f, 8f, HZ * 2f), wall, false);
            Box(new Vector3(-HX - 0.5f, 4f, 0), new Vector3(1f, 8f, HZ * 2f), wall, false);

            // left platform + stairs
            Box(new Vector3(-15f, 1.5f, 14f), new Vector3(10f, 3f, 8f), plat, true);
            for (int i = 0; i < 7; i++) { float h = 3f * (7 - i) / 8f; Box(new Vector3(-15f, h / 2f, 10f - 0.45f - i * 0.9f), new Vector3(4f, h, 0.9f), plat, true); }
            // right platform + stairs
            Box(new Vector3(14f, 2f, 15f), new Vector3(12f, 4f, 10f), plat, true);
            for (int i = 0; i < 9; i++) { float h = 4f * (9 - i) / 10f; Box(new Vector3(14f, h / 2f, 10f - 0.45f - i * 0.9f), new Vector3(5f, h, 0.9f), plat, true); }
            // cover walls where the specimens run / crawl to hide
            Covers.Clear();
            Cover(1, new Vector3(-8f, 0, -3f), 0f);
            Cover(2, new Vector3(8f, 0, -1f), 0f);
            Cover(3, new Vector3(-3f, 0, 6f), 0f);
            Cover(4, new Vector3(4f, 0, 9f), 0f);
            Cover(5, new Vector3(-12f, 0, 4f), 90f);
            Cover(6, new Vector3(12f, 0, 5f), 90f);
            Box(new Vector3(0f, 1.25f, 21f), new Vector3(10f, 2.5f, 0.5f), plat, true);
            Box(new Vector3(-18f, 0.5f, -10f), new Vector3(2.5f, 1f, 2.5f), plat, true);
            Box(new Vector3(18f, 0.5f, -12f), new Vector3(2f, 1f, 3f), plat, true);

            Text("VERTICAL IMPACT\nTESTSITE", new Vector3(-6f, 6.2f, HZ - 0.02f), Quaternion.identity, 0.9f, new Color(0.2f, 0.24f, 0.28f));
            Text("01", new Vector3(-9f, 0.01f, -6f), Quaternion.Euler(90, 0, 0), 1.6f, new Color(1, 1, 1, 0.9f));
            Text("02", new Vector3(9f, 0.01f, -9f), Quaternion.Euler(90, 0, 0), 1.6f, new Color(1, 1, 1, 0.9f));
        }

        static void Cover(int n, Vector3 p, float yaw)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "COVER " + n.ToString("00");
            g.transform.SetPositionAndRotation(p + Vector3.up * 0.65f, rot);
            g.transform.localScale = new Vector3(3f, 1.3f, 0.35f);
            g.GetComponent<Renderer>().sharedMaterial = wall;
            var e = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(e.GetComponent<Collider>());
            e.transform.SetPositionAndRotation(p + Vector3.up * 1.31f, rot);
            e.transform.localScale = new Vector3(3.02f, 0.03f, 0.37f);
            e.GetComponent<Renderer>().sharedMaterial = orange;
            string label = "COVER / " + n.ToString("00");
            Text(label, p + Vector3.up * 0.75f + rot * new Vector3(0, 0, -0.18f), rot, 0.28f, new Color(1f, 1f, 1f, 0.95f));
            Text(label, p + Vector3.up * 0.75f + rot * new Vector3(0, 0, 0.18f), rot * Quaternion.Euler(0, 180, 0), 0.28f, new Color(1f, 1f, 1f, 0.95f));
            Covers.Add(new CoverWall { pos = p, normal = rot * Vector3.forward, along = rot * Vector3.right });
        }

        static void Box(Vector3 c, Vector3 s, Material m, bool edge)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.transform.position = c; g.transform.localScale = s;
            g.GetComponent<Renderer>().sharedMaterial = m;
            if (!edge) return;
            // orange strip on the front top edge (toward -Z, where the player starts)
            var e = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(e.GetComponent<Collider>());
            e.transform.position = c + new Vector3(0, s.y / 2f - 0.03f, -s.z / 2f - 0.004f);
            e.transform.localScale = new Vector3(s.x + 0.01f, 0.06f, 0.01f);
            e.GetComponent<Renderer>().sharedMaterial = orange;
        }

        static void Text(string s, Vector3 p, Quaternion r, float size, Color c)
        {
            var go = new GameObject("Text " + s);
            go.transform.SetPositionAndRotation(p, r);
            var tm = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font; tm.text = s; tm.fontSize = 64; tm.characterSize = size * 0.1f; tm.color = c;
            tm.anchor = TextAnchor.MiddleCenter; tm.fontStyle = FontStyle.Bold;
            go.GetComponent<MeshRenderer>().sharedMaterial = Mats.Text(font);
        }

        static Texture2D GridTex()
        {
            const int S = 256;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            var baseC = new Color(0.62f, 0.68f, 0.76f);
            var line = new Color(0.95f, 0.97f, 1f);
            var px = new Color[S * S];
            var rng = new System.Random(5);
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                bool l = x < 2 || x >= S - 2 || y < 2 || y >= S - 2;
                float n = (float)rng.NextDouble() * 0.015f;
                px[y * S + x] = l ? line : new Color(baseC.r + n, baseC.g + n, baseC.b + n);
            }
            t.SetPixels(px); t.Apply(true);
            return t;
        }
    }
}
