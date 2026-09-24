using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;

#pragma warning disable CS0618

namespace JockeyStudio.Wpf.Controls;

/// <summary>Single-line text with sub-pixel letter-spacing (WPF has no native
/// tracking). Rendered via GlyphRun so the advance includes the requested
/// spacing — mirrors the CSS letter-spacing used by the original UI
/// (.topbar h1 0.4px, .tile-time 0.5px, .pop-title 0.6px, .seek-tip 0.4px).</summary>
public sealed class TrackedText : FrameworkElement
{
    private GlyphRun? _glyphs;
    private double _width;
    private double _baseline;
    private double _height;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(TrackedText),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LetterSpacingProperty = DependencyProperty.Register(
        nameof(LetterSpacing), typeof(double), typeof(TrackedText),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(
        typeof(TrackedText),
        new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(
        typeof(TrackedText),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(
        typeof(TrackedText),
        new FrameworkPropertyMetadata(FontWeights.Normal, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(TrackedText),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TabularProperty = DependencyProperty.Register(
        nameof(Tabular), typeof(bool), typeof(TrackedText),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double LetterSpacing { get => (double)GetValue(LetterSpacingProperty); set => SetValue(LetterSpacingProperty, value); }
    public bool Tabular { get => (bool)GetValue(TabularProperty); set => SetValue(TabularProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_glyphs is not null && Foreground is not null)
        {
            dc.DrawGlyphRun(Foreground, _glyphs);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Rebuild();
        return new Size(_width, _height);
    }

    private void Rebuild()
    {
        _glyphs = null;
        double spacing = LetterSpacing > 0.0 ? LetterSpacing : 0.0;
        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal);
        var text = Text ?? string.Empty;
        if (text.Length == 0 || FontSize <= 0 || !typeface.TryGetGlyphTypeface(out var glyphTypeface))
        {
            _width = 0.0;
            _height = 0.0;
            _baseline = 0.0;
            return;
        }

        double unitsPerEm = glyphTypeface.Height;
        double scale = unitsPerEm > 0 ? FontSize / unitsPerEm : FontSize;
        double lineSpacing = FontFamily.LineSpacing > 0.0 ? FontFamily.LineSpacing : 1.2;

        double digitAdvance = 0.0;
        if (Tabular && glyphTypeface.CharacterToGlyphMap.TryGetValue('0', out ushort g0))
        {
            digitAdvance = glyphTypeface.AdvanceWidths[g0] * scale;
        }

        var glyphs = new List<ushort>();
        var advances = new List<double>();

        double width = 0.0;
        for (int i = 0; i < text.Length; i++)
        {
            int cp = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text, i);
                i++;
            }
            if (!glyphTypeface.CharacterToGlyphMap.TryGetValue(cp, out ushort gi)) gi = 0;
            double advance = (Tabular && cp >= '0' && cp <= '9'
                ? digitAdvance
                : glyphTypeface.AdvanceWidths[gi] * scale) + spacing;
            glyphs.Add(gi);
            advances.Add(advance);
            width += advance;
        }

        _baseline = FontSize * 0.95;
        _width = width;
        _height = FontSize * lineSpacing;
        var origin = new Point(0.0, _baseline);
        _glyphs = new GlyphRun(glyphTypeface, 0, false, FontSize, glyphs, origin, advances, null, null, null, null, null, null);
    }
}