// Blood pool: opaque (you never see the floor through it), very dark where it is thick, a little lighter at the thin
// rim, with a wet glossy highlight and a faint reflection of the hall at grazing angles.
Shader "VITS/Pool"
{
    Properties
    {
        _MainTex ("Shape", 2D) = "white" {}
        _Color ("Color", Color) = (0.22, 0.004, 0.012, 1)
    }
    SubShader
    {
        Tags { "Queue"="Geometry+20" "RenderType"="Opaque" }
        ZWrite On
        Cull Off
        Offset -2, -2
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 _Color;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 n : TEXCOORD1; float3 v : TEXCOORD2; };
            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.n = UnityObjectToWorldNormal(v.normal);
                o.v = WorldSpaceViewDir(v.vertex);
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                clip(t.a - 0.5);
                // thickness from the soft shape: the edge (alpha 0.5..0.9) is a thin film, lighter
                float thick = saturate((t.a - 0.5) * 2.5);
                float3 col = lerp(_Color.rgb * 2.2, _Color.rgb, thick);
                float3 n = normalize(i.n); if (n.y < 0) n = -n;
                float3 v = normalize(i.v);
                float3 l = normalize(float3(0.45, 0.8, -0.35));
                float3 h = normalize(l + v);
                float spec = pow(saturate(dot(n, h)), 90) * 0.9;
                float fres = pow(1 - saturate(dot(n, v)), 4) * 0.35;
                col = col * (0.75 + 0.25 * saturate(dot(n, l))) + spec + fres * float3(0.9, 0.88, 0.85);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
