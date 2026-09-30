using UnityEngine;

namespace VITS
{
    // First person controller + semi-auto pistol (15 rounds, R reload).
    public class Player : MonoBehaviour
    {
        public Camera cam;
        public Mannequin looked;
        // 0 pistol (9 mm), 1 AK-47 (7.62x39), 2 AWP (.338 Lapua Magnum, bolt action, scoped) - chosen in the Esc menu
        public static int Weapon;
        public static bool AK => Weapon == 1;
        public static bool AWP => Weapon == 2;
        public static readonly string[] Names = { "PISTOL / 9MM", "AK-47 / 7.62", "AWP / .338 LAPUA" };
        static readonly int[] MAG = { 15, 30, 10 };
        public int ammo = 15; public int MaxAmmo => MAG[Weapon];
        public bool Scoped => AWP && GI.AimHeld() && reloadT <= 0 && boltT <= 0.9f && locked && !menu;
        float zoom = 8f, boltT;              // AWP magnification (4x / 8x / 12x, mouse wheel) and bolt cycling
        public bool menu;
        Transform pistolModel, akModel, awpModel, bolt;
        readonly int[] mags = { 15, 30, 10 };
        public float reloadT;
        public bool locked;
        public string aimInfo = ""; public float hitMarkT;
        public Rigidbody held; Vector3 heldLocal; float heldDist; LineRenderer beam;

        CharacterController cc;
        float yaw, pitch, vy, cool, recoil, flashT, tracerT, bob;
        Transform gun, slide;
        Light flash;
        LineRenderer tracer;
        AudioSource au; AudioClip shotClip, awpClip, clickClip;

        void Start()
        {
            Weapon = 0;
            cc = gameObject.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0, 0.9f, 0); cc.stepOffset = 0.45f; cc.slopeLimit = 50f;

            var cgo = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = cgo.AddComponent<Camera>();
            cgo.AddComponent<AudioListener>();
            cgo.transform.SetParent(transform, false);
            cgo.transform.localPosition = new Vector3(0, 1.65f, 0);
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 400f; cam.fieldOfView = 70f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Level.Sky;
            yaw = transform.eulerAngles.y;

            BuildGun();
            au = cgo.AddComponent<AudioSource>(); au.spatialBlend = 0;
            shotClip = MakeShot(70f, 0.35f); awpClip = MakeShot(45f, 0.9f); clickClip = MakeClick();
        }

        void BuildGun()
        {
            gun = new GameObject("Pistol").transform;
            gun.SetParent(cam.transform, false);
            gun.localPosition = new Vector3(0.17f, -0.16f, 0.36f);
            var steel = Mats.Lit(new Color(0.34f, 0.37f, 0.42f), 0.5f);
            var dark = Mats.Lit(new Color(0.16f, 0.17f, 0.2f), 0.3f);
            var grip = Mats.Lit(new Color(0.72f, 0.42f, 0.2f), 0.2f);
            var hand = Mats.Lit(Mannequin.SkinColor, 0.3f);
            slide = Mats.Vis(PrimitiveType.Cube, gun, new Vector3(0, 0.03f, 0.03f), new Vector3(0.034f, 0.036f, 0.2f), steel, false).transform;
            Mats.Vis(PrimitiveType.Cube, gun, new Vector3(0, 0.002f, 0.02f), new Vector3(0.03f, 0.026f, 0.17f), dark, false);
            Mats.Vis(PrimitiveType.Cube, gun, new Vector3(0, -0.055f, -0.045f), new Vector3(0.032f, 0.1f, 0.045f), grip, false).transform.localRotation = Quaternion.Euler(-15, 0, 0);
            Mats.Vis(PrimitiveType.Cube, gun, new Vector3(0, 0.052f, 0.12f), new Vector3(0.008f, 0.01f, 0.008f), dark, false);
            Mats.Vis(PrimitiveType.Capsule, gun, new Vector3(0.01f, -0.09f, -0.08f), new Vector3(0.06f, 0.07f, 0.06f), hand, false).transform.localRotation = Quaternion.Euler(-30, 0, 0);
            // everything built so far is the pistol; the AK is a second model
            pistolModel = new GameObject("PistolModel").transform; pistolModel.SetParent(gun, false);
            var kids = new System.Collections.Generic.List<Transform>(); foreach (Transform c in gun) if (c != pistolModel) kids.Add(c);
            foreach (var c in kids) c.SetParent(pistolModel, true);
            akModel = new GameObject("AK47Model").transform; akModel.SetParent(gun, false);
            var wood = Mats.Lit(new Color(0.88f, 0.55f, 0.28f), 0.2f); var blue = Mats.Lit(new Color(0.42f, 0.48f, 0.56f), 0.4f); var dk = Mats.Lit(new Color(0.2f, 0.22f, 0.26f), 0.3f);
            Mats.Vis(PrimitiveType.Cube, akModel, new Vector3(0, 0.02f, 0.02f), new Vector3(0.045f, 0.06f, 0.3f), blue, false);
            Mats.Vis(PrimitiveType.Cube, akModel, new Vector3(0, 0.05f, 0.0f), new Vector3(0.04f, 0.02f, 0.26f), blue, false);
            Mats.Vis(PrimitiveType.Cube, akModel, new Vector3(0, 0.03f, 0.27f), new Vector3(0.05f, 0.05f, 0.2f), wood, false);
            Mats.Vis(PrimitiveType.Cube, akModel, new Vector3(0, 0.04f, 0.48f), new Vector3(0.018f, 0.018f, 0.3f), dk, false);
            Mats.Vis(PrimitiveType.Cube, akModel, new Vector3(0, 0.075f, 0.56f), new Vector3(0.01f, 0.03f, 0.01f), dk, false);
            Mats.Vis(PrimitiveType.Cube, akModel, new Vector3(0, -0.06f, 0.1f), new Vector3(0.03f, 0.13f, 0.05f), dk, false).transform.localRotation = Quaternion.Euler(20, 0, 0);
            Mats.Vis(PrimitiveType.Cube, akModel, new Vector3(0, -0.05f, -0.06f), new Vector3(0.03f, 0.09f, 0.04f), wood, false).transform.localRotation = Quaternion.Euler(-20, 0, 0);
            Mats.Vis(PrimitiveType.Cube, akModel, new Vector3(0, 0.0f, -0.25f), new Vector3(0.04f, 0.07f, 0.26f), wood, false);
            Mats.Vis(PrimitiveType.Capsule, akModel, new Vector3(0.01f, -0.09f, -0.08f), new Vector3(0.06f, 0.07f, 0.06f), hand, false).transform.localRotation = Quaternion.Euler(-30, 0, 0);
            akModel.localPosition = new Vector3(0.02f, 0f, -0.1f);
            akModel.gameObject.SetActive(false);
            BuildAWP(hand);
            var fgo = new GameObject("Flash"); fgo.transform.SetParent(gun, false); fgo.transform.localPosition = new Vector3(0, 0.03f, 0.17f);
            flash = fgo.AddComponent<Light>(); flash.type = LightType.Point; flash.range = 8f; flash.intensity = 0; flash.color = new Color(1f, 0.85f, 0.6f);

            var tgo = new GameObject("Tracer");
            tracer = tgo.AddComponent<LineRenderer>();
            tracer.positionCount = 2; tracer.startWidth = 0.0025f; tracer.endWidth = 0.0015f;
            tracer.sharedMaterial = Mats.Decal(Texture2D.whiteTexture, new Color(1f, 0.95f, 0.8f, 0.8f));
            tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; tracer.enabled = false;
            beam = new GameObject("GrabBeam").AddComponent<LineRenderer>();
            beam.positionCount = 2; beam.startWidth = 0.008f; beam.endWidth = 0.008f;
            beam.sharedMaterial = Mats.Decal(Texture2D.whiteTexture, new Color(1f, 0.45f, 0.15f, 0.9f));
            beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; beam.enabled = false;
        }

        // Accuracy International AWP (Arctic Warfare Magnum): olive-green chassis stock with thumbhole grip and cheek piece,
        // long heavy black barrel with a muzzle brake, big scope on rings, bolt handle on the right, 10-round box magazine
        void BuildAWP(Material hand)
        {
            awpModel = new GameObject("AWPModel").transform; awpModel.SetParent(gun, false);
            var green = Mats.Lit(new Color(0.33f, 0.38f, 0.26f), 0.25f);
            var blk = Mats.Lit(new Color(0.07f, 0.075f, 0.08f), 0.45f);
            var steel = Mats.Lit(new Color(0.22f, 0.23f, 0.25f), 0.6f);
            var glass = Mats.Lit(new Color(0.1f, 0.25f, 0.35f), 0.95f);
            Transform T = awpModel;
            System.Func<PrimitiveType, Vector3, Vector3, Material, Vector3, Transform> P = (pt, p, sc, m, rot) =>
            { var t = Mats.Vis(pt, T, p, sc, m, false).transform; t.localRotation = Quaternion.Euler(rot); return t; };
            Vector3 Z = Vector3.zero, CYL = new Vector3(90, 0, 0);
            // chassis: fore-end, action block, thumbhole grip, butt with cheek piece and rubber pad
            P(PrimitiveType.Cube, new Vector3(0, 0.0f, 0.2f), new Vector3(0.05f, 0.055f, 0.34f), green, Z);
            P(PrimitiveType.Cube, new Vector3(0, 0.018f, -0.02f), new Vector3(0.046f, 0.05f, 0.16f), steel, Z);            // receiver
            P(PrimitiveType.Cube, new Vector3(0, -0.035f, -0.03f), new Vector3(0.04f, 0.05f, 0.1f), green, Z);
            P(PrimitiveType.Cube, new Vector3(0, -0.075f, -0.1f), new Vector3(0.034f, 0.11f, 0.045f), green, new Vector3(-18, 0, 0)); // pistol grip
            P(PrimitiveType.Cube, new Vector3(0, -0.012f, -0.2f), new Vector3(0.042f, 0.035f, 0.1f), green, new Vector3(8, 0, 0));   // thumbhole bridge
            P(PrimitiveType.Cube, new Vector3(0, -0.11f, -0.2f), new Vector3(0.04f, 0.03f, 0.12f), green, new Vector3(-10, 0, 0));    // lower loop
            P(PrimitiveType.Cube, new Vector3(0, -0.04f, -0.34f), new Vector3(0.045f, 0.14f, 0.2f), green, Z);                       // butt
            P(PrimitiveType.Cube, new Vector3(0, 0.035f, -0.34f), new Vector3(0.04f, 0.025f, 0.16f), green, Z);                      // cheek piece
            P(PrimitiveType.Cube, new Vector3(0, -0.04f, -0.448f), new Vector3(0.047f, 0.15f, 0.018f), blk, Z);                     // butt pad
            P(PrimitiveType.Cube, new Vector3(0, -0.06f, 0.035f), new Vector3(0.036f, 0.065f, 0.07f), blk, new Vector3(-6, 0, 0));   // magazine
            P(PrimitiveType.Cube, new Vector3(0, -0.045f, -0.06f), new Vector3(0.008f, 0.03f, 0.05f), blk, Z);                      // trigger guard
            // barrel with fluting look, muzzle brake
            P(PrimitiveType.Cylinder, new Vector3(0, 0.022f, 0.55f), new Vector3(0.024f, 0.36f, 0.024f), blk, CYL);
            P(PrimitiveType.Cylinder, new Vector3(0, 0.022f, 0.93f), new Vector3(0.032f, 0.045f, 0.032f), blk, CYL);
            P(PrimitiveType.Cube, new Vector3(0, 0.022f, 0.93f), new Vector3(0.036f, 0.012f, 0.03f), steel, Z);                      // brake ports
            // bipod folded under the fore-end
            P(PrimitiveType.Cylinder, new Vector3(0.012f, -0.035f, 0.26f), new Vector3(0.008f, 0.1f, 0.008f), blk, CYL);
            P(PrimitiveType.Cylinder, new Vector3(-0.012f, -0.035f, 0.26f), new Vector3(0.008f, 0.1f, 0.008f), blk, CYL);
            // scope: tube, big objective bell, eyepiece, turrets, rings
            P(PrimitiveType.Cylinder, new Vector3(0, 0.085f, 0.0f), new Vector3(0.034f, 0.15f, 0.034f), blk, CYL);
            P(PrimitiveType.Cylinder, new Vector3(0, 0.09f, 0.19f), new Vector3(0.056f, 0.05f, 0.056f), blk, CYL);
            P(PrimitiveType.Cylinder, new Vector3(0, 0.09f, 0.242f), new Vector3(0.05f, 0.002f, 0.05f), glass, CYL);
            P(PrimitiveType.Cylinder, new Vector3(0, 0.087f, -0.17f), new Vector3(0.044f, 0.04f, 0.044f), blk, CYL);
            P(PrimitiveType.Cylinder, new Vector3(0, 0.113f, 0.0f), new Vector3(0.022f, 0.012f, 0.022f), steel, Z);                 // elevation turret
            P(PrimitiveType.Cylinder, new Vector3(0.028f, 0.085f, 0.0f), new Vector3(0.022f, 0.012f, 0.022f), steel, new Vector3(0, 0, 90)); // windage
            P(PrimitiveType.Cube, new Vector3(0, 0.055f, 0.07f), new Vector3(0.03f, 0.03f, 0.018f), blk, Z);                        // rings
            P(PrimitiveType.Cube, new Vector3(0, 0.055f, -0.08f), new Vector3(0.03f, 0.03f, 0.018f), blk, Z);
            // bolt: handle sticking out to the right with a round knob
            bolt = new GameObject("Bolt").transform; bolt.SetParent(awpModel, false); bolt.localPosition = new Vector3(0.024f, 0.02f, -0.07f);
            var bh = Mats.Vis(PrimitiveType.Cylinder, bolt, new Vector3(0.025f, -0.005f, 0), new Vector3(0.008f, 0.025f, 0.008f), steel, false).transform; bh.localRotation = Quaternion.Euler(0, 0, 70);
            Mats.Vis(PrimitiveType.Sphere, bolt, new Vector3(0.05f, -0.014f, 0), Vector3.one * 0.02f, blk, false);
            Mats.Vis(PrimitiveType.Capsule, awpModel, new Vector3(0.01f, -0.11f, -0.12f), new Vector3(0.06f, 0.07f, 0.06f), hand, false).transform.localRotation = Quaternion.Euler(-30, 0, 0);
            awpModel.localPosition = new Vector3(0.02f, 0.0f, -0.12f);
            awpModel.gameObject.SetActive(false);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (GI.Down(K.Esc)) SetMenu(!menu);
            if (menu) return;
            if (!locked && GI.FireDown()) { locked = true; Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; return; }

            if (locked)
            {
                var md = GI.MouseDelta();
                yaw += md.x * 2f; pitch = Mathf.Clamp(pitch - md.y * 2f, -88f, 88f);
            }
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            cam.transform.localRotation = Quaternion.Euler(pitch - recoil * 2.5f, 0, 0);

            // movement (unscaled: you move normally even in slow motion)
            Vector3 mv = Vector3.zero;
            if (GI.Held(K.W)) mv += transform.forward; if (GI.Held(K.S)) mv -= transform.forward;
            if (GI.Held(K.D)) mv += transform.right; if (GI.Held(K.A)) mv -= transform.right;
            if (mv.sqrMagnitude > 1) mv.Normalize();
            float sp = GI.Held(K.Shift) ? 6f : 3.4f;
            if (cc.isGrounded) { vy = -1f; if (GI.Down(K.Space)) vy = 5.2f; } else vy -= 14f * dt;
            cc.Move((mv * sp + Vector3.up * vy) * dt);
            bob += mv.magnitude * dt * (GI.Held(K.Shift) ? 11f : 7f);

            // weapon
            cool -= dt; recoil = Mathf.MoveTowards(recoil, 0, dt * 8f);
            if (reloadT > 0) { reloadT -= dt; if (reloadT <= 0) ammo = MaxAmmo; }
            boltT -= dt;
            if (locked && (AK ? GI.FireHeld() : GI.FireDown())) Shoot();
            if (AWP && bolt != null)
            {
                // bolt cycle after each shot: up, back, forward, down
                float k = boltT > 0 ? Mathf.Clamp01(1f - boltT / 1.2f) : 1f;
                float lift = Mathf.Clamp01(Mathf.Min(k, 1f - k) * 6f), back = Mathf.Clamp01(Mathf.Sin(Mathf.Clamp01((k - 0.15f) / 0.7f) * Mathf.PI));
                bolt.localRotation = Quaternion.Euler(0, 0, lift * 70f); bolt.localPosition = new Vector3(0.024f, 0.02f, -0.07f - back * 0.09f);
                if (Scoped) zoom = Mathf.Clamp(zoom + GI.Scroll() * 4f, 4f, 12f);
            }
            if (GI.Down(K.R) && ammo < MaxAmmo && reloadT <= 0) reloadT = 1.4f;
            float wantFov = Scoped ? 2f * Mathf.Atan(Mathf.Tan(35f * Mathf.Deg2Rad) / zoom) * Mathf.Rad2Deg : GI.AimHeld() ? (AWP ? 55f : 45f) : 70f;
            cam.fieldOfView = Scoped ? wantFov : Mathf.Lerp(cam.fieldOfView, wantFov, 1 - Mathf.Exp(-dt * 12f));
            gun.gameObject.SetActive(!Scoped);
            float aim = GI.AimHeld() ? 1f : 0f;
            float rl = reloadT > 0 ? Mathf.Sin(Mathf.Clamp01(1 - reloadT / 1.4f) * Mathf.PI) : 0;
            gun.localPosition = Vector3.Lerp(new Vector3(0.17f, -0.16f, 0.36f), new Vector3(0, -0.085f, 0.3f), aim)
                                + new Vector3(Mathf.Sin(bob) * 0.006f, Mathf.Abs(Mathf.Cos(bob)) * 0.006f - rl * 0.08f, -recoil * 0.04f);
            gun.localRotation = Quaternion.Euler(-recoil * 12f + rl * 30f, 0, rl * 25f);
            slide.localPosition = new Vector3(0, 0.03f, 0.03f - recoil * 0.03f);
            flashT -= dt; flash.intensity = flashT > 0 ? 6f : 0f;
            tracerT -= dt; tracer.enabled = tracerT > 0;

            // who am I looking at (for the medical monitor)
            looked = null;
            if (Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit lh, 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                var p = lh.collider.GetComponent<Part>();
                if (p != null) looked = p.owner;
            }
            aimInfo = "";
            if (Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit ah, 200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                aimInfo = ah.collider.name.ToUpper() + " · " + ah.distance.ToString("0.0") + " M";
            if (Mannequin.Pick(cam.transform.position, cam.transform.forward, 200f, out Part lp, out float ld))
            {
                if (ld < 40f) looked = lp.owner;
                aimInfo = lp.owner.displayName + " · " + lp.key.ToUpper() + " · " + ld.ToString("0.0") + " M";
            }
            hitMarkT -= dt;
            if (impact != null) { impactT -= dt; if (impactT <= 0) impact.gameObject.SetActive(false); }

            // middle mouse: grab a body (or a piece) and drag it around; wheel = nearer / farther
            if (locked && GI.GrabDown())
            {
                held = null;
                if (Mannequin.PickSkin(cam.transform.position, cam.transform.forward, GrabReach, out Part gp, out float gd, out _) ||
                    Mannequin.Pick(cam.transform.position, cam.transform.forward, GrabReach, out gp, out gd))
                {
                    gp.owner.Grabbed();
                    held = gp.rb; heldDist = gd; heldLocal = gp.transform.InverseTransformPoint(cam.transform.position + cam.transform.forward * gd);
                }
                else if (Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit gh, GrabReach, ~0, QueryTriggerInteraction.Ignore) && gh.rigidbody != null && !gh.rigidbody.isKinematic)
                { held = gh.rigidbody; heldDist = gh.distance; heldLocal = held.transform.InverseTransformPoint(gh.point); }
            }
            if (!GI.GrabHeld()) held = null;
            if (held != null) heldDist = Mathf.Clamp(heldDist + GI.Scroll() * Mathf.Max(0.25f, heldDist * 0.12f), 0.8f, GrabReach);
            beam.enabled = held != null;
            if (held != null)
            {
                beam.SetPosition(0, gun.TransformPoint(new Vector3(0, 0.03f, 0.14f)));
                beam.SetPosition(1, held.transform.TransformPoint(heldLocal));
            }
        }

        // spring that pulls the grabbed point toward a spot in front of the camera
        void FixedUpdate()
        {
            if (held == null || cam == null) return;
            Vector3 target = cam.transform.position + cam.transform.forward * heldDist;
            Vector3 p = held.transform.TransformPoint(heldLocal);
            Vector3 v = held.GetPointVelocity(p);
            // strong enough to lift and carry a whole Carl (alive or dead, brains on or off) wherever you point
            var part = held.GetComponent<Part>();
            float M = held.mass;
            if (part != null && part.owner != null) { M = part.owner.TotalMass; part.owner.heldT = Time.time; }
            Vector3 f = ((target - p) * 160f - v * 22f) * M + (part != null ? -Physics.gravity * M * 0.85f : Vector3.zero);
            held.AddForceAtPosition(Vector3.ClampMagnitude(f, M * 120f), p);
            if (part != null)
            {
                // the rest of the body follows instead of stretching the joints
                Vector3 hv = held.linearVelocity;
                foreach (var q in part.owner.Parts)
                    if (q != null && q.rb != null && q.rb != held && !q.rb.isKinematic)
                        q.rb.AddForce((hv - q.rb.linearVelocity) * 4f, ForceMode.Acceleration);
            }
        }

        public void SetMenu(bool on)
        {
            menu = on;
            locked = !on;
            Cursor.lockState = on ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = on;
            if (Game.I != null) Game.I.SetMenuPause(on);
        }

        public const float GrabReach = 60f;

        public void SetWeapon(int w)
        {
            mags[Weapon] = ammo;
            Weapon = w; ammo = mags[w]; reloadT = 0; boltT = 0;
            pistolModel.gameObject.SetActive(w == 0); akModel.gameObject.SetActive(w == 1); awpModel.gameObject.SetActive(w == 2);
            flash.transform.localPosition = w == 1 ? new Vector3(0, 0.04f, 0.64f) : w == 2 ? new Vector3(0, 0.022f, 0.84f) : new Vector3(0, 0.03f, 0.17f);
        }

        // AWP scope picture: black all around, round lens, thin crosshair with thicker outer posts
        static Texture2D scopeTex;
        void OnGUI()
        {
            if (!Scoped) return;
            if (scopeTex == null)
            {
                int S = 512; scopeTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[S * S];
                for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S * 2 - 1, dy = (y + 0.5f) / S * 2 - 1, r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01((r - 0.985f) * 80f);                      // hard lens edge
                    float vig = Mathf.Clamp01((r - 0.8f) / 0.2f) * 0.55f;              // darker toward the rim
                    px[y * S + x] = new Color32(0, 0, 0, (byte)(Mathf.Max(a, vig) * 255));
                }
                scopeTex.SetPixels32(px); scopeTex.Apply();
            }
            float W = Screen.width, H = Screen.height, D = H * 0.98f, x0 = (W - D) / 2f, y0 = (H - D) / 2f;
            var blk = Texture2D.blackTexture; GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, x0 + 1, H), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(x0 + D - 1, 0, W - x0 - D + 1, H), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, 0, W, y0 + 1), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(0, y0 + D - 1, W, H), Texture2D.whiteTexture);
            GUI.color = Color.white; GUI.DrawTexture(new Rect(x0, y0, D, D), scopeTex);
            GUI.color = Color.black;
            float cx = W / 2f, cy = H / 2f, t = 1f, T = Mathf.Max(3f, H / 300f), post = D * 0.32f;
            GUI.DrawTexture(new Rect(x0, cy - t / 2, D, t), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(cx - t / 2, y0, t, D), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x0, cy - T / 2, post, T), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(x0 + D - post, cy - T / 2, post, T), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - T / 2, y0 + D - post, T, post), Texture2D.whiteTexture);
            GUI.color = new Color(1, 1, 1, 0.85f);
            var st = new GUIStyle(GUI.skin.label) { fontSize = (int)(H / 60f), fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(x0 + D * 0.72f, y0 + D * 0.8f, 200, 40), zoom.ToString("0") + "x", st);
            GUI.color = Color.white; _ = blk;
        }

        Transform impact; float impactT;
        // a small bright dot exactly where the bullet landed (for a moment)
        void ShowImpact(Vector3 p)
        {
            if (impact == null)
            {
                impact = Mats.Vis(PrimitiveType.Sphere, null, Vector3.zero, Vector3.one * 0.02f, Mats.Decal(Texture2D.whiteTexture, new Color(1f, 0.95f, 0.6f, 1f), 3100), false).transform;
            }
            impact.position = p; impact.gameObject.SetActive(true); impactT = 0.25f;
        }

        void Shoot()
        {
            if (cool > 0 || reloadT > 0) return;
            if (ammo <= 0) { au.PlayOneShot(clickClip, 0.6f); return; }
            if (AWP && boltT > 0) return;
            ammo--; cool = AWP ? 0.1f : AK ? 0.1f : 0.12f; recoil = AWP ? 1.6f : AK ? 0.7f : 1f; flashT = AWP ? 0.07f : 0.04f;
            if (AWP) boltT = 1.2f;
            au.PlayOneShot(AWP ? awpClip : shotClip, AWP ? 1f : 0.8f);
            // AWP: dead on through the scope, wild when fired from the hip
            float spread = AWP ? (Scoped ? 0.0003f : 0.04f) : 0.004f;
            var dir = (cam.transform.forward + Random.insideUnitSphere * spread).normalized;
            var ray = new Ray(cam.transform.position, dir);
            Vector3 end = ray.origin + dir * 200f;
            // 1) the drawn skin of every Carl (alive, down, dead, carved, cut pieces) - exactly what you see
            if (AWP)
            {
                // .338 Lapua goes straight through a body and on into the next one
                Mannequin first = null;
                if (Mannequin.PickSkin(ray.origin, dir, 400f, out Part p1, out float t1, out Vector3 n1))
                {
                    first = p1.owner;
                    bool wall1 = Physics.Raycast(ray, out RaycastHit w1, t1, ~((1 << 2) | (1 << Mannequin.LayerWalk) | (1 << Mannequin.LayerRag)), QueryTriggerInteraction.Ignore);
                    if (!wall1 && Mannequin.PickSkin(ray.origin, dir, 400f, out Part p2, out float t2, out Vector3 n2, first) && t2 > t1 + 0.3f
                        && !Physics.Raycast(ray, t2, ~((1 << 2) | (1 << Mannequin.LayerWalk) | (1 << Mannequin.LayerRag)), QueryTriggerInteraction.Ignore))
                        p2.owner.Hit(p2, null, ray.origin + dir * t2, dir, n2);
                }
            }
            bool body = Mannequin.PickSkin(ray.origin, dir, 400f, out Part bp, out float bt, out Vector3 bn);
            bool world = Physics.Raycast(ray, out RaycastHit h, 400f, ~((1 << 2) | (1 << Mannequin.LayerWalk) | (1 << Mannequin.LayerRag)), QueryTriggerInteraction.Ignore);
            if (body && (!world || bt <= h.distance + 0.02f))
            {
                end = ray.origin + dir * bt; bp.owner.Hit(bp, null, end, dir, bn); hitMarkT = 0.2f;
            }
            else if (world)
            {
                end = h.point;
                if (h.rigidbody != null) h.rigidbody.AddForceAtPosition(dir * Game.BulletImpulse, h.point, ForceMode.Impulse);
                // no bullet marks on/under bodies (they showed through carved holes as grey discs)
                else if (Mannequin.Nearest(h.point, 0.45f) == null) Blood.I.Hole(h.point, h.normal);
                Game.LastShot = h.collider.name.ToUpper();
            }
            tracer.SetPosition(0, flash.transform.position);
            tracer.SetPosition(1, end);
            tracerT = (end - cam.transform.position).magnitude > 3f ? (AWP ? 0.06f : 0.025f) : 0f;
            if (Game.I != null) Game.I.Gunshot(cam.transform.position, end);
        }

        static AudioClip MakeShot(float boom, float len)
        {
            int sr = 44100, n = (int)(sr * len);
            var d = new float[n]; var rng = new System.Random(3);
            float lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float noise = (float)(rng.NextDouble() * 2 - 1);
                lp += (noise - lp) * 0.35f;
                d[i] = (lp * Mathf.Exp(-t * 18f * 0.35f / len) * 0.9f + Mathf.Sin(t * 2 * Mathf.PI * boom) * Mathf.Exp(-t * 25f * 0.35f / len) * 0.7f);
            }
            var c = AudioClip.Create("shot", n, 1, sr, false); c.SetData(d, 0); return c;
        }

        static AudioClip MakeClick()
        {
            int sr = 44100, n = sr / 20; var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = Mathf.Sin(i * 0.9f) * Mathf.Exp(-i / 200f) * 0.5f;
            var c = AudioClip.Create("click", n, 1, sr, false); c.SetData(d, 0); return c;
        }
    }
}
