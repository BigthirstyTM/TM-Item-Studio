window.enableItemDropZone = function (zoneId, inputId) {
    const zone = document.getElementById(zoneId);
    const input = document.getElementById(inputId);
    if (!zone || !input || zone.dataset.itemDropReady === 'true') return;

    const acceptFiles = (event) => {
        event.preventDefault();
        event.stopPropagation();
        const files = event.dataTransfer?.files;
        if (!files || files.length === 0) return;

        const transfer = new DataTransfer();
        for (const file of files) transfer.items.add(file);
        input.files = transfer.files;
        input.dispatchEvent(new Event('change', { bubbles: true }));
    };

    zone.addEventListener('dragover', (event) => {
        event.preventDefault();
        event.stopPropagation();
        if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
    });
    zone.addEventListener('drop', acceptFiles);
    zone.dataset.itemDropReady = 'true';
};
