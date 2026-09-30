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
        public bool dead, ragdoll, conscious = true;
        public float blood = 5000f;
        public const float BloodMax = 5000f;
        public string status = "WANDERING", cause = "", lastHit = "-";
        public float lastHitTime = -99;
        public readonly List<string> injuries = new List<string>();
        public readonly Dictionary<string, Part> parts = new Dictionary<string, Part>();
        readonly List<Wound> wounds = new List<Wound>();

        enum S { Idle, Walk, Flee }
        S state = S.Idle;
        float stateT, phase, speed, heart, writheT, hurt;
        int torsoHits;
        Vector3 target, lastPos, vel;
        Part clutch;

        static Material skinMat, visorMat, tagMat, stumpMat, boneMat;

        struct D
        {
            public string k, p; public Vector3 pos; public float len, rad, mass; public int sh;
            public D(string k, string p, Vector3 pos, float len, float rad, float mass, int sh) { this.k = k; this.p = p; this.pos = pos; this.len = len; this.rad = rad; this.mass = mass; this.sh = sh; }
        }
        // sh: 0 limb pointing down, 1 chest, 2 head, 3 pelvis. Facing +Z, left side is -X.
        static readonly D[] DEFS =
        {
            new D("pelvis", null,     new Vector3(0, 0.95f, 0),        0.2f,  0.14f, 12f,  3),
            new D("chest",  "pelvis", new Vector3(0, 0.10f, 0),        0.46f, 0.15f, 16f,  1),
            new D("head",   "chest",  new Vector3(0, 0.46f, 0),        0.3f,  0.12f, 5f,   2),
            new D("uarmL",  "chest",  new Vector3(-0.22f, 0.39f, 0),   0.3f,  0.055f, 2.5f, 0),
            new D("farmL",  "uarmL",  new Vector3(0, -0.3f, 0),        0.33f, 0.05f, 2f,   0),
            new D("uarmR",  "chest",  new Vector3(0.22f, 0.39f, 0),    0.3f,  0.055f, 2.5f, 0),
            new D("farmR",  "uarmR",  new Vector3(0, -0.3f, 0),        0.33f, 0.05f, 2f,   0),
            new D("thighL", "pelvis", new Vector3(-0.1f, -0.05f, 0),   0.45f, 0.078f, 8f,  0),
            new D("shinL",  "thighL", new Vector3(0, -0.45f, 0),       0.45f, 0.062f, 4.5f, 0),
            new D("thighR", "pelvis", new Vector3(0.1f, -0.05f, 0),    0.45f, 0.078f, 8f,  0),
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
                visorMat = Mats.Lit(new Color(0.07f, 0.07f, 0.08f), 0.6f);
                tagMat = Mats.Lit(new Color(0.95f, 0.38f, 0.12f), 0.3f);
                stumpMat = Mats.Lit(new Color(0.42f, 0.02f, 0.04f), 0.75f);
                boneMat = Mats.Lit(new Color(0.93f, 0.9f, 0.82f), 0.4f);
            }
            Build();
            lastPos = transform.position;
            stateT = Random.Range(0.5f, 3f);
            phase = Random.value;
        }

        void OnDestroy() { All.Remove(this); }

        void Build()
        {
            foreach (var d in DEFS)
            {
                var go = new GameObject(d.k);
                var t = go.transform;
                t.SetParent(d.p == null ? transform : parts[d.p].transform, false);
                t.localPosition = d.pos;
                var part = go.AddComponent<Part>();
                part.owner = this; part.key = d.k; part.radius = d.rad; part.length = d.len;
                part.isHead = d.k == "head"; part.isTorso = d.k == "chest" || d.k == "pelvis";
                part.isLeg = d.k.StartsWith("thigh") || d.k.StartsWith("shin"); part.isArm = d.k.Contains("arm");
                if (d.p != null) part.parentPart = parts[d.p];

                var rb = go.AddComponent<Rigidbody>();
                rb.mass = d.mass; rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.solverIterations = 12; rb.solverVelocityIterations = 4;
                rb.linearDamping = 0.05f; rb.angularDamping = 0.8f;
                part.rb = rb;

                switch (d.sh)
                {
                    case 0:
                    {
                        var c = go.AddComponent<CapsuleCollider>(); c.direction = 1; c.center = new Vector3(0, -d.len / 2, 0); c.height = d.len; c.radius = d.rad;
                        Mats.Vis(PrimitiveType.Capsule, t, new Vector3(0, -d.len / 2, 0), new Vector3(d.rad * 2, d.len / 2, d.rad * 2), skinMat);
                        if (d.k.StartsWith("shin"))
                        {
                            var f = go.AddComponent<BoxCollider>(); f.center = new Vector3(0, -d.len + 0.04f, 0.05f); f.size = new Vector3(0.1f, 0.08f, 0.24f);
                            Mats.Vis(PrimitiveType.Sphere, t, new Vector3(0, -d.len + 0.04f, 0.05f), new Vector3(0.11f, 0.08f, 0.26f), skinMat);
                        }
                        break;
                    }
                    case 1:
                    {
                        var c = go.AddComponent<CapsuleCollider>(); c.direction = 1; c.center = new Vector3(0, 0.22f, 0); c.height = 0.46f; c.radius = 0.15f;
                        Mats.Vis(PrimitiveType.Capsule, t, new Vector3(0, 0.23f, 0), new Vector3(0.38f, 0.24f, 0.24f), skinMat);
                        Mats.Vis(PrimitiveType.Capsule, t, new Vector3(0, 0.38f, 0), new Vector3(0.16f, 0.24f, 0.16f), skinMat).transform.localRotation = Quaternion.Euler(0, 0, 90);
                        Mats.Vis(PrimitiveType.Cube, t, new Vector3(-0.08f, 0.3f, 0.12f), new Vector3(0.08f, 0.028f, 0.012f), tagMat, false);
                        break;
                    }
                    case 2:
                    {
                        var c = go.AddComponent<SphereCollider>(); c.center = new Vector3(0, 0.15f, 0); c.radius = 0.12f;
                        Mats.Vis(PrimitiveType.Capsule, t, new Vector3(0, 0.03f, 0), new Vector3(0.1f, 0.06f, 0.1f), skinMat);
                        Mats.Vis(PrimitiveType.Sphere, t, new Vector3(0, 0.16f, 0.005f), new Vector3(0.21f, 0.27f, 0.23f), skinMat);
                        Mats.Vis(PrimitiveType.Cube, t, new Vector3(0, 0.185f, 0.095f), new Vector3(0.17f, 0.032f, 0.05f), visorMat, false);
                        break;
                    }
                    default:
                    {
                        var c = go.AddComponent<BoxCollider>(); c.center = Vector3.zero; c.size = new Vector3(0.32f, 0.22f, 0.22f);
                        Mats.Vis(PrimitiveType.Sphere, t, Vector3.zero, new Vector3(0.34f, 0.26f, 0.24f), skinMat);
                        break;
                    }
                }
                parts[d.k] = part;
            }
            // joint limits: positive twist around X swings a downward limb backwards
            Joint("chest", -25, 25, 15, 15);
            Joint("head", -40, 40, 30, 30);
            Joint("uarmL", -140, 50, 70, 40); Joint("uarmR", -140, 50, 70, 40);
            Joint("farmL", -140, 3, 5, 5); Joint("farmR", -140, 3, 5, 5);
            Joint("thighL", -100, 30, 35, 20); Joint("thighR", -100, 30, 35, 20);
            Joint("shinL", 0, 130, 4, 4); Joint("shinR", 0, 130, 4, 4);
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
            else if (!dead && conscious) Writhe(dt);
            Bleed(dt);
            heart += dt * (dead ? 0 : (1.3f + hurt * 0.8f));
            hurt = Mathf.Max(0, hurt - dt * 0.05f);
            if (!dead)
            {
                if (blood < BloodMax * 0.5f && conscious) { conscious = false; status = "FAINTED"; GoRagdoll(); }
                if (blood < BloodMax * 0.35f) Die("BLOOD LOSS");
            }
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
                    want = hurt > 0.3f ? 0.6f : 1.15f;
                    if (stateT <= 0 || Flat(target - pos).magnitude < 0.5f) { state = S.Idle; stateT = Random.Range(1.5f, 4f); }
                    break;
                case S.Flee:
                    status = "FLEEING";
                    want = hurt > 0.6f ? 1.4f : 3.2f;
                    if (Game.I != null && Game.I.player != null)
                    {
                        Vector3 away = Flat(pos - Game.I.player.transform.position);
                        if (Flat(target - pos).magnitude < 1f || Random.value < dt) target = Game.Clamp(pos + away.normalized * 8f + Random.insideUnitSphere * 3f);
                    }
                    if (stateT <= 0) { state = S.Walk; target = Game.RandomPoint(); stateT = 8f; }
                    break;
            }
            speed = Mathf.MoveTowards(speed, want, dt * 4f);
            Vector3 dir = Flat(target - pos);
            if (speed > 0.01f && dir.sqrMagnitude > 0.01f)
            {
                var want2 = Quaternion.LookRotation(dir.normalized);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want2, 200f * dt);
                // obstacle ahead: choose another destination
                if (Physics.SphereCast(pos + Vector3.up * 1.2f, 0.25f, transform.forward, out RaycastHit h, 0.9f, ~0, QueryTriggerInteraction.Ignore))
                {
                    var o = h.collider.GetComponentInParent<Mannequin>();
                    if (o != this) target = Game.RandomPoint();
                }
                pos += transform.forward * speed * dt;
                transform.position = Game.Clamp(pos);
                phase += dt * (0.6f + speed * 0.75f);
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
            Set("thighL", Quaternion.Euler(-s * a, 0, 0), k); Set("thighR", Quaternion.Euler(s * a, 0, 0), k);
            Set("shinL", Quaternion.Euler(kneeL, 0, 0), k); Set("shinR", Quaternion.Euler(kneeR, 0, 0), k);
            Set("uarmL", Quaternion.Euler(s * a * 0.8f, 0, -5), k); Set("uarmR", Quaternion.Euler(-s * a * 0.8f, 0, 5), k);
            Set("farmL", Quaternion.Euler(-15 - a * 0.3f, 0, 0), k); Set("farmR", Quaternion.Euler(-15 - a * 0.3f, 0, 0), k);
            float lean = hurt * 18f + (state == S.Flee ? 10f : 0f);
            Set("chest", Quaternion.Euler(lean, 0, 0), k);
            Set("head", Quaternion.Euler(-lean * 0.4f, 0, 0), k);
            if (clutch != null && !clutch.severed)
            {
                // hold the wound with the opposite (or free) hand
                bool left = clutch.transform.position.x < transform.position.x;
                string arm = clutch.key.EndsWith("R") ? "L" : clutch.key.EndsWith("L") ? "R" : (Random.value < 0.5f ? "R" : "L");
                float side = arm == "R" ? -1 : 1;
                Set("uarm" + arm, Quaternion.Euler(-55, 0, side * 28), k);
                Set("farm" + arm, Quaternion.Euler(-105, 0, 0), k);
                _ = left;
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
            pick.rb.AddForce((Random.insideUnitSphere + Vector3.up * 0.6f) * pick.rb.mass * 1.6f, ForceMode.Impulse);
        }

        public void GoRagdoll()
        {
            if (ragdoll) return;
            ragdoll = true;
            foreach (var p in parts.Values)
            {
                if (p.severed) continue;
                p.rb.isKinematic = false;
                p.rb.linearVelocity = vel;
            }
            // legs give way: knees forward, torso drops
            if (!parts["shinL"].severed) parts["shinL"].rb.AddForce(transform.forward * 1.2f, ForceMode.VelocityChange);
            if (!parts["shinR"].severed) parts["shinR"].rb.AddForce(transform.forward * 1.2f, ForceMode.VelocityChange);
        }

        public void Die(string why)
        {
            if (dead) return;
            dead = true; conscious = false; cause = why; status = "DEAD / " + why;
            GoRagdoll();
        }

        public void Flee()
        {
            if (dead || ragdoll) return;
            state = S.Flee; stateT = Random.Range(5f, 9f);
            if (Game.I != null && Game.I.player != null)
                target = Game.Clamp(transform.position + Flat(transform.position - Game.I.player.transform.position).normalized * 8f);
        }

        // ======================= damage =======================
        public void Hit(Part p, Collider col, Vector3 pt, Vector3 dir, Vector3 nrm)
        {
            p.hits++; lastHit = p.key.ToUpper(); lastHitTime = Time.time;
            hurt = Mathf.Min(1, hurt + 0.35f);
            var B = Blood.I;

            // exit wound: cast back from beyond the part to find where the bullet leaves
            Vector3 exitP = pt + dir * p.radius * 2f, exitN = dir;
            bool exits = col.Raycast(new Ray(pt + dir * 0.6f, -dir), out RaycastHit eh, 0.6f);
            if (exits) { exitP = eh.point; exitN = eh.normal; }

            B.Spray(pt + nrm * 0.01f, (-dir + nrm) * 0.5f, 7, 1.6f, 0.4f, 0.05f, 0.3f);
            if (exits) B.Spray(exitP + exitN * 0.01f, dir, p.isHead ? 45 : 22, p.isHead ? 5.5f : 4f, 0.45f, 0.1f, 0.7f);
            B.BodyDecal(p.transform, pt, nrm, 0.05f, 1, Vector3.down);
            if (exits) B.BodyDecal(p.transform, exitP, exitN, 0.08f, 1, Vector3.down);

            AddWound(p.transform, pt, nrm, p.isHead ? 6f : p.isTorso ? 5f : 3.5f, false, 0);
            if (exits) AddWound(p.transform, exitP, exitN, p.isHead ? 14f : p.isTorso ? 9f : 6f, !p.isHead && Random.value < 0.35f, 0);

            if (p.severed) { /* shooting a detached piece: just push it */ }
            else if (p.isHead)
            {
                injuries.Add("HEAD  GUNSHOT  FATAL");
                Die("HEADSHOT");
                if (exits) for (int i = 0; i < 2; i++) Gib.Spawn(exitP, dir * Random.Range(2f, 4f) + Random.insideUnitSphere + Vector3.up, 0.035f);
                if (p.hits >= 2) Sever(p, dir);
            }
            else if (p.isTorso)
            {
                torsoHits++;
                injuries.Add((p.key == "chest" ? "CHEST" : "ABDOMEN") + "  GUNSHOT" + (exits ? "  THROUGH" : ""));
                if (torsoHits >= 4) Die("MASSIVE TRAUMA");
                else if (!ragdoll)
                {
                    clutch = p;
                    if (Random.value < 0.18f * torsoHits) { GoRagdoll(); }
                }
            }
            else
            {
                injuries.Add(p.key.ToUpper() + "  GUNSHOT");
                // limbs come off after repeated hits, or sometimes when the bone is hit
                if (p.hits >= 2 || Random.value < 0.3f) Sever(p, dir);
                else clutch = p;
                if (p.isLeg && !ragdoll && !dead) GoRagdoll();
            }

            if (!ragdoll) Flee();
            if (!p.rb.isKinematic) p.rb.AddForceAtPosition(dir * Game.BulletImpulse, pt, ForceMode.Impulse);
            if (injuries.Count > 12) injuries.RemoveAt(0);
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
            for (int i = wounds.Count - 1; i >= 0; i--)
            {
                var w = wounds[i];
                if (w.t == null) { wounds.RemoveAt(i); continue; }
                w.age += dt;
                float k = w.life > 0 ? Mathf.Clamp01(1f - w.age / w.life) : Mathf.Exp(-w.age / 60f);
                if (k < 0.02f) { wounds.RemoveAt(i); continue; }
                bool spurt = w.arterial && !dead;
                float rate = w.rate * k * (dead ? 0.3f : 1f) * (spurt ? pulse * 1.6f : 1f);
                w.acc += rate * dt;
                if (w.acc < dv) continue;
                Vector3 p = w.t.TransformPoint(w.lp), n = w.t.TransformDirection(w.ln);
                Vector3 bv = w.rb != null && !w.rb.isKinematic ? w.rb.linearVelocity : vel;
                int guard = 0;
                while (w.acc >= dv && guard++ < 30)
                {
                    w.acc -= dv;
                    Vector3 v = spurt ? n * (1.2f + 2.8f * pulse) + Random.insideUnitSphere * 0.25f
                                      : n * 0.12f + Random.insideUnitSphere * 0.06f;
                    Blood.I.Emit(p + n * 0.012f, v + bv, dv);
                    if (!dead && w.t.GetComponentInParent<Mannequin>() == this) blood -= dv;
                }
            }
        }

        public void Sever(Part p, Vector3 dir)
        {
            if (p.severed || p.isTorso || p.parentPart == null) return;
            var par = p.parentPart;
            Vector3 pivot = p.transform.position;
            Vector3 n = (pivot - par.transform.TransformPoint(Vector3.zero)).normalized;
            if (p.isHead) n = par.transform.up;
            Vector3 baseVel = !par.rb.isKinematic ? par.rb.linearVelocity : vel;

            var pieceCols = p.GetComponentsInChildren<Collider>();
            foreach (var pc in par.GetComponents<Collider>()) foreach (var c in pieceCols) Physics.IgnoreCollision(pc, c, true);
            foreach (var c in p.GetComponentsInChildren<Part>()) { c.severed = true; c.rb.isKinematic = false; c.rb.linearVelocity = baseVel; }
            if (p.joint != null) DestroyImmediate(p.joint);
            p.transform.SetParent(null, true);
            p.rb.linearVelocity = baseVel + dir * 2.2f + Vector3.up * 1.2f;
            p.rb.AddTorque(Random.insideUnitSphere * p.rb.mass * 0.6f, ForceMode.Impulse);

            float r = p.radius * (p.isHead ? 0.6f : 1.05f);
            Stump(par.transform, pivot, n, r);
            Stump(p.transform, pivot, -n, r * 0.95f);
            AddWound(par.transform, pivot + n * 0.01f, n, p.isHead ? 110f : p.isLeg ? 70f : 45f, true, p.isHead ? 12f : 18f);
            AddWound(p.transform, pivot - n * 0.01f, -n, 14f, false, 6f);
            for (int i = 0; i < 4; i++) Gib.Spawn(pivot, dir * Random.Range(1f, 3.2f) + Random.insideUnitSphere * 1.4f + Vector3.up * 1.2f, Random.Range(0.025f, 0.045f));
            Blood.I.Spray(pivot, (dir + n) * 0.5f, 40, 4f, 0.6f, 0.2f, 0.9f);
            injuries.Add(p.key.ToUpper() + "  SEVERED");
            if (p.isHead) Die("DECAPITATED");
            else if (p.isLeg && !dead) GoRagdoll();
        }

        static void Stump(Transform t, Vector3 world, Vector3 n, float r)
        {
            var root = new GameObject("stump").transform;
            root.SetParent(t, false);
            root.position = world;
            root.rotation = Quaternion.FromToRotation(Vector3.up, n);
            Mats.Vis(PrimitiveType.Sphere, root, Vector3.zero, new Vector3(r * 2.05f, r * 0.9f, r * 2.05f), stumpMat);
            for (int i = 0; i < 3; i++)
                Mats.Vis(PrimitiveType.Sphere, root, new Vector3(Random.Range(-r, r) * 0.5f, r * 0.2f, Random.Range(-r, r) * 0.5f), Vector3.one * r * Random.Range(0.5f, 0.9f), stumpMat);
            Mats.Vis(PrimitiveType.Cylinder, root, new Vector3(0, r * 0.35f, 0), new Vector3(r * 0.55f, r * 0.45f, r * 0.55f), boneMat);
        }
    }
}
