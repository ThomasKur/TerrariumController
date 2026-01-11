function setCameraSource(url) {
    const video = document.getElementById('camera-feed');
    if (video) {
        video.src = url;
    }
}

