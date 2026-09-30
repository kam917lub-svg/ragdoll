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
        public readonly List<Vector2> holes = new List<Vector2>(); // (height along the bone, angle around it) of every hole

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
        public Mannequin owner;
        public static readonly List<Skin> All = new List<Skin>();

        // where every vertex is right now, in world space: the same linear blend skinning the GPU does
        // (bone.localToWorld * bindpose * rest vertex, weighted). Cached for the current frame.
        Vector3[] wv; Matrix4x4[] bm; int wvFrame = -1; BoneWeight[] wvBw;
        public Vector3[] World()
        {
            if (wv != null && wvFrame == Time.frameCount && ReferenceEquals(wvBw, bw)) return wv;
            wvFrame = Time.frameCount; wvBw = bw;
            var V = BodyMesh.BodyVerts;
            if (wv == null || wv.Length != V.Length) wv = new Vector3[V.Length];
            if (bm == null || bm.Length != bones.Length) bm = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++) bm[i] = bones[i] != null ? bones[i].localToWorldMatrix * bind[i] : Matrix4x4.zero;
            for (int v = 0; v < V.Length; v++)
            {
                var w = bw[v];
                Vector3 p = bm[w.boneIndex0].MultiplyPoint3x4(V[v]) * w.weight0;
                if (w.weight1 > 0f) p += bm[w.boneIndex1].MultiplyPoint3x4(V[v]) * w.weight1;
                wv[v] = p;
            }
            return wv;
        }
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
        float bleed, born; Rigidbody rb;

        public static void Clear() { all.Clear(); }
        public static void ResetMats() { skin = null; }

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
            rb.linearDamping = 0.6f; rb.angularDamping = 6f;   // wet meat: it slaps down and stays, it doesn't roll
            var pm = new PhysicsMaterial("meat") { dynamicFriction = 1.2f, staticFriction = 1.5f, bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Maximum, bounceCombine = PhysicsMaterialCombine.Minimum };
            g.GetComponent<Collider>().sharedMaterial = pm;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var gb = g.AddComponent<Gib>(); gb.rb = rb; gb.bleed = Random.Range(1f, 2.5f); gb.born = Time.time;
            all.Enqueue(g);
            while (all.Count > 140) { var o = all.Dequeue(); if (o != null) Destroy(o); }
        }

        void Update()
        {
            // once it has landed and slowed down, it sticks
            if (rb != null && !rb.isKinematic && Time.time - born > 0.6f && rb.linearVelocity.sqrMagnitude < 0.09f && Physics.Raycast(transform.position, Vector3.down, 0.08f, ~((1 << 2) | (1 << Mannequin.LayerWalk) | (1 << Mannequin.LayerRag))))
            { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.isKinematic = true; }
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
        public static Color SkinColor => BodyMesh.Model == 1 ? new Color(0.9f, 0.56f, 0.5f) : new Color(0.84f, 0.73f, 0.62f);
        // the realistic model is a 1.85 m adult: same skeleton, whole body scaled up
        public static float Scale => BodyMesh.Model == 1 ? 1.08f : 1f;
        static int builtModel = -1;
        // after a model change: the body mesh and cached skin materials are rebuilt for the new model
        public static void ResetStatics() { builtModel = -1; seq = 0; Gib.ResetMats(); }
        public static void LoadModel() { BodyMesh.Model = PlayerPrefs.GetInt("vits_model", 0); }
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
        public float pain; int woundCount;
        float crawlRestT, deadTime;
        float turnRate, headYaw, headYawT, lookT, peekT, peek, stateT, phase, speed, heart, writheT, hurt, crawlT, detourT, stuckT, bestDist, foldT, groundY, groundT, deathT, shock;
        int torsoHits, headHits; bool headMashed; float waistDmg;   // tissue shot away around the waist
        readonly int[] carvedBone = new int[BodyMesh.NB];
        Vector3 target, lastPos, vel, detour, anchor, lastDir = Vector3.forward;
        Part clutch; Vector3 clutchLocal;
        float totalMass; public float TotalMass => totalMass;
        public float heldT = -9f;   // last time the player's grab pulled on this body
        public bool Held => Time.time - heldT < 0.2f;
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
            displayName = "KEKKO #" + seq.ToString("00");
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
            skinMat.color = SkinColor;
            if (BodyMesh.Body == null || builtModel != BodyMesh.Model) { BodyMesh.Build(); builtModel = BodyMesh.Model; }
            Build();
            lastPos = transform.position;
            stateT = Random.Range(0.5f, 3f);
            phase = Random.value;
        }

        void OnDestroy() { All.Remove(this); }

        public List<Part> Parts => allParts;

        void Build()
        {
            transform.localScale = Vector3.one * Scale;
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
                        // neckwear: bow tie, necktie or nothing (black), sitting on the real chest surface
                        int nw = Random.Range(0, 2);   // always wears one
                        if (nw == 0)
                        {
                            Mats.Vis(PrimitiveType.Cube, t, Front(part, -0.024f, 0.42f, 0.004f), new Vector3(0.04f, 0.03f, 0.012f), visorMat, false).transform.localRotation = Quaternion.Euler(0, 0, 12);
                            Mats.Vis(PrimitiveType.Cube, t, Front(part, 0.024f, 0.42f, 0.004f), new Vector3(0.04f, 0.03f, 0.012f), visorMat, false).transform.localRotation = Quaternion.Euler(0, 0, -12);
                            Mats.Vis(PrimitiveType.Cube, t, Front(part, 0f, 0.42f, 0.007f), new Vector3(0.015f, 0.017f, 0.014f), visorMat, false);
                        }
                        else if (nw == 1)
                        {
                            Mats.Vis(PrimitiveType.Cube, t, Front(part, 0f, 0.415f, 0.005f), new Vector3(0.022f, 0.02f, 0.012f), visorMat, false);
                            for (int k = 0; k < 6; k++)
                            {
                                float yy = 0.39f - k * 0.035f, wdt = 0.026f + k * 0.003f;
                                Mats.Vis(PrimitiveType.Cube, t, Front(part, 0f, yy, 0.002f), new Vector3(wdt, 0.038f, 0.005f), visorMat, false);
                            }
                        }
                        break;
                    }
                    case "head":
                    {
                        var c = go.AddComponent<SphereCollider>(); c.center = new Vector3(0, 0.17f, 0.005f); c.radius = 0.12f;
                        // two small black eyes on the face
                        Mats.Vis(PrimitiveType.Sphere, t, Front(part, -0.035f, 0.19f, 0.002f), new Vector3(0.022f, 0.026f, 0.012f), visorMat, false);
                        Mats.Vis(PrimitiveType.Sphere, t, Front(part, 0.035f, 0.19f, 0.002f), new Vector3(0.022f, 0.026f, 0.012f), visorMat, false);
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
            SnapWear(skin);
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
            var sk = new Skin { r = r, mesh = mesh, tris = tris, bw = bw, bones = bones, bind = bind, mats = mats, owner = this };
            Skin.All.Add(sk);
            return sk;
        }

        // eyes, bow tie, necktie: pushed onto the drawn skin (ray from in front of the body onto the real mesh)
        void SnapWear(Skin sk)
        {
            var W = sk.World();
            foreach (var key in new[] { "chest", "head" })
            {
                var t = parts[key].transform;
                foreach (Transform c in t)
                {
                    if (c.name == "stump" || c.GetComponent<Part>() != null) continue;
                    Vector3 lp = c.localPosition;
                    Vector3 o = t.TransformPoint(new Vector3(lp.x, lp.y, 0.4f)), d = -t.forward;
                    float hit = 0.6f;
                    if (RayTris(W, sk.tris, o, d, ref hit) < 0) continue;
                    Vector3 surf = t.InverseTransformPoint(o + d * hit);
                    c.localPosition = new Vector3(lp.x, lp.y, surf.z + c.localScale.z * 0.35f);
                }
            }
        }

        // a point on the front surface of the body at (x, y), 'out' metres proud of the skin
        static Vector3 Front(Part p, float x, float y, float @out)
        {
            var q = p.Project(new Vector3(x, y, 0.3f));
            // the drawn skin is the smooth blend of all segments, a bit fuller than one segment alone
            return new Vector3(x, y, q.z + @out + (p.isTorso ? 0.018f : 0.004f));
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
            if (Blood.I != null) Smears();
            heart += dt * (dead ? 0 : (1.3f + hurt * 0.9f));
            hurt = Mathf.Max(0, hurt - dt * 0.02f);
            shock = Mathf.Max(0, shock - dt * 0.6f);
            pain = Mathf.Max(0, pain - dt * 0.004f);   // pain barely fades while the wounds are open
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
            var hs = Physics.RaycastAll(p + Vector3.up * 0.6f, Vector3.down, 2.5f, ~((1 << 2) | (1 << LayerWalk) | (1 << LayerRag)), QueryTriggerInteraction.Ignore);
            foreach (var h in hs)
            {
                if ((h.rigidbody != null && !h.rigidbody.isKinematic) || h.collider is CharacterController) continue;   // lifts count as ground
                if (!any || h.point.y > best) { best = h.point.y; any = true; }
            }
            return any ? best : cur;
        }

        // the highest solid surface under a point (no bodies, no pieces, not the player)
        // bloody footprints while walking; smears where the body (or a piece) slides over the floor
        readonly Blood.Track feetTr = new Blood.Track();
        readonly Dictionary<Part, Blood.Track> dragTr = new Dictionary<Part, Blood.Track>();
        void Smears()
        {
            if (mode == M.Anim) { Blood.I.Step(feetTr, transform.position, true, Hopping ? 0.45f : 0.62f); return; }
            feetTr.init = false;
            foreach (var p in allParts)
            {
                if (p == null || p.rb == null || p.rb.isKinematic) continue;
                if (!dragTr.TryGetValue(p, out var t)) dragTr[p] = t = new Blood.Track();
                float bleed = 0;
                foreach (var w in wounds) if (w.part == p && w.rate > 0.5f) { bleed = 0.12f; break; }
                var col = p.GetComponent<Collider>();
                Vector3 c = col != null && col.enabled ? col.bounds.center : p.transform.position;
                Blood.I.Drag(t, c, Mathf.Max(0.06f, p.radius * 1.6f), p.radius + 0.06f, bleed);
            }
        }

        public static float GroundBelow(Vector3 p)
        {
            float best = -100f;
            var hs = Physics.RaycastAll(p + Vector3.up * 0.4f, Vector3.down, 200f, ~((1 << 2) | (1 << LayerWalk) | (1 << LayerRag)), QueryTriggerInteraction.Ignore);
            foreach (var h in hs)
            {
                if ((h.rigidbody != null && !h.rigidbody.isKinematic) || h.collider is CharacterController) continue;
                if (h.point.y > best) best = h.point.y;
            }
            return best < -99f ? 0f : best;
        }

        float fallV;
        // walking people are not floating: nothing under the feet -> they drop (a long fall knocks them down)
        void Gravity(float dt)
        {
            Vector3 p = transform.position;
            float g = GroundBelow(p);
            if (p.y > g + 0.02f)
            {
                fallV += 9.81f * dt;
                p.y = Mathf.Max(g, p.y - fallV * dt);
                transform.position = p;
                if (p.y <= g && fallV > 5.5f) { fallV = 0; Knock(transform.forward * 1.5f, true); return; }
            }
            else { if (p.y < g - 0.02f && g - p.y < 0.5f) { p.y = g; transform.position = p; } fallV = 0; }
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
                    // panic: a wound that doesn't stop the legs means a flat-out sprint for cover
                    want = Hopping ? 0.8f : (provoked ? 3.4f : 2.0f);
                    if (provoked) status = "PANIC / SPRINTING TO COVER";
                    float td = Flat(target - pos).magnitude;
                    if (provoked && !Hopping && speed > 2.5f && td < 1.8f && td > 0.9f && Time.time > diveT) { diveT = Time.time + 15f; Dive(); return; }
                    if (td < 0.6f) { state = S.Cover; stateT = Random.Range(10f, 20f); }
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

            Vector3 goal = detourT > 0 ? detour : Level.Route(pos, target);
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
                float gait = Hopping ? Mathf.Max(0f, Mathf.Sin(phase * Mathf.PI * 2f)) * 2f : 1f; // hop: forward only while airborne
                Vector3 fwd = transform.forward, step = fwd * speed * Mathf.Max(0.25f, turnSlow) * gait * dt;
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
                // up a step (< 0.5 m) or down a step; never walk off a platform edge - use the stairs
                if (gy - pos.y < 0.5f && pos.y - gy < 0.45f) np.y = Mathf.MoveTowards(pos.y, gy, dt * 3f);   // a step up or down
                else if (gy < pos.y && pos.y - gy < 1.3f) np.y = pos.y;                                  // hop down off a box: gravity does it
                else np = pos;
                transform.position = np;
                float moved = new Vector3(np.x - pos.x, 0, np.z - pos.z).magnitude;
                phase += Hopping ? dt * 1.6f : moved / Stride();
            }
            Gravity(dt);
            vel = Vector3.ClampMagnitude((transform.position - lastPos) / dt, 2.5f); lastPos = transform.position;
            Bumps(dt);
        }

        // leg swing amplitude grows with speed; one gait cycle covers two steps of real leg length
        float Amp() => Mathf.Lerp(12f, state == S.Flee ? 38f : 26f, Mathf.Clamp01(speed / 2.2f)) * Mathf.Clamp01(speed * 4f);
        float Stride() => Scale * Mathf.Max(0.3f, 4f * 0.9f * Mathf.Sin(Mathf.Max(8f, Amp()) * Mathf.Deg2Rad));

        // pushed by the player, other people, flying objects; slipping on blood
        float bumpT, stumble;
        void Bumps(float dt)
        {
            bumpT -= dt; stumble = Mathf.Max(0f, stumble - dt * 2f);
            if (bumpT > 0 || mode != M.Anim) return;
            Vector3 pos = transform.position, chest = pos + Vector3.up * 1.1f;
            // the player walking/running into them
            var pl = Game.I != null ? Game.I.player : null;
            if (pl != null)
            {
                Vector3 d = pos - pl.transform.position; d.y = 0;
                Vector3 pv = pl.moveVel; pv.y = 0;
                if (d.magnitude < 0.8f && Vector3.Dot(pv, d.normalized) > 0.8f) { Knock(pv * 1.6f, pv.magnitude > 4.5f ? Random.value < 0.75f : Random.value < 0.2f); return; }
            }
            // other people bumping into them (both moving)
            foreach (var o in All)
            {
                if (o == this || o.mode != M.Anim || o.dead) continue;
                Vector3 d = pos - o.transform.position; d.y = 0;
                float rel = Vector3.Dot(o.vel - vel, d.normalized);
                if (d.magnitude < 0.6f && rel > 1.2f) { Knock(d.normalized * rel * 0.7f, rel > 3f && Random.value < 0.5f); o.bumpT = 0.8f; return; }
            }
            // flying things (pieces, bodies, crates)
            int n = Physics.OverlapSphereNonAlloc(chest, 0.45f, buf, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var rb = buf[i].attachedRigidbody;
                if (rb == null || rb.isKinematic || buf[i].transform.IsChildOf(transform)) continue;
                float sp = rb.linearVelocity.magnitude;
                if (sp > 2.5f && rb.mass > 0.5f) { Knock(rb.linearVelocity * Mathf.Min(1f, rb.mass / 10f), sp * rb.mass > 20f); return; }
            }
            // running through a blood pool
            if (speed > 1.4f && Blood.I != null && Blood.I.PoolAt(pos) && Random.value < dt * 1.5f) Knock(transform.forward * 1.5f + Vector3.up, true);
        }

        void Knock(Vector3 push, bool fall)
        {
            bumpT = 0.8f;
            if (!fall)
            {
                // stumble: pushed a step, torso rocks, then walks on
                Vector3 np = Game.Clamp(transform.position + new Vector3(push.x, 0, push.z) * 0.22f);
                if (Free(np, false)) transform.position = np;
                stumble = 1f;
                return;
            }
            lastHitTime = Time.time; shock = 1f; provoked = true;
            injuries.Add("KNOCKED DOWN");
            GoActive();
            parts["chest"].rb.AddForce(push * parts["chest"].rb.mass * 0.6f, ForceMode.Impulse);
            parts["pelvis"].rb.AddForce(-push * parts["pelvis"].rb.mass * 0.2f, ForceMode.Impulse);
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

            float a = Amp();
            float s = Mathf.Sin(phase * Mathf.PI * 2f);
            float kneeL = Mathf.Max(0, Mathf.Sin(phase * Mathf.PI * 2f - 1.2f)) * a * 1.5f;
            float kneeR = Mathf.Max(0, Mathf.Sin(phase * Mathf.PI * 2f + Mathf.PI - 1.2f)) * a * 1.5f;
            float thL = -s * a, thR = s * a;
            // body rises over the standing leg twice per cycle, hips sway side to side
            float bob = Mathf.Abs(Mathf.Cos(phase * Mathf.PI * 2f)) * 0.022f * Mathf.Clamp01(a / 20f);
            float pelvisY = 0.935f + bob;
            if (Hopping)
            {
                // one bad leg: keep it off the ground and hop on the good one
                bool badL = legFn[0] < legFn[1];
                float hop = Mathf.Max(0f, Mathf.Sin(phase * Mathf.PI * 2f)); // airborne half of the hop
                if (badL) { thL = -25f; kneeL = 75f; thR = -8f * hop; kneeR = 12f + 25f * (1f - hop); }
                else { thR = -25f; kneeR = 75f; thL = -8f * hop; kneeL = 12f + 25f * (1f - hop); }
                pelvisY = 0.9f + 0.07f * hop;
            }
            // crouch in cover: knees bent, back fairly straight, hands resting forward
            pelvisY -= crouch * 0.3f;
            Set("thighL", Quaternion.Euler(thL - crouch * 55f, 0, -crouch * 6f), k); Set("thighR", Quaternion.Euler(thR - crouch * 55f, 0, crouch * 6f), k);
            Set("shinL", Quaternion.Euler(kneeL + crouch * 85f, 0, 0), k); Set("shinR", Quaternion.Euler(kneeR + crouch * 85f, 0, 0), k);
            Set("uarmL", Quaternion.Euler(s * a * 0.7f - crouch * 25f, 0, -4 - crouch * 4f), k); Set("uarmR", Quaternion.Euler(-s * a * 0.7f - crouch * 25f, 0, 4 + crouch * 4f), k);
            Set("farmL", Quaternion.Euler(-10 - a * 0.3f - crouch * 35f, 0, 0), k); Set("farmR", Quaternion.Euler(-10 - a * 0.3f - crouch * 35f, 0, 0), k);
            float lean = hurt * 14f + (state == S.Flee ? 8f : 0f) + crouch * 18f;
            float sway = s * a * 0.12f;
            lean += stumble * 20f;
            Set("pelvis", Quaternion.Euler(0, -s * a * 0.15f, sway * 0.5f), k);
            Set("chest", Quaternion.Euler(lean, headYaw * 0.25f + s * a * 0.2f, -sway * 0.6f), k);
            Set("head", Quaternion.Euler(-lean * 0.5f + (state == S.Idle ? 4f : 0f), headYaw * 0.75f, 0), k);
            var pv = parts["pelvis"].transform;
            pv.localPosition = Vector3.Lerp(pv.localPosition, new Vector3(0, pelvisY, 0), k);
            if (clutch != null && !clutch.severed)
            {
                string arm = ClutchArm();
                if (!parts["farm" + arm].severed && !parts["uarm" + arm].severed)
                {
                    if (HandIK(arm, out Quaternion qu, out Quaternion qf)) { Set("uarm" + arm, qu, k); Set("farm" + arm, qf, k); }
                }
            }
        }

        // two-bone IK: shoulder -> elbow -> palm onto the wound (real arm lengths; if out of reach, stretch toward it)
        static readonly Vector3[] POLES = {
            new Vector3(0.6f, -1f, -0.4f), new Vector3(1f, -0.6f, -0.3f), new Vector3(1f, -0.2f, -0.5f),
            new Vector3(1f, 0.2f, -0.2f), new Vector3(0.7f, -0.7f, -1f), new Vector3(1f, 0.5f, 0.2f), new Vector3(0.3f, -0.5f, -1f) };

        // distance from the torso surface (chest, pelvis, head) minus the arm's own thickness
        float Clear(Vector3 w)
        {
            float c = 9f;
            foreach (var k in TORSO)
                if (parts.TryGetValue(k, out Part p) && p != null && !p.severed)
                    c = Mathf.Min(c, p.Sdf(p.transform.InverseTransformPoint(w)));
            return c - 0.04f;
        }
        static readonly string[] TORSO = { "chest", "pelvis", "head" };

        bool HandIK(string arm, out Quaternion qu, out Quaternion qf)
        {
            qu = qf = Quaternion.identity;
            var ch = parts["chest"]; var ua = parts["uarm" + arm];
            if (clutch == null || clutch.severed) return false;
            float L1 = 0.3f * Scale, L2 = 0.3f * Scale;
            Vector3 wn = clutch.transform.TransformDirection(clutch.Normal(clutchLocal));
            Vector3 t = clutch.transform.TransformPoint(clutchLocal) + wn * 0.035f;   // palm pressed on the skin
            Vector3 s = ua.transform.position;
            Vector3 to = t - s; float d = to.magnitude;
            if (d < 1e-4f) return false;
            Vector3 dir = to / d; d = Mathf.Clamp(d, 0.1f, L1 + L2 - 0.005f);
            float side = arm == "R" ? 1f : -1f;
            float a = Mathf.Acos(Mathf.Clamp((L1 * L1 + d * d - L2 * L2) / (2f * L1 * d), -1f, 1f));
            // elbow points down, a bit out and back, like a real arm; if that drives the arm through the torso, swing the elbow wider
            Vector3 u = Vector3.down, e = s, f = Vector3.down; float bestC = -9f;
            for (int i = 0; i < POLES.Length; i++)
            {
                var pl = POLES[i];
                Vector3 pole = ch.transform.TransformDirection(new Vector3(pl.x * side, pl.y, pl.z)).normalized;
                Vector3 perp = pole - dir * Vector3.Dot(pole, dir);
                if (perp.sqrMagnitude < 1e-6f) continue;
                perp.Normalize();
                Vector3 uu = dir * Mathf.Cos(a) + perp * Mathf.Sin(a);
                Vector3 ee = s + uu * L1, hand = s + dir * d;
                float c = 9f;
                for (int k = 1; k <= 4; k++) c = Mathf.Min(c, Clear(Vector3.Lerp(s, ee, k / 4f)));
                for (int k = 1; k <= 3; k++) c = Mathf.Min(c, Clear(Vector3.Lerp(ee, hand, k * 0.22f)));
                if (c > bestC + 0.005f) { bestC = c; u = uu; e = ee; f = (hand - ee).normalized; }
                if (c > 0.045f) break;   // clear of the body: keep the most natural pose
            }
            // to local rotations (rest pose: segments point down -Y)
            Quaternion chestRot = ch.transform.rotation;
            qu = Quaternion.FromToRotation(Vector3.down, Quaternion.Inverse(chestRot) * u);
            Quaternion uaRot = chestRot * qu;
            qf = Quaternion.FromToRotation(Vector3.down, Quaternion.Inverse(uaRot) * f);
            return true;
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

        string ClutchArm()
        {
            if (clutch.isArm) return clutch.key.EndsWith("R") ? "L" : "R";
            Vector3 w = clutch.transform.TransformPoint(clutchLocal);
            float dl = (parts["uarmL"].transform.position - w).sqrMagnitude, dr = (parts["uarmR"].transform.position - w).sqrMagnitude;
            if (parts["farmL"].severed || parts["uarmL"].severed) return "R";
            if (parts["farmR"].severed || parts["uarmR"].severed) return "L";
            return dl < dr ? "L" : "R";
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

        // only get up from the floor: not in mid-air (thrown, falling, dropped from the grab), not while sliding
        bool OnGround()
        {
            var pel = parts["pelvis"];
            Vector3 p = pel.transform.position;
            return p.y - GroundBelow(p + Vector3.up * 0.3f) < 1.15f && pel.rb.linearVelocity.magnitude < 1.5f;
        }

        // back on its feet and able to walk (only light wounds)
        void Recover()
        {
            var pel = parts["pelvis"];
            Vector3 p = pel.transform.position;
            Vector3 fwd = Flat(parts["chest"].transform.forward);
            if (fwd.sqrMagnitude < 0.01f) fwd = transform.forward;
            float gy = GroundBelow(new Vector3(p.x, p.y + 0.3f, p.z)); fallV = 0;
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
            float want = Mathf.Min(strength, legs) - shock - pain * 0.1f;
            support = Mathf.MoveTowards(support, Mathf.Clamp01(want), dt * (want < support ? 0.45f : 0.12f));
            float kneel = Mathf.InverseLerp(0.72f, 0.45f, support);   // 0 standing .. 1 kneeling
            bool down = support < 0.35f;

            float cl = 20f * hurt;
            if (!down)
            {
                status = kneel > 0.5f ? "HURT / KNEELING" : "HURT / STANDING";
                T("chest", 10f + cl + kneel * 12f); T("head", -8f + kneel * 10f);
                // going down on one knee (the weaker leg), the other foot planted in front
                bool kneeL = legFn[0] <= legFn[1];
                T(kneeL ? "thighL" : "thighR", Mathf.Lerp(-4f, 5f, kneel)); T(kneeL ? "shinL" : "shinR", Mathf.Lerp(6f, 100f, kneel));
                T(kneeL ? "thighR" : "thighL", Mathf.Lerp(-4f, -80f, kneel)); T(kneeL ? "shinR" : "shinL", Mathf.Lerp(6f, 85f, kneel));
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
                if (conscious && !crawling) Writhe(dt * (1f + pain * 3f));
                if (conscious && !crawling && strength > 0.15f && Time.time - lastHitTime > 0.25f && (Time.time > crawlRestT || Time.time - lastHitTime < 1f)) StartCrawl();   // down: straight away, drag yourself off
            }
            if (clutch != null && !clutch.severed && conscious)
            {
                string arm = ClutchArm();
                if (!parts["farm" + arm].severed && !parts["uarm" + arm].severed)
                {
                    if (HandIK(arm, out Quaternion qu, out Quaternion qf)) { Target(parts["uarm" + arm], qu); Target(parts["farm" + arm], qf); }
                }
            }
            // light wounds only, standing and steady for a while: walk (or hop) away
            // legs still work: get up and run (hand on the wound) as soon as the jolt is over - pain drives you away, it doesn't keep you sitting
            if (!Held && Time.time - heldT > 1.5f && !crawling && Mathf.Max(legFn[0], legFn[1]) > 0.6f && Mathf.Min(legFn[0], legFn[1]) > 0.3f && strength > 0.5f && Time.time - lastHitTime > 0.7f && shock < 0.3f && OnGround())
                Recover();
        }

        // the instant reaction to a bullet: the body jerks, curls toward the wound
        void Flinch(Part p, Vector3 dir)
        {
            shock = Mathf.Max(shock, 0.25f + pain * 0.5f);
            if (mode == M.Anim) { stumble = 1f; return; }
            if (!conscious) return;
            float k = 0.6f + pain * 1.4f;
            var ch = parts["chest"];
            ch.rb.AddTorque(Vector3.Cross(ch.transform.up, (p.transform.position - ch.transform.position).normalized + Vector3.down * 0.3f) * ch.rb.mass * 3f * k, ForceMode.Impulse);
            foreach (var q in parts.Values)
                if (!q.severed && (q.isArm || q.isLeg) && q.rb != null)
                    q.rb.AddForce((Random.insideUnitSphere * 0.6f + Vector3.up * 0.3f) * q.rb.mass * k, ForceMode.Impulse);
            writheT = 0;
        }

        float diveT, lastVy;
        // hitting the ground hard: ~9 m/s (4 m) breaks legs, ~14 m/s (10 m) and up is usually fatal
        void Landed(float speed)
        {
            injuries.Add($"FALL IMPACT {speed:0} M/S");
            Blood.I.Spray(parts["pelvis"].transform.position, Vector3.up, (int)(speed * 8f), speed * 0.25f, 1f, 0.2f, 1f);
            if (speed > 14f && Random.value < 0.85f) { Die("FALL"); return; }
            legFn[0] *= Mathf.Clamp01(1.6f - speed / 10f); legFn[1] *= Mathf.Clamp01(1.6f - speed / 10f);
            shock = 1f; pain = Mathf.Min(1f, pain + speed * 0.05f);
            if (legFn[0] < 0.5f) injuries.Add("LEFT LEG  FRACTURED"); if (legFn[1] < 0.5f) injuries.Add("RIGHT LEG  FRACTURED");
        }
        // throws itself the last metre behind the wall, lands on the floor, then gets up there
        void Dive()
        {
            Vector3 d = Flat(target - transform.position).normalized;
            injuries.Add("DIVED FOR COVER");
            GoActive();
            shock = 0.5f; lastHitTime = Time.time;
            foreach (var q in parts.Values)
                if (!q.severed && q.rb != null) q.rb.linearVelocity = d * 4f + Vector3.up * 2.2f;
            parts["chest"].rb.AddTorque(Vector3.Cross(Vector3.up, d) * parts["chest"].rb.mass * 2f, ForceMode.Impulse);
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
            if (Held) { anchor = pel.transform.position; if (conscious) Writhe(Time.fixedDeltaTime * 2f); return; }   // dangling in your grip, kicking
            // falling: no muscle can hold you up in the air (this was the 'gliding' after a drop)
            float fallH = pel.transform.position.y - GroundBelow(pel.transform.position + Vector3.up * 0.2f);
            float vy = pel.rb.linearVelocity.y;
            if (lastVy < -9f && vy > lastVy + 6f && fallH < 1.3f) Landed(-lastVy);
            lastVy = vy;
            if (fallH > 1.4f) return;
            if (crawling) { Crawl(); return; }
            float s = support;
            if (s < 0.35f) return;
            groundT -= Time.fixedDeltaTime;
            if (groundT <= 0) { groundT = 0.25f; groundY = Ground(pel.transform.position, pel.transform.position.y - 0.95f * Scale); }
            float kneel = Mathf.InverseLerp(0.72f, 0.45f, s);
            float targetH = Mathf.Lerp(0.93f, 0.55f, kneel) * Scale;
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
            var ch = parts["chest"]; var pel = parts["pelvis"];
            Vector3 cp = ch.transform.position;
            if (Flat(target - cp).magnitude < 0.8f && Mathf.Abs(target.y - cp.y) < 1.2f) { crawling = false; crawlRestT = Time.time + Random.Range(6f, 12f); injuries.Add("REACHED COVER"); return; }
            Vector3 wp = Level.Route(new Vector3(cp.x, cp.y - 0.25f, cp.z), target);
            Vector3 d = Flat(wp - cp).normalized;
            // drag the whole body along the floor (friction would pin 70 kg otherwise): arms pull in bursts, legs trail
            float spd = Mathf.Clamp01(strength * 1.4f) * (0.35f + pain * 0.55f);
            float burst = Mathf.Max(0.25f, Mathf.Sin(crawlT * 3.5f) + 0.3f);
            foreach (var q in parts.Values)
            {
                if (q.severed || q.rb == null) continue;
                Vector3 v = q.rb.linearVelocity, hv = Flat(v);
                q.rb.AddForce((d * spd * burst - hv) * 0.12f, ForceMode.VelocityChange);
            }
            // a step or stair edge ahead: haul the chest up onto it
            if (Physics.Raycast(cp + Vector3.up * 0.1f, d, out RaycastHit sh, 0.6f, ~((1 << 2) | (1 << LayerWalk) | (1 << LayerRag)), QueryTriggerInteraction.Ignore) && sh.normal.y < 0.5f)
            {
                ch.rb.AddForce(Vector3.up * totalMass * 6f + d * totalMass * 2f);
                pel.rb.AddForce(Vector3.up * totalMass * 3f);
            }
            // head first toward where you are going
            Vector3 hf = Flat(ch.transform.up); if (hf.sqrMagnitude < 0.01f) hf = Flat(ch.transform.forward);
            ch.rb.AddTorque(Vector3.up * Vector3.SignedAngle(hf, d, Vector3.up) * 0.4f);
            // adrenaline: the more it hurts, the faster and more frantic the pulls
            float k = Mathf.Clamp01(strength * 1.3f) * (1f + pain * 0.8f);
            crawlT += Time.fixedDeltaTime * (1f + pain * 0.85f);
            float pull = Mathf.Max(0, Mathf.Sin(crawlT * 3.5f));
            ch.rb.AddForce(d * 300f * k * pull + Vector3.up * 70f * k);
            parts["pelvis"].rb.AddForce(d * 90f * k * pull);
            if (!parts["head"].severed) parts["head"].rb.AddForce(Vector3.up * 30f * k);
            string arm = ((int)(crawlT * 3.5f / Mathf.PI)) % 2 == 0 ? "L" : "R";
            var fa = parts["farm" + arm];
            if (!fa.severed) fa.rb.AddForce((d * 50f + Vector3.up * 22f) * k * (1f - pull));
            if (strength < 0.12f) crawling = false;
        }

        void StartCrawl()
        {
            if (!conscious || dead) return;
            crawling = true;
            // away from the shooter: a cover behind you if there is one close, otherwise just away
            Vector3 pos = parts["chest"].transform.position, shooter = Game.I != null && Game.I.player != null ? Game.I.player.transform.position : pos;
            Vector3 c = Game.CoverPoint(pos, Vector3.one * 999f);
            Vector3 away = Flat(pos - shooter); if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            target = (c - shooter).magnitude > (pos - shooter).magnitude && (c - pos).magnitude < 8f ? c : Game.Clamp(pos + away.normalized * 8f);
        }

        // kept for callers: hurt -> physical body; dead -> limp
        public void GoRagdoll() { if (dead) GoLimp(); else GoActive(); }

        // how a body goes down is different every time: depends on the person, the wound and where the shot came from
        void GoLimp()
        {
            bool wasStanding = mode == M.Anim || (mode == M.Active && support > 0.5f);
            if (mode == M.Anim) MakeDynamic();
            mode = M.Limp;
            var pel = parts["pelvis"];
            Vector3 fwd = pel.transform.forward; fwd.y = 0; fwd.Normalize();
            float fromFront = -Vector3.Dot(lastDir, fwd);   // > 0: shot from the front, the push goes backward
            float r = Random.value;
            int style;
            if (!wasStanding) style = 0;                                       // already down: just go slack
            else if (cause == "HEADSHOT") style = r < 0.3f ? 1 : r < 0.5f ? 2 : r < 0.7f ? 3 : r < 0.85f ? 4 : 5;
            else style = r < 0.35f ? 1 : r < 0.6f ? 4 : r < 0.8f ? 5 : 3;
            float side = Random.value < 0.5f ? -1f : 1f;
            Vector3 push = Vector3.zero; float tone = 0.3f; foldT = 0.55f;
            switch (style)
            {
                case 1: // knees buckle, folds down on the spot
                    T("thighL", -70f); T("thighR", -65f); T("shinL", 110f); T("shinR", 105f); T("chest", 25f); T("head", 20f);
                    T("uarmL", -15f, -10f); T("uarmR", -15f, 10f); T("farmL", -40f); T("farmR", -40f);
                    break;
                case 2: // "fencing" posture: arms flex up, elbows bent, body stiff, topples over
                    T("thighL", -5f); T("thighR", -5f); T("shinL", 5f); T("shinR", 5f); T("chest", -5f); T("head", -15f);
                    T("uarmL", -100f, -25f); T("uarmR", -70f, 25f); T("farmL", -120f); T("farmR", -90f);
                    tone = 0.55f; foldT = 0.9f; push = -fwd * Mathf.Sign(fromFront + 0.01f) * 1.2f;
                    break;
                case 3: // goes stiff and falls like a plank, backward or forward depending on the shot
                    T("thighL", 0f); T("thighR", 0f); T("shinL", 3f); T("shinR", 3f); T("chest", 0f); T("head", 0f);
                    T("uarmL", -10f, -6f); T("uarmR", -10f, 6f); T("farmL", -15f); T("farmR", -15f);
                    tone = 0.6f; foldT = 1.0f; push = (fromFront > -0.2f ? -fwd : fwd) * 1.6f;
                    break;
                case 4: // drops to the knees, then tips forward
                    T("thighL", 5f); T("thighR", 5f); T("shinL", 115f); T("shinR", 115f); T("chest", 35f); T("head", 30f);
                    T("uarmL", -5f, -5f); T("uarmR", -5f, 5f); T("farmL", -20f); T("farmR", -20f);
                    tone = 0.45f; foldT = 0.8f; push = fwd * 0.5f;
                    break;
                default: // twists and collapses sideways, one leg gives
                    Target(parts["chest"], Quaternion.Euler(15f, 35f * side, 10f * side)); T("head", 20f, 15f * side);
                    T(side > 0 ? "thighL" : "thighR", -60f); T(side > 0 ? "shinL" : "shinR", 100f);
                    T(side > 0 ? "thighR" : "thighL", -10f); T(side > 0 ? "shinR" : "shinL", 20f);
                    T("uarmL", -30f, -20f); T("uarmR", -30f, 20f); T("farmL", -60f); T("farmR", -60f);
                    push = pel.transform.right * side * 0.8f;
                    break;
            }
            // everybody is a bit different
            tone *= Random.Range(0.8f, 1.2f); foldT *= Random.Range(0.8f, 1.25f);
            Tone(tone);
            if (push != Vector3.zero)
            {
                parts["chest"].rb.AddForce(push * parts["chest"].rb.mass, ForceMode.Impulse);
                if (!parts["head"].severed) parts["head"].rb.AddForce(push * parts["head"].rb.mass, ForceMode.Impulse);
            }
        }

        public void Die(string why)
        {
            if (dead) return;
            dead = true; deadTime = Time.time; conscious = false; crawling = false; cause = why; status = "DEAD / " + why;
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
            catch (System.Exception e) { Debug.LogException(e); Game.LastShot = "HIT ERROR: " + e.Message; }
        }

        void DoHit(Part p, Vector3 pt, Vector3 dir)
        {
            provoked = true;
            // pain adds up and each new wound hurts more than the last (exponential), capped at 1
            woundCount++;
            pain = Mathf.Min(1f, pain + 0.07f * Mathf.Pow(1.3f, woundCount - 1) * (p.isHead || p.isTorso ? 1.3f : 1f));
            if (!dead) Flinch(p, dir);
            p.hits++; lastHit = p.key.ToUpper(); lastHitTime = Time.time; lastDir = dir;
            hurt = Mathf.Min(1, hurt + 0.3f);
            var B = Blood.I;

            Vector3 inL = p.transform.InverseTransformPoint(pt);
            // the entry is where the bullet met the skin: only snap to the body shape if it is right there (+-3 cm)
            if (Trace(p, pt - dir * 0.03f, dir, 0.06f, out Vector3 tl)) inL = tl;
            bool exits = Trace(p, pt + dir * 0.7f, -dir, 0.75f, out Vector3 outL);
            // exit on the drawn skin, if the skin is there (bent joints move it away from the rigid body shape)
            Vector3 outSkin = Vector3.zero; bool skinExit = false;
            if (exits && p.skin != null)
            {
                float tt = 0.69f;
                if (RayTris(p.skin.World(), p.skin.tris, pt + dir * 0.7f, -dir, ref tt) >= 0) { outSkin = pt + dir * (0.7f - tt); skinExit = (outSkin - p.transform.TransformPoint(outL)).sqrMagnitude < 0.06f * 0.06f; }
            }
            Vector3 dirL = p.transform.InverseTransformDirection(dir);
            Vector3 inW = p.transform.TransformPoint(inL), inN = p.transform.TransformDirection(p.Normal(inL));
            Vector3 outW = exits ? p.transform.TransformPoint(outL) : inW, outN = exits ? p.transform.TransformDirection(p.Normal(outL)) : dir;

            // 9 mm: a small dark hole in, a slightly bigger torn one out
            B.SkinDecal(p.transform, inL, p.Normal(inL), 0.008f, 5);
            if (exits) B.SkinDecal(p.transform, outL, p.Normal(outL), Player.AWP ? 0.035f : Player.AK ? 0.02f : 0.014f, 5);

            // flesh is torn away: a small piece at the entry, a bigger one at the exit, and it flies off
            float rin = Player.AWP ? 0.028f : Player.AK ? 0.02f : 0.015f, rout = (Player.AWP ? 0.06f : Player.AK ? 0.034f : 0.025f) * (p.isHead ? (Player.AK ? 1.9f : 1.8f) : 1f);   // skull exit wounds are big and ragged
            Carve(p, pt, rin);
            if (exits) { Carve(p, skinExit ? outSkin : outW, rout); Gib.Spawn(outW + outN * 0.02f, dir * Random.Range(2f, 4.5f) + Random.insideUnitSphere + Vector3.up * 0.6f, rout * Random.Range(0.8f, 1.2f)); }
            float cal = Player.AWP ? 2.5f : Player.AK ? 1.5f : 1f;   // bigger round, more tissue destroyed, more blood thrown
            B.Spray(inW + inN * 0.01f, (-dir + inN) * 0.5f, (int)((p.isHead ? 30 : 22) * cal), p.isHead ? 2.2f : 1.5f, 0.5f, 0.05f, 0.4f);
            if (exits) B.Spray(outW + outN * 0.01f, dir, (int)((p.isHead ? 110 : 90) * cal), p.isHead ? 6f : 4f, p.isHead ? 0.55f : 0.4f, 0.1f, p.isHead ? 0.7f : 1.2f);   // a head exit is a fine mist + bits, ~30-60 ml, not a bucket
            if (p.isHead)
            {
                // the scalp bleeds freely but a skull holds only so much: a short run of drops, not a bucket
                Vector3 src = exits ? outW : inW;
                B.Spray(src, Vector3.down + dir * 0.3f, (int)(18 * cal), 0.8f, 0.5f, 0.2f, 0.8f);
            }
            if (exits && (p.isHead || Random.value < 0.35f)) Gib.Spawn(outW, dir * Random.Range(1.5f, 3.5f) + Random.insideUnitSphere + Vector3.up * 0.8f, Random.Range(0.01f, 0.02f));

            AddWound(p, inL, p.Normal(inL), (p.isHead ? 3f : p.isTorso ? 2.5f : 1.5f) * cal, false, 0, "");
            if (exits) AddWound(p, outL, p.Normal(outL), (p.isHead ? 7f : p.isTorso ? 6f : 4f) * cal, false, 0, "");

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
                // pistol / AK: a big torn exit wound, not an explosion. Only the .338 bursts the skull.
                if (Player.AWP && !headMashed) MashHead(p);
                else if (headHits >= 7 && !headMashed) SeverJoint(p, dir);
            }
            else if (p.isTorso)
            {
                torsoHits++;
                injuries.Add((p.key == "chest" ? "CHEST" : "ABDOMEN") + "  GUNSHOT" + (exits ? "  THROUGH" : ""));
                // cutting a Carl in half takes a lot: the belly has to be shot away all around the waist, magazine after
                // magazine (about 4 pistol mags, 2 AK mags, 7 AWP rounds if they all land near the waist)
                var chW = parts["chest"];
                if (!chW.severed)
                {
                    float d = (pt - chW.transform.position).magnitude;
                    float near = Mathf.Clamp01(1f - (d - 0.12f) / 0.2f);
                    float before = waistDmg;
                    waistDmg += near * (Player.AWP ? 8f : Player.AK ? 1.3f : 1f);
                    if (before < WaistSplit * 0.5f && waistDmg >= WaistSplit * 0.5f) injuries.Add("ABDOMEN  SHREDDED");
                    if (near > 0f)
                    {
                        // the more is gone, the bigger each new wound tears and the more meat flies
                        float k = Mathf.Clamp01(waistDmg / WaistSplit);
                        Carve(p, pt, 0.02f + 0.03f * k);
                        for (int g = 0; g < 1 + (int)(k * 3f); g++)
                            Gib.Spawn(pt - dir * 0.02f, (-dir * 0.6f + Random.insideUnitSphere) * Random.Range(1f, 3f) + Vector3.up, Random.Range(0.012f, 0.024f));
                    }
                    if (waistDmg >= WaistSplit) SplitInHalf(dir);
                }
                if (!dead)
                {
                    clutch = p; clutchLocal = inL; shock = 0.35f;
                    if (Player.AWP)
                    {
                        // .338 Lapua through the torso: hydrostatic shock, massive cavity - straight down, no walking away
                        shock = 1f; pain = 1f; legFn[0] *= 0.15f; legFn[1] *= 0.15f;
                        AddWound(p, exits ? outL : inL, p.Normal(exits ? outL : inL), 45f, true, 0, "AWP WOUND CAVITY");
                        B.Spray(exits ? outW : inW, dir + Vector3.up * 0.2f, 400, 6f, 0.7f, 0.3f, 2.5f);
                        B.Spray(inW, Vector3.down, 200, 1.2f, 0.8f, 0.8f, 3f);
                        injuries.Add("AWP  TORSO  CAVITY");
                        if (Random.value < 0.4f) Die("AWP TORSO"); else { deathT = Time.time + Random.Range(4f, 12f); GoActive(); }
                    }
                    else if (torsoHits >= 5) Die("MASSIVE TRAUMA");
                    else if (torsoHits >= 3 || pain > 0.6f || art >= 0 || Random.value < 0.25f) GoActive();   // doubles over
                    else if (mode == M.Anim) Flee();                                                          // hand on it, and runs at once
                }
            }
            else if (p.isArm || p.isLeg)
            {
                injuries.Add(p.key.ToUpper() + "  GUNSHOT");
                if (!p.severed && !dead) { clutch = p; clutchLocal = inL; }
                if (p.isLeg && !p.severed) legFn[p.key.EndsWith("R") ? 1 : 0] -= 0.45f;
                // the limb comes off where a line of holes goes right across it
                p.holes.Add(new Vector2(-inL.y, Mathf.Atan2(inL.x, inL.z) * Mathf.Rad2Deg));
                bool cut = CutLine(p, out float cy);
                if (cut) { SeverAt(p, cy, dir); p.holes.Clear(); }
                else if (p.isLeg && !dead && !p.severed)
                {
                    bool bothBad = legFn[0] < 0.6f && legFn[1] < 0.6f, gone = Mathf.Min(legFn[0], legFn[1]) < 0.15f;
                    if (bothBad || gone || art >= 0 || pain > 0.55f || Random.value < 0.2f) { shock = 0.3f; GoActive(); }
                    else { shock = 0.2f; if (mode == M.Anim) Flee(); } // hops away on the good leg
                }
                else if (!dead) { shock = Mathf.Max(shock, 0.15f); if (mode == M.Anim) Flee(); }
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
        // enough holes at about the same height, spread around the limb? (pistol needs more than the AK)
        static bool CutLine(Part p, out float cy)
        {
            cy = 0; int need = Player.AWP ? 1 : Player.AK ? 3 : 5;
            if (Player.AWP && p.holes.Count > 0) { cy = p.holes[p.holes.Count - 1].x; return true; }   // .338 Lapua: one hit tears the limb off
            foreach (var h in p.holes)
            {
                int n = 0; float lo = 999, hi = -999, sum = 0;
                var angs = new List<float>();
                foreach (var o in p.holes) if (Mathf.Abs(o.x - h.x) < 0.05f) { n++; sum += o.x; angs.Add(o.y); }
                if (n < need) continue;
                angs.Sort(); float gap = 0;
                for (int i = 0; i < angs.Count; i++) { float g = (i + 1 < angs.Count ? angs[i + 1] : angs[0] + 360f) - angs[i]; if (g > gap) gap = g; }
                float cover = 360f - gap; _ = lo; _ = hi;
                if (cover >= 90f || n >= need + 2) { cy = sum / n; return true; }
            }
            return false;
        }

        // tear the skin away around a world point: the flesh inside shows, the piece flies off
        void Carve(Part p, Vector3 world, float r)
        {
            // in world space, on the skin exactly as it is drawn (bent joints included)
            var sk = p.skin; if (sk == null) return;
            var V = sk.World(); var D = BodyMesh.Dom; float r2 = r * r; Vector3 rest = world;
            var keep = new List<int>(sk.tris.Count); int removed = 0;
            for (int t = 0; t < sk.tris.Count; t += 3)
            {
                int a = sk.tris[t], b = sk.tris[t + 1], c = sk.tris[t + 2];
                if (((V[a] + V[b] + V[c]) / 3f - rest).sqrMagnitude < r2) { removed++; carvedBone[D[a]]++; continue; }
                keep.Add(a); keep.Add(b); keep.Add(c);
            }
            if (removed == 0) return;
            sk.tris = keep; sk.mesh.SetTriangles(keep, 0);
            if (p.isHead && !headMashed && carvedBone[BodyMesh.HEA] > BodyMesh.TrisPerBone[BodyMesh.HEA] * 0.6f) MashHead(p);   // only after very many hits
        }

        // too much of the head is gone: it bursts
        void MashHead(Part p)
        {
            headMashed = true;
            var sk = p.skin; var D = BodyMesh.Dom; var V = BodyMesh.BodyVerts;
            var keep = new List<int>(sk.tris.Count);
            for (int t = 0; t < sk.tris.Count; t += 3)
            {
                int a = sk.tris[t];
                // keep the neck, remove the skull
                if (D[a] == BodyMesh.HEA && V[a].y > BodyMesh.Rest[BodyMesh.HEA].m13 + 0.06f) continue;
                keep.Add(a); keep.Add(sk.tris[t + 1]); keep.Add(sk.tris[t + 2]);
            }
            sk.tris = keep; sk.mesh.SetTriangles(keep, 0);
            foreach (Transform c in p.transform) if (c.name != "stump") c.gameObject.SetActive(false); // visor, brain...
            foreach (var col in p.GetComponents<Collider>()) col.enabled = false;
            Vector3 hc = p.transform.TransformPoint(new Vector3(0, 0.17f, 0));
            for (int i = 0; i < 14; i++) Gib.Spawn(hc + Random.insideUnitSphere * 0.06f, lastDir * Random.Range(1f, 4f) + Random.insideUnitSphere * 2.5f + Vector3.up * 1.5f, Random.Range(0.02f, 0.045f));
            Blood.I.Spray(hc, lastDir + Vector3.up * 0.5f, 160, 5f, 0.9f, 0.2f, 1.4f);
            Stump(p.transform, p.transform.TransformPoint(new Vector3(0, 0.06f, 0)), p.transform.up, 0.05f);
            AddWound(p, new Vector3(0, 0.07f, 0), Vector3.up, 25f, true, 6f, "HEAD DESTROYED");
            injuries.Add("HEAD  DESTROYED");
            Die("HEAD DESTROYED");
        }

        // nearest body part to a world point (any Carl, alive, down, dead, or a cut piece)
        public static Part Nearest(Vector3 w, float maxDist)
        {
            Part best = null; float bd = maxDist;
            foreach (var m in All)
            {
                if (m == null) continue;
                foreach (var p in m.allParts)
                {
                    if (p == null || (p.transform.position - w).sqrMagnitude > 1f) continue;
                    float s = p.Sdf(p.transform.InverseTransformPoint(w));
                    if (s < bd) { bd = s; best = p; }
                }
            }
            return best;
        }

        // exact hit test against the skin triangles where they are drawn right now (skinned on the CPU with the same bones and
        // bind poses the GPU uses). Two-sided: through a carved hole you hit the flesh behind it.
        // nearest triangle along a ray (Moller-Trumbore, both faces); returns its index in T or -1, hitT = distance
        static int RayTris(Vector3[] W, List<int> T, Vector3 o, Vector3 d, ref float hitT)
        {
            int hitTri = -1;
            for (int t = 0; t < T.Count; t += 3)
            {
                Vector3 a = W[T[t]], e1 = W[T[t + 1]] - a, e2 = W[T[t + 2]] - a;
                Vector3 pv = Vector3.Cross(d, e2); float det = Vector3.Dot(e1, pv);
                if (det > -1e-10f && det < 1e-10f) continue;
                float inv = 1f / det; Vector3 tv = o - a;
                float u = Vector3.Dot(tv, pv) * inv; if (u < -1e-4f || u > 1.0001f) continue;
                Vector3 qv = Vector3.Cross(tv, e1);
                float v = Vector3.Dot(d, qv) * inv; if (v < -1e-4f || u + v > 1.0001f) continue;
                float tt = Vector3.Dot(e2, qv) * inv;
                if (tt > 0 && tt < hitT) { hitT = tt; hitTri = t; }
            }
            return hitTri;
        }

        public static bool PickSkin(Vector3 o, Vector3 d, float maxT, out Part best, out float bestT, out Vector3 nrm, Mannequin skip = null)
        {
            best = null; bestT = maxT; nrm = -d;
            foreach (var sk in Skin.All)
            {
                if (sk == null || sk.r == null || !sk.r.gameObject.activeInHierarchy || sk.tris.Count == 0) continue;
                if (skip != null && sk.owner == skip) continue;
                bool near = false;
                foreach (var bn in sk.bones)
                {
                    if (bn == null) continue;
                    Vector3 c = bn.position; float tc = Vector3.Dot(c - o, d);
                    if (tc < -1f || tc > bestT + 1f) continue;
                    if ((o + d * Mathf.Max(0f, tc) - c).sqrMagnitude < 1f) { near = true; break; }
                }
                if (!near) continue;
                var W = sk.World();
                var T = sk.tris;
                float hitT = bestT;
                int hitTri = RayTris(W, T, o, d, ref hitT);
                if (hitTri < 0) continue;
                Vector3 wp = o + d * hitT;
                // the part = dominant bone of the triangle corner nearest to the hit
                int vb = T[hitTri];
                for (int k = 1; k < 3; k++) if ((W[T[hitTri + k]] - wp).sqrMagnitude < (W[vb] - wp).sqrMagnitude) vb = T[hitTri + k];
                Part part = null;
                var w = sk.bw[vb];
                if (w.boneIndex0 < sk.bones.Length && sk.bones[w.boneIndex0] != null) part = sk.bones[w.boneIndex0].GetComponentInParent<Part>();
                if (part == null) part = Nearest(wp, 0.3f);
                if (part == null || part.owner == skip) continue;
                Vector3 a2 = W[T[hitTri]];
                Vector3 n = Vector3.Cross(W[T[hitTri + 1]] - a2, W[T[hitTri + 2]] - a2).normalized;
                if (Vector3.Dot(n, d) > 0) n = -n;
                bestT = hitT; best = part; nrm = n;
            }
            return best != null;
        }

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
                    if (Trace(p, o + d * t0, d, 1.6f, out Vector3 lp) && !(p.isHead && m.headMashed && lp.y > 0.07f))   // a burst skull is gone
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
                // after death the heart stops: wounds only drain by gravity, slower and slower, then stop
                float post = dead ? 0.45f * Mathf.Exp(-(Time.time - deadTime) / 12f) : 1f;
                if (dead && post < 0.02f) continue;
                float rate = w.rate * k * (spurt ? pulse * 1.7f * pressure : post);
                var t = w.part.transform;
                Vector3 n = t.TransformDirection(w.ln);
                w.runT -= dt;
                if (w.runT <= 0)
                {
                    w.runT = Random.Range(0.2f, 0.5f);
                    if (runners.Count < 50) runners.Add(new Runner { part = w.part, lp = w.lp, left = Mathf.Clamp(rate * (spurt ? 0.04f : 0.15f), 0.1f, 1.8f) });
                }
                w.acc += rate * dt * 5f;   // what you see is exaggerated like the reference game
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
                    if (!dead && mine) blood -= dv / 3f;
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
            go.transform.localScale = p.transform.lossyScale;
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

        const float WaistSplit = 55f;

        // the upper body comes away from the pelvis
        void SplitInHalf(Vector3 dir)
        {
            var ch = parts["chest"]; if (ch.severed) return;
            injuries.Add("CUT IN HALF");
            SeverJoint(ch, dir);
            Vector3 w = ch.transform.position;
            for (int i = 0; i < 16; i++) Gib.Spawn(w + Random.insideUnitSphere * 0.08f, dir * Random.Range(1f, 3f) + Random.insideUnitSphere * 2f + Vector3.up * 1.2f, Random.Range(0.015f, 0.035f));
            Blood.I.Spray(w, dir + Vector3.up * 0.3f, 300, 4.5f, 0.9f, 0.3f, 2.2f);
            Blood.I.Spray(w, Vector3.down, 200, 1.5f, 0.8f, 0.8f, 3f);
            Die("CUT IN HALF");
        }

        public void SeverJoint(Part p, Vector3 dir)
        {
            if (p.severed || p.parentPart == null) return;
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
            if (piece.isTorso) { nUp = upper.transform.up; r = 0.12f; }   // waist: pelvis below, chest above
            Stump(upper.transform, cw, nUp, r);
            Stump(piece.transform, cw, -nUp, r * 0.95f);
            // a severed limb opens its big artery
            float rate = piece.isTorso ? 60f : piece.isHead ? 45f : piece.isLeg ? 30f : 15f;   // torso: the aorta
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
            // torn meat: irregular lumps inside the opening, and the bone sticking out
            for (int i = 0; i < 6; i++)
            {
                var g = Mats.Vis(PrimitiveType.Sphere, root, new Vector3(Random.Range(-0.6f, 0.6f) * r, Random.Range(-0.5f, 0.1f) * r, Random.Range(-0.6f, 0.6f) * r),
                    new Vector3(Random.Range(0.4f, 0.8f), Random.Range(0.25f, 0.6f), Random.Range(0.4f, 0.8f)) * r, stumpMat);
                g.transform.localRotation = Random.rotation;
            }
            Mats.Vis(PrimitiveType.Cylinder, root, new Vector3(Random.Range(-0.1f, 0.1f) * r, r * 0.35f, 0), new Vector3(r * 0.4f, r * 0.45f, r * 0.4f), boneMat).transform.localRotation = Quaternion.Euler(Random.Range(-15f, 15f), 0, Random.Range(-15f, 15f));
        }
    }
}
