using UnityEngine;
using UnityEngine.Rendering;

namespace VITS
{
    // The two extra maps. Everything is built from code, like the test site.
    public static partial class Level
    {
        static GameObject B(Vector3 c, Vector3 s, Material m, bool collide = true, string name = "Cube", Transform parent = null)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            if (!collide) Object.DestroyImmediate(g.GetComponent<Collider>());
            if (parent != null) g.transform.SetParent(parent, false);
            g.transform.localPosition = c; g.transform.localScale = s;
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }
        static GameObject P(PrimitiveType t, Vector3 c, Vector3 s, Quaternion r, Material m, bool collide = false, Transform parent = null)
        {
            var g = GameObject.CreatePrimitive(t);
            if (!collide) Object.DestroyImmediate(g.GetComponent<Collider>());
            if (parent != null) g.transform.SetParent(parent, false);
            g.transform.localPosition = c; g.transform.localRotation = r; g.transform.localScale = s;
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }
        static Material Glow(Color c) => Mats.Decal(Texture2D.whiteTexture, c, 2450);   // unlit, looks lit from inside
        static void AddCover(Vector3 pos, Vector3 normal, Vector3 along) => Covers.Add(new CoverWall { pos = pos, normal = normal, along = along });

        static Texture2D Tiles(Color a, Color b, int n)
        {
            const int S = 128; var t = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color[S * S]; var rng = new System.Random(3);
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                bool grout = x % (S / n) < 2 || y % (S / n) < 2;
                bool ch = ((x / (S / n)) + (y / (S / n))) % 2 == 0;
                float k = (float)rng.NextDouble() * 0.02f;
                var c = grout ? new Color(0.6f, 0.6f, 0.6f) : (ch ? a : b);
                px[y * S + x] = new Color(c.r + k, c.g + k, c.b + k);
            }
            t.SetPixels(px); t.Apply(true); return t;
        }
        static Texture2D Noise(Color a, Color b, float scale, int seed)
        {
            const int S = 128; var t = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                float n = Mathf.PerlinNoise(x * scale + seed, y * scale) * 0.7f + Mathf.PerlinNoise(x * scale * 4f, y * scale * 4f + seed) * 0.3f;
                px[y * S + x] = Color.Lerp(a, b, n);
            }
            t.SetPixels(px); t.Apply(true); return t;
        }

        // ================= SUPERMARKET =================
        static void BuildMarket()
        {
            const float IX = 22f, IZ = 16f;   // the store itself; the map (HX/HZ) also covers the car park and road
            HX = 44f; HZ = 48f;
            Sky = new Color(0.55f, 0.7f, 0.9f);   // daylight outside
            SpawnArea = new Rect(-18f, -7f, 32f, 20f);   // customers wander inside the store
            PlayerStart = new Vector3(2f, 0.15f, -21f);   // on the pavement outside the doors
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.72f, 0.74f, 0.76f);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.25f; sun.color = new Color(1f, 0.96f, 0.88f);
            sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.85f; sun.shadowBias = 0.08f; sun.shadowNormalBias = 0.6f;
            sun.transform.rotation = Quaternion.Euler(48f, 30f, 0);
            QualitySettings.shadowDistance = 60f;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = new Color(0.7f, 0.78f, 0.88f);
            RenderSettings.fogStartDistance = 60f; RenderSettings.fogEndDistance = 220f;

            plat = Mats.Lit(new Color(0.9f, 0.9f, 0.88f), 0.1f); orange = Mats.Lit(new Color(1f, 0.8f, 0.03f), 0.2f);
            wall = Mats.Lit(new Color(0.93f, 0.9f, 0.82f), 0.1f);
            hazard = Mats.Lit(Color.white, 0.2f); hazard.mainTexture = HazardTex();
            var floor = Mats.Lit(Color.white, 0.55f); floor.mainTexture = Tiles(new Color(0.94f, 0.94f, 0.92f), new Color(0.82f, 0.83f, 0.84f), 2);
            floor.mainTextureScale = new Vector2(IX * 2f / 1.2f, IZ * 2f / 1.2f);
            B(new Vector3(0, -0.5f, 0), new Vector3(IX * 2f, 1f, IZ * 2f), floor, true, "Floor");
            // walls, ceiling with light panels
            float H = 5f;
            B(new Vector3(0, H / 2f, IZ + 0.25f), new Vector3(IX * 2f + 1f, H, 0.5f), wall);
            B(new Vector3(IX + 0.25f, H / 2f, 0), new Vector3(0.5f, H, IZ * 2f), wall);
            B(new Vector3(-IX - 0.25f, H / 2f, 0), new Vector3(0.5f, H, IZ * 2f), wall);
            // front: glass wall with the sliding door
            var glass = Mats.Decal(Texture2D.whiteTexture, new Color(0.7f, 0.85f, 0.95f, 0.25f), 3005);
            B(new Vector3(-12f, H / 2f, -IZ - 0.05f), new Vector3(20f, H, 0.1f), glass);
            B(new Vector3(13f, H / 2f, -IZ - 0.05f), new Vector3(18f, H, 0.1f), glass);
            B(new Vector3(2f, 4.3f, -IZ - 0.05f), new Vector3(4f, 1.4f, 0.1f), wall);
            var green = Mats.Lit(new Color(0.1f, 0.55f, 0.25f), 0.3f);
            B(new Vector3(0, H - 0.6f, -IZ + 0.3f), new Vector3(10f, 0.9f, 0.15f), green, false);
            Text("FRESH MART", new Vector3(0, H - 0.6f, -IZ + 0.2f), Quaternion.identity, 0.7f, Color.white);
            var ceil = Mats.Lit(new Color(0.85f, 0.85f, 0.83f), 0.05f);
            B(new Vector3(0, H + 0.1f, 0), new Vector3(IX * 2f, 0.2f, IZ * 2f), ceil, false);
            var panel = Glow(new Color(1f, 1f, 0.95f, 1f));
            for (int x = -16; x <= 16; x += 8) for (int z = -12; z <= 12; z += 6) B(new Vector3(x, H - 0.02f, z), new Vector3(1.2f, 0.04f, 3f), panel, false);
            for (int i = 0; i < 4; i++)
            {
                var l = new GameObject("lamp").AddComponent<Light>();
                l.type = LightType.Point; l.range = 18f; l.intensity = 0.9f; l.color = new Color(1f, 0.98f, 0.92f);
                l.transform.position = new Vector3(-12f + i * 8f, H - 0.5f, 0f);
            }
            // aisles: double-sided shelves full of products
            var shelfM = Mats.Lit(new Color(0.75f, 0.77f, 0.8f), 0.4f);
            var prod = new Material[10];
            Color[] pc = { new Color(0.9f, 0.2f, 0.2f), new Color(0.95f, 0.75f, 0.1f), new Color(0.2f, 0.45f, 0.9f), new Color(0.2f, 0.75f, 0.3f), new Color(0.95f, 0.95f, 0.95f),
                           new Color(0.6f, 0.3f, 0.15f), new Color(0.9f, 0.5f, 0.1f), new Color(0.55f, 0.2f, 0.65f), new Color(0.15f, 0.15f, 0.15f), new Color(0.95f, 0.6f, 0.7f) };
            for (int i = 0; i < prod.Length; i++) prod[i] = Mats.Lit(pc[i], 0.35f);
            string[] aisle = { "BAKERY", "SNACKS", "DRINKS", "CANNED", "CLEANING", "PASTA" };
            var rng = new System.Random(11);
            for (int a = 0; a < 6; a++)
            {
                float x = -15f + a * 5f, len = 11f, sh = 1.9f;
                var root = new GameObject("COVER SHELF " + (a + 1)).transform;
                root.position = new Vector3(x, 0, 1f);
                var body = B(new Vector3(0, sh / 2f, 0), new Vector3(0.9f, sh, len), shelfM, true, "COVER SHELF " + (a + 1), root);
                for (int lv = 0; lv < 4; lv++)
                {
                    float y = 0.18f + lv * 0.45f;
                    B(new Vector3(0, y, 0), new Vector3(1.1f, 0.03f, len), shelfM, false, "board", root);
                    for (int side = -1; side <= 1; side += 2)
                        for (float z = -len / 2f + 0.3f; z < len / 2f - 0.2f; z += 0.42f + (float)rng.NextDouble() * 0.2f)
                        {
                            float hgt = 0.18f + (float)rng.NextDouble() * 0.2f, wdt = 0.16f + (float)rng.NextDouble() * 0.16f;
                            B(new Vector3(side * 0.38f, y + hgt / 2f + 0.015f, z), new Vector3(0.28f, hgt, wdt), prod[rng.Next(prod.Length)], false, "product", root);
                        }
                }
                // hanging aisle sign
                B(new Vector3(0, 3.6f, 0), new Vector3(0.05f, 0.5f, 2.4f), green, false, "sign", root);
                Text(aisle[a], new Vector3(x - 0.04f, 3.6f, 1f), Quaternion.Euler(0, 90, 0), 0.35f, Color.white);
                Text(aisle[a], new Vector3(x + 0.04f, 3.6f, 1f), Quaternion.Euler(0, -90, 0), 0.35f, Color.white);
                AddCover(new Vector3(x, 0, 1f), Vector3.right, Vector3.forward);
            }
            // freezers along the back wall: white cabinets, glass doors, lit inside
            var frz = Mats.Lit(new Color(0.95f, 0.95f, 0.97f), 0.5f);
            var frzIn = Glow(new Color(0.75f, 0.9f, 1f, 1f));
            for (float x = -18f; x <= 14f; x += 4f)
            {
                B(new Vector3(x, 1.1f, IZ - 0.6f), new Vector3(3.8f, 2.2f, 1.1f), frz, true, "freezer");
                B(new Vector3(x, 1.15f, IZ - 1.16f), new Vector3(3.5f, 1.7f, 0.02f), frzIn, false, "freezer glass");
                for (int k = 0; k < 3; k++) B(new Vector3(x - 1.2f + k * 1.2f, 1.15f, IZ - 1.18f), new Vector3(0.04f, 1.7f, 0.04f), shelfM, false, "door frame");
            }
            Text("FROZEN", new Vector3(-2f, 2.7f, IZ - 1.12f), Quaternion.Euler(0, 180, 0), 0.5f, new Color(0.1f, 0.3f, 0.6f));
            // checkouts near the entrance, each with a cashier spot behind it
            var counter = Mats.Lit(new Color(0.3f, 0.32f, 0.35f), 0.4f);
            var belt = Mats.Lit(new Color(0.08f, 0.08f, 0.08f), 0.2f);
            var reg = Mats.Lit(new Color(0.85f, 0.85f, 0.85f), 0.5f);
            var screen = Glow(new Color(0.3f, 0.9f, 0.5f, 1f));
            for (int i = 0; i < 3; i++)
            {
                float x = -12f + i * 6f, z = -10f;
                var r = new GameObject("COVER CHECKOUT " + (i + 1)).transform; r.position = new Vector3(x, 0, z);
                B(new Vector3(0, 0.45f, 0), new Vector3(0.9f, 0.9f, 3.2f), counter, true, "COVER CHECKOUT " + (i + 1), r);
                B(new Vector3(0, 0.91f, 0.3f), new Vector3(0.6f, 0.03f, 2.2f), belt, false, "belt", r);
                B(new Vector3(0.25f, 1.05f, -1.1f), new Vector3(0.35f, 0.25f, 0.35f), reg, false, "register", r);
                B(new Vector3(0.25f, 1.3f, -1.1f), new Vector3(0.05f, 0.22f, 0.3f), screen, false, "screen", r);
                B(new Vector3(0, 1.6f, -1.5f), new Vector3(0.05f, 1.4f, 0.05f), reg, false, "pole", r);
                B(new Vector3(0, 2.35f, -1.5f), new Vector3(0.1f, 0.35f, 0.35f), green, false, "lane light", r);
                Text((i + 1).ToString(), new Vector3(x - 0.06f, 2.35f, z - 1.5f), Quaternion.Euler(0, 90, 0), 0.3f, Color.white);
                Cashiers.Add(new Vector3(x + 1.0f, 0, z - 0.9f));
                AddCover(new Vector3(x, 0, z), Vector3.right, Vector3.forward);
            }
            // shopping carts by the door
            var cart = Mats.Lit(new Color(0.7f, 0.72f, 0.75f), 0.8f);
            for (int i = 0; i < 4; i++)
            {
                var c = new GameObject("cart").transform; c.position = new Vector3(10f + i * 0.35f, 0, -13f);
                B(new Vector3(0, 0.75f, 0), new Vector3(0.55f, 0.45f, 0.85f), cart, true, "cart", c);
                B(new Vector3(0, 0.35f, 0), new Vector3(0.5f, 0.04f, 0.8f), cart, false, "cart base", c);
                B(new Vector3(0, 1.05f, -0.45f), new Vector3(0.55f, 0.04f, 0.04f), cart, false, "handle", c);
            }
            // fruit stand
            var crate = Mats.Lit(new Color(0.55f, 0.38f, 0.2f), 0.2f);
            for (int i = 0; i < 4; i++)
            {
                B(new Vector3(14f + (i % 2) * 1.3f, 0.4f, 6f + (i / 2) * 1.3f), new Vector3(1.2f, 0.8f, 1.2f), crate, true, "crate");
                for (int k = 0; k < 9; k++)
                    P(PrimitiveType.Sphere, new Vector3(14f + (i % 2) * 1.3f + (k % 3 - 1) * 0.3f, 0.88f, 6f + (i / 2) * 1.3f + (k / 3 - 1) * 0.3f), Vector3.one * 0.22f, Quaternion.identity, prod[i == 0 ? 0 : i == 1 ? 1 : i == 2 ? 3 : 6]);
            }
                    Outside();
        }

        // car park in front of the store, a pavement, a two-lane road, street lights, parked cars, people around and inside them
        public static readonly System.Collections.Generic.List<Vector3> CarPeople = new System.Collections.Generic.List<Vector3>();
        public static readonly System.Collections.Generic.List<Vector3> Drivers = new System.Collections.Generic.List<Vector3>();
        public static readonly System.Collections.Generic.List<Quaternion> DriverRot = new System.Collections.Generic.List<Quaternion>();
        static void Outside()
        {
            CarPeople.Clear(); Drivers.Clear(); DriverRot.Clear();
            var asphalt = Mats.Lit(Color.white, 0.15f); asphalt.mainTexture = Noise(new Color(0.16f, 0.16f, 0.17f), new Color(0.24f, 0.24f, 0.25f), 0.3f, 2); asphalt.mainTextureScale = new Vector2(20f, 16f);
            var paving = Mats.Lit(Color.white, 0.1f); paving.mainTexture = Tiles(new Color(0.7f, 0.7f, 0.68f), new Color(0.64f, 0.64f, 0.62f), 4); paving.mainTextureScale = new Vector2(40f, 2f);
            var paint = Mats.Lit(new Color(0.95f, 0.95f, 0.92f), 0.2f); var yellow = Mats.Lit(new Color(0.95f, 0.8f, 0.1f), 0.2f);
            var grassM = Mats.Lit(Color.white, 0.05f); grassM.mainTexture = Noise(new Color(0.25f, 0.42f, 0.15f), new Color(0.35f, 0.5f, 0.2f), 0.08f, 6); grassM.mainTextureScale = new Vector2(10f, 10f);
            // ground pieces: grass all around, pavement strip, car park, road
            B(new Vector3(0, -0.5f, -16f), new Vector3(88f, 1f, 64f), grassM, true, "Floor").transform.position = new Vector3(0, -0.52f, -16f);
            B(new Vector3(0, 0.06f, -18.5f), new Vector3(60f, 0.12f, 5f), paving, true, "Floor pavement");
            B(new Vector3(0, -0.495f, -28f), new Vector3(60f, 1f, 14f), asphalt, true, "Floor carpark");
            B(new Vector3(0, -0.495f, -40f), new Vector3(88f, 1f, 9f), asphalt, true, "Floor road");
            // parking bays, arrows, road markings
            for (int i = -8; i <= 8; i++)
            {
                B(new Vector3(i * 3f, 0.012f, -24f), new Vector3(0.12f, 0.01f, 5f), paint, false, "bay line");
                B(new Vector3(i * 3f, 0.012f, -32f), new Vector3(0.12f, 0.01f, 5f), paint, false, "bay line");
            }
            B(new Vector3(0, 0.012f, -28f), new Vector3(52f, 0.01f, 0.12f), yellow, false, "aisle line");
            for (int i = -10; i <= 10; i++) B(new Vector3(i * 4f, 0.012f, -40f), new Vector3(2f, 0.01f, 0.15f), paint, false, "lane dash");
            B(new Vector3(0, 0.012f, -36.2f), new Vector3(88f, 0.01f, 0.12f), paint, false, "road edge");
            B(new Vector3(0, 0.012f, -43.8f), new Vector3(88f, 0.01f, 0.12f), paint, false, "road edge");
            B(new Vector3(0, 0.07f, -35.6f), new Vector3(60f, 0.14f, 0.3f), Mats.Lit(new Color(0.6f, 0.6f, 0.58f), 0.1f), true, "curb");
            for (int i = 0; i < 6; i++) B(new Vector3(-2f + i * 0.7f, 0.012f, -38f), new Vector3(0.4f, 0.01f, 3.5f), paint, false, "crossing");
            // street lights
            var pole = Mats.Lit(new Color(0.3f, 0.32f, 0.34f), 0.6f);
            for (int i = -2; i <= 2; i++)
            {
                var lp = new Vector3(i * 12f, 0, -35f);
                P(PrimitiveType.Cylinder, lp + new Vector3(0, 3.5f, 0), new Vector3(0.18f, 3.5f, 0.18f), Quaternion.identity, pole, true);
                B(lp + new Vector3(0, 7f, 0.8f), new Vector3(0.12f, 0.12f, 1.8f), pole, false, "arm");
                B(lp + new Vector3(0, 6.92f, 1.6f), new Vector3(0.5f, 0.1f, 0.8f), Glow(new Color(1f, 0.95f, 0.85f, 1f)), false, "lamp head");
            }
            // trolley shelter and a bin
            B(new Vector3(18f, 1.1f, -20f), new Vector3(4f, 0.08f, 1.6f), pole, false, "shelter roof");
            for (int i = 0; i < 4; i++) P(PrimitiveType.Cylinder, new Vector3(16.2f + (i % 2) * 3.6f, 0.55f, -20.7f + (i / 2) * 1.4f), new Vector3(0.06f, 0.55f, 0.06f), Quaternion.identity, pole, true);
            P(PrimitiveType.Cylinder, new Vector3(-6f, 0.5f, -17.5f), new Vector3(0.6f, 0.5f, 0.6f), Quaternion.identity, Mats.Lit(new Color(0.1f, 0.35f, 0.15f), 0.3f), true).name = "bin";
            // boundary of the map
            var inv = Mats.Decal(Texture2D.whiteTexture, new Color(0, 0, 0, 0), 3000);
            void Wall(Vector3 c, Vector3 sz) { var w = B(c, sz, inv, true, "boundary"); w.GetComponent<Renderer>().enabled = false; }
            Wall(new Vector3(0, 3f, -HZ), new Vector3(HX * 2f, 6f, 0.5f));
            Wall(new Vector3(HX, 3f, -16f), new Vector3(0.5f, 6f, 64f)); Wall(new Vector3(-HX, 3f, -16f), new Vector3(0.5f, 6f, 64f));
            Wall(new Vector3(0, 3f, 16.5f), new Vector3(HX * 2f, 6f, 0.5f));
            // parked cars, a few with people standing by them and one with a driver in the seat
            Color[] paintC = { new Color(0.7f, 0.08f, 0.08f), new Color(0.1f, 0.2f, 0.5f), new Color(0.85f, 0.85f, 0.85f), new Color(0.08f, 0.08f, 0.09f), new Color(0.45f, 0.47f, 0.5f), new Color(0.15f, 0.35f, 0.2f), new Color(0.85f, 0.65f, 0.15f) };
            var rngC = new System.Random(5);
            int[] bays = { -7, -5, -4, -1, 2, 3, 6, 7 };
            for (int i = 0; i < bays.Length; i++)
            {
                bool north = i % 2 == 0;
                var pos = new Vector3(bays[i] * 3f + 1.5f, 0, north ? -24.2f : -31.8f);
                var rot = Quaternion.Euler(0, north ? 0f : 180f, 0);
                Car(pos, rot, paintC[rngC.Next(paintC.Length)]);
                if (i == 1 || i == 4) { Drivers.Add(pos + rot * new Vector3(-0.38f, 0.32f, 0.1f)); DriverRot.Add(rot * Quaternion.Euler(0, 180f, 0)); }
                if (i % 3 == 0) CarPeople.Add(pos + rot * new Vector3(1.6f, 0, 0.4f));
            }
            CarPeople.Add(new Vector3(10f, 0.12f, -18.5f)); CarPeople.Add(new Vector3(-12f, 0.12f, -19f));
        }

        // a simple saloon: painted body with bonnet and boot, glass cabin, roof, wheels, lights, mirrors, number plates.
        // Only the chassis and roof collide, so a person can sit inside and a body can slump in the seat.
        static void Car(Vector3 pos, Quaternion rot, Color colour)
        {
            var car = new GameObject("CAR").transform; car.SetPositionAndRotation(pos, rot);
            var body = Mats.Lit(colour, 0.85f);
            var glassC = Mats.Decal(Texture2D.whiteTexture, new Color(0.25f, 0.32f, 0.38f, 0.45f), 3004);
            var tyre = Mats.Lit(new Color(0.05f, 0.05f, 0.05f), 0.2f); var rim = Mats.Lit(new Color(0.7f, 0.7f, 0.72f), 0.9f);
            var seat = Mats.Lit(new Color(0.12f, 0.12f, 0.13f), 0.2f);
            // the car faces +z (front at +z)
            B(new Vector3(0, 0.45f, 0), new Vector3(1.8f, 0.5f, 4.4f), body, false, "body", car);
            B(new Vector3(0, 0.3f, 0), new Vector3(1.7f, 0.25f, 4.2f), body, true, "COVER CAR CHASSIS", car);
            B(new Vector3(0, 0.95f, -0.25f), new Vector3(1.66f, 0.06f, 2.2f), body, false, "beltline", car);
            B(new Vector3(0, 1.38f, -0.35f), new Vector3(1.55f, 0.06f, 1.7f), body, true, "roof", car);
            B(new Vector3(0, 1.16f, 0.72f), new Vector3(1.5f, 0.45f, 0.04f), glassC, false, "windscreen", car).transform.localRotation = Quaternion.Euler(-35f, 0, 0);
            B(new Vector3(0, 1.16f, -1.38f), new Vector3(1.5f, 0.42f, 0.04f), glassC, false, "rear window", car).transform.localRotation = Quaternion.Euler(30f, 0, 0);
            for (int s2 = -1; s2 <= 1; s2 += 2) B(new Vector3(s2 * 0.78f, 1.16f, -0.35f), new Vector3(0.03f, 0.42f, 1.7f), glassC, false, "side window", car);
            for (int s2 = -1; s2 <= 1; s2 += 2) for (int f = -1; f <= 1; f += 2)
            {
                P(PrimitiveType.Cylinder, new Vector3(s2 * 0.82f, 0.33f, f * 1.35f), new Vector3(0.66f, 0.12f, 0.66f), Quaternion.Euler(0, 0, 90), tyre, false, car);
                P(PrimitiveType.Cylinder, new Vector3(s2 * 0.9f, 0.33f, f * 1.35f), new Vector3(0.38f, 0.05f, 0.38f), Quaternion.Euler(0, 0, 90), rim, false, car);
            }
            B(new Vector3(-0.55f, 0.72f, 2.21f), new Vector3(0.35f, 0.14f, 0.02f), Glow(new Color(1f, 1f, 0.9f, 1f)), false, "headlight", car);
            B(new Vector3(0.55f, 0.72f, 2.21f), new Vector3(0.35f, 0.14f, 0.02f), Glow(new Color(1f, 1f, 0.9f, 1f)), false, "headlight", car);
            B(new Vector3(-0.6f, 0.75f, -2.21f), new Vector3(0.3f, 0.12f, 0.02f), Glow(new Color(0.8f, 0.05f, 0.05f, 1f)), false, "taillight", car);
            B(new Vector3(0.6f, 0.75f, -2.21f), new Vector3(0.3f, 0.12f, 0.02f), Glow(new Color(0.8f, 0.05f, 0.05f, 1f)), false, "taillight", car);
            B(new Vector3(0, 0.5f, 2.21f), new Vector3(0.5f, 0.11f, 0.02f), Mats.Lit(new Color(0.95f, 0.95f, 0.9f), 0.3f), false, "plate", car);
            B(new Vector3(0, 0.5f, -2.21f), new Vector3(0.5f, 0.11f, 0.02f), Mats.Lit(new Color(0.95f, 0.95f, 0.9f), 0.3f), false, "plate", car);
            for (int s2 = -1; s2 <= 1; s2 += 2) B(new Vector3(s2 * 0.98f, 1.0f, 0.6f), new Vector3(0.18f, 0.1f, 0.06f), body, false, "mirror", car);
            // seats and steering wheel
            for (int s2 = -1; s2 <= 1; s2 += 2)
            {
                B(new Vector3(s2 * 0.38f, 0.62f, 0.1f), new Vector3(0.5f, 0.12f, 0.5f), seat, false, "seat", car);
                B(new Vector3(s2 * 0.38f, 0.95f, -0.18f), new Vector3(0.5f, 0.6f, 0.1f), seat, false, "seat back", car);
            }
            B(new Vector3(0, 0.62f, -0.75f), new Vector3(1.3f, 0.12f, 0.5f), seat, false, "rear seat", car);
            P(PrimitiveType.Cylinder, new Vector3(-0.38f, 0.98f, 0.5f), new Vector3(0.36f, 0.015f, 0.36f), Quaternion.Euler(60f, 0, 0), seat, false, car);
            B(new Vector3(0, 0.88f, 0.75f), new Vector3(1.55f, 0.15f, 0.35f), seat, false, "dashboard", car);
            AddCover(pos, rot * Vector3.right, rot * Vector3.forward);
        }

        // ================= HALLOWEEN NIGHT =================
        static void BuildHalloween()
        {
            HX = 30f; HZ = 30f;
            Sky = new Color(0.05f, 0.04f, 0.1f);
            SpawnArea = new Rect(-20f, -20f, 40f, 30f);
            PlayerStart = new Vector3(0, 0.05f, -24f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.2f, 0.18f, 0.3f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.12f, 0.08f, 0.18f); RenderSettings.fogDensity = 0.025f;
            var moon = new GameObject("Moonlight").AddComponent<Light>();
            moon.type = LightType.Directional; moon.intensity = 0.45f; moon.color = new Color(0.6f, 0.7f, 1f);
            moon.shadows = LightShadows.Soft; moon.shadowStrength = 0.8f; moon.shadowBias = 0.08f; moon.shadowNormalBias = 0.6f;
            moon.transform.rotation = Quaternion.Euler(35f, 150f, 0);
            QualitySettings.shadowDistance = 40f;
            P(PrimitiveType.Sphere, new Vector3(-60f, 55f, 120f), Vector3.one * 14f, Quaternion.identity, Glow(new Color(0.95f, 0.95f, 0.85f, 1f)));

            plat = Mats.Lit(new Color(0.35f, 0.33f, 0.32f), 0.1f); orange = Mats.Lit(new Color(1f, 0.45f, 0.05f), 0.2f);
            wall = Mats.Lit(new Color(0.2f, 0.17f, 0.15f), 0.1f);
            hazard = Mats.Lit(Color.white, 0.2f); hazard.mainTexture = HazardTex();
            var grass = Mats.Lit(Color.white, 0.05f); grass.mainTexture = Noise(new Color(0.08f, 0.14f, 0.06f), new Color(0.16f, 0.2f, 0.08f), 0.06f, 4);
            grass.mainTextureScale = new Vector2(12f, 12f);
            B(new Vector3(0, -0.5f, 0), new Vector3(HX * 2f, 1f, HZ * 2f), grass, true, "Floor");
            var dirt = Mats.Lit(Color.white, 0.05f); dirt.mainTexture = Noise(new Color(0.22f, 0.16f, 0.1f), new Color(0.3f, 0.22f, 0.14f), 0.08f, 9);
            B(new Vector3(0, 0.005f, -5f), new Vector3(3f, 0.01f, 40f), dirt, false, "path");
            B(new Vector3(-10f, 0.006f, 2f), new Vector3(14f, 0.01f, 2f), dirt, false, "path");
            // invisible boundary + a crooked wooden fence
            var wood = Mats.Lit(new Color(0.25f, 0.17f, 0.1f), 0.1f);
            for (int s = 0; s < 4; s++)
            {
                bool zx = s < 2; float sg = s % 2 == 0 ? 1f : -1f;
                B(zx ? new Vector3(0, 1.5f, sg * (HZ + 0.25f)) : new Vector3(sg * (HX + 0.25f), 1.5f, 0), zx ? new Vector3(HX * 2f, 3f, 0.5f) : new Vector3(0.5f, 3f, HZ * 2f), wood);
            }
            // ---- the haunted mansion: three storeys, two wings, a central tower, crooked roofs, lit and broken windows
            var house = new GameObject("HAUNTED MANSION").transform; house.position = new Vector3(2f, 0, 22f);
            var planks = Mats.Lit(new Color(0.17f, 0.14f, 0.14f), 0.1f);
            var trim = Mats.Lit(new Color(0.32f, 0.3f, 0.3f), 0.15f);
            var roof = Mats.Lit(new Color(0.07f, 0.06f, 0.09f), 0.15f);
            var win = Glow(new Color(1f, 0.6f, 0.15f, 1f));
            var winDim = Glow(new Color(0.45f, 0.25f, 0.6f, 1f));
            var dark = Mats.Lit(new Color(0.02f, 0.02f, 0.03f), 0.6f);
            // main block + wings
            B(new Vector3(0, 5.5f, 0), new Vector3(14f, 11f, 9f), planks, true, "TOWER MANSION", house);
            B(new Vector3(-11f, 4f, 1f), new Vector3(8f, 8f, 8f), planks, true, "TOWER WING L", house);
            B(new Vector3(11f, 4f, 1f), new Vector3(8f, 8f, 8f), planks, true, "TOWER WING R", house);
            // pitched roofs (two slabs each) and ridge
            void Roof(Vector3 c, float w, float d, float h)
            {
                float slope = Mathf.Atan2(h, d / 2f) * Mathf.Rad2Deg, l = Mathf.Sqrt(h * h + d * d / 4f) + 0.4f;
                B(c + new Vector3(0, h / 2f, -d / 4f), new Vector3(w + 0.6f, 0.25f, l), roof, false, "roof", house).transform.localRotation = Quaternion.Euler(slope, 0, 0);
                B(c + new Vector3(0, h / 2f, d / 4f), new Vector3(w + 0.6f, 0.25f, l), roof, false, "roof", house).transform.localRotation = Quaternion.Euler(-slope, 0, 0);
            }
            Roof(new Vector3(0, 11f, 0), 14f, 9f, 4.5f);
            Roof(new Vector3(-11f, 8f, 1f), 8f, 8f, 3.5f);
            Roof(new Vector3(11f, 8f, 1f), 8f, 8f, 3.5f);
            // central tower with a spire
            B(new Vector3(0, 9f, -4.8f), new Vector3(4f, 18f, 4f), planks, true, "TOWER CENTRAL", house);
            B(new Vector3(0, 18.4f, -4.8f), new Vector3(4.6f, 0.4f, 4.6f), trim, false, "tower ledge", house);
            for (int i = 0; i < 4; i++) B(new Vector3(0, 19.6f + i * 1.1f, -4.8f), new Vector3(3.6f - i * 0.9f, 1.1f, 3.6f - i * 0.9f), roof, false, "spire", house).transform.localRotation = Quaternion.Euler(0, 45, 0);
            B(new Vector3(0, 24.4f, -4.8f), new Vector3(0.08f, 1.6f, 0.08f), dark, false, "spike", house);
            B(new Vector3(0, 15.5f, -6.82f), new Vector3(1.6f, 2.2f, 0.06f), win, false, "tower window", house);
            // chimneys
            B(new Vector3(-4.5f, 14f, 1.5f), new Vector3(1.2f, 4f, 1.2f), trim, false, "chimney", house);
            B(new Vector3(5f, 14.5f, 2f), new Vector3(1f, 5f, 1f), trim, false, "chimney", house);
            // windows: three rows on the main block, two on each wing; some lit, some dark, some boarded
            var rngH = new System.Random(77);
            void Window(Vector3 c)
            {
                int k = rngH.Next(5);
                B(c, new Vector3(1.1f, 1.6f, 0.06f), k < 2 ? win : k == 2 ? winDim : dark, false, "window", house);
                B(c + new Vector3(0, 0.9f, -0.02f), new Vector3(1.4f, 0.18f, 0.12f), trim, false, "lintel", house);
                B(c + new Vector3(0, -0.9f, -0.02f), new Vector3(1.4f, 0.12f, 0.18f), trim, false, "sill", house);
                if (k == 4) { B(c + new Vector3(0, 0.2f, -0.05f), new Vector3(1.3f, 0.15f, 0.04f), planks, false, "board", house).transform.localRotation = Quaternion.Euler(0, 0, 20); B(c + new Vector3(0, -0.3f, -0.05f), new Vector3(1.3f, 0.15f, 0.04f), planks, false, "board", house).transform.localRotation = Quaternion.Euler(0, 0, -15); }
            }
            for (int row = 0; row < 3; row++) for (int col = 0; col < 5; col++) if (!(row == 0 && col == 2)) Window(new Vector3(-5.6f + col * 2.8f, 2f + row * 3.4f, -4.53f));
            for (int w2 = -1; w2 <= 1; w2 += 2) for (int row = 0; row < 2; row++) for (int col = 0; col < 2; col++) Window(new Vector3(w2 * 11f - 2f + col * 4f, 2f + row * 3.4f, -3.03f));
            // big double door, porch with columns, stairs, balcony above
            B(new Vector3(0, 1.7f, -6.85f), new Vector3(2.2f, 3.4f, 0.1f), Mats.Lit(new Color(0.12f, 0.05f, 0.03f), 0.3f), false, "door", house);
            B(new Vector3(0, 3.6f, -6.86f), new Vector3(2.4f, 0.4f, 0.12f), trim, false, "door arch", house);
            B(new Vector3(0, 0.3f, -8f), new Vector3(8f, 0.6f, 2.4f), trim, true, "porch", house);
            for (int i = 0; i < 4; i++) B(new Vector3(0, 0.1f + i * 0.15f - 0.3f, -9.4f - i * 0.3f + 0.9f), new Vector3(4f, 0.3f, 0.3f), trim, true, "stair", house);
            for (int i = 0; i < 4; i++) P(PrimitiveType.Cylinder, new Vector3(-3.4f + i * 2.27f, 2.6f, -8.9f), new Vector3(0.35f, 2.3f, 0.35f), Quaternion.identity, trim, true, house);
            B(new Vector3(0, 5f, -8.4f), new Vector3(8.4f, 0.3f, 3.2f), trim, true, "balcony", house);
            for (float x = -4f; x <= 4f; x += 0.4f) B(new Vector3(x, 5.55f, -9.9f), new Vector3(0.06f, 0.8f, 0.06f), dark, false, "baluster", house);
            B(new Vector3(0, 5.95f, -9.9f), new Vector3(8.4f, 0.08f, 0.1f), dark, false, "rail", house);
            var hl = new GameObject("porch light").AddComponent<Light>(); hl.type = LightType.Point; hl.range = 14f; hl.intensity = 1.8f; hl.color = new Color(1f, 0.55f, 0.15f);
            hl.transform.position = house.position + new Vector3(0, 3.5f, -9f); hl.gameObject.AddComponent<Flicker>();
            var tl = new GameObject("tower light").AddComponent<Light>(); tl.type = LightType.Point; tl.range = 10f; tl.intensity = 1.2f; tl.color = new Color(0.7f, 0.3f, 1f);
            tl.transform.position = house.position + new Vector3(0, 15.5f, -7.5f); tl.gameObject.AddComponent<Flicker>();
            AddCover(house.position + new Vector3(-8f, 0, -5.2f), Vector3.back, Vector3.right);
            AddCover(house.position + new Vector3(8f, 0, -5.2f), Vector3.back, Vector3.right);
            Text("ENTER IF YOU DARE", house.position + new Vector3(0, 4.1f, -6.95f), Quaternion.identity, 0.28f, new Color(0.9f, 0.2f, 0.1f));
            // tall iron fence and gate in front of the mansion
            var ironF = Mats.Lit(new Color(0.05f, 0.05f, 0.06f), 0.6f);
            for (float x = -16f; x <= 18f; x += 0.45f)
            {
                if (Mathf.Abs(x - 2f) < 2f) continue;   // gate opening
                P(PrimitiveType.Cylinder, new Vector3(x, 1.1f, 9.5f), new Vector3(0.05f, 1.1f, 0.05f), Quaternion.identity, ironF);
                P(PrimitiveType.Cube, new Vector3(x, 2.25f, 9.5f), new Vector3(0.08f, 0.12f, 0.08f), Quaternion.Euler(0, 0, 45), ironF);
            }
            B(new Vector3(1f, 2f, 9.5f), new Vector3(34f, 0.05f, 0.05f), ironF, false, "fence rail");
            B(new Vector3(1f, 0.5f, 9.5f), new Vector3(34f, 0.05f, 0.05f), ironF, false, "fence rail");
            for (int g = -1; g <= 1; g += 2)
            {
                B(new Vector3(2f + g * 2.2f, 1.6f, 9.5f), new Vector3(0.6f, 3.2f, 0.6f), trim, true, "gate pillar");
                P(PrimitiveType.Sphere, new Vector3(2f + g * 2.2f, 3.45f, 9.5f), Vector3.one * 0.55f, Quaternion.identity, trim);
                B(new Vector3(2f + g * 1.4f, 1.3f, 9.2f), new Vector3(1.6f, 2.4f, 0.05f), ironF, false, "gate").transform.localRotation = Quaternion.Euler(0, g * 35f, 0);
            }
            B(new Vector3(-7f, 1.1f, 9.5f), new Vector3(18f, 2.2f, 0.1f), Mats.Decal(Texture2D.whiteTexture, new Color(0, 0, 0, 0), 3000), true, "fence collider").GetComponent<Renderer>().enabled = false;
            B(new Vector3(11f, 1.1f, 9.5f), new Vector3(14f, 2.2f, 0.1f), Mats.Decal(Texture2D.whiteTexture, new Color(0, 0, 0, 0), 3000), true, "fence collider").GetComponent<Renderer>().enabled = false;
            // graveyard: tombstones (cover), crosses, an open grave, iron fence
            var stone = Mats.Lit(new Color(0.42f, 0.42f, 0.45f), 0.1f);
            var moss = Mats.Lit(new Color(0.25f, 0.3f, 0.2f), 0.05f);
            var rng = new System.Random(31);
            for (int r = 0; r < 4; r++) for (int c = 0; c < 5; c++)
            {
                float x = -20f + c * 2.6f + (float)rng.NextDouble() * 0.4f, z = -6f + r * 3.2f;
                var tilt = Quaternion.Euler((float)rng.NextDouble() * 10f - 5f, (float)rng.NextDouble() * 16f - 8f, (float)rng.NextDouble() * 10f - 5f);
                if ((r + c) % 3 == 0)
                {
                    var cr = new GameObject("COVER CROSS").transform; cr.position = new Vector3(x, 0, z); cr.rotation = tilt;
                    B(new Vector3(0, 0.75f, 0), new Vector3(0.16f, 1.5f, 0.16f), stone, true, "COVER CROSS", cr);
                    B(new Vector3(0, 1.1f, 0), new Vector3(0.7f, 0.16f, 0.16f), stone, false, "cross arm", cr);
                }
                else
                {
                    var ts = new GameObject("COVER TOMBSTONE").transform; ts.position = new Vector3(x, 0, z); ts.rotation = tilt;
                    B(new Vector3(0, 0.45f, 0), new Vector3(0.8f, 0.9f, 0.2f), stone, true, "COVER TOMBSTONE", ts);
                    P(PrimitiveType.Cylinder, new Vector3(0, 0.9f, 0), new Vector3(0.8f, 0.1f, 0.8f), Quaternion.Euler(90, 0, 0), stone, false, ts).transform.localScale = new Vector3(0.8f, 0.1f, 0.8f);
                    B(new Vector3(0, 0.05f, -0.6f), new Vector3(0.9f, 0.1f, 1.6f), moss, false, "grave", ts);
                    Text("R.I.P", new Vector3(x, 0.6f, z - 0.11f), tilt, 0.12f, new Color(0.15f, 0.15f, 0.15f));
                    AddCover(new Vector3(x, 0, z), tilt * Vector3.forward, tilt * Vector3.right);
                }
            }
            // open grave with a coffin
            B(new Vector3(-14f, 0.3f, 8f), new Vector3(1.2f, 0.6f, 2.4f), dirt, true, "COVER DIRT MOUND");
            var coffinM = Mats.Lit(new Color(0.15f, 0.08f, 0.05f), 0.4f);
            B(new Vector3(-12.4f, 0.25f, 8f), new Vector3(0.7f, 0.5f, 2f), coffinM, true, "COVER COFFIN");
            B(new Vector3(-11.8f, 0.6f, 8f), new Vector3(0.7f, 0.08f, 2f), coffinM, false, "coffin lid").transform.rotation = Quaternion.Euler(0, 0, 40f);
            AddCover(new Vector3(-12.4f, 0, 8f), Vector3.right, Vector3.forward);
            var iron = Mats.Lit(new Color(0.05f, 0.05f, 0.06f), 0.6f);
            for (float x = -22f; x <= -7f; x += 0.5f)
            {
                P(PrimitiveType.Cylinder, new Vector3(x, 0.7f, -8.5f), new Vector3(0.05f, 0.7f, 0.05f), Quaternion.identity, iron);
                P(PrimitiveType.Cylinder, new Vector3(x, 0.7f, 11f), new Vector3(0.05f, 0.7f, 0.05f), Quaternion.identity, iron);
            }
            B(new Vector3(-14.5f, 1.2f, -8.5f), new Vector3(15f, 0.06f, 0.06f), iron, false, "rail");
            B(new Vector3(-14.5f, 1.2f, 11f), new Vector3(15f, 0.06f, 0.06f), iron, false, "rail");
            B(new Vector3(-14.5f, 0.7f, -8.5f), new Vector3(15f, 1.4f, 0.1f), Mats.Decal(Texture2D.whiteTexture, new Color(0, 0, 0, 0), 3000), true, "fence collider").GetComponent<Renderer>().enabled = false;
            // a creepy forest: gnarled trees with many forking branches and a few clumps of dark, dying leaves
            var bark = Mats.Lit(new Color(0.1f, 0.08f, 0.07f), 0.05f);
            var leafM = new[] { Mats.Lit(new Color(0.12f, 0.1f, 0.05f), 0.05f), Mats.Lit(new Color(0.25f, 0.12f, 0.04f), 0.05f), Mats.Lit(new Color(0.08f, 0.1f, 0.05f), 0.05f) };
            int leafCount = 0;
            void Branch(Transform root, Vector3 from, Vector3 dir, float len, float rad, int depth)
            {
                Vector3 to = from + dir * len;
                var q = Quaternion.FromToRotation(Vector3.up, dir);
                P(PrimitiveType.Cylinder, (from + to) / 2f, new Vector3(rad * 2f, len / 2f, rad * 2f), q, bark, false, root);
                if (depth <= 0)
                {
                    if (leafCount < 500 && rng.NextDouble() < 0.55)
                    {
                        leafCount++;
                        float ls = 0.4f + (float)rng.NextDouble() * 0.6f;
                        P(PrimitiveType.Sphere, to, new Vector3(ls, ls * 0.55f, ls), Random.rotation, leafM[rng.Next(leafM.Length)], false, root);
                    }
                    return;
                }
                int n = 2 + rng.Next(2);
                for (int i = 0; i < n; i++)
                {
                    // gnarled: random twist, droop with depth
                    var d = Quaternion.AngleAxis((float)rng.NextDouble() * 360f, dir) * Quaternion.AngleAxis(25f + (float)rng.NextDouble() * 35f, Vector3.Cross(dir, Vector3.right).normalized + Vector3.forward * 0.01f) * dir;
                    d = (d + Vector3.down * 0.1f * (3 - depth)).normalized;
                    Branch(root, to, d, len * (0.6f + (float)rng.NextDouble() * 0.15f), rad * 0.62f, depth - 1);
                }
            }
            var forest = new System.Collections.Generic.List<Vector3>
            { new Vector3(-24f, 0, -18f), new Vector3(-5f, 0, 14f), new Vector3(18f, 0, -15f), new Vector3(22f, 0, 8f), new Vector3(-24f, 0, 20f), new Vector3(12f, 0, -24f),
              new Vector3(-12f, 0, -22f), new Vector3(24f, 0, 24f), new Vector3(-26f, 0, 0f), new Vector3(-27f, 0, 10f), new Vector3(26f, 0, -2f), new Vector3(27f, 0, 15f),
              new Vector3(-18f, 0, 26f), new Vector3(-10f, 0, 29f), new Vector3(20f, 0, 29f), new Vector3(-26f, 0, -26f), new Vector3(26f, 0, -26f), new Vector3(6f, 0, -27f),
              new Vector3(-4f, 0, -27f), new Vector3(20f, 0, -8f), new Vector3(-20f, 0, 15f), new Vector3(9f, 0, 12f), new Vector3(-8f, 0, -15f), new Vector3(15f, 0, 18f) };
            foreach (var t in forest)
            {
                var tr = new GameObject("COVER TREE").transform; tr.position = t;
                float h = 3.5f + (float)rng.NextDouble() * 2.5f, rad = 0.22f + (float)rng.NextDouble() * 0.12f;
                var trunk = P(PrimitiveType.Cylinder, new Vector3(0, h / 2f, 0), new Vector3(rad * 2f, h / 2f, rad * 2f), Quaternion.Euler((float)rng.NextDouble() * 6f - 3f, 0, (float)rng.NextDouble() * 8f - 4f), bark, true, tr);
                trunk.name = "COVER TREE";
                // roots
                for (int r = 0; r < 4; r++) { var rq = Quaternion.Euler(0, r * 90f + (float)rng.NextDouble() * 40f, 0) * Quaternion.Euler(70f, 0, 0); P(PrimitiveType.Cylinder, rq * new Vector3(0, 0.45f, 0) + Vector3.up * 0.1f, new Vector3(rad, 0.45f, rad), rq, bark, false, tr); }
                for (int b2 = 0; b2 < 4; b2++)
                {
                    var d = Quaternion.Euler(0, b2 * 90f + (float)rng.NextDouble() * 50f, 0) * Quaternion.Euler(0, 0, -(35f + (float)rng.NextDouble() * 25f)) * Vector3.up;
                    Branch(tr, new Vector3(0, h * (0.65f + 0.3f * (float)rng.NextDouble()), 0), d, 1.4f + (float)rng.NextDouble() * 0.8f, rad * 0.55f, 3);
                }
                Branch(tr, new Vector3(0, h, 0), Vector3.up, 1.2f, rad * 0.5f, 2);
                AddCover(t, Vector3.forward, Vector3.right);
            }
            // low ground fog: soft round wisps lying just above the grass, slowly drifting
            var fogTex = new Texture2D(64, 64, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                float dx = (x - 31.5f) / 32f, dy = (y - 31.5f) / 32f, d = Mathf.Sqrt(dx * dx + dy * dy);
                float a2 = Mathf.Clamp01(1f - d); a2 = a2 * a2 * (0.7f + 0.3f * Mathf.PerlinNoise(x * 0.15f, y * 0.15f));
                fogTex.SetPixel(x, y, new Color(1, 1, 1, a2));
            }
            fogTex.Apply(true);
            var fogM = Mats.Decal(fogTex, new Color(0.6f, 0.55f, 0.75f, 0.22f), 3010);
            for (int i = 0; i < 45; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(q.GetComponent<Collider>());
                q.name = "fog";
                q.transform.position = new Vector3((float)rng.NextDouble() * 56f - 28f, 0.2f + (float)rng.NextDouble() * 0.6f, (float)rng.NextDouble() * 56f - 28f);
                q.transform.rotation = Quaternion.Euler(90f, rng.Next(360), 0);
                float fs = 7f + (float)rng.NextDouble() * 8f; q.transform.localScale = new Vector3(fs, fs, 1f);
                q.GetComponent<Renderer>().sharedMaterial = fogM; q.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                q.AddComponent<Drift>();
            }
            // crows on the fence and graves, candles on the graves, cobwebs on the porch
            var crow = Mats.Lit(new Color(0.02f, 0.02f, 0.03f), 0.3f);
            Vector3[] crows = { new Vector3(-12f, 1.3f, -8.5f), new Vector3(-18f, 1.3f, -8.5f), new Vector3(-17f, 0.95f, -2.8f), new Vector3(6f, 2.3f, 9.5f), new Vector3(-0.2f, 3.8f, 9.5f) };
            foreach (var c in crows)
            {
                var cr = new GameObject("crow").transform; cr.position = c; cr.rotation = Quaternion.Euler(0, rng.Next(360), 0);
                P(PrimitiveType.Sphere, new Vector3(0, 0.1f, 0), new Vector3(0.16f, 0.16f, 0.26f), Quaternion.identity, crow, false, cr);
                P(PrimitiveType.Sphere, new Vector3(0, 0.2f, 0.12f), Vector3.one * 0.1f, Quaternion.identity, crow, false, cr);
                P(PrimitiveType.Cube, new Vector3(0, 0.19f, 0.19f), new Vector3(0.02f, 0.02f, 0.07f), Quaternion.identity, Mats.Lit(new Color(0.3f, 0.25f, 0.1f), 0.2f), false, cr);
            }
            var wax = Mats.Lit(new Color(0.9f, 0.88f, 0.8f), 0.2f); var flame = Glow(new Color(1f, 0.8f, 0.3f, 1f));
            for (int i = 0; i < 10; i++)
            {
                var cp = new Vector3(-20f + (i % 5) * 2.6f + 0.3f, 0, -6.4f + (i / 5) * 6.4f);
                P(PrimitiveType.Cylinder, cp + new Vector3(0, 0.08f, 0), new Vector3(0.05f, 0.08f, 0.05f), Quaternion.identity, wax);
                P(PrimitiveType.Sphere, cp + new Vector3(0, 0.19f, 0), new Vector3(0.025f, 0.05f, 0.025f), Quaternion.identity, flame);
            }
            var web = Mats.Decal(Texture2D.whiteTexture, new Color(0.85f, 0.85f, 0.9f, 0.35f), 3011);
            for (int i = 0; i < 6; i++)
            {
                var wp = house.position + new Vector3(-3.4f + (i % 3) * 3.4f, 4.2f, -8.9f);
                for (int k = 0; k < 5; k++) B(wp + new Vector3(0.3f, -0.3f, 0), new Vector3(0.9f, 0.01f, 0.01f), web, false, "web").transform.rotation = Quaternion.Euler(0, 0, k * 36f);
            }
            // jack-o'-lanterns: squashed orange balls, stem, carved glowing face; some with a flickering candle light
            var pump = Mats.Lit(new Color(0.95f, 0.42f, 0.05f), 0.3f);
            var stem = Mats.Lit(new Color(0.2f, 0.3f, 0.1f), 0.1f);
            var face = Glow(new Color(1f, 0.75f, 0.2f, 1f));
            Vector3[] pumpkins = { new Vector3(-1.5f, 0, -12f), new Vector3(1.6f, 0, -10f), new Vector3(-1.8f, 0, -4f), new Vector3(1.8f, 0, 0f), new Vector3(2.2f, 0, 13f), new Vector3(-1.2f, 0, 14.5f),
                                   new Vector3(-8f, 0, 2.8f), new Vector3(-16f, 0, 1.2f), new Vector3(10f, 0, 4f), new Vector3(14f, 0, -6f), new Vector3(-20f, 0, -12f), new Vector3(6.5f, 0, 14f) };
            for (int i = 0; i < pumpkins.Length; i++)
            {
                var pk = new GameObject("pumpkin").transform; pk.position = pumpkins[i]; pk.rotation = Quaternion.Euler(0, rng.Next(360), 0);
                float sz = 0.45f + (float)rng.NextDouble() * 0.35f;
                P(PrimitiveType.Sphere, new Vector3(0, sz * 0.4f, 0), new Vector3(sz, sz * 0.75f, sz), Quaternion.identity, pump, true, pk);
                P(PrimitiveType.Cylinder, new Vector3(0, sz * 0.8f, 0), new Vector3(0.06f, 0.08f, 0.06f), Quaternion.Euler(10, 0, 8), stem, false, pk);
                B(new Vector3(-sz * 0.15f, sz * 0.5f, -sz * 0.47f), new Vector3(sz * 0.14f, sz * 0.12f, 0.02f), face, false, "eye", pk).transform.localRotation = Quaternion.Euler(0, 0, 45);
                B(new Vector3(sz * 0.15f, sz * 0.5f, -sz * 0.47f), new Vector3(sz * 0.14f, sz * 0.12f, 0.02f), face, false, "eye", pk).transform.localRotation = Quaternion.Euler(0, 0, 45);
                B(new Vector3(0, sz * 0.3f, -sz * 0.48f), new Vector3(sz * 0.42f, sz * 0.08f, 0.02f), face, false, "mouth", pk);
                if (i % 2 == 0)
                {
                    var l = new GameObject("candle").AddComponent<Light>(); l.type = LightType.Point; l.range = 5f; l.intensity = 1.4f; l.color = new Color(1f, 0.55f, 0.15f);
                    l.transform.SetParent(pk, false); l.transform.localPosition = new Vector3(0, sz * 0.5f, -sz * 0.7f); l.gameObject.AddComponent<Flicker>();
                }
            }
            // lantern posts along the path
            for (int i = 0; i < 4; i++)
            {
                var lp = new Vector3(i % 2 == 0 ? -2.3f : 2.3f, 0, -20f + i * 9f);
                P(PrimitiveType.Cylinder, lp + new Vector3(0, 1.3f, 0), new Vector3(0.1f, 1.3f, 0.1f), Quaternion.identity, iron, true);
                B(lp + new Vector3(0, 2.75f, 0), new Vector3(0.3f, 0.4f, 0.3f), Glow(new Color(1f, 0.85f, 0.5f, 1f)), false, "lantern");
                var l = new GameObject("lantern light").AddComponent<Light>(); l.type = LightType.Point; l.range = 9f; l.intensity = 1.2f; l.color = new Color(1f, 0.8f, 0.45f);
                l.transform.position = lp + new Vector3(0, 2.6f, 0); l.gameObject.AddComponent<Flicker>();
            }
            // scarecrow, hay bales, well
            var hay = Mats.Lit(new Color(0.75f, 0.62f, 0.28f), 0.05f);
            var sc = new GameObject("scarecrow").transform; sc.position = new Vector3(12f, 0, -10f);
            B(new Vector3(0, 1.2f, 0), new Vector3(0.12f, 2.4f, 0.12f), wood, true, "post", sc);
            B(new Vector3(0, 1.9f, 0), new Vector3(1.8f, 0.1f, 0.1f), wood, false, "arms", sc);
            B(new Vector3(0, 1.6f, 0), new Vector3(0.6f, 0.8f, 0.3f), Mats.Lit(new Color(0.35f, 0.2f, 0.15f), 0.05f), false, "shirt", sc);
            P(PrimitiveType.Sphere, new Vector3(0, 2.3f, 0), Vector3.one * 0.4f, Quaternion.identity, Mats.Lit(new Color(0.7f, 0.6f, 0.45f), 0.05f), false, sc);
            P(PrimitiveType.Cylinder, new Vector3(0, 2.52f, 0), new Vector3(0.7f, 0.02f, 0.7f), Quaternion.identity, Mats.Lit(new Color(0.12f, 0.1f, 0.08f), 0.05f), false, sc);
            P(PrimitiveType.Cylinder, new Vector3(0, 2.68f, 0), new Vector3(0.3f, 0.16f, 0.3f), Quaternion.identity, Mats.Lit(new Color(0.12f, 0.1f, 0.08f), 0.05f), false, sc);
            for (int i = 0; i < 5; i++)
            {
                var hp = new Vector3(8f + i * 1.6f, 0.45f, -14f + (i % 2) * 1.2f);
                B(hp, new Vector3(1.4f, 0.9f, 0.9f), hay, true, "COVER HAY");
                AddCover(hp - Vector3.up * 0.45f, Vector3.forward, Vector3.right);
            }
            P(PrimitiveType.Cylinder, new Vector3(18f, 0.5f, 2f), new Vector3(1.6f, 0.5f, 1.6f), Quaternion.identity, stone, true).name = "COVER WELL";
            B(new Vector3(18f, 1.8f, 2f), new Vector3(1.8f, 0.1f, 0.1f), wood, false, "well beam");
            B(new Vector3(17.2f, 1.2f, 2f), new Vector3(0.1f, 1.4f, 0.1f), wood, false, "well post");
            B(new Vector3(18.8f, 1.2f, 2f), new Vector3(0.1f, 1.4f, 0.1f), wood, false, "well post");
            AddCover(new Vector3(18f, 0, 2f), Vector3.forward, Vector3.right);
            // bats circling the house
            var batM = Mats.Lit(new Color(0.02f, 0.02f, 0.03f), 0.1f);
            for (int i = 0; i < 7; i++)
            {
                var bat = new GameObject("bat").transform;
                P(PrimitiveType.Sphere, Vector3.zero, new Vector3(0.12f, 0.1f, 0.18f), Quaternion.identity, batM, false, bat);
                P(PrimitiveType.Cube, new Vector3(-0.17f, 0, 0), new Vector3(0.3f, 0.01f, 0.14f), Quaternion.identity, batM, false, bat).name = "wingL";
                P(PrimitiveType.Cube, new Vector3(0.17f, 0, 0), new Vector3(0.3f, 0.01f, 0.14f), Quaternion.identity, batM, false, bat).name = "wingR";
                var b = bat.gameObject.AddComponent<Bat>(); b.center = new Vector3(2f, 15f, 20f); b.radius = 8f + i * 1.5f; b.phase = i * 0.9f;
            }
            Text("HAPPY HALLOWEEN", new Vector3(0, 0.02f, -21f), Quaternion.Euler(90, 0, 0), 0.9f, new Color(1f, 0.5f, 0.1f, 0.9f));
        }
    }

    // candle / lantern light that flickers like a real flame
    public class Flicker : MonoBehaviour
    {
        Light l; float baseI, seed;
        void Start() { l = GetComponent<Light>(); baseI = l.intensity; seed = Random.value * 10f; }
        void Update() { l.intensity = baseI * (0.75f + 0.35f * Mathf.PerlinNoise(Time.time * 6f, seed)); }
    }

    // a bat flapping in loose circles
    public class Bat : MonoBehaviour
    {
        public Vector3 center; public float radius = 6f, phase;
        Transform wl, wr;
        void Start() { wl = transform.Find("wingL"); wr = transform.Find("wingR"); }
        void Update()
        {
            float t = Time.time * 0.6f + phase;
            Vector3 p = center + new Vector3(Mathf.Cos(t) * radius, Mathf.Sin(t * 2.3f) * 1.2f, Mathf.Sin(t) * radius * 0.8f);
            Vector3 v = p - transform.position;
            transform.position = p;
            if (v.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(v);
            float f = Mathf.Sin(Time.time * 18f + phase) * 40f;
            if (wl != null) wl.localRotation = Quaternion.Euler(0, 0, f);
            if (wr != null) wr.localRotation = Quaternion.Euler(0, 0, -f);
        }
    }

    // slow drifting of ground fog wisps
    public class Drift : MonoBehaviour
    {
        Vector3 o; float sd;
        void Start() { o = transform.position; sd = Random.value * 100f; }
        void Update() { transform.position = o + new Vector3(Mathf.Sin(Time.time * 0.05f + sd) * 2f, 0, Mathf.Cos(Time.time * 0.04f + sd) * 2f); }
    }
}
