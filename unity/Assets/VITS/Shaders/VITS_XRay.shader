// X-ray skin: transparent, glowing at the silhouette, so bones, organs and vessels are visible inside.
Shader "VITS/XRay"
{
    Properties { _Color ("Color", Color) = (0.55, 0.8, 1, 1) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        ZWrite Off
        Cull Back
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; };
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.v = WorldSpaceViewDir(v.vertex);
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float f = 1 - abs(dot(normalize(i.n), normalize(i.v)));
                return fixed4(_Color.rgb, 0.04 + 0.6 * f * f);
            }
            ENDCG
        }
    }
}
