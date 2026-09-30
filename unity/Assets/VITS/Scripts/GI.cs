using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VITS
{
    public enum K { W, A, S, D, Shift, Space, R, T, N, Esc, LBracket, RBracket, P, M, Back, Y, Ctrl }

    // Input wrapper: works with the new Input System (Unity 6 default) or the old Input Manager.
    public static class GI
    {
#if ENABLE_INPUT_SYSTEM
        static Key Map(K k)
        {
            switch (k)
            {
                case K.W: return Key.W; case K.A: return Key.A; case K.S: return Key.S; case K.D: return Key.D;
                case K.Shift: return Key.LeftShift; case K.Space: return Key.Space; case K.R: return Key.R;
                case K.T: return Key.T; case K.N: return Key.N; case K.Esc: return Key.Escape;
                case K.LBracket: return Key.LeftBracket; case K.RBracket: return Key.RightBracket;
                case K.P: return Key.P; case K.Back: return Key.Backspace; case K.Y: return Key.Y; case K.Ctrl: return Key.LeftCtrl; default: return Key.M;
            }
        }
        public static bool Held(K k) { var kb = Keyboard.current; return kb != null && kb[Map(k)].isPressed; }
        public static bool Down(K k) { var kb = Keyboard.current; return kb != null && kb[Map(k)].wasPressedThisFrame; }
        public static Vector2 MouseDelta() { var m = Mouse.current; return m == null ? Vector2.zero : m.delta.ReadValue() * 0.1f; }
        public static bool FireDown() { var m = Mouse.current; return m != null && m.leftButton.wasPressedThisFrame; }
        public static bool FireHeld() { var m = Mouse.current; return m != null && m.leftButton.isPressed; }
        public static bool AimHeld() { var m = Mouse.current; return m != null && m.rightButton.isPressed; }
        public static bool GrabDown() { var m = Mouse.current; return m != null && m.middleButton.wasPressedThisFrame; }
        public static bool GrabHeld() { var m = Mouse.current; return m != null && m.middleButton.isPressed; }
        public static float Scroll() { var m = Mouse.current; return m == null ? 0f : m.scroll.ReadValue().y / 120f; }
#else
        static KeyCode Map(K k)
        {
            switch (k)
            {
                case K.W: return KeyCode.W; case K.A: return KeyCode.A; case K.S: return KeyCode.S; case K.D: return KeyCode.D;
                case K.Shift: return KeyCode.LeftShift; case K.Space: return KeyCode.Space; case K.R: return KeyCode.R;
                case K.T: return KeyCode.T; case K.N: return KeyCode.N; case K.Esc: return KeyCode.Escape;
                case K.LBracket: return KeyCode.LeftBracket; case K.RBracket: return KeyCode.RightBracket;
                case K.P: return KeyCode.P; case K.Back: return KeyCode.Backspace; case K.Y: return KeyCode.Y; case K.Ctrl: return KeyCode.LeftControl; default: return KeyCode.M;
            }
        }
        public static bool Held(K k) => Input.GetKey(Map(k));
        public static bool Down(K k) => Input.GetKeyDown(Map(k));
        public static Vector2 MouseDelta() => new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
        public static bool FireDown() => Input.GetMouseButtonDown(0);
        public static bool FireHeld() => Input.GetMouseButton(0);
        public static bool AimHeld() => Input.GetMouseButton(1);
        public static bool GrabDown() => Input.GetMouseButtonDown(2);
        public static bool GrabHeld() => Input.GetMouseButton(2);
        public static float Scroll() => Input.mouseScrollDelta.y;
#endif
    }

    public static class Mats
    {
        static Shader lit, dec, txt, flesh;
        static Shader Find(string a, string b) { var s = Shader.Find(a); return s != null ? s : Shader.Find(b); }
        public static Material Lit(Color c, float smooth = 0.25f)
        {
            if (lit == null) lit = Find("Universal Render Pipeline/Lit", "Standard");
            var m = new Material(lit) { color = c, enableInstancing = true };
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            return m;
        }
        // unlit transparent, tinted (blood decals / pools / holes / tracer)
        public static Material Decal(Texture2D t, Color c, int queue = 3000)
        {
            if (dec == null) dec = Find("VITS/Decal", "Sprites/Default");
            return new Material(dec) { mainTexture = t, color = c, enableInstancing = true, renderQueue = queue };
        }
        public static Material Flesh(Color c)
        {
            if (flesh == null) flesh = Find("VITS/Flesh", "Unlit/Color");
            return new Material(flesh) { color = c, enableInstancing = true };
        }
        public static Material Text(Font f)
        {
            if (txt == null) txt = Shader.Find("VITS/Text");
            if (txt == null) return f.material;
            return new Material(txt) { mainTexture = f.material.mainTexture, renderQueue = 3002 };
        }
        public static GameObject Vis(PrimitiveType type, Transform parent, Vector3 lp, Vector3 ls, Material m, bool shadow = true)
        {
            var g = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = lp; g.transform.localScale = ls;
            var r = g.GetComponent<Renderer>(); r.sharedMaterial = m;
            r.shadowCastingMode = shadow ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return g;
        }
    }
}
