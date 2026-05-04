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

    function bindShortcuts(dotnetRef) {
        document.addEventListener('keydown', function (e) {
            if (e.metaKey || e.ctrlKey || e.altKey) return;

            if (e.key === 'Escape') {
                dotnetRef.invokeMethodAsync('OnEscape');
                return;
            }

            if (isTypingTarget(e.target)) return;

            if (e.key === 'n' || e.key === 'N') {
                e.preventDefault();
                dotnetRef.invokeMethodAsync('FocusQuickAdd');
            } else if (e.key === '/') {
                const el = document.querySelector('.search-input');
                if (el) {
                    e.preventDefault();
                    el.focus();
                    el.select();
                }
            }
        });

        document.addEventListener('click', function (e) {
            if (!e.target.closest('.status-pop') && !e.target.closest('.delete-confirm')) {
                dotnetRef.invokeMethodAsync('CloseTransients');
            }
        });
    }

    return {
        bindShortcuts: bindShortcuts,
        toast: toast,
    };
})();
