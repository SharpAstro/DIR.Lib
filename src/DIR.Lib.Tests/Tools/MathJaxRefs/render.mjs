// Render every scene in scenes.json through MathJax (display mode), then
// rasterize the resulting SVG to PNG via @resvg/resvg-js. PNGs go into
// ../../Baselines/MathLayout/MathJax/<scene>.png — sibling to the
// DejaVuSans/ and STIX2Math/ baselines so a 3-up visual comparison is
// trivial.
//
// The MathJax output is composited against the same light grid-paper
// backdrop the DIR.Lib baseline tests use (`ComposeOnGridPaper`), with
// the *same* grid spacing, so a side-by-side reads cleanly: any layout
// difference is layout, not background-noise. Foreground is black,
// matching BoxStyle's foreground in MathLayoutBaselineTests.
//
// Run: `npm install` once (only after a fresh clone or version bump in
// package.json), then `npm run render`.

import { mathjax } from 'mathjax-full/js/mathjax.js';
import { TeX } from 'mathjax-full/js/input/tex.js';
import { SVG } from 'mathjax-full/js/output/svg.js';
import { liteAdaptor } from 'mathjax-full/js/adaptors/liteAdaptor.js';
import { RegisterHTMLHandler } from 'mathjax-full/js/handlers/html.js';
import { AllPackages } from 'mathjax-full/js/input/tex/AllPackages.js';

import { Resvg } from '@resvg/resvg-js';
import { PNG } from 'pngjs';

import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const scenesPath = join(__dirname, 'scenes.json');
const outDir = resolve(__dirname, '..', '..', 'Baselines', 'MathLayout', 'MathJax');

// Match DIR.Lib's baseline render font size of 96 px so MathJax output is
// scale-comparable to our render. MathJax authors documents in 'ex' units;
// we tell resvg to rasterize at this pixel-equivalent below.
const FONT_SIZE_PX = 96;

const adaptor = liteAdaptor();
RegisterHTMLHandler(adaptor);

const tex = new TeX({ packages: AllPackages });
const svg = new SVG({ fontCache: 'none' });
const html = mathjax.document('', { InputJax: tex, OutputJax: svg });

const sceneFile = JSON.parse(await readFile(scenesPath, 'utf8'));
const scenes = sceneFile.scenes;

await mkdir(outDir, { recursive: true });

// Backdrop colour and grid spacing must match
// MathLayoutBaselineTests.ComposeOnGridPaper so visual diffing is fair.
const BG = { r: 245, g: 245, b: 245 };
const GRID = { r: 215, g: 215, b: 220 };
const CENTRE = { r: 180, g: 190, b: 210 };
const GRID_SPACING = Math.round(FONT_SIZE_PX / 3); // ~32 px @ 96 em

// Pad the math content from each edge so it doesn't kiss the canvas border,
// matching the breathing room BoxRasterizer leaves around our renders.
const PAD = Math.round(FONT_SIZE_PX * 0.12);

for (const [scene, latex] of Object.entries(scenes)) {
    process.stdout.write(`render ${scene}: ${latex}\n`);

    const node = html.convert(latex, { display: true, em: 16, ex: 8, containerWidth: 80 * 16 });
    let svgString = adaptor.outerHTML(node);

    // MathJax wraps the math in <mjx-container>; pull out the inner <svg>.
    const svgMatch = /<svg[\s\S]*<\/svg>/.exec(svgString);
    if (!svgMatch) {
        console.error(`could not extract <svg> from MathJax output for ${scene}`);
        continue;
    }
    let mathSvg = svgMatch[0];
    // Bake foreground colour to black. MathJax's SVG already has a style
    // attribute on the root <svg>; merge into it instead of duplicating.
    mathSvg = mathSvg.replace(/<svg([^>]*)>/, (_m, attrs) => {
        if (/\bstyle="[^"]*"/.test(attrs)) {
            return '<svg' + attrs.replace(/\bstyle="([^"]*)"/, 'style="$1;color:#000;fill:#000"') + '>';
        }
        return '<svg' + attrs + ' style="color:#000;fill:#000">';
    });

    // Tell resvg the scale we want — match the test render's font size.
    // The math's own SVG has width/height in 'ex'; resvg interprets
    // ex => 0.5 of the font size we set via 'fitTo'. We aim for ~96 px font
    // equivalence, so scale the SVG to the 96 px font ladder.
    const resvg = new Resvg(mathSvg, {
        background: 'rgba(0,0,0,0)',  // transparent; we composite a grid behind it
        fitTo: { mode: 'height', value: 0 },  // overridden below — we rescale by font
        font: { loadSystemFonts: false },
    });
    const intrinsic = resvg.innerBBox() || { width: 100, height: 50 };

    // Compute target bitmap pixel size: scale up so the math's ex-height
    // lands at ~half the font size in px. The math SVG's height in ex
    // corresponds to its visual height in font-relative units; we pick a
    // pixel scale that puts a single line of text near FONT_SIZE_PX.
    const exPx = FONT_SIZE_PX / 2; // ex ≈ half em
    const widthMatch = /width="([\d.]+)ex"/.exec(mathSvg);
    const heightMatch = /height="([\d.]+)ex"/.exec(mathSvg);
    const innerW = widthMatch ? Math.ceil(parseFloat(widthMatch[1]) * exPx) : intrinsic.width;
    const innerH = heightMatch ? Math.ceil(parseFloat(heightMatch[1]) * exPx) : intrinsic.height;

    const wPx = innerW + PAD * 2;
    const hPx = innerH + PAD * 2;

    // Build a wrapper SVG that pins the math at PAD,PAD and a known
    // viewport size, so resvg rasterizes deterministically.
    const wrapped = `<svg xmlns="http://www.w3.org/2000/svg" width="${wPx}" height="${hPx}" viewBox="0 0 ${wPx} ${hPx}">
<g transform="translate(${PAD}, ${PAD})" color="#000" fill="#000">
${mathSvg.replace(/<svg([^>]*)\bwidth="[^"]*"/, '<svg$1').replace(/<svg([^>]*)\bheight="[^"]*"/, `<svg$1 width="${innerW}" height="${innerH}"`)}
</g>
</svg>`;

    const r2 = new Resvg(wrapped, {
        background: 'rgba(0,0,0,0)',
        font: { loadSystemFonts: false },
    });
    const fgPng = r2.render().asPng();
    // Decode the resvg output into raw RGBA so we can alpha-blend over
    // our own grid-paper backdrop. pngjs gives us an RGBA buffer.
    const fgImg = PNG.sync.read(fgPng);
    const fgPixels = fgImg.data;
    const W = fgImg.width, H = fgImg.height;

    // Compose grid backdrop, then alpha-blend math foreground over it.
    const composed = Buffer.alloc(W * H * 4);
    for (let y = 0; y < H; y++) {
        for (let x = 0; x < W; x++) {
            const i = (y * W + x) * 4;
            composed[i] = BG.r; composed[i + 1] = BG.g; composed[i + 2] = BG.b; composed[i + 3] = 255;
            if (x % GRID_SPACING === 0 || y % GRID_SPACING === 0) {
                composed[i] = GRID.r; composed[i + 1] = GRID.g; composed[i + 2] = GRID.b;
            }
            if (x === Math.floor(W / 2) || y === Math.floor(H / 2)) {
                composed[i] = CENTRE.r; composed[i + 1] = CENTRE.g; composed[i + 2] = CENTRE.b;
            }
        }
    }

    for (let i = 0; i < fgPixels.length; i += 4) {
        const sa = fgPixels[i + 3];
        if (sa === 0) continue;
        const sr = fgPixels[i], sg = fgPixels[i + 1], sb = fgPixels[i + 2];
        if (sa === 255) {
            composed[i] = sr; composed[i + 1] = sg; composed[i + 2] = sb; composed[i + 3] = 255;
            continue;
        }
        const inv = 255 - sa;
        composed[i] = Math.round((sr * sa + composed[i] * inv) / 255);
        composed[i + 1] = Math.round((sg * sa + composed[i + 1] * inv) / 255);
        composed[i + 2] = Math.round((sb * sa + composed[i + 2] * inv) / 255);
        composed[i + 3] = 255;
    }

    const outImg = new PNG({ width: W, height: H });
    composed.copy(outImg.data);
    const outBuf = PNG.sync.write(outImg);

    const outPath = join(outDir, `${scene}.png`);
    await writeFile(outPath, outBuf);

    process.stdout.write(`  -> ${outPath} (${W}x${H})\n`);
}
process.stdout.write(`done. ${Object.keys(scenes).length} scenes rendered to ${outDir}\n`);
