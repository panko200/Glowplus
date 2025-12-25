cbuffer Constants : register(b0)
{
    float4 innerColor;
    float4 outerColor;
    float tintScale;
    float tintGamma;
    float exposure;
    float sourceOpacity;
    float colorize;
    float threshold;
    float rayLength;
    float rayDecay;
    float rayDensity;
    float2 center;
    float mixingMode;
    float linearColor;
    float3 rgbScales;
};

Texture2D<float4> InputTexture : register(t0);
Texture2D<float4> SourceTexture : register(t1);

SamplerState InputSampler : register(s0)
{
    Filter = MIN_MAG_MIP_LINEAR;
    AddressU = BORDER;
    AddressV = BORDER;
    BorderColor = float4(0, 0, 0, 0);
};

// --- 関数定義 ---

float rand(float2 uv)
{
    return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
}

float4 SampleSafe(Texture2D tex, SamplerState samp, float2 uv)
{
    return tex.Sample(samp, uv);
}

float GetLuma(float3 color)
{
    return dot(color, float3(0.2126, 0.7152, 0.0722));
}

float3 SRGBToLinear(float3 color)
{
    return pow(max(color, 0.0), 2.2);
}

float3 LinearToSRGB(float3 color)
{
    return pow(max(color, 0.0), 1.0 / 2.2);
}

// --- メイン関数 ---

float4 main(
    float4 pos : SV_POSITION,
    float4 posScene : SCENE_POSITION,
    float4 uv0 : TEXCOORD0,
    float4 uv1 : TEXCOORD1
) : SV_Target
{
    float4 glow = SampleSafe(InputTexture, InputSampler, uv0.xy);
    
    // --- 放射光処理 (ここも維持) ---
    if (rayLength > 0.001)
    {
        int samples = 20;
        float2 delta = (uv0.xy - center) * (rayLength / float(samples)) * rayDensity;
        float2 currentUV = uv0.xy;
        float decay = 1.0;
        float4 accumRay = 0;
        [unroll(20)]
        for (int i = 0; i < samples; i++)
        {
            currentUV -= delta;
            float4 sampleCol = SampleSafe(InputTexture, InputSampler, currentUV);
            accumRay += sampleCol * decay;
            decay *= rayDecay;
        }
        glow += accumRay * (exposure * 0.2);
    }

    // 計算用カラー
    float3 mixInner = innerColor.rgb;
    float3 mixOuter = outerColor.rgb;
    
    // 画像側のリニア変換 (維持)
    if (linearColor > 0.5)
    {
        glow.rgb = SRGBToLinear(glow.rgb);
    }

    // --- ここがモード分岐の核心 (ここも維持) ---
    if (mixingMode > 0.5)
    {
        mixInner = SRGBToLinear(mixInner);
        mixOuter = SRGBToLinear(mixOuter);
    }

    float luma = GetLuma(glow.rgb);
    luma *= tintScale;
    float t = saturate(pow(max(luma, 0.0001), tintGamma));
    
    float3 finalGlowRGB;
    float finalAlpha;

    if (colorize > 0.5)
    {
        // --- Colorize ON (維持) ---
        finalGlowRGB = lerp(mixOuter, mixInner, t) * min(luma * 2.0, 1.0);
        finalAlpha = saturate(glow.a * tintScale);
    }
    else
    {
        // --- Colorize OFF (ここだけ修正！) ---
        // 「outerColor.rgb」を使っていたのを、モード切替済みの「mixOuter」に変えるだけです。
        // これで Colorize OFF 時も Physical モードなら色がリニア化されます。
        finalGlowRGB = glow.rgb * mixOuter;
        finalAlpha = glow.a * outerColor.a;
    }

    finalGlowRGB *= exposure;
    
    // --- 合成処理 (維持) ---
    float4 source = SampleSafe(SourceTexture, InputSampler, uv1.xy);
    source.a *= sourceOpacity;
    source.rgb *= sourceOpacity;

    if (linearColor > 0.5)
    {
        source.rgb = SRGBToLinear(source.rgb);
    }

    float3 finalRGB = source.rgb + finalGlowRGB;
    float finalA = saturate(source.a + finalAlpha);

    if (linearColor > 0.5)
    {
        finalRGB = LinearToSRGB(finalRGB);
    }

    // ディザリング (維持)
    float noise = (rand(uv0.xy) - 0.5) / 255.0;
    finalRGB += noise;

    return saturate(float4(finalRGB, finalA));
}