Shader "Rts/TerrainBlend"
{
    // Lives under Resources so the player build keeps it (it is only reached through Shader.Find).
    // The ground receives the sun's shadows and the screen-space occlusion, so the graphics settings show on it.
    Properties
    {
        _GrassTex ("Grass", 2D) = "white" {}
        _SoilTex ("Soil", 2D) = "white" {}
        _SandTex ("Sand", 2D) = "white" {}
        _RockTex ("Rock", 2D) = "white" {}
        _ForestTex ("Forest", 2D) = "white" {}
        _SplatA ("Splat Grass Soil Sand Rock", 2D) = "white" {}
        _SplatB ("Splat Forest", 2D) = "black" {}
        _TextureScale ("Tile Scale", Float) = 0.55
        _SplatTexel ("Splat texel (1/cells)", Vector) = (0.004, 0.008, 0, 0)
        _MapMeters ("Map size in metres", Vector) = (512, 256, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        float4 _SplatTexel;
        float4 _MapMeters;
        float _TextureScale;

        float TerrainHash(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float TerrainNoise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            float2 u = f * f * (3.0 - 2.0 * f);
            float a = TerrainHash(i), b = TerrainHash(i + float2(1, 0));
            float c = TerrainHash(i + float2(0, 1)), d = TerrainHash(i + float2(1, 1));
            return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_GrassTex); SAMPLER(sampler_GrassTex);
            TEXTURE2D(_SoilTex); SAMPLER(sampler_SoilTex);
            TEXTURE2D(_SandTex); SAMPLER(sampler_SandTex);
            TEXTURE2D(_RockTex); SAMPLER(sampler_RockTex);
            TEXTURE2D(_ForestTex); SAMPLER(sampler_ForestTex);
            TEXTURE2D(_SplatA); SAMPLER(sampler_SplatA);
            TEXTURE2D(_SplatB); SAMPLER(sampler_SplatB);
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                return output;
            }

            // Two samples of one tile at different sizes and offsets, mixed by slow noise, so the 8 m repeat
            // does not show as a grid from the usual camera height.
            half3 SampleTile(TEXTURE2D_PARAM(tex, samp), float2 tile, half mixAmount)
            {
                half3 near = SAMPLE_TEXTURE2D(tex, samp, tile).rgb;
                half3 far = SAMPLE_TEXTURE2D(tex, samp, tile * 0.413 + float2(0.37, 0.71)).rgb;
                return lerp(near, far, mixAmount);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 world = input.positionWS.xz;
                // Wobble the cell-sized blend map by about one cell so the edges between grass, forest and sand
                // follow a ragged line instead of the cell grid.
                float2 wobble = float2(TerrainNoise(world * 0.21), TerrainNoise(world * 0.21 + 17.3)) - 0.5;
                wobble += (float2(TerrainNoise(world * 0.73 + 5.1), TerrainNoise(world * 0.73 + 9.7)) - 0.5) * 0.45;
                float2 splatUv = input.uv + wobble * _SplatTexel.xy * 1.6;
                half4 weights = SAMPLE_TEXTURE2D(_SplatA, sampler_SplatA, splatUv);
                half forestWeight = SAMPLE_TEXTURE2D(_SplatB, sampler_SplatB, splatUv).r;
                // Sharpen the blend a little so each surface reads as its own patch.
                weights = pow(max(weights, 0.0001h), 1.6h);
                forestWeight = pow(max(forestWeight, 0.0001h), 1.6h);
                half sum = max(0.001h, weights.r + weights.g + weights.b + weights.a + forestWeight);

                float2 tile = input.uv * _TextureScale * 24.0;
                half mixAmount = saturate(TerrainNoise(world * 0.045) * 1.4 - 0.2);
                half3 color = SampleTile(TEXTURE2D_ARGS(_GrassTex, sampler_GrassTex), tile, mixAmount) * weights.r;
                color += SampleTile(TEXTURE2D_ARGS(_SoilTex, sampler_SoilTex), tile, mixAmount) * weights.g;
                color += SampleTile(TEXTURE2D_ARGS(_SandTex, sampler_SandTex), tile, mixAmount) * weights.b;
                color += SampleTile(TEXTURE2D_ARGS(_RockTex, sampler_RockTex), tile, mixAmount) * weights.a;
                color += SampleTile(TEXTURE2D_ARGS(_ForestTex, sampler_ForestTex), tile, mixAmount) * forestWeight;
                color /= sum;

                // Large, slow patches of lighter and darker, warmer and cooler ground (meadow variation).
                half macro = TerrainNoise(world * 0.018) * 0.65 + TerrainNoise(world * 0.061) * 0.35;
                color *= lerp(half3(0.80, 0.86, 0.80), half3(1.10, 1.06, 0.92), macro);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light sun = GetMainLight(shadowCoord);
                half daylight = saturate(dot(half3(0, 1, 0), sun.direction));
                half shadow = lerp(1.0h, sun.shadowAttenuation, 0.82h);
                half directAo = 1.0h, indirectAo = 1.0h;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(input.positionHCS));
                    directAo = ao.directAmbientOcclusion;
                    indirectAo = ao.indirectAmbientOcclusion;
                #endif
                half3 ambient = half3(0.58, 0.62, 0.72) * indirectAo;
                half3 lit = lerp(ambient, sun.color.rgb * directAo, daylight * shadow);
                return half4(color * lit, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            struct DepthAttributes { float4 positionOS : POSITION; };
            float4 DepthVert(DepthAttributes input) : SV_POSITION { return TransformObjectToHClip(input.positionOS.xyz); }
            half DepthFrag(float4 positionHCS : SV_POSITION) : SV_Target { return positionHCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag
            struct NormalsAttributes { float4 positionOS : POSITION; };
            float4 NormalsVert(NormalsAttributes input) : SV_POSITION { return TransformObjectToHClip(input.positionOS.xyz); }
            half4 NormalsFrag(float4 positionHCS : SV_POSITION) : SV_Target
            {
                // The ground is flat: its world-space normal (what URP's normals texture stores) is straight up.
                return half4(0.0, 1.0, 0.0, 0.0);
            }
            ENDHLSL
        }
    }
}
