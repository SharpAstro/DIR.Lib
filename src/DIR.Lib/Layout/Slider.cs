using System;

namespace DIR.Lib.Layout;

public static partial class Builder
{
    /// <summary>
    /// A draggable value in a range: <c>Builder.Slider(state)</c> is the whole declaration, and the
    /// painter takes care of drawing it (through the one shared track/fill/handle implementation),
    /// registering its <see cref="HitResult.SliderStateHit"/> and arming its own drag. See
    /// <see cref="Content.Slider"/>.
    /// <para>
    /// <c>Star</c> width by default, unlike <see cref="TextInput"/>: a slider almost always fills the row
    /// it sits in and has no placeholder-shaped content to shrink to under <c>Auto</c> the way a field
    /// does.
    /// </para>
    /// </summary>
    public static Node Slider(SliderState state, RGBAColor32? fillColor = null, TrackSliderChrome? chrome = null)
        => new Node.Leaf(new Content.Slider(state)
        {
            FillColor = fillColor ?? Content.Slider.DefaultFillColor,
            Chrome = chrome ?? new TrackSliderChrome(Content.Slider.DefaultTrackColor, Content.Slider.DefaultHandleColor),
        })
        { Width = Sizing.Star() };

    /// <summary>
    /// A themed slider, drawn from <see cref="ControlStyles.SliderOrDerived"/> alone: the track up to the thumb is the
    /// accent over a <see cref="UiPalette.ControlFill"/> track, and the thumb is round, with an accent centre and a ring.
    /// <paramref name="fillColor"/>, <paramref name="trackColor"/> and <paramref name="thumbColor"/> (the centre) win over
    /// the theme when given. The slider is as tall as its thumb, and Star wide like the plain one.
    /// </summary>
    public static Node Slider(SliderState state, UiTheme theme,
        RGBAColor32? fillColor = null, RGBAColor32? trackColor = null, RGBAColor32? thumbColor = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(theme);
        var style = theme.Controls.SliderOrDerived;
        return new Node.Leaf(new Content.Slider(state)
        {
            FillColor = fillColor ?? style.Fill,
            Chrome = new TrackSliderChrome(trackColor ?? style.Track, thumbColor ?? style.ThumbCentre),
            RoundThumb = true,
            ThumbRing = style.ThumbRing,
            ThumbRingWidth = style.ThumbRingWidth,
        })
        { Width = Sizing.Star(), Height = Sizing.Fixed(style.ThumbSize) };
    }
}
