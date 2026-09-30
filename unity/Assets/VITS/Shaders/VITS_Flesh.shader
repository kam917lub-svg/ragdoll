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
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; };
            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.v = WorldSpaceViewDir(v.vertex);
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float k = 0.45 + 0.55 * abs(dot(normalize(i.n), normalize(i.v)));
                return fixed4(_Color.rgb * k, 1);
            }
            ENDCG
        }
    }
}
