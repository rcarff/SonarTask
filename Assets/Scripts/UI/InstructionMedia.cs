using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace SonarTask.UI
{
    /// <summary>
    /// Optional attribute block that follows a Markdown image/video line:
    /// <c>![alt](Videos/demo.mp4){width=640 height=360 align=center autoplay loop muted nocontrols}</c>.
    /// width accepts pixels or a percentage of the instruction column; height accepts pixels.
    /// Missing dimensions are derived from the media aspect ratio.
    /// </summary>
    public sealed class MediaAttrs
    {
        public float? Width;
        public bool WidthPercent;
        public float? Height;
        public TextAnchor Align = TextAnchor.UpperLeft;
        public bool Autoplay, Loop, Muted, Controls = true;

        static readonly string[] VideoExtensions = { ".mp4", ".m4v", ".webm", ".mov" };

        public static bool IsVideo(string path)
        {
            string ext = Path.GetExtension(path ?? "").ToLowerInvariant();
            return Array.IndexOf(VideoExtensions, ext) >= 0;
        }

        public static MediaAttrs Parse(string block, string context)
        {
            var a = new MediaAttrs();
            if (string.IsNullOrWhiteSpace(block))
                return a;

            foreach (Match m in Regex.Matches(block, @"([A-Za-z]+)\s*=\s*(""[^""]*""|'[^']*'|[^\s]+)|([A-Za-z]+)"))
            {
                string key = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[3].Value).ToLowerInvariant();
                string value = m.Groups[2].Success ? m.Groups[2].Value.Trim('"', '\'').Trim().ToLowerInvariant() : null;
                switch (key)
                {
                    case "width":
                        if (value != null && TryLength(value, true, out var w, out var pct)) { a.Width = w; a.WidthPercent = pct; }
                        else Warn(context, "width", value);
                        break;
                    case "height":
                        if (value != null && TryLength(value, false, out var h, out _)) a.Height = h;
                        else Warn(context, "height", value);
                        break;
                    case "align":
                        if (value == "left") a.Align = TextAnchor.UpperLeft;
                        else if (value == "center" || value == "centre") a.Align = TextAnchor.UpperCenter;
                        else if (value == "right") a.Align = TextAnchor.UpperRight;
                        else Warn(context, "align", value);
                        break;
                    case "autoplay": a.Autoplay = Flag(value); break;
                    case "loop": a.Loop = Flag(value); break;
                    case "muted": a.Muted = Flag(value); break;
                    case "controls": a.Controls = Flag(value); break;
                    case "nocontrols": a.Controls = !Flag(value); break;
                    default:
                        Debug.LogWarning($"Instructions media '{context}': unknown attribute '{key}' ignored.");
                        break;
                }
            }
            return a;
        }

        static bool Flag(string value) => value == null || value == "true" || value == "yes" || value == "1";

        static bool TryLength(string value, bool allowPercent, out float result, out bool percent)
        {
            percent = value.EndsWith("%");
            if (percent && !allowPercent) { result = 0; return false; }
            string number = percent ? value.TrimEnd('%') : (value.EndsWith("px") ? value.Substring(0, value.Length - 2) : value);
            bool ok = float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && result > 0;
            if (ok && percent) result = Mathf.Min(result, 100f);
            return ok;
        }

        static void Warn(string context, string key, string value) =>
            Debug.LogWarning($"Instructions media '{context}': invalid {key} value '{value}' ignored.");
    }

    /// <summary>
    /// Sits on a full-width media row inside the instruction VerticalLayoutGroup and keeps the
    /// media box sized from its attributes, the known aspect ratio and the current column width.
    /// It re-fits when the column width changes (e.g. responsive WebGL canvas resize).
    /// </summary>
    public sealed class MediaFit : MonoBehaviour
    {
        public MediaAttrs Attrs;
        public LayoutElement Box;
        public float Aspect = 16f / 9f;
        public float ExtraHeight;
        public Action Rebuild;

        bool dirty = true;
        bool failed;
        float lastWidth = -1f;

        public void SetAspect(float aspect)
        {
            if (aspect > 0.01f && !float.IsNaN(aspect) && !float.IsInfinity(aspect))
                Aspect = aspect;
            dirty = true;
            lastWidth = -1f;
        }

        public void Fail(float height)
        {
            failed = true;
            Box.preferredWidth = -1f;
            Box.flexibleWidth = 1f;
            Box.minHeight = Box.preferredHeight = height;
            Rebuild?.Invoke();
        }

        void OnRectTransformDimensionsChange() => dirty = true;

        void LateUpdate()
        {
            if (!dirty || failed || Box == null)
                return;
            float available = ((RectTransform)transform).rect.width;
            if (available <= 1f)
                return;
            dirty = false;
            if (Mathf.Abs(available - lastWidth) < .5f)
                return;
            lastWidth = available;

            var size = Compute(available);
            Box.minWidth = Box.preferredWidth = size.x;
            Box.flexibleWidth = 0f;
            Box.minHeight = Box.preferredHeight = size.y + ExtraHeight;
            Box.flexibleHeight = 0f;
            Rebuild?.Invoke();
        }

        Vector2 Compute(float available)
        {
            var a = Attrs ?? new MediaAttrs();
            float w;
            if (a.Width.HasValue) w = a.WidthPercent ? available * a.Width.Value / 100f : a.Width.Value;
            else if (a.Height.HasValue) w = a.Height.Value * Aspect;
            else w = available;
            w = Mathf.Clamp(w, 16f, available);

            // When both dimensions are authored the box is honoured exactly and the media is
            // letterboxed inside it; otherwise the height follows the (possibly capped) width.
            float h = a.Height.HasValue && (a.Width.HasValue || w * 1.001f >= a.Height.Value * Aspect)
                ? a.Height.Value
                : w / Aspect;
            return new Vector2(w, Mathf.Max(8f, h));
        }
    }
}
