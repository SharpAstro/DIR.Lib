using SharpAstro.Fonts.Outlines;
using Xunit;

namespace DIR.Lib.Tests;

public sealed class TmpScan(ITestOutputHelper output)
{
    private sealed class Lines : IGlyphSink
    {
        public readonly List<(double X0, double Y0, double X1, double Y1)> E = [];
        double _x, _y, _sx, _sy; bool _open;
        public void MoveTo(float x, float y) { Cl(); _x = _sx = x; _y = _sy = y; _open = true; }
        public void LineTo(float x, float y) { En(); L(x, y); }
        public void QuadTo(float cx, float cy, float x, float y)
        {
            En(); double x0 = _x, y0 = _y;
            for (int i = 1; i <= 6; i++) { double t = i / 6.0, u = 1 - t; L(u * u * x0 + 2 * u * t * cx + t * t * x, u * u * y0 + 2 * u * t * cy + t * t * y); }
        }
        public void CubicTo(float a, float b, float c, float d, float x, float y) { En(); L(x, y); }
        public void Close() => Cl();
        void En() { if (!_open) { _x = _sx; _y = _sy; _open = true; } }
        void L(double x, double y) { E.Add((_x, _y, x, y)); _x = x; _y = y; }
        public void Cl() { if (_open && (_x != _sx || _y != _sy)) L(_sx, _sy); _open = false; }
        public int Wn(double px, double py)
        {
            int wn = 0;
            foreach (var (x0, y0, x1, y1) in E)
            {
                if ((y0 <= py) == (y1 <= py)) continue;
                if (x0 + (py - y0) / (y1 - y0) * (x1 - x0) > px) wn += y1 > y0 ? 1 : -1;
            }
            return wn;
        }
    }

    [Fact]
    public void Scan()
    {
        using var r = new ManagedFontRasterizer();
        var path = Path.Combine("Fonts", "DejaVuSans.ttf");
        r.TryGetOpenTypeFont(path, out var font);
        var results = new List<(int Gid, int Count, int Upem)>();
        for (int g = 0; g < (int)font!.NumGlyphs; g++)
        {
            var s = new Lines();
            if (!r.TryDrawGlyphOutline(path, new GlyphIdentity((uint)g, null), s, out int upem)) continue;
            s.Cl();
            if (s.E.Count < 4) continue;
            double minX = s.E.Min(e => Math.Min(e.X0, e.X1)), maxX = s.E.Max(e => Math.Max(e.X0, e.X1));
            double minY = s.E.Min(e => Math.Min(e.Y0, e.Y1)), maxY = s.E.Max(e => Math.Max(e.Y0, e.Y1));
            bool cand = false;
            for (int j = 0; j < 24 && !cand; j++)
                for (int i = 0; i < 24; i++)
                    if (Math.Abs(s.Wn(minX + (i + 0.37) / 24 * (maxX - minX), minY + (j + 0.61) / 24 * (maxY - minY))) >= 2) { cand = true; break; }
            if (!cand) continue;
            double step = upem / 64.0;
            int nx = (int)((maxX - minX) / step) + 1, ny = (int)((maxY - minY) / step) + 1;
            var wn = new int[nx * ny];
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                    wn[j * nx + i] = s.Wn(minX + (i + 0.37) * step, minY + (j + 0.61) * step);
            int count = 0;
            for (int j = 1; j < ny - 1; j++)
                for (int i = 1; i < nx - 1; i++)
                {
                    if (Math.Abs(wn[j * nx + i]) < 2) continue;
                    bool ok = true;
                    for (int dj = -1; dj <= 1 && ok; dj++)
                        for (int di = -1; di <= 1; di++)
                            if (wn[(j + dj) * nx + i + di] == 0) { ok = false; break; }
                    if (ok) count++;
                }
            results.Add((g, count, upem));
        }
        foreach (var x in results.OrderByDescending(x => x.Count).Take(15)) output.WriteLine($"gid {x.Gid} wound-twice interior pixels at 64 ppe: {x.Count}");
        output.WriteLine($"{results.Count} candidates");
    }
}
