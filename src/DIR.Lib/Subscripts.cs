namespace DIR.Lib;

/// <summary>
/// Unicode super/subscript helpers. Most modern terminals render these as a
/// single cell each (they are normal codepoints, not combining marks), so
/// they fit Console.Lib's "1 char = 1 cell" model without any new
/// infrastructure. Use these for cheap inline notation — chemistry mass
/// numbers (²³⁸U), exponents (E=mc²), molecular formulae (H₂O).
///
/// For genuinely multi-cell typographic effects (stacking sub- and
/// superscripts in the same column on one line, mixed font sizes within a
/// line), reach for a Sixel canvas instead — terminal text cannot do those.
///
/// Handles digits 0–9, +, −, =, parens, plus the lowercase letters that
/// have dedicated Unicode super/subscript codepoints. Anything else passes
/// through unchanged.
/// </summary>
public static class Subscripts
{
    public static char ToSuperscript(char c) => c switch
    {
        '0' => '\u2070', '1' => '\u00B9', '2' => '\u00B2', '3' => '\u00B3',
        '4' => '\u2074', '5' => '\u2075', '6' => '\u2076', '7' => '\u2077',
        '8' => '\u2078', '9' => '\u2079',
        '+' => '\u207A', '-' => '\u207B', '=' => '\u207C',
        '(' => '\u207D', ')' => '\u207E',
        'n' => '\u207F', 'i' => '\u2071',
        _ => c,
    };

    public static char ToSubscript(char c) => c switch
    {
        '0' => '\u2080', '1' => '\u2081', '2' => '\u2082', '3' => '\u2083',
        '4' => '\u2084', '5' => '\u2085', '6' => '\u2086', '7' => '\u2087',
        '8' => '\u2088', '9' => '\u2089',
        '+' => '\u208A', '-' => '\u208B', '=' => '\u208C',
        '(' => '\u208D', ')' => '\u208E',
        'a' => '\u2090', 'e' => '\u2091', 'o' => '\u2092', 'x' => '\u2093',
        'h' => '\u2095', 'k' => '\u2096', 'l' => '\u2097', 'm' => '\u2098',
        'n' => '\u2099', 'p' => '\u209A', 's' => '\u209B', 't' => '\u209C',
        _ => c,
    };

    public static string Super(string s) => Map(s, ToSuperscript);
    public static string Sub(string s)   => Map(s, ToSubscript);

    private static string Map(string s, Func<char, char> f)
    {
        if (string.IsNullOrEmpty(s)) return s;
        Span<char> buf = s.Length <= 64 ? stackalloc char[s.Length] : new char[s.Length];
        for (int i = 0; i < s.Length; i++) buf[i] = f(s[i]);
        return new string(buf);
    }
}
