window.jobtracker = (function () {
    function isTypingTarget(el) {
        if (!el) return false;
        const tag = el.tagName;
        return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || el.isContentEditable;
    }

    function toast(message, kind) {
        let host = document.getElementById('jt-toasts');
        if (!host) {
            host = document.createElement('div');
            host.id = 'jt-toasts';
            document.body.appendChild(host);
        }
        const el = document.createElement('div');
        el.className = 'jt-toast' + (kind ? ' jt-toast-' + kind : '');
        el.textContent = message;
        host.appendChild(el);
        requestAnimationFrame(() => el.classList.add('show'));
        setTimeout(() => {
            el.classList.remove('show');
            el.addEventListener('transitionend', () => el.remove(), { once: true });
        }, 1500);
    }

    let _shortcutCleanup = null;

    function bindShortcuts(dotnetRef) {
        // Replace any previous binding so we don't accumulate stale listeners
        // each time the Home component re-mounts after a navigation.
        if (_shortcutCleanup) _shortcutCleanup();

        let alive = true;

        function safeInvoke(method) {
            if (!alive) return;
            return dotnetRef.invokeMethodAsync(method).catch(() => {
                // The component was disposed (most likely a navigation away).
                // Stop listening so we don't keep failing on every event.
                alive = false;
                cleanup();
            });
        }

        function onKeydown(e) {
            if (e.metaKey || e.ctrlKey || e.altKey) return;

            if (e.key === 'Escape') {
                safeInvoke('OnEscape');
                return;
            }

            if (isTypingTarget(e.target)) return;

            if (e.key === 'n' || e.key === 'N') {
                e.preventDefault();
                safeInvoke('FocusQuickAdd');
            } else if (e.key === '/') {
                const el = document.querySelector('.search-input');
                if (el) {
                    e.preventDefault();
                    el.focus();
                    el.select();
                }
            }
        }

        function onClick(e) {
            // Don't close transients when the click was on something that just
            // *opened* a transient state — otherwise CloseTransients races the
            // Blazor handler that set the state and the button reverts mid-flash.
            if (e.target.closest('.status-pop') ||
                e.target.closest('.delete-confirm') ||
                e.target.closest('.icon-btn-danger')) {
                return;
            }
            safeInvoke('CloseTransients');
        }

        function cleanup() {
            document.removeEventListener('keydown', onKeydown);
            document.removeEventListener('click', onClick);
            if (_shortcutCleanup === cleanup) _shortcutCleanup = null;
        }

        document.addEventListener('keydown', onKeydown);
        document.addEventListener('click', onClick);
        _shortcutCleanup = cleanup;
    }

    function findJobsUrl(provider, q) {
        q = (q || '').trim();
        var hasQ = q.length > 0;
        var enc = encodeURIComponent(q);
        switch (provider) {
            case 'linkedin':  return hasQ ? 'https://www.linkedin.com/jobs/search/?keywords=' + enc : 'https://www.linkedin.com/jobs/';
            case 'indeed':    return hasQ ? 'https://www.indeed.com/jobs?q=' + enc : 'https://www.indeed.com/';
            case 'wellfound': return hasQ ? 'https://wellfound.com/jobs?q=' + enc : 'https://wellfound.com/jobs';
            case 'glassdoor': return hasQ ? 'https://www.glassdoor.com/Job/jobs.htm?sc.keyword=' + enc : 'https://www.glassdoor.com/Job/index.htm';
            case 'google':    return hasQ ? 'https://www.google.com/search?q=' + enc + '+jobs&ibp=htl;jobs' : 'https://www.google.com/search?q=jobs&ibp=htl;jobs';
            default:          return '#';
        }
    }

    function openPastePanel() {
        var d = document.getElementById('jt-paste-details');
        if (d) d.open = true;
    }

    function aimSearchLink(link, provider, ev) {
        var input = document.getElementById('jt-findjobs-q');
        var q = input ? (input.value || '').trim() : '';
        if (!q) {
            if (ev && ev.preventDefault) ev.preventDefault();
            return false;
        }
        link.href = findJobsUrl(provider, q);
        return true;
    }

    return {
        bindShortcuts: bindShortcuts,
        toast: toast,
        findJobsUrl: findJobsUrl,
        aimSearchLink: aimSearchLink,
        openPastePanel: openPastePanel,
    };
})();
