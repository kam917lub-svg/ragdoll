using UnityEngine;
using UnityEngine.Rendering;

namespace VITS
{
    // "Vertical Impact Testsite": open light-blue test area, white grid floor,
    // pale platforms with orange edges, stairs, low cover walls.
    public static class Level
    {
        public static readonly Color Sky = new Color(0.92f, 0.92f, 0.9f);   // crash test hall: white, yellow, black
        public const float HX = 36f, HZ = 36f;
        // four pits in the floor (x, z, half size): two with stairs down, two with a meat grinder at the bottom
        public static readonly Rect[] Pits = { new Rect(-29f, -12f, 5f, 5f), new Rect(24f, -12f, 5f, 5f), new Rect(-29f, 22f, 5f, 5f), new Rect(24f, 22f, 5f, 5f) };
        public const float PitDepth = 3.5f;
        public struct CoverWall { public Vector3 pos, normal, along; }
        // a raised platform with its staircase: bottom / top landing points, the platform area and the stairs area (xz)
        public class Stair { public Vector3 bottom, top; public Rect plat, steps; public float h; }
        public static readonly System.Collections.Generic.List<Stair> Stairs = new System.Collections.Generic.List<Stair>();

        static bool In(Rect r, Vector3 p) => r.Contains(new Vector2(p.x, p.z));

        // index of the platform a point is on (-1 = ground level)
        public static int PlatOf(Vector3 p)
        {
            for (int i = 0; i < Stairs.Count; i++) if (In(Stairs[i].plat, p) && p.y > Stairs[i].h - 0.6f) return i;
            return -1;
        }

        // next point to walk (or crawl) to on the way to 'target': through the stairs if it is on another level
        public static Vector3 Route(Vector3 pos, Vector3 target)
        {
            int a = PlatOf(pos), b = PlatOf(target);
            if (a == b) return target;
            if (a >= 0)
            {
                var s = Stairs[a];                                            // going down
                if (In(s.steps, pos) || (pos - s.top).magnitude < 0.8f) return s.bottom;
                return s.top;
            }
            var t = Stairs[b];                                                // going up
            if (In(t.steps, pos) || (pos - t.bottom).magnitude < 0.8f) return t.top;
            return t.bottom;
        }

        public static readonly System.Collections.Generic.List<CoverWall> Covers = new System.Collections.Generic.List<CoverWall>();
        static Material plat, orange, wall, hazard;

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

            plat = Mats.Lit(new Color(0.95f, 0.95f, 0.94f), 0.1f);
            orange = Mats.Lit(new Color(1f, 0.8f, 0.03f), 0.2f);   // safety yellow edges
            wall = Mats.Lit(new Color(0.96f, 0.96f, 0.95f), 0.1f);
            hazard = Mats.Lit(Color.white, 0.2f); hazard.mainTexture = HazardTex();

            // floor with white grid every 2.5 m
            var floorMat = Mats.Lit(Color.white, 0.2f);
            floorMat.mainTexture = GridTex();
            floorMat.mainTextureScale = new Vector2(HX * 2f / 2.5f, HZ * 2f / 2.5f);
            // the floor is cut into slabs around the pits
            var xs = new System.Collections.Generic.List<float> { -HX, HX }; var zs = new System.Collections.Generic.List<float> { -HZ, HZ };
            foreach (var r in Pits) { xs.Add(r.xMin); xs.Add(r.xMax); zs.Add(r.yMin); zs.Add(r.yMax); }
            xs.Sort(); zs.Sort();
            for (int i = 0; i + 1 < xs.Count; i++) for (int j = 0; j + 1 < zs.Count; j++)
            {
                float x0 = xs[i], x1 = xs[i + 1], z0 = zs[j], z1 = zs[j + 1];
                if (x1 - x0 < 0.01f || z1 - z0 < 0.01f) continue;
                var c = new Vector2((x0 + x1) / 2f, (z0 + z1) / 2f);
                bool hole = false; foreach (var r in Pits) if (r.Contains(c)) hole = true;
                if (hole) continue;
                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = "Floor";
                slab.transform.position = new Vector3(c.x, -0.5f, c.y);
                slab.transform.localScale = new Vector3(x1 - x0, 1f, z1 - z0);
                var m = new Material(floorMat) { mainTextureScale = new Vector2((x1 - x0) / 2.5f, (z1 - z0) / 2.5f), mainTextureOffset = new Vector2(x0 / 2.5f, z0 / 2.5f) };
                slab.GetComponent<Renderer>().sharedMaterial = m;
            }
            for (int i = 0; i < Pits.Length; i++) Pit(Pits[i], i % 2 == 0, i >= 2);

            // black and yellow hazard band along the bottom of every outer wall
            for (int side = 0; side < 4; side++)
            {
                bool zx = side < 2; float sg = side % 2 == 0 ? 1f : -1f;
                var band = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(band.GetComponent<Collider>());
                band.transform.position = zx ? new Vector3(0, 0.6f, sg * (HZ - 0.005f)) : new Vector3(sg * (HX - 0.005f), 0.6f, 0);
                band.transform.localScale = zx ? new Vector3(HX * 2f, 1.2f, 0.02f) : new Vector3(0.02f, 1.2f, HZ * 2f);
                var hm = new Material(hazard); hm.mainTextureScale = new Vector2(HX * 2f / 1.2f, 1f);
                band.GetComponent<Renderer>().sharedMaterial = hm;
            }
            // outer walls
            Box(new Vector3(0, 10f, HZ + 0.5f), new Vector3(HX * 2f, 20f, 1f), wall, false);
            Box(new Vector3(0, 10f, -HZ - 0.5f), new Vector3(HX * 2f, 20f, 1f), wall, false);
            Box(new Vector3(HX + 0.5f, 10f, 0), new Vector3(1f, 20f, HZ * 2f), wall, false);
            Box(new Vector3(-HX - 0.5f, 10f, 0), new Vector3(1f, 20f, HZ * 2f), wall, false);

            Stairs.Clear();
            // left platform (3 m) and right platform (4 m), each with a normal staircase (18 cm rise, 30 cm tread)
            Box(new Vector3(-15f, 1.5f, 14f), new Vector3(10f, 3f, 8f), plat, true);
            Staircase(-15f, 4f, 10f, 3f, new Rect(-20f, 10f, 10f, 8f));
            Box(new Vector3(14f, 2f, 15f), new Vector3(12f, 4f, 10f), plat, true);
            Staircase(14f, 5f, 10f, 4f, new Rect(8f, 10f, 12f, 10f));
            // drop test towers along the back wall: 5, 8, 10 and 15 m, each with a lift
            float[] th = { 5f, 8f, 10f, 15f }; float[] tx = { -12f, -4f, 4f, 12f };
            for (int i = 0; i < th.Length; i++) Tower(tx[i], -HZ + 3.5f, th[i]);
            // cover walls where the specimens run / crawl to hide
            Covers.Clear();
            Cover(1, new Vector3(-8f, 0, -3f), 0f);
            Cover(2, new Vector3(8f, 0, -1f), 0f);
            Cover(3, new Vector3(-3f, 0, 6f), 0f);
            Cover(4, new Vector3(4f, 0, 9f), 0f);
            Cover(5, new Vector3(-12f, 0, 4f), 90f);
            Cover(6, new Vector3(10.2f, 0, 5f), 90f);   // clear of the right staircase
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
            var cm = new Material(hazard); cm.mainTextureScale = new Vector2(3f / 0.65f, 1f);
            g.GetComponent<Renderer>().sharedMaterial = cm;   // cover walls: hazard striped
            var e = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(e.GetComponent<Collider>());
            e.transform.SetPositionAndRotation(p + Vector3.up * 1.31f, rot);
            e.transform.localScale = new Vector3(3.02f, 0.03f, 0.37f);
            e.GetComponent<Renderer>().sharedMaterial = orange;
            string label = "COVER / " + n.ToString("00");
            Text(label, p + Vector3.up * 0.75f + rot * new Vector3(0, 0, -0.18f), rot, 0.28f, new Color(1f, 1f, 1f, 0.95f));
            Text(label, p + Vector3.up * 0.75f + rot * new Vector3(0, 0, 0.18f), rot * Quaternion.Euler(0, 180, 0), 0.28f, new Color(1f, 1f, 1f, 0.95f));   // white label on the stripes
            Covers.Add(new CoverWall { pos = p, normal = rot * Vector3.forward, along = rot * Vector3.right });
        }

        // n steps from the floor up to a platform of height H whose front edge is at zTop; the stairs run toward -Z
        static void Staircase(float x, float width, float zTop, float H, Rect platRect)
        {
            const float tread = 0.3f;
            int n = Mathf.Max(1, Mathf.RoundToInt(H / 0.18f) - 1);
            float rise = H / (n + 1);
            for (int i = 0; i < n; i++)
            {
                float h = rise * (n - i);
                Box(new Vector3(x, h / 2f, zTop - tread * (i + 0.5f)), new Vector3(width, h, tread), plat, i == n - 1);
            }
            float len = n * tread;
            Stairs.Add(new Stair { bottom = new Vector3(x, 0f, zTop - len - 0.8f), top = new Vector3(x, H, zTop + 1f), plat = platRect,
                                   steps = new Rect(x - width / 2f, zTop - len - 0.2f, width, len + 0.2f), h = H });
        }

        // a solid tower to throw / drop Carls from, the height written on it, and a lift pad on its front
        static void Tower(float x, float z, float H)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "TOWER " + H.ToString("0") + " M";
            g.transform.position = new Vector3(x, H / 2f, z); g.transform.localScale = new Vector3(4f, H, 4f);
            g.GetComponent<Renderer>().sharedMaterial = plat;
            var e = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(e.GetComponent<Collider>());
            e.transform.position = new Vector3(x, H - 0.03f, z + 2.004f); e.transform.localScale = new Vector3(4.01f, 0.06f, 0.01f);
            e.GetComponent<Renderer>().sharedMaterial = orange;
            string label = H.ToString("0") + " M";
            Text(label, new Vector3(x, H - 0.8f, z + 2.02f), Quaternion.Euler(0, 180, 0), 1.2f, new Color(0.2f, 0.24f, 0.28f));
            Text(label, new Vector3(x, H + 0.01f, z), Quaternion.Euler(90, 0, 0), 1f, new Color(1f, 1f, 1f, 0.9f));
            // height marks every meter on the front
            for (int m = 1; m < H; m++)
            {
                var k = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(k.GetComponent<Collider>());
                k.transform.position = new Vector3(x - 1.7f, m, z + 2.004f); k.transform.localScale = new Vector3(0.5f, 0.025f, 0.01f);
                k.GetComponent<Renderer>().sharedMaterial = orange;
            }
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "LIFT " + label;
            pad.transform.position = new Vector3(x + 1f, -0.09f, z + 2.95f); pad.transform.localScale = new Vector3(1.8f, 0.2f, 1.8f);
            pad.GetComponent<Renderer>().sharedMaterial = orange;
            pad.AddComponent<Lift>().top = H;
        }

        // a square pit: dark walls, yellow/black rim; optional stairs down one side; optional meat grinder at the bottom
        static void Pit(Rect r, bool stairs, bool grinder)
        {
            float D = PitDepth, cx = r.center.x, cz = r.center.y, w = r.width, l = r.height;
            var dark = Mats.Lit(new Color(0.12f, 0.12f, 0.12f), 0.1f);
            Box(new Vector3(cx, -D - 0.5f, cz), new Vector3(w + 1f, 1f, l + 1f), dark, false);                                    // bottom
            // walls stop 2 cm under the floor surface: no two faces at the same height (that flickered)
            Box(new Vector3(r.xMin - 0.25f, -D / 2f - 0.51f, cz), new Vector3(0.5f, D + 0.98f, l + 1f), dark, false);
            Box(new Vector3(r.xMax + 0.25f, -D / 2f - 0.51f, cz), new Vector3(0.5f, D + 0.98f, l + 1f), dark, false);
            Box(new Vector3(cx, -D / 2f - 0.51f, r.yMin - 0.25f), new Vector3(w, D + 0.98f, 0.5f), dark, false);
            Box(new Vector3(cx, -D / 2f - 0.51f, r.yMax + 0.25f), new Vector3(w, D + 0.98f, 0.5f), dark, false);
            // hazard rim
            for (int s = 0; s < 4; s++)
            {
                var e = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(e.GetComponent<Collider>());
                bool zx = s < 2; float sg = s % 2 == 0 ? 1f : -1f;
                e.transform.position = zx ? new Vector3(cx, 0.003f, sg > 0 ? r.yMax + 0.15f : r.yMin - 0.15f) : new Vector3(sg > 0 ? r.xMax + 0.15f : r.xMin - 0.15f, 0.003f, cz);
                e.transform.localScale = zx ? new Vector3(w + 0.6f, 0.01f, 0.3f) : new Vector3(0.3f, 0.01f, l + 0.6f);
                var hm = new Material(hazard); hm.mainTextureScale = new Vector2((zx ? w : l) / 0.3f, 1f);
                e.GetComponent<Renderer>().sharedMaterial = hm;
            }
            Text((grinder ? "GRINDER" : "PIT") + " " + D.ToString("0.0") + " M", new Vector3(cx, 0.01f, r.yMin - 0.7f), Quaternion.Euler(90, 0, 0), 0.8f, new Color(0.05f, 0.05f, 0.05f, 0.9f));
            if (stairs)
            {
                // steps along the -x wall going down toward +z, 1 m wide
                int n = Mathf.RoundToInt(D / 0.18f); float rise = D / n, tread = (l - 0.3f) / n;
                for (int i = 1; i < n; i++)
                {
                    float top = -rise * i;
                    float z = r.yMin + tread * (i - 0.5f);
                    if (z > r.yMax - 0.2f) break;
                    Box(new Vector3(r.xMin + 0.5f, (top - D) / 2f, z), new Vector3(1f, top + D, tread), plat, false);
                }
            }
            if (grinder)
            {
                var g = new GameObject("MEAT GRINDER").AddComponent<Grinder>();
                g.transform.position = new Vector3(cx + (stairs ? 0.5f : 0f), -D + 0.55f, cz);
                g.length = w - (stairs ? 1.4f : 0.4f);
            }
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

        static Texture2D HazardTex()
        {
            const int S = 64;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) px[y * S + x] = ((x + y) % S) < S / 2 ? new Color(0.06f, 0.06f, 0.06f) : new Color(1f, 0.8f, 0.03f);
            t.SetPixels(px); t.Apply(true);
            return t;
        }

        static Texture2D GridTex()
        {
            const int S = 256;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            var baseC = new Color(0.9f, 0.9f, 0.88f);
            var line = new Color(0.08f, 0.08f, 0.08f);
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

    // lift pad: stand on it and it takes you up to the top of its tower (or back down). Anything lying on it rides too.
    public class Lift : MonoBehaviour
    {
        public float top;
        float y0, y, dir, wait; bool needLeave; Rigidbody rb;
        const float Speed = 3f;

        void Start()
        {
            rb = gameObject.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.Interpolate;
            y0 = y = transform.position.y;
        }

        void FixedUpdate()
        {
            var pl = Game.I != null ? Game.I.player : null;
            float dt = Time.fixedDeltaTime;
            float surf = y + transform.localScale.y / 2f;
            bool riding = false, nearBottom = false, nearTop = false;
            if (pl != null)
            {
                Vector3 p = pl.transform.position, c = transform.position;
                bool over = Mathf.Abs(p.x - c.x) < 1.0f && Mathf.Abs(p.z - c.z) < 1.0f;
                riding = over && Mathf.Abs(p.y - surf) < 0.3f;
                float dxz = new Vector2(p.x - c.x, p.z - c.z).magnitude;
                nearBottom = dxz < 4f && p.y < 0.8f;
                nearTop = dxz < 5f && Mathf.Abs(p.y - (y0 + top)) < 0.8f;
            }
            if (!riding) needLeave = false;
            bool atBottom = y <= y0 + 0.001f, atTop = y >= y0 + top - 0.001f;
            if (dir == 0)
            {
                if (riding && !needLeave) { wait += dt; if (wait > 0.5f) dir = atBottom ? 1f : -1f; }
                else if (!riding && atTop && nearBottom) dir = -1f;       // call it down
                else if (!riding && atBottom && nearTop) dir = 1f;        // call it up
                else wait = 0;
            }
            if (dir == 0) return;
            float ny = Mathf.Clamp(y + dir * Speed * dt, y0, y0 + top);
            float dy = ny - y; y = ny;
            rb.MovePosition(new Vector3(transform.position.x, y, transform.position.z));
            if (riding && pl != null) pl.Carry(new Vector3(0, dy, 0));
            if (y <= y0 + 0.001f || y >= y0 + top - 0.001f) { dir = 0; wait = 0; needLeave = riding; }
        }
    }

    // two toothed steel rollers turning toward each other: whatever drops between them is pulled in and torn apart
    public class Grinder : MonoBehaviour
    {
        public float length = 4f;
        Transform[] rollers = new Transform[2];
        Rigidbody[] rbs = new Rigidbody[2];
        float ang;
        static readonly Collider[] buf = new Collider[64];
        readonly System.Collections.Generic.Dictionary<Part, float> chew = new System.Collections.Generic.Dictionary<Part, float>();
        const float R = 0.32f, Gap = 0.36f;

        void Start()
        {
            var steel = Mats.Lit(new Color(0.55f, 0.57f, 0.6f), 0.7f);
            var tooth = Mats.Lit(new Color(0.35f, 0.36f, 0.38f), 0.6f);
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("roller" + i);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0, 0, (i == 0 ? -1f : 1f) * Gap);
                var cyl = Mats.Vis(PrimitiveType.Cylinder, go.transform, Vector3.zero, new Vector3(R * 2f, length / 2f, R * 2f), steel);
                cyl.transform.localRotation = Quaternion.Euler(0, 0, 90);
                for (int k = 0; k < 10; k++) for (int j = 0; j < 6; j++)
                {
                    float a = j * 60f + k * 17f;
                    var t = Mats.Vis(PrimitiveType.Cube, go.transform, Quaternion.Euler(a, 0, 0) * new Vector3(0, R + 0.03f, 0) + new Vector3(-length / 2f + (k + 0.5f) * length / 10f, 0, 0), new Vector3(0.05f, 0.09f, 0.07f), tooth);
                    t.transform.localRotation = Quaternion.Euler(a, 0, 0);
                }
                var cap = go.AddComponent<CapsuleCollider>(); cap.direction = 0; cap.radius = R + 0.04f; cap.height = length + 2f * R;
                var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true;
                rollers[i] = go.transform; rbs[i] = rb;
            }
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            ang += 260f * dt;   // tops turn toward the middle
            for (int i = 0; i < 2; i++)
                rbs[i].MoveRotation(transform.rotation * Quaternion.Euler((i == 0 ? 1f : -1f) * ang, 0, 0));
            // everything in the jaws: pulled down and in, chewed apart
            int n = Physics.OverlapBoxNonAlloc(transform.position + Vector3.up * 0.35f, new Vector3(length / 2f, 0.55f, Gap + R), buf, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var rb = buf[i].attachedRigidbody;
                if (rb == null || rb.isKinematic) continue;
                Vector3 toC = transform.position - rb.position; toC.x = 0;
                rb.AddForce((toC.normalized * 6f + Vector3.down * 10f) * rb.mass);
                var p = buf[i].GetComponent<Part>();
                if (p == null) { if (buf[i].gameObject.name == "gib" && Random.value < dt * 3f) Destroy(buf[i].gameObject); continue; }
                chew.TryGetValue(p, out float c); c += dt; chew[p] = c;
                if (p.owner != null) p.owner.Grind(p, c);
            }
        }
    }
}
