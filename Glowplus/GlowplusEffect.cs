using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace Glowplus
{
    [VideoEffect("Glow plus", ["加工"], ["glow", "deep", "light", "optical", "発光", "光"])]
    internal class GlowplusEffect : VideoEffectBase
    {
        public override string Label => "Glow plus";

        // --- 基本設定 ---
        [Display(GroupName = "基本", Name = "強度", Description = "光の強さです。")]
        [AnimationSlider("F2", "", 0, 5)]
        public Animation Exposure { get; } = new Animation(1.0f, 0, 100);

        // ★変更点1: 本当の閾値
        [Display(GroupName = "基本", Name = "閾値", Description = "これ以下の明るさの光をカットします(0～100%)。")]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation Threshold { get; } = new Animation(60f, 0, 100);

        // ★変更点2: 旧・閾値を「コントラスト拡張」に変更
        [Display(GroupName = "基本", Name = "コントラスト (Contrast)", Description = "こいつと閾値を0にすると、いい感じに色が光ってくれます。")]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation Contrast { get; } = new Animation(0f, 0, 100);

        // ★追加: 自動クリッピング設定
        [Display(GroupName = "基本", Name = "自動クリッピング", Description = "ON: 画面外の計算を省略して軽量化します。\nOFF: 画面外も光の計算をします。死ぬほど重いです。")]
        [ToggleSlider]
        public bool AutoClipping
        {
            get => autoClipping;
            set => Set(ref autoClipping, value);
        }
        private bool autoClipping = true; // デフォルトON

        [Display(GroupName = "形状", Name = "品質", Description = "グローの滑らかさ。なんでか知らんけど品質5ぐらいが一番重いです。")]
        [AnimationSlider("F0", "段", 1, 10)]
        public Animation Steps { get; } = new Animation(10, 1, 10);

        [Display(GroupName = "形状", Name = "処理モード", Description = "内部解像度の設定です。軽量はまじでえぐい。最高品質のほうがいいよ")]
        [EnumComboBox]
        public GlowQualityMode QualityMode
        {
            get => qualityMode;
            set => Set(ref qualityMode, value);
        }
        private GlowQualityMode qualityMode = GlowQualityMode.Lightweight;

        [Display(GroupName = "形状", Name = "拡散", Description = "光の広がり具合です。")]
        [AnimationSlider("F1", "px", 0, 20)]
        public Animation BlurRadius { get; } = new Animation(6.5f, 0, 200);

        // --- サイズ制御 ---
        [Display(GroupName = "形状", Name = "サイズ X", Description = "横方向の広がり倍率")]
        [AnimationSlider("F0", "%", 0, 200)]
        public Animation SizeX { get; } = new Animation(100f, 0, 500);

        [Display(GroupName = "形状", Name = "サイズ Y", Description = "縦方向の広がり倍率")]
        [AnimationSlider("F0", "%", 0, 200)]
        public Animation SizeY { get; } = new Animation(100f, 0, 500);
        /*
        // --- 色収差 (倍率) ---
        [Display(GroupName = "色収差", Name = "赤の倍率 (Red Scale)", Description = "中心からの赤成分の広がりです。")]
        [AnimationSlider("F2", "%", 50, 150)]
        public Animation RedScale { get; } = new Animation(100f, 50, 150);

        [Display(GroupName = "色収差", Name = "緑の倍率 (Green Scale)", Description = "中心からの緑成分の広がりです。")]
        [AnimationSlider("F2", "%", 50, 150)]
        public Animation GreenScale { get; } = new Animation(100f, 50, 150);

        [Display(GroupName = "色収差", Name = "青の倍率 (Blue Scale)", Description = "中心からの青成分の広がりです。")]
        [AnimationSlider("F2", "%", 50, 150)]
        public Animation BlueScale { get; } = new Animation(100f, 50, 150);
        */
        // --- 色設定 ---
        [Display(GroupName = "色", Name = "彩度", Description = "色を乗せる前に彩度を高めます。")]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation Vibrance { get; } = new Animation(0f, 0, 100);

        [Display(GroupName = "色", Name = "全体の色", Description = "ColorizeがOFFの時の光の色。")]
        [ColorPicker]
        public Color GlowColor
        {
            get => glowColor;
            set => Set(ref glowColor, value);
        }
        private Color glowColor = Colors.White;

        // --- Colorize設定 ---
        [Display(GroupName = "着色", Name = "有効にする", Description = "ONにすると光の色をグラデーション置換します。")]
        [ToggleSlider]
        public bool Colorize
        {
            get => colorize;
            set => Set(ref colorize, value);
        }
        private bool colorize = false;

        [Display(GroupName = "着色", Name = "内側の色", Description = "中心（明るい部分）の色")]
        [ColorPicker]
        public Color InnerTint
        {
            get => innerTint;
            set => Set(ref innerTint, value);
        }
        private Color innerTint = Colors.White;

        [Display(GroupName = "着色", Name = "外側の色", Description = "周辺（暗い部分）の色")]
        [ColorPicker]
        public Color OuterTint
        {
            get => outerTint;
            set => Set(ref outerTint, value);
        }
        private Color outerTint = Color.FromRgb(0, 120, 255);

        // --- 追加スライダー: 着色の調整 ---
        [Display(GroupName = "着色", Name = "色の強さ", Description = "値を上げると内側の色が出やすくなります。")]
        [AnimationSlider("F2", "x", 0.1, 5.0)]
        public Animation TintScale { get; } = new Animation(0.5f, 0.1, 10);

        [Display(GroupName = "着色", Name = "色の分布", Description = "色の混ざり具合を調整します。小さいと外側寄り、大きいと内側寄りになります。")]
        [AnimationSlider("F2", "", 0.1, 3.0)]
        public Animation TintGamma { get; } = new Animation(1.0f, 0.1, 5.0);


        // --- 合成 ---
        [Display(GroupName = "合成", Name = "リニア合成", Description = "物理的に正しい光の加算を行います。")]
        [ToggleSlider]
        public bool LinearColor
        {
            get => linearColor;
            set => Set(ref linearColor, value);
        }
        private bool linearColor = true;
        [Display(GroupName = "合成", Name = "合成モード", Description = "いい感じの色になったやつと、物理的に正しい混ざり方のやつ。")]
        [EnumComboBox]
        public GlowMixingMode MixingMode
        {
            get => mixingMode;
            set => Set(ref mixingMode, value);
        }
        private GlowMixingMode mixingMode = GlowMixingMode.Physical;

        [Display(GroupName = "合成", Name = "元の画像を表示", Description = "OFFにするとグロー成分のみ出力します。\n正直、元の不透明度の実装によっていらない子になった。")]
        [ToggleSlider]
        public bool ShowSource
        {
            get => showSource;
            set => Set(ref showSource, value);
        }
        private bool showSource = true;

        [Display(GroupName = "合成", Name = "元の不透明度", Description = "元の画像の濃さを調整します。下げると芯の色がグロー色に馴染みます。")]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation SourceOpacity { get; } = new Animation(100f, 0, 100);
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
        public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription)
        {
            return [];
        }

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
        {
            return new GlowplusEffectProcessor(devices, this);
        }

        protected override IEnumerable<IAnimatable> GetAnimatables() => [Exposure, Threshold, Contrast, Steps, BlurRadius, SizeX, SizeY, Vibrance, TintScale, TintGamma, SourceOpacity/*, RedScale, GreenScale, BlueScale*/];
    }
}