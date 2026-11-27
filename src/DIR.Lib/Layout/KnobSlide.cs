namespace DIR.Lib.Layout;

/// <summary>
/// What the painter needs to slide a switch's knob: the switch's own identity (its track's hit, which keys the per-window
/// memory of the side it was last painted at), the side it is on now, and whether it is locked (a locked switch lands at
/// once). See <see cref="Node.Knob"/>.
/// </summary>
/// <param name="Switch">The track's hit identity.</param>
/// <param name="On">The side the knob is on now: right when true.</param>
/// <param name="Locked">True when a lock should show the value without animating it.</param>
public sealed record KnobSlide(HitResult Switch, bool On, bool Locked);
