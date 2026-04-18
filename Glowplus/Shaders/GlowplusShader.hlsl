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
    float mixingMode;
    float linearColor;
    
    float chromaR;
    float chromaG;
    float chromaB;
    float rayLength;
    
    float rayCenterX;
    float rayCenterY;
    float raySamples;
    float texWidth;
    
    float texHeight;
    float rayFalloff;
    float rayStyle;
    float rayAngle;
    
    float chromaStyle;
    float chromaAngle;
    float chromaCenterX;
    float chromaCenterY;
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

// ディザリング用のシンプルな乱数関数
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

float2 PixelToUVOffset(float2 pixelOffset, float2 duvdx, float2 duvdy)
{
    return pixelOffset.x * duvdx + pixelOffset.y * duvdy;
}

float4 main(
    float4 pos : SV_POSITION,
    float4 posScene : SCENE_POSITION,
    float4 uv0 : TEXCOORD0,
    float4 uv1 : TEXCOORD1
) : SV_Target
{
    float2 rayCenterPixel = float2(rayCenterX, rayCenterY);
    float2 chromaCenterPixel = float2(chromaCenterX, chromaCenterY);
    
    // ★ タイル分割バグ回避：ピクセル単位の移動量をUV空間に変換するための偏微分
    float2 duvdx = ddx(uv0.xy);
    float2 duvdy = ddy(uv0.xy);
    
    float safeRayLength = min(max(rayLength, 0.0), 0.99);
    float maxDim = max(texWidth, texHeight);

    float4 sumGlow = float4(0, 0, 0, 0);
    int samples = max(1, (int) raySamples);
    float totalWeight = 0.0;
    
    for (int i = 0; i < samples; i++)
    {
        float2 offsetPixel;
        float weight;
        
        // ★ 放射光のスタイル分岐
        if (rayStyle > 0.5)
        {
            // Directional
            float ratio = (float) i / max(1.0, (float) (samples - 1));
            float offset = (ratio * 2.0 - 1.0);
            
            // ピクセル単位での移動量
            float pixelDist = maxDim * safeRayLength * offset * 0.5;
            offsetPixel = float2(cos(rayAngle), sin(rayAngle)) * pixelDist;
            
            weight = pow(max(1.0 - abs(offset), 0.0), rayFalloff);
        }
        else
        {
            // Radial
            float ratio = (float) i / max(1.0, (float) samples);
            float2 dirPixel = posScene.xy - rayCenterPixel;
            
            // ピクセル単位での移動量
            offsetPixel = dirPixel * (safeRayLength * ratio);
            weight = pow(max(1.0 - ratio, 0.0), rayFalloff);
        }
        
        // ピクセルオフセットを正しくUVオフセットに変換
        float2 currentRayOffsetUV = PixelToUVOffset(offsetPixel, duvdx, duvdy);
        float2 baseUV = uv0.xy - currentRayOffsetUV;

        // ★ 色収差のスタイル分岐
        float2 chromaOffsetR, chromaOffsetG, chromaOffsetB;
        if (chromaStyle > 0.5)
        {
            // Directional: 平行ズレ
            float chromaDist = maxDim * 0.5;
            float2 cDirPixel = float2(cos(chromaAngle), sin(chromaAngle)) * chromaDist;
            
            float2 cDirUV = PixelToUVOffset(cDirPixel, duvdx, duvdy);
            chromaOffsetR = cDirUV * chromaR;
            chromaOffsetG = cDirUV * chromaG;
            chromaOffsetB = cDirUV * chromaB;
        }
        else
        {
            // Radial
            float2 currentPixelPos = posScene.xy - offsetPixel;
            float2 cDirPixel = currentPixelPos - chromaCenterPixel;
            
            float2 cDirUV = PixelToUVOffset(cDirPixel, duvdx, duvdy);
            
            chromaOffsetR = cDirUV * chromaR;
            chromaOffsetG = cDirUV * chromaG;
            chromaOffsetB = cDirUV * chromaB;
        }

        // サンプリング座標の決定
        float2 uvR = baseUV - chromaOffsetR;
        float2 uvG = baseUV - chromaOffsetG;
        float2 uvB = baseUV - chromaOffsetB;

        float r = SampleSafe(InputTexture, InputSampler, uvR).r;
        float g = SampleSafe(InputTexture, InputSampler, uvG).g;
        float b = SampleSafe(InputTexture, InputSampler, uvB).b;
        
        // Alphaは色欠けを防ぐためにRGBの最大値とベースのAを考慮
        float a = SampleSafe(InputTexture, InputSampler, baseUV).a;
        a = max(a, max(r, max(g, b)));
        
        sumGlow += float4(r, g, b, a) * weight;
        totalWeight += weight;
    }
    
    float4 glow = sumGlow / max(totalWeight, 0.0001);

    // 以降、既存の着色・ブレンド処理
    float3 mixInner = innerColor.rgb;
    float3 mixOuter = outerColor.rgb;
    
    if (linearColor > 0.5)
        glow.rgb = SRGBToLinear(glow.rgb);
    if (mixingMode > 0.5)
    {
        mixInner = SRGBToLinear(mixInner);
        mixOuter = SRGBToLinear(mixOuter);
    }

    float luma = GetLuma(glow.rgb);
    luma *= tintScale;
    float t_color = saturate(pow(max(luma, 0.0001), tintGamma));
    
    float3 finalGlowRGB;
    float finalAlpha;

    if (colorize > 0.5)
    {
        finalGlowRGB = lerp(mixOuter, mixInner, t_color) * min(luma * 2.0, 1.0);
        finalAlpha = saturate(glow.a * tintScale);
    }
    else
    {
        finalGlowRGB = glow.rgb * mixOuter;
        finalAlpha = glow.a * outerColor.a;
    }

    finalGlowRGB *= exposure;
    
    // --- 合成処理 ---
    float4 source = SampleSafe(SourceTexture, InputSampler, uv1.xy);
    source.a *= sourceOpacity;
    source.rgb *= sourceOpacity;

    if (linearColor > 0.5)
        source.rgb = SRGBToLinear(source.rgb);

    float3 finalRGB = source.rgb + finalGlowRGB;
    float finalA = saturate(source.a + finalAlpha);

    if (linearColor > 0.5)
        finalRGB = LinearToSRGB(finalRGB);

    // ディザリング (バンディング・等高線ノイズの防止)
    float noise = (rand(uv0.xy) - 0.5) / 255.0;
    finalRGB += noise;

    return saturate(float4(finalRGB, finalA));
}