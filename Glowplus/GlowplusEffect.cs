using Glowplus;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Windows.Interop;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;
using static Glowplus.GlowplusEffect;

namespace Glowplus
{
    [VideoEffect("Glow plus", ["加工"], ["glow", "deep", "light", "optical", "発光", "光", "色収差", "放射光", "フレア"])]
    internal class GlowplusEffect : VideoEffectBase
    {
        public override string Label => "Glow plus";

        public enum GlowMixingMode
        {
            [Display(Name = "個人的にいい感じのやつ")]
            Artistic = 0,
            [Display(Name = "物理的")]
            Physical = 1
        }
        public enum GlowQualityMode
        {
            [Display(Name = "軽量")]
            Lightweight = 0,
            [Display(Name = "バランス")]
            Balanced = 1,
            [Display(Name = "最高品質")]
            HighQuality = 2
        }
        public enum ChromaStyleMode
        {
            [Display(Name = "放射状")] Radial = 1,
            [Display(Name = "平行・指向性")] Directional = 2
        }

        public enum RayStyleMode
        {
            [Display(Name = "放射状")] Radial = 1,
            [Display(Name = "平行・指向性")] Directional = 2
        }

        // --- 基本設定 ---
        [Display(GroupName = "基本", Name = "強度", Description = "光の強さです。")]
        [AnimationSlider("F2", "", 0, 5)]
        public Animation Exposure { get; } = new Animation(1.0f, 0, 100);

        [Display(GroupName = "基本", Name = "閾値", Description = "これ以下の明るさの光をカットします(0～100%)。")]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation Threshold { get; } = new Animation(60f, 0, 100);

        [Display(GroupName = "基本", Name = "コントラスト", Description = "こいつと閾値を0にすると、いい感じに色が光ってくれます。")]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation Contrast { get; } = new Animation(0f, 0, 100);

        [Display(GroupName = "基本", Name = "自動クリッピング", Description = "ON: 画面外の計算を省略して軽量化します。\nOFF: 画面外も光の計算をします。")]
        [ToggleSlider]
        public bool AutoClipping { get => autoClipping; set => Set(ref autoClipping, value); }
        private bool autoClipping = true;

        [Display(GroupName = "基本", Name = "枠サイズに固定", Description = "ONにすると光が元画像の枠外にはみ出さなくなり、処理が軽くなります。")]
        [ToggleSlider]
        public bool FixToOriginalSize { get => fixToOriginalSize; set => Set(ref fixToOriginalSize, value); }
        private bool fixToOriginalSize = false;

        [Display(GroupName = "形状", Name = "品質", Description = "グローの滑らかさ。")]
        [AnimationSlider("F0", "段", 1, 10)]
        public Animation Steps { get; } = new Animation(10, 1, 10);

        [Display(GroupName = "形状", Name = "処理モード", Description = "内部解像度の設定です。")]
        [EnumComboBox]
        public GlowQualityMode QualityMode { get => qualityMode; set => Set(ref qualityMode, value); }
        private GlowQualityMode qualityMode = GlowQualityMode.HighQuality;

        [Display(GroupName = "形状", Name = "拡散", Description = "光の広がり具合です。")]
        [AnimationSlider("F1", "px", 0, 20)]
        public Animation BlurRadius { get; } = new Animation(6.5f, 0, 200);

        [Display(GroupName = "形状", Name = "サイズ X", Description = "横方向の広がり倍率")]
        [AnimationSlider("F0", "%", 0, 200)]
        public Animation SizeX { get; } = new Animation(100f, 0, 500);

        [Display(GroupName = "形状", Name = "サイズ Y", Description = "縦方向の広がり倍率")]
        [AnimationSlider("F0", "%", 0, 200)]
        public Animation SizeY { get; } = new Animation(100f, 0, 500);

        [Display(GroupName = "色", Name = "彩度", Description = "色を乗せる前に彩度を高めます。")]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation Vibrance { get; } = new Animation(0f, 0, 100);

        [Display(GroupName = "色", Name = "全体の色", Description = "ColorizeがOFFの時の光の色。")]
        [ColorPicker]
        public Color GlowColor { get => glowColor; set => Set(ref glowColor, value); }
        private Color glowColor = Colors.White;

        [Display(GroupName = "着色", Name = "有効にする", Description = "ONにすると光の色をグラデーション置換します。")]
        [ToggleSlider]
        public bool Colorize { get => colorize; set => Set(ref colorize, value); }
        private bool colorize = false;

        [Display(GroupName = "着色", Name = "内側の色", Description = "中心（明るい部分）の色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(Colorize), true)]
        public Color InnerTint { get => innerTint; set => Set(ref innerTint, value); }
        private Color innerTint = Colors.White;

        [Display(GroupName = "着色", Name = "外側の色", Description = "周辺（暗い部分）の色")]
        [ColorPicker]
        [ShowPropertyEditorWhen(nameof(Colorize), true)]
        public Color OuterTint { get => outerTint; set => Set(ref outerTint, value); }
        private Color outerTint = Color.FromRgb(0, 120, 255);

        [Display(GroupName = "着色", Name = "色の強さ", Description = "値を上げると内側の色が出やすくなります。")]
        [AnimationSlider("F2", "x", 0.1, 5.0)]
        [ShowPropertyEditorWhen(nameof(Colorize), true)]
        public Animation TintScale { get; } = new Animation(0.5f, 0.1, 10);

        [Display(GroupName = "着色", Name = "色の分布", Description = "色の混ざり具合を調整します。")]
        [AnimationSlider("F2", "", 0.1, 3.0)]
        [ShowPropertyEditorWhen(nameof(Colorize), true)]
        public Animation TintGamma { get; } = new Animation(1.0f, 0.1, 5.0);


        // --- ★色収差設定 ---
        [Display(GroupName = "色収差", Name = "収差スタイル", Description = "ズレる方向を指定します。")]
        [EnumComboBox]
        public ChromaStyleMode ChromaStyle { get => chromaStyle; set => Set(ref chromaStyle, value); }
        private ChromaStyleMode chromaStyle = ChromaStyleMode.Radial;

        [Display(GroupName = "色収差", Name = "角度", Description = "Directionalモード時の色ズレの角度です。")]
        [AnimationSlider("F1", "度", -180, 180)]
        [ShowPropertyEditorWhen(nameof(ChromaStyle), ChromaStyleMode.Directional)]
        public Animation ChromaAngle { get; } = new Animation(0f, -360, 360);

        [Display(GroupName = "色収差", Name = "中心 X", Description = "Radialモード時の色収差の基点(X)")]
        [AnimationSlider("F1", "px", -500, 500)]
        [ShowPropertyEditorWhen(nameof(ChromaStyle), ChromaStyleMode.Radial)]
        public Animation ChromaCenterX { get; } = new Animation(0f, -5000, 5000);

        [Display(GroupName = "色収差", Name = "中心 Y", Description = "Radialモード時の色収差の基点(Y)")]
        [AnimationSlider("F1", "px", -500, 500)]
        [ShowPropertyEditorWhen(nameof(ChromaStyle), ChromaStyleMode.Radial)]
        public Animation ChromaCenterY { get; } = new Animation(0f, -5000, 5000);

        [Display(GroupName = "色収差", Name = "赤 (R) ズレ", Description = "赤色をずらします。")]
        [AnimationSlider("F1", "%", -10, 10)]
        public Animation ChromaR { get; } = new Animation(0f, -100, 100);

        [Display(GroupName = "色収差", Name = "緑 (G) ズレ", Description = "緑色をずらします。")]
        [AnimationSlider("F1", "%", -10, 10)]
        public Animation ChromaG { get; } = new Animation(0f, -100, 100);

        [Display(GroupName = "色収差", Name = "青 (B) ズレ", Description = "青色をずらします。")]
        [AnimationSlider("F1", "%", -10, 10)]
        public Animation ChromaB { get; } = new Animation(0f, -100, 100);


        // --- ★放射光設定 ---
        [Display(GroupName = "放射光", Name = "放射スタイル", Description = "Directionalにするとアナモルフィック・フレアのような一直線の光になります。")]
        [EnumComboBox]
        public RayStyleMode RayStyle { get => rayStyle; set => Set(ref rayStyle, value); }
        private RayStyleMode rayStyle = RayStyleMode.Radial;

        [Display(GroupName = "放射光", Name = "角度", Description = "Directionalモード時の光の伸びる角度です。")]
        [AnimationSlider("F1", "度", -180, 180)]
        [ShowPropertyEditorWhen(nameof(RayStyle), RayStyleMode.Directional)]
        public Animation RayAngle { get; } = new Animation(0f, -360, 360);

        [Display(GroupName = "放射光", Name = "長さ", Description = "光を伸ばします。0で無効です。")]
        [AnimationSlider("F2", "x", 0, 2)]
        public Animation RayLength { get; } = new Animation(0f, 0, 1);

        [Display(GroupName = "放射光", Name = "減衰カーブ", Description = "光の消え方を調整します。大きいほど根元だけが強く光ります。")]
        [AnimationSlider("F2", "", 0.1, 5.0)]
        public Animation RayFalloff { get; } = new Animation(1.0f, 0.1, 10.0);

        [Display(GroupName = "放射光", Name = "中心 X", Description = "Radialモード時の中心座標(X)")]
        [AnimationSlider("F1", "px", -500, 500)]
        [ShowPropertyEditorWhen(nameof(RayStyle), RayStyleMode.Radial)]
        public Animation RayCenterX { get; } = new Animation(0f, -5000, 5000);

        [Display(GroupName = "放射光", Name = "中心 Y", Description = "Radialモード時の中心座標(Y)")]
        [AnimationSlider("F1", "px", -500, 500)]
        [ShowPropertyEditorWhen(nameof(RayStyle), RayStyleMode.Radial)]
        public Animation RayCenterY { get; } = new Animation(0f, -5000, 5000);

        [Display(GroupName = "放射光", Name = "品質", Description = "サンプリング数。多いほど滑らかですが処理が重くなります。")]
        [AnimationSlider("F0", "回", 1, 256)]
        public Animation RaySamples { get; } = new Animation(256, 1, 256);


        // --- 合成 ---
        [Display(GroupName = "合成", Name = "リニア合成", Description = "物理的に正しい光の加算を行います。")]
        [ToggleSlider]
        public bool LinearColor { get => linearColor; set => Set(ref linearColor, value); }
        private bool linearColor = true;

        [Display(GroupName = "合成", Name = "合成モード", Description = "いい感じの色になったやつと、物理的に正しい混ざり方のやつ。")]
        [EnumComboBox]
        public GlowMixingMode MixingMode { get => mixingMode; set => Set(ref mixingMode, value); }
        private GlowMixingMode mixingMode = GlowMixingMode.Physical;

        [Display(GroupName = "合成", Name = "元の画像を表示", Description = "OFFにするとグロー成分のみ出力します。")]
        [ToggleSlider]
        public bool ShowSource { get => showSource; set => Set(ref showSource, value); }
        private bool showSource = true;

        [Display(GroupName = "合成", Name = "元の不透明度", Description = "元の画像の濃さを調整します。")]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation SourceOpacity { get; } = new Animation(100f, 0, 100);

        public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
        {
            return new GlowplusEffectProcessor(devices, this);
        }

        protected override IEnumerable<IAnimatable> GetAnimatables() => [
            Exposure, Threshold, Contrast, Steps, BlurRadius, SizeX, SizeY, Vibrance, TintScale, TintGamma, SourceOpacity,
            ChromaAngle, ChromaCenterX, ChromaCenterY, ChromaR, ChromaG, ChromaB, RayAngle, RayLength, RayFalloff, RayCenterX, RayCenterY, RaySamples
        ];
    }
}