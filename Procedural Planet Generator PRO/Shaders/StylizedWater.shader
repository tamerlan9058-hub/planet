Shader "Custom/StylizedWater"
{
    Properties
    {
        [Header(Water Colors)]
        _ShallowColor      ("Shallow Color (Day)",   Color) = (0.16, 0.62, 0.82, 0.72)
        _DeepColor         ("Deep Color (Day)",      Color) = (0.03, 0.16, 0.44, 0.86)
        _NightShallowColor ("Shallow Color (Night)", Color) = (0.02, 0.04, 0.10, 0.84)
        _NightDeepColor    ("Deep Color (Night)",    Color) = (0.01, 0.02, 0.07, 0.94)
        _FoamColor         ("Foam Color",            Color) = (0.92, 0.96, 1.00, 1)

        [Header(Wave Settings)]
        _WaveSpeed     ("Wave Speed", Float) = 0.55
        _WaveAmplitude ("Wave Amplitude", Float) = 0.85
        _WaveFrequency ("Wave Frequency", Float) = 0.0028
        _WaveDirection ("Wave Direction", Vector) = (1, 0, 0.35, 0)
        _WaveChop      ("Wave Chop", Range(0, 1.5)) = 0.55

        [Header(Foam)]
        _FoamDistance  ("Foam Distance", Float) = 2.0
        _FoamAmount    ("Foam Amount", Range(0, 1)) = 0.24
        _FoamSpeed     ("Foam Speed", Float) = 0.32
        _FoamSharpness ("Foam Sharpness", Range(0, 1)) = 0.72

        [Header(Visual)]
        _Smoothness       ("Smoothness", Range(0, 1)) = 0.74
        _Transparency     ("Transparency", Range(0, 1)) = 0.52
        _DepthGradient    ("Depth Gradient", Float) = 0.024
        _FresnelPower     ("Fresnel Power", Range(1, 8)) = 4.2
        _SparkleStrength  ("Sparkle Strength", Range(0, 1)) = 0.08
        _SpecularStrength ("Specular Strength", Range(0, 2)) = 0.88
        _DeepAbsorption   ("Deep Absorption", Range(0, 2)) = 0.72
        _EdgeFade         ("Edge Fade", Range(0, 1)) = 0.18

        [Header(Refraction)]
        _Distortion ("Distortion", Range(0, 0.1)) = 0.0025

        [Header(Planet)]
        _PlanetCenter ("Planet Center", Vector) = (0, 0, 0, 0)
        _PlanetRadius ("Planet Radius", Float) = 5000
        _WaterLevel   ("Water Level", Float) = 5030

        [Header(Night Settings)]
        _NightAmbient    ("Night Ambient", Range(0, 0.05)) = 0.012
        _HorizonSoftness ("Horizon Softness", Range(0.5, 10.0)) = 4.0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 260

        GrabPass { "_WaterBackground" }
        Cull Back

        CGPROGRAM
        #pragma surface surf Standard alpha:fade vertex:vert noforwardadd
        #pragma target 3.0
        #include "UnityCG.cginc"

        struct Input
        {
            float3 worldPos;
            float4 screenPos;
            float3 viewDir;
            float3 worldNormal;
        };

        float4 _ShallowColor, _DeepColor;
        float4 _NightShallowColor, _NightDeepColor;
        float4 _FoamColor;

        float _WaveSpeed, _WaveAmplitude, _WaveFrequency, _WaveChop;
        float4 _WaveDirection;
        float _FoamDistance, _FoamAmount, _FoamSpeed, _FoamSharpness;
        float _Smoothness, _Transparency, _DepthGradient;
        float _FresnelPower, _SparkleStrength, _Distortion;
        float _NightAmbient, _HorizonSoftness;
        float _SpecularStrength, _DeepAbsorption, _EdgeFade;
        float4 _PlanetCenter;
        float _PlanetRadius;
        float _WaterLevel;

        sampler2D _CameraDepthTexture;
        sampler2D _WaterBackground;

        float3 GetSafeTangent(float3 normal)
        {
            float3 axis = abs(normal.y) < 0.95 ? float3(0, 1, 0) : float3(1, 0, 0);
            return normalize(cross(axis, normal));
        }

        float2 BuildWaveUV(float3 worldPos, float3 radialNormal)
        {
            float3 tangent = GetSafeTangent(radialNormal);
            float3 bitangent = normalize(cross(radialNormal, tangent));

            float2 dir = _WaveDirection.xz;
            if (dot(dir, dir) < 1e-6)
                dir = float2(1.0, 0.0);
            else
                dir = normalize(dir);

            float2 uv = float2(dot(worldPos, tangent), dot(worldPos, bitangent));
            float2 rotated;
            rotated.x = uv.x * dir.x - uv.y * dir.y;
            rotated.y = uv.x * dir.y + uv.y * dir.x;
            return rotated;
        }

        float Hash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float Noise2(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);

            float a = Hash21(i);
            float b = Hash21(i + float2(1.0, 0.0));
            float c = Hash21(i + float2(0.0, 1.0));
            float d = Hash21(i + float2(1.0, 1.0));

            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }

        float FBM2(float2 p)
        {
            float sum = 0.0;
            float amp = 0.55;
            float freq = 1.0;

            [unroll(3)]
            for (int i = 0; i < 3; i++)
            {
                sum += Noise2(p * freq) * amp;
                freq *= 2.03;
                amp *= 0.5;
            }

            return sum;
        }

        float MultiWave(float2 pos, float t)
        {
            float w1 = sin(pos.x * _WaveFrequency + t * _WaveSpeed);
            float w2 = cos(pos.y * (_WaveFrequency * 1.31) - t * (_WaveSpeed * 0.64));
            float w3 = sin((pos.x + pos.y) * (_WaveFrequency * 0.72) + t * (_WaveSpeed * 0.42));
            float w4 = sin((pos.x * 0.62 - pos.y * 1.18) * (_WaveFrequency * 1.83) + t * (_WaveSpeed * 0.28));
            return (w1 * 0.42 + w2 * 0.26 + w3 * 0.20 + w4 * 0.12) * _WaveAmplitude;
        }

        float FoamNoise(float2 pos, float t)
        {
            float slow = FBM2(pos * 0.12 + t * _FoamSpeed);
            float fast = FBM2(pos * 0.24 - t * (_FoamSpeed * 0.72));
            return saturate(slow * 0.65 + fast * 0.35);
        }

        void vert(inout appdata_full v)
        {
            float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
            float3 radialNormal = normalize(worldPos - _PlanetCenter.xyz);
            float2 waveUV = BuildWaveUV(worldPos, radialNormal);
            float wave = MultiWave(waveUV, _Time.y);
            worldPos += radialNormal * wave;
            v.vertex = mul(unity_WorldToObject, float4(worldPos, 1.0));
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 radialNormal = normalize(IN.worldPos - _PlanetCenter.xyz);
            float2 waveUV = BuildWaveUV(IN.worldPos, radialNormal);
            float t = _Time.y;
            float wave = MultiWave(waveUV, t);

            float waveDx = MultiWave(waveUV + float2(1.1, 0.0), t) - wave;
            float waveDy = MultiWave(waveUV + float2(0.0, 1.1), t) - wave;

            float3 tangent = GetSafeTangent(radialNormal);
            float3 bitangent = normalize(cross(radialNormal, tangent));
            float3 waveNormal = normalize(radialNormal - tangent * waveDx * _WaveChop - bitangent * waveDy * _WaveChop);

            float3 lightDir = normalize(UnityWorldSpaceLightDir(IN.worldPos));
            float3 viewDir = normalize(IN.viewDir);
            float dayFactor = smoothstep(-0.18, 0.30, dot(radialNormal, lightDir) * _HorizonSoftness);

            float sceneDepth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(IN.screenPos)));
            float surfaceDepth = IN.screenPos.w;
            float depthDiff = max(0.0, sceneDepth - surfaceDepth);
            float waterDepth = saturate(1.0 - exp(-depthDiff * _DepthGradient * max(0.1, _DeepAbsorption)));

            float shoreline = saturate(1.0 - depthDiff / max(_FoamDistance, 0.0001));
            float foamNoise = FoamNoise(waveUV, t);
            float shorelineFoam = pow(shoreline, lerp(1.8, 0.7, _FoamSharpness)) * smoothstep(0.42, 0.82, foamNoise);
            float crestFoam = saturate((abs(waveDx) + abs(waveDy)) * (1.4 + _WaveChop * 2.2) - (0.75 - _FoamSharpness * 0.45));
            float foam = saturate(max(shorelineFoam, crestFoam * 0.55) * _FoamAmount);

            float3 dayColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, waterDepth);
            float3 nightColor = lerp(_NightShallowColor.rgb, _NightDeepColor.rgb, waterDepth);

            float fresnel = pow(1.0 - saturate(dot(viewDir, waveNormal)), _FresnelPower);
            float2 distortion = waveNormal.xz * (_Distortion * IN.screenPos.w);
            float4 sceneCol = tex2Dproj(_WaterBackground, UNITY_PROJ_COORD(IN.screenPos + float4(distortion, 0, 0)));

            float specularTerm = pow(saturate(dot(reflect(-lightDir, waveNormal), viewDir)), lerp(72.0, 160.0, _Smoothness));
            float sparkle = pow(saturate(dot(reflect(-lightDir, waveNormal), viewDir)), 320.0) * _SparkleStrength;
            float specular = (specularTerm * _SpecularStrength + sparkle) * dayFactor * saturate(dot(waveNormal, lightDir) * 0.5 + 0.5);

            float3 refraction = lerp(dayColor, sceneCol.rgb * 0.36 + dayColor * 0.64, 0.18);
            float3 reflection = sceneCol.rgb * (0.22 + fresnel * 0.42);
            float3 waterColor = lerp(refraction, reflection + dayColor * 0.55, saturate(fresnel * 0.65));
            waterColor = lerp(nightColor, waterColor, dayFactor);

            float subsurface = saturate(dot(-lightDir, waveNormal) * 0.45 + 0.55) * (1.0 - waterDepth) * dayFactor * 0.18;
            waterColor += _ShallowColor.rgb * subsurface;
            waterColor = lerp(waterColor, _FoamColor.rgb, foam);
            waterColor += specular;
            waterColor = max(waterColor, nightColor * (0.52 + _NightAmbient * 9.0));

            float alpha = lerp(_Transparency, 0.92, fresnel);
            alpha = lerp(alpha, 0.98, shoreline * _EdgeFade + foam * 0.55);
            alpha = saturate(alpha);

            o.Albedo = waterColor * 0.72;
            o.Emission = waterColor * (0.04 + dayFactor * 0.05);
            o.Metallic = 0.0;
            o.Smoothness = lerp(_Smoothness, _Smoothness * 0.55, foam);
            o.Alpha = alpha;
        }
        ENDCG
    }
    FallBack "Transparent/Diffuse"
}
