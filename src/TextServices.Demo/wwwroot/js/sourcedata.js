import { loadConfig } from './iiif-helpers.js';

const STORAGE_KEY = 'textservices_demo_jobs';   // shared with builder.js

let config = null;

const configInfo     = document.getElementById('config-info');
const errorArea      = document.getElementById('error-area');
const jsonEditor     = document.getElementById('json-editor');
const newIdBtn       = document.getElementById('new-id-btn');
const formatBtn      = document.getElementById('format-btn');
const submitBtn      = document.getElementById('submit-btn');
const validationMsg  = document.getElementById('validation-msg');
const resultArea     = document.getElementById('result-area');

// ---- Init --------------------------------------------------------------------

(async function init() {
    config = await loadConfig();
    configInfo.innerHTML =
        `<span class="label">Builder API:</span>${esc(config.builderApi)}&nbsp;&nbsp;` +
        `<span class="label">Search API:</span>${esc(config.searchApi)}`;
    configInfo.style.display = 'block';

    jsonEditor.value = JSON.stringify(buildTemplate(newJobId()), null, 2);
    jsonEditor.addEventListener('input', validateQuiet);
})();

// ---- Template ----------------------------------------------------------------

function newJobId() {
    // crypto.randomUUID() is available in all modern browsers
    const suffix = crypto.randomUUID().replace(/-/g, '').slice(0, 8);
    return `local/sd-${suffix}`;
}

// Pre-populated with E2E fixture files drawn from the Wellcome b2888193x manifest.
//
// imageUri is the body.id from the painting annotation in the source manifest —
// an already-resolved image URL that is echoed back as-is into the synthesised manifest.
// Pages 1–2 use the concrete HTTPS image URLs from the Wellcome manifest (656×1024,
// the largest entry in the image service sizes array).
// Page 3 uses a file:// imageUri pointing to a locally saved copy of the same JPEG,
// demonstrating that file:// images are routed through the Search API /proxy/image
// endpoint so IIIF viewers can load them.
//
// Fixture paths are computed by the server (relative to its ContentRootPath) and
// returned via /demo-config — no checkout-specific paths in this file.
function buildTemplate(id) {
    const FIXTURE_ALTO   = config.fixtureAlto   ?? '';
    const FIXTURE_IMAGES = config.fixtureImages ?? '';
    return {
        id,
        sourceData: [
            {
                id:       'https://iiif.wellcomecollection.org/presentation/b2888193x/canvases/b2888193x_0001.jp2',
                width:    2679,
                height:   4179,
                textUri:  `${FIXTURE_ALTO}/b2888193x_0001.jp2.xml`,
                imageUri: 'https://iiif.wellcomecollection.org/image/b2888193x_0001.jp2/full/656,1024/0/default.jpg',
                profile:  'http://www.loc.gov/standards/alto/v3/alto.xsd',
            },
            {
                id:       'https://iiif.wellcomecollection.org/presentation/b2888193x/canvases/b2888193x_0002.jp2',
                width:    2495,
                height:   4067,
                textUri:  `${FIXTURE_ALTO}/b2888193x_0002.jp2.xml`,
                imageUri: 'https://iiif.wellcomecollection.org/image/b2888193x_0002.jp2/full/656,1024/0/default.jpg',
                profile:  'http://www.loc.gov/standards/alto/v3/alto.xsd',
            },
            {
                // Page 3: imageUri is a file:// path to the locally saved body.id JPEG
                // (downloaded from the Wellcome image service at 656×1024).
                // The Search API proxies it via /proxy/image so viewers can load it.
                id:       'https://iiif.wellcomecollection.org/presentation/b2888193x/canvases/b2888193x_0003.jp2',
                width:    2495,
                height:   4067,
                textUri:  `${FIXTURE_ALTO}/b2888193x_0003.jp2.xml`,
                imageUri: `${FIXTURE_IMAGES}/b2888193x_0003.jpg`,
                profile:  'http://www.loc.gov/standards/alto/v3/alto.xsd',
            },
        ],
    };
}

// ---- Toolbar actions ---------------------------------------------------------

newIdBtn.addEventListener('click', () => {
    try {
        const obj = JSON.parse(jsonEditor.value);
        obj.id = newJobId();
        jsonEditor.value = JSON.stringify(obj, null, 2);
        setValidation('');
    } catch {
        // If the JSON is currently broken, replace with a fresh template
        jsonEditor.value = JSON.stringify(buildTemplate(newJobId()), null, 2);
        setValidation('');
    }
    resultArea.innerHTML = '';
});

formatBtn.addEventListener('click', () => {
    try {
        const obj = JSON.parse(jsonEditor.value);
        jsonEditor.value = JSON.stringify(obj, null, 2);
        setValidation('✓ Valid JSON');
        showError(null);
    } catch (e) {
        showError(`JSON parse error: ${e.message}`);
    }
});

// ---- Submit ------------------------------------------------------------------

submitBtn.addEventListener('click', async () => {
    showError(null);
    resultArea.innerHTML = '';

    let body;
    try {
        body = JSON.parse(jsonEditor.value);
    } catch (e) {
        showError(`JSON parse error — fix before submitting: ${e.message}`);
        return;
    }

    if (!body.id?.trim()) {
        showError('Missing or empty "id" field.');
        return;
    }
    if (!body.sourceData && !body.sourceUri) {
        showError('Must include either "sourceData" (array) or "sourceUri" (string).');
        return;
    }

    submitBtn.disabled = true;
    submitBtn.textContent = 'Submitting…';

    try {
        const res = await fetch(`${config.builderApi}/textbuilder`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body),
        });
        const resBody = await res.json().catch(() => null);

        if (res.status === 202) {
            rememberJob(body.id, body.sourceUri ?? '');
            resultArea.innerHTML =
                `<div class="info-box">✓ Job <code>${esc(body.id)}</code> accepted. ` +
                `<a href="/builder.html">View in Jobs list</a></div>`;
        } else if (res.status === 409) {
            resultArea.innerHTML =
                `<div class="info-box">Job <code>${esc(body.id)}</code> already exists — ` +
                `use <em>New ID</em> to submit as a new job, or ` +
                `<a href="/builder.html">view it in the Jobs list</a>.</div>`;
        } else {
            const msg = resBody?.title ?? resBody?.errors ?? JSON.stringify(resBody);
            showError(`Error ${res.status}: ${msg}`);
        }
    } catch (err) {
        showError(`Network error: ${err.message}`);
    } finally {
        submitBtn.disabled = false;
        submitBtn.textContent = 'Submit Job';
    }
});

// ---- localStorage (shared with builder.js) -----------------------------------

function rememberJob(id, sourceUri) {
    try {
        const jobs = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '[]');
        if (!jobs.find(j => j.id === id)) {
            jobs.unshift({ id, sourceUri, submittedAt: new Date().toISOString() });
            localStorage.setItem(STORAGE_KEY, JSON.stringify(jobs));
        }
    } catch { /* ignore storage errors */ }
}

// ---- Quiet validation (on input) --------------------------------------------

function validateQuiet() {
    try {
        JSON.parse(jsonEditor.value);
        setValidation('✓ Valid JSON');
    } catch {
        setValidation('⚠ Invalid JSON');
    }
}

// ---- Helpers -----------------------------------------------------------------

function showError(msg) {
    errorArea.innerHTML = msg
        ? `<div class="error-box">${esc(msg)}</div>`
        : '';
}

function setValidation(msg) {
    validationMsg.textContent = msg;
}

function esc(s) {
    return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
