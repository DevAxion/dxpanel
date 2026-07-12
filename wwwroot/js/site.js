document.addEventListener('submit', function (e) {
    const form = e.target;
    if (form.classList && form.classList.contains('srx-confirm-form')) {
        const message = form.dataset.confirmMessage || 'Are you sure?';
        if (!window.confirm(message)) {
            e.preventDefault();
        }
    }
});
