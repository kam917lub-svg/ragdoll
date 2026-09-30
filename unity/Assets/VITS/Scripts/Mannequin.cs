using System.Collections.Generic;
using UnityEngine;

namespace VITS
{
    public class Part : MonoBehaviour
    {
        public Mannequin owner;
        public string key;
        public Rigidbody rb;
        public CharacterJoint joint;
        public Part parentPart;
        public float radius, length;
        public int hits;
        public bool severed, isHead, isLeg, isArm, isTorso;
        public int idx;
        public Skin skin;
    }

    public class Skin
    {
        public SkinnedMeshRenderer r;
        public Mesh mesh;
        public List<int> tris;
    }

    public class Wound
    {
        public Transform t; public Rigidbody rb;
        public Vector3 lp, ln;
        public float rate, acc, age, life; // rate in ml/s, life 0 = until clotted by decay
        public bool arterial;
    }

    // Meat chunk torn off by a bullet or an amputation. Real rigidbody, bleeds for a moment.
    public class Gib : MonoBehaviour
    {
        static readonly Queue<GameObject> all = new Queue<GameObject>();
        static Material skin, meat;
        float bleed; Rigidbody rb;

        public static void Clear() { all.Clear(); }

        public static void Spawn(Vector3 p, Vector3 v, float s)
        {
            if (skin == null) { skin = Mats.Lit(Mannequin.SkinColor, 0.3f); meat = Mats.Lit(new Color(0.5f, 0.04f, 0.06f), 0.6f); }
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "gib";
            g.transform.position = p;
            g.transform.rotation = Random.rotation;
            g.transform.localScale = new Vector3(s * Random.Range(0.8f, 1.5f), s * Random.Range(0.5f, 1f), s * Random.Range(0.8f, 1.3f));
            g.GetComponent<Renderer>().sharedMaterial = skin;
            var inner = Mats.Vis(PrimitiveType.Sphere, g.transform, new Vector3(0, 0, 0.35f), new Vector3(0.95f, 0.95f, 0.8f), meat);
            inner.name = "meat";
            var rb = g.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(0.05f, s * s * s * 1000f);
            rb.linearVelocity = v; rb.angularVelocity = Random.insideUnitSphere * 15f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var gb = g.AddComponent<Gib>(); gb.rb = rb; gb.bleed = Random.Range(1.5f, 3f);
            all.Enqueue(g);
            while (all.Count > 140) { var o = all.Dequeue(); if (o != null) Destroy(o); }
        }

        void Update()
        {
            if (bleed <= 0) return;
            bleed -= Time.deltaTime;
            if (Random.value < Time.deltaTime * 18f && Blood.I != null)
                Blood.I.Emit(transform.position, rb.linearVelocity * 0.3f + Random.insideUnitSphere * 0.3f, Random.Range(0.2f, 0.5f));
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
        readonly Part[] byIdx = new Part[BodyMesh.NB];
        readonly List<Wound> wounds = new List<Wound>();
        readonly int[] carved = new int[BodyMesh.NB];
        Transform[] bones;
        Mesh bake;

        enum S { Idle, Walk, Flee, Cover }
        S state = S.Idle;
        float stateT, phase, speed, heart, writheT, hurt, crawlT, streakT;
        int torsoHits;
        Vector3 target, lastPos, vel, lastDir = Vector3.forward;
        Part clutch;

        static Material skinMat, innerMat, visorMat, tagMat, stumpMat, boneMat;

        struct D
        {
            public string k, p; public Vector3 pos; public float len, rad, mass; public int sh;
            public D(string k, string p, Vector3 pos, float len, float rad, float mass, int sh) { this.k = k; this.p = p; this.pos = pos; this.len = len; this.rad = rad; this.mass = mass; this.sh = sh; }
        }
        // order = bone index in BodyMesh. Facing +Z, left side is -X.
        static readonly D[] DEFS =
        {
            new D("pelvis", null,     new Vector3(0, 0.95f, 0),        0.2f,  0.14f, 12f,  3),
            new D("chest",  "pelvis", new Vector3(0, 0.10f, 0),        0.46f, 0.15f, 16f,  1),
            new D("head",   "chest",  new Vector3(0, 0.46f, 0),        0.3f,  0.12f, 5f,   2),
            new D("uarmL",  "chest",  new Vector3(-0.245f, 0.39f, 0),  0.3f,  0.056f, 2.5f, 0),
            new D("farmL",  "uarmL",  new Vector3(0, -0.3f, 0),        0.33f, 0.05f, 2f,   0),
            new D("uarmR",  "chest",  new Vector3(0.245f, 0.39f, 0),   0.3f,  0.056f, 2.5f, 0),
            new D("farmR",  "uarmR",  new Vector3(0, -0.3f, 0),        0.33f, 0.05f, 2f,   0),
            new D("thighL", "pelvis", new Vector3(-0.1f, -0.05f, 0),   0.45f, 0.08f, 8f,   0),
            new D("shinL",  "thighL", new Vector3(0, -0.45f, 0),       0.45f, 0.062f, 4.5f, 0),
            new D("thighR", "pelvis", new Vector3(0.1f, -0.05f, 0),    0.45f, 0.08f, 8f,   0),
            new D("shinR",  "thighR", new Vector3(0, -0.45f, 0),       0.45f, 0.062f, 4.5f, 0),
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
                innerMat = Mats.Lit(new Color(0.55f, 0.05f, 0.07f), 0.7f);
                if (innerMat.HasProperty("_Cull")) innerMat.SetFloat("_Cull", 1f); // back faces: the inside of the flesh
                visorMat = Mats.Lit(new Color(0.07f, 0.07f, 0.08f), 0.6f);
                tagMat = Mats.Lit(new Color(0.95f, 0.38f, 0.12f), 0.3f);
                stumpMat = Mats.Lit(new Color(0.42f, 0.02f, 0.04f), 0.75f);
                boneMat = Mats.Lit(new Color(0.93f, 0.9f, 0.82f), 0.4f);
            }
            if (BodyMesh.Mesh == null) BodyMesh.Build();
            Build();
            lastPos = transform.position;
            stateT = Random.Range(0.5f, 3f);
            phase = Random.value;
        }

        void OnDestroy() { All.Remove(this); }

        void Build()
        {
            bones = new Transform[DEFS.Length];
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
                part.rb = rb;

                switch (d.sh)
                {
                    case 0:
                    {
                        var c = go.AddComponent<CapsuleCollider>(); c.direction = 1; c.center = new Vector3(0, -d.len / 2, 0); c.height = d.len; c.radius = d.rad;
                        if (d.k.StartsWith("shin")) { var f = go.AddComponent<BoxCollider>(); f.center = new Vector3(0, -d.len + 0.04f, 0.05f); f.size = new Vector3(0.09f, 0.08f, 0.22f); }
                        break;
                    }
                    case 1:
                    {
                        var c = go.AddComponent<CapsuleCollider>(); c.direction = 1; c.center = new Vector3(0, 0.2f, 0); c.height = 0.46f; c.radius = 0.14f;
                        Mats.Vis(PrimitiveType.Cube, t, new Vector3(-0.08f, 0.28f, 0.103f), new Vector3(0.08f, 0.028f, 0.012f), tagMat, false);
                        break;
                    }
                    case 2:
                    {
                        var c = go.AddComponent<SphereCollider>(); c.center = new Vector3(0, 0.15f, 0); c.radius = 0.12f;
                        Mats.Vis(PrimitiveType.Cube, t, new Vector3(0, 0.185f, 0.1f), new Vector3(0.17f, 0.032f, 0.05f), visorMat, false);
                        break;
                    }
                    default:
                    {
                        var c = go.AddComponent<BoxCollider>(); c.center = Vector3.zero; c.size = new Vector3(0.3f, 0.22f, 0.2f);
                        break;
                    }
                }
                parts[d.k] = part; byIdx[n] = part; bones[n] = t;
            }
            Joint("chest", -25, 25, 15, 15);
            Joint("head", -40, 40, 30, 30);
            Joint("uarmL", -140, 50, 70, 40); Joint("uarmR", -140, 50, 70, 40);
            Joint("farmL", -140, 3, 5, 5); Joint("farmR", -140, 3, 5, 5);
            Joint("thighL", -100, 30, 35, 20); Joint("thighR", -100, 30, 35, 20);
            Joint("shinL", 0, 130, 4, 4); Joint("shinR", 0, 130, 4, 4);

            // single smooth skinned body
            var skin = MakeSkin(gameObject, new List<int>(BodyMesh.Mesh.triangles));
            foreach (var p in parts.Values) p.skin = skin;
            bake = new Mesh();
        }

        Skin MakeSkin(GameObject host, List<int> tris)
        {
            var mesh = Object.Instantiate(BodyMesh.Mesh);
            mesh.SetTriangles(tris, 0);
            var r = host.AddComponent<SkinnedMeshRenderer>();
            r.sharedMesh = mesh; r.bones = bones; r.rootBone = bones[0];
            r.updateWhenOffscreen = true;
            r.sharedMaterials = new[] { skinMat, innerMat };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return new Skin { r = r, mesh = mesh, tris = tris };
        }

        void Joint(string k, float lo, float hi, float s1, float s2)
        {
            var p = parts[k];
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
            heart += dt * (dead ? 0 : (1.3f + hurt * 0.8f));
            hurt = Mathf.Max(0, hurt - dt * 0.03f);
            if (!dead)
            {
                if (blood < BloodMax * 0.5f && conscious) { conscious = false; crawling = false; status = "FAINTED"; GoRagdoll(); }
                if (blood < BloodMax * 0.35f) Die("BLOOD LOSS");
            }
        }

        bool Blocked(Vector3 pos, Vector3 dir, float dist)
        {
            var hits = Physics.CapsuleCastAll(pos + Vector3.up * 0.55f, pos + Vector3.up * 1.5f, 0.24f, dir, dist, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.distance <= 0f) continue;
                if (h.collider.GetComponentInParent<Mannequin>() == this) continue;
                if (h.collider.GetComponent<Gib>() != null) continue;
                return true;
            }
            return false;
        }

        float Ground(Vector3 p, float cur)
        {
            float best = cur - 1.2f; bool any = false;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 0.6f, Vector3.down, 2f, ~0, QueryTriggerInteraction.Ignore))
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
                    if (stateT <= 0) { state = S.Walk; target = Game.RandomPoint(); stateT = Random.Range(6f, 12f); }
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
                    if (stateT <= 0) { state = S.Walk; target = Game.RandomPoint(); stateT = 8f; }
                    break;
                case S.Cover:
                    status = "IN COVER";
                    if (stateT <= 0) { state = S.Walk; target = Game.RandomPoint(); stateT = 8f; }
                    break;
            }
            speed = Mathf.MoveTowards(speed, want, dt * 4f);
            Vector3 dir = Flat(target - pos);
            if (speed > 0.01f && dir.sqrMagnitude > 0.01f)
            {
                var want2 = Quaternion.LookRotation(dir.normalized);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want2, 120f * dt);
                Vector3 step = transform.forward * speed * dt;
                if (Blocked(pos, transform.forward, step.magnitude + 0.25f))
                {
                    // walk around: try turning, else pick another destination
                    var side = Quaternion.Euler(0, Random.value < 0.5f ? 60f : -60f, 0) * transform.forward;
                    if (!Blocked(pos, side, 0.6f)) transform.rotation = Quaternion.LookRotation(side);
                    else if (state == S.Walk) target = Game.RandomPoint();
                    step = Vector3.zero;
                }
                Vector3 np = Game.Clamp(pos + step);
                float gy = Ground(np, pos.y);
                if (gy - pos.y < 0.5f) np.y = Mathf.MoveTowards(pos.y, gy, dt * 3f); else np = pos; // steps: ok, walls: no
                transform.position = np;
                phase += dt * (0.35f + speed * 0.65f);
            }
            vel = (transform.position - lastPos) / dt; lastPos = transform.position;
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
            Set("uarmL", Quaternion.Euler(s * a * 0.8f, 0, -5), k); Set("uarmR", Quaternion.Euler(-s * a * 0.8f, 0, 5), k);
            Set("farmL", Quaternion.Euler(-15 - a * 0.3f, 0, 0), k); Set("farmR", Quaternion.Euler(-15 - a * 0.3f, 0, 0), k);
            float lean = hurt * 18f + (state == S.Flee ? 10f : 0f) + crouch * 30f;
            Set("chest", Quaternion.Euler(lean, 0, 0), k);
            Set("head", Quaternion.Euler(-lean * 0.4f, 0, 0), k);
            if (parts.TryGetValue("pelvis", out var pv)) pv.transform.localPosition = Vector3.Lerp(pv.transform.localPosition, new Vector3(0, 0.95f - crouch * 0.38f, 0), k);
            if (clutch != null && !clutch.severed)
            {
                string arm = clutch.key.EndsWith("R") ? "L" : "R";
                float side = arm == "R" ? -1 : 1;
                Set("uarm" + arm, Quaternion.Euler(-55, 0, side * 28), k);
                Set("farm" + arm, Quaternion.Euler(-105, 0, 0), k);
            }
        }

        void Set(string key, Quaternion q, float k)
        {
            if (parts.TryGetValue(key, out var p) && !p.severed)
                p.transform.localRotation = Quaternion.Slerp(p.transform.localRotation, q, k);
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
            pick.rb.AddForce((Random.insideUnitSphere + Vector3.up * 0.6f) * pick.rb.mass * 1.4f, ForceMode.Impulse);
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
            float k = Mathf.Clamp01(blood / BloodMax * 2f - 0.8f) * Mathf.Clamp01(hurt < 0.95f ? 1f : 0.6f);
            crawlT += Time.fixedDeltaTime;
            float pull = Mathf.Max(0, Mathf.Sin(crawlT * 4f)); // arm strokes
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
            state = S.Flee; stateT = Random.Range(8f, 14f);
            target = Game.CoverPoint(transform.position);
        }

        // ======================= damage =======================
        public void Hit(Part p, Collider col, Vector3 pt, Vector3 dir, Vector3 nrm)
        {
            p.hits++; lastHit = p.key.ToUpper(); lastHitTime = Time.time; lastDir = dir;
            hurt = Mathf.Min(1, hurt + 0.3f);
            var B = Blood.I;

            Vector3 exitP = pt + dir * p.radius * 2f, exitN = dir;
            bool exits = col.Raycast(new Ray(pt + dir * 0.6f, -dir), out RaycastHit eh, 0.6f);
            if (exits) { exitP = eh.point; exitN = eh.normal; }

            // blood: small back-spatter at the entry, a cone of spray out of the exit
            B.Spray(pt + nrm * 0.01f, (-dir + nrm) * 0.5f, 10, 1.4f, 0.4f, 0.05f, 0.3f);
            if (exits) B.Spray(exitP + exitN * 0.01f, dir, p.isHead ? 70 : 40, p.isHead ? 5f : 3.8f, 0.4f, 0.1f, 0.8f);

            // flesh is removed along the wound; enough removed = the piece comes off
            bool limb = p.isArm || p.isLeg;
            Carve(p, pt, limb ? 0.03f : 0.025f);
            if (exits) Carve(p, exitP, p.isHead ? 0.05f : limb ? 0.045f : 0.04f);
            if (exits && Random.value < 0.8f) Gib.Spawn(exitP, dir * Random.Range(1.5f, 3.5f) + Random.insideUnitSphere + Vector3.up * 0.8f, Random.Range(0.02f, 0.035f));

            AddWound(p.transform, pt, nrm, p.isHead ? 8f : p.isTorso ? 8f : 6f, false, 0);
            if (exits) AddWound(p.transform, exitP, exitN, p.isHead ? 22f : p.isTorso ? 16f : 12f, !p.isHead && Random.value < 0.35f, 0);

            if (!p.severed)
            {
                if (p.isHead) { injuries.Add("HEAD  GUNSHOT  FATAL"); Die("HEADSHOT"); }
                else if (p.isTorso)
                {
                    torsoHits++;
                    injuries.Add((p.key == "chest" ? "CHEST" : "ABDOMEN") + "  GUNSHOT" + (exits ? "  THROUGH" : ""));
                    if (torsoHits >= 5) Die("MASSIVE TRAUMA");
                    else if (!ragdoll) { clutch = p; if (Random.value < 0.15f * torsoHits) GoRagdoll(); }
                }
                else
                {
                    injuries.Add(p.key.ToUpper() + "  GUNSHOT");
                    clutch = p;
                    if (p.isLeg && !dead)
                    {
                        GoRagdoll();
                        if (conscious) { crawling = true; target = Game.CoverPoint(parts["chest"].transform.position); }
                    }
                }
            }
            if (!ragdoll) Flee();
            if (!p.rb.isKinematic) p.rb.AddForceAtPosition(dir * Game.BulletImpulse, pt, ForceMode.Impulse);
            while (injuries.Count > 12) injuries.RemoveAt(0);
        }

        // remove the triangles of the skin around a point (world space)
        void Carve(Part p, Vector3 world, float r)
        {
            var sk = p.skin; if (sk == null) return;
            sk.r.BakeMesh(bake, true);
            var v = bake.vertices;
            Vector3 lp = sk.r.transform.InverseTransformPoint(world);
            float r2 = r * r;
            var keep = new List<int>(sk.tris.Count);
            var touched = new HashSet<int>();
            for (int t = 0; t < sk.tris.Count; t += 3)
            {
                int a = sk.tris[t], b = sk.tris[t + 1], c = sk.tris[t + 2];
                if ((v[a] - lp).sqrMagnitude < r2 || (v[b] - lp).sqrMagnitude < r2 || (v[c] - lp).sqrMagnitude < r2)
                {
                    int bone = BodyMesh.Dom[a]; carved[bone]++; touched.Add(bone);
                    continue;
                }
                keep.Add(a); keep.Add(b); keep.Add(c);
            }
            if (keep.Count == sk.tris.Count) return;
            sk.tris = keep; sk.mesh.SetTriangles(keep, 0);
            foreach (int bone in touched)
            {
                var bp = byIdx[bone];
                if (bp == null || bp.isTorso || bp.severed && bp.parentPart != null && bp.parentPart.severed) continue;
                float frac = carved[bone] / (float)Mathf.Max(1, BodyMesh.TrisPerBone[bone]);
                if (frac > (bp.isHead ? 0.4f : 0.2f)) Sever(bp, lastDir);
            }
        }

        void AddWound(Transform t, Vector3 world, Vector3 n, float rate, bool arterial, float life)
        {
            wounds.Add(new Wound
            {
                t = t, rb = t.GetComponent<Rigidbody>(),
                lp = t.InverseTransformPoint(world), ln = t.InverseTransformDirection(n.normalized),
                rate = rate, arterial = arterial, life = life
            });
        }

        void Bleed(float dt)
        {
            const float dv = 0.45f;
            float pulse = 0.35f + 0.65f * Mathf.Pow(Mathf.Max(0, Mathf.Cos(heart * Mathf.PI * 2f)), 3f);
            streakT -= dt;
            bool streak = streakT <= 0;
            if (streak) streakT = 0.35f;
            for (int i = wounds.Count - 1; i >= 0; i--)
            {
                var w = wounds[i];
                if (w.t == null) { wounds.RemoveAt(i); continue; }
                w.age += dt;
                float k = w.life > 0 ? Mathf.Clamp01(1f - w.age / w.life) : Mathf.Exp(-w.age / 70f);
                if (k < 0.02f) { wounds.RemoveAt(i); continue; }
                bool spurt = w.arterial && !dead;
                float rate = w.rate * k * (dead ? 0.35f : 1f) * (spurt ? pulse * 1.6f : 1f);
                w.acc += rate * dt;
                Vector3 p = w.t.TransformPoint(w.lp), n = w.t.TransformDirection(w.ln);
                // blood running down the skin from the wound
                if (streak && !spurt && k > 0.2f && Random.value < 0.6f)
                {
                    Vector3 down = Vector3.ProjectOnPlane(Vector3.down, n);
                    if (down.sqrMagnitude > 0.05f)
                    {
                        down.Normalize();
                        float len = Random.Range(0.03f, 0.09f);
                        Blood.I.BodyDecal(w.t, p + down * len * 0.8f, n, Random.Range(0.012f, 0.02f), 2, -down, len * 2.2f);
                    }
                }
                if (w.acc < dv) continue;
                Vector3 bv = w.rb != null && !w.rb.isKinematic ? w.rb.linearVelocity : vel;
                bool mine = w.t.GetComponentInParent<Mannequin>() == this;
                int guard = 0;
                while (w.acc >= dv && guard++ < 30)
                {
                    w.acc -= dv;
                    Vector3 v = spurt ? n * (1.2f + 2.8f * pulse) + Random.insideUnitSphere * 0.25f
                                      : n * 0.12f + Random.insideUnitSphere * 0.06f;
                    Blood.I.Emit(p + n * 0.012f, v + bv, dv);
                    if (!dead && mine) blood -= dv;
                }
            }
        }

        public void Sever(Part p, Vector3 dir)
        {
            if (p.severed || p.isTorso || p.parentPart == null) return;
            var par = p.parentPart;
            Vector3 pivot = p.transform.position;
            Vector3 n = p.isHead ? par.transform.up : (pivot - par.transform.position).normalized;
            Vector3 baseVel = !par.rb.isKinematic ? par.rb.linearVelocity : vel;

            // split the skin: the piece takes the triangles of its bones
            var sub = p.GetComponentsInChildren<Part>();
            var inSub = new bool[BodyMesh.NB];
            foreach (var c in sub) inSub[c.idx] = true;
            var sk = par.skin;
            var keep = new List<int>(); var piece = new List<int>();
            for (int t = 0; t < sk.tris.Count; t += 3)
            {
                int a = sk.tris[t], b = sk.tris[t + 1], c = sk.tris[t + 2];
                bool ia = inSub[BodyMesh.Dom[a]], ib = inSub[BodyMesh.Dom[b]], ic = inSub[BodyMesh.Dom[c]];
                if (ia && ib && ic) { piece.Add(a); piece.Add(b); piece.Add(c); }
                else if (!ia && !ib && !ic) { keep.Add(a); keep.Add(b); keep.Add(c); }
            }
            sk.tris = keep; sk.mesh.SetTriangles(keep, 0);
            var psk = MakeSkin(p.gameObject, piece);
            foreach (var c in sub) c.skin = psk;

            var pieceCols = p.GetComponentsInChildren<Collider>();
            foreach (var pc in par.GetComponents<Collider>()) foreach (var c in pieceCols) Physics.IgnoreCollision(pc, c, true);
            foreach (var c in sub) { c.severed = true; c.rb.isKinematic = false; c.rb.interpolation = RigidbodyInterpolation.Interpolate; c.rb.linearVelocity = baseVel; }
            if (p.joint != null) DestroyImmediate(p.joint);
            p.transform.SetParent(null, true);
            p.rb.linearVelocity = baseVel + dir * 2.2f + Vector3.up * 1.2f;
            p.rb.AddTorque(Random.insideUnitSphere * p.rb.mass * 0.6f, ForceMode.Impulse);

            float r = p.radius * (p.isHead ? 0.55f : 0.95f);
            Stump(par.transform, pivot, n, r);
            Stump(p.transform, pivot, -n, r * 0.95f);
            AddWound(par.transform, pivot + n * 0.01f, n, p.isHead ? 110f : p.isLeg ? 70f : 45f, true, p.isHead ? 12f : 18f);
            AddWound(p.transform, pivot - n * 0.01f, -n, 14f, false, 6f);
            for (int i = 0; i < 4; i++) Gib.Spawn(pivot, dir * Random.Range(1f, 3.2f) + Random.insideUnitSphere * 1.4f + Vector3.up * 1.2f, Random.Range(0.025f, 0.045f));
            Blood.I.Spray(pivot, (dir + n) * 0.5f, 50, 4f, 0.6f, 0.2f, 0.9f);
            injuries.Add(p.key.ToUpper() + "  SEVERED");
            if (p.isHead) Die("DECAPITATED");
            else if (p.isLeg && !dead)
            {
                GoRagdoll();
                if (conscious) { crawling = true; target = Game.CoverPoint(parts["chest"].transform.position); }
            }
        }

        static void Stump(Transform t, Vector3 world, Vector3 n, float r)
        {
            var root = new GameObject("stump").transform;
            root.SetParent(t, false);
            root.position = world;
            root.rotation = Quaternion.FromToRotation(Vector3.up, n);
            Mats.Vis(PrimitiveType.Sphere, root, Vector3.zero, new Vector3(r * 2.0f, r * 0.8f, r * 2.0f), stumpMat);
            for (int i = 0; i < 3; i++)
                Mats.Vis(PrimitiveType.Sphere, root, new Vector3(Random.Range(-r, r) * 0.5f, r * 0.15f, Random.Range(-r, r) * 0.5f), Vector3.one * r * Random.Range(0.4f, 0.7f), stumpMat);
            Mats.Vis(PrimitiveType.Cylinder, root, new Vector3(0, r * 0.3f, 0), new Vector3(r * 0.5f, r * 0.4f, r * 0.5f), boneMat);
        }
    }
}
