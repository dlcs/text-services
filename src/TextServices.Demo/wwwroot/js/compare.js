import { fetchManifest, extractCanvases, extractSearchServices, buildImageUrl, parseXYWH } from './iiif-helpers.js';

// ---- State -------------------------------------------------------------------
let canvases = [];
let services = [];   // [{ searchUrl, autocompleteUrl }, ...]
let acTimer  = null;

// ---- DOM refs ----------------------------------------------------------------
const manifestInput = document.getElementById('manifest-url');
const loadBtn       = document.getElementById('load-btn');
const errorArea     = document.getElementById('error-area');
const compareUi     = document.getElementById('compare-ui');
const servicesInfo  = document.getElementById('services-info');
const queryInput    = document.getElementById('query-input');
const searchBtn     = document.getElementById('search-btn');

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

// ---- Manifest loading --------------------------------------------------------

async function loadManifest(url) {
    errorArea.innerHTML = '';
    compareUi.style.display = 'none';

    try {
        const manifest = await fetchManifest(url);
        canvases = extractCanvases(manifest);
        services = extractSearchServices(manifest);

        if (services.length < 2) {
            showError(`Found ${services.length} search service(s). This page requires at least two to compare.`);
            return;
        }

        // Update labels
        setLabel('a', services[0].searchUrl);
        setLabel('b', services[1].searchUrl);

        // Update info box
        const svcLines = services.slice(0, 2).map((s, i) =>
            `<span class="label">Service ${i === 0 ? 'A' : 'B'}:</span>${esc(s.searchUrl)}`
        );
        servicesInfo.innerHTML = svcLines.join('<br>');

        compareUi.style.display = 'block';
    } catch (err) {
        showError(err.message);
    }
}

function setLabel(side, url) {
    document.getElementById(`label-${side}`).textContent = url;
}

// ---- Search ------------------------------------------------------------------

searchBtn.addEventListener('click', doSearch);
queryInput.addEventListener('keydown', e => { if (e.key === 'Enter') doSearch(); });

async function doSearch() {
    const q = queryInput.value.trim();
    if (!q || services.length < 2) return;

    clearResults();

    const [resA, resB] = await Promise.allSettled([
        timedSearch(services[0].searchUrl, q),
        timedSearch(services[1].searchUrl, q),
    ]);

    renderSearchResults('a', resA, services[0]);
    renderSearchResults('b', resB, services[1]);
}

async function timedSearch(searchUrl, q) {
    const t0 = Date.now();
    const res = await fetch(`${searchUrl}?q=${encodeURIComponent(q)}`);
    const ms  = Date.now() - t0;
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const data = await res.json();
    return { data, ms };
}

function renderSearchResults(side, settled, svc) {
    const latencyEl  = document.getElementById(`latency-${side}`);
    const resultsEl  = document.getElementById(`results-list-${side}`);
    const thumbsEl   = document.getElementById(`thumbs-${side}`);

    thumbsEl.innerHTML = '';
    resultsEl.innerHTML = '';

    if (settled.status === 'rejected') {
        latencyEl.textContent = '';
        resultsEl.innerHTML = `<div class="error-box">${esc(settled.reason?.message ?? 'Error')}</div>`;
        return;
    }

    const { data, ms } = settled.value;
    latencyEl.textContent = `${ms} ms`;

    const resources = data.resources ?? [];
    const hits      = data.hits ?? [];
    const resourceById = Object.fromEntries(resources.map(r => [r['@id'], r]));

    const total = hits.length;
    resultsEl.innerHTML = `<div style="font-size:0.78rem;color:#666;margin-bottom:0.3rem">${total} hit${total === 1 ? '' : 's'}</div>`;

    hits.slice(0, 20).forEach(hit => {
        const div = document.createElement('div');
        div.className = 'result-item';
        div.innerHTML =
            `${esc(hit.before ?? '')} <span class="match">${esc(hit.match)}</span> ${esc(hit.after ?? '')}`;
        resultsEl.appendChild(div);
    });

    // Thumbnails — one per hit, using the union bounding box of all annotations
    hits.slice(0, 12).forEach(hit => {
        // Collect all parsed xywh values for this hit (may be multiple words)
        const rects = (hit.annotations ?? [])
            .map(id => parseXYWH(resourceById[id]?.on))
            .filter(Boolean);
        if (rects.length === 0) return;

        // All annotations should be on the same canvas; use the first as reference
        const canvasId = rects[0].canvasId;
        if (rects.some(r => r.canvasId !== canvasId)) return; // spans canvases — skip

        const canvas = canvases.find(c => c.id === canvasId);
        if (!canvas?.imageServiceId) return;

        // Union bounding box
        const x1 = Math.min(...rects.map(r => r.x));
        const y1 = Math.min(...rects.map(r => r.y));
        const x2 = Math.max(...rects.map(r => r.x + r.w));
        const y2 = Math.max(...rects.map(r => r.y + r.h));

        const thumbUrl = buildImageUrl(
            canvas.imageServiceId,
            `${x1},${y1},${x2 - x1},${y2 - y1}`,
            '!150,150'
        );

        const img = document.createElement('img');
        img.src   = thumbUrl;
        img.title = hit.match;
        img.alt   = hit.match;
        img.crossOrigin = 'anonymous';
        thumbsEl.appendChild(img);
    });
}

// ---- Autocomplete ------------------------------------------------------------

queryInput.addEventListener('input', () => {
    clearTimeout(acTimer);
    acTimer = setTimeout(doAutocomplete, 300);
});

async function doAutocomplete() {
    const q = queryInput.value.trim();
    clearAcLists();
    if (q.length < 3 || services.length < 2) return;

    const [resA, resB] = await Promise.allSettled([
        fetchAc(services[0].autocompleteUrl, q),
        fetchAc(services[1].autocompleteUrl, q),
    ]);

    renderAcList('a', resA);
    renderAcList('b', resB);
}

async function fetchAc(url, q) {
    if (!url) return [];
    const res = await fetch(`${url}?q=${encodeURIComponent(q)}`);
    if (!res.ok) return [];
    const data = await res.json();
    return (data.terms ?? []).map(t => t.match);
}

function renderAcList(side, settled) {
    const ul = document.getElementById(`ac-list-${side}`);
    ul.innerHTML = '';
    if (settled.status === 'rejected') return;
    const terms = settled.value ?? [];
    terms.slice(0, 10).forEach(term => {
        const li = document.createElement('li');
        li.textContent = term;
        li.addEventListener('click', () => {
            queryInput.value = term;
            doSearch();
        });
        ul.appendChild(li);
    });
}

// ---- Helpers -----------------------------------------------------------------

function clearResults() {
    ['a', 'b'].forEach(s => {
        document.getElementById(`latency-${s}`).textContent = '';
        document.getElementById(`results-list-${s}`).innerHTML = '<div style="color:#888;font-size:0.85rem">Searching…</div>';
        document.getElementById(`thumbs-${s}`).innerHTML = '';
    });
}

function clearAcLists() {
    ['a', 'b'].forEach(s => document.getElementById(`ac-list-${s}`).innerHTML = '');
}

function showError(msg) {
    errorArea.innerHTML = `<div class="error-box">${esc(msg)}</div>`;
}

function esc(s) {
    return String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
}
