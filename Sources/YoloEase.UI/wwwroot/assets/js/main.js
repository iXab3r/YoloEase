function initializeCvat(){
    Split(['#sideMenu', '#mainContent'], {
        sizes: [25, 75],
        minSize: 200,
        maxSize: 500,
        gutterSize: 15,
        expandToMin: true,
        cursor: 'row-resize',
    });
}

window.yoloEase = window.yoloEase || {};
window.yoloEase.scrollIntoView = function (elementId) {
    const element = document.getElementById(elementId);
    if (!element) {
        return;
    }

    element.scrollIntoView({ block: 'start', inline: 'nearest', behavior: 'auto' });
};
