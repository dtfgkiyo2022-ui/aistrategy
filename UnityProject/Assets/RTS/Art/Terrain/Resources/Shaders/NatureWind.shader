Shader "Rts/NatureWind"
{
    // Lives under Resources so the player build keeps it (it is only reached through Shader.Find).
    // Vertex-coloured trees, rocks and grass that sway, cast and receive the sun's shadows.
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _WindAmount ("Wind", Float) = 0.04
        // Near grass only (NearGrassField, 11-09): blades shrink into the ground between x and y metres from the
        // point the camera looks at; z = 1 turns it on; w scales every blade (0..1, fades the field out as the
        // camera rises). Off (z = 0) for the trees, rocks and the whole-map decorations.
        _FadeRange ("Fade Range", Vector) = (0,0,0,1)
        _FadeCentre ("Fade Centre", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        half4 _BaseColor; float _WindAmount; float4 _FadeRange; float4 _FadeCentre;
        // High and Ultra only (GraphicsQuality, 11-09); see Rts/TerrainBlend.
        float _RtsLook; float4 _RtsSky; float4 _RtsGround;

        float3 NatureSway(float3 p)
        {
            if (_FadeRange.z > 0.5)
            {
                float3 w = TransformObjectToWorld(p);
                float d = distance(w.xz, _FadeCentre.xz);
                p.y *= saturate((_FadeRange.y - d) / max(0.001, _FadeRange.y - _FadeRange.x)) * _FadeRange.w;
            }
            float sway = sin(_Time.y * 1.4 + p.x * 0.8 + p.z * 0.5) * _WindAmount * saturate(p.y);
            p.x += sway;
            p.z += cos(_Time.y * 1.1 + p.z) * _WindAmount * 0.35 * saturate(p.y);
            return p;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "RtsCloudShadow.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION; float4 color : COLOR;
                float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(NatureSway(input.positionOS.xyz));
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                output.color = input.color * _BaseColor;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            half4 Frag(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                float3 normal = normalize(input.normalWS) * (frontFace ? 1.0 : -1.0);
                Light sun = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half daylight = saturate(dot(normal, sun.direction)) * lerp(1.0h, sun.shadowAttenuation, 0.85h) * RtsCloudLight(input.positionWS);
                half directAo = 1.0h, indirectAo = 1.0h;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(input.positionHCS));
                    directAo = ao.directAmbientOcclusion;
                    indirectAo = ao.indirectAmbientOcclusion;
                #endif
                half3 lit = lerp(half3(0.48, 0.52, 0.62) * indirectAo, sun.color.rgb * directAo, daylight);
                if (_RtsLook > 0.5)
                {
                    // Softer wrap of the sun round thin blades and leaves, and sky above / ground below.
                    half wrap = saturate((dot(normal, sun.direction) + 0.35h) / 1.35h) * lerp(1.0h, sun.shadowAttenuation, 0.85h) * RtsCloudLight(input.positionWS);
                    half3 ambient = lerp(_RtsGround.rgb, _RtsSky.rgb, normal.y * 0.5h + 0.5h) * indirectAo;
                    lit = lerp(ambient, sun.color.rgb * directAo, wrap);
                }
                return half4(input.color.rgb * lit, input.color.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct ShadowAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            float4 ShadowVert(ShadowAttributes input) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(NatureSway(input.positionOS.xyz));
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirection = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirection = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }
            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            struct DepthAttributes { float4 positionOS : POSITION; };
            float4 DepthVert(DepthAttributes input) : SV_POSITION
            {
                return TransformObjectToHClip(NatureSway(input.positionOS.xyz));
            }
            half DepthFrag(float4 positionHCS : SV_POSITION) : SV_Target { return positionHCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull Off
            HLSLPROGRAM
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag
            struct NormalsAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct NormalsVaryings { float4 positionHCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            NormalsVaryings NormalsVert(NormalsAttributes input)
            {
                NormalsVaryings output;
                output.positionHCS = TransformObjectToHClip(NatureSway(input.positionOS.xyz));
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            half4 NormalsFrag(NormalsVaryings input) : SV_Target { return half4(normalize(input.normalWS), 0.0); }
            ENDHLSL
        }
    }
}
