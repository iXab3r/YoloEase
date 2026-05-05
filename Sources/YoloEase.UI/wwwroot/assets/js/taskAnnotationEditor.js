export function observeSize(element, dotNetRef) {
    if (!element) {
        throw new Error('Task annotation editor viewport was not provided.');
    }

    let disposed = false;
    let animationFrame = 0;

    const notify = () => {
        if (disposed) {
            return;
        }

        if (animationFrame) {
            cancelAnimationFrame(animationFrame);
        }

        animationFrame = requestAnimationFrame(() => {
            if (disposed) {
                return;
            }

            const rect = element.getBoundingClientRect();
            dotNetRef.invokeMethodAsync(
                'OnTaskEditorViewportResized',
                Math.max(1, rect.width),
                Math.max(1, rect.height));
        });
    };

    const observer = new ResizeObserver(notify);
    observer.observe(element);
    notify();

    return {
        dispose() {
            disposed = true;
            if (animationFrame) {
                cancelAnimationFrame(animationFrame);
                animationFrame = 0;
            }
            observer.disconnect();
        }
    };
}

export function focusAndSelect(element) {
    if (!element) {
        return;
    }

    element.focus({ preventScroll: true });
    if (typeof element.select === 'function') {
        element.select();
    }
}

export function focusElement(element) {
    if (!element) {
        return;
    }

    requestAnimationFrame(() => {
        element.focus({ preventScroll: true });
    });
}

const preloadedImages = new Map();
const maxPreloadedImages = 64;

export function preloadImages(urls) {
    if (!Array.isArray(urls)) {
        return;
    }

    for (const url of urls) {
        if (!url || typeof url !== 'string') {
            continue;
        }

        if (preloadedImages.has(url)) {
            const existing = preloadedImages.get(url);
            preloadedImages.delete(url);
            preloadedImages.set(url, existing);
            continue;
        }

        const image = new Image();
        image.decoding = 'async';
        image.loading = 'eager';
        image.src = url;

        if (typeof image.decode === 'function') {
            image.decode().catch(() => {});
        }

        preloadedImages.set(url, image);
    }

    while (preloadedImages.size > maxPreloadedImages) {
        const oldestUrl = preloadedImages.keys().next().value;
        preloadedImages.delete(oldestUrl);
    }
}
