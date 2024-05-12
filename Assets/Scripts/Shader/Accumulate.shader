Shader "Custom/Accumulate"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _PrevFrame ("Previous Frame", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float frameWeight : TEXCOORD1; // Pass weight from vertex to fragment shader.
            };

            sampler2D _MainTex;
            sampler2D _PrevFrame;
            int _Frame;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.frameWeight = 1.0 / max(1, _Frame + 1); // Avoid division by zero and move calculation to vertex shader.
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float4 currentColor = tex2D(_MainTex, i.uv);
                float4 previousColor = tex2D(_PrevFrame, i.uv);

                // Using lerp for blending current and previous frame based on the weight.
                float4 accumulatedColor = lerp(previousColor, currentColor, i.frameWeight);
                
                return accumulatedColor;
            }
            ENDCG
        }
    }
}
