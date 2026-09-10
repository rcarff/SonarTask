using System;
using System.Collections.Generic;
using SonarTask.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SonarTask.UI;

namespace SonarTask.Rendering {
public enum WaterfallAxis { Frequency, Bearing }

public sealed class WaterfallDisplay : MonoBehaviour, IPointerClickHandler {
    public WaterfallAxis Axis;
    public event Action<float> BearingClicked;
    public RectTransform Plot { get; private set; }
    public int HistoryRows => height;

    RawImage image;
    Texture2D scan;
    Texture2D historyTexture;
    RenderTexture a, b, display;
    Material shift;
    int width = 1024, height = 2;
    float plotTop = .90f;
    readonly List<GameObject> transient = new();
    readonly List<Marker> markers = new();
    float historySec = 30;
    Color32[] pixels;
    Color32[] historyPixels;

    struct Marker { public GameObject go; public float age, maxAge; }

    public void Build(WaterfallAxis axis, object view, string title, float scanRateHz) {
        Axis = axis;
        var bg = gameObject.AddComponent<Image>();
        bg.color = new Color(.025f, .035f, .04f, 1);
        bool hasTitle = !string.IsNullOrWhiteSpace(title);
        plotTop = hasTitle ? .90f : .94f;
        if (hasTitle) {
            var titleText = UIFactory.Text(title, transform, 16, TextAnchor.UpperCenter);
            titleText.rectTransform.anchorMin = new Vector2(0, 1);
            titleText.rectTransform.anchorMax = new Vector2(1, 1);
            titleText.rectTransform.pivot = new Vector2(.5f, 1);
            titleText.rectTransform.sizeDelta = new Vector2(0, 26);
            titleText.rectTransform.anchoredPosition = Vector2.zero;
        }

        Plot = UIFactory.Rect("Plot", transform, new Vector2(.06f, .09f), new Vector2(.985f, plotTop), Vector2.zero, Vector2.zero);
        var border = Plot.gameObject.AddComponent<Image>();
        border.color = Color.white;
        var inner = UIFactory.Rect("Image", Plot, new Vector2(0, 0), new Vector2(1, 1), new Vector2(2, 2), new Vector2(-2, -2));
        image = inner.gameObject.AddComponent<RawImage>();
        image.color = Color.white;

        ConfigureView(view);
        float safeScanRate = Mathf.Max(1f, scanRateHz);
        height = Mathf.Max(2, Mathf.RoundToInt(historySec * safeScanRate));
        if (height > 4096)
            throw new InvalidOperationException($"Waterfall history requires {height} rows ({historySec:0.###} sec at {safeScanRate:0.###} Hz). Maximum supported rows is 4096.");
        BuildAxes(view);
        InitTextures();
    }

    void ConfigureView(object v) {
        if (v is BTHView bv) historySec = Mathf.Max(1, bv.TimeEnd - bv.TimeStart);
        else if (v is LOFARView lv) historySec = Mathf.Max(1, lv.TimeEnd - lv.TimeStart);
    }

    void InitTextures() {
        // Keep the ping-pong buffers point-filtered so each shift copies exact rows.
        // A separate presentation RenderTexture is kept attached to the RawImage.
        // This avoids exposing alternating A/B textures directly to WebGL, which can
        // otherwise produce visible flicker/static differences on some browsers/GPUs.
        a = RT("A", FilterMode.Point);
        b = RT("B", FilterMode.Point);
        display = RT("Display", FilterMode.Bilinear);
        scan = new Texture2D(width, 1, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        historyTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        pixels = new Color32[width];
        historyPixels = new Color32[width * height];
        // This shader is loaded from Resources so it cannot be stripped from player/Web builds.
        // Shader.Find alone works in the Editor but Unity may omit an otherwise unreferenced
        // shader from a player build.
        var waterfallShader = Resources.Load<Shader>("WaterfallShift");
        if (!waterfallShader) waterfallShader = Shader.Find("Sonar/WaterfallShift");
        if (!waterfallShader)
            throw new InvalidOperationException(
                "Required waterfall shader 'Sonar/WaterfallShift' was not included in this build.");

        shift = new Material(waterfallShader);
        shift.SetFloat("_TextureHeight", height);
        shift.SetFloat("_TexelHeight", 1f / height);
        ClearTexture(a);
        ClearTexture(b);
        ClearTexture(display);
        image.texture = display;
    }

    RenderTexture RT(string n, FilterMode filterMode) {
        var r = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) {
            name = "Waterfall" + n,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = filterMode,
            useMipMap = false,
            autoGenerateMips = false,
            antiAliasing = 1
        };
        r.Create();
        return r;
    }

    static void ClearTexture(RenderTexture r) {
        var old = RenderTexture.active;
        RenderTexture.active = r;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = old;
    }

    public void ClearHistory() {
        if (a) ClearTexture(a);
        if (b) ClearTexture(b);
        if (display) ClearTexture(display);
        if (image && display) image.texture = display;
    }

    /// <summary>
    /// Replaces the entire visible waterfall history in one upload. Rows are ordered
    /// oldest-to-newest; the newest row is placed at the top, matching PushScanline.
    /// </summary>
    public void ReplaceHistory(IReadOnlyList<float[]> oldestToNewest, string palette) {
        if (oldestToNewest == null || oldestToNewest.Count == 0) {
            ClearHistory();
            return;
        }

        for (int y = 0; y < height; y++) {
            int sourceRow = Mathf.Clamp(Mathf.RoundToInt(y * (oldestToNewest.Count - 1f) / Mathf.Max(1, height - 1f)), 0, oldestToNewest.Count - 1);
            var intensity = oldestToNewest[sourceRow];
            for (int x = 0; x < width; x++) {
                float f = intensity == null || intensity.Length == 0
                    ? 0f
                    : intensity[Mathf.Clamp(Mathf.RoundToInt(x * (intensity.Length - 1f) / (width - 1f)), 0, intensity.Length - 1)];
                historyPixels[y * width + x] = Palette(Mathf.Clamp01(f), palette);
            }
        }

        historyTexture.SetPixels32(historyPixels);
        historyTexture.Apply(false, false);
        Graphics.Blit(historyTexture, a);
        Graphics.Blit(a, b);
        Graphics.Blit(a, display);
        image.texture = display;
    }

    public void PushScanline(float[] intensity, string palette) {
        if (intensity == null) return;
        for (int x = 0; x < width; x++) {
            float f = intensity[Mathf.Clamp(Mathf.RoundToInt(x * (intensity.Length - 1f) / (width - 1f)), 0, intensity.Length - 1)];
            pixels[x] = Palette(Mathf.Clamp01(f), palette);
        }
        scan.SetPixels32(pixels);
        scan.Apply(false, false);
        shift.SetTexture("_ScanLine", scan);
        Graphics.Blit(a, b, shift);
        (a, b) = (b, a);

        // Present through a stable texture instead of swapping the RawImage's source
        // between the two ping-pong buffers. The internal buffers remain exact
        // point-sampled history; the presentation texture can be bilinear-scaled.
        Graphics.Blit(a, display);
        image.texture = display;
    }

    static Color32 Palette(float v, string p) {
        v = Mathf.Pow(v, .72f);
        if (string.Equals(p, "Grayscale", StringComparison.OrdinalIgnoreCase)) {
            byte c = (byte)(255 * v);
            return new Color32(c, c, c, 255);
        }
        if (string.Equals(p, "Cividis", StringComparison.OrdinalIgnoreCase)) {
            float r = Mathf.Clamp01(.05f + .94f * v), g = Mathf.Clamp01(.12f + .78f * v), bl = Mathf.Clamp01(.25f + .35f * (1 - v));
            return (Color)new Color(r, g, bl, 1);
        }
        return (Color)new Color(.02f + .72f * v, .03f + .97f * v, .02f + .64f * v, 1);
    }

    void BuildAxes(object v) {
        if (v is LOFARView f) {
            Ticks(f.HzStart, f.HzEnd, f.HzTicksEvery, f.HzLabelsEvery, true, x => $"{Mathf.RoundToInt(x)} Hz");
            TimeTicks(f.TimeStart, f.TimeEnd, f.TimeTicksEvery, f.TimeLabelsEvery);
        } else if (v is BTHView d) {
            float span = AngleSpan(d.DegStart, d.DegEnd);
            Ticks(0, span, d.DegTicksEvery, d.DegLabelsEvery, true, x => $"{ExperimentResolver.NormInt(d.DegStart + Mathf.RoundToInt(x)):D3}");
            TimeTicks(d.TimeStart, d.TimeEnd, d.TimeTicksEvery, d.TimeLabelsEvery);
        }
    }

    void Ticks(float lo, float hi, float tick, float label, bool horizontal, Func<float, string> fmt) {
        if (tick <= 0 || hi <= lo) return;
        for (float x = lo; x <= hi + .001f; x += tick) {
            float n = (x - lo) / (hi - lo);
            var line = UIFactory.Panel("Tick", Plot, new Color(.75f, .78f, .8f, .85f));
            var r = line.rectTransform;
            r.anchorMin = new Vector2(n, 1); r.anchorMax = new Vector2(n, 1); r.pivot = new Vector2(.5f, 1); r.sizeDelta = new Vector2(1, 7); r.anchoredPosition = Vector2.zero;
            var mirror = UIFactory.Panel("TickBottom", Plot, new Color(.75f, .78f, .8f, .85f));
            var mr = mirror.rectTransform;
            mr.anchorMin = new Vector2(n, 0); mr.anchorMax = new Vector2(n, 0); mr.pivot = new Vector2(.5f, 0); mr.sizeDelta = new Vector2(1, 7);
            if (label > 0 && Mathf.Abs(((x - lo) / label) - Mathf.Round((x - lo) / label)) < .01f) {
                var t = UIFactory.Text(fmt(x), transform, 11, TextAnchor.UpperCenter);
                var tr = t.rectTransform;
                tr.anchorMin = new Vector2(.06f + .925f * n, plotTop); tr.anchorMax = tr.anchorMin; tr.pivot = new Vector2(.5f, 0); tr.sizeDelta = new Vector2(75, 22);
            }
        }
    }

    void TimeTicks(float lo, float hi, float tick, float label) {
        float span = Mathf.Max(.001f, hi - lo);
        if (tick <= 0) return;
        for (float x = lo; x <= hi + .001f; x += tick) {
            float n = (x - lo) / span;
            var line = UIFactory.Panel("TickT", Plot, new Color(.75f, .78f, .8f, .85f));
            var r = line.rectTransform;
            r.anchorMin = new Vector2(0, 1 - n); r.anchorMax = new Vector2(0, 1 - n); r.pivot = new Vector2(0, .5f); r.sizeDelta = new Vector2(7, 1);
            var mirror = UIFactory.Panel("TickRight", Plot, new Color(.75f, .78f, .8f, .85f));
            var mr = mirror.rectTransform;
            mr.anchorMin = new Vector2(1, 1 - n); mr.anchorMax = new Vector2(1, 1 - n); mr.pivot = new Vector2(1, .5f); mr.sizeDelta = new Vector2(7, 1);
            if (label > 0 && Mathf.Abs(((x - lo) / label) - Mathf.Round((x - lo) / label)) < .01f) {
                var t = UIFactory.Text($"{Mathf.RoundToInt(x)}s", transform, 11, TextAnchor.MiddleRight);
                var tr = t.rectTransform;
                tr.anchorMin = new Vector2(.055f, .09f + (plotTop - .09f) * (1 - n)); tr.anchorMax = tr.anchorMin; tr.pivot = new Vector2(1, .5f); tr.sizeDelta = new Vector2(55, 20);
            }
        }
    }

    public void ShowBearingSelection(float normalized, float normalizedWidth) {
        ClearSelection();
        float lo = normalized - normalizedWidth / 2, hi = normalized + normalizedWidth / 2;
        if (lo < 0) { AddSelection(0, hi); AddSelection(1 + lo, 1); }
        else if (hi > 1) { AddSelection(lo, 1); AddSelection(0, hi - 1); }
        else AddSelection(lo, hi);
    }

    void AddSelection(float lo, float hi) {
        var box = UIFactory.Panel("Selection", Plot, new Color(1, 1, 0, .11f));
        var r = box.rectTransform;
        r.anchorMin = new Vector2(Mathf.Clamp01(lo), 0); r.anchorMax = new Vector2(Mathf.Clamp01(hi), 1); r.offsetMin = r.offsetMax = Vector2.zero;
        var outline = box.gameObject.AddComponent<Outline>();
        outline.effectColor = Color.yellow; outline.effectDistance = new Vector2(2, 2);
        transient.Add(box.gameObject);
    }

    public void ShowCarets(IEnumerable<float> normalized) {
        ClearCarets();
        foreach (var n in normalized) {
            var r = UIFactory.DownChevron(Plot, new Color(1f, .85f, .1f, 1), 20f, 11f, 2.8f);
            r.gameObject.name = "TrainingCaret";
            r.anchorMin = new Vector2(Mathf.Clamp01(n), 1); r.anchorMax = r.anchorMin;
            r.pivot = new Vector2(.5f, 0); r.sizeDelta = new Vector2(20, 11);
            r.anchoredPosition = new Vector2(0, 3);
            transient.Add(r.gameObject);
        }
    }

    public void ClearCarets() {
        for (int i = transient.Count - 1; i >= 0; i--)
            if (transient[i] && transient[i].name == "TrainingCaret") {
                Destroy(transient[i]);
                transient.RemoveAt(i);
            }
    }

    public void ClearSelection() {
        for (int i = transient.Count - 1; i >= 0; i--)
            if (transient[i] && transient[i].name == "Selection") {
                Destroy(transient[i]);
                transient.RemoveAt(i);
            }
    }

    public void ClearTransient() {
        foreach (var g in transient) if (g) Destroy(g);
        transient.Clear();
    }

    public void AddSMarker(float normalized, float visibleForSec) {
        var holder = UIFactory.GO("SMarker", transform);
        var r = (RectTransform)holder.transform;
        r.anchorMin = new Vector2(.06f + .925f * Mathf.Clamp01(normalized), .035f); r.anchorMax = r.anchorMin; r.sizeDelta = new Vector2(26, 42);
        var t = UIFactory.Text("│\nS", holder.transform, 15, TextAnchor.MiddleCenter); t.color = Color.white;
        markers.Add(new Marker { go = holder, age = 0, maxAge = Mathf.Max(1, visibleForSec) });
    }

    public void AdvanceMarkers(float dt) {
        for (int i = markers.Count - 1; i >= 0; i--) {
            var m = markers[i];
            m.age += dt;
            if (m.age >= m.maxAge) {
                if (m.go) Destroy(m.go);
                markers.RemoveAt(i);
            } else markers[i] = m;
        }
    }

    public void OnPointerClick(PointerEventData e) {
        if (Axis != WaterfallAxis.Bearing || BearingClicked == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Plot, e.position, e.pressEventCamera, out var local)) return;
        var r = Plot.rect;
        float n = Mathf.InverseLerp(r.xMin, r.xMax, local.x);
        if (n >= 0 && n <= 1) BearingClicked(n);
    }

    public static float AngleSpan(float start, float end) {
        float s = (end - start) % 360;
        if (s <= 0) s += 360;
        return s;
    }

    void OnDestroy() {
        if (a) { a.Release(); Destroy(a); }
        if (b) { b.Release(); Destroy(b); }
        if (display) { display.Release(); Destroy(display); }
        if (scan) Destroy(scan);
        if (historyTexture) Destroy(historyTexture);
        if (shift) Destroy(shift);
    }
}
}
