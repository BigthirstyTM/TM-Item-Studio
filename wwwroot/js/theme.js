// Theme setup runs before the application paints; storage failure is harmless.
window.studioTheme = {
    toggle() {
        const dark = document.documentElement.dataset.theme === 'dark';
        const next = dark ? 'light' : 'dark';
        document.documentElement.dataset.theme = next;
        try { localStorage.setItem('studio-theme', next); } catch { }
    }
};
(() => {
    let saved;
    try { saved = localStorage.getItem('studio-theme'); } catch { }
    document.documentElement.dataset.theme = saved === 'light' || saved === 'dark'
        ? saved : (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
})();
