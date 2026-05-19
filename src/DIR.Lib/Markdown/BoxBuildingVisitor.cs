using System;
using System.Collections.Generic;
using LALR.CC.LexicalGrammar;
using DIR.Lib;
using DIR.Lib.MathLayout;
using static DIR.Lib.Latex;

namespace DIR.Lib.Markdown;

/// <summary>
/// IVisitor implementation: maps each Latex AST record to a deferred
/// <see cref="Box"/> builder — a <c>Func&lt;BoxStyle, Box&gt;</c> that materialises
/// the subtree at whatever style its caller chooses. Pairs with
/// <see cref="BoxRenderer"/>, which paints the materialised Box into an RGBA
/// buffer for sixel/sextant/half-block output.
///
/// <para><b>Why deferred:</b> the visitor runs bottom-up at parse time, so when
/// <see cref="Visit(Sup)"/> finally sees an exponent its sub-tree was already
/// materialised by the parent style. To honour TeX's script-size shrink we need
/// the entire exponent — atom *or* composite — rebuilt at <see cref="BoxStyle.Smaller"/>.
/// Returning thunks instead of boxes pushes construction past the parse so
/// <see cref="Visit(Sup)"/> / <see cref="Visit(Subscript)"/> can invoke the
/// child builder with the smaller style; every nested <c>new GlyphBox</c>,
/// kern, <see cref="FracBox"/>, <see cref="BracketBox"/>, etc. inside the
/// closure then picks up the smaller size automatically.</para>
///
/// <para>Atoms (Number / Variable / Command) build a <see cref="GlyphBox"/> at
/// the supplied style. Composites (Add / Sub / Eq / Mul / Div / Juxt) combine
/// child builders with <see cref="HBox"/> + small inter-token kerns sized off
/// the supplied style. Scripts use <see cref="SupSubBox"/> with the script
/// builder invoked at <c>style.Smaller()</c>. <see cref="Frac"/>, <see cref="Sqrt"/>,
/// brackets, and the big-operator scaffold build the obvious dedicated boxes.</para>
///
/// <para>Because the AST records carry <see cref="Item"/> children and the
/// visitor lets us return any T from each Visit overload, the parser threads
/// already-built sub-builders up the tree via <c>Item.Content</c>. Atoms read
/// the raw lexer-matched bytes from <c>Arg0.Content</c>.</para>
///
/// <para>Lifted from the LALR.CC LatexConsole example so Console.Lib can render
/// display math (Markdig <c>MathBlock</c> nodes) inline with the rest of a
/// rendered markdown document. The example app now lives in this repo under
/// examples/LatexConsole/ and just demonstrates the renderer in isolation.</para>
/// </summary>
public sealed class BoxBuildingVisitor : IVisitor<Func<BoxStyle, Box>>
{
    /// <summary>Default style — applied when an external caller materialises
    /// the top-level builder. Scripts re-enter their builders at
    /// <c>Style.Smaller()</c>; that smaller value flows through the closure
    /// chain rather than being stored here.</summary>
    public BoxStyle Style { get; }

    public BoxBuildingVisitor(BoxStyle style)
    {
        Style = style;
    }

    /// <summary>Materialise the top-level builder returned by the parser at
    /// this visitor's <see cref="Style"/>. Call sites that received a parse
    /// result's <c>Item.Content</c> can use this to recover a <see cref="Box"/>
    /// without knowing the deferred-builder shape.</summary>
    public Box Build(object content) => ((Func<BoxStyle, Box>)content)(Style);

    private static Func<BoxStyle, Box> Builder(Item item) => (Func<BoxStyle, Box>)item.Content;

    private static Func<BoxStyle, Box> BinaryOp(
        Func<BoxStyle, Box> left, string op, Func<BoxStyle, Box> right, float kernEm = 0.25f) =>
        style =>
        {
            var kern = new KernBox(style.FontSize * kernEm);
            return new HBox(left(style), kern, new GlyphBox(op, style), kern, right(style));
        };

    public Func<BoxStyle, Box> Visit(Add node)      => BinaryOp(Builder(node.Arg0), "+", Builder(node.Arg2));
    public Func<BoxStyle, Box> Visit(Subtract node) => BinaryOp(Builder(node.Arg0), "−", Builder(node.Arg2)); // U+2212 minus
    public Func<BoxStyle, Box> Visit(Eq node)       => BinaryOp(Builder(node.Arg0), "=", Builder(node.Arg2), 0.35f);
    public Func<BoxStyle, Box> Visit(Mul node)      => BinaryOp(Builder(node.Arg0), "·", Builder(node.Arg2), 0.15f); // U+00B7 dot
    public Func<BoxStyle, Box> Visit(Div node)      => BinaryOp(Builder(node.Arg0), "/", Builder(node.Arg2));

    /// <summary>
    /// Binary relation (\approx \leq \geq \neq \equiv \ll \gg \in \notin
    /// \subset \to \leftarrow \rightarrow \pm \mp). Looks up the bare glyph
    /// via <see cref="RenderCommand"/> then uses the standard relation
    /// kerning of 0.35em (matching TeX's <c>\thickmuskip</c>-style
    /// surrounding space for <c>\mathrel</c>). Falls back to the raw token
    /// bytes for any rel name we haven't mapped, so a typo surfaces
    /// visibly rather than rendering as a strange unicode glyph.
    /// </summary>
    public Func<BoxStyle, Box> Visit(Rel node)
    {
        var raw = (string)node.Arg1.Content;
        var name = raw.Length > 1 && raw[0] == '\\' ? raw.Substring(1) : raw;
        var glyph = Commands.TryGetValue(name, out var g) ? g : raw;
        return BinaryOp(Builder(node.Arg0), glyph, Builder(node.Arg2), 0.35f);
    }

    /// <summary>Implicit multiplication ("xy", "n(n+1)") — a tiny kern, no operator.</summary>
    public Func<BoxStyle, Box> Visit(Juxt node)
    {
        var left = Builder(node.Arg0);
        var right = Builder(node.Arg1);
        return style => new HBox(new KernBox(style.FontSize * 0.05f), left(style), right(style));
    }

    public Func<BoxStyle, Box> Visit(Neg node)
    {
        var inner = Builder(node.Arg1);
        return style => new HBox(new GlyphBox("−", style), inner(style));
    }

    /// <summary>
    /// Postfix superscript. For ordinary atoms, builds a right-of-operator
    /// <see cref="SupSubBox"/>. For big operators (∫ ∑ ∏ etc.), folds into
    /// a <see cref="LimitsBox"/> so the script lands above the operator —
    /// what TeX does in display style for <c>\int^a</c>, <c>\sum^n</c>.
    /// Stacking <c>\int_0^\infty</c> works because each Sup/Subscript visit
    /// preserves the <see cref="BigOpScaffold"/>: the first script wraps the
    /// bare operator into a scaffold, the second slots into the same
    /// scaffold's other slot.
    ///
    /// <para>The exponent's builder is invoked at <c>style.Smaller()</c>, so
    /// composite exponents — <c>x^{a+b}</c>, <c>(1-x)^{-1/2}</c> — recurse
    /// down the closure chain shrinking every inner glyph, kern, fraction
    /// rule, and bracket. Atom exponents (<c>x^2</c>) get the same shrink
    /// for free.</para>
    /// </summary>
    public Func<BoxStyle, Box> Visit(Sup node)
    {
        var baseBuilder = Builder(node.Arg0);
        var supBuilder = Builder(node.Arg2);
        return style =>
        {
            var baseBox = baseBuilder(style);
            var smaller = style.Smaller();
            if (baseBox is BigOpScaffold scaffold && !scaffold.HasUpper)
                return scaffold.WithUpper(supBuilder(smaller));
            // Merge with a sub-only SupSubBox so combined sub+sup on the
            // same base stack vertically rather than cascading to the
            // right. The LR(1) grammar's leftmost reduction always emits
            // X_a^b as `Sup(Subscript(X, a), b)` — a nested chain — so
            // any TeX-style stacked-script formula (\ce{SO4^{2-}},
            // chemistry isotopes via the \null trick) lands here.
            if (baseBox is SupSubBox supSub && supSub.Sub is not null && supSub.Sup is null)
                return new SupSubBox(supSub.Base, sup: supBuilder(smaller), sub: supSub.Sub, style);
            return new SupSubBox(baseBox, sup: supBuilder(smaller), sub: null, style);
        };
    }

    /// <summary>Postfix subscript. Mirror of <see cref="Visit(Sup)"/>.</summary>
    public Func<BoxStyle, Box> Visit(Subscript node)
    {
        var baseBuilder = Builder(node.Arg0);
        var subBuilder = Builder(node.Arg2);
        return style =>
        {
            var baseBox = baseBuilder(style);
            var smaller = style.Smaller();
            if (baseBox is BigOpScaffold scaffold && !scaffold.HasLower)
                return scaffold.WithLower(subBuilder(smaller));
            // Symmetric merge for `X^a_b` ordering — collapse a sup-only
            // SupSubBox + outer sub into a single stacked SupSubBox so
            // the scripts share a baseline pair.
            if (baseBox is SupSubBox supSub && supSub.Sup is not null && supSub.Sub is null)
                return new SupSubBox(supSub.Base, sup: supSub.Sup, sub: subBuilder(smaller), style);
            return new SupSubBox(baseBox, sup: null, sub: subBuilder(smaller), style);
        };
    }

    public Func<BoxStyle, Box> Visit(Number node)
    {
        var text = (string)node.Arg0.Content;
        return style => new GlyphBox(text, style);
    }

    public Func<BoxStyle, Box> Visit(Variable node)
    {
        var text = (string)node.Arg0.Content;
        return style => new GlyphBox(text, style);
    }

    /// <summary>
    /// \name commands. Greek + symbol lookups produce a single Unicode glyph;
    /// function names like \sin become an upright-style multi-letter atom;
    /// unknown commands fall back to the raw \name text so it's debuggable.
    /// </summary>
    public Func<BoxStyle, Box> Visit(Command node)
    {
        var raw = (string)node.Arg0.Content;
        var name = raw.Length > 1 && raw[0] == '\\' ? raw.Substring(1) : raw;
        var glyph = Commands.TryGetValue(name, out var g) ? g : raw;
        if (LimitOps.Contains(name))
        {
            // Display-style big operator: render the glyph at 1.5x and wrap
            // in a scaffold so subsequent _/^ scripts fold into a LimitsBox
            // (limits above/below) instead of a SupSubBox (right-of).
            return style => new BigOpScaffold(new GlyphBox(glyph, style, style.FontSize * 1.5f), null, null, style);
        }
        return style => new GlyphBox(glyph, style);
    }

    /// <summary>(E) — render a parenthesised expression with scalable parens.</summary>
    public Func<BoxStyle, Box> Visit(Paren node)
    {
        var inner = Builder(node.Arg1);
        return style => new BracketBox(inner(style), BracketKind.Paren, style);
    }

    /// <summary>{ E } in LaTeX is an invisible group — render the inner E unchanged.</summary>
    public Func<BoxStyle, Box> Visit(Group node) => Builder(node.Arg1);

    public Func<BoxStyle, Box> Visit(Sqrt node)
    {
        var radicand = Builder(node.Arg1);
        return style => new SqrtBox(radicand(style), style);
    }

    public Func<BoxStyle, Box> Visit(Frac node)
    {
        var num = Builder(node.Arg1);
        var den = Builder(node.Arg2);
        return style => new FracBox(num(style), den(style), style);
    }

    private static readonly Dictionary<string, string> Commands = new(System.StringComparer.Ordinal)
    {
        // Lowercase Greek
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ",
        ["epsilon"] = "ε", ["zeta"] = "ζ", ["eta"] = "η", ["theta"] = "θ",
        ["iota"] = "ι", ["kappa"] = "κ", ["lambda"] = "λ", ["mu"] = "μ",
        ["nu"] = "ν", ["xi"] = "ξ", ["pi"] = "π", ["rho"] = "ρ",
        ["sigma"] = "σ", ["tau"] = "τ", ["upsilon"] = "υ", ["phi"] = "φ",
        ["chi"] = "χ", ["psi"] = "ψ", ["omega"] = "ω",
        // Uppercase Greek (ones that aren't Latin lookalikes)
        ["Gamma"] = "Γ", ["Delta"] = "Δ", ["Theta"] = "Θ", ["Lambda"] = "Λ",
        ["Xi"] = "Ξ", ["Pi"] = "Π", ["Sigma"] = "Σ", ["Phi"] = "Φ",
        ["Psi"] = "Ψ", ["Omega"] = "Ω",
        // Function names — kept multi-letter; rendered as upright run.
        ["sin"] = "sin", ["cos"] = "cos", ["tan"] = "tan",
        ["sec"] = "sec", ["csc"] = "csc", ["cot"] = "cot",
        ["arcsin"] = "arcsin", ["arccos"] = "arccos", ["arctan"] = "arctan",
        ["sinh"] = "sinh", ["cosh"] = "cosh", ["tanh"] = "tanh",
        ["log"] = "log", ["ln"] = "ln", ["exp"] = "exp",
        ["lim"] = "lim", ["max"] = "max", ["min"] = "min",
        ["det"] = "det", ["dim"] = "dim", ["gcd"] = "gcd",
        // Big operators
        ["sum"] = "∑", ["prod"] = "∏", ["int"] = "∫", ["oint"] = "∮",
        ["bigcup"] = "⋃", ["bigcap"] = "⋂",
        // Constants and relation symbols
        ["infty"] = "∞", ["partial"] = "∂", ["nabla"] = "∇",
        ["pm"] = "±", ["mp"] = "∓",
        ["to"] = "→", ["leftarrow"] = "←", ["rightarrow"] = "→",
        // Chemistry arrows. \rightleftharpoons (⇌) is the canonical
        // equilibrium symbol; \leftrightarrow (↔) covers both resonance
        // notation in chemistry and bidirectional implication elsewhere.
        ["rightleftharpoons"] = "⇌", ["leftrightarrow"] = "↔",
        ["leq"] = "≤", ["geq"] = "≥", ["neq"] = "≠",
        ["approx"] = "≈", ["equiv"] = "≡",
        ["in"] = "∈", ["notin"] = "∉", ["subset"] = "⊂",
        ["cup"] = "∪", ["cap"] = "∩",
        // Arithmetic operators (\cdot / \times are also lexer-mapped to *).
        ["div"] = "÷", ["cdot"] = "·",
        // Sign atoms used by mhchem-emitted LaTeX. The math grammar treats
        // bare + / - as binary operators, so they can't appear standalone
        // inside a script group (e.g. `Cl^{-}` would fail to parse the `-`).
        // Mhchem.ToLatex rewrites the signs in superscript/subscript content
        // to \plus / \minus commands; these entries provide the rendered
        // glyphs. U+2212 minus sign for typographic correctness.
        ["plus"] = "+", ["minus"] = "−",
        // Zero-width "null" atom used by mhchem-emitted LaTeX for prefix
        // isotope notation: \ce{^{238}U} → \null^{238}U so the script
        // attaches to a zero-width base (the grammar's P → P op A
        // requires a left operand). The empty glyph + the sub+sup merge
        // in Visit(Sup) / Visit(Subscript) collapse \null^{14}_{6}C into
        // a stacked-script box, then juxtaposition glues C on the right
        // — laying out ¹⁴₆C with both scripts pre-attached to C, the
        // chemistry convention.
        ["null"] = "",
        // LaTeX spacing macros. Inside a box-rendered formula these become
        // invisible kerns rather than literal spaces — the layout system
        // already spaces atoms appropriately; just keep the glyph empty.
        ["quad"] = " ", ["qquad"] = "  ",
        // Ellipsis variants — \dots is the catch-all model's default, the
        // others pick a specific orientation.
        ["dots"] = "…", ["ldots"] = "…", ["cdots"] = "⋯",
        ["vdots"] = "⋮", ["ddots"] = "⋱",
        // Much-less-than / much-greater-than relations.
        ["ll"] = "≪", ["gg"] = "≫",
    };

    /// <summary>
    /// Commands that take their <c>_</c>/<c>^</c> as below/above limits in
    /// display style (the LimitsBox rendering) rather than as right-of
    /// scripts. Big operators (∑ ∏ ∫ ∮ ⋃ ⋂) plus the limit-style functions
    /// (lim sup inf max min etc.).
    /// </summary>
    private static readonly HashSet<string> LimitOps = new(System.StringComparer.Ordinal)
    {
        "sum", "prod", "int", "oint", "bigcup", "bigcap",
        "lim", "limsup", "liminf", "max", "min", "sup", "inf",
        "argmax", "argmin", "det", "gcd",
    };

    /// <summary>
    /// Transient marker box: a big operator that hasn't yet absorbed its
    /// scripts. <see cref="Visit(Sup)"/> and <see cref="Visit(Subscript)"/>
    /// look for this on their base — if found, they fold the script into this
    /// scaffold's empty slot and return a new scaffold (still itself a Box,
    /// materialising into a <see cref="LimitsBox"/> on demand). The double-
    /// script case <c>\int_0^\infty</c> walks Subscript-then-Sup (or Sup-then-
    /// Subscript) over the same scaffold, ending with both slots filled
    /// before any consumer asks for Width/Height/Draw.
    ///
    /// Standalone <c>\sum</c> (no scripts) flows through unchanged: the
    /// scaffold materialises to a LimitsBox with both slots null, which just
    /// renders the centred operator with no limits.
    /// </summary>
    private sealed class BigOpScaffold : Box
    {
        private readonly Box _base;
        private readonly Box? _lower;
        private readonly Box? _upper;
        private readonly BoxStyle _style;
        private LimitsBox? _materialized;

        public BigOpScaffold(Box @base, Box? lower, Box? upper, BoxStyle style)
        {
            _base = @base;
            _lower = lower;
            _upper = upper;
            _style = style;
        }

        public bool HasLower => _lower is not null;
        public bool HasUpper => _upper is not null;

        public BigOpScaffold WithLower(Box lower) => new(_base, lower, _upper, _style);
        public BigOpScaffold WithUpper(Box upper) => new(_base, _lower, upper, _style);

        private LimitsBox Materialize() => _materialized ??= new LimitsBox(_base, _lower, _upper, _style);

        public override float Width => Materialize().Width;
        public override float Height => Materialize().Height;
        public override float Depth => Materialize().Depth;

        public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
            => Materialize().Draw(renderer, penX, baselineY, style);
    }
}
