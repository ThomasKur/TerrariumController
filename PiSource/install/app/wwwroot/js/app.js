function setCameraUrl(url) {
    const cameraFeed = document.getElementById('camera-feed');
    if (cameraFeed) {
        cameraFeed.src = url;
    }
}
