// Raw GBX.NET icons are top-down [x,y]. GBX WebP bytes store bottom-up rows.
// IconQuarterRotationY controls auto-rendering of the model, not stored image pixels.
const studioIconRequests = new WeakMap();
function beginIconPreview(canvasId) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return null;
    const request = (studioIconRequests.get(canvas) || 0) + 1;
    studioIconRequests.set(canvas, request);
    canvas.getContext('2d').clearRect(0, 0, canvas.width, canvas.height);
    return { canvas, request };
}
function iconPixelStatus(canvas) {
    const pixels = canvas.getContext('2d', { willReadFrequently: true }).getImageData(0, 0, canvas.width, canvas.height).data;
    let opaque = false, color = false;
    for (let i = 0; i < pixels.length; i += 4) {
        if (!pixels[i + 3]) continue;
        opaque = true;
        color ||= pixels[i] !== 0 || pixels[i + 1] !== 0 || pixels[i + 2] !== 0;
    }
    return !opaque ? 'transparent' : !color ? 'black' : 'ready';
}
window.clearIconPreview = canvasId => beginIconPreview(canvasId) ? 'missing' : 'pending';
window.normalizeIcon = async function (bytes, size) {
    const bitmap = await createImageBitmap(new Blob([bytes]));
    try {
        const canvas = document.createElement('canvas');
        canvas.width = canvas.height = size;
        const context = canvas.getContext('2d', { willReadFrequently: true });
        const scale = Math.max(size / bitmap.width, size / bitmap.height);
        context.drawImage(bitmap, (size - bitmap.width * scale) / 2, (size - bitmap.height * scale) / 2, bitmap.width * scale, bitmap.height * scale);
        const pixels = Array.from(context.getImageData(0, 0, size, size).data);
        const stored = document.createElement('canvas'); stored.width = stored.height = size;
        const storedContext = stored.getContext('2d');
        storedContext.translate(0, size); storedContext.scale(1, -1); storedContext.drawImage(canvas, 0, 0);
        return { pixels, webPBase64: stored.toDataURL('image/webp', .9).split(',')[1] };
    } finally { bitmap.close(); }
};
window.renderIconPreview = function (canvasId, pixels, width, height) {
    const state = beginIconPreview(canvasId);
    if (!state) return 'pending';
    if (width < 1 || height < 1 || pixels?.length !== width * height * 4) return 'decode-failed';
    const { canvas } = state;
    canvas.width = width; canvas.height = height;
    const context = canvas.getContext('2d'), image = context.createImageData(width, height);
    image.data.set(pixels); context.putImageData(image, 0, 0);
    return iconPixelStatus(canvas);
};
window.renderIconPreviewFromBytes = async function (canvasId, bytes) {
    const state = beginIconPreview(canvasId);
    if (!state) return 'pending';
    if (!bytes?.length) return 'missing';
    try {
        const bitmap = await createImageBitmap(new Blob([bytes]));
        try {
            const { canvas, request } = state;
            if (!canvas.isConnected || studioIconRequests.get(canvas) !== request) return 'superseded';
            if (bitmap.width > 1024 || bitmap.height > 1024) return 'decode-failed';
            canvas.width = bitmap.width; canvas.height = bitmap.height;
            const context = canvas.getContext('2d');
            context.translate(0, canvas.height); context.scale(1, -1); context.drawImage(bitmap, 0, 0);
            context.setTransform(1, 0, 0, 1, 0, 0);
            return iconPixelStatus(canvas);
        } finally { bitmap.close(); }
    } catch { return 'decode-failed'; }
};
