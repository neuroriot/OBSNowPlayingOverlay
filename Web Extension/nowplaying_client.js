var conn = null;
var transfer_interval = null;
var join_interval = null;
var hostname = window.location.hostname;
const FETCH_URL = 'ws://localhost:52998/';
var join_retry_time = 2000
var isStopped = false;

function join() {
    conn = new WebSocket(FETCH_URL);

    conn.addEventListener('open', function (event) {
        console.log('Connection to Now Playing server established');
        conn.send("connected - " + hostname);
        start_transfer();
        if (join_interval) {
            clearTimeout(join_interval);
            join_interval = null;
        };
    });

    conn.addEventListener('close', function () {
        console.log("Connection to Now Playing server closed, retrying...");
        clearTimeout(join_interval);
        clearInterval(transfer_interval);
        join_interval = setTimeout(function () { join() }, join_retry_time);
    });
};


function query(target, fun, alt = null) {
    var element = document.querySelector(target);
    if (element !== null) {
        return fun(element);
    }
    return alt;
};

function timestamp_to_ms(ts) {
    var splits = ts.split(':');
    if (splits.length == 2) {
        return splits[0] * 60 * 1000 + splits[1] * 1000;
    } else if (splits.length == 3) {
        return splits[0] * 60 * 60 * 1000 + splits[1] * 60 * 1000 + splits[0] * 1000;
    }
    return 0;
};

function start_transfer() {
    transfer_interval = setInterval(() => {
        // TODO: maybe add more?
        if (hostname === 'soundcloud.com') {
            let status = query('.playControl', e => e.classList.contains('playing') ? "playing" : "stopped", 'unknown');
            let cover = query('.playbackSoundBadge span.sc-artwork', e => e.style.backgroundImage.slice(5, -2).replace('t50x50', 't500x500'));
            let title = query('.playbackSoundBadge__titleLink', e => e.title);
            let artists = [query('.playbackSoundBadge__lightLink', e => e.title)];
            let progress = query('.playbackTimeline__timePassed span:nth-child(2)', e => timestamp_to_ms(e.textContent));
            let duration = query('.playbackTimeline__duration span:nth-child(2)', e => timestamp_to_ms(e.textContent));
            let song_link = ''

            if (document.getElementsByClassName('playbackSoundBadge__avatar').length > 0) {
                song_link = document.getElementsByClassName('playbackSoundBadge__avatar')[0].href.split('?')[0];
            }

            if (title === null)
                return;

            if (status == "playing") {
                isStopped = false;
                conn.send(JSON.stringify({ cover, title, artists, status, progress, duration, song_link, platform: 'soundcloud', is_live: false }));
            }
            else if (status == 'stopped' && !isStopped) {
                isStopped = true;
                conn.send(JSON.stringify({ cover, title, artists, status, progress, duration, song_link, platform: 'soundcloud', is_live: false }));
            }
        } else if (hostname === 'open.spotify.com') {
            let data = navigator.mediaSession;
            let status = query('.XrZ1iHVHAPMya3jkB2sa > button', e => e === null ? 'stopped' : (e.getAttribute('aria-label') === 'Play' || e.getAttribute('aria-label') === 'Слушать'|| e.getAttribute('aria-label') === '播放' ? 'stopped' : 'playing'));
            let cover = ''
            let title = ''
            let artists = ''
            if (data.metadata != null) {
                cover = data.metadata.artwork[0].src;
                title = data.metadata.title
                artists = [data.metadata.artist]
            }

            let progress = query('.IPbBrI6yF4zhaizFmrg6', e => timestamp_to_ms(e.textContent));
            let duration = query('.DSdahCi0SDG37V9ZmsGO', e => timestamp_to_ms(e.textContent));
            let song_link = ''
            if (document.querySelectorAll('a[aria-label][data-context-item-type="track"]').length > 0) {
                song_link = 'https://open.spotify.com/track/' + decodeURIComponent(document.querySelectorAll('a[aria-label][data-context-item-type="track"]')[0].href).split(':').slice(-1)[0];
            }

            if (title === '')
                return;

            if (status == "playing") {
                isStopped = false;
                conn.send(JSON.stringify({ cover, title, artists, status, progress, duration, song_link, platform: 'spotify', is_live: false }));
            }
            else if (status == 'stopped' && !isStopped) {
                isStopped = true;
                conn.send(JSON.stringify({ cover, title, artists, status, progress, duration, song_link, platform: 'spotify', is_live: false }));
            }
        } else if (hostname === 'www.youtube.com') {
            if (!navigator.mediaSession.metadata) // if nothing is playing we don't submit anything, otherwise having two youtube tabs open causes issues
                return;
            if (window.location.href == 'https://www.youtube.com/') // 在主頁面
                return;

            let artists = [];

            try {
                artists = [navigator.mediaSession.metadata.artist]; // 改用 mediaSession 來獲取作者資訊
            } catch (e) { }

            let title = query('.style-scope.ytd-video-primary-info-renderer', e => {
                let t = e.getElementsByClassName('title');
                if (t && t.length > 0)
                    return t[0].innerText;
                return "";
            });

            let duration = query('video', e => e.duration * 1000);
            let progress = query('video', e => e.currentTime * 1000);
            let cover = navigator.mediaSession.metadata.artwork[0].src;
            let status = navigator.mediaSession.playbackState; // playbackState = playing, paused, none
            let song_link = window.location.href.split('&')[0];
            let is_live = false;

            // 檢測觀看的影片是否正在直播中
            if (document.querySelector('#movie_player > div.ytp-chrome-bottom > div.ytp-chrome-controls > div.ytp-left-controls > div.ytp-time-display.notranslate.ytp-live > button')) {
                is_live = true;
            }

            if (title === null)
                return;

            title = title.replace("(Official Audio)", "");
            title = title.replace("(Official Music Video)", "");
            title = title.replace("(Original Video)", "");
            title = title.replace("(Original Mix)", "");
            
            if (status == 'playing' && progress > 0) {
                isStopped = false;
                conn.send(JSON.stringify({ cover, title, artists, status, progress: Math.floor(progress), duration, song_link, platform: 'youtube', is_live }));
            }
            else if (status == 'paused' && !isStopped) {
                isStopped = true;
                conn.send(JSON.stringify({ cover, title, artists, status, progress: Math.floor(progress), duration, song_link, platform: 'youtube', is_live }));
            }
        } else if (hostname === 'music.youtube.com') {
            if (!navigator.mediaSession.metadata) // if nothing is playing we don't submit anything, otherwise having two youtube tabs open causes issues
                return;

            let time = query('.ytmusic-player-bar.time-info', e => e.innerText.split(" / "));

            let status = query('#play-pause-button', e => e === null ? 'stopped' : (e.getAttribute('aria-label') === 'Play' || e.getAttribute('aria-label') === 'Воспроизвести' || e.getAttribute('aria-label') === '播放' ? 'stopped' : 'playing'));

            let title = document.getElementsByClassName("title style-scope ytmusic-player-bar")[0].innerHTML;
            let artists = [navigator.mediaSession.metadata.artist];
            let artwork = navigator.mediaSession.metadata.artwork;
            let cover = artwork[artwork.length - 1].src;
            let progress = timestamp_to_ms(time[0]);
            let duration = timestamp_to_ms(time[1]);
            let lnk = navigator.mediaSession.metadata.artwork[0].src;
            let song_link = 'https://www.youtube.com/watch?v=' + lnk.substring(
                lnk.indexOf("vi/") + 3,
                lnk.lastIndexOf("/sddefault")
            );

            if (title === null)
                return;

            if (status == 'playing') {
                isStopped = false;
                conn.send(JSON.stringify({ cover, title, artists, status, progress, duration, song_link, platform: 'youtube_music', is_live: false }));
            }
            else if (status == 'stopped' && !isStopped) {
                isStopped = true;
                conn.send(JSON.stringify({ cover, title, artists, status, progress, duration, song_link, platform: 'youtube_music', is_live: false }));
            }
        }
    }, 500);
}

if (hostname === 'soundcloud.com' || hostname === 'music.youtube.com' || hostname === 'www.youtube.com' || hostname === 'open.spotify.com') {
    join();
};

window.addEventListener('beforeunload', function (e) {
    if (conn.readyState == WebSocket.OPEN) {
        conn.send("closed - " + hostname);
    }
});
