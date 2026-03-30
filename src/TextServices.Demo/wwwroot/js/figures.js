import { loadConfig, fetchManifest, extractCanvases, buildImageUrl, parseXYWH, extractFiguresUrl } from './iiif-helpers.js';

// ---- State -------------------------------------------------------------------
let canvasMap = {};  // canvasId -> { imageServiceId, width, height }

// ---- DOM refs ----------------------------------------------------------------
const manifestInput = document.getElementById('manifest-url');
const loadBtn       = document.getElementById('load-btn');
const errorArea     = document.getElementById('error-area');
const infoArea      = document.getElementById('info-area');
const figuresArea   = document.getElementById('figures-area');

// ---- Init --------------------------------------------------------------------

(async function init() {
    await loadConfig();
    const params = new URLSearchParams(window.location.search);
    const url = params.get('iiif-content');
    if (url) {
        manifestInput.value = url;
        await loadFigures(url);
    }
})();

loadBtn.addEventListener('click', async () => {
    const url = manifestInput.value.trim();
    if (!url) return;
    history.pushState({}, '', `?iiif-content=${encodeURIComponent(url)}`);
    await loadFigures(url);
});

manifestInput.addEventListener('keydown', e => { if (e.key === 'Enter') loadBtn.click(); });

// ---- Main loading ------------------------------------------------------------

async function loadFigures(manifestUrl) {
    errorArea.innerHTML = '';
    infoArea.style.display = 'none';
    figuresArea.innerHTML = '';
    canvasMap = {};

    let manifest;
    try {
        manifest = await fetchManifest(manifestUrl);
    } catch (err) {
        showError(`Could not load manifest: ${err.message}`);
        return;
    }

    const canvases = extractCanvases(manifest);
    for (const c of canvases) {
        canvasMap[c.id] = c;
    }

    const figuresUrl = extractFiguresUrl(manifest);
    if (!figuresUrl) {
        infoArea.textContent = 'This manifest has no identified figures annotation page. Process it first, or it may contain no ComposedBlock elements in the ALTO.';
        infoArea.style.display = 'block';
        return;
    }

    let page;
    try {
        const res = await fetch(figuresUrl, { headers: { 'Accept': 'application/json, application/ld+json' } });
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        page = await res.json();
    } catch (err) {
        showError(`Could not load figures annotation page: ${err.message}`);
        return;
    }

    const items = page.items ?? [];
    if (items.length === 0) {
        infoArea.textContent = 'Figures annotation page is empty.';
        infoArea.style.display = 'block';
        return;
    }

    infoArea.innerHTML = `<span class="label">Figures page:</span><a href="${esc(figuresUrl)}" target="_blank">${esc(figuresUrl)}</a> &nbsp; <span class="label">Total:</span>${items.length} item${items.length === 1 ? '' : 's'}`;
    infoArea.style.display = 'block';

    renderFigures(items);
}

// ---- Rendering ---------------------------------------------------------------

function renderFigures(items) {
    // Group by BlockType (body.value)
    const groups = new Map();
    for (const item of items) {
        const blockType = item.body?.value ?? 'Unknown';
        if (!groups.has(blockType)) groups.set(blockType, []);
        groups.get(blockType).push(item);
    }

    // Render each group
    for (const [blockType, groupItems] of groups) {
        const section = document.createElement('section');
        section.className = 'figures-section';

        const heading = document.createElement('h2');
        heading.textContent = `${blockType} (${groupItems.length})`;
        section.appendChild(heading);

        const grid = document.createElement('div');
        grid.className = 'figures-grid';

        for (const item of groupItems) {
            const parsed = parseXYWH(item.target);
            if (!parsed) continue;

            const canvas = canvasMap[parsed.canvasId];
            if (!canvas) continue;

            const card = document.createElement('div');
            card.className = 'figure-card';

            if (canvas.imageServiceId) {
                const region = `${parsed.x},${parsed.y},${parsed.w},${parsed.h}`;
                const imgUrl = buildImageUrl(canvas.imageServiceId, region, '!300,300');

                const img = document.createElement('img');
                img.src = imgUrl;
                img.alt = `${blockType} region`;
                img.loading = 'lazy';
                img.className = 'figure-thumb';
                img.title = `Canvas: ${parsed.canvasId}\n${parsed.x},${parsed.y} ${parsed.w}×${parsed.h}`;
                card.appendChild(img);
            } else {
                const placeholder = document.createElement('div');
                placeholder.className = 'figure-no-image';
                placeholder.textContent = 'No image service';
                card.appendChild(placeholder);
            }

            const meta = document.createElement('div');
            meta.className = 'figure-meta';
            meta.textContent = `${parsed.w}×${parsed.h}`;
            card.appendChild(meta);

            grid.appendChild(card);
        }

        section.appendChild(grid);
        figuresArea.appendChild(section);
    }
}

// ---- Utilities ---------------------------------------------------------------

function showError(msg) {
    errorArea.innerHTML = `<div class="error-box">${esc(msg)}</div>`;
}

function esc(s) {
    return String(s ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}
