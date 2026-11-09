Shader "Rts/FogOfWar"
{
    // The fog texture has one pixel per 2 m cell. Read plainly it shows the cell steps; this blurs it over about
    // a cell and wobbles the edge with noise so the seen area ends in a soft, uneven line. Display only.
    Properties
    {
        _MainTex ("Fog (alpha = darkness)", 2D) = "clear" {}
        _Texel ("Texel (1/cells)", Vector) = (0.004, 0.008, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _Texel;
            // High and Ultra (11-09): a wider, softer edge and a lighter blue-grey instead of near black.
            float _RtsLook;
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; float2 world : TEXCOORD1; };

            float FogHash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float FogNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(FogHash(i), FogHash(i + float2(1, 0)), u.x),
                    lerp(FogHash(i + float2(0, 1)), FogHash(i + float2(1, 1)), u.x), u.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(world);
                output.uv = input.uv;
                output.world = world.xz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 wobble = float2(FogNoise(input.world * 0.35), FogNoise(input.world * 0.35 + 11.1)) - 0.5;
                float2 uv = input.uv + wobble * _Texel.xy * 1.5;
                float2 r = _Texel.xy * (_RtsLook > 0.5 ? 2.2 : 0.9);
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a * 0.28;
                a += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(r.x, 0)).a * 0.13;
                a += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - float2(r.x, 0)).a * 0.13;
                a += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0, r.y)).a * 0.13;
                a += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - float2(0, r.y)).a * 0.13;
                a += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + r).a * 0.05;
                a += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - r).a * 0.05;
                a += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(r.x, -r.y)).a * 0.05;
                a += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-r.x, r.y)).a * 0.05;
                if (_RtsLook > 0.5) return half4(0.07, 0.09, 0.14, a * 0.86);
                return half4(0.02, 0.03, 0.06, a);
            }
            ENDHLSL
        }
    }
}
