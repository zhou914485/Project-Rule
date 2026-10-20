Shader "RuleGame/Player/HurtFeedback"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Mode ("Silhouette / Impact / Mote", Float) = 0
        _Progress ("Progress", Range(0,1)) = 0
        _Opacity ("Opacity", Range(0,1)) = 1
        _Accent ("Accent", Color) = (.72,.16,.24,1)
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            struct Attributes { COMMON_2D_INPUTS half4 color : COLOR; };
            struct Varyings { COMMON_2D_OUTPUTS half4 color : COLOR; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;
            CBUFFER_START(UnityPerMaterial)
                half4 _Accent;
                float _Mode, _Progress, _Opacity;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                output.color = input.color * unity_SpriteColor;
                return output;
            }

            float Spike(float2 p, float angle, float extent, float width)
            {
                float2 axis = float2(cos(angle), sin(angle));
                float along = dot(p, axis);
                float taper = saturate(1 - along / extent);
                float across = abs(dot(p, float2(-axis.y, axis.x)));
                return (1 - smoothstep(width * taper, width * taper + .008, across))
                    * smoothstep(-.02, .01, along) * (1 - smoothstep(extent - .012, extent, along));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float t = saturate(_Progress);
                half3 ivory = half3(1, .98, .9);
                half3 ink = half3(.045, .022, .035);
                if (_Mode < .5)
                {
                    float alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                    float2 d = _MainTex_TexelSize.xy * 2;
                    float adjacent = min(min(
                        SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(d.x, 0)).a,
                        SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - float2(d.x, 0)).a), min(
                        SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(0, d.y)).a,
                        SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - float2(0, d.y)).a));
                    float edge = saturate((alpha - adjacent) * 3);
                    if (t > .8) return half4(ivory, edge * _Opacity * input.color.a);
                    return half4(lerp(ivory, ink, edge * .75), alpha * _Opacity * input.color.a);
                }
                float2 p = input.uv * 2 - 1;
                float radius = length(p);
                if (_Mode > 1.5)
                {
                    // 深色不规则尖片；只在尖端留一点高光，避免变成成圈的花瓣。
                    float taper = saturate((1 - p.y) * .65);
                    float jagged = .035 * sin(p.y * 13 + t * 4);
                    float shape = (1 - smoothstep(.46 * taper, .46 * taper + .07, abs(p.x + jagged)))
                        * (1 - smoothstep(.82, 1, abs(p.y)));
                    float glint = smoothstep(.65, .95, p.y) * .65;
                    return half4(lerp(ink, ivory, glint), shape * _Opacity * input.color.a);
                }
                // 不等角、不等长的连根裂芒，前两帧骤然撑开，随后碎裂消退。
                float2 q = p / lerp(.66, 1.08, saturate(t * 3));
                float light = 0, silhouette = 0;
                [unroll] for (int i = 0; i < 11; i++)
                {
                    float angle = i * 2.399963 + frac(i * .413) * .25;
                    float extent = .42 + frac(i * .754878 + .19) * .55;
                    float width = .025 + frac(i * .381966) * .075;
                    silhouette = max(silhouette, Spike(q, angle, extent + .035, width + .025));
                    light = max(light, Spike(q, angle, extent, width));
                }
                float core = 1 - smoothstep(.08, .22, length(q * float2(1, 1.25)));
                light = max(light, core);
                silhouette = max(silhouette, 1 - smoothstep(.10, .25, length(q * float2(1, 1.25))));
                float fade = 1 - smoothstep(.12, .85, t);
                float breakup = 1 - smoothstep(.36, .9, t) * smoothstep(.05, .36, radius);
                float alpha = silhouette * fade * breakup * (1 - smoothstep(.95, 1, radius));
                return half4(lerp(ink, ivory, light), alpha * _Opacity * input.color.a);
            }
            ENDHLSL
        }
    }
}
