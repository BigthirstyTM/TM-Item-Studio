(() => {
    let bridge = null;
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
            // No source names/paths in automatic logs; details require an explicit report.
            for (const code of codes) console.warn(`[TMIS] ${code}; use studioDebug.getReport() for details.`);
        }
    });
})();
