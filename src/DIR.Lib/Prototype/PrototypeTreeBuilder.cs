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
    // The enclosing tree's bind and table's columns, set while that container builds its rows.
    private string? _treeBind;
    private bool _treeReservesTrailing;
    private TableColumn[] _tableColumns = [];
    private TableStyle _tableStyle;

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

    /// <summary>
    /// Measures a tab label's width at a font size, design units; a <see cref="PrototypePlayer{TSurface}"/> supplies
    /// its renderer's. Null estimates a label as <see cref="TabCharEm"/> of the font size per character, which is
    /// close for Latin text, so a tab strip built without a surface is still sized and testable.
    /// </summary>
    public Func<string, float, float>? MeasureLabel { get; set; }

    /// <summary>The per-character width, as a fraction of the font size, a tab label is estimated at when <see cref="MeasureLabel"/> is null.</summary>
    public const float TabCharEm = 0.6f;

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
            case "hstack": return B.HStack(BuildChildren(n)).WithGap(Num(p, "gap", 0f)).CrossCenter();
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
            case "text": return BuildText(n);
            case "button": return BuildButton(n);
            case "switch": return BuildSwitch(n);
            case "tree": return BuildTree(n);
            case "treeRow": return BuildTreeRow(n);
            case "tabs": return BuildTabs(n);
            case "table": return BuildTable(n);
            case "tableRow": return BuildTableRow(n);
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

    private Node BuildText(PrototypeNode n)
    {
        var p = n.Properties;
        var style = Str(p, "style", "body");
        var metrics = _theme.Metrics;
        var text = Str(p, "text", "");
        float size;
        RGBAColor32 ink = Col(p, "color") ?? Palette.BodyText;
        var bold = false;
        switch (style)
        {
            case "heading":
                size = metrics.BaseFontSize * metrics.HeadingFontScale;
                bold = true;
                break;
            case "label":
                size = metrics.BaseFontSize * metrics.LabelFontScale;
                text = text.ToUpperInvariant();
                ink = Col(p, "color") ?? Palette.DimText;
                bold = true;
                break;
            default:
                size = 14f;
                break;
        }
        if (p.TryGetValue("fontSize", out var fs)) size = (float)fs.Number;
        if (p.TryGetValue("weight", out var wt)) bold = wt.Text == "bold";
        var node = B.Text(text, size, ink,
            Str(p, "align", "near") switch { "center" => TextAlign.Center, "far" => TextAlign.Far, _ => TextAlign.Near });
        if (bold) node = node.Bold();
        if (Str(p, "face", "ui") == "mono") node = node.Mono();
        return node;
    }

    private Node BuildButton(PrototypeNode n)
    {
        var p = n.Properties;
        var text = Str(p, "text", "");
        var action = n.Action;
        Action click = action is null ? static () => { } : () => Run(action);
        HitResult hit = new HitResult.ButtonHit(Str(p, "key", null!) ?? action ?? text);
        var style = Str(p, "style", "standard");
        if (style == "link") return B.Hyperlink(text, click, _theme, hit: hit);
        var kind = style switch { "accent" => ButtonKind.Accent, "subtle" => ButtonKind.Subtle, _ => ButtonKind.Standard };
        IconKind? icon = null;
        if (p.TryGetValue("icon", out var ic) && Enum.TryParse<IconKind>(ic.Text, out var parsed)) icon = parsed;
        return B.Button(text, kind, click, _theme, icon, Flag(p, "opens", false),
            p.TryGetValue("disabled", out var dis) ? dis.Text : null, hit: hit);
    }

    private Node BuildSwitch(PrototypeNode n)
    {
        var p = n.Properties;
        var name = n.Bind;
        Action<bool>? change = name is null ? null : v => _state.Set(name, v);
        return B.Switch(name is not null && _state.GetBool(name), change, _theme,
            p.TryGetValue("label", out var l) ? l.Text : null, Flag(p, "labelBefore", false),
            p.TryGetValue("disabled", out var d) ? d.Text : null);
    }

    // ---- structured views ----

    private Node BuildTree(PrototypeNode n)
    {
        var outer = _treeBind;
        var outerReserve = _treeReservesTrailing;
        _treeBind = n.Bind;
        // When any row carries a trailing action, every row keeps its room, so the notes share one right edge.
        _treeReservesTrailing = n.Children.Any(c => c.Properties.ContainsKey("trailingIcon"));
        try { return B.VStack(BuildChildren(n)).WStar(); }
        finally { _treeBind = outer; _treeReservesTrailing = outerReserve; }
    }

    /// <summary>
    /// The ink of an unselected row's marks and notes: the dim one while it reads at 4.5:1 on every fill a row can have
    /// (the page, the panel and the hover fill), else the body text, so a note stays legible when the pointer lights its row.
    /// </summary>
    private RGBAColor32 RowDimInk()
    {
        var dim = Palette.DimText;
        return RGBAColor32.ContrastRatio(dim, Palette.ContentBg) >= 4.5f
            && RGBAColor32.ContrastRatio(dim, Palette.PanelBg) >= 4.5f
            && RGBAColor32.ContrastRatio(dim, Palette.Separator) >= 4.5f
            ? dim : Palette.BodyText;
    }

    private Node BuildTreeRow(PrototypeNode n)
    {
        var p = n.Properties;
        var metrics = _theme.Metrics;
        var size = metrics.BaseFontSize;
        var key = Str(p, "key", null!);
        var bind = _treeBind;
        var selected = bind is not null && key is not null && _state.Get(bind) == key;
        // On the Selection fill the dim ink is too faint (about 2.7:1 in the dark palette), so a selected row's marks and
        // notes take the selection ink, which is measured against that fill.
        var dim = selected ? Palette.SelectionText : RowDimInk();
        var kids = new List<Node>();
        if (p.TryGetValue("mark", out var mark) && Enum.TryParse<IconKind>(mark.Text, out var markKind))
            kids.Add(B.Icon(markKind, size, dim));
        kids.Add(B.Text(Str(p, "text", ""), size, selected ? Palette.SelectionText : Palette.BodyText).WStar().HStar());
        if (p.TryGetValue("note", out var note))
            kids.Add(B.Text(note.Text, size, dim));
        if (p.TryGetValue("trailingIcon", out var ti) && Enum.TryParse<IconKind>(ti.Text, out var tiKind))
        {
            Node trailing = B.Icon(tiKind, size, Palette.Accent);
            if (p.TryGetValue("trailingAction", out var ta))
            {
                var act = ta.Text;
                trailing = trailing.Clickable(new HitResult.ButtonHit("tree-trailing:" + (Str(p, "key", null!) ?? n.Path)), _ => Run(act), CursorKind.Pointer);
            }
            // Shown on hover and on the selected row; it keeps its room hidden, so nothing moves.
            kids.Add(trailing.RevealOnRowHover());
        }
        else if (_treeReservesTrailing)
        {
            kids.Add(B.Spacer().WFixed(size).HStar());
        }
        // The row's own padding on the right, so a trailing action or a note is never on the edge or under the ring.
        kids.Add(B.Spacer().WFixed(metrics.Padding).HStar());

        var content = B.HStack([.. kids]).WithGap(metrics.Padding * 0.5f).CrossCenter();
        var row = B.TreeRow((int)Num(p, "depth", 0f), false, false, null, content,
            new TreeRowStyle(Palette.DimText), size).HFixed(metrics.ItemHeight).WStar();

        var action = n.Action;
        row = row.Clickable(new HitResult.ButtonHit("tree-row:" + (key ?? n.Path)), _ =>
        {
            if (bind is not null && key is not null) _state.Set(bind, key);
            if (action is not null) Run(action);
        }).BgHover(Palette.Separator);
        if (selected)
            row = row.Selected().Bg(Palette.Selection).Border(Palette.Accent, metrics.ControlEdgeWidth, 0f);
        return row;
    }

    private Node BuildTabs(PrototypeNode n)
    {
        var metrics = _theme.Metrics;
        var options = n.Properties.TryGetValue("options", out var o) ? o.List : [];
        var items = new TabItem<string>[options.Length];
        for (int i = 0; i < items.Length; i++) items[i] = new TabItem<string>(options[i], options[i]) { Tooltip = options[i] };
        var name = n.Bind;
        var current = name is null ? "" : _state.Get(name);
        var active = Array.IndexOf([.. options], current);
        // A state not set yet shows the first tab, as a real tab bar opens on one.
        if (active < 0 && current.Length == 0 && options.Length > 0) active = 0;

        var fontSize = metrics.BaseFontSize;
        Func<string, float> measure = MeasureLabel is { } m
            // One padding of slack: a label measured to the pixel lands exactly on its tab's edge and the
            // text fit ellipsises it, which cut "Project" to "Proj..." with 260 px of strip to spare.
            ? label => m(label, fontSize) + metrics.Padding
            : label => label.Length * fontSize * TabCharEm;
        var strip = new TabStripOptions
        {
            // Bottom puts the accent under the tab: the selected tab is underlined.
            Side = TabStripSide.Bottom,
            Metrics = new TabStripMetrics(metrics.ItemHeight, fontSize, metrics.Padding, metrics.ControlEdgeWidth, 0f, 0f, 0f, float.MaxValue),
            Colors = TabBarColors.FromPalette(Palette) with { ActiveAccent = Palette.Accent },
            CanCloseTabs = false,
        };
        var available = DesignSize?.Width ?? 1_000_000f;
        return TabStripTree.Build(items, active, null, null, available, measure, strip,
            i => { if (name is not null) _state.Set(name, options[i]); }).Root.HFixed(metrics.ItemHeight);
    }

    private Node BuildTable(PrototypeNode n)
    {
        var p = n.Properties;
        var metrics = _theme.Metrics;
        var titles = p.TryGetValue("columns", out var c) ? c.List : [];
        var widths = p.TryGetValue("columnWidths", out var w) ? w.List : [];
        var columns = new TableColumn[titles.Length];
        for (int i = 0; i < columns.Length; i++)
            columns[i] = new TableColumn(titles[i], i < widths.Length ? SizingOfWord(widths[i]) : Sizing.Star()) { Sortable = false };

        var outerColumns = _tableColumns;
        var outerStyle = _tableStyle;
        _tableColumns = columns;
        _tableStyle = new TableStyle(Palette.HeaderBg, Palette.HeaderText, Palette.Separator, Palette.Accent)
        {
            Gap = metrics.Padding,
            PadX = metrics.Padding,
        };
        try
        {
            var rows = new List<Node> { B.TableHeader(columns, null, null, _tableStyle, metrics.BaseFontSize).HFixed(metrics.ItemHeight).WStar() };
            rows.AddRange(BuildChildren(n));
            return B.VStack([.. rows]).WStar();
        }
        finally
        {
            _tableColumns = outerColumns;
            _tableStyle = outerStyle;
        }
    }

    private Node BuildTableRow(PrototypeNode n)
        => B.TableRow(_tableColumns, BuildChildren(n), _tableStyle).HFixed(_theme.Metrics.ItemHeight).WStar()
            .BgHover(_tableStyle.HoverFill);

    private static Sizing SizingOfWord(string word)
    {
        if (double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var fixedWidth)) return Sizing.Fixed((float)fixedWidth);
        if (word == "auto") return Sizing.Auto;
        if (word.StartsWith("star:", StringComparison.Ordinal)
            && float.TryParse(word.AsSpan(5), NumberStyles.Float, CultureInfo.InvariantCulture, out var weight))
            return Sizing.Star(weight);
        return Sizing.Star();
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
        if (p.TryGetValue("hoverBackground", out _) && Col(p, "hoverBackground") is { } hover) node = node.BgHover(hover);
        if (Flag(p, "revealOnHover", false)) node = node.RevealOnRowHover();
        if (n.SelectedWhen is not null && _state.WhenHolds(n.SelectedWhen))
            node = node.Selected().Bg(Palette.Selection).Border(Palette.Accent, _theme.Metrics.ControlEdgeWidth, radius);
        if (p.TryGetValue("tooltip", out var tip)) node = node.WithTooltip(tip.Text);
        if (n.DoubleAction is { } dbl)
            node = node.DoubleClickable(_ => Run(dbl)).Activatable(_ => Run(dbl));

        // A modal's action is its scrim dismissal, and a bound input owns its own press.
        if (n.Action is { } action && n.Kind is not ("modal" or "button" or "treeRow") && n.Bind is null)
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
