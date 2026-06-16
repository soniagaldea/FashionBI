/**
 * FashionBI – PDF export via browser print
 *
 * exportToPdf(moduleName) is called by every analytics module's Export button.
 * It:
 *  1. Shows a loading state on the button.
 *  2. Injects a print-only report header at the top of the page content.
 *  3. Unlocks inline-style scroll containers (max-height / overflow-y: auto)
 *     so the browser can see — and therefore print — their full content.
 *  4. Calls window.print(), which triggers @media print CSS that hides the
 *     sidebar, topbar, and all application chrome.
 *  5. Restores everything and shows a success/error toast when done.
 */
(function () {
    'use strict';

    // ── Toast ─────────────────────────────────────────────────────────────────
    function showToast(msg, type) {
        var el = document.getElementById('exportToast');
        if (!el) return;
        var body = document.getElementById('exportToastBody');
        el.className = 'toast align-items-center border-0 ' +
            (type === 'success' ? 'text-bg-success' : 'text-bg-danger');
        body.innerHTML = (type === 'success'
            ? '<i class="fa-solid fa-circle-check me-2"></i>'
            : '<i class="fa-solid fa-circle-xmark me-2"></i>') + msg;
        bootstrap.Toast.getOrCreateInstance(el, { delay: 3500 }).show();
    }

    // ── Button loading state ──────────────────────────────────────────────────
    function setLoading(btn, on) {
        if (!btn) return;
        if (on) {
            btn._exportHtml      = btn.innerHTML;
            btn.innerHTML        = '<i class="fa-solid fa-spinner fa-spin"></i> Preparing…';
            btn.disabled         = true;
            btn.style.opacity    = '0.65';
            btn.style.pointerEvents = 'none';
        } else {
            btn.innerHTML        = btn._exportHtml || '<i class="fa-solid fa-arrow-up-from-bracket"></i> Export';
            btn.disabled         = false;
            btn.style.opacity    = '';
            btn.style.pointerEvents = '';
        }
    }

    // ── Release inline scroll containers ──────────────────────────────────────
    // Table and list panels use inline max-height + overflow-y:auto so they
    // scroll on screen. In print we must remove those constraints so the browser
    // can measure and render the full content. chart-wrap elements are skipped
    // because they use an explicit height for the Chart.js canvas.
    function unlockScrollContainers() {
        var saved = [];
        document.querySelectorAll(
            '[style*="max-height"], [style*="overflow-y"], [style*="overflow:auto"], [style*="overflow: auto"]'
        ).forEach(function (el) {
            if (el.classList.contains('chart-wrap')) return; // keep chart heights

            var mh = el.style.maxHeight;
            var oy = el.style.overflowY;
            var ov = el.style.overflow;

            if (mh || oy === 'auto' || oy === 'scroll' || ov === 'auto' || ov === 'scroll') {
                saved.push({ el: el, maxHeight: mh, overflowY: oy, overflow: ov });
                el.style.maxHeight  = 'none';
                el.style.overflowY  = 'visible';
                if (ov === 'auto' || ov === 'scroll') el.style.overflow = 'visible';
            }
        });
        return saved;
    }

    function relockScrollContainers(saved) {
        saved.forEach(function (item) {
            item.el.style.maxHeight  = item.maxHeight;
            item.el.style.overflowY  = item.overflowY;
            item.el.style.overflow   = item.overflow;
        });
    }

    // ── Print header injection ─────────────────────────────────────────────────
    function injectPrintHeader(moduleName) {
        var header = document.createElement('div');
        header.id  = '_biPrintHeader';
        header.className = 'bi-print-header';
        header.innerHTML =
            '<span class="bi-ph-brand"><i class="fa-solid fa-chart-line" style="margin-right:6px;"></i>FashionBI</span>' +
            '<span class="bi-ph-title">' + moduleName + '</span>' +
            '<span class="bi-ph-meta">Exported ' +
                new Date().toLocaleDateString('en-GB', { day: '2-digit', month: 'long', year: 'numeric' }) +
            '</span>';
        var main = document.getElementById('mainContent');
        if (main) main.insertBefore(header, main.firstChild);
        return header;
    }

    // ── Main export entry point ───────────────────────────────────────────────
    window.exportToPdf = function (moduleName) {
        var btn       = document.querySelector('.export-btn');
        var date      = new Date().toISOString().slice(0, 10);
        var origTitle = document.title;
        var fileName  = 'FashionBI_' + moduleName.replace(/[\s/]+/g, '_') + '_' + date;

        // ① Show loading state
        setLoading(btn, true);
        document.title = fileName;

        // ② Inject print-only header
        var header = injectPrintHeader(moduleName);

        // ③ Release scroll-clipped containers
        var locked = unlockScrollContainers();

        // Allow repaint before the print dialog blocks the thread
        setTimeout(function () {
            var done = false;

            function cleanup() {
                done = true;
                window.removeEventListener('afterprint', onAfterPrint);
                relockScrollContainers(locked);
                if (header.parentNode) header.parentNode.removeChild(header);
                document.title = origTitle;
                setLoading(btn, false);
            }

            function onAfterPrint() {
                if (done) return;
                cleanup();
                showToast('Report saved as PDF.', 'success');
            }

            window.addEventListener('afterprint', onAfterPrint);

            // Safety net — restore state if afterprint never fires (e.g. popup blocked)
            setTimeout(function () { if (!done) cleanup(); }, 120000);

            try {
                window.print();
            } catch (e) {
                if (!done) {
                    cleanup();
                    showToast('Export failed. Please try again.', 'error');
                }
            }
        }, 200);
    };
})();
