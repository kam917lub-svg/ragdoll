using System.Collections.Generic;
using UnityEngine;

namespace VITS
{
    public class Part : MonoBehaviour
    {
        public Mannequin owner;
        public string key;
        public int idx;                 // body segment (BodyMesh bone index)
        public Rigidbody rb;
        public CharacterJoint joint;
        public Part parentPart;
        public float radius, length;
        public int hits;
        public bool severed, isHead, isLeg, isArm, isTorso;
        public MeshFilter mf;
        public Mesh mesh;               // own copy once modified
        public Vector3[] verts;
        public List<int> tris;
        public Vector3 sdfOff;          // local -> original segment space (for lower pieces of a cut limb)
        public float yMin = -9f, yMax = 9f;
        public int triTotal, carved;

        public float Sdf(Vector3 local) => BodyMesh.Sdf(idx, local + sdfOff);
        public Vector3 Normal(Vector3 local) => BodyMesh.Normal(idx, local + sdfOff);
        public Vector3 Project(Vector3 local) => BodyMesh.Project(idx, local + sdfOff) - sdfOff;
    }

    public class Wound
    {
        public Part part;
        public Vector3 lp, ln;          // local to the part
        public float rate, acc, age, life, runT;
        public bool arterial;
    }

    // blood running down the skin, following gravity across segments
    public class Runner
    {
        public Part part; public Vector3 lp; public float left, acc;
    }

    // Meat chunk torn off by a bullet or an amputation. Real rigidbody, bleeds for a moment. Not hit by bullets.
    public class Gib : MonoBehaviour
    {
        static readonly Queue<GameObject> all = new Queue<GameObject>();
        static Material skin, meat;
        float bleed; Rigidbody rb;

        public static void Clear() { all.Clear(); }

        public static void Spawn(Vector3 p, Vector3 v, float s)
        {
            if (skin == null) { skin = Mats.Lit(Mannequin.SkinColor, 0.3f); meat = Mats.Lit(new Color(0.5f, 0.04f, 0.06f), 0.6f); }
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            g.name = "gib"; g.layer = 2; // Ignore Raycast: bullets pass through chunks
            g.transform.position = p;
            g.transform.rotation = Random.rotation;
            g.transform.localScale = new Vector3(s * Random.Range(0.8f, 1.4f), s * Random.Range(0.5f, 0.9f), s * Random.Range(0.8f, 1.2f));
            g.GetComponent<Renderer>().sharedMaterial = Random.value < 0.5f ? skin : meat;
            var inner = Mats.Vis(PrimitiveType.Sphere, g.transform, new Vector3(0, 0.25f, 0.2f), new Vector3(0.8f, 0.8f, 0.8f), meat);
            inner.layer = 2;
            var rb = g.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(0.03f, s * s * s * 1000f);
            rb.linearVelocity = v; rb.angularVelocity = Random.insideUnitSphere * 12f;
            rb.maxDepenetrationVelocity = 1f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var gb = g.AddComponent<Gib>(); gb.rb = rb; gb.bleed = Random.Range(1f, 2.5f);
            all.Enqueue(g);
            while (all.Count > 140) { var o = all.Dequeue(); if (o != null) Destroy(o); }
        }

        void Update()
        {
            if (bleed <= 0) return;
            bleed -= Time.deltaTime;
            if (Random.value < Time.deltaTime * 14f && Blood.I != null)
                Blood.I.Emit(transform.position, rb.linearVelocity * 0.3f + Random.insideUnitSphere * 0.3f, Random.Range(0.2f, 0.6f));
        }
    }

    public class Mannequin : MonoBehaviour
    {
        public static readonly List<Mannequin> All = new List<Mannequin>();
        static int seq;
        public static readonly Color SkinColor = new Color(0.84f, 0.73f, 0.62f);

        public string displayName;
        public bool dead, ragdoll, conscious = true, crawling;
        public float blood = 5000f;
        public const float BloodMax = 5000f;
        public string status = "WANDERING", cause = "", lastHit = "-";
        public float lastHitTime = -99;
        public readonly List<string> injuries = new List<string>();
        public readonly Dictionary<string, Part> parts = new Dictionary<string, Part>();
        readonly List<Part> allParts = new List<Part>();   // includes detached pieces
        readonly List<Wound> wounds = new List<Wound>();
        readonly List<Runner> runners = new List<Runner>();

        enum S { Idle, Walk, Flee, Cover }
        S state = S.Idle;
        float stateT, phase, speed, heart, writheT, hurt, crawlT, detourT, stuckT, bestDist;
        int torsoHits, headHits;
        Vector3 target, lastPos, vel, detour, lastDir = Vector3.forward;
        Part clutch;
        static readonly Collider[] buf = new Collider[32];

        static Material skinMat, innerMat, visorMat, tagMat, stumpMat, boneMat;

        struct D
        {
            public string k, p; public Vector3 pos; public float len, rad, mass;
            public D(string k, string p, Vector3 pos, float len, float rad, float mass) { this.k = k; this.p = p; this.pos = pos; this.len = len; this.rad = rad; this.mass = mass; }
        }
        // order = BodyMesh bone index. Facing +Z, left side is -X.
        static readonly D[] DEFS =
        {
            new D("pelvis", null,     new Vector3(0, 0.95f, 0),        0.2f,  0.13f, 12f),
            new D("chest",  "pelvis", new Vector3(0, 0.10f, 0),        0.46f, 0.14f, 16f),
            new D("head",   "chest",  new Vector3(0, 0.46f, 0),        0.3f,  0.11f, 5f),
            new D("uarmL",  "chest",  new Vector3(-0.235f, 0.39f, 0),  0.3f,  0.06f, 2.5f),
            new D("farmL",  "uarmL",  new Vector3(0, -0.3f, 0),        0.37f, 0.048f, 2f),
            new D("uarmR",  "chest",  new Vector3(0.235f, 0.39f, 0),   0.3f,  0.06f, 2.5f),
            new D("farmR",  "uarmR",  new Vector3(0, -0.3f, 0),        0.37f, 0.048f, 2f),
            new D("thighL", "pelvis", new Vector3(-0.1f, -0.05f, 0),   0.45f, 0.085f, 8f),
            new D("shinL",  "thighL", new Vector3(0, -0.45f, 0),       0.45f, 0.062f, 4.5f),
            new D("thighR", "pelvis", new Vector3(0.1f, -0.05f, 0),    0.45f, 0.085f, 8f),
            new D("shinR",  "thighR", new Vector3(0, -0.45f, 0),       0.45f, 0.062f, 4.5f),
        };

        void Awake()
        {
            All.Add(this);
            seq++;
            displayName = "CARL #" + seq.ToString("00");
            gameObject.name = displayName;
            if (skinMat == null)
            {
                skinMat = Mats.Lit(SkinColor, 0.35f);
                innerMat = Mats.Flesh(new Color(0.6f, 0.06f, 0.08f));
                visorMat = Mats.Lit(new Color(0.07f, 0.07f, 0.08f), 0.6f);
                tagMat = Mats.Lit(new Color(0.95f, 0.38f, 0.12f), 0.3f);
                stumpMat = Mats.Lit(new Color(0.45f, 0.03f, 0.05f), 0.75f);
                boneMat = Mats.Lit(new Color(0.93f, 0.9f, 0.82f), 0.4f);
            }
            if (BodyMesh.Meshes[0] == null) BodyMesh.Build();
            Build();
            lastPos = transform.position;
            stateT = Random.Range(0.5f, 3f);
            phase = Random.value;
        }

        void OnDestroy() { All.Remove(this); }

        void Build()
        {
            for (int n = 0; n < DEFS.Length; n++)
            {
                var d = DEFS[n];
                var go = new GameObject(d.k);
                var t = go.transform;
                t.SetParent(d.p == null ? transform : parts[d.p].transform, false);
                t.localPosition = d.pos;
                var part = go.AddComponent<Part>();
                part.owner = this; part.key = d.k; part.radius = d.rad; part.length = d.len; part.idx = n;
                part.isHead = d.k == "head"; part.isTorso = d.k == "chest" || d.k == "pelvis";
                part.isLeg = d.k.StartsWith("thigh") || d.k.StartsWith("shin"); part.isArm = d.k.Contains("arm");
                if (d.p != null) part.parentPart = parts[d.p];

                var rb = go.AddComponent<Rigidbody>();
                rb.mass = d.mass; rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.None;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.solverIterations = 12; rb.solverVelocityIterations = 4;
                rb.linearDamping = 0.05f; rb.angularDamping = 0.8f;
                rb.maxDepenetrationVelocity = 1.5f;
                part.rb = rb;

                // visible segment
                part.mf = go.AddComponent<MeshFilter>(); part.mf.sharedMesh = BodyMesh.Meshes[n];
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = new[] { skinMat, innerMat };
                part.verts = BodyMesh.Verts[n]; part.triTotal = BodyMesh.Tris[n].Length / 3;

                switch (d.k)
                {
                    case "pelvis": { var c = go.AddComponent<CapsuleCollider>(); c.direction = 0; c.center = new Vector3(0, -0.02f, 0); c.height = 0.36f; c.radius = 0.12f; break; }
                    case "chest":
                    {
                        var c = go.AddComponent<CapsuleCollider>(); c.direction = 1; c.center = new Vector3(0, 0.19f, 0); c.height = 0.5f; c.radius = 0.13f;
                        var s = go.AddComponent<CapsuleCollider>(); s.direction = 0; s.center = new Vector3(0, 0.315f, 0); s.height = 0.46f; s.radius = 0.085f;
                        Mats.Vis(PrimitiveType.Cube, t, new Vector3(-0.07f, 0.27f, 0.104f), new Vector3(0.08f, 0.028f, 0.01f), tagMat, false);
                        break;
                    }
                    case "head":
                    {
                        var c = go.AddComponent<SphereCollider>(); c.center = new Vector3(0, 0.17f, 0.005f); c.radius = 0.12f;
                        Mats.Vis(PrimitiveType.Cube, t, new Vector3(0, 0.19f, 0.108f), new Vector3(0.17f, 0.032f, 0.04f), visorMat, false);
                        break;
                    }
                    default:
                        AddLimbColliders(part, 0f, d.len);
                        break;
                }
                parts[d.k] = part; allParts.Add(part);
            }
            Joint(parts["chest"], -25, 25, 15, 15);
            Joint(parts["head"], -40, 40, 30, 30);
            Joint(parts["uarmL"], -140, 50, 70, 40); Joint(parts["uarmR"], -140, 50, 70, 40);
            Joint(parts["farmL"], -140, 3, 5, 5); Joint(parts["farmR"], -140, 3, 5, 5);
            Joint(parts["thighL"], -100, 30, 35, 20); Joint(parts["thighR"], -100, 30, 35, 20);
            Joint(parts["shinL"], 0, 130, 4, 4); Joint(parts["shinR"], 0, 130, 4, 4);
        }

        // capsule along the limb between local y = -from and -to (+ foot box for the shin)
        static void AddLimbColliders(Part p, float from, float to)
        {
            float h = Mathf.Max(to - from, 0.02f), r = Mathf.Min(p.radius, h * 0.5f);
            var c = p.gameObject.AddComponent<CapsuleCollider>(); c.direction = 1;
            c.center = new Vector3(0, -(from + to) * 0.5f, 0); c.height = h; c.radius = r;
            if (p.key.StartsWith("shin") && to >= p.length - 0.001f)
            {
                var f = p.gameObject.AddComponent<BoxCollider>();
                f.center = new Vector3(0, -0.4f, 0.045f) - p.sdfOff; f.size = new Vector3(0.1f, 0.1f, 0.26f);
            }
        }

        static void Joint(Part p, float lo, float hi, float s1, float s2)
        {
            var j = p.gameObject.AddComponent<CharacterJoint>();
            j.connectedBody = p.parentPart.rb;
            j.axis = Vector3.right; j.swingAxis = Vector3.forward;
            j.lowTwistLimit = new SoftJointLimit { limit = lo };
            j.highTwistLimit = new SoftJointLimit { limit = hi };
            j.swing1Limit = new SoftJointLimit { limit = s1 };
            j.swing2Limit = new SoftJointLimit { limit = s2 };
            j.enableProjection = true;
            p.joint = j;
        }

        // ======================= behaviour =======================
        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            if (!ragdoll) { Locomotion(dt); Pose(dt); }
            else if (!dead && conscious && !crawling) Writhe(dt);
            Bleed(dt);
            RunBlood(dt);
            heart += dt * (dead ? 0 : (1.3f + hurt * 0.8f));
            hurt = Mathf.Max(0, hurt - dt * 0.03f);
            if (!dead)
            {
                if (blood < BloodMax * 0.5f && conscious) { conscious = false; crawling = false; status = "FAINTED"; GoRagdoll(); }
                if (blood < BloodMax * 0.35f) Die("BLOOD LOSS");
            }
        }

        // is the standing body (capsule from knee to head) free at pos? steps lower than 0.45 m are ignored
        bool Free(Vector3 pos, bool carls = true)
        {
            int n = Physics.OverlapCapsuleNonAlloc(pos + Vector3.up * 0.7f, pos + Vector3.up * 1.5f, 0.24f, buf, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = buf[i];
                if (c.transform.IsChildOf(transform)) continue;
                if (c.gameObject.layer == 2) continue;
                var ar = c.attachedRigidbody;
                if (ar != null && !ar.isKinematic) continue; // corpses and pieces on the floor: step over
                if (!carls && c.GetComponentInParent<Mannequin>() != null) continue;
                return false;
            }
            return true;
        }

        float Ground(Vector3 p, float cur)
        {
            float best = cur - 1.5f; bool any = false;
            var hs = Physics.RaycastAll(p + Vector3.up * 0.6f, Vector3.down, 2f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hs)
            {
                if (h.rigidbody != null) continue;
                if (!any || h.point.y > best) { best = h.point.y; any = true; }
            }
            return any ? best : cur;
        }

        void Locomotion(float dt)
        {
            stateT -= dt;
            Vector3 pos = transform.position;
            float want = 0;
            switch (state)
            {
                case S.Idle:
                    status = hurt > 0.3f ? "HURT / STANDING" : "IDLE";
                    if (stateT <= 0) NewGoal(S.Walk, Game.RandomPoint(), Random.Range(8f, 14f));
                    break;
                case S.Walk:
                    status = hurt > 0.3f ? "HURT / LIMPING" : "WANDERING";
                    want = hurt > 0.3f ? 0.45f : 0.85f;
                    if (stateT <= 0 || Flat(target - pos).magnitude < 0.5f) { state = S.Idle; stateT = Random.Range(1.5f, 4f); }
                    break;
                case S.Flee:
                    status = "RUNNING TO COVER";
                    want = hurt > 0.6f ? 0.9f : 2.2f;
                    if (Flat(target - pos).magnitude < 0.6f) { state = S.Cover; stateT = Random.Range(8f, 16f); }
                    if (stateT <= 0) NewGoal(S.Walk, Game.RandomPoint(), 10f);
                    break;
                case S.Cover:
                    status = "IN COVER";
                    if (stateT <= 0) NewGoal(S.Walk, Game.RandomPoint(), 10f);
                    break;
            }
            speed = Mathf.MoveTowards(speed, want, dt * 4f);

            // stuck detection: no progress toward the goal for 3 s -> another goal
            float dist = Flat(target - pos).magnitude;
            if (dist < bestDist - 0.3f) { bestDist = dist; stuckT = 0; } else if (want > 0) stuckT += dt;
            if (stuckT > 3f)
            {
                if (state == S.Flee) NewGoal(S.Flee, Game.CoverPoint(pos, target), stateT);
                else NewGoal(S.Walk, Game.RandomPoint(), 10f);
            }

            Vector3 goal = detourT > 0 ? detour : target;
            detourT -= dt;
            Vector3 dir = Flat(goal - pos);
            if (!Free(pos, false))
            {
                // pushed into something (spawned, shoved): step out
                for (int a = 0; a < 8; a++) { var o = Quaternion.Euler(0, a * 45f, 0) * Vector3.forward * 0.3f; if (Free(pos + o, false)) { transform.position = pos + o; break; } }
            }
            else if (speed > 0.01f && dir.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir.normalized), 150f * dt);
                Vector3 fwd = transform.forward, step = fwd * speed * dt;
                if (!Free(pos + fwd * 0.35f))
                {
                    // walk around the obstacle: first free direction closest to the goal
                    Vector3 to = dir.normalized; float bestA = 999; Vector3 bestD = Vector3.zero;
                    for (int a = 1; a <= 5; a++) foreach (int sg in new[] { 1, -1 })
                    {
                        var dd = Quaternion.Euler(0, sg * a * 30f, 0) * fwd;
                        if (!Free(pos + dd * 0.5f) || !Free(pos + dd * 1.0f)) continue;
                        float ang = Vector3.Angle(dd, to);
                        if (ang < bestA) { bestA = ang; bestD = dd; }
                    }
                    if (bestA < 999) { detour = pos + bestD * 1.2f; detourT = 1.0f; transform.rotation = Quaternion.LookRotation(bestD); }
                    step = Vector3.zero;
                }
                Vector3 np = Game.Clamp(pos + step);
                float gy = Ground(np, pos.y);
                if (gy - pos.y < 0.5f) np.y = Mathf.MoveTowards(pos.y, gy, dt * 3f); else np = pos;
                transform.position = np;
                phase += dt * (0.35f + speed * 0.65f);
            }
            vel = Vector3.ClampMagnitude((transform.position - lastPos) / dt, 3f); lastPos = transform.position;
        }

        void NewGoal(S s, Vector3 t, float time)
        {
            state = s; target = t; stateT = time; stuckT = 0; detourT = 0; bestDist = Flat(t - transform.position).magnitude;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        void Pose(float dt)
        {
            float a = Mathf.Clamp01(speed / 1.2f) * (state == S.Flee ? 38f : 26f);
            float s = Mathf.Sin(phase * Mathf.PI * 2f), k = 1f - Mathf.Exp(-dt * 12f);
            float kneeL = Mathf.Max(0, Mathf.Sin(phase * Mathf.PI * 2f - 1.2f)) * a * 1.6f;
            float kneeR = Mathf.Max(0, Mathf.Sin(phase * Mathf.PI * 2f + Mathf.PI - 1.2f)) * a * 1.6f;
            float crouch = state == S.Cover ? 1f : 0f;
            Set("thighL", Quaternion.Euler(-s * a - crouch * 60f, 0, 0), k); Set("thighR", Quaternion.Euler(s * a - crouch * 60f, 0, 0), k);
            Set("shinL", Quaternion.Euler(kneeL + crouch * 100f, 0, 0), k); Set("shinR", Quaternion.Euler(kneeR + crouch * 100f, 0, 0), k);
            Set("uarmL", Quaternion.Euler(s * a * 0.8f, 0, -4), k); Set("uarmR", Quaternion.Euler(-s * a * 0.8f, 0, 4), k);
            Set("farmL", Quaternion.Euler(-12 - a * 0.3f, 0, 0), k); Set("farmR", Quaternion.Euler(-12 - a * 0.3f, 0, 0), k);
            float lean = hurt * 18f + (state == S.Flee ? 10f : 0f) + crouch * 30f;
            Set("chest", Quaternion.Euler(lean, 0, 0), k);
            Set("head", Quaternion.Euler(-lean * 0.4f, 0, 0), k);
            var pv = parts["pelvis"].transform;
            pv.localPosition = Vector3.Lerp(pv.localPosition, new Vector3(0, 0.95f - crouch * 0.38f, 0), k);
            if (clutch != null && !clutch.severed)
            {
                string arm = clutch.key.EndsWith("R") ? "L" : "R";
                if (!parts["farm" + arm].severed && !parts["uarm" + arm].severed)
                {
                    float side = arm == "R" ? -1 : 1;
                    Set("uarm" + arm, Quaternion.Euler(-55, 0, side * 28), k);
                    Set("farm" + arm, Quaternion.Euler(-105, 0, 0), k);
                }
            }
        }

        void Set(string key, Quaternion q, float k)
        {
            var p = parts[key];
            if (!p.severed) p.transform.localRotation = Quaternion.Slerp(p.transform.localRotation, q, k);
        }

        void Writhe(float dt)
        {
            status = "DOWN / CONSCIOUS";
            writheT -= dt;
            if (writheT > 0) return;
            writheT = Random.Range(0.35f, 1.1f);
            var list = new List<Part>();
            foreach (var p in parts.Values) if (!p.severed && (p.isArm || p.isLeg || p.isHead)) list.Add(p);
            if (list.Count == 0) return;
            var pick = list[Random.Range(0, list.Count)];
            pick.rb.AddForce((Random.insideUnitSphere + Vector3.up * 0.6f) * pick.rb.mass * 1.2f, ForceMode.Impulse);
        }

        // shot in the legs but awake: drag yourself with the arms toward cover
        void FixedUpdate()
        {
            if (!ragdoll || dead || !conscious || !crawling) return;
            var ch = parts["chest"];
            Vector3 to = Flat(target - ch.transform.position);
            if (to.magnitude < 0.7f) { crawling = false; status = "IN COVER / DOWN"; return; }
            status = "CRAWLING TO COVER";
            Vector3 d = to.normalized;
            float k = Mathf.Clamp01(blood / BloodMax * 2f - 0.8f);
            crawlT += Time.fixedDeltaTime;
            float pull = Mathf.Max(0, Mathf.Sin(crawlT * 4f));
            ch.rb.AddForce(d * 170f * k * pull + Vector3.up * 70f * k);
            parts["pelvis"].rb.AddForce(d * 40f * k * pull);
            if (!parts["head"].severed) parts["head"].rb.AddForce(Vector3.up * 35f * k);
            string arm = ((int)(crawlT * 4f / Mathf.PI)) % 2 == 0 ? "L" : "R";
            var fa = parts["farm" + arm];
            if (!fa.severed) fa.rb.AddForce((d * 55f + Vector3.up * 25f) * k * (1f - pull));
        }

        public void GoRagdoll()
        {
            if (ragdoll) return;
            ragdoll = true;
            foreach (var p in parts.Values)
            {
                if (p.severed) continue;
                p.rb.isKinematic = false;
                p.rb.interpolation = RigidbodyInterpolation.Interpolate;
                p.rb.linearVelocity = vel;
            }
            if (!parts["shinL"].severed) parts["shinL"].rb.AddForce(transform.forward * 1.2f, ForceMode.VelocityChange);
            if (!parts["shinR"].severed) parts["shinR"].rb.AddForce(transform.forward * 1.2f, ForceMode.VelocityChange);
        }

        public void Die(string why)
        {
            if (dead) return;
            dead = true; conscious = false; crawling = false; cause = why; status = "DEAD / " + why;
            GoRagdoll();
        }

        public void Flee()
        {
            if (dead || ragdoll) return;
            NewGoal(S.Flee, Game.CoverPoint(transform.position, Vector3.one * 999f), Random.Range(10f, 16f));
        }

        // ======================= damage =======================
        // sphere-trace the real skin surface along the bullet (colliders are only approximate)
        static bool Trace(Part p, Vector3 worldFrom, Vector3 dir, float maxT, out Vector3 local)
        {
            Vector3 o = p.transform.InverseTransformPoint(worldFrom), d = p.transform.InverseTransformDirection(dir);
            float t = 0; local = o;
            for (int i = 0; i < 64 && t < maxT; i++)
            {
                Vector3 q = o + d * t;
                float s = p.Sdf(q);
                if (s < 0.0008f) { local = q; return q.y >= p.yMin - 0.02f && q.y <= p.yMax + 0.02f; }
                t += Mathf.Max(s, 0.002f);
            }
            return false;
        }

        public void Hit(Part p, Collider col, Vector3 pt, Vector3 dir, Vector3 nrm)
        {
            try { DoHit(p, pt, dir); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        void DoHit(Part p, Vector3 pt, Vector3 dir)
        {
            p.hits++; lastHit = p.key.ToUpper(); lastHitTime = Time.time; lastDir = dir;
            hurt = Mathf.Min(1, hurt + 0.3f);
            var B = Blood.I;

            // entry / exit on the visible skin
            Vector3 inL = p.transform.InverseTransformPoint(pt);
            if (Trace(p, pt - dir * 0.2f, dir, 0.4f, out Vector3 tl)) inL = tl;
            bool exits = Trace(p, pt + dir * 0.7f, -dir, 0.75f, out Vector3 outL);
            Vector3 inW = p.transform.TransformPoint(inL), inN = p.transform.TransformDirection(p.Normal(inL));
            Vector3 outW = exits ? p.transform.TransformPoint(outL) : inW, outN = exits ? p.transform.TransformDirection(p.Normal(outL)) : dir;

            // small entry wound, bigger torn exit
            B.SkinDecal(p.transform, inL, p.Normal(inL), 0.012f, 5);
            if (exits) B.SkinDecal(p.transform, outL, p.Normal(outL), 0.022f, 5);
            if (exits) Carve(p, outL, p.isHead ? 0.028f : p.isTorso ? 0.02f : 0.016f);

            // spatter: a little back toward the shooter, a cone out of the exit
            B.Spray(inW + inN * 0.01f, (-dir + inN) * 0.5f, 10, 1.4f, 0.45f, 0.05f, 0.35f);
            if (exits) B.Spray(outW + outN * 0.01f, dir, p.isHead ? 80 : 45, p.isHead ? 5f : 3.8f, 0.4f, 0.15f, 1.0f);
            if (exits && Random.value < 0.5f) Gib.Spawn(outW, dir * Random.Range(1.5f, 3.5f) + Random.insideUnitSphere + Vector3.up * 0.8f, Random.Range(0.012f, 0.022f));

            AddWound(p, inL, p.Normal(inL), p.isHead ? 9f : p.isTorso ? 9f : 7f, false, 0);
            if (exits) AddWound(p, outL, p.Normal(outL), p.isHead ? 24f : p.isTorso ? 18f : 14f, !p.isHead && Random.value < 0.35f, 0);

            if (p.isHead && !p.severed)
            {
                headHits++;
                injuries.Add("HEAD  GUNSHOT  FATAL");
                Die("HEADSHOT");
                if (headHits >= 3) SeverJoint(p, dir);
            }
            else if (p.isTorso)
            {
                torsoHits++;
                injuries.Add((p.key == "chest" ? "CHEST" : "ABDOMEN") + "  GUNSHOT" + (exits ? "  THROUGH" : ""));
                if (torsoHits >= 5 || p.carved > p.triTotal * 0.25f) Die("MASSIVE TRAUMA");
                else if (!ragdoll) { clutch = p; if (Random.value < 0.15f * torsoHits) GoRagdoll(); }
            }
            else if (p.isArm || p.isLeg)
            {
                injuries.Add(p.key.ToUpper() + "  GUNSHOT");
                if (!p.severed) clutch = p;
                // limbs come apart where they are hit: 3rd hit, 2nd hit (50%), or too much flesh gone
                bool cut = p.hits >= 3 || (p.hits >= 2 && Random.value < 0.5f) || p.carved > p.triTotal * 0.12f;
                if (cut) SeverAt(p, -inL.y, dir);
                else if (p.isLeg && !dead && !p.severed) { GoRagdoll(); StartCrawl(); }
            }

            if (!ragdoll) Flee();
            if (!p.rb.isKinematic) p.rb.AddForceAtPosition(dir * Game.BulletImpulse, inW, ForceMode.Impulse);
            while (injuries.Count > 12) injuries.RemoveAt(0);
        }

        void StartCrawl()
        {
            if (!conscious || dead) return;
            crawling = true; target = Game.CoverPoint(parts["chest"].transform.position, Vector3.one * 999f);
        }

        static void OwnMesh(Part p)
        {
            if (p.mesh != null) return;
            p.mesh = Object.Instantiate(p.mf.sharedMesh);
            p.mf.sharedMesh = p.mesh;
            if (p.tris == null) p.tris = new List<int>(BodyMesh.Tris[p.idx]);
        }

        // tear the skin away around a point (local to the part): holes show the flesh inside
        void Carve(Part p, Vector3 lp, float r)
        {
            OwnMesh(p);
            var v = p.verts; float r2 = r * r;
            var keep = new List<int>(p.tris.Count);
            int removed = 0;
            for (int t = 0; t < p.tris.Count; t += 3)
            {
                int a = p.tris[t], b = p.tris[t + 1], c = p.tris[t + 2];
                if (((v[a] + v[b] + v[c]) / 3f - lp).sqrMagnitude < r2) { removed++; continue; }
                keep.Add(a); keep.Add(b); keep.Add(c);
            }
            if (removed == 0) return;
            p.carved += removed; p.tris = keep; p.mesh.SetTriangles(keep, 0);
        }

        void AddWound(Part p, Vector3 lp, Vector3 ln, float rate, bool arterial, float life)
        {
            wounds.Add(new Wound { part = p, lp = lp, ln = ln, rate = rate, arterial = arterial, life = life, runT = Random.Range(0f, 0.3f) });
        }

        void Bleed(float dt)
        {
            const float dv = 0.45f;
            float pulse = 0.35f + 0.65f * Mathf.Pow(Mathf.Max(0, Mathf.Cos(heart * Mathf.PI * 2f)), 3f);
            for (int i = wounds.Count - 1; i >= 0; i--)
            {
                var w = wounds[i];
                if (w.part == null) { wounds.RemoveAt(i); continue; }
                w.age += dt;
                float k = w.life > 0 ? Mathf.Clamp01(1f - w.age / w.life) : Mathf.Exp(-w.age / 80f);
                if (k < 0.02f) { wounds.RemoveAt(i); continue; }
                bool spurt = w.arterial && !dead;
                float rate = w.rate * k * (dead ? 0.45f : 1f) * (spurt ? pulse * 1.6f : 1f);
                var t = w.part.transform;
                Vector3 n = t.TransformDirection(w.ln);
                // blood running from the wound down the skin
                w.runT -= dt;
                if (w.runT <= 0 && !spurt)
                {
                    w.runT = Random.Range(0.25f, 0.6f);
                    if (runners.Count < 40) runners.Add(new Runner { part = w.part, lp = w.lp, left = Mathf.Clamp(rate * 0.06f, 0.05f, 1.6f) });
                }
                w.acc += rate * dt;
                if (w.acc < dv) continue;
                Vector3 p = t.TransformPoint(w.lp);
                Vector3 bv = !w.part.rb.isKinematic ? w.part.rb.linearVelocity : vel;
                bool mine = !w.part.severed;
                int guard = 0;
                while (w.acc >= dv && guard++ < 30)
                {
                    w.acc -= dv;
                    // most of the flow runs down the skin and drips; spurts jet out
                    Vector3 v = spurt ? n * (1.2f + 2.8f * pulse) + Random.insideUnitSphere * 0.25f
                                      : n * 0.05f + Random.insideUnitSphere * 0.04f;
                    if (spurt || Random.value < 0.35f) Blood.I.Emit(p + n * 0.012f, v + bv, dv);
                    if (!dead && mine) blood -= dv;
                }
            }
        }

        // move each running stream down along the skin; it crosses from one segment to the next
        void RunBlood(float dt)
        {
            for (int i = runners.Count - 1; i >= 0; i--)
            {
                var r = runners[i];
                if (r.part == null || r.left <= 0) { runners.RemoveAt(i); continue; }
                r.acc += dt * 0.6f; // m/s
                int guard = 0;
                while (r.acc > 0.018f && guard++ < 6)
                {
                    r.acc -= 0.018f;
                    var t = r.part.transform;
                    Vector3 n = r.part.Normal(r.lp);
                    Vector3 down = t.InverseTransformDirection(Vector3.down);
                    Vector3 tan = down - n * Vector3.Dot(down, n);
                    if (tan.magnitude < 0.25f)
                    {
                        // skin faces up/down here: the blood drips off
                        Blood.I.Emit(t.TransformPoint(r.lp + n * 0.01f), Vector3.down * 0.2f, 0.4f + r.left);
                        r.left = 0; break;
                    }
                    tan.Normalize();
                    Vector3 np = r.part.Project(r.lp + tan * 0.018f);
                    Vector3 mid = (np + r.lp) * 0.5f;
                    Blood.I.SkinStreak(t, mid, r.part.Normal(mid), tan, Random.Range(0.009f, 0.014f), 0.03f);
                    r.lp = np; r.left -= 0.018f;
                    // flowed into another segment (e.g. chest -> pelvis -> thigh)?
                    Vector3 w = t.TransformPoint(np);
                    if (np.y < r.part.yMin || np.y > r.part.yMax) { r.left = 0; break; }
                    foreach (var o in allParts)
                    {
                        if (o == r.part || o == null || o.transform.root != t.root) continue;
                        Vector3 ol = o.transform.InverseTransformPoint(w);
                        if (ol.y < o.yMin || ol.y > o.yMax) continue;
                        if (o.Sdf(ol) < -0.003f) { r.part = o; r.lp = o.Project(ol); break; }
                    }
                }
            }
        }

        // cut a limb where it was hit (distance c from the joint down the bone)
        public void SeverAt(Part p, float c, Vector3 dir)
        {
            if (p.isTorso) return;
            if (p.isHead || c < 0.07f) { if (!p.severed) SeverJoint(p, dir); return; }
            c = Mathf.Min(c, p.length - 0.05f);
            if (c < 0.07f) return;

            OwnMesh(p);
            var v = p.verts;
            var up = new List<int>(); var lo = new List<int>();
            for (int t = 0; t < p.tris.Count; t += 3)
            {
                int a = p.tris[t], b = p.tris[t + 1], cc = p.tris[t + 2];
                float y = (v[a].y + v[b].y + v[cc].y) / 3f;
                if (y >= -c) { up.Add(a); up.Add(b); up.Add(cc); } else { lo.Add(a); lo.Add(b); lo.Add(cc); }
            }
            p.tris = up; p.mesh.SetTriangles(up, 0);

            // the lower piece: its own object, mesh and rigidbody, cut face at its origin
            var go = new GameObject(p.key + " (piece)");
            go.transform.SetPositionAndRotation(p.transform.TransformPoint(new Vector3(0, -c, 0)), p.transform.rotation);
            var np = go.AddComponent<Part>();
            np.owner = this; np.key = p.key; np.idx = p.idx; np.isArm = p.isArm; np.isLeg = p.isLeg; np.radius = p.radius;
            np.length = p.length - c; np.severed = true; np.sdfOff = p.sdfOff + new Vector3(0, -c, 0); np.yMax = 0f;
            var shifted = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) shifted[i] = v[i] + new Vector3(0, c, 0);
            np.verts = shifted; np.tris = lo; np.triTotal = p.triTotal; np.carved = p.carved;
            np.mesh = Object.Instantiate(p.mesh); np.mesh.SetVertices(shifted); np.mesh.SetTriangles(lo, 0); np.mesh.RecalculateBounds();
            np.mf = go.AddComponent<MeshFilter>(); np.mf.sharedMesh = np.mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { skinMat, innerMat };
            var rb = go.AddComponent<Rigidbody>();
            float frac = np.length / Mathf.Max(0.01f, p.length);
            rb.mass = Mathf.Max(0.3f, p.rb.mass * frac); p.rb.mass = Mathf.Max(0.3f, p.rb.mass * (1f - frac));
            rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.angularDamping = 0.8f; rb.maxDepenetrationVelocity = 1.5f; rb.solverIterations = 12;
            np.rb = rb;

            // colliders: upper part keeps [0,c], piece gets the rest
            foreach (var col in p.GetComponents<Collider>()) Destroy(col);
            AddLimbColliders(p, 0f, c);
            AddLimbColliders(np, 0f, np.length);
            p.length = c; p.yMin = -c;

            // children (forearm / shin) now hang from the piece
            var kids = new List<Part>();
            foreach (Transform ch in p.transform) { var cp = ch.GetComponent<Part>(); if (cp != null) kids.Add(cp); }
            foreach (var cp in kids)
            {
                cp.transform.SetParent(go.transform, true);
                cp.parentPart = np;
                if (cp.joint != null) cp.joint.connectedBody = rb;
            }
            foreach (var cp in go.GetComponentsInChildren<Part>()) { cp.severed = true; cp.rb.isKinematic = false; cp.rb.interpolation = RigidbodyInterpolation.Interpolate; }
            foreach (var a in p.GetComponents<Collider>()) foreach (var b in go.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(a, b, true);

            Vector3 baseVel = !p.rb.isKinematic ? p.rb.linearVelocity : vel;
            rb.linearVelocity = baseVel + dir * 1.8f + Vector3.up * 0.8f;
            rb.AddTorque(Random.insideUnitSphere * rb.mass * 0.3f, ForceMode.Impulse);
            allParts.Add(np);
            FinishCut(p, np, new Vector3(0, -c, 0), dir);
        }

        // detach a whole segment at its joint (head, or a limb hit right at the joint)
        public void SeverJoint(Part p, Vector3 dir)
        {
            if (p.severed || p.isTorso || p.parentPart == null) return;
            var par = p.parentPart;
            foreach (var a in par.GetComponents<Collider>()) foreach (var b in p.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(a, b, true);
            Vector3 baseVel = !par.rb.isKinematic ? par.rb.linearVelocity : vel;
            foreach (var c in p.GetComponentsInChildren<Part>()) { c.severed = true; c.rb.isKinematic = false; c.rb.interpolation = RigidbodyInterpolation.Interpolate; c.rb.linearVelocity = baseVel; }
            if (p.joint != null) DestroyImmediate(p.joint);
            p.transform.SetParent(null, true);
            p.rb.linearVelocity = baseVel + dir * 2f + Vector3.up * 1f;
            p.rb.AddTorque(Random.insideUnitSphere * p.rb.mass * 0.4f, ForceMode.Impulse);
            FinishCut(par, p, par.transform.InverseTransformPoint(p.transform.position), dir);
        }

        // stumps, fountains, chunks
        void FinishCut(Part upper, Part piece, Vector3 cutLocalUpper, Vector3 dir)
        {
            Vector3 cw = upper.transform.TransformPoint(cutLocalUpper);
            Vector3 nUp = upper.isTorso ? upper.transform.TransformDirection((cutLocalUpper - new Vector3(0, 0.15f, 0)).normalized) : -upper.transform.up;
            float r = Mathf.Clamp(-upper.Sdf(cutLocalUpper), 0.02f, 0.09f);
            if (upper.isTorso) r = piece.isHead ? 0.05f : Mathf.Clamp(piece.radius, 0.04f, 0.09f);
            Stump(upper.transform, cw, nUp, r);
            Stump(piece.transform, cw, -nUp, r * 0.95f);
            AddWound(upper, upper.transform.InverseTransformPoint(cw + nUp * 0.01f), upper.transform.InverseTransformDirection(nUp), piece.isHead ? 110f : piece.isLeg ? 70f : 45f, true, piece.isHead ? 12f : 18f);
            AddWound(piece, piece.transform.InverseTransformPoint(cw - nUp * 0.01f), piece.transform.InverseTransformDirection(-nUp), 16f, false, 7f);
            for (int i = 0; i < 3; i++) Gib.Spawn(cw, dir * Random.Range(1f, 3f) + Random.insideUnitSphere * 1.2f + Vector3.up * 1f, Random.Range(0.015f, 0.03f));
            Blood.I.Spray(cw, (dir + nUp) * 0.5f, 60, 4f, 0.6f, 0.2f, 1.0f);
            injuries.Add(piece.key.ToUpper() + "  SEVERED");
            if (piece.isHead) Die("DECAPITATED");
            else if (piece.isLeg && !dead) { GoRagdoll(); StartCrawl(); }
        }

        static void Stump(Transform t, Vector3 world, Vector3 n, float r)
        {
            var root = new GameObject("stump").transform;
            root.SetParent(t, false);
            root.position = world;
            root.rotation = Quaternion.FromToRotation(Vector3.up, n);
            Mats.Vis(PrimitiveType.Sphere, root, Vector3.zero, new Vector3(r * 2.0f, r * 0.6f, r * 2.0f), stumpMat);
            for (int i = 0; i < 3; i++)
                Mats.Vis(PrimitiveType.Sphere, root, new Vector3(Random.Range(-r, r) * 0.5f, r * 0.1f, Random.Range(-r, r) * 0.5f), Vector3.one * r * Random.Range(0.35f, 0.6f), stumpMat);
            Mats.Vis(PrimitiveType.Cylinder, root, new Vector3(0, r * 0.25f, 0), new Vector3(r * 0.45f, r * 0.35f, r * 0.45f), boneMat);
        }
    }
}
