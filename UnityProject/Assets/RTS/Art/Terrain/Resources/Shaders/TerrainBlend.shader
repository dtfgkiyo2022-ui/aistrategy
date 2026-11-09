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
        _WearTex ("Worn ground (GroundWear)", 2D) = "black" {}
        _ReliefTexel ("Relief sample step (1.5 / tile pixels)", Float) = 0.0029
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        float4 _SplatTexel;
        float4 _MapMeters;
        float _TextureScale;
        // Set by GraphicsQuality for High and Ultra (11-09); 0 on Low and Medium keeps the old look exactly.
        // _RtsLook: 1 = relief from the painted tile and sky/ground ambient. _RtsBump: relief strength.
        // _RtsSky / _RtsGround: ambient from above and from below.
        float _RtsLook; float _RtsBump; float4 _RtsSky; float4 _RtsGround;
        // Medium and above: packed earth round buildings and walked paths (GroundWear, 11-09).
        float _RtsWear; float _ReliefTexel;

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
            #include "RtsCloudShadow.hlsl"
            TEXTURE2D(_GrassTex); SAMPLER(sampler_GrassTex);
            TEXTURE2D(_SoilTex); SAMPLER(sampler_SoilTex);
            TEXTURE2D(_SandTex); SAMPLER(sampler_SandTex);
            TEXTURE2D(_RockTex); SAMPLER(sampler_RockTex);
            TEXTURE2D(_ForestTex); SAMPLER(sampler_ForestTex);
            TEXTURE2D(_SplatA); SAMPLER(sampler_SplatA);
            TEXTURE2D(_SplatB); SAMPLER(sampler_SplatB);
            TEXTURE2D(_WearTex); SAMPLER(sampler_WearTex);
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

                // From the usual camera (150 m and more) one painted tile averages to a single green, so the
                // variation that reads from there is added here, in metres of the world:
                // bare soil showing through the grass (patches of about 10-20 m) ...
                half bare = smoothstep(0.66, 0.86, TerrainNoise(world * 0.055) * 0.7 + TerrainNoise(world * 0.31) * 0.3);
                half grassShare = weights.r / sum;
                half3 soil = SampleTile(TEXTURE2D_ARGS(_SoilTex, sampler_SoilTex), tile * 1.7, mixAmount);
                color = lerp(color, soil * half3(0.86, 0.80, 0.74), bare * grassShare * 0.65);
                // Worn ground: packed earth round buildings and along the paths the villagers keep walking.
                if (_RtsWear > 0.5)
                {
                    half wear = SAMPLE_TEXTURE2D(_WearTex, sampler_WearTex, input.uv).r;
                    half ragged = (TerrainNoise(world * 0.9) - 0.5) * 0.22 + (TerrainNoise(world * 3.1) - 0.5) * 0.10;
                    half worn = smoothstep(0.20, 0.50, wear + ragged);
                    half3 earth = SampleTile(TEXTURE2D_ARGS(_SoilTex, sampler_SoilTex), tile * 1.35, mixAmount) * half3(0.95, 0.86, 0.74);
                    color = lerp(color, earth, worn * (1.0h - weights.b / sum * 0.6h));
                }
                // ... lush and dry meadows (about 60-90 m; mostly lush, the warm evening sun already yellows the grass) ...
                half dry = smoothstep(0.58, 0.95, TerrainNoise(world * 0.013 + 3.7));
                color *= lerp(half3(0.80, 1.00, 0.84), half3(1.06, 1.02, 0.80), dry * (0.4 + 0.6 * grassShare));
                // ... and lighter and darker ground at two sizes (about 25 m and 4 m clumps).
                half macro = TerrainNoise(world * 0.04) * 0.6 + TerrainNoise(world * 0.26) * 0.4;
                color *= lerp(0.80h, 1.14h, macro);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light sun = GetMainLight(shadowCoord);
                half3 normal = half3(0, 1, 0);
                if (_RtsLook > 0.5)
                {
                    // Relief: the painted tile's brightness read as height, so blades, clods and pebbles catch the sun.
                    half3 luma = half3(0.299h, 0.587h, 0.114h);
                    float e = _ReliefTexel;
                    half3 c0 = SAMPLE_TEXTURE2D(_GrassTex, sampler_GrassTex, tile).rgb * (weights.r + forestWeight)
                        + SAMPLE_TEXTURE2D(_SoilTex, sampler_SoilTex, tile).rgb * (weights.g + weights.b + weights.a);
                    half3 cx = SAMPLE_TEXTURE2D(_GrassTex, sampler_GrassTex, tile + float2(e, 0)).rgb * (weights.r + forestWeight)
                        + SAMPLE_TEXTURE2D(_SoilTex, sampler_SoilTex, tile + float2(e, 0)).rgb * (weights.g + weights.b + weights.a);
                    half3 cz = SAMPLE_TEXTURE2D(_GrassTex, sampler_GrassTex, tile + float2(0, e)).rgb * (weights.r + forestWeight)
                        + SAMPLE_TEXTURE2D(_SoilTex, sampler_SoilTex, tile + float2(0, e)).rgb * (weights.g + weights.b + weights.a);
                    half h0 = dot(c0, luma) / sum, hx = dot(cx, luma) / sum, hz = dot(cz, luma) / sum;
                    normal = normalize(half3((h0 - hx) * _RtsBump, 1.0h, (h0 - hz) * _RtsBump));
                }
                half daylight = _RtsLook > 0.5 ? saturate(dot(normal, sun.direction)) : saturate(dot(half3(0, 1, 0), sun.direction));
                half shadow = lerp(1.0h, sun.shadowAttenuation, 0.82h) * RtsCloudLight(input.positionWS);
                half directAo = 1.0h, indirectAo = 1.0h;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(input.positionHCS));
                    directAo = ao.directAmbientOcclusion;
                    indirectAo = ao.indirectAmbientOcclusion;
                #endif
                half3 ambient = half3(0.58, 0.62, 0.72) * indirectAo;
                if (_RtsLook > 0.5) ambient = lerp(_RtsGround.rgb, _RtsSky.rgb, normal.y * 0.5h + 0.5h) * indirectAo;
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
