Shader "RuleGame/DiscoRadiation"
{
    Properties { _Strength ("Strength", Float) = 1 _Phase ("Phase", Float) = 0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Strength, _Phase;
                float4 _Origin;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 world : TEXCOORD0; float4 color : COLOR; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = pos.positionCS; output.world = pos.positionWS.xy; output.color = input.color;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float2 delta = input.world - _Origin.xy;
                float radius = length(delta);
                float angle = atan2(delta.y, delta.x);
                // 全周底色一直可见；彩色光束之间的暗带仍是危险区。
                float hue = angle / 6.2831853 - _Phase * .075 + radius * .025;
                half3 rainbow = saturate(abs(frac(hue + float3(0, .666667, .333333)) * 6 - 3) - 1);
                half3 counterColor = saturate(abs(frac(hue + .42 + float3(0, .666667, .333333)) * 6 - 3) - 1);
                float fans = smoothstep(.3, .95, .5 + .5 * sin(angle * 7 - _Phase * .85 + radius * .17));
                float beams = pow(saturate(.5 + .5 * cos(angle * 18 - _Phase * 1.65)), 22);
                float counter = pow(saturate(.5 + .5 * cos(angle * 11 + _Phase * 1.05)), 32);
                float rings = pow(saturate(.5 + .5 * sin(radius * 4.2 - _Phase * 3.4)), 10);
                float beat = .9 + .1 * sin(_Phase * 3.5);
                // 镜球散落的小亮片有界且连续变化，不需要额外粒子或光源。
                float spin = _Phase * .045;
                float2 grid = float2(delta.x * cos(spin) - delta.y * sin(spin),
                    delta.x * sin(spin) + delta.y * cos(spin)) * 2.4;
                float seed = frac(sin(dot(floor(grid), float2(127.1, 311.7))) * 43758.5453);
                float2 cellOffset = abs(frac(grid) - .5);
                float star = pow(saturate(1 - cellOffset.x * 28), 2) * pow(saturate(1 - cellOffset.y * 6), 3)
                    + pow(saturate(1 - cellOffset.y * 28), 2) * pow(saturate(1 - cellOffset.x * 6), 3);
                star *= smoothstep(.76, .94, seed) * pow(saturate(.5 + .5 * sin(_Phase * 2.5 + seed * 6.2831853)), 4);
                half3 color = rainbow * (.8 + fans * .65) + counterColor * counter * .8
                    + beams * .55 + rings * rainbow * .35 + star * 1.5;
                float alpha = (.25 + fans * .13 + beams * .2 + counter * .12 + rings * .05 + star * .16) * beat;
                // 低亮暖色预警与正式彩色辐射区分，停火由表现组件直接隐藏。
                if (_Strength < .5) { color = half3(1, .68, .25); alpha = .2; }
                return half4(color, alpha * input.color.a * _Strength);
            }
            ENDHLSL
        }
    }
}
