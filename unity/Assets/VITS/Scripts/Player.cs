using UnityEngine;

namespace VITS
{
    // First person controller + semi-auto pistol (15 rounds, R reload).
    public class Player : MonoBehaviour
    {
        public Camera cam;
        public Mannequin looked;
        public int ammo = 15; public const int MaxAmmo = 15;
        public float reloadT;
        public bool locked;
        public string aimInfo = ""; public float hitMarkT;

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
            var fgo = new GameObject("Flash"); fgo.transform.SetParent(gun, false); fgo.transform.localPosition = new Vector3(0, 0.03f, 0.17f);
            flash = fgo.AddComponent<Light>(); flash.type = LightType.Point; flash.range = 8f; flash.intensity = 0; flash.color = new Color(1f, 0.85f, 0.6f);

            var tgo = new GameObject("Tracer");
            tracer = tgo.AddComponent<LineRenderer>();
            tracer.positionCount = 2; tracer.startWidth = 0.012f; tracer.endWidth = 0.004f;
            tracer.sharedMaterial = Mats.Decal(Texture2D.whiteTexture, new Color(1f, 0.95f, 0.8f, 0.8f));
            tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; tracer.enabled = false;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (GI.Down(K.Esc)) { locked = false; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
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
            if (locked && GI.FireDown()) Shoot();
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
        }

        void Shoot()
        {
            if (cool > 0 || reloadT > 0) return;
            if (ammo <= 0) { au.PlayOneShot(clickClip, 0.6f); return; }
            ammo--; cool = 0.12f; recoil = 1f; flashT = 0.04f;
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
                    else Blood.I.Hole(h.point, h.normal);
                }
            }
            else if (Mannequin.Pick(ray.origin, dir, 200f, out Part sp2, out float st2)) { end = ray.origin + dir * st2; sp2.owner.Hit(sp2, null, end, dir, -dir); hitMarkT = 0.2f; }
            tracer.SetPosition(0, gun.TransformPoint(new Vector3(0, 0.03f, 0.14f)));
            tracer.SetPosition(1, end);
            tracerT = 0.03f;
            if (Game.I != null) Game.I.Gunshot(transform.position);
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
