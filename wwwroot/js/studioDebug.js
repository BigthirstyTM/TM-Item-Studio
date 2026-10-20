(() => {
    let bridge = null;
    const informationalCodes = new Set([
        'dependencies-not-loaded',
        'external-reference',
        'legacy-light',
        'light-socket-transform',
        'no-mesh-geometry',
        'stream-layout-opaque',
        'textures-not-loaded',
        'unmapped-visual'
    ]);
    window.registerStudioDebug = reference => { bridge = reference; };
    window.unregisterStudioDebug = () => { bridge = null; };
    window.studioDebug = Object.freeze({
        async getReport() {
            if (!bridge) throw new Error('Item Studio is not ready. Wait for the upload controls.');
            const report = JSON.parse(await bridge.invokeMethodAsync('GetDebugReportJson'));
            report.viewer.runtime = window.getStudioViewerDebugInfo();
            return report;
        },
        async downloadReport() {
            const report = await this.getReport();
            const url = URL.createObjectURL(new Blob([JSON.stringify(report, null, 2) + '\n'], { type: 'application/json' }));
            const link = document.createElement('a');
            link.href = url; link.download = 'item-studio-debug.json';
            document.body.appendChild(link);
            try { link.click(); }
            finally { link.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000); }
        },
        logDiagnostics(codes) {
            // Known viewer coverage limits remain available in the Diagnostics panel and report,
            // but should not look like browser/runtime failures in the console. Keep unexpected
            // inspection problems as warnings so they are still visible during development.
            const unique = [...new Set(codes)].filter(Boolean);
            const informational = unique.filter(code => informationalCodes.has(code));
            const actionable = unique.filter(code => !informationalCodes.has(code));
            if (informational.length)
                console.info(`[TMIS] ${informational.join(', ')}; use studioDebug.getReport() for details.`);
            for (const code of actionable)
                console.warn(`[TMIS] ${code}; use studioDebug.getReport() for details.`);
        }
    });
})();
