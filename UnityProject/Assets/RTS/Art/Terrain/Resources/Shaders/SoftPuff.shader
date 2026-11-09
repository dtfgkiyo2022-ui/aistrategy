Shader "Rts/SoftPuff"
{
    // Lives under Resources so the player build keeps it (it is only reached through Shader.Find).
    // 11-10: a soft round puff of smoke or steam on a camera-facing quad (uv 0..1), lit a little by the sun side.
    // _Fade (per puff, MaterialPropertyBlock) fades it in and out as it rises. Display only.
    Properties
    {
        _Color ("Colour", Color) = (0.86, 0.87, 0.88, 0.6)
        _Fade ("Fade", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            half4 _Color;
            float _Fade;
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 d = input.uv - 0.5;
                float r = length(d) * 2.0;
                // A lumpy edge from a few bumps round the circle.
                float a = atan2(d.y, d.x);
                r *= 1.0 + 0.10 * sin(a * 3.0 + 1.3) + 0.06 * sin(a * 5.0 + 0.4);
                half body = saturate(1.0h - r);
                body = body * body * (3.0h - 2.0h * body);
                // Lighter on the upper side, as if lit from above.
                half shade = lerp(0.78h, 1.08h, saturate(input.uv.y * 1.2h));
                return half4(_Color.rgb * shade, body * _Color.a * _Fade);
            }
            ENDHLSL
        }
    }
}
