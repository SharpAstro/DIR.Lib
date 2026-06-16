namespace DIR.Lib;

/// <summary>
/// Shared chrome colour roles for cross-surface UI. Surface-agnostic: the same
/// <see cref="RGBAColor32"/> values drive the Vulkan GPU renderer and the terminal
/// (Console.Lib maps each colour to nearest-SGR / truecolor via its <c>VtStyle</c>).
/// Consuming apps supply their own <see cref="UiTheme"/> instance; these roles are
/// the chrome the apps share, not the full palette (app/tab-specific colours -- sky
/// map, charts, overlays -- stay local to their owner).
/// </summary>
public readonly record struct UiPalette(
    RGBAColor32 ContentBg,
    RGBAColor32 PanelBg,
    RGBAColor32 HeaderBg,
    RGBAColor32 HeaderText,
    RGBAColor32 BodyText,
    RGBAColor32 DimText,
    RGBAColor32 Separator,
    RGBAColor32 Selection);

/// <summary>
/// Base (unscaled, pixel) layout metrics shared across chrome. Callers still multiply
/// by their own DPI scale; these are the logical base sizes.
/// </summary>
public readonly record struct UiMetrics(
    float BaseFontSize,
    float Padding,
    float HeaderHeight,
    float ItemHeight,
    float ButtonHeight);

/// <summary>
/// A complete UI theme: a colour <see cref="Palette"/> plus base <see cref="Metrics"/>.
/// One instance is the single source of truth for an app's chrome, replacing per-tab
/// duplicated colour/size constants.
/// </summary>
public readonly record struct UiTheme(UiPalette Palette, UiMetrics Metrics);
