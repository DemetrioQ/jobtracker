window.jobtracker = {
    bindShortcuts: function (dotnetRef) {
        document.addEventListener('keydown', function (e) {
            if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA' || e.target.tagName === 'SELECT') return;
            if (e.metaKey || e.ctrlKey || e.altKey) return;
            if (e.key === 'n' || e.key === 'N') {
                e.preventDefault();
                dotnetRef.invokeMethodAsync('FocusQuickAdd');
            }
        });
    }
};
