import { fetchManifest, extractCanvases, extractSearchServices, extractRendering, buildImageUrl, parseXYWH, normalizeSearchResults, normalizeAutocompleteTerms } from './iiif-helpers.js';

// ---- State -------------------------------------------------------------------
let canvases = [];
let currentIndex = 0;
let searchService = null;   // { searchUrl, autocompleteUrl }
let currentHits = [];       // flat array of { canvasId, x, y, w, h, startS, endS, before, match, after }
let acTimer = null;

// ---- DOM refs ----------------------------------------------------------------
const manifestInput = document.getElementById('manifest-url');
const loadBtn       = document.getElementById('load-btn');
const errorArea     = document.getElementById('error-area');
const servicesInfo  = document.getElementById('services-info');
const viewerWrap    = document.getElementById('viewer-wrap');
const canvasImg     = document.getElementById('canvas-img');
const canvasArea    = document.getElementById('canvas-area');
const imgWrap       = document.getElementById('img-wrap');
const videoWrap     = document.getElementById('video-wrap');
const canvasVideo   = document.getElementById('canvas-video');
const prevBtn       = document.getElementById('prev-btn');
const nextBtn       = document.getElementById('next-btn');
const pageLabel     = document.getElementById('page-label');
const searchPanel   = document.getElementById('search-panel');
const searchForm    = document.getElementById('search-form');
const searchInput   = document.getElementById('search-input');
const acSuggestions = document.getElementById('ac-suggestions');
const resultsList   = document.getElementById('results-list');
const resultsCount  = document.getElementById('results-count');

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

manifestInput.addEventListener('keydown', async e => {
    if (e.key === 'Enter') loadBtn.click();
});

// ---- Manifest loading --------------------------------------------------------

async function loadManifest(url) {
    clearError();
    viewerWrap.style.display = 'none';
    servicesInfo.style.display = 'none';
    resultsList.innerHTML = '';
    resultsCount.textContent = '';

    try {
        const manifest = await fetchManifest(url);
        canvases = extractCanvases(manifest);
        if (canvases.length === 0) throw new Error('Manifest contains no canvases.');

        const services  = extractSearchServices(manifest);
        searchService = services.length > 0 ? services[0] : null;

        const rendering = extractRendering(manifest);
        updateServicesInfo(url, searchService, rendering);
        searchPanel.style.display = searchService ? 'block' : 'none';

        currentIndex = 0;
        currentHits = [];
        renderCanvas(0);
        viewerWrap.style.display = 'flex';
    } catch (err) {
        showError(err.message);
    }
}

function updateServicesInfo(manifestUrl, svc, rendering) {
    const lines = [`<span class="label">Manifest:</span>${esc(manifestUrl)}`];
    if (svc) {
        lines.push(`<span class="label">Search service:</span>${esc(svc.searchUrl)}`);
        lines.push(`<span class="label">Autocomplete:</span>${svc.autocompleteUrl ? esc(svc.autocompleteUrl) : '(none)'}`);
    } else {
        lines.push('<span class="label">Search service:</span>none detected');
    }
    if (rendering.length > 0) {
        const links = rendering.map(r =>
            `<a href="${esc(r.id)}" target="_blank" class="download-link">${esc(r.label)}</a>`
        ).join(' ');
        lines.push(`<span class="label">Downloads:</span>${links}`);
    }
    servicesInfo.innerHTML = lines.join('<br>');
    servicesInfo.style.display = 'block';
}

// ---- Canvas rendering --------------------------------------------------------

function renderCanvas(index) {
    const canvas = canvases[index];
    currentIndex = index;

    pageLabel.textContent = `${canvas.label || ''}  (${index + 1} / ${canvases.length})`;
    prevBtn.disabled = index === 0;
    nextBtn.disabled = index === canvases.length - 1;

    if (canvas.isTemporalContent) {
        renderVideoCanvas(canvas);
    } else {
        renderImageCanvas(canvas);
    }
}

function renderImageCanvas(canvas) {
    videoWrap.style.display = 'none';
    imgWrap.style.display = 'inline-block';

    // Remove existing overlays
    imgWrap.querySelectorAll('.hit-overlay').forEach(el => el.remove());

    let src = null;
    if (canvas.imageServiceId) {
        src = buildImageUrl(canvas.imageServiceId, 'full', '!1200,900');
    } else if (canvas.imageUrl) {
        src = canvas.imageUrl;
    }

    if (src) {
        canvasImg.src = src;
        canvasImg.onload = () => renderHitsForCanvas(canvas.id);
    } else {
        canvasImg.src = '';
        canvasImg.alt = 'No image available';
    }
}

function renderVideoCanvas(canvas) {
    imgWrap.style.display = 'none';
    videoWrap.style.display = 'block';

    if (canvas.videoUrl && canvasVideo.src !== canvas.videoUrl) {
        canvasVideo.src = canvas.videoUrl;
    }
}

prevBtn.addEventListener('click', () => renderCanvas(currentIndex - 1));
nextBtn.addEventListener('click', () => renderCanvas(currentIndex + 1));

// ---- Hit overlays (spatial only) --------------------------------------------

function renderHitsForCanvas(canvasId) {
    imgWrap.querySelectorAll('.hit-overlay').forEach(el => el.remove());

    const hits = currentHits.filter(h => h.canvasId === canvasId && (h.w > 0 || h.h > 0));
    const canvas = canvases.find(c => c.id === canvasId);
    if (!canvas || hits.length === 0) return;

    hits.forEach(hit => {
        const overlay = document.createElement('div');
        overlay.className = 'hit-overlay';

        // Position as percentages relative to canvas coordinate space,
        // so they survive image resizes.
        overlay.style.left   = `${(hit.x / canvas.width) * 100}%`;
        overlay.style.top    = `${(hit.y / canvas.height) * 100}%`;
        overlay.style.width  = `${(hit.w / canvas.width) * 100}%`;
        overlay.style.height = `${(hit.h / canvas.height) * 100}%`;

        imgWrap.appendChild(overlay);
    });
}

// Reposition overlays on resize via ResizeObserver.
new ResizeObserver(() => {
    if (canvases.length > 0 && !canvases[currentIndex].isTemporalContent) {
        renderHitsForCanvas(canvases[currentIndex].id);
    }
}).observe(imgWrap);

// ---- Search ------------------------------------------------------------------

searchForm.addEventListener('submit', async e => {
    e.preventDefault();
    const q = searchInput.value.trim();
    if (!q || !searchService) return;
    await doSearch(q);
});

async function doSearch(q) {
    resultsList.innerHTML = '<li style="color:#888">Searching…</li>';
    resultsCount.textContent = '';
    currentHits = [];
    imgWrap.querySelectorAll('.hit-overlay').forEach(el => el.remove());

    try {
        const url = `${searchService.searchUrl}?q=${encodeURIComponent(q)}`;
        const res = await fetch(url);
        if (!res.ok) throw new Error(`Search returned HTTP ${res.status}`);
        const data = await res.json();

        const { hitList, allRects } = normalizeSearchResults(data);
        currentHits = allRects;

        resultsCount.textContent = `${hitList.length} hit${hitList.length === 1 ? '' : 's'}`;
        resultsList.innerHTML = '';

        if (hitList.length === 0) {
            resultsList.innerHTML = '<li style="color:#888">No results.</li>';
            return;
        }

        hitList.forEach(hit => {
            const li = document.createElement('li');
            const timeStr = hit.startS > 0
                ? `<span class="hit-time">${formatTime(hit.startS)}</span> `
                : '';
            li.innerHTML =
                timeStr +
                `<span class="hit-before">${esc(hit.before)} </span>` +
                `<span class="hit-match">${esc(hit.match)}</span>` +
                `<span class="hit-after"> ${esc(hit.after)}</span>`;
            li.addEventListener('click', () => navigateToHit(hit));
            resultsList.appendChild(li);
        });

        // Show overlays on the current canvas immediately if it has spatial hits
        if (canvases.length > 0 && !canvases[currentIndex].isTemporalContent) {
            renderHitsForCanvas(canvases[currentIndex].id);
        }

    } catch (err) {
        resultsList.innerHTML = `<li style="color:#900">${esc(err.message)}</li>`;
    }
}

function navigateToHit(hit) {
    const idx = canvases.findIndex(c => c.id === hit.canvasId);
    if (idx === -1) return;

    if (idx !== currentIndex) {
        renderCanvas(idx);
    }

    if (hit.startS > 0) {
        // Temporal: seek to hit position, then play once seek completes.
        // play() called before seeked fires would race and start from position 0.
        const doSeek = (startS) => {
            canvasVideo.addEventListener('seeked', () => {
                canvasVideo.play().catch(() => {});
            }, { once: true });
            canvasVideo.currentTime = startS;
        };
        if (canvasVideo.readyState >= 1) {
            doSeek(hit.startS);
        } else {
            canvasVideo.addEventListener('loadedmetadata', () => doSeek(hit.startS), { once: true });
        }
        canvasArea.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    } else {
        // Spatial: render overlays (image may still be loading if we just switched canvas)
        if (canvasImg.complete) {
            renderHitsForCanvas(hit.canvasId);
        }
        // else overlays rendered via canvasImg.onload
    }
}

// ---- Autocomplete ------------------------------------------------------------

searchInput.addEventListener('input', () => {
    clearTimeout(acTimer);
    acTimer = setTimeout(fetchAutocomplete, 300);
});

async function fetchAutocomplete() {
    const q = searchInput.value.trim();
    acSuggestions.innerHTML = '';
    if (!searchService?.autocompleteUrl || q.length < 3) return;

    try {
        const url = `${searchService.autocompleteUrl}?q=${encodeURIComponent(q)}`;
        const res = await fetch(url);
        if (!res.ok) return;
        const data = await res.json();

        normalizeAutocompleteTerms(data).slice(0, 20).forEach(term => {
            const opt = document.createElement('option');
            opt.value = term;
            acSuggestions.appendChild(opt);
        });
    } catch { /* ignore autocomplete errors */ }
}

// ---- Helpers -----------------------------------------------------------------

/**
 * Formats a time in seconds as MM:SS or H:MM:SS.
 */
function formatTime(totalSeconds) {
    const s = Math.floor(totalSeconds);
    const h = Math.floor(s / 3600);
    const m = Math.floor((s % 3600) / 60);
    const sec = s % 60;
    const mm = String(m).padStart(2, '0');
    const ss = String(sec).padStart(2, '0');
    return h > 0 ? `${h}:${mm}:${ss}` : `${mm}:${ss}`;
}

function showError(msg) {
    errorArea.innerHTML = `<div class="error-box">${esc(msg)}</div>`;
}
function clearError() { errorArea.innerHTML = ''; }
function esc(s) {
    return String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
}
