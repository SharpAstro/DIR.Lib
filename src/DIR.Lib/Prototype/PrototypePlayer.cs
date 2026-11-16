using System;
using System.Linq;
using DIR.Lib.Layout;

namespace DIR.Lib.Prototyping;

/// <summary>
/// Plays a loaded <see cref="PrototypeDocument"/> as an ordinary widget: it holds the current screen and the
/// <see cref="State"/>, builds the screen's tree every frame (<see cref="PrototypeTreeBuilder"/>) and paints it
/// through the usual layout path, so its clicks are <see cref="ClickableRegion"/>s that
/// <see cref="InputRouter"/> reaches like any widget's. A host adds no dispatcher: it paints the player, lists it
/// among the router's widgets and forwards input.
/// </summary>
/// <remarks>
/// The document's design size is scaled to the surface uniformly (the smaller of the two ratios, as a
/// <see cref="DesignScale"/>) and centred, so the prototype keeps its proportions on any surface.
/// </remarks>
public sealed class PrototypePlayer<TSurface> : PixelWidgetBase<TSurface>
{
    private readonly PrototypeTreeBuilder _builder;
    private PrototypeScreen _screen;
    private PrototypeTarget? _target;

    /// <summary>Creates a player on the document's entry screen.</summary>
    public PrototypePlayer(Renderer<TSurface> renderer, PrototypeDocument document, UiTheme theme, string fontPath)
        : base(renderer)
    {
        Document = document;
        Theme = theme;
        FontPath = fontPath;
        State = new PrototypeState();
        _builder = new PrototypeTreeBuilder(theme, State, OnAction) { DesignSize = (document.Width, document.Height) };
        _screen = Find(document.Entry) ?? document.Screens[0];
    }

    /// <summary>The document being played.</summary>
    public PrototypeDocument Document { get; }

    /// <summary>
    /// The target to draw at; null draws at the document's own width and height. Setting it makes the design size
    /// the target's width and height, so the read-only <c>target</c> state follows it.
    /// </summary>
    public PrototypeTarget? Target
    {
        get => _target;
        set
        {
            _target = value;
            _builder.DesignSize = DesignSize;
        }
    }

    /// <summary>The design size in use: the <see cref="Target"/>'s, else the document's.</summary>
    public (float Width, float Height) DesignSize
        => _target is { } t ? (t.Width, t.Height) : (Document.Width, Document.Height);

    /// <summary>The theme the screens are built with.</summary>
    public UiTheme Theme { get; }

    /// <summary>The values screens read and write.</summary>
    public PrototypeState State { get; }

    /// <summary>The screen on show.</summary>
    public PrototypeScreen CurrentScreen => _screen;

    /// <summary>Raised after <see cref="Goto"/> moves to another screen.</summary>
    public event Action<PrototypeScreen>? ScreenChanged;

    /// <summary>The tree builder, for a host that wants the persistent field state.</summary>
    public PrototypeTreeBuilder TreeBuilder => _builder;

    /// <summary>Moves to the screen with this id; false (and no move) when there is none.</summary>
    public bool Goto(string id)
    {
        if (Find(id) is not { } next) return false;
        if (!ReferenceEquals(next, _screen))
        {
            _screen = next;
            ScreenChanged?.Invoke(next);
        }
        return true;
    }

    /// <summary>Builds the current screen's tree for the current state, without painting.</summary>
    public Node BuildTree()
    {
        return _builder.Build(_screen);
    }

    /// <summary>The surface rect the design area occupies inside <paramref name="bounds"/>, and its scale.</summary>
    public (RectF32 Rect, DesignScale Scale) Fit(RectF32 bounds)
    {
        var (dw, dh) = DesignSize;
        var s = Math.Min(bounds.Width / dw, bounds.Height / dh);
        if (!(s > 0f)) s = 1f;
        var w = dw * s;
        var h = dh * s;
        return (new RectF32(bounds.X + (bounds.Width - w) / 2f, bounds.Y + (bounds.Height - h) / 2f, w, h), new DesignScale(s));
    }

    /// <summary>Paints the current screen into <paramref name="bounds"/>, one frame.</summary>
    public void Render(RectF32 bounds)
    {
        BeginFrame();
        var (rect, scale) = Fit(bounds);
        RenderLayout(BuildTree(), rect, FontPath, scale);
    }

    private void OnAction(PrototypeAction action)
    {
        if (action.Kind == PrototypeActionKind.Goto) Goto(action.Target);
    }

    private PrototypeScreen? Find(string id) => Document.Screens.FirstOrDefault(s => s.Id == id);
}
