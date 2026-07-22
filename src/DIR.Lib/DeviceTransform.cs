using System.Numerics;

namespace DIR.Lib;

/// <summary>
/// A quarter-turn rotation — the only rotations a <see cref="DeviceTransform"/> allows. Restricting to
/// 90° multiples is load-bearing: it keeps an axis-aligned rectangle axis-aligned (width/height simply
/// swap at 90°/270°), so the whole <see cref="RectInt"/>-based layout, hit-testing and clipping stay
/// valid under the transform with no polygon rasterization. Values are ordered so the backing int is the
/// number of clockwise quarter-turns (screen space, Y points down), which makes composition a mod-4 add.
/// </summary>
public enum Rotation90
{
    /// <summary>No rotation (0°).</summary>
    None = 0,

    /// <summary>90° clockwise (screen space, Y-down): (x, y) → (-y, x).</summary>
    Cw90 = 1,

    /// <summary>180°: (x, y) → (-x, -y). The across-the-table hot-seat flip.</summary>
    Half = 2,

    /// <summary>270° clockwise / 90° counter-clockwise: (x, y) → (y, -x).</summary>
    Cw270 = 3,
}

/// <summary>
/// A deliberately constrained content→device affine transform: a <see cref="Rotation90"/>, a single
/// uniform <see cref="Scale"/> (no anisotropy, no shear) and a translation (<see cref="Tx"/>,
/// <see cref="Ty"/>). It unifies the three things the render stack used to model separately — DPI scaling
/// (the <see cref="Scale"/> component), device/app rotation (the <see cref="Rotation"/> component) and
/// safe-area/letterbox offset (the translation) — into one value.
///
/// <para>The constraints are what make it cheap and safe to thread through an existing pixel UI: a
/// 90°-multiple rotation plus uniform scale maps axis-aligned rects to axis-aligned rects, so layout,
/// hit-testing and clipping are unchanged; the map is trivially invertible (<see cref="Invert"/>), so a
/// host remaps pointer input to content coordinates in one place; and uniform scale keeps glyph sampling
/// clean. See <c>docs/device-transform.md</c> in the chess repo for the full design.</para>
///
/// <para>Being an affine map, its natural matrix form is 2×3 (<see cref="ToMatrix3x2"/>), not 4×4 — the
/// six coefficients ARE the transform; there is no perspective and no z. Renderers compose it with their
/// projection in <see cref="Matrix3x2"/> and only widen the result to the <c>mat4</c> a vertex shader
/// multiplies at the GPU boundary.</para>
///
/// <para>"Device" names the map's <i>target space</i> (content → device pixels), not its role: it is an
/// <i>app-driven content</i> transform layered ON TOP of the compositor's surface orientation (Vulkan
/// <c>preTransform</c>). Physical device/screen rotation stays the compositor's job; this carries only
/// DPI scale × app rotation (e.g. the hot-seat 180°). It never replaces <c>preTransform</c>.</para>
/// </summary>
public readonly record struct DeviceTransform(Rotation90 Rotation, float Scale, float Tx, float Ty)
{
    /// <summary>The no-op transform: no rotation, unit scale, no translation.</summary>
    public static readonly DeviceTransform Identity = new(Rotation90.None, 1f, 0f, 0f);

    /// <summary>True when this is the identity, so a renderer can skip the compose entirely.</summary>
    public bool IsIdentity => Rotation == Rotation90.None && Scale == 1f && Tx == 0f && Ty == 0f;

    // Exact cosine/sine for a quarter-turn — integer-valued, so 90°-multiple rotations carry no
    // floating-point error into the matrix (unlike MathF.Cos(MathF.PI / 2), which is ~-4.4e-8 not 0).
    private static (int Cos, int Sin) CosSin(Rotation90 r) => r switch
    {
        Rotation90.None => (1, 0),
        Rotation90.Cw90 => (0, 1),
        Rotation90.Half => (-1, 0),
        Rotation90.Cw270 => (0, -1),
        _ => (1, 0),
    };

    /// <summary>
    /// The content→device transform as a 2D affine matrix, in the row-vector convention that
    /// <see cref="Vector2.Transform(Vector2, Matrix3x2)"/> and <see cref="System.Numerics"/> matrix
    /// multiplication use. This is the canonical matrix form: a renderer composes it with its projection
    /// in <see cref="Matrix3x2"/> (<c>content→device→NDC</c>) and only widens to a <c>mat4</c> at upload.
    /// </summary>
    public Matrix3x2 ToMatrix3x2()
    {
        var (c, s) = CosSin(Rotation);
        return new Matrix3x2(
            Scale * c, Scale * s,
            -Scale * s, Scale * c,
            Tx, Ty);
    }

    /// <summary>Maps a point from content space to device space.</summary>
    public Vector2 Apply(Vector2 content) => Vector2.Transform(content, ToMatrix3x2());

    /// <summary>
    /// Maps a point from device space back to content space — the inverse of <see cref="Apply"/>. A host
    /// runs pointer events through this at the input boundary so everything downstream keeps hit-testing
    /// in content coordinates. Closed-form (a non-zero scale is always invertible): <c>Rᵀ·((p − t)/s)</c>.
    /// </summary>
    public Vector2 Invert(Vector2 device)
    {
        var (c, s) = CosSin(Rotation);
        var ux = (device.X - Tx) / Scale;
        var uy = (device.Y - Ty) / Scale;
        // The inverse of a rotation is its transpose: [[c, s], [-s, c]].
        return new Vector2(c * ux + s * uy, -s * ux + c * uy);
    }

    /// <summary>
    /// Returns the transform that applies <paramref name="inner"/> first and then this one:
    /// <c>outer.Compose(inner).Apply(p) == outer.Apply(inner.Apply(p))</c>. Stays in the constrained
    /// representation — rotations add (mod 4), scales multiply — rather than round-tripping through a
    /// matrix, so there is no rotation to re-extract.
    /// </summary>
    public DeviceTransform Compose(DeviceTransform inner)
    {
        var (c, s) = CosSin(Rotation); // outer rotation, applied to inner's translation (no scale)
        var rtx = c * inner.Tx - s * inner.Ty;
        var rty = s * inner.Tx + c * inner.Ty;
        return new DeviceTransform(
            (Rotation90)(((int)Rotation + (int)inner.Rotation) & 3),
            Scale * inner.Scale,
            Scale * rtx + Tx,
            Scale * rty + Ty);
    }

    /// <summary>
    /// A rotation (with optional uniform scale) about the centre of a <paramref name="width"/> ×
    /// <paramref name="height"/> surface — the centre maps to itself, so the content stays centred. This
    /// is how a consumer builds the across-the-table hot-seat flip:
    /// <c>CenteredRotation(Rotation90.Half, w, h)</c>. Depends on the surface size, so recompute it on
    /// resize.
    /// </summary>
    public static DeviceTransform CenteredRotation(Rotation90 rotation, float width, float height, float scale = 1f)
    {
        var (c, s) = CosSin(rotation);
        var cx = width * 0.5f;
        var cy = height * 0.5f;
        // t = centre − scale·(R·centre), chosen so Apply(centre) == centre.
        var rcx = c * cx - s * cy;
        var rcy = s * cx + c * cy;
        return new DeviceTransform(rotation, scale, cx - scale * rcx, cy - scale * rcy);
    }
}
