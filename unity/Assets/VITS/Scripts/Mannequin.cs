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
        public ConfigurableJoint joint;
        public float driveK;           // muscle strength of this joint at full tone
        public Part parentPart;
        public float radius, length;
        public int hits;
        public bool severed, isHead, isLeg, isArm, isTorso;
        public Skin skin;
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

    // one skinned mesh (the body, or a piece that was cut off). All share the body vertices;
    // each has its own triangles, bone weights, bones and bind poses.
    public class Skin
    {
        public SkinnedMeshRenderer r;
        public Mesh mesh;
        public List<int> tris;
        public BoneWeight[] bw;
        public Transform[] bones;
        public Matrix4x4[] bind;
        public Material[] mats;
        public static readonly List<Skin> All = new List<Skin>();
    }

    public class Wound
    {
        public Part part;
        public Vector3 lp, ln;          // local to the part
        public float rate, acc, age, life, runT;
        public bool arterial, inside;
        public string name;
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
        public const int LayerWalk = 8, LayerRag = 9;

        // Anim: healthy, animated (kinematic). Active: physical body held up by muscles (hurt, kneeling, down, crawling).
        // Limp: dead, physical with only a little stiffness left.
        public enum M { Anim, Active, Limp }
        public M mode = M.Anim;
        public bool ragdoll => mode != M.Anim;

        public string displayName;
        public bool dead, conscious = true, crawling, provoked;
        public float blood = 5000f, support = 1f, strength = 1f;
        public const float BloodMax = 5000f;
        public string status = "WANDERING", cause = "", lastHit = "-";
        public float lastHitTime = -99;
        public readonly float[] legFn = { 1f, 1f };
        public readonly List<string> injuries = new List<string>();
        public readonly Dictionary<string, Part> parts = new Dictionary<string, Part>();
        readonly List<Part> allParts = new List<Part>();
        readonly List<Wound> wounds = new List<Wound>();
        readonly List<Runner> runners = new List<Runner>();

        enum S { Idle, Walk, Flee, Cover }
        S state = S.Idle;
        float turnRate, headYaw, headYawT, lookT, peekT, peek, stateT, phase, speed, heart, writheT, hurt, crawlT, detourT, stuckT, bestDist, foldT, groundY, groundT, deathT, shock;
        int torsoHits, headHits;
        Vector3 target, lastPos, vel, detour, anchor, lastDir = Vector3.forward;
        Part clutch;
        float totalMass;
        static readonly Collider[] buf = new Collider[32];
        static Material skinMat, innerMat, visorMat, tagMat, stumpMat, boneMat;

        struct D
        {
            public string k, p; public Vector3 pos; public float len, rad, mass, drive;
            public D(string k, string p, Vector3 pos, float len, float rad, float mass, float drive) { this.k = k; this.p = p; this.pos = pos; this.len = len; this.rad = rad; this.mass = mass; this.drive = drive; }
        }
        // order = BodyMesh bone index. Facing +Z, left side is -X.
        static readonly D[] DEFS =
        {
            new D("pelvis", null,     new Vector3(0, 0.95f, 0),        0.2f,  0.13f, 12f,  0),
            new D("chest",  "pelvis", new Vector3(0, 0.10f, 0),        0.46f, 0.14f, 16f,  1400),
            new D("head",   "chest",  new Vector3(0, 0.46f, 0),        0.3f,  0.11f, 5f,   260),
            new D("uarmL",  "chest",  new Vector3(-0.235f, 0.39f, 0),  0.3f,  0.06f, 2.5f, 260),
            new D("farmL",  "uarmL",  new Vector3(0, -0.3f, 0),        0.37f, 0.048f, 2f,  120),
            new D("uarmR",  "chest",  new Vector3(0.235f, 0.39f, 0),   0.3f,  0.06f, 2.5f, 260),
            new D("farmR",  "uarmR",  new Vector3(0, -0.3f, 0),        0.37f, 0.048f, 2f,  120),
            new D("thighL", "pelvis", new Vector3(-0.1f, -0.05f, 0),   0.45f, 0.085f, 8f,  1300),
            new D("shinL",  "thighL", new Vector3(0, -0.45f, 0),       0.45f, 0.062f, 4.5f, 900),
            new D("thighR", "pelvis", new Vector3(0.1f, -0.05f, 0),    0.45f, 0.085f, 8f,  1300),
            new D("shinR",  "thighR", new Vector3(0, -0.45f, 0),       0.45f, 0.062f, 4.5f, 900),
        };

        // main arteries (segment space of each bone, left side; right side mirrored in x). Hitting one = big pulsing jets.
        struct Art { public int bone; public Vector3 a, b; public float r, rate; public string name; public bool inside; }
        static readonly Art[] ARTS =
        {
            new Art { bone = 2, a = new Vector3(-0.03f, -0.07f, 0.02f), b = new Vector3(-0.03f, 0.08f, 0.02f), r = 0.014f, rate = 30f, name = "CAROTID" },
            new Art { bone = 2, a = new Vector3(0.03f, -0.07f, 0.02f),  b = new Vector3(0.03f, 0.08f, 0.02f),  r = 0.014f, rate = 30f, name = "CAROTID" },
            new Art { bone = 1, a = new Vector3(0.015f, 0.1f, 0.02f),   b = new Vector3(0f, 0.34f, 0f),        r = 0.045f, rate = 45f, name = "HEART / AORTA", inside = true },
            new Art { bone = 1, a = new Vector3(-0.1f, 0.33f, 0.03f),   b = new Vector3(-0.2f, 0.33f, 0.02f),  r = 0.013f, rate = 20f, name = "SUBCLAVIAN" },
            new Art { bone = 1, a = new Vector3(0.1f, 0.33f, 0.03f),    b = new Vector3(0.2f, 0.33f, 0.02f),   r = 0.013f, rate = 20f, name = "SUBCLAVIAN" },
            new Art { bone = 0, a = new Vector3(-0.07f, 0.05f, 0.03f),  b = new Vector3(-0.09f, -0.08f, 0.05f), r = 0.015f, rate = 25f, name = "ILIAC" },
            new Art { bone = 0, a = new Vector3(0.07f, 0.05f, 0.03f),   b = new Vector3(0.09f, -0.08f, 0.05f), r = 0.015f, rate = 25f, name = "ILIAC" },
            new Art { bone = 7, a = new Vector3(0.028f, -0.02f, 0.035f), b = new Vector3(0.015f, -0.38f, 0f),  r = 0.015f, rate = 25f, name = "FEMORAL" },
            new Art { bone = 3, a = new Vector3(0.03f, -0.03f, 0.01f),  b = new Vector3(0.025f, -0.28f, 0.015f), r = 0.012f, rate = 10f, name = "BRACHIAL" },
            new Art { bone = 4, a = new Vector3(0f, -0.02f, 0.03f),     b = new Vector3(0f, -0.27f, 0.03f),    r = 0.009f, rate = 5f, name = "RADIAL" },
            new Art { bone = 8, a = new Vector3(0f, -0.05f, -0.035f),   b = new Vector3(0f, -0.38f, -0.02f),   r = 0.009f, rate = 5f, name = "TIBIAL" },
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
            if (BodyMesh.Body == null) BodyMesh.Build();
            Build();
            lastPos = transform.position;
            stateT = Random.Range(0.5f, 3f);
            phase = Random.value;
        }

        void OnDestroy() { All.Remove(this); }

        public List<Part> Parts => allParts;

        void Build()
        {
            for (int n = 0; n < DEFS.Length; n++)
            {
                var d = DEFS[n];
                var go = new GameObject(d.k) { layer = LayerWalk };
                var t = go.transform;
                t.SetParent(d.p == null ? transform : parts[d.p].transform, false);
                t.localPosition = d.pos;
                var part = go.AddComponent<Part>();
                part.owner = this; part.key = d.k; part.radius = d.rad; part.length = d.len; part.idx = n; part.driveK = d.drive;
                part.isHead = d.k == "head"; part.isTorso = d.k == "chest" || d.k == "pelvis";
                part.isLeg = d.k.StartsWith("thigh") || d.k.StartsWith("shin"); part.isArm = d.k.Contains("arm");
                if (d.p != null) part.parentPart = parts[d.p];

                var rb = go.AddComponent<Rigidbody>();
                rb.mass = d.mass; rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.None;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.solverIterations = 20; rb.solverVelocityIterations = 8;
                rb.linearDamping = 0.05f; rb.angularDamping = 0.8f;
                rb.maxDepenetrationVelocity = 1f;
                part.rb = rb; totalMass += d.mass;


                switch (d.k)
                {
                    case "pelvis": { var c = go.AddComponent<CapsuleCollider>(); c.direction = 0; c.center = new Vector3(0, -0.02f, 0); c.height = 0.36f; c.radius = 0.12f; break; }
                    case "chest":
                    {
                        var c = go.AddComponent<CapsuleCollider>(); c.direction = 1; c.center = new Vector3(0, 0.19f, 0); c.height = 0.5f; c.radius = 0.13f;
                        var s = go.AddComponent<CapsuleCollider>(); s.direction = 0; s.center = new Vector3(0, 0.315f, 0); s.height = 0.46f; s.radius = 0.085f;
                        Mats.Vis(PrimitiveType.Cube, t, new Vector3(-0.06f, 0.27f, 0.098f), new Vector3(0.05f, 0.016f, 0.004f), tagMat, false);
                        break;
                    }
                    case "head":
                    {
                        var c = go.AddComponent<SphereCollider>(); c.center = new Vector3(0, 0.17f, 0.005f); c.radius = 0.12f;
                        Mats.Vis(PrimitiveType.Cube, t, new Vector3(0, 0.19f, 0.108f), new Vector3(0.17f, 0.032f, 0.04f), visorMat, false);
                        break;
                    }
                    default: AddLimbColliders(part, 0f, d.len); break;
                }
                parts[d.k] = part; allParts.Add(part);
            }
            // PhysX measures the X angle opposite to the transform rotation:
            // joint angle = -(Euler X). Knees bend back (Euler +), hips/shoulders swing forward (Euler -), elbows bend forward (Euler -).
            Joint(parts["chest"], -30, 30, 15, 20);
            Joint(parts["head"], -40, 45, 30, 35);
            Joint(parts["uarmL"], -50, 150, 70, 40); Joint(parts["uarmR"], -50, 150, 70, 40);
            Joint(parts["farmL"], -3, 145, 5, 5); Joint(parts["farmR"], -3, 145, 5, 5);
            Joint(parts["thighL"], -25, 110, 35, 20); Joint(parts["thighR"], -25, 110, 35, 20);
            Joint(parts["shinL"], -140, 0, 4, 4); Joint(parts["shinR"], -140, 0, 4, 4);

            // the whole body is one smooth skinned mesh
            var bones = new Transform[DEFS.Length];
            for (int n = 0; n < DEFS.Length; n++) bones[n] = parts[DEFS[n].k].transform;
            var skin = MakeSkin(gameObject, new List<int>(BodyMesh.BodyTris), (BoneWeight[])BodyMesh.Weights.Clone(), bones, BodyMesh.Bind);
            foreach (var p in parts.Values) p.skin = skin;
            foreach (var p in parts.Values) XRay.AddInternals(p);
        }

        Skin MakeSkin(GameObject host, List<int> tris, BoneWeight[] bw, Transform[] bones, Matrix4x4[] bind)
        {
            var mesh = Object.Instantiate(BodyMesh.Body);
            mesh.boneWeights = bw; mesh.bindposes = bind; mesh.SetTriangles(tris, 0);
            var r = host.AddComponent<SkinnedMeshRenderer>();
            r.sharedMesh = mesh; r.bones = bones; r.rootBone = bones[0] != null ? bones[0] : host.transform;
            r.updateWhenOffscreen = true;
            var mats = new[] { skinMat, innerMat };
            r.sharedMaterials = XRay.On ? new[] { XRay.Ghost } : mats;
            var sk = new Skin { r = r, mesh = mesh, tris = tris, bw = bw, bones = bones, bind = bind, mats = mats };
            Skin.All.Add(sk);
            return sk;
        }

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

        static void Joint(Part p, float xLo, float xHi, float yLim, float zLim)
        {
            var j = p.gameObject.AddComponent<ConfigurableJoint>();
            j.connectedBody = p.parentPart.rb;
            j.axis = Vector3.right; j.secondaryAxis = Vector3.up;
            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
            j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Limited;
            j.lowAngularXLimit = new SoftJointLimit { limit = xLo };
            j.highAngularXLimit = new SoftJointLimit { limit = xHi };
            j.angularYLimit = new SoftJointLimit { limit = yLim };
            j.angularZLimit = new SoftJointLimit { limit = zLim };
            j.rotationDriveMode = RotationDriveMode.Slerp;
            j.projectionMode = JointProjectionMode.PositionAndRotation;
            j.projectionDistance = 0.02f; j.projectionAngle = 4f;
            j.enablePreprocessing = false;
            p.joint = j;
        }

        // muscle target: the local rotation the segment should have relative to its parent
        static void Target(Part p, Quaternion local)
        {
            var j = p.joint; if (j == null) return;
            Vector3 right = j.axis, fwd = Vector3.Cross(j.axis, j.secondaryAxis).normalized, up = Vector3.Cross(fwd, right).normalized;
            var w2j = Quaternion.LookRotation(fwd, up);
            j.targetRotation = Quaternion.Inverse(w2j) * Quaternion.Inverse(local) * w2j;
        }
        void T(string k, float x, float z = 0)
        {
            var p = parts[k];
            if (!p.severed) Target(p, Quaternion.Euler(x, 0, z));
        }

        void Tone(float k)
        {
            foreach (var p in parts.Values)
            {
                if (p.joint == null) continue;
                float kk = p.severed ? 0.03f : k;
                float spring = p.driveK * kk;
                p.joint.slerpDrive = new JointDrive { positionSpring = spring, positionDamper = spring * 0.06f + 1f, maximumForce = float.MaxValue };
            }
        }

        // ======================= behaviour =======================
        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            strength = dead ? 0 : Mathf.Clamp01((blood / BloodMax - 0.62f) / 0.25f) * (conscious ? 1f : 0f);
            if (mode == M.Anim) { Locomotion(dt); Pose(dt); }
            else if (mode == M.Active) ActiveBrain(dt);
            Bleed(dt);
            RunBlood(dt);
            heart += dt * (dead ? 0 : (1.3f + hurt * 0.9f));
            hurt = Mathf.Max(0, hurt - dt * 0.02f);
            shock = Mathf.Max(0, shock - dt * 0.6f);
            if (!dead)
            {
                if (blood < BloodMax * 0.65f && conscious) { conscious = false; crawling = false; injuries.Add("LOST CONSCIOUSNESS"); GoActive(); }
                if (blood < BloodMax * 0.5f) Die("BLOOD LOSS");
                if (deathT > 0 && Time.time > deathT) Die("CARDIAC ARREST");
            }
        }

        // ---------------- healthy: animated walking ----------------
        bool Free(Vector3 pos, bool carls = true)
        {
            int n = Physics.OverlapCapsuleNonAlloc(pos + Vector3.up * 0.7f, pos + Vector3.up * 1.5f, 0.24f, buf, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = buf[i];
                if (c.transform.IsChildOf(transform)) continue;
                if (c.gameObject.layer == 2) continue;
                var part = c.GetComponent<Part>();
                if (part != null)
                {
                    if (!carls || part.owner == this) continue;
                    var o = part.owner;
                    // bodies on the floor and loose pieces: step over; standing people: go around
                    if (part.severed || o.dead || (o.mode == M.Active && o.support < 0.4f)) continue;
                    return false;
                }
                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;
                return false;
            }
            return true;
        }

        float Ground(Vector3 p, float cur)
        {
            float best = cur - 1.5f; bool any = false;
            var hs = Physics.RaycastAll(p + Vector3.up * 0.6f, Vector3.down, 2.5f, ~0, QueryTriggerInteraction.Ignore);
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
                    if (!Game.Brains && !provoked) { stateT = 1f; break; }
                    if (stateT <= 0) NewGoal(S.Walk, Game.RandomPoint(), Random.Range(8f, 14f));
                    break;
                case S.Walk:
                    status = hurt > 0.3f ? "HURT / LIMPING" : "WANDERING";
                    want = hurt > 0.3f ? 0.45f : 0.8f;
                    if (stateT <= 0 || Flat(target - pos).magnitude < 0.5f) { state = S.Idle; stateT = Random.Range(1.5f, 4f); }
                    break;
                case S.Flee:
                    status = hurt > 0.3f ? "HURT / GOING TO COVER" : "RUNNING TO COVER";
                    want = hurt > 0.5f ? 0.8f : 2.0f;
                    if (Flat(target - pos).magnitude < 0.6f) { state = S.Cover; stateT = Random.Range(10f, 20f); }
                    if (stateT <= 0) NewGoal(S.Walk, Game.RandomPoint(), 10f);
                    break;
                case S.Cover:
                    status = "IN COVER";
                    if (stateT <= 0) NewGoal(S.Walk, Game.RandomPoint(), 10f);
                    break;
            }
            speed = Mathf.MoveTowards(speed, want * (Hopping ? 0.35f : 1f), dt * (want > speed ? 1.6f : 2.5f));

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
                for (int a = 0; a < 8; a++) { var o = Quaternion.Euler(0, a * 45f, 0) * Vector3.forward * 0.3f; if (Free(pos + o, false)) { transform.position = pos + o; break; } }
            }
            else if (speed > 0.01f && dir.sqrMagnitude > 0.01f)
            {
                float turn = Vector3.Angle(transform.forward, dir);
                turnRate = Mathf.MoveTowards(turnRate, Mathf.Clamp(turn * 2.5f, 0f, 110f), dt * 220f);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir.normalized), turnRate * dt);
                // people slow down to turn
                float turnSlow = Mathf.Clamp01(1f - turn / 100f);
                Vector3 fwd = transform.forward, step = fwd * speed * Mathf.Max(0.25f, turnSlow) * dt;
                if (!Free(pos + fwd * 0.35f))
                {
                    Vector3 to = dir.normalized; float bestA = 999; Vector3 bestD = Vector3.zero;
                    for (int a = 1; a <= 5; a++) foreach (int sg in new[] { 1, -1 })
                    {
                        var dd = Quaternion.Euler(0, sg * a * 30f, 0) * fwd;
                        if (!Free(pos + dd * 0.5f) || !Free(pos + dd * 1.0f)) continue;
                        float ang = Vector3.Angle(dd, to);
                        if (ang < bestA) { bestA = ang; bestD = dd; }
                    }
                    if (bestA < 999) { detour = pos + bestD * 1.5f; detourT = 1.4f; }
                    step = Vector3.zero;
                }
                Vector3 np = Game.Clamp(pos + step);
                float gy = Ground(np, pos.y);
                if (gy - pos.y < 0.5f) np.y = Mathf.MoveTowards(pos.y, gy, dt * 3f); else np = pos;
                transform.position = np;
                phase += dt * (0.35f + speed * 0.65f);
            }
            vel = Vector3.ClampMagnitude((transform.position - lastPos) / dt, 2.5f); lastPos = transform.position;
        }

        void NewGoal(S s, Vector3 t, float time)
        {
            state = s; target = t; stateT = time; stuckT = 0; detourT = 0; bestDist = Flat(t - transform.position).magnitude;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        bool Hopping => mode == M.Anim && (legFn[0] < 0.6f || legFn[1] < 0.6f);

        void Pose(float dt)
        {
            float k = 1f - Mathf.Exp(-dt * 10f);
            // look around: glance every few seconds; in cover, peek out toward the shooter
            lookT -= dt;
            if (lookT <= 0) { lookT = Random.Range(1.5f, 4f); headYawT = state == S.Walk ? Random.Range(-35f, 35f) : Random.Range(-70f, 70f); }
            float cover = state == S.Cover ? 1f : 0f;
            if (state == S.Cover)
            {
                peekT -= dt;
                if (peekT <= 0) { peekT = peek > 0.5f ? Random.Range(3f, 7f) : Random.Range(1.2f, 2.2f); peek = peek > 0.5f ? 0f : 1f; }
            }
            else peek = 0f;
            float crouch = cover * (1f - peek * 0.7f);
            if (peek > 0.5f && Game.I != null && Game.I.player != null)
            {
                Vector3 to = Game.I.player.transform.position - transform.position; to.y = 0;
                headYawT = Mathf.Clamp(Vector3.SignedAngle(transform.forward, to, Vector3.up), -80f, 80f);
            }
            headYaw = Mathf.MoveTowards(headYaw, headYawT, dt * 120f);

            float a = Mathf.Clamp01(speed / 1.2f) * (state == S.Flee ? 34f : 22f);
            float s = Mathf.Sin(phase * Mathf.PI * 2f);
            float kneeL = Mathf.Max(0, Mathf.Sin(phase * Mathf.PI * 2f - 1.2f)) * a * 1.5f;
            float kneeR = Mathf.Max(0, Mathf.Sin(phase * Mathf.PI * 2f + Mathf.PI - 1.2f)) * a * 1.5f;
            float thL = -s * a, thR = s * a, pelvisY = 0.95f;
            if (Hopping)
            {
                // one bad leg: keep it off the ground and hop on the good one
                bool badL = legFn[0] < legFn[1];
                float hop = Mathf.Abs(Mathf.Sin(phase * Mathf.PI * 2f));
                if (badL) { thL = -25f; kneeL = 75f; thR = -8f * hop; kneeR = 12f + 25f * (1f - hop); }
                else { thR = -25f; kneeR = 75f; thL = -8f * hop; kneeL = 12f + 25f * (1f - hop); }
                pelvisY = 0.9f + 0.05f * hop * Mathf.Clamp01(speed * 3f);
            }
            // crouch in cover: knees bent, back fairly straight, hands resting forward
            pelvisY -= crouch * 0.3f;
            Set("thighL", Quaternion.Euler(thL - crouch * 55f, 0, -crouch * 6f), k); Set("thighR", Quaternion.Euler(thR - crouch * 55f, 0, crouch * 6f), k);
            Set("shinL", Quaternion.Euler(kneeL + crouch * 85f, 0, 0), k); Set("shinR", Quaternion.Euler(kneeR + crouch * 85f, 0, 0), k);
            Set("uarmL", Quaternion.Euler(s * a * 0.7f - crouch * 25f, 0, -4 - crouch * 4f), k); Set("uarmR", Quaternion.Euler(-s * a * 0.7f - crouch * 25f, 0, 4 + crouch * 4f), k);
            Set("farmL", Quaternion.Euler(-10 - a * 0.3f - crouch * 35f, 0, 0), k); Set("farmR", Quaternion.Euler(-10 - a * 0.3f - crouch * 35f, 0, 0), k);
            float lean = hurt * 14f + (state == S.Flee ? 8f : 0f) + crouch * 18f;
            Set("chest", Quaternion.Euler(lean, headYaw * 0.25f, 0), k);
            Set("head", Quaternion.Euler(-lean * 0.5f + (state == S.Idle ? 4f : 0f), headYaw * 0.75f, 0), k);
            var pv = parts["pelvis"].transform;
            pv.localPosition = Vector3.Lerp(pv.localPosition, new Vector3(0, pelvisY, 0), k);
            if (clutch != null && !clutch.severed)
            {
                string arm = clutch.key.EndsWith("R") ? "L" : "R";
                if (!parts["farm" + arm].severed && !parts["uarm" + arm].severed)
                {
                    ClutchTarget(arm, out float ux, out float uy, out float uz, out float fx);
                    Set("uarm" + arm, Quaternion.Euler(ux, uy, uz), k);
                    Set("farm" + arm, Quaternion.Euler(fx, 0, 0), k);
                }
            }
        }

        // arm pose that puts the hand on the wound: raise a little, rotate the shoulder inward, bend the elbow
        void ClutchTarget(string arm, out float ux, out float uy, out float uz, out float fx)
        {
            float s = arm == "R" ? -1f : 1f;   // inward rotation sign
            var c = clutch;
            if (c.isHead) { ux = -95f; uy = 35f * s; uz = 10f * s; fx = -125f; }
            else if (c.key == "chest") { ux = -30f; uy = 65f * s; uz = 8f * s; fx = -105f; }
            else if (c.key == "pelvis") { ux = -10f; uy = 60f * s; uz = 6f * s; fx = -80f; }
            else if (c.isLeg) { ux = -12f; uy = 20f * s; uz = 4f * s; fx = -25f; }
            else { ux = -25f; uy = 60f * s; uz = 6f * s; fx = -95f; } // other arm
        }

        // picked up with the middle mouse button
        public void Grabbed()
        {
            if (dead) return;
            shock = 1f; hurt = Mathf.Min(1f, hurt + 0.2f);
            if (mode == M.Anim) GoActive();
        }

        void Set(string key, Quaternion q, float k)
        {
            var p = parts[key];
            if (!p.severed) p.transform.localRotation = Quaternion.Slerp(p.transform.localRotation, q, k);
        }

        // ---------------- hurt: physical body with muscles ----------------
        void MakeDynamic()
        {
            foreach (var p in parts.Values)
            {
                if (p.severed) continue;
                p.rb.isKinematic = false;
                p.rb.interpolation = RigidbodyInterpolation.Interpolate;
                p.rb.linearVelocity = vel;
                p.gameObject.layer = LayerRag;
            }
        }

        // start being a physical body that holds itself up
        void GoActive()
        {
            if (mode != M.Anim) return;
            // current pose becomes the muscle target, so nothing snaps
            foreach (var p in parts.Values) if (p.joint != null && !p.severed) Target(p, p.transform.localRotation);
            Tone(1f);
            MakeDynamic();
            mode = M.Active;
            anchor = parts["pelvis"].transform.position;
            support = Mathf.Min(support, 1f);
            groundT = 0;
        }

        // back on its feet and able to walk (only light wounds)
        void Recover()
        {
            var pel = parts["pelvis"];
            Vector3 p = pel.transform.position;
            Vector3 fwd = Flat(parts["chest"].transform.forward);
            if (fwd.sqrMagnitude < 0.01f) fwd = transform.forward;
            float gy = Ground(p, p.y - 0.95f);
            transform.SetPositionAndRotation(new Vector3(p.x, gy, p.z), Quaternion.LookRotation(fwd.normalized));
            for (int n = 0; n < DEFS.Length; n++)
            {
                var q = parts[DEFS[n].k];
                if (q.severed) continue;
                q.rb.isKinematic = true; q.rb.interpolation = RigidbodyInterpolation.None; q.gameObject.layer = LayerWalk;
                q.transform.localPosition = DEFS[n].pos;
                if (n == 0) q.transform.localRotation = Quaternion.identity;
            }
            mode = M.Anim; lastPos = transform.position; vel = Vector3.zero; speed = 0;
            Flee();
        }

        void ActiveBrain(float dt)
        {
            // how well can the body hold itself up
            float legs = Mathf.Min(legFn[0], legFn[1]) * 0.65f + (legFn[0] + legFn[1]) * 0.175f;
            float want = Mathf.Min(strength, legs) - shock;
            support = Mathf.MoveTowards(support, Mathf.Clamp01(want), dt * (want < support ? 0.45f : 0.12f));
            float kneel = Mathf.InverseLerp(0.72f, 0.45f, support);   // 0 standing .. 1 kneeling
            bool down = support < 0.35f;

            float cl = 20f * hurt;
            if (!down)
            {
                status = kneel > 0.5f ? "HURT / KNEELING" : "HURT / STANDING";
                T("chest", 10f + cl + kneel * 12f); T("head", -8f + kneel * 10f);
                T("thighL", Mathf.Lerp(-4f, 2f, kneel)); T("thighR", Mathf.Lerp(-4f, 2f, kneel));
                T("shinL", Mathf.Lerp(6f, 95f, kneel)); T("shinR", Mathf.Lerp(6f, 95f, kneel));
                T("uarmL", -6f, -6f); T("uarmR", -6f, 6f); T("farmL", -18f); T("farmR", -18f);
                Tone(1f);
            }
            else
            {
                // on the floor: curl up around the wounds
                status = crawling ? "CRAWLING TO COVER" : conscious ? "DOWN / CONSCIOUS" : "DOWN / UNCONSCIOUS";
                float c = conscious ? 1f : 0.3f;
                T("chest", 22f * c); T("head", 18f * c);
                T("thighL", -60f * c); T("thighR", -55f * c); T("shinL", 100f * c); T("shinR", 95f * c);
                T("uarmL", -40f * c, 20f * c); T("uarmR", -40f * c, -20f * c); T("farmL", -100f * c); T("farmR", -100f * c);
                Tone(crawling ? 0.25f : conscious ? 0.35f : 0.08f);
                if (conscious && !crawling) Writhe(dt);
                if (conscious && !crawling && (legFn[0] < 0.5f || legFn[1] < 0.5f) && strength > 0.35f && Random.value < dt * 0.3f) StartCrawl();
            }
            if (clutch != null && !clutch.severed && conscious)
            {
                string arm = clutch.key.EndsWith("R") ? "L" : "R";
                if (!parts["farm" + arm].severed && !parts["uarm" + arm].severed)
                {
                    ClutchTarget(arm, out float ux, out float uy, out float uz, out float fx);
                    Target(parts["uarm" + arm], Quaternion.Euler(ux, uy, uz)); T("farm" + arm, fx);
                }
            }
            // light wounds only, standing and steady for a while: walk (or hop) away
            if (!down && support > 0.85f && Mathf.Max(legFn[0], legFn[1]) > 0.7f && Mathf.Min(legFn[0], legFn[1]) > 0.3f && strength > 0.75f && Time.time - lastHitTime > 2f)
                Recover();
        }

        void Writhe(float dt)
        {
            writheT -= dt;
            if (writheT > 0) return;
            writheT = Random.Range(0.5f, 1.4f);
            var list = new List<Part>();
            foreach (var p in parts.Values) if (!p.severed && (p.isArm || p.isLeg || p.isHead)) list.Add(p);
            if (list.Count == 0) return;
            var pick = list[Random.Range(0, list.Count)];
            pick.rb.AddForce((Random.insideUnitSphere + Vector3.up * 0.4f) * pick.rb.mass * 0.8f, ForceMode.Impulse);
        }

        void FixedUpdate()
        {
            if (mode == M.Limp)
            {
                if (foldT > 0) { foldT -= Time.fixedDeltaTime; if (foldT <= 0) Tone(0.05f); }
                return;
            }
            if (mode != M.Active) return;
            var pel = parts["pelvis"]; var ch = parts["chest"];
            if (crawling) { Crawl(); return; }
            float s = support;
            if (s < 0.35f) return;
            groundT -= Time.fixedDeltaTime;
            if (groundT <= 0) { groundT = 0.25f; groundY = Ground(pel.transform.position, pel.transform.position.y - 0.95f); }
            float kneel = Mathf.InverseLerp(0.72f, 0.45f, s);
            float targetH = Mathf.Lerp(0.93f, 0.55f, kneel);
            float h = pel.transform.position.y - groundY;
            float m = totalMass, g = 9.81f;
            float fy = m * g * 0.9f + (targetH - h) * m * 50f - pel.rb.linearVelocity.y * m * 7f;
            pel.rb.AddForce(Vector3.up * Mathf.Clamp(fy, 0f, m * g * 1.6f));
            // stay over the feet but let bullets rock the body
            Vector3 dxz = Flat(anchor - pel.transform.position), vxz = Flat(pel.rb.linearVelocity);
            pel.rb.AddForce(Vector3.ClampMagnitude(dxz * m * 20f - vxz * m * 5f, m * g * 0.6f));
            Upright(pel.rb, pel.transform.up, Vector3.up, 500f, 45f);
            Upright(ch.rb, ch.transform.up, Vector3.up, 120f, 12f);
        }

        static void Upright(Rigidbody rb, Vector3 cur, Vector3 want, float k, float d)
        {
            rb.AddTorque(Vector3.Cross(cur, want) * k - rb.angularVelocity * d);
        }

        // shot in the legs but awake: drag yourself with the arms toward cover
        void Crawl()
        {
            var ch = parts["chest"];
            Vector3 to = Flat(target - ch.transform.position);
            if (to.magnitude < 0.7f) { crawling = false; injuries.Add("REACHED COVER"); return; }
            Vector3 d = to.normalized;
            float k = Mathf.Clamp01(strength * 1.3f);
            crawlT += Time.fixedDeltaTime;
            float pull = Mathf.Max(0, Mathf.Sin(crawlT * 3.5f));
            ch.rb.AddForce(d * 160f * k * pull + Vector3.up * 60f * k);
            parts["pelvis"].rb.AddForce(d * 35f * k * pull);
            if (!parts["head"].severed) parts["head"].rb.AddForce(Vector3.up * 30f * k);
            string arm = ((int)(crawlT * 3.5f / Mathf.PI)) % 2 == 0 ? "L" : "R";
            var fa = parts["farm" + arm];
            if (!fa.severed) fa.rb.AddForce((d * 50f + Vector3.up * 22f) * k * (1f - pull));
            if (strength < 0.3f) crawling = false;
        }

        void StartCrawl()
        {
            if (!conscious || dead) return;
            crawling = true; target = Game.CoverPoint(parts["chest"].transform.position, Vector3.one * 999f);
        }

        // kept for callers: hurt -> physical body; dead -> limp
        public void GoRagdoll() { if (dead) GoLimp(); else GoActive(); }

        void GoLimp()
        {
            if (mode == M.Anim) MakeDynamic();
            mode = M.Limp;
            // knees buckle and the body folds down, then only a little stiffness is left
            T("thighL", -70f); T("thighR", -65f); T("shinL", 110f); T("shinR", 105f); T("chest", 25f); T("head", 20f);
            T("uarmL", -15f, -10f); T("uarmR", -15f, 10f); T("farmL", -40f); T("farmR", -40f);
            Tone(0.3f); foldT = 0.55f;
        }

        public void Die(string why)
        {
            if (dead) return;
            dead = true; conscious = false; crawling = false; cause = why; status = "DEAD / " + why;
            GoLimp();
        }

        // run AWAY from the shooter: a cover farther from him, or just away
        public void Flee()
        {
            provoked = true;
            if (dead || mode != M.Anim) return;
            Vector3 pos = transform.position, shooter = Game.I != null && Game.I.player != null ? Game.I.player.transform.position : pos - transform.forward;
            Vector3 c = Game.CoverPoint(pos, Vector3.one * 999f);
            bool coverAway = (c - shooter).magnitude > (pos - shooter).magnitude + 1f && (c - pos).magnitude < 14f;
            Vector3 away = pos - shooter; away.y = 0; if (away.sqrMagnitude < 0.01f) away = -transform.forward;
            Vector3 t = coverAway ? c : Game.Clamp(pos + away.normalized * 14f + Quaternion.Euler(0, Random.Range(-35f, 35f), 0) * away.normalized * 2f);
            NewGoal(S.Flee, t, Random.Range(10f, 16f));
        }

        // ======================= damage =======================
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

        static float SegSeg(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r), s, t;
            if (a < 1e-8f && e < 1e-8f) return r.magnitude;
            if (a < 1e-8f) { s = 0; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e < 1e-8f) { t = 0; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), den = a * e - b * b;
                    s = den > 1e-8f ? Mathf.Clamp01((b * f - c * e) / den) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0) { t = 0; s = Mathf.Clamp01(-c / a); } else if (t > 1) { t = 1; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            return ((p1 + d1 * s) - (p2 + d2 * t)).magnitude;
        }

        // which main artery does the bullet path (segment space) cross?
        static int ArteryHit(Part p, Vector3 aL, Vector3 bL)
        {
            Vector3 a = aL + p.sdfOff, b = bL + p.sdfOff;
            bool right = p.key.EndsWith("R");
            int best = -1; float bd = 9f;
            for (int i = 0; i < ARTS.Length; i++)
            {
                var ar = ARTS[i];
                int bone = ar.bone;
                if (bone == 3 || bone == 4 || bone == 7 || bone == 8) { if (p.idx != bone && p.idx != bone + 2) continue; }
                else if (p.idx != bone) continue;
                Vector3 x = ar.a, y = ar.b;
                if (right && bone != 0 && bone != 1 && bone != 2) { x.x = -x.x; y.x = -y.x; }
                float d = SegSeg(a, b, x, y) - ar.r;
                if (d < 0.008f && d < bd) { bd = d; best = i; }
            }
            return best;
        }

        public void Hit(Part p, Collider col, Vector3 pt, Vector3 dir, Vector3 nrm)
        {
            try { DoHit(p, pt, dir); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        void DoHit(Part p, Vector3 pt, Vector3 dir)
        {
            provoked = true;
            p.hits++; lastHit = p.key.ToUpper(); lastHitTime = Time.time; lastDir = dir;
            hurt = Mathf.Min(1, hurt + 0.3f);
            var B = Blood.I;

            Vector3 inL = p.transform.InverseTransformPoint(pt);
            if (Trace(p, pt - dir * 0.2f, dir, 0.4f, out Vector3 tl)) inL = tl;
            bool exits = Trace(p, pt + dir * 0.7f, -dir, 0.75f, out Vector3 outL);
            Vector3 dirL = p.transform.InverseTransformDirection(dir);
            Vector3 inW = p.transform.TransformPoint(inL), inN = p.transform.TransformDirection(p.Normal(inL));
            Vector3 outW = exits ? p.transform.TransformPoint(outL) : inW, outN = exits ? p.transform.TransformDirection(p.Normal(outL)) : dir;

            // 9 mm: a small dark hole in, a slightly bigger torn one out
            B.SkinDecal(p.transform, inL, p.Normal(inL), 0.008f, 5);
            if (exits) B.SkinDecal(p.transform, outL, p.Normal(outL), Player.AK ? 0.02f : 0.014f, 5);

            B.Spray(inW + inN * 0.01f, (-dir + inN) * 0.5f, 10, 1.4f, 0.45f, 0.05f, 0.35f);
            if (exits) B.Spray(outW + outN * 0.01f, dir, p.isHead ? 90 : 45, p.isHead ? 5.5f : 3.8f, 0.4f, 0.15f, 1.1f);
            if (exits && (p.isHead || Random.value < 0.35f)) Gib.Spawn(outW, dir * Random.Range(1.5f, 3.5f) + Random.insideUnitSphere + Vector3.up * 0.8f, Random.Range(0.01f, 0.02f));

            AddWound(p, inL, p.Normal(inL), p.isHead ? 2.5f : p.isTorso ? 1f : 0.6f, false, 0, "");
            if (exits) AddWound(p, outL, p.Normal(outL), p.isHead ? 6f : p.isTorso ? 3f : 1.5f, false, 0, "");

            // arteries: big pulsing jets out of the wound
            int art = ArteryHit(p, inL, exits ? outL : inL + dirL * 0.1f);
            if (art >= 0)
            {
                var ar = ARTS[art];
                Vector3 wl = exits ? outL : inL;
                AddWound(p, wl, p.Normal(wl), ar.rate, true, 0, ar.name);
                if (!exits || Random.value < 0.5f) AddWound(p, inL, p.Normal(inL), ar.rate * 0.5f, true, 0, ar.name);
                injuries.Add(ar.name + "  HIT");
                if (ar.inside && !dead) deathT = Time.time + Random.Range(5f, 12f);
                if (p.isLeg) legFn[p.key.EndsWith("R") ? 1 : 0] -= 0.2f;
            }

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
                if (!dead)
                {
                    clutch = p; shock = 0.35f;
                    if (torsoHits >= 5) Die("MASSIVE TRAUMA");
                    else GoActive();
                }
            }
            else if (p.isArm || p.isLeg)
            {
                injuries.Add(p.key.ToUpper() + "  GUNSHOT");
                if (!p.severed && !dead) clutch = p;
                if (p.isLeg && !p.severed) legFn[p.key.EndsWith("R") ? 1 : 0] -= 0.45f;
                bool cut = Player.AK ? (p.hits >= 2 || Random.value < 0.3f) : (p.hits >= 3 || (p.hits >= 2 && Random.value < 0.5f));
                if (cut) SeverAt(p, -inL.y, dir);
                else if (p.isLeg && !dead && !p.severed)
                {
                    bool bothBad = legFn[0] < 0.6f && legFn[1] < 0.6f, gone = Mathf.Min(legFn[0], legFn[1]) < 0.15f;
                    if (bothBad || gone || art >= 0 || Random.value < 0.35f) { shock = 0.3f; GoActive(); }
                    else { shock = 0.2f; if (mode == M.Anim) Flee(); } // hops away on the good leg
                }
                else if (!dead) shock = Mathf.Max(shock, 0.15f);
            }
            for (int i = 0; i < 2; i++) legFn[i] = Mathf.Max(0, legFn[i]);

            if (mode == M.Anim) Flee();
            if (!p.rb.isKinematic) p.rb.AddForceAtPosition(dir * Game.BulletImpulse * (dead ? 2.5f : 1f), inW, ForceMode.Impulse);
            Game.LastShot = displayName + " · " + p.key.ToUpper() + (dead ? " (DEAD)" : "");
            while (injuries.Count > 12) injuries.RemoveAt(0);
        }

        // move every weight that points at a bone we are cutting away onto 'to'
        static BoneWeight Rebind(BoneWeight w, System.Func<int, bool> bad, int to)
        {
            bool b0 = bad(w.boneIndex0), b1 = w.weight1 > 0 && bad(w.boneIndex1);
            if (!b0 && !b1) return w;
            return new BoneWeight { boneIndex0 = to, weight0 = 1f };
        }

        // what does a bullet along this ray really hit? (the visible skin, not the rough colliders)
        public static bool Pick(Vector3 o, Vector3 d, float maxT, out Part best, out float bestT)
        {
            best = null; bestT = maxT;
            foreach (var m in All)
            {
                if (m == null) continue;
                foreach (var p in m.allParts)
                {
                    if (p == null) continue;
                    Vector3 c = p.transform.position;
                    float tc = Vector3.Dot(c - o, d);
                    if (tc < -0.8f || tc > bestT + 0.8f) continue;
                    if ((o + d * tc - c).sqrMagnitude > 0.8f * 0.8f) continue;
                    float t0 = Mathf.Max(0f, tc - 0.8f);
                    if (Trace(p, o + d * t0, d, 1.6f, out Vector3 lp))
                    {
                        float t = t0 + Vector3.Dot(p.transform.TransformPoint(lp) - (o + d * t0), d);
                        if (t < bestT) { bestT = t; best = p; }
                    }
                }
            }
            return best != null;
        }

        void AddWound(Part p, Vector3 lp, Vector3 ln, float rate, bool arterial, float life, string name)
        {
            wounds.Add(new Wound { part = p, lp = lp, ln = ln, rate = rate, arterial = arterial, life = life, runT = Random.Range(0f, 0.3f), name = name });
        }

        void Bleed(float dt)
        {
            const float dv = 0.45f;
            float pulse = 0.3f + 0.7f * Mathf.Pow(Mathf.Max(0, Mathf.Cos(heart * Mathf.PI * 2f)), 3f);
            for (int i = wounds.Count - 1; i >= 0; i--)
            {
                var w = wounds[i];
                if (w.part == null) { wounds.RemoveAt(i); continue; }
                w.age += dt;
                float k = w.life > 0 ? Mathf.Clamp01(1f - w.age / w.life) : Mathf.Exp(-w.age / (w.arterial ? 120f : 70f));
                if (k < 0.02f) { wounds.RemoveAt(i); continue; }
                bool spurt = w.arterial && !dead;
                // pressure falls with blood volume: jets get weaker as they bleed out
                float pressure = dead ? 0.25f : Mathf.Clamp01(blood / BloodMax * 1.4f - 0.3f);
                float rate = w.rate * k * (spurt ? pulse * 1.7f * pressure : (dead ? 0.45f : 1f));
                var t = w.part.transform;
                Vector3 n = t.TransformDirection(w.ln);
                w.runT -= dt;
                if (w.runT <= 0)
                {
                    w.runT = Random.Range(0.2f, 0.5f);
                    if (runners.Count < 50) runners.Add(new Runner { part = w.part, lp = w.lp, left = Mathf.Clamp(rate * (spurt ? 0.02f : 0.06f), 0.05f, 1.8f) });
                }
                w.acc += rate * dt * 2f;
                if (w.acc < dv) continue;
                Vector3 p = t.TransformPoint(w.lp);
                Vector3 bv = !w.part.rb.isKinematic ? w.part.rb.linearVelocity : vel;
                bool mine = !w.part.severed;
                int guard = 0;
                while (w.acc >= dv && guard++ < 40)
                {
                    w.acc -= dv;
                    Vector3 v = spurt ? (n * (0.6f + 1.6f * pulse * pressure) + Vector3.down * 0.3f) + Random.insideUnitSphere * 0.15f
                                      : n * 0.05f + Random.insideUnitSphere * 0.04f;
                    if (spurt || Random.value < 0.5f) Blood.I.Emit(p + n * 0.012f, v + bv, dv);
                    if (!dead && mine) blood -= dv * 0.5f;
                }
            }
            // heart / aorta: most of the blood goes inside
            if (!dead && deathT > 0) blood -= 25f * dt;
        }

        void RunBlood(float dt)
        {
            for (int i = runners.Count - 1; i >= 0; i--)
            {
                var r = runners[i];
                if (r.part == null || r.left <= 0) { runners.RemoveAt(i); continue; }
                r.acc += dt * 0.6f;
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
                        Blood.I.Emit(t.TransformPoint(r.lp + n * 0.01f), Vector3.down * 0.2f, 0.4f + r.left);
                        r.left = 0; break;
                    }
                    tan.Normalize();
                    Vector3 np = r.part.Project(r.lp + tan * 0.018f);
                    Vector3 mid = (np + r.lp) * 0.5f;
                    Blood.I.SkinStreak(t, mid, r.part.Normal(mid), tan, Random.Range(0.009f, 0.014f), 0.03f);
                    r.lp = np; r.left -= 0.018f;
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

            var sk = p.skin;
            int bi = System.Array.IndexOf(sk.bones, p.transform);
            if (bi < 0) return;
            // bones below the cut: the children of this segment
            var below = new bool[sk.bones.Length];
            for (int i = 0; i < sk.bones.Length; i++) below[i] = sk.bones[i] != null && sk.bones[i] != p.transform && sk.bones[i].IsChildOf(p.transform);
            var V = BodyMesh.BodyVerts; var D = BodyMesh.Dom;
            var lower = new bool[V.Length];
            for (int i = 0; i < V.Length; i++)
                lower[i] = below[D[i]] || (D[i] == bi && sk.bind[bi].MultiplyPoint3x4(V[i]).y < -c);
            var up = new List<int>(); var lo = new List<int>();
            for (int t = 0; t < sk.tris.Count; t += 3)
            {
                int a = sk.tris[t], b = sk.tris[t + 1], cc = sk.tris[t + 2];
                bool la = lower[a], lb = lower[b], lc = lower[cc];
                if (la && lb && lc) { lo.Add(a); lo.Add(b); lo.Add(cc); }
                else if (!la && !lb && !lc) { up.Add(a); up.Add(b); up.Add(cc); }
            }
            var go = new GameObject(p.key + " (piece)") { layer = LayerRag };
            go.transform.SetPositionAndRotation(p.transform.TransformPoint(new Vector3(0, -c, 0)), p.transform.rotation);
            var np = go.AddComponent<Part>();
            np.owner = this; np.key = p.key; np.idx = p.idx; np.isArm = p.isArm; np.isLeg = p.isLeg; np.radius = p.radius;
            np.length = p.length - c; np.severed = true; np.sdfOff = p.sdfOff + new Vector3(0, -c, 0); np.yMax = 0f;
            // body keeps the upper part; everything weighted to the lower bones goes back to this segment
            var bwUp = (BoneWeight[])sk.bw.Clone();
            for (int i = 0; i < bwUp.Length; i++) if (!lower[i]) bwUp[i] = Rebind(bwUp[i], j => below[j], bi);
            sk.bw = bwUp; sk.tris = up; sk.mesh.boneWeights = bwUp; sk.mesh.SetTriangles(up, 0);
            // the piece: same vertices, this segment's bone replaced by the new piece transform
            var pBones = (Transform[])sk.bones.Clone(); pBones[bi] = go.transform;
            var pBind = (Matrix4x4[])sk.bind.Clone(); pBind[bi] = Matrix4x4.Translate(new Vector3(0, c, 0)) * sk.bind[bi];
            var bwLo = (BoneWeight[])sk.bw.Clone();
            for (int i = 0; i < bwLo.Length; i++) if (lower[i]) bwLo[i] = Rebind(bwLo[i], j => j != bi && !below[j], D[i]);
            var psk = MakeSkin(go, lo, bwLo, pBones, pBind);
            np.skin = psk;
            var rb = go.AddComponent<Rigidbody>();
            float frac = np.length / Mathf.Max(0.01f, p.length);
            rb.mass = Mathf.Max(0.3f, p.rb.mass * frac); p.rb.mass = Mathf.Max(0.3f, p.rb.mass * (1f - frac));
            rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.angularDamping = 0.8f; rb.maxDepenetrationVelocity = 1f; rb.solverIterations = 20;
            np.rb = rb;

            foreach (var col in p.GetComponents<Collider>()) Destroy(col);
            AddLimbColliders(p, 0f, c);
            AddLimbColliders(np, 0f, np.length);
            p.length = c; p.yMin = -c;

            var kids = new List<Part>();
            foreach (Transform ch in p.transform) { var cp = ch.GetComponent<Part>(); if (cp != null) kids.Add(cp); }
            foreach (var cp in kids)
            {
                cp.transform.SetParent(go.transform, true);
                cp.parentPart = np;
                if (cp.joint != null) cp.joint.connectedBody = rb;
            }
            foreach (var cp in go.GetComponentsInChildren<Part>())
            {
                cp.skin = np.skin == null ? cp.skin : np.skin;
                cp.severed = true; cp.rb.isKinematic = false; cp.rb.interpolation = RigidbodyInterpolation.Interpolate; cp.gameObject.layer = LayerRag;
                if (cp.joint != null) cp.joint.slerpDrive = new JointDrive { positionSpring = cp.driveK * 0.03f, positionDamper = 2f, maximumForce = float.MaxValue };
            }
            foreach (var a in p.GetComponents<Collider>()) foreach (var b in go.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(a, b, true);

            Vector3 baseVel = !p.rb.isKinematic ? p.rb.linearVelocity : vel;
            rb.linearVelocity = baseVel + dir * 1.5f + Vector3.up * 0.6f;
            rb.AddTorque(Random.insideUnitSphere * rb.mass * 0.25f, ForceMode.Impulse);
            allParts.Add(np);
            FinishCut(p, np, new Vector3(0, -c, 0), dir);
        }

        public void SeverJoint(Part p, Vector3 dir)
        {
            if (p.severed || p.isTorso || p.parentPart == null) return;
            var par = p.parentPart;
            foreach (var a in par.GetComponents<Collider>()) foreach (var b in p.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(a, b, true);
            Vector3 baseVel = !par.rb.isKinematic ? par.rb.linearVelocity : vel;
            foreach (var c in p.GetComponentsInChildren<Part>())
            {
                c.severed = true; c.rb.isKinematic = false; c.rb.interpolation = RigidbodyInterpolation.Interpolate; c.rb.linearVelocity = baseVel; c.gameObject.layer = LayerRag;
                if (c.joint != null && c != p) c.joint.slerpDrive = new JointDrive { positionSpring = c.driveK * 0.03f, positionDamper = 2f, maximumForce = float.MaxValue };
            }
            {
                var sk = par.skin;
                var inSub = new bool[sk.bones.Length];
                for (int i = 0; i < sk.bones.Length; i++) inSub[i] = sk.bones[i] != null && (sk.bones[i] == p.transform || sk.bones[i].IsChildOf(p.transform));
                var D = BodyMesh.Dom;
                var keep = new List<int>(); var piece = new List<int>();
                for (int t = 0; t < sk.tris.Count; t += 3)
                {
                    int a = sk.tris[t], b = sk.tris[t + 1], cc = sk.tris[t + 2];
                    bool ia = inSub[D[a]], ib = inSub[D[b]], ic = inSub[D[cc]];
                    if (ia && ib && ic) { piece.Add(a); piece.Add(b); piece.Add(cc); }
                    else if (!ia && !ib && !ic) { keep.Add(a); keep.Add(b); keep.Add(cc); }
                }
                var bwBody = (BoneWeight[])sk.bw.Clone(); var bwPiece = (BoneWeight[])sk.bw.Clone();
                for (int i = 0; i < bwBody.Length; i++)
                {
                    if (!inSub[D[i]]) bwBody[i] = Rebind(bwBody[i], j => inSub[j], D[i]);
                    else bwPiece[i] = Rebind(bwPiece[i], j => !inSub[j], D[i]);
                }
                sk.bw = bwBody; sk.tris = keep; sk.mesh.boneWeights = bwBody; sk.mesh.SetTriangles(keep, 0);
                var psk = MakeSkin(p.gameObject, piece, bwPiece, sk.bones, sk.bind);
                foreach (var c in p.GetComponentsInChildren<Part>()) if (c.skin == sk) c.skin = psk;
            }
            if (p.joint != null) DestroyImmediate(p.joint);
            p.transform.SetParent(null, true);
            p.rb.linearVelocity = baseVel + dir * 1.5f + Vector3.up * 0.8f;
            p.rb.AddTorque(Random.insideUnitSphere * p.rb.mass * 0.3f, ForceMode.Impulse);
            FinishCut(par, p, par.transform.InverseTransformPoint(p.transform.position), dir);
        }

        void FinishCut(Part upper, Part piece, Vector3 cutLocalUpper, Vector3 dir)
        {
            Vector3 cw = upper.transform.TransformPoint(cutLocalUpper);
            Vector3 nUp = upper.isTorso ? upper.transform.TransformDirection((cutLocalUpper - new Vector3(0, 0.15f, 0)).normalized) : -upper.transform.up;
            float r = Mathf.Clamp(-upper.Sdf(cutLocalUpper), 0.02f, 0.09f);
            if (upper.isTorso) r = piece.isHead ? 0.05f : Mathf.Clamp(piece.radius, 0.04f, 0.09f);
            Stump(upper.transform, cw, nUp, r);
            Stump(piece.transform, cw, -nUp, r * 0.95f);
            // a severed limb opens its big artery
            float rate = piece.isHead ? 45f : piece.isLeg ? 30f : 15f;
            AddWound(upper, upper.transform.InverseTransformPoint(cw + nUp * 0.01f), upper.transform.InverseTransformDirection(nUp), rate, true, piece.isHead ? 14f : 25f, "SEVERED");
            AddWound(piece, piece.transform.InverseTransformPoint(cw - nUp * 0.01f), piece.transform.InverseTransformDirection(-nUp), 6f, false, 7f, "");
            for (int i = 0; i < 3; i++) Gib.Spawn(cw, dir * Random.Range(1f, 2.5f) + Random.insideUnitSphere * 1f + Vector3.up * 0.8f, Random.Range(0.012f, 0.025f));
            Blood.I.Spray(cw, (dir + nUp) * 0.5f, 60, 4f, 0.6f, 0.2f, 1.0f);
            injuries.Add(piece.key.ToUpper() + "  SEVERED");
            if (piece.isHead) Die("DECAPITATED");
            else if (!dead)
            {
                shock = 0.5f;
                if (piece.isLeg) { legFn[piece.key.EndsWith("R") ? 1 : 0] = 0; GoActive(); }
            }
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
