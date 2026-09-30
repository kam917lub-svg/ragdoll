// Inside of the body: drawn only on back faces, so it shows through holes torn in the skin.
Shader "VITS/Flesh"
{
    Properties { _Color ("Color", Color) = (0.55, 0.05, 0.07, 1) }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Cull Front
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; float3 w : TEXCOORD2; };
            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.v = WorldSpaceViewDir(v.vertex);
                o.w = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                // solid raw meat, not a lit cave: no depth shading (that made the inside look hollow),
                // just muscle fibre and fat marbling from world position, wet and dark
                float3 p = i.w * 38.0;
                float fib = frac(sin(dot(floor(p), float3(12.9898, 78.233, 37.719))) * 43758.5453);
                float str = 0.5 + 0.5 * sin(p.y * 2.3 + sin(p.x * 1.7) * 2.0 + p.z * 0.7);
                float3 c = _Color.rgb * (0.72 + 0.22 * str + 0.08 * fib);
                c = lerp(c, float3(0.78, 0.62, 0.52), step(0.93, fib) * 0.35);   // flecks of fat
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
}
