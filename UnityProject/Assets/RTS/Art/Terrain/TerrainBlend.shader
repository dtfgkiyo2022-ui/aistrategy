Shader "Rts/TerrainBlend"
{
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
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_GrassTex); SAMPLER(sampler_GrassTex);
            TEXTURE2D(_SoilTex); SAMPLER(sampler_SoilTex);
            TEXTURE2D(_SandTex); SAMPLER(sampler_SandTex);
            TEXTURE2D(_RockTex); SAMPLER(sampler_RockTex);
            TEXTURE2D(_ForestTex); SAMPLER(sampler_ForestTex);
            TEXTURE2D(_SplatA); SAMPLER(sampler_SplatA);
            TEXTURE2D(_SplatB); SAMPLER(sampler_SplatB);
            float _TextureScale;
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
                float2 uv = input.uv;
                half4 weights = SAMPLE_TEXTURE2D(_SplatA, sampler_SplatA, uv);
                half forestWeight = SAMPLE_TEXTURE2D(_SplatB, sampler_SplatB, uv).r;
                half sum = max(0.001h, weights.r + weights.g + weights.b + weights.a + forestWeight);
                float2 tile = uv * _TextureScale * 24.0;
                half4 color = SAMPLE_TEXTURE2D(_GrassTex, sampler_GrassTex, tile) * weights.r;
                color += SAMPLE_TEXTURE2D(_SoilTex, sampler_SoilTex, tile) * weights.g;
                color += SAMPLE_TEXTURE2D(_SandTex, sampler_SandTex, tile) * weights.b;
                color += SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, tile) * weights.a;
                color += SAMPLE_TEXTURE2D(_ForestTex, sampler_ForestTex, tile) * forestWeight;
                color /= sum;
                Light sun = GetMainLight();
                half daylight = saturate(dot(half3(0, 1, 0), sun.direction));
                color.rgb *= lerp(half3(0.58, 0.62, 0.72), sun.color.rgb, daylight);
                return color;
            }
            ENDHLSL
        }
    }
}
