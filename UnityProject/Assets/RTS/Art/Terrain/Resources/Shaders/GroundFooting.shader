Shader "Rts/GroundFooting"
{
    // Lives under Resources so the player build keeps it (it is only reached through Shader.Find).
    // 11-10: a patch of trodden earth and a soft contact shadow under each building, so a building sits in the ground
    // instead of standing on the grass. Drawn on a flat quad (uv 0..1) a little larger than the footprint; the edge
    // is broken up by noise. Display only.
    Properties
    {
        _Earth ("Earth", Color) = (0.36, 0.29, 0.20, 0.75)
        _Shadow ("Contact shadow", Float) = 0.45
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-20" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Earth;
            float _Shadow;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; float2 world : TEXCOORD1; };

            float FootHash(float2 p)
            {
                p = frac(p * float2(171.3, 263.9));
                p += dot(p, p + 23.7);
                return frac(p.x * p.y);
            }

            float FootNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(FootHash(i), FootHash(i + float2(1, 0)), u.x),
                    lerp(FootHash(i + float2(0, 1)), FootHash(i + float2(1, 1)), u.x), u.y);
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
                // 0 in the middle, 1 at the quad's edge, as a rounded square.
                float2 d = abs(input.uv - 0.5) * 2.0;
                float r = pow(pow(d.x, 4.0) + pow(d.y, 4.0), 0.25);
                float ragged = (FootNoise(input.world * 0.8) - 0.5) * 0.22 + (FootNoise(input.world * 2.7) - 0.5) * 0.08;
                half earth = 1.0h - smoothstep(0.62h, 0.98h, r + ragged);
                // The contact shadow hugs the footprint (about 0.74 of the quad).
                half shadow = (1.0h - smoothstep(0.60h, 0.86h, r)) * _Shadow;
                half3 colour = _Earth.rgb * (1.0h - shadow * 0.8h) * lerp(0.85h, 1.1h, FootNoise(input.world * 1.6));
                half alpha = saturate(earth * _Earth.a + shadow * 0.5h);
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }
}
