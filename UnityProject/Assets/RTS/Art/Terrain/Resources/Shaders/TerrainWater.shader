Shader "Rts/TerrainWater"
{
    Properties { _BaseColor ("Water Color", Color) = (0.15, 0.42, 0.78, 1) }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            half4 _BaseColor;
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings { float4 positionHCS : SV_POSITION; float4 color : COLOR; float3 world : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output; float3 world = TransformObjectToWorld(input.positionOS.xyz);
                world.y += sin(world.x * 0.35 + _Time.y * 0.45) * 0.025 + cos(world.z * 0.21 + _Time.y * 0.30) * 0.018;
                output.positionHCS = TransformWorldToHClip(world); output.world = world; output.color = input.color; return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half wave = 0.94h + 0.06h * sin(input.world.x * 0.7 + input.world.z * 0.33 + _Time.y * 0.5);
                return half4(_BaseColor.rgb * input.color.rgb * wave, 0.88h);
            }
            ENDHLSL
        }
    }
}
