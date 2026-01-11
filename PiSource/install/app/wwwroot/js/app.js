let cameraTimer;
function startCameraSnapshot(path) {
    const img = document.getElementById('camera-feed');
    if (!img) return;
    const refresh = () => {
        img.src = path + '?ts=' + Date.now();
    };
    refresh();
    clearInterval(cameraTimer);
    cameraTimer = setInterval(refresh, 1000);
}

