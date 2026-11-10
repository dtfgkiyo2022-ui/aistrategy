Shader "Rts/TerrainWater"
{
    // 11-10: the vertex colour carries the depth tint and, in alpha, how near the shore a corner is (1 on the shore
    // line). The water now has moving ripples, the sky reflected more at a glancing view, small sun glints, a
    // drifting foam line along the shore, and the cloud shadows. Display only.
    // 11-10 (3rd round): drawn before the fog of war (Transparent-10), so unexplored rivers are darkened like the
    // ground instead of shining through; the bank is rounded and edged with damp earth (see BuildWater).
    Properties { _BaseColor ("Water Color", Color) = (1, 1, 1, 1) }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-20" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "RtsCloudShadow.hlsl"
            half4 _BaseColor;
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings { float4 positionHCS : SV_POSITION; float4 color : COLOR; float3 world : TEXCOORD0; };

            float WaterHash(float2 p)
            {
                p = frac(p * float2(213.1, 97.7));
                p += dot(p, p + 19.19);
                return frac(p.x * p.y);
            }

            float WaterNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(WaterHash(i), WaterHash(i + float2(1, 0)), u.x),
                    lerp(WaterHash(i + float2(0, 1)), WaterHash(i + float2(1, 1)), u.x), u.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output; float3 world = TransformObjectToWorld(input.positionOS.xyz);
                world.y += sin(world.x * 0.35 + _Time.y * 0.45) * 0.025 + cos(world.z * 0.21 + _Time.y * 0.30) * 0.018;
                output.positionHCS = TransformWorldToHClip(world); output.world = world; output.color = input.color; return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.world.xz;
                float t = _Time.y;
                // Two layers of ripples moving different ways give a slope to light.
                float2 e = float2(0.35, 0);
                float h0 = WaterNoise(p * 0.55 + float2(t * 0.20, t * 0.07)) + WaterNoise(p * 1.30 - float2(t * 0.11, t * 0.23)) * 0.5;
                float hx = WaterNoise((p + e.xy) * 0.55 + float2(t * 0.20, t * 0.07)) + WaterNoise((p + e.xy) * 1.30 - float2(t * 0.11, t * 0.23)) * 0.5;
                float hz = WaterNoise((p + e.yx) * 0.55 + float2(t * 0.20, t * 0.07)) + WaterNoise((p + e.yx) * 1.30 - float2(t * 0.11, t * 0.23)) * 0.5;
                half3 normal = normalize(half3((h0 - hx) * 0.9, 1.0, (h0 - hz) * 0.9));

                half3 toEye = normalize(GetCameraPositionWS() - input.world);
                Light sun = GetMainLight();
                half cloud = RtsCloudLight(input.world);

                // 11-10 (3rd round): alpha is 1 at a water cell centre and 0 at a land one. Blended over the quad and
                // wobbled by noise, it crosses one half on a rounded, ragged bank instead of the 2 m cell steps.
                half field = input.color.a + (WaterNoise(p * 0.45) - 0.5h) * 0.24h + (WaterNoise(p * 1.4 + 7.3) - 0.5h) * 0.08h;
                clip(field - 0.30h);
                half cover = smoothstep(0.47h, 0.53h, field);
                half depth = saturate(input.color.r * 1.25h);

                half3 shallow = half3(0.15, 0.40, 0.44), deep = half3(0.05, 0.19, 0.31);
                half3 water = lerp(shallow, deep, depth) * _BaseColor.rgb;
                // Sky reflected more at a glancing view and on ripples facing away.
                half fresnel = pow(1.0h - saturate(dot(normal, toEye)), 3.0h);
                half3 sky = half3(0.62, 0.74, 0.88);
                half3 colour = lerp(water, sky, saturate(0.05h + fresnel * 0.32h));
                colour *= lerp(0.80h, 1.06h, cloud) * (0.95h + 0.08h * h0);
                // Small glints where a ripple turns the sun towards the eye.
                half3 halfway = normalize(sun.direction + toEye);
                half glint = pow(saturate(dot(normal, halfway)), 220.0h) * 1.6h * cloud;
                colour += sun.color.rgb * glint;
                // Broken foam flecks near the bank and a thin lapping line on it.
                half foamNoise = WaterNoise(p * 0.9 + float2(t * 0.15, -t * 0.12));
                half fleck = smoothstep(0.62h, 0.80h, foamNoise) * smoothstep(0.55h, 0.65h, WaterNoise(p * 2.6 - float2(t * 0.2, t * 0.1)));
                half nearBank = 1.0h - smoothstep(0.53h, 0.72h, field);
                half lap = smoothstep(0.47h, 0.51h, field) * (1.0h - smoothstep(0.51h, 0.58h, field)) * (0.55h + 0.45h * sin(t * 1.3 + p.x * 0.7 + p.y * 0.5));
                half band = saturate(nearBank * fleck + lap * 0.6h);
                colour = lerp(colour, half3(0.86, 0.90, 0.88), band * 0.5h);
                half waterAlpha = lerp(0.74h, 0.94h, depth);
                waterAlpha = lerp(waterAlpha, 0.95h, band);

                // Just outside the bank: damp, darker earth fading into the grass.
                half wet = smoothstep(0.30h, 0.47h, field);
                half3 mud = half3(0.20, 0.17, 0.12) * lerp(0.75h, 1.05h, cloud);
                colour = lerp(mud, colour, cover);
                half alpha = lerp(wet * 0.5h, waterAlpha, cover);
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }
}
