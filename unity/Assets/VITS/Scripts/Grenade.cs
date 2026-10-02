using UnityEngine;

namespace VITS
{
    // M67-style fragmentation grenade: thrown body that bounces and rolls, 4 s fuse from when the spoon flies,
    // then a blast (flash, fireball, shock wave, smoke column, scorch) and ~300 steel fragments
    public class Grenade : MonoBehaviour
    {
        public float fuse;
        Rigidbody rb; AudioSource au; float lastClink;
        static AudioClip boomNear, boomFar, clinkClip, ringClip, pinClip;
        static Material bodyM, darkM, spoonM;

        public static AudioClip Pin { get { Clips(); return pinClip; } }

        // the visual model (also used in the player's hand)
        public static Transform Model(Transform parent, bool withSpoon)
        {
            if (bodyM == null) { bodyM = Mats.Lit(new Color(0.2f, 0.25f, 0.14f), 0.35f); darkM = Mats.Lit(new Color(0.12f, 0.12f, 0.11f), 0.5f); spoonM = Mats.Lit(new Color(0.45f, 0.45f, 0.42f), 0.7f); }
            var root = new GameObject("GrenadeModel").transform; root.SetParent(parent, false);
            Mats.Vis(PrimitiveType.Sphere, root, Vector3.zero, new Vector3(0.062f, 0.068f, 0.062f), bodyM, true);
            // fuse head on top, spoon down the side, pin ring
            Mats.Vis(PrimitiveType.Cylinder, root, new Vector3(0, 0.038f, 0), new Vector3(0.024f, 0.012f, 0.024f), darkM, true);
            if (withSpoon)
            {
                Mats.Vis(PrimitiveType.Cube, root, new Vector3(0, 0.045f, 0.012f), new Vector3(0.012f, 0.004f, 0.028f), spoonM, false);
                Mats.Vis(PrimitiveType.Cube, root, new Vector3(0, 0.012f, 0.033f), new Vector3(0.012f, 0.06f, 0.004f), spoonM, false).transform.localRotation = Quaternion.Euler(-12f, 0, 0);
                var ring = Mats.Vis(PrimitiveType.Cylinder, root, new Vector3(0.022f, 0.042f, 0), new Vector3(0.026f, 0.0015f, 0.026f), spoonM, false);
                ring.name = "PinRing"; ring.transform.localRotation = Quaternion.Euler(0, 0, 90f);
            }
            return root;
        }

        public static void Throw(Vector3 pos, Vector3 vel, float fuseLeft)
        {
            Clips();
            var g = new GameObject("Grenade");
            g.transform.position = pos; g.transform.rotation = Random.rotation;
            Model(g.transform, false);
            var col = g.AddComponent<SphereCollider>(); col.radius = 0.034f;
            col.sharedMaterial = new PhysicsMaterial { bounciness = 0.3f, dynamicFriction = 0.6f, staticFriction = 0.7f };
            var rb = g.AddComponent<Rigidbody>(); rb.mass = 0.4f; rb.linearVelocity = vel; rb.angularVelocity = Random.insideUnitSphere * 15f;
            rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.angularDamping = 1.5f;
            var gr = g.AddComponent<Grenade>(); gr.fuse = fuseLeft; gr.rb = rb;
            gr.au = g.AddComponent<AudioSource>(); gr.au.spatialBlend = 1f; gr.au.minDistance = 1.5f; gr.au.maxDistance = 30f;
            // the spoon flips off as it leaves the hand
            var sp = Mats.Vis(PrimitiveType.Cube, null, pos + Vector3.up * 0.05f, new Vector3(0.012f, 0.004f, 0.07f), spoonM, false);
            var sc = sp.AddComponent<BoxCollider>(); _ = sc;
            var srb = sp.AddComponent<Rigidbody>(); srb.mass = 0.02f; srb.linearVelocity = vel * 0.6f + Vector3.up * 2.5f + Random.insideUnitSphere; srb.angularVelocity = Random.insideUnitSphere * 30f;
            Destroy(sp, 20f);
        }

        void OnCollisionEnter(Collision c)
        {
            float v = c.relativeVelocity.magnitude;
            if (v > 0.6f && Time.time > lastClink + 0.06f) { lastClink = Time.time; au.pitch = Random.Range(0.8f, 1.2f); au.PlayOneShot(clinkClip, Mathf.Clamp01(v / 8f)); }
        }

        void Update()
        {
            fuse -= Time.deltaTime;
            if (fuse <= 0f) { Explode(transform.position); Destroy(gameObject); }
        }

        // ---------- the blast
        public static void Explode(Vector3 c)
        {
            Clips();
            const int WORLD = ~((1 << 2) | (1 << Mannequin.LayerWalk) | (1 << Mannequin.LayerRag));
            Vector3 up = Vector3.up;
            if (Physics.Raycast(c + Vector3.up * 0.2f, Vector3.down, out RaycastHit gh, 0.6f, WORLD, QueryTriggerInteraction.Ignore)) up = gh.normal;

            // sound: crack and boom close by, a duller thump far off; it arrives later with distance
            var cam = Camera.main; float dCam = cam != null ? Vector3.Distance(cam.transform.position, c) : 10f;
            var sgo = new GameObject("BoomSound"); sgo.transform.position = c;
            var s = sgo.AddComponent<AudioSource>(); s.spatialBlend = 0.85f; s.minDistance = 6f; s.maxDistance = 400f; s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.clip = dCam < 30f ? boomNear : boomFar; s.PlayDelayed(dCam / 343f); Destroy(sgo, 6f);

            // ears ringing and a hard camera kick when it goes off near you
            if (dCam < 14f)
            {
                float k = 1f - dCam / 14f;
                Shake.Add(k * 1.2f);
                if (dCam < 7f) { var r = new GameObject("Ringing").AddComponent<AudioSource>(); r.spatialBlend = 0f; r.clip = ringClip; r.volume = k * 0.35f; r.Play(); Destroy(r.gameObject, 6f); Deafen.Hit(k); }
            }

            // light: a white flash that turns orange and dies
            var lg = new GameObject("BlastLight"); lg.transform.position = c + up * 0.4f;
            var L = lg.AddComponent<Light>(); L.type = LightType.Point; L.range = 18f; L.color = new Color(1f, 0.75f, 0.45f); L.intensity = 40f; L.shadows = LightShadows.Hard;
            lg.AddComponent<Fade>().Set(0.35f, 40f);

            Fx.Blast(c, up);

            // everyone hears it; anyone near enough is thrown, torn or killed; fragments fly in every direction
            foreach (var m in Mannequin.All.ToArray())
            {
                if (m == null) continue;
                float d = Vector3.Distance(m.transform.position + Vector3.up * 0.9f, c);
                if (d < 7f && !Physics.Linecast(c + up * 0.15f, m.transform.position + Vector3.up * 0.9f, WORLD, QueryTriggerInteraction.Ignore)) m.Blast(c, d);
                else if (!m.dead && d < 120f) m.Scare(c, Mathf.Lerp(1f, 0.35f, d / 120f), "EXPLOSION");
            }
            int frags = 0;
            for (int i = 0; i < 320 && frags < 22; i++)
            {
                Vector3 dir = Random.onUnitSphere; if (Vector3.Dot(dir, up) < -0.1f) dir = Vector3.Reflect(dir, up);
                Vector3 o = c + up * 0.08f; float max = 25f;
                if (Physics.Raycast(o, dir, out RaycastHit wh, max, WORLD, QueryTriggerInteraction.Ignore)) max = wh.distance;
                if (Mannequin.PickSkin(o, dir, max, out Part p, out float t, out Vector3 n))
                {
                    if (Random.value < Mathf.Lerp(1f, 0.25f, t / 25f)) { p.owner.Hit(p, null, o + dir * t, dir, n); frags++; }
                }
                else if (max < 25f && i % 4 == 0 && Mannequin.Nearest(wh.point, 0.45f) == null) Fx.Frag(wh, dir);
            }
            // shock wave pushes loose things
            foreach (var col in Physics.OverlapSphere(c, 8f, ~0, QueryTriggerInteraction.Ignore))
                if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic && col.attachedRigidbody.GetComponent<Part>() == null)
                    col.attachedRigidbody.AddExplosionForce(14f, c, 8f, 1f, ForceMode.Impulse);
            var pl = Game.I != null ? Game.I.player : null;
            if (pl != null) { Vector3 to = pl.transform.position - c; float dp = to.magnitude; if (dp < 5f) pl.Push(to.normalized * (5f - dp) * 2.5f + Vector3.up * (5f - dp) * 0.8f); }
            Game.LastShot = "EXPLOSION  ·  " + dCam.ToString("0.0") + " M";
        }

        // ---------- sounds
        static void Clips()
        {
            if (boomNear != null) return;
            boomNear = Boom(2.8f, true); boomFar = Boom(3.2f, false);
            int sr = 44100;
            { int n = sr / 6; var d = new float[n]; for (int i = 0; i < n; i++) { float t = i / (float)sr; d[i] = (Mathf.Sin(t * 3400f * 6.283f) * 0.5f + Mathf.Sin(t * 5100f * 6.283f) * 0.3f + Mathf.Sin(t * 1900f * 6.283f) * 0.3f) * Mathf.Exp(-t * 40f) * 0.7f; } clinkClip = AudioClip.Create("clink", n, 1, sr, false); clinkClip.SetData(d, 0); }
            { int n = sr * 5; var d = new float[n]; for (int i = 0; i < n; i++) { float t = i / (float)sr; d[i] = Mathf.Sin(t * 3800f * 6.283f) * Mathf.Exp(-t * 0.7f) * Mathf.Min(1f, t * 10f) * 0.25f; } ringClip = AudioClip.Create("ring", n, 1, sr, false); ringClip.SetData(d, 0); }
            { int n = sr / 4; var d = new float[n]; var rng = new System.Random(9); for (int i = 0; i < n; i++) { float t = i / (float)sr; float a = (t < 0.03f ? Mathf.Exp(-t * 120f) : 0f) + (t > 0.12f ? Mathf.Exp(-(t - 0.12f) * 80f) : 0f); d[i] = (Mathf.Sin(t * 4200f * 6.283f) * 0.5f + (float)(rng.NextDouble() * 2 - 1) * 0.5f) * a * 0.6f; } pinClip = AudioClip.Create("pin", n, 1, sr, false); pinClip.SetData(d, 0); }
        }
        static AudioClip Boom(float len, bool near)
        {
            int sr = 44100, n = (int)(sr * len); var d = new float[n]; var rng = new System.Random(near ? 21 : 22);
            float lp = 0, lp2 = 0, echo = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr, w = (float)(rng.NextDouble() * 2 - 1);
                lp += (w - lp) * (near ? 0.25f : 0.06f); lp2 += (lp - lp2) * 0.08f;
                float crack = near ? w * Mathf.Exp(-t * 60f) * 1.2f : 0f;                      // the sharp front
                float body = lp * Mathf.Exp(-t * (near ? 4f : 3f)) * 1.4f;                      // the roar
                float sub = Mathf.Sin(t * 6.283f * (42f - t * 10f)) * Mathf.Exp(-t * 3f) * 1.1f;   // the thump in the chest
                float rumble = lp2 * Mathf.Exp(-t * 1.1f) * 3f;                                  // rolling off buildings
                if (i > sr / 5) echo = d[i - sr / 5] * 0.3f;
                d[i] = Mathf.Clamp((crack + body + sub + rumble) * Mathf.Min(1f, t * 3000f) + echo, -1f, 1f) * 0.95f;
            }
            var c = AudioClip.Create("boom", n, 1, sr, false); c.SetData(d, 0); return c;
        }
    }

    // a light that dies away
    public class Fade : MonoBehaviour
    {
        float t, len, i0; Light l;
        public void Set(float length, float intensity) { len = length; i0 = intensity; l = GetComponent<Light>(); }
        void Update()
        {
            t += Time.deltaTime; float k = 1f - t / len;
            if (k <= 0) { Destroy(gameObject); return; }
            l.intensity = i0 * k * k; l.color = Color.Lerp(new Color(1f, 0.4f, 0.1f), new Color(1f, 0.9f, 0.75f), k);
        }
    }

    // camera shake (added to by blasts, read by the player camera)
    public static class Shake
    {
        public static float amount;
        public static void Add(float a) { amount = Mathf.Min(2f, amount + a); }
        public static Vector3 Offset(float dt)
        {
            amount = Mathf.MoveTowards(amount, 0f, dt * 1.6f);
            float a = amount * amount;
            return new Vector3((Mathf.PerlinNoise(Time.time * 25f, 0f) - 0.5f) * 6f, (Mathf.PerlinNoise(0f, Time.time * 25f) - 0.5f) * 6f, (Mathf.PerlinNoise(Time.time * 18f, 5f) - 0.5f) * 4f) * a;
        }
    }

    // muffled hearing after a close blast: everything goes quiet and comes back over a few seconds
    public static class Deafen
    {
        static float until, depth;
        public static void Hit(float k) { depth = Mathf.Max(depth, k); until = Time.time + 1f + 4f * k; }
        public static float Factor()
        {
            if (Time.time > until + 2f) return 1f;
            float r = Mathf.Clamp01((Time.time - (until - 1f)) / 3f);
            return Mathf.Lerp(1f - depth * 0.85f, 1f, r);
        }
    }
}
