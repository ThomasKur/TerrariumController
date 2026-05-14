function startCameraStream() {
    const img = document.getElementById('camera-feed');
    if (!img) return;

    const cameraPort = 8080;
    const protocol = window.location.protocol === 'https:' ? 'http:' : window.location.protocol;
    const streamUrl = `${protocol}//${window.location.hostname}:${cameraPort}/?action=stream`;

    // Reconnect the MJPEG stream if the backend camera service restarts.
    img.onerror = () => {
        setTimeout(() => {
            img.src = `${streamUrl}&ts=${Date.now()}`;
        }, 3000);
    };

    img.src = streamUrl;
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

