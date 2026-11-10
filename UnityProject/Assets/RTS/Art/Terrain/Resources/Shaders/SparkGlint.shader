Shader "Rts/SparkGlint"
{
    // Lives under Resources so the player build keeps it (it is only reached through Shader.Find).
    // 11-11: a small glowing fleck on a camera-facing quad (uv 0..1), added to what is behind it: a white-hot core
    // and a coloured edge. _Color and _Fade come per fleck (MaterialPropertyBlock). Display only.
    Properties
    {
        _Color ("Colour", Color) = (1, 0.7, 0.3, 1)
        _Fade ("Fade", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+20" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend One One
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
                half r = length(input.uv - 0.5) * 2.0h;
                half glow = saturate(1.0h - r);
                glow *= glow;
                half core = smoothstep(0.55h, 1.0h, 1.0h - r);
                half3 colour = _Color.rgb * glow * 1.6h + core * half3(1.0, 0.95, 0.8);
                return half4(colour * _Fade, 0.0h);
            }
            ENDHLSL
        }
    }
}
