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
 * Each descriptor: { id, label, width, height, imageUrl, imageServiceId }
 *
 * imageUrl  — the direct image URL (info.json/@id stripped, or body id)
 * imageServiceId — IIIF Image API base URL if present, else null
 */
export function extractCanvases(manifest) {
    return (manifest.items ?? []).map(canvas => {
        const label = labelToString(canvas.label);
        let imageUrl = null;
        let imageServiceId = null;

        // Walk: canvas.items[0].items[0] = the painting annotation
        const paintingAnno = canvas.items?.[0]?.items?.[0];
        if (paintingAnno?.motivation === 'painting') {
            const body = paintingAnno.body;
            // body may be a single resource or an array
            const resource = Array.isArray(body) ? body[0] : body;
            if (resource) {
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

        return {
            id: canvas.id,
            label,
            width: canvas.width ?? 1000,
            height: canvas.height ?? 1000,
            imageUrl,
            imageServiceId,
        };
    });
}

// ---- Search service extraction -----------------------------------------------

/**
 * Returns an array of search service descriptors found in the manifest's
 * top-level service array.
 *
 * Each descriptor: { searchUrl, autocompleteUrl }
 * autocompleteUrl may be null if no autocomplete service is nested.
 */
export function extractSearchServices(manifest) {
    const services = normaliseServices(manifest.service);
    const results = [];

    for (const svc of services) {
        const profile = svc.profile ?? svc['@context'] ?? '';
        const isSearch = typeof profile === 'string'
            ? profile.includes('search/1')
            : Array.isArray(profile) && profile.some(p => typeof p === 'string' && p.includes('search/1'));

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
