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
        // プロパティ
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

        public float RayLength { set => SetValue((int)Props.RayLength, value); }
        public float RayDecay { set => SetValue((int)Props.RayDecay, value); }
        public float RayDensity { set => SetValue((int)Props.RayDensity, value); }
        public Vector2 Center { set => SetValue((int)Props.Center, value); }

        public Vector3 RGBScales { set => SetValue((int)Props.RGBScales, value); }

        public GlowplusCustomEffect(IGraphicsDevicesAndContext devices) : base(Create<EffectImpl>(devices)) { }

        [StructLayout(LayoutKind.Sequential)]
        struct ConstantBuffer
        {
            public Vector4 InnerColor;  // 16
            public Vector4 OuterColor;  // 16
            public float TintScale;     // 4
            public float TintGamma;     // 4
            public float Exposure;      // 4
            public float SourceOpacity; // 4 -> 合計16
            public float Colorize;      // 4
            public float Threshold;     // 4
            public float RayLength;     // 4
            public float RayDecay;      // 4 -> 合計16
            public float RayDensity;    // 4
            public Vector2 Center;      // 8
            public float MixingMode;    // 4 -> 合計16
            public float LinearColor;   // 4
            public Vector3 RGBScales;   // 12 -> 合計16 
        }

        private enum Props
        {
            InnerColor, OuterColor, TintScale, TintGamma, Exposure, SourceOpacity, Colorize, Threshold, RayLength, RayDecay, RayDensity, Center, MixingMode, LinearColor, RGBScales
        }

        [CustomEffect(2)] // InputCount = 2
        private class EffectImpl : D2D1CustomShaderEffectImplBase<EffectImpl>
        {
            private ConstantBuffer constants;

            protected override void UpdateConstants()
            {
                if (drawInformation != null)
                {
                    drawInformation.SetPixelShaderConstantBuffer(constants);
                }
            }

            // ★★★ ここが復活！最重要ポイント ★★★
            // これがないとDirect2Dが画像の結合範囲を勝手に計算し、座標がズレて「I HATE HLSL」状態になります。

            // 「出力画像のサイズ」を決めるメソッド
            public override void MapInputRectsToOutputRect(RawRect[] inputRects, RawRect[] inputOpaqueSubRects, out RawRect outputRect, out RawRect outputOpaqueSubRect)
            {
                // Input0（グロー画像）のサイズをそのまま出力サイズとして採用します。
                if (inputRects.Length > 0)
                {
                    outputRect = inputRects[0];
                }
                else
                {
                    outputRect = new RawRect();
                }
                outputOpaqueSubRect = new RawRect();
            }

            // 「入力画像の必要な範囲」を決めるメソッド
            public override void MapOutputRectToInputRects(RawRect outputRect, RawRect[] inputRects)
            {
                // Input0もInput1も、出力と同じ範囲が必要だと明示します。
                // これにより、サイズ違いの画像でも強制的に座標が揃います。
                if (inputRects.Length > 0) inputRects[0] = outputRect;
                if (inputRects.Length > 1) inputRects[1] = outputRect;
            }
            // ★★★★★★★★★★★★★★★★★★★★★

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
                    RayDecay = 0.95f,
                    RayDensity = 1.0f,
                    Center = new Vector2(0.5f, 0.5f),
                    LinearColor = 0.0f
                };
            }

            [CustomEffectProperty(PropertyType.Vector4, (int)Props.InnerColor)]
            public Vector4 InnerColor { get => constants.InnerColor; set { constants.InnerColor = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Vector4, (int)Props.OuterColor)]
            public Vector4 OuterColor { get => constants.OuterColor; set { constants.OuterColor = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.TintScale)]
            public float TintScale { get => constants.TintScale; set { constants.TintScale = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.TintGamma)]
            public float TintGamma { get => constants.TintGamma; set { constants.TintGamma = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.Exposure)]
            public float Exposure { get => constants.Exposure; set { constants.Exposure = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.SourceOpacity)]
            public float SourceOpacity { get => constants.SourceOpacity; set { constants.SourceOpacity = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.Colorize)]
            public float Colorize { get => constants.Colorize; set { constants.Colorize = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.Threshold)]
            public float Threshold { get => constants.Threshold; set { constants.Threshold = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.RayLength)]
            public float RayLength { get => constants.RayLength; set { constants.RayLength = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.RayDecay)]
            public float RayDecay { get => constants.RayDecay; set { constants.RayDecay = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.RayDensity)]
            public float RayDensity { get => constants.RayDensity; set { constants.RayDensity = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Vector2, (int)Props.Center)]
            public Vector2 Center { get => constants.Center; set { constants.Center = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.MixingMode)]
            public float MixingMode { get => constants.MixingMode; set { constants.MixingMode = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Float, (int)Props.LinearColor)]
            public float LinearColor { get => constants.LinearColor; set { constants.LinearColor = value; UpdateConstants(); } }
            [CustomEffectProperty(PropertyType.Vector3, (int)Props.RGBScales)]
            public Vector3 RGBScales { get => constants.RGBScales; set { constants.RGBScales = value; UpdateConstants(); } }
        }
    }
}