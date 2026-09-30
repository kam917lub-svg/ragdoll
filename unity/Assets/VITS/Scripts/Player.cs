using UnityEngine;

namespace VITS
{
    // First person controller + semi-auto pistol (15 rounds, R reload).
    public class Player : MonoBehaviour
    {
        public Camera cam;
        public Mannequin looked;
        public int ammo = 15; public int MaxAmmo => AK ? 30 : 15;
        public static bool AK;               // chosen in the Esc menu
        public bool menu;
        Transform pistolModel, akModel;
        int pistolAmmo = 15, akAmmo = 30;
        public float reloadT;
        public bool locked;
        public string aimInfo = ""; public float hitMarkT;
        public Rigidbody held; Vector3 heldLocal; float heldDist; LineRenderer beam;

        CharacterController cc;
        float yaw, pitch, vy, cool, recoil, flashT, tracerT, bob;
        Transform gun, slide;
        Light flash;
        LineRenderer tracer;
        AudioSource au; AudioClip shotClip, clickClip;

        void Start()
        {
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
            shotClip = MakeShot(); clickClip = MakeClick();
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
            var fgo = new GameObject("Flash"); fgo.transform.SetParent(gun, false); fgo.transform.localPosition = new Vector3(0, 0.03f, 0.17f);
            flash = fgo.AddComponent<Light>(); flash.type = LightType.Point; flash.range = 8f; flash.intensity = 0; flash.color = new Color(1f, 0.85f, 0.6f);

            var tgo = new GameObject("Tracer");
            tracer = tgo.AddComponent<LineRenderer>();
            tracer.positionCount = 2; tracer.startWidth = 0.012f; tracer.endWidth = 0.004f;
            tracer.sharedMaterial = Mats.Decal(Texture2D.whiteTexture, new Color(1f, 0.95f, 0.8f, 0.8f));
            tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; tracer.enabled = false;
            beam = new GameObject("GrabBeam").AddComponent<LineRenderer>();
            beam.positionCount = 2; beam.startWidth = 0.008f; beam.endWidth = 0.008f;
            beam.sharedMaterial = Mats.Decal(Texture2D.whiteTexture, new Color(1f, 0.45f, 0.15f, 0.9f));
            beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; beam.enabled = false;
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
            if (locked && (AK ? GI.FireHeld() : GI.FireDown())) Shoot();
            if (GI.Down(K.R) && ammo < MaxAmmo && reloadT <= 0) reloadT = 1.4f;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, GI.AimHeld() ? 45f : 70f, 1 - Mathf.Exp(-dt * 12f));
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
                if (Mannequin.Pick(cam.transform.position, cam.transform.forward, 6f, out Part gp, out float gd))
                {
                    gp.owner.Grabbed();
                    held = gp.rb; heldDist = gd; heldLocal = gp.transform.InverseTransformPoint(cam.transform.position + cam.transform.forward * gd);
                }
                else if (Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit gh, 6f, ~0, QueryTriggerInteraction.Ignore) && gh.rigidbody != null && !gh.rigidbody.isKinematic)
                { held = gh.rigidbody; heldDist = gh.distance; heldLocal = held.transform.InverseTransformPoint(gh.point); }
            }
            if (!GI.GrabHeld()) held = null;
            if (held != null) heldDist = Mathf.Clamp(heldDist + GI.Scroll() * 0.25f, 0.8f, 5f);
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
            Vector3 f = (target - p) * 900f - v * 60f;
            held.AddForceAtPosition(Vector3.ClampMagnitude(f, 1500f), p);
        }

        public void SetMenu(bool on)
        {
            menu = on;
            locked = !on;
            Cursor.lockState = on ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = on;
            if (Game.I != null) Game.I.SetMenuPause(on);
        }

        public void SetWeapon(bool ak)
        {
            if (AK) akAmmo = ammo; else pistolAmmo = ammo;
            AK = ak; ammo = ak ? akAmmo : pistolAmmo; reloadT = 0;
            pistolModel.gameObject.SetActive(!ak); akModel.gameObject.SetActive(ak);
            flash.transform.localPosition = ak ? new Vector3(0, 0.04f, 0.64f) : new Vector3(0, 0.03f, 0.17f);
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
            ammo--; cool = AK ? 0.1f : 0.12f; recoil = AK ? 0.7f : 1f; flashT = 0.04f;
            au.PlayOneShot(shotClip, 0.8f);
            var dir = (cam.transform.forward + Random.insideUnitSphere * 0.004f).normalized;
            var ray = new Ray(cam.transform.position, dir);
            Vector3 end = ray.origin + dir * 200f;
            if (Physics.Raycast(ray, out RaycastHit h, 200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                end = h.point;
                // a body in front of what the physics hit? (bullets hit the visible skin, not the rough colliders)
                if (Mannequin.Pick(ray.origin, dir, h.distance + 0.02f, out Part sp, out float st)) { end = ray.origin + dir * st; sp.owner.Hit(sp, null, end, dir, -dir); hitMarkT = 0.2f; }
                else
                {
                    var part = h.collider.GetComponent<Part>();
                    if (part != null) { part.owner.Hit(part, h.collider, h.point, dir, h.normal); hitMarkT = 0.2f; }
                    else if (h.rigidbody != null) h.rigidbody.AddForceAtPosition(dir * Game.BulletImpulse, h.point, ForceMode.Impulse);
                    else { Blood.I.Hole(h.point, h.normal); Game.LastShot = h.collider.name.ToUpper(); }
                }
            }
            else if (Mannequin.Pick(ray.origin, dir, 200f, out Part sp2, out float st2)) { end = ray.origin + dir * st2; sp2.owner.Hit(sp2, null, end, dir, -dir); hitMarkT = 0.2f; }
            ShowImpact(end);
            tracer.SetPosition(0, flash.transform.position);
            tracer.SetPosition(1, end);
            tracerT = 0.03f;
            if (Game.I != null) Game.I.Gunshot(cam.transform.position, end);
        }

        static AudioClip MakeShot()
        {
            int sr = 44100, n = (int)(sr * 0.35f);
            var d = new float[n]; var rng = new System.Random(3);
            float lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float noise = (float)(rng.NextDouble() * 2 - 1);
                lp += (noise - lp) * 0.35f;
                d[i] = (lp * Mathf.Exp(-t * 18f) * 0.9f + Mathf.Sin(t * 2 * Mathf.PI * 70f) * Mathf.Exp(-t * 25f) * 0.7f);
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
