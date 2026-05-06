# MathJax reference renders

This tool renders the same LaTeX expressions our `MathLayoutBaselineTests` cover, but through MathJax (the engine behind Wikipedia, MathOverflow, arXiv HTML, etc.) — giving us a known-good golden image per scene to compare visually with our `DejaVuSans/` and `STIX2Math/` baselines.

The output PNGs land in `../../Baselines/MathLayout/MathJax/<scene>.png` so a file-explorer 3-up of `DejaVuSans`, `STIX2Math`, and `MathJax` reads at a glance.

## One-time setup

```bash
cd DIR.Lib.Tests/Tools/MathJaxRefs
npm install
```

This pulls `mathjax-full` (the headless Node renderer) and `sharp` (for SVG→PNG) — both pure-Node, no native build deps beyond what npm bundles.

## Running

```bash
npm run render
```

Reads `scenes.json` (scene name → LaTeX) and writes one PNG per scene.

## Adding scenes

Edit `scenes.json`. Keep the scene names the same as the corresponding case in `MathLayoutBaselineTests.cs:SceneNames` so the file-name match-up stays trivial.

## Notes

- We composite the rendered SVG against the *same* light grid-paper backdrop the .NET test uses (`ComposeOnGridPaper`), with the same grid spacing — so when you put two PNGs side by side the visual quality difference is layout, not chrome.
- MathJax outputs SVG in `ex` units; the script scales to ~96 px font equivalence (matching `MathLayoutBaselineTests.BaselineFontSize`).
- These PNGs are *references*, not assertions — no test consumes them yet. They exist for human visual diffing during layout development.
