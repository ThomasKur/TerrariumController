let cameraTimer;
function startCameraSnapshot(path) {
    const img = document.getElementById('camera-feed');
    if (!img) return;
    const refresh = () => {
        img.src = path + '?ts=' + Date.now();
    };
    refresh();
    clearInterval(cameraTimer);
    cameraTimer = setInterval(refresh, 5000);
}

function closeKioskWindow() {
    if (document.fullscreenElement && document.exitFullscreen) {
        document.exitFullscreen().catch(() => {});
    }

    try {
        window.open('', '_self');
        window.close();
    } catch {
    }

    setTimeout(() => {
        if (!window.closed) {
            window.location.replace('about:blank');
        }
    }, 150);
}

