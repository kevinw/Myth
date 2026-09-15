#!/usr/bin/env bash
# Exports pitchdeck.pdf to html/ (index.html plus page images, fonts, and videos) with pdf2htmlEX (Docker, e.g. `colima start`),
# sizes the stacked pages to 80% of the viewport width, and swaps links to .mp4 files
# (see \video and \fullbleedvideo in pitchdeck.tex) for autoplaying looped videos.
set -euo pipefail
cd "$(dirname "$0")"

image=pdf2htmlex/pdf2htmlex:0.18.8.rc2-master-20200820-ubuntu-20.04-x86_64
pdf2html() { docker run --rm --platform linux/amd64 -v "$PWD":/pdf -w /pdf "$image" --zoom 2 "$@"; }

rm -rf html && mkdir -p html/figures
pdf2html --bg-format jpg --dpi 576 --embed-image 0 --embed-font 0 --dest-dir html pitchdeck.pdf index.html
grep -oE 'figures/[^"#]+\.mp4' html/index.html | sort -u | xargs -I{} cp {} html/figures/

# A full-bleed video plays beneath its page, so that page's background must be transparent:
# render the deck again without posters or canvas, as SVG backgrounds, and use those instead.
if grep -q '?fullbleed' html/index.html; then
	trap 'rm -rf pitchdeck-overlay.* html/overlay' EXIT
	echo '\def\hidevideoposters{}\input{pitchdeck}' > pitchdeck-overlay.tex
	tectonic pitchdeck-overlay.tex > /dev/null 2>&1
	pdf2html --bg-format svg --dest-dir html/overlay pitchdeck-overlay.pdf index.html
	python3 - <<'EOF'
import os
main = open('html/index.html').read()
overlay = open('html/overlay/index.html').read()

def pages(s):
	starts = []
	while (i := s.find('<div id="pf', starts[-1] + 1 if starts else 0)) != -1:
		starts.append(i)
	return list(zip(starts, starts[1:] + [len(s)]))

def background_src(s, start, end):
	i = s.index('src="', s.index('<img class="b', start)) + len('src="')  # "bi" for bitmap backgrounds, "bf" for SVG
	assert i < end
	return i, s.index('"', i)

out, last = [], 0
for (start, end), (ostart, oend) in zip(pages(main), pages(overlay)):
	if '?fullbleed' in main[start:end]:
		i, j = background_src(main, start, end)
		oi, oj = background_src(overlay, ostart, oend)
		os.remove('html/' + main[i:j])  # the bitmap background the (embedded) SVG replaces
		out += [main[last:i], overlay[oi:oj]]
		last = j
open('html/index.html', 'w').write(''.join(out) + main[last:])
EOF
fi

extra=$(cat <<'EOF'
<style>
	.pf { zoom: var(--page-zoom, 1); }
	/* pdf2htmlEX hides pages it thinks are offscreen, and misjudges that under zoom in Safari */
	.pc { display: block !important; }
	.pf video { object-fit: cover; image-rendering: pixelated; }
</style>
<script>
	addEventListener("DOMContentLoaded", () => {
		const width = document.querySelector(".pf").offsetWidth;
		const fit = () => document.documentElement.style.setProperty("--page-zoom", innerWidth * 0.8 / width);
		addEventListener("resize", fit);
		fit();
		for (const link of document.querySelectorAll('a[href*=".mp4"]')) {
			const [src, query] = link.getAttribute("href").split("?");
			const video = Object.assign(document.createElement("video"), { src, autoplay: true, muted: true, loop: true, playsInline: true });
			// the link box is sized in PDF units and scaled up by its transform class (m1)
			video.className = link.firstElementChild.className;
			video.style.cssText = link.firstElementChild.style.cssText;
			if (query === "fullbleed") {
				// beneath the page's (transparent) background, so the slide's bars and boxes stay on top
				link.closest(".pc").prepend(video);
				link.remove();
			} else {
				link.replaceWith(video);
			}
		}
	});
</script>
EOF
)
EXTRA="$extra" perl -0pi -e 's#<title>.*?</title>#<title>Human Questions - 2026 Pitch Deck</title>#; s#</head>#$ENV{EXTRA}</head>#' html/index.html
