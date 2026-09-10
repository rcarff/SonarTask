using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace SonarTask.Services {
public sealed class AudioService : MonoBehaviour {
    sealed class SignalChannel { public AudioSource Source; public bool Started; }

    AudioSource background;
    readonly List<SignalChannel> signalSources = new();
    bool backgroundStarted;
    bool backgroundPlayRequested;
    bool paused;

    public void Init() {
        background = gameObject.AddComponent<AudioSource>();
        ConfigureSource(background, true);
    }


    static void ConfigureSource(AudioSource source, bool loop) {
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;      // Always 2D: experiment audio is not spatialized.
        source.dopplerLevel = 0f;
        source.priority = 128;
        source.mute = false;
    }

    IEnumerator Load(string url, Action<AudioClip> done) {
        AudioType type = url.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ? AudioType.MPEG : AudioType.WAV;
        using var request = UnityWebRequestMultimedia.GetAudioClip(url, type);
        yield return request.SendWebRequest();
        done(request.result == UnityWebRequest.Result.Success ? DownloadHandlerAudioClip.GetContent(request) : null);
    }

    public void SetBackground(string package, string path, float volume, bool play) {
        background.Stop();
        background.clip = null;
        backgroundStarted = false;
        backgroundPlayRequested = play;
        if (string.IsNullOrWhiteSpace(path)) return;
        StartCoroutine(Load(RepositoryFactory.Experiments.AssetUrl(package, path), clip => {
            if (!clip || !background) return;
            background.clip = clip;
            background.volume = Mathf.Clamp01(volume);
            if (backgroundPlayRequested && !paused) {
                background.Play();
                backgroundStarted = true;
            }
        }));
    }

    public void PauseAll() {
        paused = true;
        if (background.isPlaying) background.Pause();
        foreach (var c in signalSources) if (c.Source && c.Source.isPlaying) c.Source.Pause();
    }

    public void ResumeAll() {
        paused = false;
        backgroundPlayRequested = true;
        if (background.clip) {
            if (backgroundStarted) background.UnPause();
            else { background.Play(); backgroundStarted = true; }
        }
        foreach (var c in signalSources) {
            if (!c.Source || !c.Source.clip) continue;
            if (c.Started) c.Source.UnPause();
            else { c.Source.Play(); c.Started = true; }
        }
    }

    public void StopSignalAudio() {
        foreach (var c in signalSources) if (c.Source) StartCoroutine(FadeDestroy(c.Source, .08f));
        signalSources.Clear();
    }

    IEnumerator FadeDestroy(AudioSource source, float sec) {
        float initial = source.volume, t = 0;
        while (source && t < sec) {
            t += Time.unscaledDeltaTime;
            source.volume = initial * Mathf.Clamp01(1 - t/sec);
            yield return null;
        }
        if (source) Destroy(source.gameObject);
    }

    public void PlaySelected(string package, IEnumerable<SonarTask.Core.ResolvedSignal> signals) {
        StopSignalAudio();
        foreach (var sig in signals) {
            if (string.IsNullOrWhiteSpace(sig.AudioFile)) continue;
            var go = new GameObject("SignalAudio:" + sig.ID);
            go.transform.SetParent(transform);
            var src = go.AddComponent<AudioSource>();
            ConfigureSource(src, true);
            src.volume = Mathf.Clamp01(sig.AudioVolume);
            var channel = new SignalChannel { Source = src };
            signalSources.Add(channel);
            StartCoroutine(Load(RepositoryFactory.Experiments.AssetUrl(package, sig.AudioFile), clip => {
                if (!src || !clip) return;
                src.clip = clip;
                if (!paused) { src.Play(); channel.Started = true; }
            }));
        }
    }

    public void StopAll() {
        backgroundPlayRequested = false;
        background.Stop();
        StopSignalAudio();
    }
}
}
