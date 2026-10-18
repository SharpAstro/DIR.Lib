#!/usr/bin/env pwsh
# Regenerates src/DIR.Lib.Tests/RadicalJoinTests.Before.cs: the square root as a base commit drew it, at
# every point RadicalJoinTests sweeps.
#
# Why a442e91: it is the radical with the whole-row clearance (radical-clearance), which the stretchy-assembly
# change branched from, so the sweep compares that change with the code it replaced. Before it the base was
# fe09d4a, the radical the whole-row clearance replaced, for the same reason. Until then the base was e6778fc, the radical before the radical-join rework (#115):
# math-italic (#117), which landed on main just before the rework, with the SqrtBox, StretchyVerticalBox
# and ManagedFontRasterizer of 619f0e4, where the rework branched. Measured from 619f0e4, with upright
# variables, the sweep counted italic's own change to the radicand as radical drift (STIX Two Math \sqrt{x}
# at 7 px: depth 0 -> 2.16). A base must likewise differ from the tested tree only in the radical's own
# code. Both bases still regenerate: the patch below is picked by which SqrtBox the commit has.
#
#   pwsh tools/gen-radical-join-before/generate.ps1                       # extract the commit, patch, measure
#   pwsh tools/gen-radical-join-before/generate.ps1 -BaseTree DIR         # reuse an extracted copy of it
#   pwsh tools/gen-radical-join-before/generate.ps1 -BaseCommit e6778fc   # the radical before the rework
#
# Steps: `git archive` the whole commit into obj/base here (ignored by git; its build packs, so it wants
# the root files as well as src/); patch that copy's SqrtBox so each Draw records where its bar ended (the
# code itself keeps no record of it); build GenRadicalJoinBefore against the copy and run it. Nothing in
# src/ is built or touched except the output file. Change the point set in Program.cs and in
# RadicalJoinTests together.
param(
    [string]$BaseCommit = 'a442e91',
    [string]$BaseTree
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $here '../..')).Path

if (-not $BaseTree) {
    $BaseTree = Join-Path $here 'obj/base'
    if (Test-Path $BaseTree) { Remove-Item -Recurse -Force $BaseTree }
    New-Item -ItemType Directory -Force $BaseTree | Out-Null
    # A zip and Expand-Archive rather than tar: on Windows the tar first on PATH can be Git's, which reads
    # the drive letter in C:\... as a remote host.
    $zip = Join-Path $here 'obj/base.zip'
    git -C $repo archive --format=zip -o $zip $BaseCommit
    if ($LASTEXITCODE -ne 0) { throw "git archive $BaseCommit failed" }
    Expand-Archive -Path $zip -DestinationPath $BaseTree -Force
}
$BaseTree = (Resolve-Path $BaseTree).Path

# The patch: a field Draw sets once the bar is placed, holding the bar's top row, the row below the bar, the
# glyph's right edge (where the bar starts over the radicand) and the bar's right end.
$sqrt = Join-Path $BaseTree 'src/DIR.Lib/MathLayout/SqrtBox.cs'
$text = [IO.File]::ReadAllText($sqrt)
function Replace-Once([string]$s, [string]$old, [string]$new) {
    $at = $s.IndexOf($old, [StringComparison]::Ordinal)
    if ($at -lt 0 -or $s.IndexOf($old, $at + 1, [StringComparison]::Ordinal) -ge 0) {
        throw "SqrtBox.cs: expected exactly one '$old' (is $BaseCommit a commit one of these patches was written for?)"
    }
    return $s.Substring(0, $at) + $new + $s.Substring($at + $old.Length)
}
if ($text.Contains('_probeBar')) {
    # Already patched (a reused -BaseTree).
}
elseif ($text.Contains('internal RadicalJoin? JoinAt(')) {
    # Since 9fe19fd Draw takes the bar from JoinAt and keeps the join it drew with in a local.
    $field = "    private readonly bool _strokeCapTrimmedBand;`n"
    $text = Replace-Once $text $field ($field + "    private (int, int, int, int) _probeBar; // recorded by Draw for tools/gen-radical-join-before`n")
    $draw = "            DrawWithGlyph(renderer, join, RadicandX(penX), baselineY, style);`n"
    $text = Replace-Once $text $draw ($draw + "            _probeBar = (join.FlagTopY, join.FlagTopY + join.FlagRows, join.BarStartX, join.BarEndX);`n")
    [IO.File]::WriteAllText($sqrt, $text)
}
else {
    # e6778fc: each glyph path fills its own vinculum rectangle.
    $field = "    private readonly int _flagRightCol;`n"
    $text = Replace-Once $text $field ($field + "    private (int, int, int, int) _probeBar; // recorded by Draw for tools/gen-radical-join-before`n")
    $fill = "        renderer.FillRectangle(vinc, style.Foreground);`n    }`n`n    /// <summary>`n    /// MATH-driven path"
    $text = Replace-Once $text $fill ("        renderer.FillRectangle(vinc, style.Foreground);`n        _probeBar = (vincTop, vincBottom, (int)MathF.Floor(penX) + _radicalScaled.Width, vincRight);`n    }`n`n    /// <summary>`n    /// MATH-driven path")
    $fill = "        renderer.FillRectangle(vinc, style.Foreground);`n    }`n`n    /// <summary>`n    /// Parametric fallback"
    $text = Replace-Once $text $fill ("        renderer.FillRectangle(vinc, style.Foreground);`n        _probeBar = (vincTop, vincBottom, (int)MathF.Floor(penX) + bitmap.Width, vincRight);`n    }`n`n    /// <summary>`n    /// Parametric fallback")
    [IO.File]::WriteAllText($sqrt, $text)
}

$fontsDir = Join-Path $repo 'src/DIR.Lib.Tests/Fonts'
$out = Join-Path $repo 'src/DIR.Lib.Tests/RadicalJoinTests.Before.cs'
dotnet run --project $here -c Release "-p:BaseTree=$BaseTree" -- $fontsDir $out $BaseCommit
if ($LASTEXITCODE -ne 0) { throw "GenRadicalJoinBefore failed" }
