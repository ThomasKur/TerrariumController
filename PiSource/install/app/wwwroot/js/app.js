function startCameraStream() {
    const img = document.getElementById('camera-feed');
    if (!img) return;

    const cameraPort = 5001;
    const protocol = window.location.protocol === 'https:' ? 'http:' : window.location.protocol;
    // ffmpeg MJPEG server serves stream at root path
    const streamUrl = `${protocol}//${window.location.hostname}:${cameraPort}/`;

    // Reconnect the MJPEG stream if the backend camera service restarts.
    img.onerror = () => {
        setTimeout(() => {
            img.src = `${streamUrl}?ts=${Date.now()}`;
        }, 3000);
    };

    img.src = streamUrl;
}

function closeKioskWindow() {
    requestKioskExit();

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

function requestKioskExit() {
    const exitUrl = '/api/kiosk/exit';

    if (navigator.sendBeacon) {
        const sent = navigator.sendBeacon(exitUrl, new Blob([], { type: 'text/plain' }));
        if (sent) {
            return;
        }
    }

    fetch(exitUrl, {
        method: 'POST',
        cache: 'no-store',
        keepalive: true
    }).catch(() => {});
}

