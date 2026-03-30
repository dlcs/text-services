import { fetchManifest, extractCanvases, extractSearchServices, buildImageUrl, parseXYWH, normalizeSearchResults, normalizeAutocompleteTerms } from './iiif-helpers.js';

// ---- State -------------------------------------------------------------------
let canvases = [];
let services = [];   // [{ searchUrl, autocompleteUrl }, ...]
let acTimer  = null;

// ---- DOM refs ----------------------------------------------------------------
const manifestInput  = document.getElementById('manifest-url');
const loadBtn        = document.getElementById('load-btn');
const errorArea      = document.getElementById('error-area');
const compareUi      = document.getElementById('compare-ui');
const servicesInfo   = document.getElementById('services-info');
const queryInput     = document.getElementById('query-input');
const searchBtn      = document.getElementById('search-btn');
const compareColumns = document.getElementById('compare-columns');

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
    compareColumns.innerHTML = '';

    try {
        const manifest = await fetchManifest(url);
        canvases = extractCanvases(manifest);
        services = extractSearchServices(manifest);

        if (services.length < 1) {
            showError('No search services found in this manifest.');
            return;
        }

        buildColumns(services);

        const svcLines = services.map((s, i) =>
            `<span class="label">Service ${i + 1}:</span>${esc(s.searchUrl)}`
        );
        servicesInfo.innerHTML = svcLines.join('<br>');

        compareUi.style.display = 'block';
    } catch (err) {
        showError(err.message);
    }
}

function buildColumns(svcs) {
    compareColumns.innerHTML = '';
    compareColumns.style.gridTemplateColumns = `repeat(${svcs.length}, minmax(200px, 1fr))`;

    svcs.forEach((svc, i) => {
        const col = document.createElement('div');
        col.className = 'compare-col';
        col.id = `col-${i}`;
        col.innerHTML = `
            <h3 id="label-${i}" title="${esc(svc.searchUrl)}">${esc(truncate(svc.searchUrl, 60))}</h3>
            <div class="latency" id="latency-${i}"></div>
            <div>
              <strong style="font-size:0.8rem">Autocomplete</strong>
              <ul class="ac-list" id="ac-list-${i}"></ul>
            </div>
            <div>
              <strong style="font-size:0.8rem">Search results</strong>
              <div id="results-list-${i}"></div>
            </div>
            <div class="thumb-grid" id="thumbs-${i}"></div>
        `;
        compareColumns.appendChild(col);
    });
}

// ---- Search ------------------------------------------------------------------

searchBtn.addEventListener('click', doSearch);
queryInput.addEventListener('keydown', e => { if (e.key === 'Enter') doSearch(); });

async function doSearch() {
    const q = queryInput.value.trim();
    if (!q || services.length === 0) return;

    clearResults();

    const results = await Promise.allSettled(
        services.map(svc => timedSearch(svc.searchUrl, q))
    );

    results.forEach((res, i) => renderSearchResults(i, res));
}

async function timedSearch(searchUrl, q) {
    const t0  = Date.now();
    const res = await fetch(`${searchUrl}?q=${encodeURIComponent(q)}`);
    const ms  = Date.now() - t0;
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const data = await res.json();
    return { data, ms };
}

function renderSearchResults(idx, settled) {
    const latencyEl = document.getElementById(`latency-${idx}`);
    const resultsEl = document.getElementById(`results-list-${idx}`);
    const thumbsEl  = document.getElementById(`thumbs-${idx}`);

    thumbsEl.innerHTML  = '';
    resultsEl.innerHTML = '';

    if (settled.status === 'rejected') {
        latencyEl.textContent = '';
        resultsEl.innerHTML = `<div class="error-box">${esc(settled.reason?.message ?? 'Error')}</div>`;
        return;
    }

    const { data, ms } = settled.value;
    latencyEl.textContent = `${ms} ms`;

    const { hitList, allRects } = normalizeSearchResults(data);

    const total = hitList.length;
    resultsEl.innerHTML = `<div style="font-size:0.78rem;color:#666;margin-bottom:0.3rem">${total} hit${total === 1 ? '' : 's'}</div>`;

    hitList.slice(0, 20).forEach(hit => {
        const div = document.createElement('div');
        div.className = 'result-item';
        div.innerHTML =
            `${esc(hit.before ?? '')} <span class="match">${esc(hit.match)}</span> ${esc(hit.after ?? '')}`;
        resultsEl.appendChild(div);
    });

    // Thumbnails — one per matched word rectangle
    allRects.slice(0, 20).forEach(rect => {
        const canvas = canvases.find(c => c.id === rect.canvasId);
        if (!canvas?.imageServiceId) return;

        const hitForRect = hitList.find(h => h.canvasId === rect.canvasId);
        const matchLabel = hitForRect?.match ?? '';

        const thumbUrl = buildImageUrl(
            canvas.imageServiceId,
            `${rect.x},${rect.y},${rect.w},${rect.h}`,
            '!150,150'
        );

        const img = document.createElement('img');
        img.src         = thumbUrl;
        img.title       = matchLabel;
        img.alt         = matchLabel;
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
    if (q.length < 3 || services.length === 0) return;

    const results = await Promise.allSettled(
        services.map(svc => fetchAc(svc.autocompleteUrl, q))
    );

    results.forEach((res, i) => renderAcList(i, res));
}

async function fetchAc(url, q) {
    if (!url) return [];
    const res = await fetch(`${url}?q=${encodeURIComponent(q)}`);
    if (!res.ok) return [];
    const data = await res.json();
    return normalizeAutocompleteTerms(data);
}

function renderAcList(idx, settled) {
    const ul = document.getElementById(`ac-list-${idx}`);
    if (!ul) return;
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
    services.forEach((_, i) => {
        const latency = document.getElementById(`latency-${i}`);
        const results = document.getElementById(`results-list-${i}`);
        const thumbs  = document.getElementById(`thumbs-${i}`);
        if (latency) latency.textContent = '';
        if (results) results.innerHTML = '<div style="color:#888;font-size:0.85rem">Searching…</div>';
        if (thumbs)  thumbs.innerHTML = '';
    });
}

function clearAcLists() {
    services.forEach((_, i) => {
        const ul = document.getElementById(`ac-list-${i}`);
        if (ul) ul.innerHTML = '';
    });
}

function showError(msg) {
    errorArea.innerHTML = `<div class="error-box">${esc(msg)}</div>`;
}

function esc(s) {
    return String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
}

function truncate(s, n) {
    return s.length > n ? s.slice(0, n) + '…' : s;
}
