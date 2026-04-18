using Glowplus;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Windows;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace Glowplus
{
    internal class GlowplusEffectProcessor : IVideoEffectProcessor, IDisposable
    {
        private readonly GlowplusEffect item;
        private readonly IGraphicsDevicesAndContext devices;

        private ID2D1Image? input;

        // --- エフェクトのインスタンス ---
        private ColorMatrix? _thresholdEffect;
        private Saturation? _saturationEffect;
        private Scale? _finalUpscaler;
        private Crop? _initialCrop;
        private Crop? _finalSourceCrop;
        private Crop? _finalGlowCrop;
        private GlowplusCustomEffect? _deepGlowEffect;
        private Crop? _preBlurCrop;

        // 端っこ引き延ばし修正用のパディング
        private Flood? _padFlood;
        private Composite? _padComposite;

        private readonly List<PyramidLevel> _pyramidLevels = new();
        private ID2D1Image? _lastOutput;
        public ID2D1Image Output => _lastOutput!;

        // プロジェクト解像度管理
        private float projectWidth = 1920f;
        private float projectHeight = 1080f;
        private bool isProjectInfoFetched = false;

        public GlowplusEffectProcessor(IGraphicsDevicesAndContext devices, GlowplusEffect item)
        {
            this.devices = devices;
            this.item = item;
        }

        public void SetInput(ID2D1Image? input) { this.input = input; }

        public DrawDescription Update(EffectDescription effectDescription)
        {
            var dc = devices.DeviceContext;

            // --- 0. メモリ掃除 ---
            foreach (var level in _pyramidLevels) level.ClearCache();
            if (_lastOutput != null) { _lastOutput.Dispose(); _lastOutput = null; }

            if (this.input == null) return effectDescription.DrawDescription;

            // --- 1. プロジェクト情報の取得 (初回のみ) ---
            if (!isProjectInfoFetched)
            {
                UpdateProjectInfo();
                isProjectInfoFetched = true;
            }

            var frame = effectDescription.ItemPosition.Frame;
            var length = effectDescription.ItemDuration.Frame;
            var fps = effectDescription.FPS;

            // --- パラメータ取得 ---
            float exposure = (float)item.Exposure.GetValue(frame, length, fps);
            float threshold = (float)item.Threshold.GetValue(frame, length, fps) / 100.0f;
            float contrast = (float)item.Contrast.GetValue(frame, length, fps) / 100.0f;
            int steps = Math.Clamp((int)item.Steps.GetValue(frame, length, fps), 1, 10);
            float blurRadius = (float)item.BlurRadius.GetValue(frame, length, fps);
            float sizeX = (float)item.SizeX.GetValue(frame, length, fps) / 100.0f;
            float sizeY = (float)item.SizeY.GetValue(frame, length, fps) / 100.0f;
            float vibrance = (float)item.Vibrance.GetValue(frame, length, fps) / 100.0f;
            float tintScale = (float)item.TintScale.GetValue(frame, length, fps);
            float tintGamma = (float)item.TintGamma.GetValue(frame, length, fps);
            bool colorize = item.Colorize;
            var globalGlowColor = item.GlowColor;
            var innerColor = item.InnerTint;
            var outerColor = item.OuterTint;
            bool showSource = item.ShowSource;
            float mixingMode = (float)item.MixingMode;
            float sourceOpacity = (float)item.SourceOpacity.GetValue(frame, length, fps) / 100.0f;
            bool linear = item.LinearColor;
            var currentQualityMode = (int)item.QualityMode;
            bool autoClipping = item.AutoClipping;
            bool fixToOriginalSize = item.FixToOriginalSize;

            // ★新規取得パラメータ群
            float chromaStyle = item.ChromaStyle == GlowplusEffect.ChromaStyleMode.Directional ? 1.0f : 0.0f;
            float chromaAngle = (float)item.ChromaAngle.GetValue(frame, length, fps) * (float)Math.PI / 180.0f;
            float chromaCenterX = (float)item.ChromaCenterX.GetValue(frame, length, fps);
            float chromaCenterY = (float)item.ChromaCenterY.GetValue(frame, length, fps);
            float chromaR = (float)item.ChromaR.GetValue(frame, length, fps) / 100.0f;
            float chromaG = (float)item.ChromaG.GetValue(frame, length, fps) / 100.0f;
            float chromaB = (float)item.ChromaB.GetValue(frame, length, fps) / 100.0f;

            float rayStyle = item.RayStyle == GlowplusEffect.RayStyleMode.Directional ? 1.0f : 0.0f;
            float rayAngle = (float)item.RayAngle.GetValue(frame, length, fps) * (float)Math.PI / 180.0f;
            float rayLength = (float)item.RayLength.GetValue(frame, length, fps);
            float rayFalloff = (float)item.RayFalloff.GetValue(frame, length, fps);
            float rayCenterX = (float)item.RayCenterX.GetValue(frame, length, fps);
            float rayCenterY = (float)item.RayCenterY.GetValue(frame, length, fps);
            int raySamples = Math.Clamp((int)item.RaySamples.GetValue(frame, length, fps), 1, 256);

            // --- 2. インスタンス生成 ---
            try
            {
                _thresholdEffect ??= new ColorMatrix(dc);
                _deepGlowEffect ??= new GlowplusCustomEffect(devices);
                _initialCrop ??= new Crop(dc);
                _finalSourceCrop ??= new Crop(dc);
                _finalGlowCrop ??= new Crop(dc);
                _preBlurCrop ??= new Crop(dc);

                _padFlood ??= new Flood(dc) { Color = new Vector4(0f, 0f, 0f, 0f) };
                _padComposite ??= new Composite(dc);
            }
            catch
            {
                Dispose();
                return effectDescription.DrawDescription;
            }

            // --- 3. 座標計算とクリッピング判定 ---
            var bounds = dc.GetImageLocalBounds(this.input);

            // ① 無限大の場合はプロジェクトサイズにフォールバック（背景アイテム等への対応・既存のまま）
            if (float.IsInfinity(bounds.Left) || float.IsInfinity(bounds.Right) || Math.Abs(bounds.Right - bounds.Left) > 500000)
            {
                float halfW = projectWidth / 2.0f;
                float halfH = projectHeight / 2.0f;
                bounds = new Vortice.RawRectF(-halfW, -halfH, halfW, halfH);
            }

            // ★② 新規追加：バグ回避（アイテムサイズが0、つまり何も描画されていない場合）
            if (float.IsNaN(bounds.Left) || (bounds.Right - bounds.Left) <= 0.1f || (bounds.Bottom - bounds.Top) <= 0.1f)
            {
                // エフェクトをかけずに、安全に入力をパススルーして終わる
                using (var floodOut = _padFlood.Output)
                {
                    _padComposite.SetInput(0, floodOut, true);
                }
                _padComposite.SetInput(1, this.input, true);
                _lastOutput = _padComposite.Output;
                return effectDescription.DrawDescription;
            }

            // 光の広がり計算
            float baseBlurSigma = Math.Clamp(blurRadius, 0.1f, 200.0f);
            float maxScaleFactor = (float)Math.Pow(2, steps - 1);
            float maxSpread = baseBlurSigma * 6.0f * maxScaleFactor * Math.Max(sizeX, sizeY);

            // 放射光のための安全マージンも追加
            float safePadding = maxSpread + 50.0f;
            if (rayLength > 0) safePadding += Math.Max(projectWidth, projectHeight) * rayLength * 0.5f;

            float centerX = (bounds.Left + bounds.Right) / 2.0f;
            float centerY = (bounds.Top + bounds.Bottom) / 2.0f;
            float inputHalfW = (bounds.Right - bounds.Left) / 2.0f;
            float inputHalfH = (bounds.Bottom - bounds.Top) / 2.0f;

            float finalHalfWidth, finalHalfHeight;
            float maxTextureSize;

            // ★ ハードウェア限界を考慮した絶対的な最大サイズ (直径が12000程度に収まるように)
            float ABSOLUTE_MAX_HALF_DIM = 6000.0f;

            if (fixToOriginalSize)
            {
                finalHalfWidth = inputHalfW;
                finalHalfHeight = inputHalfH;
                maxTextureSize = 8192.0f;
            }
            else if (autoClipping)
            {
                float maxAllowedDimension = Math.Max(projectWidth, projectHeight) * 1.5f;
                finalHalfWidth = Math.Min(inputHalfW + safePadding, maxAllowedDimension / 2.0f);
                finalHalfHeight = Math.Min(inputHalfH + safePadding, maxAllowedDimension / 2.0f);
                if (currentQualityMode == 0) maxTextureSize = 512f;
                else if (currentQualityMode == 2) maxTextureSize = 2048.0f;
                else maxTextureSize = 1024.0f;
            }
            else
            {
                finalHalfWidth = Math.Min(inputHalfW + safePadding, ABSOLUTE_MAX_HALF_DIM);
                finalHalfHeight = Math.Min(inputHalfH + safePadding, ABSOLUTE_MAX_HALF_DIM);
                maxTextureSize = 8192.0f;
            }

            var safeRect = new Vector4(
                centerX - finalHalfWidth,
                centerY - finalHalfHeight,
                centerX + finalHalfWidth,
                centerY + finalHalfHeight
            );

            // --- UV座標上の中心位置計算 ---
            float safeWidth = safeRect.Z - safeRect.X;
            float safeHeight = safeRect.W - safeRect.Y;
            float normalizedCenterX = 0.5f + (rayCenterX / Math.Max(1.0f, safeWidth));
            float normalizedCenterY = 0.5f + (rayCenterY / Math.Max(1.0f, safeHeight));
            float rayCenterX_Pixel = centerX + rayCenterX;
            float rayCenterY_Pixel = centerY + rayCenterY;
            float chromaCenterX_Pixel = centerX + chromaCenterX;
            float chromaCenterY_Pixel = centerY + chromaCenterY;

            // --- 4. 解像度リミッター ---
            float currentW = safeWidth;
            float currentH = safeHeight;
            float maxDim = Math.Max(currentW, currentH);

            float autoDownscale = 1.0f;
            if (maxDim > maxTextureSize) autoDownscale = maxTextureSize / Math.Max(1.0f, maxDim);

            float renderScale = 1.0f;
            if (currentQualityMode == 0) renderScale = 0.5f;
            else if (currentQualityMode == 1) renderScale = 1.0f;

            renderScale = Math.Min(renderScale, autoDownscale);
            renderScale = Math.Max(renderScale, 0.05f);

            // ★ メモリリーク修正：入力画像の透明パディング
            using (var floodOut = _padFlood.Output)
            {
                _padComposite.SetInput(0, floodOut, true);
            }
            _padComposite.SetInput(1, this.input, true);
            ID2D1Image paddedInput = _padComposite.Output;

            // --- 5. 前処理パイプライン ---
            _initialCrop.Rectangle = safeRect;
            _initialCrop.SetInput(0, paddedInput, true);
            ID2D1Image processingImage = _initialCrop.Output;
            bool processingImageIsOwned = true;

            // Threshold
            float denom = Math.Max(0.0001f, 1.0f - threshold);
            float a = (1.0f + contrast) / denom;
            float b = ((-threshold * (1.0f + contrast)) / denom) - contrast;

            _thresholdEffect.Matrix = new Matrix5x4
            {
                M11 = a,
                M22 = a,
                M33 = a,
                M44 = 1,
                M51 = b,
                M52 = b,
                M53 = b
            };

            _thresholdEffect.SetInput(0, processingImage, true);
            var thresholdOut = _thresholdEffect.Output;
            if (processingImageIsOwned) processingImage.Dispose();
            processingImage = thresholdOut;
            processingImageIsOwned = true;

            // Vibrance
            if (vibrance > 0)
            {
                _saturationEffect ??= new Saturation(dc);
                _saturationEffect.SetValue((int)SaturationProperties.Saturation, 1.0f + vibrance);
                _saturationEffect.SetInput(0, processingImage, true);
                var satOut = _saturationEffect.Output;
                if (processingImageIsOwned) processingImage.Dispose();
                processingImage = satOut;
                processingImageIsOwned = true;
            }

            // --- 6. ブラーピラミッド ---
            EnsurePyramidLevels(dc, steps);

            if (renderScale < 0.99f)
            {
                var level0 = _pyramidLevels[0];
                level0.Downscaler.SetValue((int)ScaleProperties.Scale, new Vector2(renderScale, renderScale));
                level0.Downscaler.SetInput(0, processingImage, true);

                var preRect = new Vector4(
                    safeRect.X * renderScale, safeRect.Y * renderScale,
                    safeRect.Z * renderScale, safeRect.W * renderScale
                );
                _preBlurCrop.Rectangle = preRect;
                using (var downOut = level0.Downscaler.Output)
                {
                    _preBlurCrop.SetInput(0, downOut, true);
                }
                var preOut = _preBlurCrop.Output;
                if (processingImageIsOwned) processingImage.Dispose();
                processingImage = preOut;
                processingImageIsOwned = true;
            }

            float scaledSigma = baseBlurSigma * renderScale;

            for (int i = 0; i < steps; i++)
            {
                var level = _pyramidLevels[i];

                if (i > 0)
                {
                    level.Downscaler.SetValue((int)ScaleProperties.Scale, new Vector2(0.5f, 0.5f));
                    level.Downscaler.SetInput(0, processingImage, true);
                    var downOut = level.Downscaler.Output;
                    if (processingImageIsOwned) processingImage.Dispose();
                    processingImage = downOut;
                    processingImageIsOwned = true;
                }

                if (sizeX > 0.01f)
                {
                    level.BlurX.StandardDeviation = scaledSigma * sizeX;
                    level.BlurX.SetInput(0, processingImage, true);
                    var blurX = level.BlurX.Output;
                    if (processingImageIsOwned) processingImage.Dispose();
                    processingImage = blurX;
                    processingImageIsOwned = true;
                }

                if (sizeY > 0.01f)
                {
                    level.BlurY.StandardDeviation = scaledSigma * sizeY;
                    level.BlurY.SetInput(0, processingImage, true);
                    var blurY = level.BlurY.Output;
                    if (processingImageIsOwned) processingImage.Dispose();
                    processingImage = blurY;
                    processingImageIsOwned = true;
                }

                float currentLevelScale = renderScale * (float)Math.Pow(0.5, i);
                var levelRect = new Vector4(
                    safeRect.X * currentLevelScale,
                    safeRect.Y * currentLevelScale,
                    safeRect.Z * currentLevelScale,
                    safeRect.W * currentLevelScale
                );
                level.Cropper.Rectangle = levelRect;
                level.Cropper.SetInput(0, processingImage, true);

                level.OutputCache = level.Cropper.Output;

                if (processingImageIsOwned) processingImage.Dispose();
                processingImage = level.OutputCache;
                processingImageIsOwned = false;
            }

            ID2D1Image accumulated = _pyramidLevels[steps - 1].OutputCache!;
            bool accumulatedIsOwned = false;

            for (int i = steps - 2; i >= 0; i--)
            {
                var level = _pyramidLevels[i];
                level.Upscaler.SetValue((int)ScaleProperties.Scale, new Vector2(2.0f, 2.0f));
                level.Upscaler.SetInput(0, accumulated, true);

                using (var upOut = level.Upscaler.Output)
                {
                    level.Blender.SetInput(0, level.OutputCache, true);
                    level.Blender.SetInput(1, upOut, true);
                }
                var newAcc = level.Blender.Output;
                if (accumulatedIsOwned) accumulated.Dispose();
                accumulated = newAcc;
                accumulatedIsOwned = true;
            }

            ID2D1Image finalGlowImage = accumulated;
            bool finalGlowIsOwned = accumulatedIsOwned;

            if (renderScale < 0.99f)
            {
                _finalUpscaler ??= new Scale(dc);
                _finalUpscaler.SetValue((int)ScaleProperties.Scale, new Vector2(1.0f / renderScale, 1.0f / renderScale));
                _finalUpscaler.SetInput(0, accumulated, true);
                var upscaled = _finalUpscaler.Output;
                if (finalGlowIsOwned) accumulated.Dispose();
                finalGlowImage = upscaled;
                finalGlowIsOwned = true;
            }

            _finalGlowCrop.Rectangle = safeRect;
            _finalGlowCrop.SetInput(0, finalGlowImage, true);
            var glowCropped = _finalGlowCrop.Output;
            if (finalGlowIsOwned) finalGlowImage.Dispose();

            _finalSourceCrop.Rectangle = safeRect;
            _finalSourceCrop.SetInput(0, paddedInput, true);
            var sourceCropped = _finalSourceCrop.Output;

            // --- シェーダーへのパラメータ適用 ---
            _deepGlowEffect.SetInput(0, glowCropped, true);
            _deepGlowEffect.SetInput(1, sourceCropped, true);

            _deepGlowEffect.Colorize = colorize;
            _deepGlowEffect.MixingMode = mixingMode;
            _deepGlowEffect.Exposure = exposure;
            _deepGlowEffect.SourceOpacity = showSource ? sourceOpacity : 0.0f;
            _deepGlowEffect.LinearColor = linear;
            _deepGlowEffect.TintScale = tintScale;

            // ★パラメータの適用
            _deepGlowEffect.ChromaR = chromaR;
            _deepGlowEffect.ChromaG = chromaG;
            _deepGlowEffect.ChromaB = chromaB;
            _deepGlowEffect.ChromaStyle = chromaStyle;
            _deepGlowEffect.ChromaAngle = chromaAngle;
            _deepGlowEffect.ChromaCenterX = chromaCenterX_Pixel;
            _deepGlowEffect.ChromaCenterY = chromaCenterY_Pixel;

            _deepGlowEffect.RayLength = rayLength;
            _deepGlowEffect.RayCenterX = rayCenterX_Pixel;
            _deepGlowEffect.RayCenterY = rayCenterY_Pixel;
            _deepGlowEffect.RaySamples = (float)raySamples;
            _deepGlowEffect.RayFalloff = rayFalloff;
            _deepGlowEffect.RayStyle = rayStyle;
            _deepGlowEffect.RayAngle = rayAngle;

            _deepGlowEffect.TexWidth = Math.Max(1.0f, safeRect.Z - safeRect.X);
            _deepGlowEffect.TexHeight = Math.Max(1.0f, safeRect.W - safeRect.Y);


            if (!colorize)
            {
                var c = new Vector4(globalGlowColor.R / 255f, globalGlowColor.G / 255f, globalGlowColor.B / 255f, globalGlowColor.A / 255f);
                _deepGlowEffect.OuterColor = c;
                _deepGlowEffect.InnerColor = c;
                _deepGlowEffect.TintGamma = 1.0f;
            }
            else
            {
                _deepGlowEffect.InnerColor = new Vector4(innerColor.R / 255f, innerColor.G / 255f, innerColor.B / 255f, innerColor.A / 255f);
                _deepGlowEffect.OuterColor = new Vector4(outerColor.R / 255f, outerColor.G / 255f, outerColor.B / 255f, outerColor.A / 255f);
                _deepGlowEffect.TintGamma = Math.Max(0.1f, 1.0f / Math.Max(0.1f, tintGamma));
            }

            _lastOutput = _deepGlowEffect.Output;

            glowCropped.Dispose();
            sourceCropped.Dispose();
            paddedInput.Dispose();

            return effectDescription.DrawDescription;
        }

        private void UpdateProjectInfo()
        {
            if (Application.Current == null) return;
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var window = GetYmmMainWindow();
                    if (window == null) return;

                    dynamic? mainVM = window.DataContext;
                    dynamic? statusBarVM = GetProp(mainVM, "StatusBarViewModel");
                    dynamic? statusBarVal = GetProp(statusBarVM, "Value");
                    dynamic? videoInfoProp = GetProp(statusBarVal, "VideoInfo");
                    string? videoInfoString = GetProp(videoInfoProp, "Value");

                    if (!string.IsNullOrEmpty(videoInfoString))
                    {
                        var parts = videoInfoString.Split(' ');
                        if (parts.Length > 0)
                        {
                            var resParts = parts[0].Split('x');
                            if (resParts.Length >= 2)
                            {
                                if (float.TryParse(resParts[0], out float w) && float.TryParse(resParts[1], out float h))
                                {
                                    this.projectWidth = w;
                                    this.projectHeight = h;
                                }
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Project Info Fetch Error: " + ex.Message);
            }
        }

        static Window? GetYmmMainWindow()
        {
            if (Application.Current == null) return null;
            foreach (Window w in Application.Current.Windows)
            {
                if (w.GetType().FullName == "YukkuriMovieMaker.Views.MainView") return w;
            }
            return null;
        }

        static dynamic? GetProp(dynamic obj, string propName)
        {
            if (obj == null) return null;
            Type type = obj.GetType();
            PropertyInfo? info = type.GetProperty(propName);
            return info?.GetValue(obj);
        }

        private void EnsurePyramidLevels(ID2D1DeviceContext dc, int steps)
        {
            while (_pyramidLevels.Count < steps) _pyramidLevels.Add(new PyramidLevel(dc));
        }

        public void ClearInput() { this.input = null; }

        public void Dispose()
        {
            // （既存のDispose群）
            _thresholdEffect?.SetInput(0, null, true); _thresholdEffect?.Dispose(); _thresholdEffect = null;
            _saturationEffect?.SetInput(0, null, true); _saturationEffect?.Dispose(); _saturationEffect = null;
            _finalUpscaler?.SetInput(0, null, true); _finalUpscaler?.Dispose(); _finalUpscaler = null;
            _initialCrop?.SetInput(0, null, true); _initialCrop?.Dispose(); _initialCrop = null;
            _finalSourceCrop?.SetInput(0, null, true); _finalSourceCrop?.Dispose(); _finalSourceCrop = null;
            _finalGlowCrop?.SetInput(0, null, true); _finalGlowCrop?.Dispose(); _finalGlowCrop = null;
            _deepGlowEffect?.SetInput(0, null, true); _deepGlowEffect?.SetInput(1, null, true); _deepGlowEffect?.Dispose(); _deepGlowEffect = null;
            _preBlurCrop?.SetInput(0, null, true); _preBlurCrop?.Dispose(); _preBlurCrop = null;

            _padFlood?.Dispose(); _padFlood = null;
            _padComposite?.SetInput(0, null, true); _padComposite?.SetInput(1, null, true); _padComposite?.Dispose(); _padComposite = null;

            _lastOutput?.Dispose(); _lastOutput = null;
            foreach (var level in _pyramidLevels) level.Dispose();
            _pyramidLevels.Clear();
            this.input = null;
        }

        private class PyramidLevel : IDisposable
        {
            // （既存のまま）
            public Scale Downscaler;
            public DirectionalBlur BlurX;
            public DirectionalBlur BlurY;
            public Scale Upscaler;
            public Vortice.Direct2D1.Effects.Blend Blender;
            public Crop Cropper;
            public ID2D1Image? OutputCache;

            public PyramidLevel(ID2D1DeviceContext dc)
            {
                Downscaler = new Scale(dc); Downscaler.SetValue((int)ScaleProperties.InterpolationMode, InterpolationMode.Linear);
                BlurX = new DirectionalBlur(dc); BlurX.Optimization = DirectionalBlurOptimization.Speed; BlurX.BorderMode = BorderMode.Soft; BlurX.Angle = 0f;
                BlurY = new DirectionalBlur(dc); BlurY.Optimization = DirectionalBlurOptimization.Speed; BlurY.BorderMode = BorderMode.Soft; BlurY.Angle = 90f;
                Upscaler = new Scale(dc); Upscaler.SetValue((int)ScaleProperties.InterpolationMode, InterpolationMode.Linear);
                Blender = new Vortice.Direct2D1.Effects.Blend(dc); Blender.Mode = BlendMode.LinearDodge;
                Cropper = new Crop(dc);
            }
            public void ClearCache() { OutputCache?.Dispose(); OutputCache = null; }
            public void Dispose()
            {
                Downscaler.SetInput(0, null, true); Downscaler.Dispose();
                BlurX.SetInput(0, null, true); BlurX.Dispose();
                BlurY.SetInput(0, null, true); BlurY.Dispose();
                Upscaler.SetInput(0, null, true); Upscaler.Dispose();
                Blender.SetInput(0, null, true); Blender.SetInput(1, null, true); Blender.Dispose();
                Cropper.SetInput(0, null, true); Cropper.Dispose();
                OutputCache?.Dispose();
            }
        }
    }
}