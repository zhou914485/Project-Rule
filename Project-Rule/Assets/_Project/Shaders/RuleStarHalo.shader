Shader "RuleGame/Rules/StarHalo"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        [HDR] _Accent ("Solar / Enemy Rose", Color) = (1,1,1,1)
        [HDR] _Secondary ("Ion / Enemy Violet", Color) = (1,1,1,1)
        _Radius ("Radius", Float) = .72
        _Age ("Visual clock", Float) = 0
        _Burst ("Release", Float) = 1
        _Collapse ("Return", Float) = 0
        _Opacity ("Brightness", Float) = 1
        _Glyph ("Attribute", Float) = 0
        _WorldScale ("World canvas scale", Float) = 1
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="True" }
        Blend One OneMinusSrcColor
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
            CBUFFER_START(UnityPerMaterial)
                half4 _Accent, _Secondary;
                float _Radius, _Age, _Burst, _Collapse, _Opacity, _Glyph, _WorldScale;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv; output.color = input.color * unity_SpriteColor;
                return output;
            }
            float Cloud(float a, float r, float t)
            {
                // 连续的极坐标湍流，形成柔软的云气和丝绸状光潮，不画细轮廓/刻度。
                float n = sin(a * 9 + r * 31 - t * 1.4) * .45;
                n += sin(a * 17 - r * 48 + t * 2.1) * .25;
                n += sin(a * 29 + r * 71 - t * .8) * .15;
                return .68 + n * .32;
            }
            float Band(float distance, float width)
            { float x = distance / max(.005, width); return exp2(-x * x * 2); }
            half4 Frag(Varyings input) : SV_Target
            {
                const float TAU = 6.2831853;
                float2 p = input.uv * 2 - 1;
                float r = length(p), a = atan2(p.y, p.x);
                float R = max(.015, _Radius), age = _Age;
                float B = _Burst, C = _Collapse;
                float energy = max(B, sin(C * 3.14159) * .8);
                float turbulence = Cloud(a + _Glyph * .19, r, age);
                float wave = sin(a * 7 + age * 1.7) * .007 + sin(a * 13 - age * 2) * .003;
                float d = r - R + wave * energy;
                float width = lerp(.026, .047, energy) * smoothstep(.015, .16, R);
                width = max(.008, width) * rsqrt(max(1, _WorldScale));
                // 宽阔的白热冲击带、云状散射和两层延迟光潮；所有边界都有体积和柔光。
                float front = Band(d, width) * (.7 + turbulence * .65);
                float scattering = Band(d, width * 2.5) * .085;
                float trailing = Band(r - R * .82, width * 1.35) * .28 * turbulence;
                float echo = Band(r - R * .59, width * 1.8) * .10 * (.3 + energy);
                float edgeTint = Band(d - width * .8, width * 1.7) * .25;
                float sheet = exp2(-max(0, R - r) * 13) * step(r, R) * turbulence * .07;

                // 圣光从中心放射到波前，宽窄不同的光束叠加，避免整齐的科技辐条。
                float rays = pow(saturate(sin(a * 11 + sin(a * 3) * 1.7 + age * .12)), 12);
                rays += pow(saturate(sin(a * 19 - age * .08)), 30) * .45;
                float rayEnvelope = smoothstep(.025, .12, r) * (1 - smoothstep(R * .72, R * 1.2 + .03, r));
                float shafts = rays * rayEnvelope * (.04 + energy * .8) * Cloud(a, r * .4, age * .35);
                float blessing = exp2(-r * r / max(.004, R * R * .7)) * (.008 + energy * .025);
                float2 coreP = p * max(1, _WorldScale);
                float coreR = length(coreP);
                float launch = exp2(-age * 5) + pow(C, 8) * 1.2;
                float core = exp2(-coreR * coreR / (.003 + launch * .012));
                float coreGlow = exp2(-coreR * coreR * 24) * (.015 + launch * .14);

                float feathers = 0;
                [unroll] for (int i = 0; i < 10; i++)
                {
                    float seed = frac(i * .618034 + _Glyph * .137);
                    float theta = i * 2.39996 + age * (.035 + seed * .07) * (1 - C * 2);
                    float travel = frac(age * .16 + seed);
                    float2 axis = float2(cos(theta), sin(theta));
                    float2 q = p - axis * R * (.65 + travel * .46);
                    float along = dot(q, axis), across = dot(q, float2(-axis.y, axis.x));
                    float plume = exp2(-along * along / .00065 - across * across / .000035);
                    feathers += plume * sin(travel * 3.14159) * (.2 + energy * .8);
                }
                // 用原配色混入柔白，降低饱和度；中心只保留圆形光晕。
                half3 sacredWhite = lerp(_Accent.rgb, half3(1,1,1), .32);
                half3 softAccent = lerp(_Accent.rgb, half3(1,1,1), .22);
                half3 softSecondary = lerp(_Secondary.rgb, half3(1,1,1), .22);
                half3 light = sacredWhite * (front * 2.8 + scattering + trailing + echo + sheet + shafts + blessing);
                light += softAccent * (edgeTint + coreGlow + feathers * .35);
                light += softSecondary * (Band(d + width * 1.2, width * 2.7) * .15 + feathers * .3);
                light += half3(1,1,1) * (core * (.12 + launch * .65) + feathers * .8);
                light *= _Opacity * input.color.a * (1 - smoothstep(.91, .995, r));
                // Screen 混合配合柔性压高光，保留大面积白光中的层次和场景轮廓。
                light = 1 - exp2(-light * 1.2);
                return half4(light, 0);
            }
            ENDHLSL
        }
    }
}
