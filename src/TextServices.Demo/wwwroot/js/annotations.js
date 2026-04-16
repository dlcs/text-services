import {
    fetchManifest,
    extractCanvases,
    extractCanvasAnnotationPageRefs,
    buildImageUrl,
    parseXYWH,
    parseTemporalFragment,
} from './iiif-helpers.js';

// ---- State -------------------------------------------------------------------

let manifest  = null;
let canvases  = [];   // descriptors from extractCanvases()

// ---- DOM refs ----------------------------------------------------------------

const manifestInput  = document.getElementById('manifest-url');
const loadBtn        = document.getElementById('load-btn');
const errorArea      = document.getElementById('error-area');
const annoUi         = document.getElementById('anno-ui');
const canvasInfo     = document.getElementById('canvas-info');
const canvasSelect   = document.getElementById('canvas-select');
const annoColumns    = document.getElementById('anno-columns');

// ---- Init --------------------------------------------------------------------

(async function init() {
    const params = new URLSearchParams(window.location.search);
    const url = params.get('iiif-content');
    if (url) {
        manifestInput.value = url;
        await loadManifest(url);
    }
})();

loadBtn.addEventListener('click', async () => {
    const url = manifestInput.value.trim();
    if (!url) return;
    history.pushState({}, '', `?iiif-content=${encodeURIComponent(url)}`);
    await loadManifest(url);
});

manifestInput.addEventListener('keydown', e => { if (e.key === 'Enter') loadBtn.click(); });

canvasSelect.addEventListener('change', () => {
    const idx = parseInt(canvasSelect.value, 10);
    if (!isNaN(idx)) loadCanvasAnnotations(idx);
});

// ---- Manifest loading --------------------------------------------------------

async function loadManifest(url) {
    errorArea.innerHTML = '';
    annoUi.style.display = 'none';
    annoColumns.innerHTML = '';

    try {
        manifest = await fetchManifest(url);
        canvases = extractCanvases(manifest);

        if (canvases.length === 0) {
            showError('No canvases found in this manifest.');
            return;
        }

        // Populate canvas selector
        canvasSelect.innerHTML = '';
        canvases.forEach((c, i) => {
            const opt = document.createElement('option');
            opt.value = i;
            opt.textContent = `[${i}] ${c.label || c.id}`;
            canvasSelect.appendChild(opt);
        });

        canvasInfo.textContent = `${canvases.length} canvas${canvases.length === 1 ? '' : 'es'}`;
        annoUi.style.display = 'block';

        loadCanvasAnnotations(0);
    } catch (err) {
        showError(err.message);
    }
}

// ---- Annotation page loading -------------------------------------------------

async function loadCanvasAnnotations(canvasIndex) {
    annoColumns.innerHTML = '<div style="color:#888;font-size:0.85rem">Loading…</div>';

    const rawCanvas = manifest.items?.[canvasIndex];
    const canvas    = canvases[canvasIndex];
    if (!rawCanvas || !canvas) return;

    const refs = extractCanvasAnnotationPageRefs(rawCanvas);

    if (refs.length === 0) {
        annoColumns.innerHTML =
            '<div style="color:#888;font-size:0.85rem">No annotation pages found on this canvas.</div>';
        return;
    }

    // Fetch all pages in parallel, preserving order.
    const fetched = await Promise.allSettled(
        refs.map(ref => fetchAnnotationPage(ref.id))
    );

    // Sort: non-generated pages first (left), generated (textGranularity) last (right).
    // "Generated" = has a textGranularity property (our pages) or label contains "transcription".
    const columns = refs.map((ref, i) => ({
        ref,
        result: fetched[i],
        isGenerated: isGeneratedPage(ref, fetched[i]),
    }));
    columns.sort((a, b) => Number(a.isGenerated) - Number(b.isGenerated));

    annoColumns.innerHTML = '';
    annoColumns.style.gridTemplateColumns = `repeat(${columns.length}, minmax(240px, 1fr))`;

    for (const { ref, result, isGenerated } of columns) {
        const col = buildColumn(ref, result, canvas, isGenerated);
        annoColumns.appendChild(col);
    }
}

async function fetchAnnotationPage(url) {
    const res = await fetch(url, {
        headers: { Accept: 'application/json, application/ld+json' },
    });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return res.json();
}

function isGeneratedPage(ref, result) {
    if (ref.label.toLowerCase().includes('transcription')) return true;
    if (result.status === 'fulfilled') {
        const page = result.value;
        if (page.textGranularity) return true;
    }
    return false;
}

// ---- Column rendering --------------------------------------------------------

function buildColumn(ref, settled, canvas, isGenerated) {
    const col = document.createElement('div');
    col.className = 'compare-col';

    if (settled.status === 'rejected') {
        col.innerHTML = `
            <h3 title="${esc(ref.id)}">${esc(truncate(ref.label, 60))}</h3>
            <div class="error-box">${esc(settled.reason?.message ?? 'Failed to load')}</div>`;
        return col;
    }

    const page  = settled.value;
    const items = page.items ?? [];
    const gran  = page.textGranularity ?? null;
    const tag   = isGenerated
        ? `<span class="gen-badge">generated</span>`
        : `<span class="src-badge">source</span>`;

    const label = page.label
        ? Object.values(page.label)[0]?.[0] ?? ref.label
        : ref.label;

    col.innerHTML = `
        <h3 title="${esc(ref.id)}">${esc(truncate(label, 55))} ${tag}</h3>
        ${gran ? `<div style="font-size:0.75rem;color:#666;margin-bottom:0.4rem">textGranularity: <strong>${esc(gran)}</strong></div>` : ''}
        <div style="font-size:0.78rem;color:#888;margin-bottom:0.6rem">${items.length} annotation${items.length === 1 ? '' : 's'}</div>
        <div class="anno-list" id="anno-list-${encodeURIComponent(ref.id)}"></div>`;

    const listEl = col.querySelector('.anno-list');
    renderAnnotationList(listEl, items, canvas);

    return col;
}

function renderAnnotationList(container, items, canvas) {
    if (items.length === 0) {
        container.innerHTML = '<div style="color:#aaa;font-size:0.82rem">(empty)</div>';
        return;
    }

    const limit = 80;
    items.slice(0, limit).forEach((item, i) => {
        const value  = item.body?.value ?? item.body?.['@value'] ?? '';
        const target = item.target ?? '';
        const xywh   = parseXYWH(target);
        const time   = xywh ? null : parseTemporalFragment(target);

        const row = document.createElement('div');
        row.className = 'anno-row';

        let fragmentHtml = '';
        if (xywh) {
            fragmentHtml = `<span class="frag-badge spatial">${xywh.x},${xywh.y} ${xywh.w}×${xywh.h}</span>`;
            // Thumbnail crop if image service is available
            if (canvas.imageServiceId && xywh.w > 0 && xywh.h > 0) {
                const thumbUrl = buildImageUrl(
                    canvas.imageServiceId,
                    `${xywh.x},${xywh.y},${xywh.w},${xywh.h}`,
                    '!180,60'
                );
                fragmentHtml += `<img class="anno-thumb" src="${thumbUrl}" alt="" loading="lazy"
                    crossOrigin="anonymous" onerror="this.style.display='none'">`;
            }
        } else if (time) {
            fragmentHtml = `<span class="frag-badge temporal">${time.startS}s – ${time.endS}s</span>`;
        }

        row.innerHTML = `
            <div class="anno-index">${i + 1}</div>
            <div class="anno-body">
                <div class="anno-text">${esc(value)}</div>
                ${fragmentHtml}
            </div>`;
        container.appendChild(row);
    });

    if (items.length > limit) {
        const more = document.createElement('div');
        more.style.cssText = 'font-size:0.78rem;color:#888;padding:0.3rem 0';
        more.textContent = `… and ${items.length - limit} more`;
        container.appendChild(more);
    }
}

// ---- Helpers -----------------------------------------------------------------

function showError(msg) {
    errorArea.innerHTML = `<div class="error-box">${esc(msg)}</div>`;
}

function esc(s) {
    return String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
}

function truncate(s, n) {
    return s.length > n ? s.slice(0, n) + '…' : s;
}
