using UnityEngine;
using UnityEngine.SceneManagement;
using System.Reflection;

namespace VITS
{
    // Entry point: builds the whole test site in any scene you press Play in.
    public class Game : MonoBehaviour
    {
        public static Game I;
        public static float BulletImpulse => Player.AWP ? 14f : Player.AK ? 6f : 3.5f; // momentum of a 9 mm / 7.62x39 / .338 Lapua round, rounded up
        public static bool Brains = true;
        public static string LastShot = ""; // Esc menu: when off, Carls stand still until shot at // N·s pushed into a ragdoll part by a 9 mm round (gamey, not realistic)
        public Player player;
        static readonly float[] TS = { 0.25f, 0.5f, 1f, 2f };
        int tsi = 2; bool paused;
        GUIStyle st; Texture2D panelTex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<Game>() == null) new GameObject("VITS Game").AddComponent<Game>();
        }

        void Awake()
        {
            I = this;
            Gib.Clear();
            // disable the template's camera and lights: we bring our own
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.gameObject.SetActive(false);
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) l.gameObject.SetActive(false);

            Physics.defaultSolverIterations = 20; Physics.defaultSolverVelocityIterations = 8;
            // walking (animated) people must not bulldoze bodies and pieces lying around
            Physics.IgnoreLayerCollision(Mannequin.LayerWalk, Mannequin.LayerRag, true);
            Physics.IgnoreLayerCollision(Mannequin.LayerWalk, 2, true);
            Application.logMessageReceived += OnLog;
            FlatLook(true);
            Physics.gravity = new Vector3(0, -9.81f, 0);
            ApplyTime();

            Level.Build();
            new GameObject("Blood").AddComponent<Blood>();
            var pgo = new GameObject("Player");
            pgo.transform.SetPositionAndRotation(new Vector3(0, 0.05f, -14f), Quaternion.identity);
            player = pgo.AddComponent<Player>();
            for (int i = 0; i < 10; i++) Spawn();
        }

        public static string LastError = "";
        void OnLog(string msg, string stack, LogType t)
        {
            if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert) LastError = msg.Length > 160 ? msg.Substring(0, 160) : msg;
        }

        // the reference game has a flat look: no ambient occlusion (it also darkens holes in the skin) and no post effects
        readonly System.Collections.Generic.List<object> disabledFeatures = new System.Collections.Generic.List<object>();
        void FlatLook(bool on)
        {
            try
            {
                if (on)
                {
                    foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                        if (root.GetComponent("Volume") != null && root != gameObject) root.SetActive(false);
                }
                var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                if (rp == null) return;
                if (!on) { foreach (var f in disabledFeatures) f.GetType().GetMethod("SetActive")?.Invoke(f, new object[] { true }); disabledFeatures.Clear(); return; }
                var field = rp.GetType().GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
                if (!(field?.GetValue(rp) is System.Array list)) return;
                foreach (var rd in list)
                {
                    if (rd == null) continue;
                    var feats = rd.GetType().GetProperty("rendererFeatures", BindingFlags.Public | BindingFlags.Instance)?.GetValue(rd) as System.Collections.IList;
                    if (feats == null) continue;
                    foreach (var f in feats)
                    {
                        if (f == null || !f.GetType().Name.Contains("AmbientOcclusion")) continue;
                        var active = f.GetType().GetProperty("isActive")?.GetValue(f);
                        if (active is bool b && !b) continue;
                        f.GetType().GetMethod("SetActive")?.Invoke(f, new object[] { false });
                        disabledFeatures.Add(f);
                    }
                }
            }
            catch (System.Exception e) { Debug.LogWarning("FlatLook: " + e.Message); }
        }

        void OnDestroy() { FlatLook(false); Application.logMessageReceived -= OnLog; }

        public void Spawn()
        {
            var go = new GameObject("Mannequin");
            go.transform.SetPositionAndRotation(RandomPoint(), Quaternion.Euler(0, Random.Range(0, 360f), 0));
            go.AddComponent<Mannequin>();
        }

        // Y: a new Carl where the crosshair points (on the floor under that spot, pushed out of walls), facing you
        public void SpawnAtCrosshair()
        {
            var cam = Camera.main; if (cam == null) { Spawn(); return; }
            int mask = ~((1 << 2) | (1 << Mannequin.LayerWalk) | (1 << Mannequin.LayerRag));
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            Vector3 p = Physics.Raycast(ray, out RaycastHit h, 60f, mask, QueryTriggerInteraction.Ignore) ? h.point + h.normal * 0.35f : ray.origin + ray.direction * 8f;
            if (Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out RaycastHit g, 50f, mask, QueryTriggerInteraction.Ignore)) p = g.point;
            p.y = Mathf.Max(0f, p.y);
            for (int i = 0; i < 12 && Physics.CheckCapsule(p + Vector3.up * 0.4f, p + Vector3.up * 1.5f, 0.28f, mask, QueryTriggerInteraction.Ignore); i++)
            {
                Vector3 back = cam.transform.position - p; back.y = 0; p += back.normalized * 0.15f;
            }
            Vector3 face = cam.transform.position - p; face.y = 0;
            var go = new GameObject("Mannequin");
            go.transform.SetPositionAndRotation(p, face.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(face) : Quaternion.identity);
            go.AddComponent<Mannequin>();
        }

        public static Vector3 RandomPoint()
        {
            for (int i = 0; i < 40; i++)
            {
                var p = new Vector3(Random.Range(-11f, 11f), 0, Random.Range(-8f, 8f));
                if (!Physics.CheckSphere(p + Vector3.up * 1f, 0.6f, ~0, QueryTriggerInteraction.Ignore)) return p;
            }
            return Vector3.zero;
        }

        public static Vector3 Clamp(Vector3 p)
        {
            p.x = Mathf.Clamp(p.x, -Level.HX + 1f, Level.HX - 1f);
            p.z = Mathf.Clamp(p.z, -Level.HZ + 1f, Level.HZ - 1f);
            return p;
        }

        // a spot behind the nearest cover wall, on the side away from the shooter
        // a spot behind a cover wall, on the side away from the shooter; avoids crowded spots and 'avoid'
        public static Vector3 CoverPoint(Vector3 from, Vector3 avoid)
        {
            Vector3 shooter = I != null && I.player != null ? I.player.transform.position : Vector3.zero;
            Vector3 best = RandomPoint(); float bd = 1e9f;
            foreach (var c in Level.Covers)
            {
                Vector3 away = c.pos - shooter; away.y = 0;
                // hide behind the long side of the wall
                Vector3 n = c.normal * Mathf.Sign(Vector3.Dot(c.normal, away) + 1e-4f);
                float side = Mathf.Clamp(Vector3.Dot(from - c.pos, c.along), -1.1f, 1.1f);
                Vector3 hide = c.pos + n * 0.75f + c.along * side; hide.y = 0;
                float d = (hide - from).magnitude;
                if ((hide - avoid).magnitude < 1f) d += 30f;
                foreach (var m in Mannequin.All) if (m != null && !m.dead && (m.transform.position - hide).magnitude < 0.8f && (m.transform.position - from).magnitude > 0.1f) d += 4f;
                if (d < bd) { bd = d; best = hide; }
            }
            return best;
        }

        // everybody near hears the shot and runs; with brains off only those the bullet passed close to react
        public void Gunshot(Vector3 from, Vector3 end)
        {
            foreach (var m in Mannequin.All)
            {
                if (m.dead || m.ragdoll) continue;
                Vector3 c = m.transform.position + Vector3.up * 1.2f, seg = end - from;
                float t = Mathf.Clamp01(Vector3.Dot(c - from, seg) / Mathf.Max(1e-4f, seg.sqrMagnitude));
                bool close = (from + seg * t - c).magnitude < 2f;
                if (close || (Brains && (m.transform.position - from).magnitude < 25f)) m.Flee();
            }
        }

        bool menuPause;
        public void SetMenuPause(bool on) { menuPause = on; ApplyTime(); }

        void ApplyTime()
        {
            Time.timeScale = (paused || menuPause) ? 0f : TS[tsi];
            Time.fixedDeltaTime = (1f / 90f) * Mathf.Max(0.25f, TS[tsi]);
        }

        void Update()
        {
            if (GI.Down(K.N)) Spawn();
            if (GI.Down(K.Y)) SpawnAtCrosshair();
            if (GI.Down(K.T)) XRay.Toggle();
            if (GI.Down(K.Back)) { XRay.On = false; Skin.All.Clear(); SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); return; }
            if (GI.Down(K.LBracket)) { tsi = Mathf.Max(0, tsi - 1); ApplyTime(); }
            if (GI.Down(K.RBracket)) { tsi = Mathf.Min(TS.Length - 1, tsi + 1); ApplyTime(); }
            if (GI.Down(K.P)) { paused = !paused; ApplyTime(); }
        }

        // ---------------- HUD ----------------
        void Label(Rect r, string s, int size, TextAnchor a, float alpha = 0.92f)
        {
            st.fontSize = size; st.alignment = a;
            st.normal.textColor = new Color(0, 0, 0, 0.45f); GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), s, st);
            st.normal.textColor = new Color(0.95f, 0.96f, 0.98f, alpha); GUI.Label(r, s, st);
        }

        void OnGUI()
        {
            if (player == null) return;
            if (st == null) { st = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, richText = true }; panelTex = new Texture2D(1, 1); panelTex.SetPixel(0, 0, new Color(0.08f, 0.1f, 0.12f, 0.88f)); panelTex.Apply(); }
            float s = Screen.height / 1080f; int W = Screen.width, H = Screen.height;
            int alive = 0; foreach (var m in Mannequin.All) if (!m.dead) alive++;

            Label(new Rect(0, 14 * s, W, 30 * s), $"SPECIMENS  {alive:00} / 100   <size={(int)(14 * s)}>· 90 HZ</size>", (int)(20 * s), TextAnchor.UpperCenter);
            Label(new Rect(W - 330 * s, 14 * s, 300 * s, 34 * s), paused ? "TIME  PAUSED" : $"TIME  {TS[tsi]:0.0}x", (int)(26 * s), TextAnchor.UpperRight);
            Label(new Rect(W - 330 * s, 48 * s, 300 * s, 22 * s), XRay.On ? "X-RAY  ·  FREE MOVEMENT" : "FREE MOVEMENT", (int)(14 * s), TextAnchor.UpperRight, 0.7f);

            Label(new Rect(W - 330 * s, H - 150 * s, 300 * s, 22 * s), Player.Names[Player.Weapon], (int)(14 * s), TextAnchor.UpperRight, 0.7f);
            string am = player.reloadT > 0 ? "..." : player.ammo.ToString("00");
            Label(new Rect(W - 330 * s, H - 128 * s, 300 * s, 70 * s), $"{am}<size={(int)(24 * s)}> / {player.MaxAmmo}</size>", (int)(56 * s), TextAnchor.UpperRight);
            Label(new Rect(W - 330 * s, H - 58 * s, 300 * s, 22 * s), "∞  RESERVE", (int)(14 * s), TextAnchor.UpperRight, 0.6f);
            Label(new Rect(20 * s, H - 36 * s, W, 24 * s), "WASD move · SHIFT run · SPACE jump · LMB fire · RMB aim · MMB hold: grab & drag · R reload · T x-ray · Y spawn Carl · [ ] time · P pause · BACKSPACE reset · ESC mouse", (int)(13 * s), TextAnchor.UpperLeft, 0.55f);

            // crosshair (the AWP scope has its own)
            float cx = W / 2f, cy = H / 2f;
            if (!player.Scoped) {
            var cc = player.looked != null ? new Color(1f, 0.35f, 0.3f, 0.95f) : new Color(1, 1, 1, 0.9f);
            GUI.color = cc; float g = 5 * s, l = 8 * s, t = Mathf.Max(1, 2 * s);
            GUI.DrawTexture(new Rect(cx - g - l, cy - t / 2, l, t), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(cx + g, cy - t / 2, l, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - t / 2, cy - g - l, t, l), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(cx - t / 2, cy + g, t, l), Texture2D.whiteTexture);
            if (player.hitMarkT > 0)
            {
                GUI.color = new Color(1f, 0.25f, 0.2f, 1f); float q = 7 * s;
                GUIUtility.RotateAroundPivot(45, new Vector2(cx, cy));
                GUI.DrawTexture(new Rect(cx - g - l - q, cy - t / 2, l, t), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(cx + g + q, cy - t / 2, l, t), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx - t / 2, cy - g - l - q, t, l), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(cx - t / 2, cy + g + q, t, l), Texture2D.whiteTexture);
                GUI.matrix = Matrix4x4.identity;
            }
            }
            GUI.color = Color.white;
            Label(new Rect(0, cy + 40 * s, W, 20 * s), LastShot.Length > 0 ? "LAST SHOT: " + LastShot : "", (int)(12 * s), TextAnchor.UpperCenter, 0.6f);
            Label(new Rect(0, cy + 22 * s, W, 20 * s), player.held != null ? "HOLDING" : player.aimInfo, (int)(12 * s), TextAnchor.UpperCenter, 0.75f);

            if (LastError.Length > 0) { st.normal.textColor = new Color(1f, 0.3f, 0.3f); st.fontSize = (int)(13 * s); st.alignment = TextAnchor.UpperLeft; GUI.Label(new Rect(20 * s, H - 62 * s, W - 40 * s, 24 * s), "ERROR: " + LastError, st); }
            if (player.menu)
            {
                GUI.color = Color.white;
                var box = new Rect(W / 2f - 190 * s, H / 2f - 250 * s, 380 * s, 500 * s);
                GUI.DrawTexture(box, panelTex);
                Label(new Rect(box.x, box.y + 14 * s, box.width, 30 * s), "VERTICAL IMPACT TESTSITE", (int)(20 * s), TextAnchor.UpperCenter);
                var bs = new GUIStyle(GUI.skin.button) { fontSize = (int)(18 * s), fontStyle = FontStyle.Bold };
                float bx = box.x + 30 * s, bw = box.width - 60 * s, bh = 42 * s, y0 = box.y + 60 * s;
                if (GUI.Button(new Rect(bx, y0, bw, bh), (Player.Weapon == 0 ? "▶ " : "  ") + "PISTOL  (9 mm, semi-auto)", bs)) player.SetWeapon(0);
                if (GUI.Button(new Rect(bx, y0 + 52 * s, bw, bh), (Player.Weapon == 1 ? "▶ " : "  ") + "AK-47  (7.62, full auto)", bs)) player.SetWeapon(1);
                if (GUI.Button(new Rect(bx, y0 + 104 * s, bw, bh), (Player.Weapon == 2 ? "▶ " : "  ") + "AWP  (.338, bolt, scope)", bs)) player.SetWeapon(2);
                if (GUI.Button(new Rect(bx, y0 + 156 * s, bw, bh), "CARL BRAINS: " + (Brains ? "ON (walk around)" : "OFF (wait until shot)"), bs)) Brains = !Brains;
                // settings: mouse sensitivity and aim (ADS) sensitivity multiplier
                float sy = y0 + 214 * s;
                Label(new Rect(bx, sy, bw, 22 * s), $"SENSITIVITY  {Player.Sens:0.00}", (int)(15 * s), TextAnchor.UpperLeft);
                float ns = GUI.HorizontalSlider(new Rect(bx, sy + 24 * s, bw, 20 * s), Player.Sens, 0.1f, 4f);
                Label(new Rect(bx, sy + 50 * s, bw, 22 * s), $"ADS SENSITIVITY  {Player.AdsSens:0.00}x", (int)(15 * s), TextAnchor.UpperLeft);
                float na = GUI.HorizontalSlider(new Rect(bx, sy + 74 * s, bw, 20 * s), Player.AdsSens, 0.1f, 2f);
                if (ns != Player.Sens || na != Player.AdsSens) { Player.Sens = Mathf.Round(ns * 20f) / 20f; Player.AdsSens = Mathf.Round(na * 20f) / 20f; Player.SaveSens(); }
                if (GUI.Button(new Rect(bx, sy + 110 * s, bw, bh), "RESUME  (Esc)", bs)) player.SetMenu(false);
                return;
            }
            if (!player.locked) Label(new Rect(0, H * 0.55f, W, 30 * s), "CLICK TO PLAY", (int)(22 * s), TextAnchor.UpperCenter);

            // medical monitor
            var d = player.looked;
            if (d != null)
            {
                var r = new Rect(16 * s, 16 * s, 380 * s, (212 + d.injuries.Count * 20) * s);
                GUI.DrawTexture(r, panelTex);
                GUI.DrawTexture(new Rect(r.x, r.y, 4 * s, r.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, new Color(1f, 0.45f, 0.18f), 0, 0);
                float y = r.y + 10 * s; float x = r.x + 16 * s;
                Label(new Rect(x, y, 360 * s, 30 * s), "VITAL MONITOR", (int)(12 * s), TextAnchor.UpperLeft, 0.5f); y += 18 * s;
                Label(new Rect(x, y, 360 * s, 34 * s), d.displayName, (int)(24 * s), TextAnchor.UpperLeft); y += 32 * s;
                string life = d.dead ? "<color=#ff5a50>DEAD</color>" : d.conscious ? "<color=#5cd08f>ALIVE · CONSCIOUS</color>" : "<color=#f0b040>ALIVE · FAINTED</color>";
                Label(new Rect(x, y, 360 * s, 24 * s), life, (int)(14 * s), TextAnchor.UpperLeft); y += 24 * s;
                Label(new Rect(x, y, 360 * s, 24 * s), "STATUS  " + d.status, (int)(13 * s), TextAnchor.UpperLeft, 0.8f); y += 22 * s;
                Label(new Rect(x, y, 360 * s, 24 * s), $"LEG SUPPORT  L {d.legFn[0] * 100f:0}%  R {d.legFn[1] * 100f:0}%   EFFORT {d.support * 100f:0}%", (int)(12 * s), TextAnchor.UpperLeft, 0.7f); y += 20 * s;
                float bl = Mathf.Max(0, d.blood) / 1000f;
                Label(new Rect(x, y, 360 * s, 24 * s), $"BLOOD  {bl:0.00} OF 5.0 L  ({d.blood / Mannequin.BloodMax * 100f:0}%)", (int)(13 * s), TextAnchor.UpperLeft, 0.8f); y += 20 * s;
                GUI.DrawTexture(new Rect(x, y, 340 * s, 5 * s), Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, new Color(1, 1, 1, 0.15f), 0, 0);
                GUI.DrawTexture(new Rect(x, y, 340 * s * Mathf.Clamp01(d.blood / Mannequin.BloodMax), 5 * s), Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, new Color(0.85f, 0.1f, 0.12f), 0, 0);
                y += 14 * s;
                Label(new Rect(x, y, 360 * s, 24 * s), "LAST HIT  " + d.lastHit + (d.lastHitTime > 0 ? $"  ·  {Time.time - d.lastHitTime:0} S AGO" : ""), (int)(13 * s), TextAnchor.UpperLeft, 0.8f); y += 24 * s;
                Label(new Rect(x, y, 360 * s, 24 * s), "<color=#ff7a3c>ACTIVE WOUNDS</color>", (int)(12 * s), TextAnchor.UpperLeft); y += 20 * s;
                foreach (var inj in d.injuries) { Label(new Rect(x, y, 360 * s, 22 * s), inj, (int)(12 * s), TextAnchor.UpperLeft, 0.75f); y += 20 * s; }
            }
        }
    }
}
