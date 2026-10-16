#!/usr/bin/env pwsh
# Regenerates src/DIR.Lib.Tests/RadicalJoinTests.Before.cs: the square root as commit e6778fc drew it,
# before the radical-join rework, at every point RadicalJoinTests sweeps.
#
# Why e6778fc: it is math-italic (#117), which landed on main just before the rework (#115). Its SqrtBox,
# StretchyVerticalBox and ManagedFontRasterizer are those of 619f0e4, where the rework branched, but it
# builds parsed variables in math italic, as the tree the tests run against does. Measured from 619f0e4,
# with upright variables, the sweep counted italic's own change to the radicand as radical drift (STIX Two
# Math \sqrt{x} at 7 px: depth 0 -> 2.16). A new base must likewise differ from the tested tree only in the
# radical's own code.
#
#   pwsh tools/gen-radical-join-before/generate.ps1               # extract the commit, patch, measure
#   pwsh tools/gen-radical-join-before/generate.ps1 -BaseTree DIR # reuse an extracted copy of it
#
# Steps: `git archive` the whole commit into obj/base here (ignored by git; its build packs, so it wants
# the root files as well as src/); patch that copy's SqrtBox so each Draw records where its bar ended (the
# old code kept no record of it); build GenRadicalJoinBefore against the copy and run it. Nothing in src/
# is built or touched except the output file. Change the point set in Program.cs and in RadicalJoinTests
# together.
param(
    [string]$BaseCommit = 'e6778fc',
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

# The patch: a field the two glyph paths' Draw methods set right after filling the bar, holding the row
# below the bar, the glyph's right edge (where the bar starts over the radicand) and the bar's right end.
$sqrt = Join-Path $BaseTree 'src/DIR.Lib/MathLayout/SqrtBox.cs'
$text = [IO.File]::ReadAllText($sqrt)
if (-not $text.Contains('_probeBar')) {
    function Replace-Once([string]$s, [string]$old, [string]$new) {
        $at = $s.IndexOf($old, [StringComparison]::Ordinal)
        if ($at -lt 0 -or $s.IndexOf($old, $at + 1, [StringComparison]::Ordinal) -ge 0) {
            throw "SqrtBox.cs: expected exactly one '$old' (is $BaseCommit the commit this patch was written for?)"
        }
        return $s.Substring(0, $at) + $new + $s.Substring($at + $old.Length)
    }
    $field = "    private readonly int _flagRightCol;`n"
    $text = Replace-Once $text $field ($field + "    private (int, int, int) _probeBar; // recorded by Draw for tools/gen-radical-join-before`n")
    $fill = "        renderer.FillRectangle(vinc, style.Foreground);`n    }`n`n    /// <summary>`n    /// MATH-driven path"
    $text = Replace-Once $text $fill ("        renderer.FillRectangle(vinc, style.Foreground);`n        _probeBar = (vincBottom, (int)MathF.Floor(penX) + _radicalScaled.Width, vincRight);`n    }`n`n    /// <summary>`n    /// MATH-driven path")
    $fill = "        renderer.FillRectangle(vinc, style.Foreground);`n    }`n`n    /// <summary>`n    /// Parametric fallback"
    $text = Replace-Once $text $fill ("        renderer.FillRectangle(vinc, style.Foreground);`n        _probeBar = (vincBottom, (int)MathF.Floor(penX) + bitmap.Width, vincRight);`n    }`n`n    /// <summary>`n    /// Parametric fallback")
    [IO.File]::WriteAllText($sqrt, $text)
}

$fontsDir = Join-Path $repo 'src/DIR.Lib.Tests/Fonts'
$out = Join-Path $repo 'src/DIR.Lib.Tests/RadicalJoinTests.Before.cs'
dotnet run --project $here -c Release "-p:BaseTree=$BaseTree" -- $fontsDir $out
if ($LASTEXITCODE -ne 0) { throw "GenRadicalJoinBefore failed" }
