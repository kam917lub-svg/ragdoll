using UnityEngine;
using UnityEngine.SceneManagement;

namespace VITS
{
    // Entry point: builds the whole test site in any scene you press Play in.
    public class Game : MonoBehaviour
    {
        public static Game I;
        public const float BulletImpulse = 5f; // N·s pushed into a ragdoll part by a 9 mm round (gamey, not realistic)
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

            Physics.defaultSolverIterations = 12; Physics.defaultSolverVelocityIterations = 4;
            Physics.gravity = new Vector3(0, -9.81f, 0);
            ApplyTime();

            Level.Build();
            new GameObject("Blood").AddComponent<Blood>();
            var pgo = new GameObject("Player");
            pgo.transform.SetPositionAndRotation(new Vector3(0, 0.05f, -14f), Quaternion.identity);
            player = pgo.AddComponent<Player>();
            for (int i = 0; i < 10; i++) Spawn();
        }

        public void Spawn()
        {
            var go = new GameObject("Mannequin");
            go.transform.SetPositionAndRotation(RandomPoint(), Quaternion.Euler(0, Random.Range(0, 360f), 0));
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
        public static Vector3 CoverPoint(Vector3 from)
        {
            Vector3 shooter = I != null && I.player != null ? I.player.transform.position : Vector3.zero;
            Vector3 best = RandomPoint(); float bd = 1e9f;
            foreach (var c in Level.Covers)
            {
                Vector3 away = c - shooter; away.y = 0;
                if (away.sqrMagnitude < 1e-4f) away = Vector3.forward;
                Vector3 hide = c + away.normalized * 0.9f; hide.y = 0;
                float d = (hide - from).magnitude + (hide - shooter).magnitude * 0.1f;
                if (d < bd) { bd = d; best = hide; }
            }
            return best;
        }

        public void Gunshot(Vector3 from)
        {
            foreach (var m in Mannequin.All)
                if (!m.dead && !m.ragdoll && (m.transform.position - from).magnitude < 15f && Random.value < 0.5f) m.Flee();
        }

        void ApplyTime()
        {
            Time.timeScale = paused ? 0f : TS[tsi];
            Time.fixedDeltaTime = (1f / 90f) * Mathf.Max(0.25f, TS[tsi]);
        }

        void Update()
        {
            if (GI.Down(K.N)) Spawn();
            if (GI.Down(K.T)) { SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); return; }
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
            Label(new Rect(W - 330 * s, 48 * s, 300 * s, 22 * s), "FREE MOVEMENT", (int)(14 * s), TextAnchor.UpperRight, 0.7f);

            Label(new Rect(W - 330 * s, H - 150 * s, 300 * s, 22 * s), "PISTOL / 9MM", (int)(14 * s), TextAnchor.UpperRight, 0.7f);
            string am = player.reloadT > 0 ? "..." : player.ammo.ToString("00");
            Label(new Rect(W - 330 * s, H - 128 * s, 300 * s, 70 * s), $"{am}<size={(int)(24 * s)}> / {Player.MaxAmmo}</size>", (int)(56 * s), TextAnchor.UpperRight);
            Label(new Rect(W - 330 * s, H - 58 * s, 300 * s, 22 * s), "∞  RESERVE", (int)(14 * s), TextAnchor.UpperRight, 0.6f);
            Label(new Rect(20 * s, H - 36 * s, W, 24 * s), "WASD move · SHIFT run · SPACE jump · LMB fire · RMB aim · R reload · [ ] time · P pause · N spawn · T reset · ESC mouse", (int)(13 * s), TextAnchor.UpperLeft, 0.55f);

            // crosshair
            var cc = player.looked != null ? new Color(1f, 0.35f, 0.3f, 0.95f) : new Color(1, 1, 1, 0.9f);
            GUI.color = cc; float cx = W / 2f, cy = H / 2f, g = 5 * s, l = 8 * s, t = Mathf.Max(1, 2 * s);
            GUI.DrawTexture(new Rect(cx - g - l, cy - t / 2, l, t), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(cx + g, cy - t / 2, l, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - t / 2, cy - g - l, t, l), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(cx - t / 2, cy + g, t, l), Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (!player.locked) Label(new Rect(0, H * 0.55f, W, 30 * s), "CLICK TO PLAY", (int)(22 * s), TextAnchor.UpperCenter);

            // medical monitor
            var d = player.looked;
            if (d != null)
            {
                var r = new Rect(16 * s, 16 * s, 380 * s, (190 + d.injuries.Count * 20) * s);
                GUI.DrawTexture(r, panelTex);
                GUI.DrawTexture(new Rect(r.x, r.y, 4 * s, r.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, new Color(1f, 0.45f, 0.18f), 0, 0);
                float y = r.y + 10 * s; float x = r.x + 16 * s;
                Label(new Rect(x, y, 360 * s, 30 * s), "VITAL MONITOR", (int)(12 * s), TextAnchor.UpperLeft, 0.5f); y += 18 * s;
                Label(new Rect(x, y, 360 * s, 34 * s), d.displayName, (int)(24 * s), TextAnchor.UpperLeft); y += 32 * s;
                string life = d.dead ? "<color=#ff5a50>DEAD</color>" : d.conscious ? "<color=#5cd08f>ALIVE · CONSCIOUS</color>" : "<color=#f0b040>ALIVE · FAINTED</color>";
                Label(new Rect(x, y, 360 * s, 24 * s), life, (int)(14 * s), TextAnchor.UpperLeft); y += 24 * s;
                Label(new Rect(x, y, 360 * s, 24 * s), "STATUS  " + d.status, (int)(13 * s), TextAnchor.UpperLeft, 0.8f); y += 22 * s;
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
