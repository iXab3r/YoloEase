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