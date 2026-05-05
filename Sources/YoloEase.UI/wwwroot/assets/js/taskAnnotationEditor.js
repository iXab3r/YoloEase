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
