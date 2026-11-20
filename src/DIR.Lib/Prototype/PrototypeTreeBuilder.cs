using System;
using System.Collections.Generic;
using System.Globalization;
using DIR.Lib.Layout;
using Node = DIR.Lib.Layout.Node;
using B = DIR.Lib.Layout.Builder;

namespace DIR.Lib.Prototyping;

/// <summary>
/// Turns a <see cref="PrototypeScreen"/> into a real <see cref="Node"/> tree with the <c>Builder</c> calls, for a
/// theme and a <see cref="PrototypeState"/>. Pure: it touches no surface, so a screen's geometry and click
/// bindings are testable with a stub measure context.
/// </summary>
/// <remarks>
/// <para>
/// A node whose <c>when</c> does not hold is absent from the tree (a child a fixed-arity container needs is a
/// spacer in its place; a modal's card absent is the modal closed). An <c>action</c> is a clickable node whose
/// press runs <c>onAction</c> (a set or toggle is applied to the state first, so a host only has to answer
/// <c>goto</c>). A <c>bind</c> on an input reads and writes the named state; the builder keeps ONE
/// <see cref="TextInputState"/> or <see cref="SliderState"/> per bound name across calls, so focus and the caret
/// survive the per-frame rebuild.
/// </para>
/// <para>Text with no <c>color</c> takes the palette's <c>BodyText</c>, not the white a bare <c>Builder.Text</c> defaults to.</para>
/// </remarks>
public sealed class PrototypeTreeBuilder
{
    private readonly UiTheme _theme;
    private readonly PrototypeState _state;
    private readonly Action<PrototypeAction>? _onAction;
    private readonly Dictionary<string, TextInputState> _inputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SliderState> _sliders = new(StringComparer.Ordinal);
    private readonly HashSet<string> _openDropdowns = new(StringComparer.Ordinal);
    // Unbound inputs keep their state by node path, so a rebuild each frame does not lose what was typed or dragged.
    private readonly Dictionary<string, TextInputState> _unboundInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SliderState> _unboundSliders = new(StringComparer.Ordinal);

    /// <summary>Creates a builder.</summary>
    /// <param name="theme">Supplies the colour roles and the base metrics.</param>
    /// <param name="state">The state <c>when</c> reads and <c>bind</c>, <c>set</c> and <c>toggle</c> write.</param>
    /// <param name="onAction">Told of every action a press runs, after a set or toggle has been applied; a host answers <c>goto</c> here.</param>
    public PrototypeTreeBuilder(UiTheme theme, PrototypeState state, Action<PrototypeAction>? onAction = null)
    {
        _theme = theme;
        _state = state;
        _onAction = onAction;
    }

    /// <summary>
    /// The design area in design units. It centres a fixed-size modal card, and its width sets the read-only
    /// <c>target</c> state on every <see cref="Build"/> (<see cref="PrototypeTargets.ForWidth"/>). Null leaves the card
    /// as built and <c>target</c> as it is: on a builder used on its own that is unset, so a <c>when</c> on
    /// <c>target</c> holds for neither value until a size is given. A <see cref="PrototypePlayer{TSurface}"/> sets
    /// this from its own design size before each build, so a value written here on a player's builder does not last.
    /// </summary>
    public (float Width, float Height)? DesignSize { get; set; }

    /// <summary>The persistent field state for a bound name, created on first use.</summary>
    public TextInputState InputFor(string name)
    {
        if (!_inputs.TryGetValue(name, out var s))
        {
            s = new TextInputState { Text = _state.Get(name) };
            var captured = name;
            s.OnTextChanged = text => _state.Set(captured, text);
            _inputs[name] = s;
        }
        return s;
    }

    /// <summary>The persistent slider state for a bound name, created on first use.</summary>
    public SliderState SliderFor(string name)
    {
        if (!_sliders.TryGetValue(name, out var s))
        {
            s = new SliderState();
            var captured = name;
            s.OnChanged = v => _state.Set(captured, (double)v);
            _sliders[name] = s;
        }
        return s;
    }

    private static T Unbound<T>(Dictionary<string, T> map, string path) where T : new()
    {
        if (!map.TryGetValue(path, out var s)) map[path] = s = new T();
        return s;
    }

    /// <summary>Builds the screen's tree.</summary>
    public Node Build(PrototypeScreen screen)
    {
        // The read-only 'target' state follows the design width, written quietly so painting never raises Changed.
        if (DesignSize is { } d)
            _state.SetQuietly(PrototypeTargets.StateName, PrototypeTargets.ForWidth(d.Width));
        return BuildNode(screen.Root) ?? B.Spacer();
    }

    // ---- one node ----

    private Node? BuildNode(PrototypeNode n)
    {
        if (!_state.WhenHolds(n.When)) return null;

        var node = BuildKind(n);
        return ApplyCommon(n, node);
    }

    private Node BuildNode(PrototypeNode n, Node fallback) => BuildNode(n) ?? fallback;

    private Node[] BuildChildren(PrototypeNode n)
    {
        var list = new List<Node>(n.Children.Length);
        foreach (var c in n.Children)
        {
            if (BuildNode(c) is { } built) list.Add(built);
        }
        return [.. list];
    }

    private Node BuildKind(PrototypeNode n)
    {
        var p = n.Properties;
        switch (n.Kind)
        {
            case "vstack": return B.VStack(BuildChildren(n)).WithGap(Num(p, "gap", 0f));
            case "hstack": return B.HStack(BuildChildren(n)).WithGap(Num(p, "gap", 0f));
            case "grid":
            {
                var gap = Num(p, "gap", 0f);
                return B.Grid((int)Num(p, "columns", 1f), BuildChildren(n)).WithGaps(gap, gap);
            }
            case "wrap":
            {
                var kids = BuildChildren(n);
                var wrap = Flag(p, "vertical", false) ? B.WrapV(kids) : B.WrapH(kids);
                return wrap.WithGap(Num(p, "gap", 0f));
            }
            case "dock": return BuildDock(n);
            case "split":
            {
                var axis = Str(p, "axis", "horizontal") == "vertical" ? Axis.Vertical : Axis.Horizontal;
                return B.Split(BuildNode(n.Children[0], B.Spacer()), BuildNode(n.Children[1], B.Spacer()), axis, Num(p, "firstExtent", 0f));
            }
            case "overlay":
                return B.Overlay(BuildNode(n.Children[0], B.Spacer()), BuildNode(n.Children[1], B.Spacer()));
            case "modal":
            {
                var layer = BuildNode(n.Children[0], B.Spacer());
                var card = Flag(p, "open", true) ? BuildNode(n.Children[1]) : null;
                // A card of fixed size is centred in the design area, so the scrim around it is outside the card
                // and a press there dismisses. Any other card fills the surface, as an overlay's top does.
                if (card is not null && DesignSize is { } size && card.Width.IsFixed && card.Height.IsFixed)
                {
                    card = B.Anchored(card, side: null,
                        offsetAlong: (size.Width - card.Width.Value) / 2f,
                        offsetAcross: (size.Height - card.Height.Value) / 2f);
                }
                return B.Modal(layer, card, ActionOf(n.Action));
            }
            case "text":
                return B.Text(Str(p, "text", ""), Num(p, "fontSize", 14f), Col(p, "color") ?? Palette.BodyText,
                    Str(p, "align", "near") switch { "center" => TextAlign.Center, "far" => TextAlign.Far, _ => TextAlign.Near });
            case "markdown": return B.Markdown(Str(p, "source", ""), MarkdownStyleOf());
            case "icon":
            {
                Enum.TryParse<IconKind>(Str(p, "icon", ""), out var kind);
                return B.Icon(kind, p.TryGetValue("iconSize", out var sz) ? (float)sz.Number : null, Col(p, "color") ?? Palette.BodyText);
            }
            case "box":
                return B.Box(FixedOf(p, "width"), FixedOf(p, "height"), Col(p, "color"));
            case "fill": return B.Fill(Num(p, "minWidth", 0f), Num(p, "minHeight", 0f), Str(p, "key", null!));
            case "spacer": return B.Spacer();
            case "textInput": return BuildTextInput(n);
            case "checkbox": return BuildCheckbox(n);
            case "buttonGroup": return BuildButtonGroup(n);
            case "dropdown": return BuildDropdown(n);
            case "slider": return BuildSlider(n);
            case "progress":
                return B.Progress((float)Math.Clamp(Num(p, "value", 0f), 0f, 1f), Col(p, "track") ?? Palette.Separator,
                    Col(p, "color") ?? Palette.Accent, p.TryGetValue("label", out var label) ? label.Text : null,
                    Num(p, "fontSize", 14f), Palette.BodyText).WStar().H(Sizing.Fixed(Math.Max(1f, _theme.Metrics.ItemHeight * 0.6f)));
            default: return B.Spacer();
        }
    }

    private Node BuildDock(PrototypeNode n)
    {
        Node? fill = null;
        var docked = new List<DockChild>();
        foreach (var c in n.Children)
        {
            var built = BuildNode(c);
            if (c.Properties.TryGetValue("side", out var side))
            {
                if (built is null) continue;
                var size = c.Properties.TryGetValue("size", out var sz) ? (float)sz.Number : 0f;
                docked.Add(side.Text switch
                {
                    "left" => B.Left(built, size),
                    "right" => B.Right(built, size),
                    "top" => B.Top(built, size),
                    _ => B.Bottom(built, size),
                });
            }
            else fill = built;
        }
        return B.Dock(fill ?? B.Spacer(), [.. docked]);
    }

    // ---- inputs ----

    private Node BuildTextInput(PrototypeNode n)
    {
        var p = n.Properties;
        var field = n.Bind is { } name ? InputFor(name) : Unbound(_unboundInputs, n.Path);
        if (n.Bind is { } bound && !field.IsActive && field.Text != _state.Get(bound))
        {
            field.Text = _state.Get(bound);
            field.CursorPos = Math.Min(field.CursorPos, field.Text.Length);
        }
        if (p.TryGetValue("placeholder", out var ph)) field.Placeholder = ph.Text;
        var node = B.TextInput(field, Num(p, "fontSize", 14f));
        return node.WStar().HFixed(_theme.Metrics.ItemHeight);
    }

    private Node BuildCheckbox(PrototypeNode n)
    {
        var style = new CheckboxStyle(Palette.Separator, Palette.Accent, Palette.BodyText, Palette.HeaderBg);
        var name = n.Bind;
        var isChecked = name is not null && _state.GetBool(name);
        Action<bool>? toggle = name is null ? null : v => _state.Set(name, v);
        return B.Checkbox(Str(n.Properties, "label", ""), isChecked, toggle, style);
    }

    private Node BuildButtonGroup(PrototypeNode n)
    {
        var style = new ButtonGroupStyle(Palette.Selection, Palette.HeaderBg, Palette.SelectionText, Palette.BodyText, Palette.Separator);
        var options = n.Properties.TryGetValue("options", out var o) ? o.List : [];
        var items = new ButtonGroupOption<string>[options.Length];
        for (int i = 0; i < items.Length; i++) items[i] = new ButtonGroupOption<string>(options[i], options[i]);
        var name = n.Bind;
        Action<string>? select = name is null ? null : v => _state.Set(name, v);
        var selected = name is null ? "" : _state.Get(name);
        return B.ButtonGroup<string>(items, selected, select, style).WStar().HFixed(_theme.Metrics.ButtonHeight);
    }

    private Node BuildDropdown(PrototypeNode n)
    {
        var p = n.Properties;
        var name = n.Bind ?? n.Path;
        var current = n.Bind is null ? "" : _state.Get(n.Bind);
        var caption = current.Length > 0 ? current : Str(p, "label", "");
        var open = _openDropdowns.Contains(name);
        var fontSize = _theme.Metrics.BaseFontSize;

        var trigger = B.HStack(
                B.Text(caption, fontSize, Palette.BodyText).WStar().HStar(),
                B.Icon(open ? IconKind.CaretUp : IconKind.CaretDown, null, Palette.DimText))
            .CrossCenter().Pad(_theme.Metrics.Padding, 0f)
            .Bg(Palette.HeaderBg).HFixed(_theme.Metrics.ItemHeight)
            .Clickable(new HitResult.ButtonHit("dropdown:" + name), _ => { if (!_openDropdowns.Remove(name)) _openDropdowns.Add(name); });
        if (!open) return trigger.WStar();

        var rows = new List<Node> { trigger };
        foreach (var option in p.TryGetValue("options", out var o) ? o.List : [])
        {
            var value = option;
            rows.Add(B.Text(option, fontSize, value == current ? Palette.SelectionText : Palette.BodyText)
                .Pad(_theme.Metrics.Padding, 0f).HFixed(_theme.Metrics.ItemHeight)
                .Bg(value == current ? Palette.Selection : Palette.PanelBg)
                .BgHover(Palette.Separator)
                .Clickable(new HitResult.ButtonHit("option:" + name + "=" + value), _ =>
                {
                    if (n.Bind is { } bind) _state.Set(bind, value);
                    _openDropdowns.Remove(name);
                }));
        }
        return B.VStack([.. rows]).WStar();
    }

    private Node BuildSlider(PrototypeNode n)
    {
        var p = n.Properties;
        var state = n.Bind is { } name ? SliderFor(name) : Unbound(_unboundSliders, n.Path);
        state.Min = Num(p, "min", 0f);
        state.Max = Num(p, "max", 1f);
        if (n.Bind is { } b) state.Value = (float)_state.GetNumber(b, state.Min);
        var chrome = new TrackSliderChrome(Palette.Separator, Palette.Accent);
        return B.Slider(state, Col(p, "color") ?? Palette.Accent, chrome).HFixed(_theme.Metrics.ItemHeight);
    }

    // ---- common properties and actions ----

    private Node ApplyCommon(PrototypeNode n, Node node)
    {
        var p = n.Properties;
        if (p.TryGetValue("width", out var w)) node = node.W(SizingOf(w));
        if (p.TryGetValue("height", out var h)) node = node.H(SizingOf(h));
        if (p.TryGetValue("padding", out var pad)) node = node.Pad((float)pad.Number);
        if (Col(p, "background") is { } bg) node = node.Bg(bg);
        var radius = Num(p, "borderRadius", 0f);
        if (Col(p, "border") is { } border) node = node.Border(border, Num(p, "borderWidth", 1f), radius);
        else if (radius > 0f) node = node.Radius(radius);
        if (p.TryGetValue("tooltip", out var tip)) node = node.WithTooltip(tip.Text);

        // A modal's action is its scrim dismissal, and a bound input owns its own press.
        if (n.Action is { } action && n.Kind != "modal" && n.Bind is null)
        {
            node = node.Clickable(new HitResult.ButtonHit(Str(p, "key", null!) ?? action), _ => Run(action));
        }
        return node;
    }

    private Action? ActionOf(string? action) => action is null ? null : () => Run(action);

    private void Run(string action)
    {
        if (!PrototypeAction.TryParse(action, out var parsed)) return;
        _state.Apply(parsed);
        _onAction?.Invoke(parsed);
    }

    // ---- property reading ----

    private UiPalette Palette => _theme.Palette;

    private static float Num(IReadOnlyDictionary<string, PrototypeValue> p, string name, float fallback)
        => p.TryGetValue(name, out var v) ? (float)v.Number : fallback;

    private static bool Flag(IReadOnlyDictionary<string, PrototypeValue> p, string name, bool fallback)
        => p.TryGetValue(name, out var v) ? v.Flag : fallback;

    private static string Str(IReadOnlyDictionary<string, PrototypeValue> p, string name, string fallback)
        => p.TryGetValue(name, out var v) ? v.Text : fallback;

    private static float FixedOf(IReadOnlyDictionary<string, PrototypeValue> p, string name)
        => p.TryGetValue(name, out var v) && v.Kind == PrototypeValueKind.Number ? (float)v.Number : 0f;

    private RGBAColor32? Col(IReadOnlyDictionary<string, PrototypeValue> p, string name)
        => p.TryGetValue(name, out var v) ? ResolveColor(v.Text) : null;

    /// <summary>Resolves a colour role name through the theme's palette, or a <c>#rgb</c>, <c>#rrggbb</c> or <c>#rrggbbaa</c> literal.</summary>
    public RGBAColor32 ResolveColor(string text)
    {
        if (text.StartsWith('#'))
        {
            var hex = text[1..];
            if (hex.Length == 3)
            {
                return new RGBAColor32(Nib(hex[0]), Nib(hex[1]), Nib(hex[2]), 0xff);
            }
            byte Pair(int i) => byte.Parse(hex.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new RGBAColor32(Pair(0), Pair(2), Pair(4), hex.Length == 8 ? Pair(6) : (byte)0xff);
        }
        var pal = Palette;
        return text switch
        {
            "ContentBg" => pal.ContentBg,
            "PanelBg" => pal.PanelBg,
            "HeaderBg" => pal.HeaderBg,
            "Separator" => pal.Separator,
            "SeparatorStrong" => pal.SeparatorStrong,
            "BodyText" => pal.BodyText,
            "DimText" => pal.DimText,
            "HeaderText" => pal.HeaderText,
            "Accent" => pal.Accent,
            "AccentAlt" => pal.AccentAlt,
            "Selection" => pal.Selection,
            "SelectionText" => pal.SelectionText,
            "Focus" => pal.Focus,
            "Info" => pal.Info,
            "Warn" => pal.Warn,
            "Error" => pal.Error,
            "Success" => pal.Success,
            _ => pal.BodyText,
        };

        static byte Nib(char c) => (byte)(Convert.ToByte(c.ToString(), 16) * 17);
    }

    private static Sizing SizingOf(PrototypeValue v)
    {
        if (v.Kind == PrototypeValueKind.Number) return Sizing.Fixed((float)v.Number);
        if (v.Text == "auto") return Sizing.Auto;
        if (v.Text.StartsWith("star:", StringComparison.Ordinal)
            && float.TryParse(v.Text.AsSpan(5), NumberStyles.Float, CultureInfo.InvariantCulture, out var weight))
            return Sizing.Star(weight);
        return Sizing.Star();
    }

    private MarkdownStyle MarkdownStyleOf() => new()
    {
        FontSize = _theme.Metrics.BaseFontSize,
        Text = Palette.BodyText,
        Strong = Palette.HeaderText,
        Muted = Palette.DimText,
        Link = Palette.Accent,
        CodeBackground = Palette.HeaderBg,
    };
}
