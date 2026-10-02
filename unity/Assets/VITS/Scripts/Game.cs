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
            // RESET / model change reload the scene: the site must be rebuilt every time, not only at the first start
            SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded;
        }
        static void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            if (FindAnyObjectByType<Game>() == null) new GameObject("VITS Game").AddComponent<Game>();
        }

        void Awake()
        {
            I = this;
            Mannequin.LoadModel();
            Gib.Clear();
            // disable the template's camera and lights: we bring our own
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.gameObject.SetActive(false);
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) l.gameObject.SetActive(false);

            Physics.defaultSolverIterations = 12; Physics.defaultSolverVelocityIterations = 4;   // plenty for these joints, much cheaper
            // walking (animated) people must not bulldoze bodies and pieces lying around
            Physics.IgnoreLayerCollision(Mannequin.LayerWalk, Mannequin.LayerRag, true);
            Physics.IgnoreLayerCollision(Mannequin.LayerWalk, 2, false);   // the player (layer 2) must bump into walking Carls
            Application.logMessageReceived += OnLog;
            FlatLook(true);
            Physics.gravity = new Vector3(0, -9.81f, 0);
            ApplyTime();

            // the player (and its camera) first: whatever goes wrong after this, you still see the site and the error line
            var pgo = new GameObject("Player") { layer = 2 };   // Ignore Raycast: bullets and aim rays start inside your own capsule
            pgo.transform.SetPositionAndRotation(new Vector3(0, 0.05f, -14f), Quaternion.identity);
            player = pgo.AddComponent<Player>();
            try { Level.Build(); } catch (System.Exception e) { Debug.LogException(e); }
            try { OptimizeStatic(); } catch (System.Exception e) { Debug.LogException(e); }
            pgo.transform.position = Level.PlayerStart;
            // supermarket: a cashier behind every till
            foreach (var c in Level.Cashiers)
            {
                var cg = new GameObject("Mannequin");
                cg.transform.SetPositionAndRotation(c, Quaternion.LookRotation(Vector3.left));
                cg.AddComponent<Mannequin>().SetCashier(c, Vector3.left);
            }
            try { new GameObject("Blood").AddComponent<Blood>(); } catch (System.Exception e) { Debug.LogException(e); }
            for (int i = 0; i < 10; i++) { try { Spawn(); } catch (System.Exception e) { Debug.LogException(e); } }
        }

        // ---------- video: frame limiter and motion blur (saved)
        static readonly int[] FpsSteps = { 60, 120, 144, 165, 240, 0 };
        public static int FpsIdx = 5; public static float Blur;
        public static void ApplyVideo()
        {
            FpsIdx = PlayerPrefs.GetInt("vits_fps", 5); Blur = PlayerPrefs.GetFloat("vits_blur", 0f);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = FpsSteps[Mathf.Clamp(FpsIdx, 0, FpsSteps.Length - 1)] == 0 ? -1 : FpsSteps[FpsIdx];
            SetMotionBlur(Blur);
        }
        static Component blurVol; static object blurComp;
        static void SetMotionBlur(float v)
        {
            try
            {
                var volT = System.Type.GetType("UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime");
                var profT = System.Type.GetType("UnityEngine.Rendering.VolumeProfile, Unity.RenderPipelines.Core.Runtime");
                var mbT = System.Type.GetType("UnityEngine.Rendering.Universal.MotionBlur, Unity.RenderPipelines.Universal.Runtime");
                if (volT == null || profT == null || mbT == null) return;
                if (blurVol == null)
                {
                    var go = new GameObject("VITS Motion Blur");
                    blurVol = go.AddComponent(volT);
                    volT.GetField("isGlobal")?.SetValue(blurVol, true);
                    volT.GetField("priority")?.SetValue(blurVol, 100f);
                    var prof = ScriptableObject.CreateInstance(profT);
                    blurComp = profT.GetMethod("Add", new[] { typeof(System.Type), typeof(bool) }).Invoke(prof, new object[] { mbT, true });
                    volT.GetProperty("profile")?.SetValue(blurVol, prof);
                }
                var inten = mbT.GetField("intensity").GetValue(blurComp);
                inten.GetType().GetMethod("Override", new[] { typeof(float) })?.Invoke(inten, new object[] { v });
                mbT.GetField("active")?.SetValue(blurComp, v > 0.001f);
                // the camera must render post effects for the blur to show
                var camT = System.Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
                var cam = Camera.main;
                if (camT != null && cam != null)
                {
                    var data = cam.GetComponent(camT) ?? cam.gameObject.AddComponent(camT);
                    camT.GetProperty("renderPostProcessing")?.SetValue(data, v > 0.001f);
                }
            }
            catch (System.Exception e) { Debug.LogWarning("motion blur: " + e.Message); }
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

        // start over: every Carl, all blood, pieces, time scale, back to a fresh test site
        public void ResetAll()
        {
            XRay.On = false; Skin.All.Clear(); Gib.Clear(); LastShot = "";
            Mannequin.ResetStatics();
            Time.timeScale = 1f; paused = false;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        // professional-game style scene prep: everything that never moves is merged into static batches (far fewer draw calls),
        // and small props stop casting shadows (shadow passes are the most expensive part of the frame)
        static void OptimizeStatic()
        {
            var list = new System.Collections.Generic.List<GameObject>();
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.GetComponentInChildren<Rigidbody>() != null || root.GetComponentInChildren<Lift>() != null || root.GetComponentInChildren<Grinder>() != null
                    || root.GetComponentInChildren<Bat>() != null || root.GetComponentInChildren<Mannequin>() != null || root.GetComponentInChildren<Player>() != null) continue;
                foreach (var mr in root.GetComponentsInChildren<MeshRenderer>())
                {
                    if (mr.GetComponent<TextMesh>() != null) continue;
                    var b = mr.bounds.size;
                    if (Mathf.Max(b.x, Mathf.Max(b.y, b.z)) < 0.6f) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    list.Add(mr.gameObject);
                }
            }
            if (list.Count > 0) StaticBatchingUtility.Combine(list.ToArray(), new GameObject("Static Batch Root"));
        }

        public static Vector3 RandomPoint()
        {
            // now and then somewhere up on a platform (they take the stairs to get there)
            if (Level.Stairs.Count > 0 && Random.value < 0.2f)
            {
                var s = Level.Stairs[Random.Range(0, Level.Stairs.Count)];
                var q = new Vector3(Random.Range(s.plat.xMin + 1f, s.plat.xMax - 1f), s.h, Random.Range(s.plat.yMin + 1.5f, s.plat.yMax - 1f));
                if (!Physics.CheckSphere(q + Vector3.up * 1f, 0.6f, ~0, QueryTriggerInteraction.Ignore)) return q;
            }
            var A = Level.SpawnArea;
            for (int i = 0; i < 40; i++)
            {
                var p = new Vector3(Random.Range(A.xMin, A.xMax), 0, Random.Range(A.yMin, A.yMax));
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
            // a shot is heard far (louder guns further) and a bullet cracking past is terrifying
            float range = Player.AWP ? 140f : Player.AK ? 90f : 60f;
            foreach (var m in Mannequin.All)
            {
                if (m.dead) continue;
                Vector3 c = m.transform.position + Vector3.up * 1.2f, seg = end - from;
                float t = Mathf.Clamp01(Vector3.Dot(c - from, seg) / Mathf.Max(1e-4f, seg.sqrMagnitude));
                float miss = (from + seg * t - c).magnitude;
                if (miss < 2f) m.Scare(from, 0.9f, "BULLET WHIZZED PAST");
                else if (Brains) { float d = (m.transform.position - from).magnitude; if (d < range) m.Scare(from, Mathf.Lerp(0.75f, 0.2f, d / range), "HEARD A SHOT"); }
            }
        }

        bool menuPause;
        public void SetMenuPause(bool on) { menuPause = on; ApplyTime(); }

        void ApplyTime()
        {
            Time.timeScale = (paused || menuPause) ? 0f : TS[tsi];
            Time.fixedDeltaTime = (1f / 75f) * Mathf.Max(0.25f, TS[tsi]);   // 75 Hz physics: stable ragdolls, a sixth cheaper than 90
        }

        float fpsSmooth = 60f, msSmooth = 16f, msWorst, msWorstAcc, worstT;
        void Update()
        {
            float fdt = Time.unscaledDeltaTime;
            if (fdt > 0f)
            {
                msSmooth = Mathf.Lerp(msSmooth, fdt * 1000f, 0.08f); fpsSmooth = 1000f / Mathf.Max(0.01f, msSmooth);
                msWorstAcc = Mathf.Max(msWorstAcc, fdt * 1000f); worstT += fdt;
                if (worstT >= 1f) { msWorst = msWorstAcc; msWorstAcc = 0f; worstT = 0f; }
            }
            if (GI.Down(K.N)) Spawn();
            if (GI.Down(K.Y)) SpawnAtCrosshair();
            if (GI.Down(K.T)) XRay.Toggle();
            if (GI.Down(K.Tab)) showMonitor = !showMonitor;
            if (GI.Down(K.Back)) { ResetAll(); return; }
            if (GI.Down(K.LBracket)) { tsi = Mathf.Max(0, tsi - 1); ApplyTime(); }
            if (GI.Down(K.RBracket)) { tsi = Mathf.Min(TS.Length - 1, tsi + 1); ApplyTime(); }
            if (GI.Down(K.P)) { paused = !paused; ApplyTime(); }
        }

        // ---------------- HUD ----------------
        bool showMonitor = true;   // TAB
        public const string Discord = "elliotalderson1338_30647";
        static readonly Color Yel = new Color(1f, 0.8f, 0.05f);
        Texture2D dummyTex, stripeTex; GUIStyle dst;
        void MakeDummyTex()
        {
            // the crash test target: circle split in four, black and yellow
            const int S = 64; dummyTex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear };
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                float dx = x - S / 2f + 0.5f, dy = y - S / 2f + 0.5f, rr = Mathf.Sqrt(dx * dx + dy * dy);
                Color c = rr > 31f ? Color.clear : rr > 28f ? Color.black : ((dx >= 0) == (dy >= 0) ? Color.black : Yel);
                dummyTex.SetPixel(x, y, c);
            }
            dummyTex.Apply();
            stripeTex = new Texture2D(16, 16) { wrapMode = TextureWrapMode.Repeat };
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) stripeTex.SetPixel(x, y, ((x + y) % 16) < 8 ? Color.black : Yel);
            stripeTex.Apply();
        }
        void Dark(Rect r, string text, int size, float s, FontStyle fs, float alpha)
        {
            if (dst == null) dst = new GUIStyle(GUI.skin.label) { richText = true };
            dst.fontSize = (int)(size * s); dst.fontStyle = fs; dst.alignment = TextAnchor.UpperLeft;
            dst.normal.textColor = new Color(0.05f, 0.05f, 0.05f, alpha);
            GUI.Label(r, text, dst);
        }

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

            Label(new Rect(0, 14 * s, W, 30 * s), $"SPECIMENS  {alive:00} / 100   <size={(int)(14 * s)}>· 75 HZ</size>", (int)(20 * s), TextAnchor.UpperCenter);
            // performance: smoothed fps and frame time, worst frame of the last second
            string fc = fpsSmooth >= 144f ? "#5cd08f" : fpsSmooth >= 60f ? "#f0c040" : "#ff5a50";
            Label(new Rect(0, 42 * s, W, 22 * s), $"<color={fc}>{fpsSmooth:0} FPS</color>  ·  {msSmooth:0.0} MS  ·  WORST {msWorst:0.0} MS", (int)(13 * s), TextAnchor.UpperCenter, 0.85f);
            Label(new Rect(W - 330 * s, 14 * s, 300 * s, 34 * s), paused ? "TIME  PAUSED" : $"TIME  {TS[tsi]:0.0}x", (int)(26 * s), TextAnchor.UpperRight);
            Label(new Rect(W - 330 * s, 48 * s, 300 * s, 22 * s), XRay.On ? "X-RAY  ·  FREE MOVEMENT" : "FREE MOVEMENT", (int)(14 * s), TextAnchor.UpperRight, 0.7f);

            Label(new Rect(W - 330 * s, H - 150 * s, 300 * s, 22 * s), Player.Names[Player.Weapon], (int)(14 * s), TextAnchor.UpperRight, 0.7f);
            string am = Player.Knife ? "—" : player.reloadT > 0 ? "..." : player.ammo.ToString("00");
            Label(new Rect(W - 330 * s, H - 128 * s, 300 * s, 70 * s), $"{am}<size={(int)(24 * s)}> / {player.MaxAmmo}</size>", (int)(56 * s), TextAnchor.UpperRight);
            Label(new Rect(W - 330 * s, H - 58 * s, 300 * s, 22 * s), "∞  RESERVE", (int)(14 * s), TextAnchor.UpperRight, 0.6f);
            Label(new Rect(20 * s, H - 36 * s, W, 24 * s), "WASD move · SHIFT run · SPACE jump · LMB fire · RMB aim · MMB hold: grab & drag · R reload · T x-ray · Y spawn Carl · [ ] time · P pause · BACKSPACE reset · ESC mouse", (int)(13 * s), TextAnchor.UpperLeft, 0.55f);

            // crosshair (the AWP scope has its own)
            float cx = W / 2f, cy = H / 2f;
            // with the iron sights up (or the scope) the sights are the crosshair; the hit marker still shows
            {
            var cc = player.looked != null ? new Color(1f, 0.35f, 0.3f, 0.95f) : new Color(1, 1, 1, 0.9f);
            GUI.color = cc; float g = 5 * s, l = 8 * s, t = Mathf.Max(1, 2 * s);
            if (!player.Scoped && !player.Ads)
            {
                GUI.DrawTexture(new Rect(cx - g - l, cy - t / 2, l, t), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(cx + g, cy - t / 2, l, t), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx - t / 2, cy - g - l, t, l), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(cx - t / 2, cy + g, t, l), Texture2D.whiteTexture);
            }
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
                var box = new Rect(W / 2f - 190 * s, H / 2f - 410 * s, 380 * s, 840 * s);
                GUI.DrawTexture(box, panelTex);
                Label(new Rect(box.x, box.y + 14 * s, box.width, 30 * s), "VERTICAL IMPACT TESTSITE", (int)(20 * s), TextAnchor.UpperCenter);
                var bs = new GUIStyle(GUI.skin.button) { fontSize = (int)(18 * s), fontStyle = FontStyle.Bold };
                float bx = box.x + 30 * s, bw = box.width - 60 * s, bh = 42 * s, y0 = box.y + 60 * s;
                if (GUI.Button(new Rect(bx, y0, bw, bh), (Player.Weapon == 0 ? "▶ " : "  ") + "PISTOL  (9 mm, semi-auto)", bs)) player.SetWeapon(0);
                if (GUI.Button(new Rect(bx, y0 + 52 * s, bw, bh), (Player.Weapon == 1 ? "▶ " : "  ") + "AK-47  (7.62, full auto)", bs)) player.SetWeapon(1);
                if (GUI.Button(new Rect(bx, y0 + 104 * s, bw, bh), (Player.Weapon == 2 ? "▶ " : "  ") + "AWP  (.338, bolt, scope)", bs)) player.SetWeapon(2);
                if (GUI.Button(new Rect(bx, y0 + 156 * s, bw, bh), (Player.Weapon == 3 ? "▶ " : "  ") + "KNIFE  (slash & cut)", bs)) player.SetWeapon(3);
                if (GUI.Button(new Rect(bx, y0 + 260 * s, bw, bh), "MODEL: " + (BodyMesh.Model == 1 ? "REALISTIC 1.85 M" : "CLASSIC DUMMY") + "  (restarts)", bs))
                {
                    BodyMesh.Model = 1 - BodyMesh.Model; PlayerPrefs.SetInt("vits_model", BodyMesh.Model); PlayerPrefs.Save();
                    player.SetMenu(false); ResetAll(); return;
                }
                if (GUI.Button(new Rect(bx, y0 + 208 * s, bw, bh), "KEKKO BRAINS: " + (Brains ? "ON (walk around)" : "OFF (wait until shot)"), bs)) Brains = !Brains;
                // settings: mouse sensitivity and aim (ADS) sensitivity multiplier
                float sy = y0 + 318 * s;
                Label(new Rect(bx, sy, bw, 22 * s), $"SENSITIVITY  {Player.Sens:0.00}", (int)(15 * s), TextAnchor.UpperLeft);
                float ns = GUI.HorizontalSlider(new Rect(bx, sy + 24 * s, bw, 20 * s), Player.Sens, 0.1f, 4f);
                Label(new Rect(bx, sy + 50 * s, bw, 22 * s), $"ADS SENSITIVITY  {Player.AdsSens:0.00}x", (int)(15 * s), TextAnchor.UpperLeft);
                float na = GUI.HorizontalSlider(new Rect(bx, sy + 74 * s, bw, 20 * s), Player.AdsSens, 0.1f, 2f);
                Label(new Rect(bx, sy + 100 * s, bw, 22 * s), $"VOLUME  {Player.Volume * 100f:0}%", (int)(15 * s), TextAnchor.UpperLeft);
                float nv = GUI.HorizontalSlider(new Rect(bx, sy + 124 * s, bw, 20 * s), Player.Volume, 0f, 1f);
                if (ns != Player.Sens || na != Player.AdsSens || nv != Player.Volume)
                {
                    Player.Sens = Mathf.Round(ns * 20f) / 20f; Player.AdsSens = Mathf.Round(na * 20f) / 20f; Player.Volume = Mathf.Round(nv * 100f) / 100f;
                    Player.SaveSens();
                }
                if (GUI.Button(new Rect(bx, sy + 160 * s, bw, bh), "RESUME  (Esc)", bs)) player.SetMenu(false);
                if (GUI.Button(new Rect(bx, sy + 210 * s, bw * 0.48f, bh), "RESET", bs)) { player.SetMenu(false); ResetAll(); }
                if (GUI.Button(new Rect(bx + bw * 0.52f, sy + 210 * s, bw * 0.48f, bh), "MAP: " + Level.MapNames[Mathf.Clamp(PlayerPrefs.GetInt("vits_map", 0), 0, 2)], bs))
                {
                    PlayerPrefs.SetInt("vits_map", (PlayerPrefs.GetInt("vits_map", 0) + 1) % Level.MapNames.Length); PlayerPrefs.Save();
                    player.SetMenu(false); ResetAll(); return;
                }
                // video
                string fpsTxt = FpsSteps[FpsIdx] == 0 ? "UNLIMITED" : FpsSteps[FpsIdx] + " FPS";
                if (GUI.Button(new Rect(bx, sy + 290 * s, bw * 0.48f, bh), "LIMIT: " + fpsTxt, bs)) { FpsIdx = (FpsIdx + 1) % FpsSteps.Length; PlayerPrefs.SetInt("vits_fps", FpsIdx); PlayerPrefs.Save(); ApplyVideo(); }
                Label(new Rect(bx + bw * 0.52f, sy + 286 * s, bw * 0.48f, 22 * s), $"MOTION BLUR  {Blur * 100f:0}%", (int)(14 * s), TextAnchor.UpperLeft);
                float nb = GUI.HorizontalSlider(new Rect(bx + bw * 0.52f, sy + 312 * s, bw * 0.48f, 20 * s), Blur, 0f, 1f);
                if (Mathf.Abs(nb - Blur) > 0.009f) { Blur = Mathf.Round(nb * 20f) / 20f; PlayerPrefs.SetFloat("vits_blur", Blur); PlayerPrefs.Save(); SetMotionBlur(Blur); }
                Label(new Rect(bx, sy + 340 * s, bw, 20 * s), $"{1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime):0} FPS NOW", (int)(13 * s), TextAnchor.UpperCenter, 0.7f);
                Label(new Rect(bx, sy + 362 * s, bw, 22 * s), Discord + " on Discord!", (int)(15 * s), TextAnchor.UpperCenter, 0.9f);
                return;
            }
            if (!player.locked) Label(new Rect(0, H * 0.55f, W, 30 * s), "CLICK TO PLAY", (int)(22 * s), TextAnchor.UpperCenter);

            // crash test dummy monitor: yellow/black, hazard stripes, the quartered target mark
            var d = player.looked;
            if (showMonitor)
            {
                if (dummyTex == null) MakeDummyTex();
                float w = 400 * s, hh = (d == null ? 150 : 236 + d.injuries.Count * 19) * s;
                var r = new Rect(16 * s, 16 * s, w, hh);
                GUI.color = Color.black; GUI.DrawTexture(new Rect(r.x - 3 * s, r.y - 3 * s, r.width + 6 * s, r.height + 6 * s), Texture2D.whiteTexture);
                GUI.color = Yel; GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.DrawTextureWithTexCoords(new Rect(r.x, r.y, r.width, 12 * s), stripeTex, new Rect(0, 0, r.width / (12 * s), 1));
                GUI.DrawTextureWithTexCoords(new Rect(r.x, r.yMax - 12 * s, r.width, 12 * s), stripeTex, new Rect(0, 0, r.width / (12 * s), 1));
                GUI.DrawTexture(new Rect(r.xMax - 70 * s, r.y + 20 * s, 56 * s, 56 * s), dummyTex);
                float y = r.y + 18 * s, x = r.x + 14 * s;
                Dark(new Rect(x, y, w, 20 * s), "CRASH TEST SPECIMEN  ·  VITAL MONITOR", 11, s, FontStyle.Bold, 0.75f); y += 16 * s;
                if (d == null)
                {
                    int dots = 1 + (int)(Time.unscaledTime * 2f) % 3;
                    Dark(new Rect(x, y, w, 40 * s), "WAITING" + new string('.', dots), 30, s, FontStyle.Bold, 1f); y += 38 * s;
                    Dark(new Rect(x, y, w, 22 * s), "NO SPECIMEN IN SIGHT  ·  AIM AT A KEKKO", 13, s, FontStyle.Bold, 0.7f); y += 20 * s;
                    Dark(new Rect(x, y, w, 22 * s), "TAB  hide / show", 11, s, FontStyle.Normal, 0.55f);
                }
                else {
                Dark(new Rect(x, y, w, 40 * s), d.displayName, 30, s, FontStyle.Bold, 1f); y += 38 * s;
                string life = d.dead ? "<color=#b00010>■ DEAD</color>" : d.conscious ? "<color=#0a6a2a>■ ALIVE · CONSCIOUS</color>" : "<color=#9a5200>■ ALIVE · FAINTED</color>";
                Dark(new Rect(x, y, w, 24 * s), life, 15, s, FontStyle.Bold, 1f); y += 24 * s;
                Dark(new Rect(x, y, w, 22 * s), "STATUS   " + d.status, 13, s, FontStyle.Bold, 0.9f); y += 20 * s;
                Dark(new Rect(x, y, w, 22 * s), $"LEGS  L {d.legFn[0] * 100f:0}%  R {d.legFn[1] * 100f:0}%    EFFORT {d.support * 100f:0}%    PAIN {d.pain * 100f:0}%", 12, s, FontStyle.Normal, 0.85f); y += 20 * s;
                Dark(new Rect(x, y, w, 22 * s), $"BLOOD  {Mathf.Max(0, d.blood) / 1000f:0.00} / 5.00 L", 13, s, FontStyle.Bold, 0.9f); y += 20 * s;
                GUI.color = Color.black; GUI.DrawTexture(new Rect(x, y, w - 28 * s, 10 * s), Texture2D.whiteTexture);
                GUI.color = new Color(0.75f, 0.05f, 0.08f); GUI.DrawTexture(new Rect(x + 2 * s, y + 2 * s, (w - 32 * s) * Mathf.Clamp01(d.blood / Mannequin.BloodMax), 6 * s), Texture2D.whiteTexture);
                GUI.color = Color.white; y += 16 * s;
                Dark(new Rect(x, y, w, 22 * s), "IMPACT   " + d.lastHit + (d.lastHitTime > 0 ? $"  ·  T+{Time.time - d.lastHitTime:0} S" : ""), 13, s, FontStyle.Bold, 0.9f); y += 22 * s;
                Dark(new Rect(x, y, w, 20 * s), "DAMAGE LOG", 11, s, FontStyle.Bold, 0.6f); y += 17 * s;
                foreach (var inj in d.injuries) { Dark(new Rect(x, y, w, 20 * s), "▸ " + inj, 12, s, FontStyle.Normal, 0.9f); y += 19 * s; }
                }
            }
            // watermark
            Label(new Rect(W - 430 * s, 72 * s, 400 * s, 20 * s), Discord + " on Discord!", (int)(13 * s), TextAnchor.UpperRight, 0.55f);
        }
    }
}
