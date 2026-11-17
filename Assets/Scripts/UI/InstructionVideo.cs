using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;

namespace SonarTask.UI
{
    /// <summary>
    /// Inline instruction video. Plays a package asset by URL (the only VideoPlayer source
    /// WebGL supports), so desktop file://, Web /api and local-web-dev relative URLs all work.
    /// Controls are drawn as geometry (no Unicode glyphs) and the play button always carries a
    /// text label so state is not conveyed by the icon alone.
    /// Raises Video_Played / Video_Paused / Video_Seeked / Video_Ended with the playback position.
    /// </summary>
    public sealed class InstructionVideo : MonoBehaviour
    {
        public const float ControlBarHeight = 40f;

        public event Action<string, string, double> VideoEvent;

        static Sprite playSprite;

        string path;
        MediaAttrs attrs;
        MediaFit fit;
        VideoPlayer player;
        RenderTexture target;
        RawImage frame;
        RectTransform surfaceRect, controlsRect;
        AspectRatioFitter frameAspect;
        Text status, buttonLabel, timeLabel;
        GameObject playIcon, pauseIcon;
        SeekSlider seek;
        bool prepared, wantPlay, wasPlaying, failed;

        public void Init(string url, string assetPath, string altText, MediaAttrs a, MediaFit f)
        {
            path = assetPath;
            attrs = a;
            fit = f;
            BuildUi(altText);

            player = gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.waitForFirstFrame = true;
            player.skipOnDrop = true;
            player.isLooping = a.Loop;
            player.source = VideoSource.Url;
            player.url = url;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.audioOutputMode = a.Muted ? VideoAudioOutputMode.None : VideoAudioOutputMode.Direct;
            if (!a.Muted) { player.controlledAudioTrackCount = 1; player.EnableAudioTrack(0, true); }
            player.prepareCompleted += OnPrepared;
            player.errorReceived += OnError;
            player.loopPointReached += _ => Raise("Video_Ended", player.length > 0 ? player.length : player.time);

            wantPlay = a.Autoplay;
            player.Prepare();
        }

        void BuildUi(string altText)
        {
            var surface = UIFactory.Panel("Surface", transform, Color.black);
            var sr = surfaceRect = surface.rectTransform;
            sr.anchorMin = Vector2.zero; sr.anchorMax = Vector2.one;
            sr.offsetMin = new Vector2(0, attrs.Controls ? ControlBarHeight : 0); sr.offsetMax = Vector2.zero;
            var surfaceButton = surface.gameObject.AddComponent<Button>();
            surfaceButton.transition = Selectable.Transition.None;
            surfaceButton.navigation = new Navigation { mode = Navigation.Mode.None };
            surfaceButton.onClick.AddListener(TogglePlay);

            var frameGo = UIFactory.GO("Frame", surface.transform);
            frame = frameGo.AddComponent<RawImage>();
            frame.color = Color.clear;
            frame.raycastTarget = false;
            frameAspect = frameGo.AddComponent<AspectRatioFitter>();
            frameAspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            frameAspect.aspectRatio = fit.Aspect;

            status = UIFactory.Text("Loading video: " + altText, surface.transform, 15, TextAnchor.MiddleCenter);
            status.color = new Color(.75f, .78f, .8f, 1f);
            status.raycastTarget = false;

            if (!attrs.Controls)
                return;

            var bar = UIFactory.Panel("Controls", transform, new Color(.10f, .12f, .14f, 1f));
            var br = controlsRect = bar.rectTransform;
            br.anchorMin = Vector2.zero; br.anchorMax = new Vector2(1, 0); br.pivot = new Vector2(.5f, 0);
            br.sizeDelta = new Vector2(0, ControlBarHeight); br.anchoredPosition = Vector2.zero;
            var h = UIFactory.HLayout(bar.transform, 10, 5);
            h.childAlignment = TextAnchor.MiddleLeft;
            h.padding = new RectOffset(6, 10, 5, 5);

            var playBg = UIFactory.Panel("PlayPause", bar.transform, new Color(.20f, .25f, .30f, 1f));
            var playButton = playBg.gameObject.AddComponent<Button>();
            playButton.targetGraphic = playBg;
            playButton.navigation = new Navigation { mode = Navigation.Mode.None };
            playButton.onClick.AddListener(TogglePlay);
            UIFactory.Size(playBg, 30, 96, 0, 0);

            var icon = UIFactory.GO("Icon", playBg.transform);
            var ir = (RectTransform)icon.transform;
            ir.anchorMin = ir.anchorMax = new Vector2(0, .5f); ir.pivot = new Vector2(.5f, .5f);
            ir.sizeDelta = new Vector2(16, 16); ir.anchoredPosition = new Vector2(18, 0);
            var tri = icon.AddComponent<Image>();
            tri.sprite = PlaySprite(); tri.color = Color.white; tri.raycastTarget = false;
            playIcon = icon;
            var pause = UIFactory.GO("PauseIcon", playBg.transform);
            var pr = (RectTransform)pause.transform;
            pr.anchorMin = pr.anchorMax = new Vector2(0, .5f); pr.pivot = new Vector2(.5f, .5f);
            pr.sizeDelta = new Vector2(16, 16); pr.anchoredPosition = new Vector2(18, 0);
            UIFactory.LineGraphic("BarL", pause.transform, Color.white, new Vector2(4.5f, 14), new Vector2(-4, 0), 0);
            UIFactory.LineGraphic("BarR", pause.transform, Color.white, new Vector2(4.5f, 14), new Vector2(4, 0), 0);
            pauseIcon = pause;

            buttonLabel = UIFactory.Text("Play", playBg.transform, 15, TextAnchor.MiddleLeft);
            buttonLabel.rectTransform.offsetMin = new Vector2(32, 0);
            buttonLabel.raycastTarget = false;

            seek = BuildSeekSlider(bar.transform);
            seek.Released = OnSeekReleased;

            timeLabel = UIFactory.Text("0:00 / 0:00", bar.transform, 14, TextAnchor.MiddleRight);
            UIFactory.Size(timeLabel, 30, 104, 0, 0);
            SetPlayingVisual(false);
        }

        static SeekSlider BuildSeekSlider(Transform parent)
        {
            var root = UIFactory.GO("Seek", parent);
            UIFactory.Size(root.transform as RectTransform, 30, 80, 0, 1);
            var hit = root.AddComponent<Image>();
            hit.color = Color.clear; // full-height hit area
            var track = UIFactory.Panel("Track", root.transform, new Color(.28f, .32f, .36f, 1f));
            track.raycastTarget = false;
            var tr = track.rectTransform;
            tr.anchorMin = new Vector2(0, .5f); tr.anchorMax = new Vector2(1, .5f);
            tr.sizeDelta = new Vector2(-8, 6); tr.anchoredPosition = Vector2.zero;

            var fillArea = UIFactory.Rect("FillArea", root.transform, new Vector2(0, .5f), new Vector2(1, .5f), new Vector2(4, -3), new Vector2(-4, 3));
            var fill = UIFactory.Panel("Fill", fillArea, new Color(.45f, .72f, .95f, 1f));
            fill.raycastTarget = false;
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(0, 1);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;

            var handleArea = UIFactory.Rect("HandleArea", root.transform, Vector2.zero, Vector2.one, new Vector2(6, 0), new Vector2(-6, 0));
            var handle = UIFactory.Panel("Handle", handleArea, new Color(.9f, .93f, .95f, 1f));
            var hr = handle.rectTransform;
            hr.anchorMin = new Vector2(0, 0); hr.anchorMax = new Vector2(0, 1);
            hr.sizeDelta = new Vector2(10, -10); hr.anchoredPosition = Vector2.zero;

            var s = root.AddComponent<SeekSlider>();
            s.targetGraphic = handle;
            s.fillRect = fill.rectTransform;
            s.handleRect = hr;
            s.direction = Slider.Direction.LeftToRight;
            s.minValue = 0; s.maxValue = 1; s.wholeNumbers = false;
            s.navigation = new Navigation { mode = Navigation.Mode.None };
            s.SetValueWithoutNotify(0);
            return s;
        }

        internal static Sprite PlaySprite()
        {
            if (playSprite) return playSprite;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "RuntimePlayTriangle", wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave
            };
            var px = new Color32[size * size];
            float left = size * .14f, right = size * .9f, mid = (size - 1) * .5f, half = size * .42f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Right-pointing triangle: half-height shrinks linearly from left edge to tip.
                    float t = (x - left) / (right - left);
                    bool inside = t >= 0 && t <= 1 && Mathf.Abs(y - mid) <= half * (1 - t);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(inside ? 255 : 0));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            playSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size);
            playSprite.name = "RuntimePlayTriangleSprite";
            playSprite.hideFlags = HideFlags.HideAndDontSave;
            return playSprite;
        }

        void OnPrepared(VideoPlayer vp)
        {
            if (failed) return;
            prepared = true;
            int w = (int)vp.width, h = (int)vp.height;
            if (w <= 0 || h <= 0) { w = 1280; h = 720; }
            target = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "InstructionVideo:" + path };
            target.Create();
            vp.targetTexture = target;
            frame.texture = target;
            frame.color = Color.white;
            float aspect = w / (float)h;
            frameAspect.aspectRatio = aspect;
            fit.SetAspect(aspect);
            status.gameObject.SetActive(false);
            if (wantPlay) Play();
            UpdateTime();
        }

        void OnError(VideoPlayer vp, string message)
        {
            failed = true;
            prepared = false;
            Debug.LogWarning($"Instruction video '{path}' failed: {message}");
            status.gameObject.SetActive(true);
            status.text = "[Video unavailable: " + path + "]";
            status.color = new Color(.9f, .65f, .65f, 1f);
            if (controlsRect) controlsRect.gameObject.SetActive(false);
            surfaceRect.offsetMin = Vector2.zero;
            frame.color = Color.clear;
            fit.Fail(48f);
        }

        void TogglePlay()
        {
            if (failed) return;
            if (!prepared) { wantPlay = !wantPlay; SetPlayingVisual(wantPlay); return; }
            if (player.isPlaying)
            {
                player.Pause();
                Raise("Video_Paused", player.time);
                SetPlayingVisual(false);
            }
            else Play();
        }

        void Play()
        {
            wantPlay = false;
            // A non-looping video that reached its end restarts from the beginning.
            if (!player.isLooping && player.length > 0 && player.time >= player.length - 0.05)
                player.time = 0;
            player.Play();
        }

        void OnSeekReleased(float value)
        {
            if (!prepared || failed || !player.canSetTime || player.length <= 0) return;
            double t = Mathf.Clamp01(value) * player.length;
            player.time = t;
            Raise("Video_Seeked", t);
        }

        void Update()
        {
            if (!prepared || failed) return;
            bool playing = player.isPlaying;
            if (playing != wasPlaying)
            {
                // Logged on the actual transition so a browser-blocked autoplay never records Played.
                if (playing) Raise("Video_Played", player.time);
                wasPlaying = playing;
                SetPlayingVisual(playing);
            }
            UpdateTime();
        }

        void UpdateTime()
        {
            if (!seek) return;
            double len = player.length;
            if (!seek.Dragging)
                seek.SetValueWithoutNotify(len > 0 ? (float)(player.time / len) : 0f);
            double shown = seek.Dragging && len > 0 ? seek.value * len : player.time;
            timeLabel.text = Clock(shown) + " / " + Clock(len);
        }

        void SetPlayingVisual(bool playing)
        {
            if (!buttonLabel) return;
            buttonLabel.text = playing ? "Pause" : "Play";
            playIcon.SetActive(!playing);
            pauseIcon.SetActive(playing);
        }

        static string Clock(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
            int s = (int)Math.Floor(seconds);
            return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        void Raise(string type, double position) => VideoEvent?.Invoke(type, path, position);

        void OnDestroy()
        {
            if (player)
            {
                player.Stop();
                player.targetTexture = null;
            }
            if (target)
            {
                target.Release();
                Destroy(target);
            }
        }
    }

    /// <summary>Slider that reports drag/click release so seeking happens once per gesture.</summary>
    public sealed class SeekSlider : Slider
    {
        public bool Dragging { get; private set; }
        public Action<float> Released;

        public override void OnPointerDown(PointerEventData eventData)
        {
            Dragging = true;
            base.OnPointerDown(eventData);
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            base.OnPointerUp(eventData);
            if (!Dragging) return;
            Dragging = false;
            Released?.Invoke(value);
        }
    }
}
