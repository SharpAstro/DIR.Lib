using System;
using System.Collections.Generic;
using LALR.CC.LexicalGrammar;
using DIR.Lib;
using DIR.Lib.MathLayout;
using SharpAstro.Fonts;
using static DIR.Lib.Latex;
using AtomClass = DIR.Lib.Markdown.MathAtomClass;

namespace DIR.Lib.Markdown;

/// <summary>
/// IVisitor implementation: maps each Latex AST record to a deferred
/// <see cref="Box"/> builder — a <c>Func&lt;BoxStyle, Box&gt;</c> that materialises
/// the subtree at whatever style its caller chooses. Pairs with
/// <see cref="BoxRasterizer"/>, which paints the materialised Box into an RGBA
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
/// <para>Atoms build a glyph box at the supplied style: a Variable a
/// math-italic <see cref="MathGlyphBox"/> (upright inside <c>\mathrm</c>),
/// a Number or a Command an upright <see cref="GlyphBox"/>. Composites (Add / Sub / Eq / Mul / Div) combine
/// child builders with <see cref="HBox"/> + small inter-token kerns sized off
/// the supplied style; Juxt adds only the thin space TeX puts round an operator
/// name. Scripts use <see cref="SupSubBox"/> with the script
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

    /// <summary>
    /// Left, the operator, right, with <paramref name="kernEm"/> either side.
    /// Against an empty side (an operand
    /// <see cref="MarkdownMacros.SupplyMissingOperands"/> supplied, which has
    /// no width) a binary operator takes no space at all, being ordinary there
    /// in TeX, and a relation none on that side. <paramref name="aloneOp"/>,
    /// when given, is drawn instead of <paramref name="op"/> there.
    /// </summary>
    private static Func<BoxStyle, Box> BinaryOp(
        Func<BoxStyle, Box> left, string op, Func<BoxStyle, Box> right, float kernEm = 0.25f, bool binary = true,
        string? aloneOp = null) =>
        style =>
        {
            var l = left(style);
            var r = right(style);
            var kern = new KernBox(style.FontSize * kernEm);
            var alone = l.Width <= 0 || r.Width <= 0;
            var parts = new List<Box>(5);
            if (l.Width > 0) parts.Add(l);
            if (l.Width > 0 && !(binary && alone)) parts.Add(kern);
            parts.Add(new GlyphBox(alone && aloneOp is not null ? aloneOp : op, style));
            if (r.Width > 0 && !(binary && alone)) parts.Add(kern);
            if (r.Width > 0) parts.Add(r);
            return new HBox(parts.ToArray());
        };

    public Func<BoxStyle, Box> Visit(Add node)      => BinaryOp(Builder(node.Arg0), "+", Builder(node.Arg2));
    public Func<BoxStyle, Box> Visit(Subtract node) => BinaryOp(Builder(node.Arg0), "−", Builder(node.Arg2)); // U+2212 minus
    public Func<BoxStyle, Box> Visit(Eq node)       => BinaryOp(Builder(node.Arg0), "=", Builder(node.Arg2), 0.35f, binary: false);
    public Func<BoxStyle, Box> Visit(Div node)      => BinaryOp(Builder(node.Arg0), "/", Builder(node.Arg2));

    /// <summary>\times is ×, with TeX's medium space (4 mu) either side, as
    /// for any binary operator; * and \cdot stay the tight middle dot. \times
    /// used to draw the dot too, so 7 \times 7 read 7·7. A * with nothing on a
    /// side is an asterisk, TeX's \ast (x^{*}), not the dot of a product.</summary>
    public Func<BoxStyle, Box> Visit(Mul node) => (string)node.Arg1.Content switch
    {
        @"\times" => BinaryOp(Builder(node.Arg0), "×", Builder(node.Arg2), 4f / 18f),
        "*" => BinaryOp(Builder(node.Arg0), "·", Builder(node.Arg2), 0.15f, aloneOp: "∗"),
        _ => BinaryOp(Builder(node.Arg0), "·", Builder(node.Arg2), 0.15f), // U+00B7 dot
    };

    /// <summary>
    /// Binary relation (\approx \leq \geq \neq \equiv \ll \gg \in \notin
    /// \subset \to \leftarrow \rightarrow \pm \mp). Looks up the bare glyph
    /// via <see cref="Commands"/> then uses the standard relation
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
        // \pm and \mp lex as relations but are binary operators in TeX: ±0.2.
        return BinaryOp(Builder(node.Arg0), glyph, Builder(node.Arg2), 0.35f, binary: name is "pm" or "mp");
    }

    /// <summary>
    /// Implicit multiplication ("xy", "n(n+1)", "\sin x"): the two sides
    /// side by side, with the space TeX puts between the atoms that meet
    /// there (<see cref="MarkdownMacros.ThinSpaceBetween"/>), which for two
    /// ordinary atoms is none. It used to put 0.05 em before every
    /// juxtaposition, and they nest to the left, so <c>1, 2, 3</c> carried
    /// 0.2 em in front of its 1. Punctuation carries its own space
    /// (<see cref="Visit(Punct)"/>).
    /// </summary>
    public Func<BoxStyle, Box> Visit(Juxt node)
    {
        var left = Builder(node.Arg0);
        var right = Builder(node.Arg1);
        return style =>
        {
            var l = left(style);
            var r = right(style);
            return MarkdownMacros.ThinSpaceBetween(RightClass(l), LeftClass(r))
                ? new HBox(l, new KernBox(style.FontSize / 6f), r)
                : new HBox(l, r);
        };
    }

    /// <summary>The class of the first atom in <paramref name="box"/>. A
    /// scripted atom keeps its nucleus's class, as in TeX.</summary>
    private static AtomClass LeftClass(Box box) => box switch
    {
        AtomBox atom => atom.Class,
        BigOpScaffold => AtomClass.Op,
        SupSubBox scripted => LeftClass(scripted.Base),
        BracketBox or DelimiterPairBox => AtomClass.Open,
        HBox row => EdgeClass(row.Children, LeftClass, fromEnd: false),
        KernBox => AtomClass.Glue,
        _ => AtomClass.Ord,
    };

    /// <summary>The class of the last atom in <paramref name="box"/>.</summary>
    private static AtomClass RightClass(Box box) => box switch
    {
        AtomBox atom => atom.Class,
        BigOpScaffold => AtomClass.Op,
        SupSubBox scripted => RightClass(scripted.Base),
        BracketBox or DelimiterPairBox => AtomClass.Close,
        HBox row => EdgeClass(row.Children, RightClass, fromEnd: true),
        KernBox => AtomClass.Glue,
        _ => AtomClass.Ord,
    };

    /// <summary>
    /// The class of a row's first (or last) atom. Space is not an atom, so a
    /// kern at that end is passed over, as TeX passes over glue: in
    /// <c>\sin\,x</c> the x still follows an Op, and gets its thin space on
    /// top of the <c>\,</c>. A row of nothing but space is space.
    /// </summary>
    private static AtomClass EdgeClass(IReadOnlyList<Box> children, Func<Box, AtomClass> classOf, bool fromEnd)
    {
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[fromEnd ? children.Count - 1 - i : i];
            if (child is not KernBox) return classOf(child);
        }
        return children.Count == 0 ? AtomClass.Ord : AtomClass.Glue;
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

    /// <summary>
    /// A number, upright. Inside <c>\mathbb{...}</c> its digits are
    /// double-struck where the font has them, as unicode-math sets them.
    /// </summary>
    public Func<BoxStyle, Box> Visit(Number node)
    {
        var text = (string)node.Arg0.Content;
        return style => t_alphabet is { } alphabet and not MathStyle.Normal
            ? new MathGlyphBox(text, style, alphabet)
            : new GlyphBox(text, style);
    }

    /// <summary>
    /// A single-letter variable, set in math italic as TeX sets it: a
    /// <see cref="MathGlyphBox"/> remaps the letter to its U+1D400-block
    /// italic (or ℎ U+210E for h) when the font's cmap has it, and leaves it
    /// upright in a font that lacks the block. Inside <c>\mathrm{...}</c>
    /// (see <see cref="Visit(MathRm)"/>) the letter stays upright, and inside
    /// <c>\mathbb{...}</c> it is double-struck, falling back the same way.
    /// </summary>
    public Func<BoxStyle, Box> Visit(Variable node)
    {
        var text = (string)node.Arg0.Content;
        return style => t_alphabet switch
        {
            null => new MathGlyphBox(text, style, MathStyle.Italic),
            MathStyle.Normal => new GlyphBox(text, style),
            { } alphabet => new MathGlyphBox(text, style, alphabet),
        };
    }

    /// <summary>
    /// The math alphabet a <c>\mathrm</c> or <c>\mathbb</c> argument is
    /// being built in, or null outside one, where letters are math italic.
    /// TeX's alphabet commands switch the math family for the rest of their
    /// group, which is a dynamic scope, and so is this: it is set around the
    /// argument builder's call, so every variable that builder reaches, at
    /// any depth (<c>\mathrm{Na}</c>, <c>\mathrm{e^{x}}</c>), takes it, and
    /// the innermost command wins. It is read only when a builder runs, never
    /// while parsing, and it is per thread because a builder runs to
    /// completion on the thread that calls it.
    /// </summary>
    [ThreadStatic]
    private static MathStyle? t_alphabet;

    /// <summary><paramref name="inner"/>, built with its letters in
    /// <paramref name="alphabet"/> (see <see cref="t_alphabet"/>).</summary>
    private static Func<BoxStyle, Box> InAlphabet(MathStyle alphabet, Func<BoxStyle, Box> inner) =>
        style =>
        {
            var outer = t_alphabet;
            t_alphabet = alphabet;
            try
            {
                return inner(style);
            }
            finally
            {
                t_alphabet = outer;
            }
        };

    /// <summary>
    /// <c>\mathrm{...}</c>: the argument builds exactly as it would bare,
    /// except that its variables stay upright. Digits, operator names and
    /// Greek are untouched, since they never were italic here; that matches
    /// TeX, whose <c>\mathrm</c> changes only the Latin letters.
    /// </summary>
    public Func<BoxStyle, Box> Visit(MathRm node) => InAlphabet(MathStyle.Normal, Builder(node.Arg1));

    /// <summary>
    /// <c>\mathbb{...}</c>: the argument's letters (and digits) in the
    /// double-struck alphabet, ℝ for R. A <see cref="MathGlyphBox"/> finds
    /// the letter where Unicode put it, so ℂ ℍ ℕ ℙ ℚ ℝ ℤ come from Letterlike
    /// Symbols and the rest from U+1D538, and a font without the glyph gets
    /// the plain letter, as math italic falls back.
    /// </summary>
    public Func<BoxStyle, Box> Visit(MathBb node) => InAlphabet(MathStyle.DoubleStruck, Builder(node.Arg1));

    /// <summary><c>\mathbf{X}</c>: X's letters and digits in the font's
    /// mathematical bold, as <c>\mathbb</c>'s are double-struck.</summary>
    public Func<BoxStyle, Box> Visit(MathBf node) => InAlphabet(MathStyle.Bold, Builder(node.Arg1));

    /// <summary>
    /// <c>\hat{x}</c> and the other accents: the accent's spacing glyph set
    /// over the argument by <see cref="AccentBox"/>, which centres it on the
    /// base's top-accent attachment from the font's MATH table. AccentBox was
    /// only reachable from code; the grammar had no accent, so <c>\hat</c>
    /// drew its own name.
    /// </summary>
    public Func<BoxStyle, Box> Visit(Accented node)
    {
        var glyph = MarkdownMacros.Accents[((string)node.Arg0.Content).TrimStart('\\')].Glyph;
        var @base = Builder(node.Arg1);
        return style => new AccentBox(@base(style), new GlyphBox(glyph, style), style);
    }

    /// <summary>
    /// <c>\lfloor E \rfloor</c>, <c>\lceil E \rceil</c> and
    /// <c>\langle E \rangle</c>: the pair stretched to E as parens are, with or
    /// without <c>\left</c> and <c>\right</c>
    /// (<see cref="DelimiterPairBox"/>, which takes any two codepoints).
    /// </summary>
    public Func<BoxStyle, Box> Visit(NamedPair node)
    {
        var open = MarkdownMacros.NamedDelimiters[((string)node.Arg0.Content).TrimStart('\\')];
        var close = MarkdownMacros.NamedDelimiters[((string)node.Arg2.Content).TrimStart('\\')];
        var inner = Builder(node.Arg1);
        return style => new DelimiterPairBox(inner(style), open, close, style);
    }

    /// <summary><c>x'</c>: the prime glyph after the atom. U+2032 is a
    /// spacing prime, drawn raised by the font, so it is set at the body size,
    /// as MathJax sets it with a Unicode math font.</summary>
    public Func<BoxStyle, Box> Visit(Primed node)
    {
        var @base = Builder(node.Arg0);
        var primes = MarkdownMacros.Primes(((string)node.Arg1.Content).Length);
        return style => new HBox(@base(style), new GlyphBox(primes, style));
    }

    /// <summary>
    /// <c>\operatorname{X}</c>: X as an operator name of the writer's own,
    /// one Op atom, so it is spaced like <c>\sin</c> and its scripts go to
    /// its right. <c>\operatorname*{X}</c> takes them as limits instead, as
    /// <c>\lim</c> does. X is math, as amsmath sets it, built with its letters
    /// upright: blanks in it are dropped, <c>arg\,max</c> keeps its thin
    /// space, <c>\mathbb{E}</c> is 𝔼. A name of nothing but upright glyphs is
    /// drawn as one run (<see cref="AsOneRun"/>), so <c>\operatorname{sin}</c>
    /// is exactly <c>\sin</c>.
    /// </summary>
    public Func<BoxStyle, Box> Visit(OperatorName node)
    {
        var starred = ((string)node.Arg0.Content).EndsWith('*');
        var argument = InAlphabet(MathStyle.Normal, Builder(node.Arg1));
        return style =>
        {
            var name = AsOneRun(argument(style), style);
            return starred ? new BigOpScaffold(name, null, null, style) : new AtomBox(name, AtomClass.Op);
        };
    }

    /// <summary>
    /// <paramref name="box"/> as one <see cref="GlyphBox"/> of its text when it
    /// is nothing but a row of upright glyphs at the body size, which is how
    /// a built-in name like <c>\sin</c> is drawn; anything else as it is.
    /// </summary>
    private static Box AsOneRun(Box box, BoxStyle style)
    {
        var text = new System.Text.StringBuilder();
        return AppendRun(box, text, style.FontSize) ? new GlyphBox(text.ToString(), style) : box;
    }

    private static bool AppendRun(Box box, System.Text.StringBuilder text, float fontSize)
    {
        switch (box)
        {
            case GlyphBox glyph when glyph.FontSize == fontSize:
                text.Append(glyph.Text);
                return true;
            case HBox row:
                foreach (var child in row.Children)
                    if (!AppendRun(child, text, fontSize)) return false;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// TeX's math spaces, in mu (an eighteenth of an em at the current
    /// style's size): <c>\,</c> 3, <c>\:</c> 4, <c>\;</c> 5 and <c>\!</c>
    /// minus 3. A kern, which <see cref="LeftClass"/> and
    /// <see cref="RightClass"/> pass over as space rather than an atom.
    /// </summary>
    public Func<BoxStyle, Box> Visit(MathSpace node)
    {
        var mu = ((string)node.Arg0.Content)[1] switch
        {
            ',' => 3,
            ':' => 4,
            ';' => 5,
            _ => -3,
        };
        return style => new KernBox(style.FontSize * mu / 18f);
    }

    /// <summary>
    /// <c>\textrm{X}</c>: X as upright text, an ordinary atom that keeps its
    /// blanks. The token holds the braced text (see the grammar).
    /// </summary>
    public Func<BoxStyle, Box> Visit(TextRm node)
    {
        var text = MarkdownMacros.BracedArgument((string)node.Arg0.Content);
        return style => new GlyphBox(text, style);
    }

    /// <summary>
    /// \name commands. Greek + symbol lookups produce a single Unicode glyph;
    /// function names like \sin become an upright-style multi-letter atom,
    /// an Op (see <see cref="MarkdownMacros.ThinSpaceBetween"/>), as are the big operators;
    /// unknown commands fall back to the raw \name text so it's debuggable.
    /// </summary>
    public Func<BoxStyle, Box> Visit(Command node)
    {
        var raw = (string)node.Arg0.Content;
        var name = raw.Length > 1 && raw[0] == '\\' ? raw.Substring(1) : raw;
        if (MarkdownMacros.OperatorNames.TryGetValue(name, out var opName))
        {
            // A named limit operator (\lim, \max) wraps its name, at the body
            // size as TeX draws it, in a scaffold so subsequent _/^ scripts
            // fold into a LimitsBox (limits above/below) instead of a
            // SupSubBox (right-of).
            if (MarkdownMacros.LimitOperatorNames.Contains(name))
                return style => new BigOpScaffold(new GlyphBox(opName, style), null, null, style);
            return style => new AtomBox(new GlyphBox(opName, style), AtomClass.Op);
        }
        // \quad is an em of space and \qquad two, as in TeX; a space glyph is
        // a third of that.
        if (name is "quad" or "qquad")
            return style => new KernBox(style.FontSize * (name == "quad" ? 1f : 2f));
        var glyph = Commands.TryGetValue(name, out var g) ? g : raw;
        if (BigOperators.Contains(name))
        {
            // Display-style big operator: render the glyph at 1.5x and wrap
            // in a scaffold, as for a named limit operator.
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

    /// <summary>
    /// { E } in LaTeX is an invisible group: render the inner E unchanged.
    /// To its neighbours it is one Ord atom, as in TeX, whatever its own ends
    /// are, so <c>{\sin} x</c> is set tight; a group whose ends are ordinary
    /// already is returned as it is.
    /// </summary>
    public Func<BoxStyle, Box> Visit(Group node)
    {
        var inner = Builder(node.Arg1);
        return style =>
        {
            var box = inner(style);
            return LeftClass(box) == AtomClass.Ord && RightClass(box) == AtomClass.Ord
                ? box
                : new AtomBox(box, AtomClass.Ord);
        };
    }

    /// <summary>
    /// \{ E \}, a set: a curly <see cref="BracketBox"/>, stretchy like
    /// <see cref="Visit(Paren)"/>'s parens. <c>\left\{ … \right\}</c> and
    /// <c>\lbrace … \rbrace</c> arrive here too, since
    /// <see cref="MarkdownMacros.NormalizeLatexAliases"/> reduces both to the
    /// escaped braces.
    /// </summary>
    public Func<BoxStyle, Box> Visit(Brace node)
    {
        var inner = Builder(node.Arg1);
        return style => new BracketBox(inner(style), BracketKind.Curly, style);
    }

    /// <summary>
    /// (), [] or \{\}: a <see cref="BracketBox"/> around nothing, so the
    /// font's delimiters at their normal height, side by side. Arg0 is the
    /// opening delimiter as written.
    /// </summary>
    public Func<BoxStyle, Box> Visit(EmptyPair node)
    {
        var kind = (string)node.Arg0.Content switch
        {
            "(" => BracketKind.Paren,
            "[" => BracketKind.Square,
            _ => BracketKind.Curly,
        };
        return style => new BracketBox(new HBox(), kind, style);
    }

    /// <summary>
    /// [E], and the half-open mixes [E) and (E]. A matched pair is a
    /// <see cref="BracketBox"/>, stretchy like <see cref="Visit(Paren)"/>'s
    /// parens (<c>\left[ … \right]</c> arrives here too, since
    /// <see cref="MarkdownMacros.NormalizeLatexAliases"/> reduces it to the
    /// bare brackets). <see cref="BracketBox"/> draws one kind on both sides,
    /// so a mixed pair gets a <see cref="DelimiterPairBox"/> instead.
    /// </summary>
    public Func<BoxStyle, Box> Visit(Bracket node)
    {
        var open = ((string)node.Arg0.Content)[0];
        var close = ((string)node.Arg2.Content)[0];
        var inner = Builder(node.Arg1);
        if (open == '[' && close == ']')
            return style => new BracketBox(inner(style), BracketKind.Square, style);
        return style => new DelimiterPairBox(inner(style), open, close, style);
    }

    /// <summary>
    /// <c>,</c> as an atom. TeX sets a comma as punctuation: no space before
    /// it and a thin space (3 mu, a sixth of an em) after, which is what
    /// keeps <c>[a, b]</c> from reading as <c>[a,b]</c>.
    /// </summary>
    public Func<BoxStyle, Box> Visit(Punct node)
    {
        var text = (string)node.Arg0.Content;
        return style => new AtomBox(new HBox(new GlyphBox(text, style), new KernBox(style.FontSize / 6f)), AtomClass.Punct);
    }

    public Func<BoxStyle, Box> Visit(Sqrt node)
    {
        var radicand = Builder(node.Arg1);
        return style => new SqrtBox(radicand(style), style);
    }

    /// <summary>
    /// <c>\sqrt[n]{x}</c>. TeX sets a root's index in script-script style,
    /// two script steps below the radical, so the index builder runs at
    /// <c>style.Smaller().Smaller()</c>, the way <see cref="Visit(Sup)"/>
    /// runs an exponent's at <c>style.Smaller()</c>.
    /// <see cref="SqrtBox"/> tucks it into the radical's hook.
    /// </summary>
    public Func<BoxStyle, Box> Visit(NthRoot node)
    {
        var index = Builder(node.Arg2);
        var radicand = Builder(node.Arg4);
        return style => new SqrtBox(radicand(style), index(style.Smaller().Smaller()), style);
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
        // Function names (\sin, \log, \lim, ...) are in
        // MarkdownMacros.OperatorNames, which both visitors share.
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
        // Set-builder's bar: U+2223 DIVIDES, the relation, not a plain |.
        ["mid"] = "∣",
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
        // The empty operand MarkdownMacros.SupplyMissingOperands adds where an
        // operator has none on a side, drawn as nothing, like null.
        ["nulloperand"] = "",
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
    /// The symbol big operators (∑ ∏ ∫ ∮ ⋃ ⋂): drawn at 1.5 times the body
    /// size, as TeX's display style draws them, with their <c>_</c>/<c>^</c>
    /// as below/above limits (the LimitsBox rendering) rather than as
    /// right-of scripts. The named ones that take limits (<c>\lim</c>,
    /// <c>\max</c>, ...) are <see cref="MarkdownMacros.LimitOperatorNames"/>,
    /// at the body size.
    /// </summary>
    private static readonly HashSet<string> BigOperators = new(System.StringComparer.Ordinal)
    {
        "sum", "prod", "int", "oint", "bigcup", "bigcap",
    };

    /// <summary>
    /// A box set as one atom of a class the box itself does not show: an
    /// operator name, which is an Op, punctuation with its own space after
    /// it, or a braced group, an Ord. It draws and measures exactly as the
    /// box it wraps.
    /// </summary>
    private sealed class AtomBox(Box inner, AtomClass atomClass) : Box
    {
        public AtomClass Class => atomClass;

        public override float Width => inner.Width;
        public override float Height => inner.Height;
        public override float Depth => inner.Depth;

        public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
            => inner.Draw(renderer, penX, baselineY, style);
    }

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

    /// <summary>
    /// Two different delimiters around content, for the half-open intervals
    /// <c>[a, b)</c> and <c>(a, b]</c>. Laid out like <see cref="BracketBox"/>'s
    /// MATH-driven path: each side is a <see cref="StretchyVerticalBox"/> sized
    /// to the content's extent, centred on the math axis, with the same 0.15 em
    /// of padding either side of the content. A font with no MATH variant for
    /// either codepoint gets the plain glyphs, unstretched.
    /// </summary>
    private sealed class DelimiterPairBox : Box
    {
        private readonly Box _inner;
        private readonly Box _left;
        private readonly Box _right;
        private readonly float _padding;

        public DelimiterPairBox(Box inner, char open, char close, BoxStyle style)
        {
            _inner = inner;
            _padding = style.FontSize * 0.15f;
            var left = new StretchyVerticalBox(open, inner.TotalHeight, style);
            var right = new StretchyVerticalBox(close, inner.TotalHeight, style);
            if (left.IsAvailable && right.IsAvailable)
            {
                _left = left;
                _right = right;
            }
            else
            {
                _left = new GlyphBox(open.ToString(), style);
                _right = new GlyphBox(close.ToString(), style);
            }
        }

        public override float Width => _left.Width + _padding + _inner.Width + _padding + _right.Width;

        public override float Height =>
            MathF.Max(_inner.Height + _padding * 0.5f, MathF.Max(_left.Height, _right.Height));

        public override float Depth =>
            MathF.Max(_inner.Depth + _padding * 0.5f, MathF.Max(_left.Depth, _right.Depth));

        public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
        {
            _left.Draw(renderer, penX, baselineY, style);
            _inner.Draw(renderer, penX + _left.Width + _padding, baselineY, style);
            _right.Draw(renderer, penX + _left.Width + _padding + _inner.Width + _padding, baselineY, style);
        }
    }
}
