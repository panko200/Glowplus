using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using Vortice;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace Glowplus
{
    internal class GlowplusCustomEffect : D2D1CustomShaderEffectBase
    {
        public Vector4 InnerColor { set => SetValue((int)Props.InnerColor, value); }
        public Vector4 OuterColor { set => SetValue((int)Props.OuterColor, value); }
        public float TintScale { set => SetValue((int)Props.TintScale, value); }
        public float TintGamma { set => SetValue((int)Props.TintGamma, value); }
        public float Exposure { set => SetValue((int)Props.Exposure, value); }
        public float SourceOpacity { set => SetValue((int)Props.SourceOpacity, value); }
        public bool Colorize { set => SetValue((int)Props.Colorize, value ? 1.0f : 0.0f); }
        public float Threshold { set => SetValue((int)Props.Threshold, value); }
        public float MixingMode { set => SetValue((int)Props.MixingMode, value); }
        public bool LinearColor { set => SetValue((int)Props.LinearColor, value ? 1.0f : 0.0f); }

        public float ChromaR { set => SetValue((int)Props.ChromaR, value); }
        public float ChromaG { set => SetValue((int)Props.ChromaG, value); }
        public float ChromaB { set => SetValue((int)Props.ChromaB, value); }
        public float RayLength { set => SetValue((int)Props.RayLength, value); }
        public float RayCenterX { set => SetValue((int)Props.RayCenterX, value); }
        public float RayCenterY { set => SetValue((int)Props.RayCenterY, value); }
        public float RaySamples { set => SetValue((int)Props.RaySamples, value); }
        public float TexWidth { set => SetValue((int)Props.TexWidth, value); }

        public float TexHeight { set => SetValue((int)Props.TexHeight, value); }
        public float RayFalloff { set => SetValue((int)Props.RayFalloff, value); }
        public float RayStyle { set => SetValue((int)Props.RayStyle, value); }
        public float RayAngle { set => SetValue((int)Props.RayAngle, value); }
        public float ChromaStyle { set => SetValue((int)Props.ChromaStyle, value); }
        public float ChromaAngle { set => SetValue((int)Props.ChromaAngle, value); }

        // ★新規：色収差の中心
        public float ChromaCenterX { set => SetValue((int)Props.ChromaCenterX, value); }
        public float ChromaCenterY { set => SetValue((int)Props.ChromaCenterY, value); }

        public GlowplusCustomEffect(IGraphicsDevicesAndContext devices) : base(Create<EffectImpl>(devices)) { }

        [StructLayout(LayoutKind.Sequential)]
        struct ConstantBuffer
        {
            public Vector4 InnerColor;      // 16
            public Vector4 OuterColor;      // 16

            public float TintScale;         // 4
            public float TintGamma;         // 4
            public float Exposure;          // 4
            public float SourceOpacity;     // 4 -> 16

            public float Colorize;          // 4
            public float Threshold;         // 4
            public float MixingMode;        // 4
            public float LinearColor;       // 4 -> 16

            public float ChromaR;           // 4
            public float ChromaG;           // 4
            public float ChromaB;           // 4
            public float RayLength;         // 4 -> 16

            public float RayCenterX;        // 4
            public float RayCenterY;        // 4
            public float RaySamples;        // 4
            public float TexWidth;          // 4 -> 16

            public float TexHeight;         // 4
            public float RayFalloff;        // 4
            public float RayStyle;          // 4
            public float RayAngle;          // 4 -> 16

            public float ChromaStyle;       // 4
            public float ChromaAngle;       // 4
            public float ChromaCenterX;     // 4
            public float ChromaCenterY;     // 4 -> 16
        }

        private enum Props
        {
            InnerColor, OuterColor, TintScale, TintGamma, Exposure, SourceOpacity, Colorize, Threshold, MixingMode, LinearColor,
            ChromaR, ChromaG, ChromaB, RayLength, RayCenterX, RayCenterY, RaySamples, TexWidth, TexHeight,
            RayFalloff, RayStyle, RayAngle, ChromaStyle, ChromaAngle, ChromaCenterX, ChromaCenterY
        }

        [CustomEffect(2)]
        private class EffectImpl : D2D1CustomShaderEffectImplBase<EffectImpl>
        {
            private ConstantBuffer constants;

            protected override void UpdateConstants()
            {
                if (drawInformation != null) drawInformation.SetPixelShaderConstantBuffer(constants);
            }

            public override void MapInputRectsToOutputRect(RawRect[] inputRects, RawRect[] inputOpaqueSubRects, out RawRect outputRect, out RawRect outputOpaqueSubRect)
            {
                if (inputRects.Length > 0) outputRect = inputRects[0];
                else outputRect = new RawRect();
                outputOpaqueSubRect = new RawRect();
            }

            public override void MapOutputRectToInputRects(RawRect outputRect, RawRect[] inputRects)
            {
                float maxChroma = Math.Max(Math.Abs(constants.ChromaR), Math.Max(Math.Abs(constants.ChromaG), Math.Abs(constants.ChromaB)));
                float maxDim = Math.Max(constants.TexWidth, constants.TexHeight);
                if (maxDim < 1.0f) maxDim = 1920.0f; // セーフガード

                // 画像サイズを基準に必要なサンプリング範囲を確保
                int margin = (int)(maxDim * constants.RayLength * 1.5f + maxDim * maxChroma * 1.5f) + 100;

                // 広がりすぎによるエラーを防ぐため、マージン上限を設ける
                margin = Math.Min(margin, 8000);

                var expandedRect = new RawRect(
                    outputRect.Left - margin, outputRect.Top - margin,
                    outputRect.Right + margin, outputRect.Bottom + margin
                );

                if (inputRects.Length > 0) inputRects[0] = expandedRect;
                if (inputRects.Length > 1) inputRects[1] = expandedRect;
            }

            private static byte[] LoadShader()
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("Glowplus.Shaders.GlowplusShader.cso");
                if (stream == null) throw new FileNotFoundException("GlowplusShader.cso not found");
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }

            public EffectImpl() : base(LoadShader())
            {
                constants = new ConstantBuffer
                {
                    TintScale = 1.0f,
                    TintGamma = 1.0f,
                    Exposure = 1.0f,
                    SourceOpacity = 1.0f,
                    LinearColor = 0.0f,
                    ChromaR = 0f,
                    ChromaG = 0f,
                    ChromaB = 0f,
                    RayLength = 0f,
                    RayCenterX = 0f,
                    RayCenterY = 0f,
                    RaySamples = 8f,
                    TexWidth = 1920f,
                    TexHeight = 1080f,
                    RayFalloff = 1.0f,
                    RayStyle = 0f,
                    RayAngle = 0f,
                    ChromaStyle = 0f,
                    ChromaAngle = 0f,
                    ChromaCenterX = 0f,
                    ChromaCenterY = 0f
                };
            }

            [CustomEffectProperty(PropertyType.Vector4, (int)Props.InnerColor)] public Vector4 InnerColor { get => constants.InnerColor; set { constants.InnerColor = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Vector4, (int)Props.OuterColor)] public Vector4 OuterColor { get => constants.OuterColor; set { constants.OuterColor = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.TintScale)] public float TintScale { get => constants.TintScale; set { constants.TintScale = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.TintGamma)] public float TintGamma { get => constants.TintGamma; set { constants.TintGamma = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.Exposure)] public float Exposure { get => constants.Exposure; set { constants.Exposure = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.SourceOpacity)] public float SourceOpacity { get => constants.SourceOpacity; set { constants.SourceOpacity = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.Colorize)] public float Colorize { get => constants.Colorize; set { constants.Colorize = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.Threshold)] public float Threshold { get => constants.Threshold; set { constants.Threshold = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.MixingMode)] public float MixingMode { get => constants.MixingMode; set { constants.MixingMode = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.LinearColor)] public float LinearColor { get => constants.LinearColor; set { constants.LinearColor = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.ChromaR)] public float ChromaR { get => constants.ChromaR; set { constants.ChromaR = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.ChromaG)] public float ChromaG { get => constants.ChromaG; set { constants.ChromaG = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.ChromaB)] public float ChromaB { get => constants.ChromaB; set { constants.ChromaB = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.RayLength)] public float RayLength { get => constants.RayLength; set { constants.RayLength = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.RayCenterX)] public float RayCenterX { get => constants.RayCenterX; set { constants.RayCenterX = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.RayCenterY)] public float RayCenterY { get => constants.RayCenterY; set { constants.RayCenterY = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.RaySamples)] public float RaySamples { get => constants.RaySamples; set { constants.RaySamples = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.TexWidth)] public float TexWidth { get => constants.TexWidth; set { constants.TexWidth = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.TexHeight)] public float TexHeight { get => constants.TexHeight; set { constants.TexHeight = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.RayFalloff)] public float RayFalloff { get => constants.RayFalloff; set { constants.RayFalloff = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.RayStyle)] public float RayStyle { get => constants.RayStyle; set { constants.RayStyle = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.RayAngle)] public float RayAngle { get => constants.RayAngle; set { constants.RayAngle = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.ChromaStyle)] public float ChromaStyle { get => constants.ChromaStyle; set { constants.ChromaStyle = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.ChromaAngle)] public float ChromaAngle { get => constants.ChromaAngle; set { constants.ChromaAngle = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.ChromaCenterX)] public float ChromaCenterX { get => constants.ChromaCenterX; set { constants.ChromaCenterX = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.ChromaCenterY)] public float ChromaCenterY { get => constants.ChromaCenterY; set { constants.ChromaCenterY = value; UpdateConstants(); } }
        }
    }
}