import { loadConfig } from './iiif-helpers.js';

// ---- State -------------------------------------------------------------------
let config = null;
let pollTimer = null;

const STORAGE_KEY = 'textservices_demo_jobs';  // localStorage key

// ---- DOM refs ----------------------------------------------------------------
const errorArea    = document.getElementById('error-area');
const configInfo   = document.getElementById('config-info');
const apiError     = document.getElementById('api-error');
const submitForm   = document.getElementById('submit-form');
const jobIdInput   = document.getElementById('job-id');
const manifestInput= document.getElementById('manifest-url');
const submitResult = document.getElementById('submit-result');
const refreshBtn   = document.getElementById('refresh-btn');
const hangfireLink = document.getElementById('hangfire-link');
const jobsTable    = document.getElementById('jobs-table');
const jobsBody     = document.getElementById('jobs-body');
const noJobsMsg    = document.getElementById('no-jobs-msg');

// ---- Init --------------------------------------------------------------------

(async function init() {
    config = await loadConfig();
    hangfireLink.href = config.hangfireUrl;

    configInfo.innerHTML =
        `<span class="label">Builder API:</span>${esc(config.builderApi)}&nbsp;&nbsp;` +
        `<span class="label">Search API:</span>${esc(config.searchApi)}`;
    configInfo.style.display = 'block';

    await refreshJobs();
    startPolling();
})();

// ---- Submit ------------------------------------------------------------------

submitForm.addEventListener('submit', async e => {
    e.preventDefault();
    const id  = jobIdInput.value.trim();
    const url = manifestInput.value.trim();
    if (!id || !url) return;

    submitResult.textContent = 'Submitting…';

    try {
        const res = await fetch(`${config.builderApi}/textbuilder`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ id, sourceUri: url }),
        });
        const body = await res.json();

        if (res.status === 202) {
            submitResult.textContent = `✓ Accepted — job "${id}" enqueued.`;
            rememberJob(id, url);
            await refreshJobs();
        } else if (res.status === 409) {
            submitResult.textContent = `Job "${id}" already exists.`;
            rememberJob(id, url); // ensure it's in our list
            await refreshJobs();
        } else {
            const msg = body?.title ?? body?.errors ?? JSON.stringify(body);
            submitResult.textContent = `Error ${res.status}: ${msg}`;
        }
    } catch (err) {
        submitResult.textContent = `Network error: ${err.message}`;
    }
});

// ---- localStorage job tracking -----------------------------------------------

function storedJobs() {
    try { return JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '[]'); }
    catch { return []; }
}

function rememberJob(id, sourceUri) {
    const jobs = storedJobs();
    if (!jobs.find(j => j.id === id)) {
        jobs.unshift({ id, sourceUri, submittedAt: new Date().toISOString() });
        localStorage.setItem(STORAGE_KEY, JSON.stringify(jobs));
    }
}

// ---- Refresh -----------------------------------------------------------------

refreshBtn.addEventListener('click', refreshJobs);

async function refreshJobs() {
    const stored = storedJobs();

    // Pull the full job list from the API — this gives us status without extra requests.
    let apiJobs = [];
    let apiReachable = true;
    try {
        const res = await fetch(`${config.builderApi}/textbuilder?pageSize=100`);
        if (res.ok) {
            const data = await res.json();
            apiJobs = data.items ?? [];
            // Merge any new IDs into localStorage
            for (const j of apiJobs) {
                if (!stored.find(s => s.id === j.id)) {
                    stored.push({ id: j.id, sourceUri: j.sourceUri ?? '', submittedAt: j.created });
                }
            }
            localStorage.setItem(STORAGE_KEY, JSON.stringify(stored));
        } else {
            apiReachable = false;
            console.warn(`Builder API returned ${res.status} for GET /textbuilder`);
        }
    } catch (err) {
        apiReachable = false;
        console.warn(`Builder API not reachable at ${config.builderApi}: ${err.message}`);
    }

    if (apiError) {
        if (apiReachable) {
            apiError.style.display = 'none';
        } else {
            apiError.textContent = `⚠ Cannot reach Builder API at ${config.builderApi} — showing locally tracked jobs only.`;
            apiError.style.display = 'block';
        }
    }

    // Use the list response as the primary source of job data.
    const apiJobMap = new Map(apiJobs.map(j => [j.id, j]));

    // For jobs only in localStorage (not returned by the list), fetch individually.
    const localOnlyIds = stored.map(j => j.id).filter(id => !apiJobMap.has(id));
    const localStatuses = await Promise.allSettled(
        localOnlyIds.map(id =>
            fetch(`${config.builderApi}/textbuilder/${encodeJobId(id)}`)
                .then(r => r.ok ? r.json() : null)
        )
    );
    const localJobMap = new Map(
        localOnlyIds.map((id, i) => {
            const r = localStatuses[i];
            return [id, r.status === 'fulfilled' ? r.value : null];
        })
    );

    // API jobs are already newest-first; local-only jobs follow in localStorage order.
    const allIds = [...apiJobs.map(j => j.id), ...localOnlyIds];
    const rows = allIds.map(id => {
        const job = apiJobMap.get(id) ?? localJobMap.get(id) ?? null;
        const stored_ = stored.find(s => s.id === id);
        return { id, sourceUri: job?.sourceUri ?? stored_?.sourceUri ?? '', job };
    });

    renderTable(rows);
}

function encodeJobId(id) {
    // Job IDs may contain slashes — pass them through as path segments
    return id.split('/').map(encodeURIComponent).join('/');
}

// ---- Table rendering ---------------------------------------------------------

function renderTable(rows) {
    if (rows.length === 0) {
        jobsTable.style.display = 'none';
        noJobsMsg.style.display = 'block';
        return;
    }
    jobsTable.style.display = 'table';
    noJobsMsg.style.display = 'none';

    jobsBody.innerHTML = '';
    rows.forEach(({ id, sourceUri, job }) => {
        const status = job?.status ?? 'Unknown';
        const tr = document.createElement('tr');

        const viewerUrl = job?.status === 'Completed'
            ? `/viewer.html?iiif-content=${encodeURIComponent(`${config.searchApi}/text-augmented/v3/${id}`)}`
            : null;

        const compareUrl = job?.status === 'Completed' && sourceUri
            ? `/compare.html?iiif-content=${encodeURIComponent(`${config.searchApi}/text-augmented/v3/${id}`)}`
            : null;

        const figuresUrl = job?.status === 'Completed'
            ? `/figures.html?iiif-content=${encodeURIComponent(`${config.searchApi}/text-augmented/v3/${id}`)}`
            : null;

        const annotationsUrl = job?.status === 'Completed'
            ? `/annotations.html?iiif-content=${encodeURIComponent(`${config.searchApi}/text-augmented/v3/${id}`)}`
            : null;

        tr.innerHTML = `
            <td><code style="font-size:0.8rem">${esc(id)}</code></td>
            <td style="max-width:200px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap" title="${esc(sourceUri)}">${esc(truncate(sourceUri, 50))}</td>
            <td><span class="status-badge status-${esc(status)}">${esc(status)}</span></td>
            <td>${job?.invocationCount ?? '—'}</td>
            <td>${renderProgress(job)}</td>
            <td>${job?.totalWordCount != null ? job.totalWordCount.toLocaleString() : '—'}</td>
            <td class="actions-cell"></td>
        `;

        const actionsCell = tr.querySelector('.actions-cell');
        if (viewerUrl) {
            const a = document.createElement('a');
            a.href = viewerUrl;
            a.textContent = 'View';
            a.style.cssText = 'margin-right:0.5rem;font-size:0.82rem';
            actionsCell.appendChild(a);
        }
        if (compareUrl && sourceUri) {
            // Offer compare link using augmented manifest (has our service + original services)
            const a = document.createElement('a');
            a.href = compareUrl;
            a.textContent = 'Compare';
            a.style.cssText = 'margin-right:0.5rem;font-size:0.82rem';
            actionsCell.appendChild(a);
        }
        if (figuresUrl) {
            const a = document.createElement('a');
            a.href = figuresUrl;
            a.textContent = 'Figures';
            a.style.cssText = 'margin-right:0.5rem;font-size:0.82rem';
            actionsCell.appendChild(a);
        }
        if (annotationsUrl) {
            const a = document.createElement('a');
            a.href = annotationsUrl;
            a.textContent = 'Annotations';
            a.style.cssText = 'font-size:0.82rem';
            actionsCell.appendChild(a);
        }

        if (job && (job.status === 'Completed' || job.status === 'Failed')) {
            const btn = document.createElement('button');
            btn.textContent = 'Reprocess';
            btn.className = 'secondary';
            btn.style.cssText = 'font-size:0.82rem;padding:0.2rem 0.5rem;margin-left:0.5rem';
            btn.addEventListener('click', () => reprocessJob(id, btn));
            actionsCell.appendChild(btn);
        }

        jobsBody.appendChild(tr);
    });
}

function renderProgress(job) {
    if (!job) return '—';
    if (job.status === 'Completed' || job.status === 'Failed') {
        return `${job.pagesCompleted}/${job.totalPages}`;
    }
    if (job.status === 'Running' && job.totalPages > 0) {
        return `<progress value="${job.pagesCompleted}" max="${job.totalPages}"></progress> ${job.pagesCompleted}/${job.totalPages}`;
    }
    return job.status === 'Waiting' || job.status === 'Running' ? 'queued' : '—';
}

// ---- Polling -----------------------------------------------------------------

function startPolling() {
    if (pollTimer) clearInterval(pollTimer);
    pollTimer = setInterval(async () => {
        const stored = storedJobs();
        const hasActive = stored.some(j => {
            // We don't know status from localStorage alone, so always poll if there are jobs
            return true;
        });
        if (hasActive) await refreshJobs();
    }, 4000);
}

// ---- Reprocess ---------------------------------------------------------------

async function reprocessJob(id, btn) {
    const original = btn.textContent;
    btn.disabled = true;
    btn.textContent = 'Reprocessing…';

    try {
        const res = await fetch(`${config.builderApi}/textbuilder/${encodeJobId(id)}`, {
            method: 'PUT',
        });

        if (res.status === 202) {
            await refreshJobs();
            return;
        }

        if (res.status === 409) {
            btn.textContent = 'Still running';
        } else {
            btn.textContent = `Error ${res.status}`;
        }
    } catch (err) {
        btn.textContent = 'Network error';
    }

    // Reset button after a short pause so the user can see the feedback.
    setTimeout(() => { btn.disabled = false; btn.textContent = original; }, 2500);
}

// ---- Helpers -----------------------------------------------------------------

function esc(s) {
    return String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
}
function truncate(s, n) {
    return s.length > n ? s.slice(0, n) + '…' : s;
}
