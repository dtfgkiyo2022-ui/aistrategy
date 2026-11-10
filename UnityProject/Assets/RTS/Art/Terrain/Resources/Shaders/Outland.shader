Shader "Rts/Outland"
{
    // 11-10 (3rd round): the ground beyond the edge of the map. It used to be the flat camera colour, a dark void
    // with a straight edge. This draws it like unexplored land under the fog of war (the same dark blue-grey over
    // the grass tile), with darker woods here and there, so the map seems to go on. Display only; nothing walks there.
    Properties
    {
        _GrassTex ("Grass", 2D) = "white" {}
        _TileMeters ("Tile size in metres", Float) = 8
        _MapMeters ("Map size in metres", Vector) = (512, 256, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "RtsCloudShadow.hlsl"
            TEXTURE2D(_GrassTex); SAMPLER(sampler_GrassTex);
            float _TileMeters; float4 _MapMeters;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionHCS : SV_POSITION; float3 world : TEXCOORD0; };

            float OutHash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float OutNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(OutHash(i), OutHash(i + float2(1, 0)), u.x),
                    lerp(OutHash(i + float2(0, 1)), OutHash(i + float2(1, 1)), u.x), u.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(output.world);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.world.xz;
                half3 grass = SAMPLE_TEXTURE2D(_GrassTex, sampler_GrassTex, p / _TileMeters).rgb;
                grass = lerp(grass, SAMPLE_TEXTURE2D(_GrassTex, sampler_GrassTex, p / (_TileMeters * 2.4) + 0.37).rgb, 0.5h);
                // Woods: darker clumps 20-40 m across, and lighter and darker ground at about 25 m.
                half wood = smoothstep(0.52h, 0.78h, OutNoise(p * 0.03) * 0.5h + OutNoise(p * 0.07 + 3.1) * 0.3h + OutNoise(p * 0.19 + 7.7) * 0.2h);
                half3 ground = grass * lerp(0.80h, 1.10h, OutNoise(p * 0.04));
                ground = lerp(ground, half3(0.10, 0.16, 0.08), wood * 0.75h);
                ground *= lerp(0.85h, 1.0h, RtsCloudLight(input.world));
                // How far outside the map (metres): just past the edge a little darker, so the edge reads as a border.
                float2 outside = max(max(-p, p - _MapMeters.xy), 0.0);
                float distance = length(outside);
                half veil = lerp(0.90h, 0.94h, smoothstep(0.0h, 60.0h, distance));
                veil = max(veil, 0.95h - smoothstep(0.0h, 6.0h, distance) * 0.05h);
                return half4(lerp(ground, half3(0.07, 0.09, 0.14), veil), 1.0h);
            }
            ENDHLSL
        }
    }
}
