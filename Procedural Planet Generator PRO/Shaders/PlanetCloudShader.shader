Shader "Custom/PlanetCloudShader"
{
    Properties
    {
        _CloudColor ("Cloud Color", Color) = (1, 1, 1, 1)
        _ShadowColor ("Shadow Color", Color) = (0.72, 0.76, 0.82, 1)
        _PlanetCenter ("Planet Center", Vector) = (0, 0, 0, 0)
        _CloudScale ("Cloud Scale", Range(1, 12)) = 4.4
        _Coverage ("Coverage", Range(0, 1)) = 0.58
        _Softness ("Softness", Range(0.01, 0.4)) = 0.12
        _Opacity ("Opacity", Range(0, 1)) = 0.72
        _ScrollSpeedA ("Scroll Speed A", Range(0, 0.2)) = 0.03
        _ScrollSpeedB ("Scroll Speed B", Range(0, 0.2)) = 0.015
        _WindDirectionA ("Wind Direction A", Vector) = (0.88, 0.18, 0.32, 0)
        _WindDirectionB ("Wind Direction B", Vector) = (-0.36, 0.08, 0.92, 0)
        _LightWrap ("Light Wrap", Range(0, 1)) = 0.38
        _SilverLining ("Silver Lining", Range(0, 1)) = 0.48
        _InnerOpacity ("Inner Opacity", Range(0, 1)) = 0.9
        _BacklightStrength ("Backlight Strength", Range(0, 1)) = 0.22
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 220

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _CloudColor;
            fixed4 _ShadowColor;
            float4 _PlanetCenter;
            float _CloudScale;
            float _Coverage;
            float _Softness;
            float _Opacity;
            float _ScrollSpeedA;
            float _ScrollSpeedB;
            float4 _WindDirectionA;
            float4 _WindDirectionB;
            float _LightWrap;
            float _SilverLining;
            float _InnerOpacity;
            float _BacklightStrength;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 viewDir : TEXCOORD2;
            };

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float Noise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash31(i + float3(0, 0, 0));
                float n100 = Hash31(i + float3(1, 0, 0));
                float n010 = Hash31(i + float3(0, 1, 0));
                float n110 = Hash31(i + float3(1, 1, 0));
                float n001 = Hash31(i + float3(0, 0, 1));
                float n101 = Hash31(i + float3(1, 0, 1));
                float n011 = Hash31(i + float3(0, 1, 1));
                float n111 = Hash31(i + float3(1, 1, 1));

                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);
                return lerp(nxy0, nxy1, f.z);
            }

            float FBM(float3 p)
            {
                float sum = 0.0;
                float amp = 0.55;
                float freq = 1.0;

                [unroll(4)]
                for (int i = 0; i < 4; i++)
                {
                    sum += Noise3(p * freq) * amp;
                    freq *= 2.02;
                    amp *= 0.5;
                }

                return sum;
            }

            float3 SafeNormalize(float3 dir, float3 fallback)
            {
                float lenSq = dot(dir, dir);
                return lenSq > 1e-5 ? dir * rsqrt(lenSq) : fallback;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = normalize(_WorldSpaceCameraPos - o.worldPos);
                return o;
            }

            fixed4 frag(v2f i, fixed facing : VFACE) : SV_Target
            {
                float3 radial = normalize(i.worldPos - _PlanetCenter.xyz);
                float3 normal = normalize(i.worldNormal);
                float3 shadingNormal = normalize(normal * (facing >= 0.0 ? 1.0 : -1.0));
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float3 windA = SafeNormalize(_WindDirectionA.xyz, float3(0.88, 0.18, 0.32));
                float3 windB = SafeNormalize(_WindDirectionB.xyz, float3(-0.36, 0.08, 0.92));

                float t = _Time.y;
                float3 cloudP = radial * _CloudScale;
                float macro = FBM(cloudP + windA * (t * _ScrollSpeedA));
                float detail = FBM(cloudP * 2.15 + windB * (t * _ScrollSpeedB) + 31.7);
                float erosion = FBM(cloudP * 3.8 - (windA * 0.65 + windB * 0.35) * (t * (_ScrollSpeedB * 0.6)) + 73.1);

                float density = macro * 0.58 + detail * 0.32 + erosion * 0.10;
                float mask = smoothstep(_Coverage - _Softness * 0.65, _Coverage + _Softness, density);

                float outerView = saturate(dot(i.viewDir, radial));
                float innerView = saturate(-dot(i.viewDir, radial));
                float wrapLight = saturate((dot(shadingNormal, lightDir) + _LightWrap) / (1.0 + _LightWrap));
                float silver = pow(1.0 - saturate(abs(dot(i.viewDir, shadingNormal))), 4.5) * _SilverLining * saturate(dot(shadingNormal, lightDir) * 0.5 + 0.5);
                float backlight = saturate(dot(-lightDir, shadingNormal) * 0.5 + 0.5) * innerView * _BacklightStrength;
                float horizonOuter = saturate(0.45 + pow(1.0 - outerView, 2.1) * 0.55);
                float horizonInner = saturate(0.82 + pow(1.0 - saturate(abs(dot(i.viewDir, shadingNormal))), 2.0) * 0.18);
                float horizon = lerp(horizonOuter, horizonInner, innerView);

                float3 color = lerp(_ShadowColor.rgb, _CloudColor.rgb, wrapLight);
                color += _CloudColor.rgb * (silver + backlight);

                float alpha = saturate(mask * lerp(_Opacity, max(_Opacity, _InnerOpacity), innerView) * horizon);
                return fixed4(color, alpha);
            }
            ENDCG
        }
    }
    FallBack Off
}
