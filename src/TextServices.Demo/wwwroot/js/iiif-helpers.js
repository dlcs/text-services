/**
 * Shared IIIF helper utilities for the TextServices Demo.
 * Imported as an ES module by viewer.js, builder.js, compare.js.
 *
 * Supports IIIF Presentation API v3 only. v2 manifests are rejected.
 */

// ---- Config ------------------------------------------------------------------

let _config = null;

export async function loadConfig() {
    if (_config) return _config;
    const res = await fetch('/demo-config');
    _config = await res.json();
    return _config;
}

// ---- Manifest loading --------------------------------------------------------

/**
 * Fetches and validates a IIIF Presentation v3 Manifest.
 * Throws an Error if the resource is not reachable or not a v3 Manifest.
 */
export async function fetchManifest(url) {
    const res = await fetch(url, {
        headers: { 'Accept': 'application/ld+json, application/json' }
    });
    if (!res.ok) throw new Error(`HTTP ${res.status} fetching manifest: ${url}`);

    const manifest = await res.json();
    const ctx = manifest['@context'];
    const isV3 = Array.isArray(ctx)
        ? ctx.includes('http://iiif.io/api/presentation/3/context.json')
        : ctx === 'http://iiif.io/api/presentation/3/context.json';

    if (!isV3) throw new Error('Only IIIF Presentation API v3 manifests are supported.');
    if (manifest.type !== 'Manifest') throw new Error(`Expected type "Manifest", got "${manifest.type}".`);

    return manifest;
}

// ---- Canvas extraction -------------------------------------------------------

/**
 * Returns an array of canvas descriptors from a v3 Manifest.
 * Each descriptor: { id, label, width, height, imageUrl, imageServiceId, videoUrl, isTemporalContent }
 *
 * imageUrl          — the direct image URL (info.json stripped, or body id)
 * imageServiceId    — IIIF Image API base URL if present, else null
 * videoUrl          — direct video/audio URL if the canvas is temporal, else null
 * isTemporalContent — true when the painting body is a Video or Sound resource
 */
export function extractCanvases(manifest) {
    return (manifest.items ?? []).map(canvas => {
        const label = labelToString(canvas.label);
        let imageUrl = null;
        let imageServiceId = null;
        let videoUrl = null;
        let isTemporalContent = false;

        // Walk: canvas.items[0].items[0] = the painting annotation
        const paintingAnno = canvas.items?.[0]?.items?.[0];
        if (paintingAnno?.motivation === 'painting') {
            const body = paintingAnno.body;
            // body may be a single resource or an array
            const resource = Array.isArray(body) ? body[0] : body;
            if (resource) {
                const type = resource.type ?? resource['@type'] ?? '';
                if (type === 'Video' || type === 'Sound') {
                    isTemporalContent = true;
                    videoUrl = resource.id ?? resource['@id'] ?? null;
                } else {
                    imageUrl = resource.id ?? resource['@id'] ?? null;
                    const services = normaliseServices(resource.service);
                    const imgSvc = services.find(s => isImageService(s));
                    if (imgSvc) {
                        imageServiceId = imgSvc.id ?? imgSvc['@id'] ?? null;
                    }
                    // If the image URL points to an info.json, strip it
                    if (imageUrl?.endsWith('/info.json')) {
                        imageUrl = imageUrl.slice(0, -'/info.json'.length);
                    }
                }
            }
        }

        return {
            id: canvas.id,
            label,
            width: canvas.width ?? 0,
            height: canvas.height ?? 0,
            imageUrl,
            imageServiceId,
            videoUrl,
            isTemporalContent,
        };
    });
}

// ---- Canvas annotation page reference extraction ----------------------------

/**
 * Returns annotation page references from a single canvas's `annotations` array.
 * Each reference: { id, label }
 *
 * These are external AnnotationPage pointers (not embedded items).
 * Used by the annotation comparator to discover line/word annotation pages.
 */
export function extractCanvasAnnotationPageRefs(canvas) {
    const annos = canvas.annotations;
    if (!Array.isArray(annos)) return [];
    return annos
        .filter(a => a.id && (a.type === 'AnnotationPage' || a['@type'] === 'sc:AnnotationPage'))
        .map(a => ({
            id:    a.id ?? a['@id'],
            label: labelToString(a.label) || (a.id ?? ''),
        }));
}

// ---- Figures annotation page extraction --------------------------------------

/**
 * Returns the URL of the figures AnnotationPage referenced in the manifest's
 * top-level `annotations` array, or null if none is present.
 *
 * Recognises entries whose label (en) contains "figure" (case-insensitive).
 */
export function extractFiguresUrl(manifest) {
    const annotations = manifest.annotations;
    if (!Array.isArray(annotations)) return null;

    for (const anno of annotations) {
        const label = anno.label?.en?.[0] ?? '';
        if (typeof label === 'string' && label.toLowerCase().includes('figure')) {
            return anno.id ?? anno['@id'] ?? null;
        }
    }
    return null;
}

// ---- Rendering extraction ----------------------------------------------------

/**
 * Returns an array of rendering descriptors from a v3 Manifest.
 * Each descriptor: { id, label, format }
 */
export function extractRendering(manifest) {
    const items = manifest.rendering;
    if (!items) return [];
    return (Array.isArray(items) ? items : [items])
        .filter(r => r.id)
        .map(r => ({
            id:     r.id,
            label:  labelToString(r.label) || r.format || 'Download',
            format: r.format ?? null,
        }));
}

// ---- Search service extraction -----------------------------------------------

/**
 * Returns an array of search service descriptors found in the manifest's
 * top-level service array.
 *
 * Each descriptor: { searchUrl, autocompleteUrl }
 * autocompleteUrl may be null if no autocomplete service is nested.
 *
 * Detects both:
 *   - IIIF Presentation 3 style: type "SearchService2" / "SearchService1"
 *   - Legacy style: profile containing "search/1" or "search/2"
 */
export function extractSearchServices(manifest) {
    const services = normaliseServices(manifest.service);
    const results = [];

    for (const svc of services) {
        const profile = svc.profile ?? svc['@context'] ?? '';
        const type    = svc.type ?? svc['@type'] ?? '';

        const profileStr = typeof profile === 'string' ? profile
            : Array.isArray(profile) ? profile.find(p => typeof p === 'string' && p.includes('search')) ?? ''
            : '';

        const isSearch = profileStr.includes('search')
            || type === 'SearchService1'
            || type === 'SearchService2';

        if (!isSearch) continue;

        const searchUrl = svc.id ?? svc['@id'] ?? null;
        if (!searchUrl) continue;

        // Look for nested autocomplete service
        let autocompleteUrl = null;
        const nested = normaliseServices(svc.service);
        for (const n of nested) {
            const np = n.profile ?? '';
            const nt = n['@type'] ?? n.type ?? '';
            const isAc = (typeof np === 'string' && np.includes('autocomplete'))
                      || (typeof nt === 'string' && nt.toLowerCase().includes('autocomplete'));
            if (isAc) {
                autocompleteUrl = n.id ?? n['@id'] ?? null;
                break;
            }
        }

        results.push({ searchUrl, autocompleteUrl });
    }

    return results;
}

// ---- Search response normalisation -------------------------------------------

/**
 * Normalises a IIIF Search API v1 or v2 response into a common shape.
 *
 * Returns:
 *   {
 *     hitList:  [{ canvasId, x, y, w, h, before, match, after }],
 *     allRects: [{ canvasId, x, y, w, h }]   // one per matched word
 *   }
 */
export function normalizeSearchResults(data) {
    if (data.type === 'AnnotationPage') {
        return _normalizeV2Results(data);
    }
    return _normalizeV1Results(data);
}

function _normalizeV1Results(data) {
    const resourceById = {};
    for (const r of data.resources ?? []) {
        resourceById[r['@id']] = r;
    }

    const hitList  = [];
    const allRects = [];

    for (const hit of data.hits ?? []) {
        const firstAnnoId = hit.annotations?.[0];
        const firstAnno   = resourceById[firstAnnoId];
        if (!firstAnno) continue;

        const parsed = parseXYWH(firstAnno.on);
        if (!parsed) continue;

        for (const annoId of (hit.annotations ?? [])) {
            const anno = resourceById[annoId];
            if (!anno) continue;
            const p = parseXYWH(anno.on);
            if (p) allRects.push(p);
        }

        hitList.push({
            canvasId: parsed.canvasId,
            x: parsed.x, y: parsed.y, w: parsed.w, h: parsed.h,
            before: hit.before ?? '',
            match:  hit.match  ?? '',
            after:  hit.after  ?? '',
        });
    }

    return { hitList, allRects };
}

function _normalizeV2Results(data) {
    const annoById = {};
    for (const item of data.items ?? []) {
        annoById[item.id] = item;
    }

    // allRects = spatial matched word positions (used for image overlays)
    const allRects = (data.items ?? [])
        .map(item => parseXYWH(item.target))
        .filter(Boolean);

    const hitList = [];

    const contextItems = (data.annotations ?? []).flatMap(p => p.items ?? []);
    const contextualizing = contextItems.filter(a => a.motivation === 'contextualizing');

    if (contextualizing.length > 0) {
        for (const anno of contextualizing) {
            const source    = anno.target?.source;
            const paintAnno = annoById[source];
            if (!paintAnno) continue;

            // Try spatial fragment first, then temporal
            const spatial  = parseXYWH(paintAnno.target);
            const temporal = spatial ? null : parseTemporalFragment(paintAnno.target);
            const fragment = spatial ?? temporal;
            if (!fragment) continue;

            const selectorArr = anno.target?.selector;
            const tqs = Array.isArray(selectorArr)
                ? selectorArr.find(s => s.type === 'TextQuoteSelector')
                : (selectorArr?.type === 'TextQuoteSelector' ? selectorArr : null);

            hitList.push({
                canvasId: fragment.canvasId,
                x: spatial?.x ?? 0, y: spatial?.y ?? 0,
                w: spatial?.w ?? 0, h: spatial?.h ?? 0,
                startS: temporal?.startS ?? 0, endS: temporal?.endS ?? 0,
                before: tqs?.prefix  ?? '',
                match:  tqs?.exact   ?? paintAnno.body?.value ?? '',
                after:  tqs?.suffix  ?? '',
            });
        }
    } else {
        // No contextualizing annotations — use items directly (no context text)
        for (const item of data.items ?? []) {
            const spatial  = parseXYWH(item.target);
            const temporal = spatial ? null : parseTemporalFragment(item.target);
            const fragment = spatial ?? temporal;
            if (!fragment) continue;
            hitList.push({
                canvasId: fragment.canvasId,
                x: spatial?.x ?? 0, y: spatial?.y ?? 0,
                w: spatial?.w ?? 0, h: spatial?.h ?? 0,
                startS: temporal?.startS ?? 0, endS: temporal?.endS ?? 0,
                before: '', match: item.body?.value ?? '', after: '',
            });
        }
    }

    return { hitList, allRects };
}

/**
 * Normalises a IIIF Search API v1 TermList or v2 TermPage into a plain string array.
 */
export function normalizeAutocompleteTerms(data) {
    if (data.type === 'TermPage') {
        return (data.items ?? []).map(t => t.value).filter(Boolean);
    }
    // v1 TermList
    return (data.terms ?? []).map(t => t.match).filter(Boolean);
}

// ---- IIIF Image API ----------------------------------------------------------

/**
 * Builds a IIIF Image API URL.
 * @param {string} serviceId  - Base URL of the image service (no trailing slash)
 * @param {string} region     - e.g. "full" or "x,y,w,h"
 * @param {string} size       - e.g. "!800,600" or "full"
 * @param {string} [rotation] - default "0"
 * @param {string} [quality]  - default "default"
 * @param {string} [format]   - default "jpg"
 */
export function buildImageUrl(serviceId, region, size, rotation = '0', quality = 'default', format = 'jpg') {
    const base = serviceId.replace(/\/$/, '');
    return `${base}/${region}/${size}/${rotation}/${quality}.${format}`;
}

// ---- xywh fragment parsing ---------------------------------------------------

/**
 * Parses an annotation `on` value like
 *   "https://example.org/canvas/1#xywh=10,20,50,30"
 * Returns { canvasId, x, y, w, h } or null if no fragment.
 */
export function parseXYWH(on) {
    if (!on) return null;
    const hashIdx = on.indexOf('#xywh=');
    if (hashIdx === -1) return null;
    const canvasId = on.slice(0, hashIdx);
    const parts = on.slice(hashIdx + 6).split(',').map(Number);
    if (parts.length !== 4 || parts.some(isNaN)) return null;
    return { canvasId, x: parts[0], y: parts[1], w: parts[2], h: parts[3] };
}

/**
 * Parses a temporal annotation target like
 *   "https://example.org/canvas/1#t=10.167,14.973"
 * Returns { canvasId, startS, endS } (seconds as floats) or null if no #t= fragment.
 */
export function parseTemporalFragment(on) {
    if (!on) return null;
    const hashIdx = on.indexOf('#t=');
    if (hashIdx === -1) return null;
    const canvasId = on.slice(0, hashIdx);
    const parts = on.slice(hashIdx + 3).split(',').map(Number);
    if (parts.length !== 2 || parts.some(isNaN)) return null;
    return { canvasId, startS: parts[0], endS: parts[1] };
}

// ---- Utilities ---------------------------------------------------------------

function labelToString(label) {
    if (!label) return '';
    if (typeof label === 'string') return label;
    // v3 LanguageMap: { "en": ["Page 1"], ... }
    const values = Object.values(label);
    return values.length > 0 ? (values[0][0] ?? '') : '';
}

function normaliseServices(service) {
    if (!service) return [];
    if (Array.isArray(service)) return service;
    return [service];
}

function isImageService(svc) {
    const profile = svc.profile ?? svc['@context'] ?? '';
    if (typeof profile === 'string') {
        return profile.includes('iiif.io/api/image');
    }
    if (Array.isArray(profile)) {
        return profile.some(p => typeof p === 'string' && p.includes('iiif.io/api/image'));
    }
    return false;
}
