using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using SonarTask.Services;

namespace SonarTask.UI
{
    /// <summary>
    /// Small Markdown renderer used by experiment instructions.
    /// Supports headings, paragraphs, emphasis, lists, rules, quotes, local images and videos.
    /// Media lines use <c>![alt](path){width=50% height=360 align=center autoplay loop muted nocontrols}</c>;
    /// the attribute block is optional and the file extension selects image vs video.
    /// Media participate in the normal VerticalLayoutGroup so they cannot overlap
    /// following content and the ScrollRect content height always includes them.
    /// Consecutive list items are grouped with compact spacing so Markdown lists read
    /// like lists rather than a collection of widely separated paragraphs.
    /// </summary>
    public sealed class MarkdownRenderer : MonoBehaviour
    {
        string package;
        bool hasContent;
        bool lastWasSpacer;
        Transform listContainer;
        bool listIsOrdered;
        Transform quoteContainer;

        // Bullets and the quote bar are drawn as geometry rather than glyphs (• / ▌): WebGL
        // builds have no OS font fallback, so those characters silently vanish there.
        const float ListIndent = 22f, ListNestIndent = 22f, ListMarkerWidth = 20f, OrderedMarkerWidth = 30f, ListLineHeight = 24f;
        static readonly Color QuoteBarColor = new Color(.55f, .58f, .62f, 1f);
        static readonly Color QuoteTextColor = new Color(.82f, .84f, .86f);

        /// <summary>Instruction video playback events: (eventType, packageRelativePath, positionSec).</summary>
        public event System.Action<string, string, double> VideoEvent;

        public void Render(string packageName, string markdown)
        {
            package = packageName;
            hasContent = false;
            lastWasSpacer = false;
            listContainer = null;
            listIsOrdered = false;
            quoteContainer = null;

            foreach (Transform c in transform)
                Destroy(c.gameObject);

            var layout = UIFactory.VLayout(transform, 10, 12);
            layout.childAlignment = TextAnchor.UpperLeft;

            var lines = (markdown ?? "").Replace("\r", "").Split('\n');
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd();

                if (string.IsNullOrWhiteSpace(line))
                {
                    EndList();
                    EndQuote();
                    AddSpacerIfNeeded(10);
                    continue;
                }

                bool orderedListItem = Regex.IsMatch(line, @"^\s*\d+\.\s");
                bool unorderedListItem = Regex.IsMatch(line, @"^\s*[-*]\s");

                if (orderedListItem || unorderedListItem)
                {
                    EndQuote();
                    EnsureList(orderedListItem);
                    int level = Mathf.Min(4, (line.Length - line.TrimStart().Length) / 2);
                    var item = Regex.Match(line.TrimStart(), @"^(\d+\.|[-*])\s+(.*)$");
                    AddListItem(orderedListItem ? item.Groups[1].Value : null, item.Groups[2].Value, level);
                    hasContent = true;
                    lastWasSpacer = false;
                    continue;
                }

                EndList();

                if (line == ">" || line.StartsWith("> "))
                {
                    EnsureQuote();
                    string quoted = line.Length > 2 ? line.Substring(2) : "";
                    if (quoted.Trim().Length == 0)
                        QuoteSpacer(8);
                    else
                        AddQuoteText(quoted);
                    hasContent = true;
                    lastWasSpacer = false;
                    continue;
                }

                EndQuote();

                var media = Regex.Match(line.Trim(), @"^!\[(.*?)\]\((.*?)\)(?:\s*\{(.*?)\})?$");
                if (media.Success)
                {
                    string mediaPath = media.Groups[2].Value.Trim();
                    var attrs = MediaAttrs.Parse(media.Groups[3].Value, mediaPath);
                    if (MediaAttrs.IsVideo(mediaPath))
                        AddVideo(mediaPath, media.Groups[1].Value, attrs);
                    else
                        AddImage(mediaPath, media.Groups[1].Value, attrs);
                    hasContent = true;
                    lastWasSpacer = false;
                    continue;
                }

                if (line.StartsWith("### "))
                    AddHeading(line.Substring(4), 20, 10);
                else if (line.StartsWith("## "))
                    AddHeading(line.Substring(3), 23, 14);
                else if (line.StartsWith("# "))
                    AddHeading(line.Substring(2), 28, 18);
                else if (line.StartsWith("---"))
                    AddRule();
                else
                    AddText(line, 17);

                hasContent = true;
                lastWasSpacer = false;
            }

            EndList();
            EndQuote();
            StartCoroutine(RebuildNextFrame());
        }

        void EnsureList(bool ordered)
        {
            if (listContainer != null && listIsOrdered == ordered)
                return;

            EndList();
            var holder = UIFactory.GO(ordered ? "OrderedList" : "UnorderedList", transform);
            var listLayout = UIFactory.VLayout(holder.transform, 1f, 0);
            listLayout.childAlignment = TextAnchor.UpperLeft;
            listLayout.childControlHeight = true;
            listLayout.childForceExpandHeight = false;
            listContainer = holder.transform;
            listIsOrdered = ordered;
        }

        void EndList()
        {
            listContainer = null;
        }

        /// <summary>
        /// One list item: an indented row holding a fixed-width marker cell (a drawn dot, or the
        /// item number) and a flexible text column, so wrapped lines hang under the text rather
        /// than under the marker. <paramref name="number"/> is null for unordered items.
        /// </summary>
        void AddListItem(string number, string value, int level)
        {
            var row = UIFactory.GO("ListItem", listContainer);
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(Mathf.RoundToInt(ListIndent + level * ListNestIndent), 0, 0, 0);
            h.spacing = 0;
            h.childAlignment = TextAnchor.UpperLeft;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;

            float markerWidth = number != null ? OrderedMarkerWidth : ListMarkerWidth;
            var marker = UIFactory.GO("Marker", row.transform);
            var mle = marker.AddComponent<LayoutElement>();
            mle.minWidth = mle.preferredWidth = markerWidth;
            mle.flexibleWidth = 0;
            mle.minHeight = mle.preferredHeight = ListLineHeight;
            if (number != null)
            {
                var n = UIFactory.Text(number, marker.transform, 17, TextAnchor.UpperLeft);
                n.raycastTarget = false;
                var nr = n.rectTransform;
                nr.offsetMin = Vector2.zero; nr.offsetMax = Vector2.zero;
            }
            else
            {
                // Centered vertically on the first line of the 17px item text.
                var dot = UIFactory.CircleGraphic("Bullet", marker.transform, false, Color.lightGray);
                dot.raycastTarget = false;
                var dr = dot.rectTransform;
                dr.anchorMin = dr.anchorMax = new Vector2(.35f, 1f);
                dr.pivot = new Vector2(.5f, .5f);
                dr.sizeDelta = new Vector2(8f, 8f);
                dr.anchoredPosition = new Vector2(0f, -8f);
            }

            var t = UIFactory.Text(Inline(value), row.transform, 17, TextAnchor.UpperLeft);
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;

            // Lists should be compact. The parent list container has only 1px spacing,
            // while the text element uses a slightly tighter minimum line height than
            // normal paragraphs. Wrapped items still expand to their preferred height.
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.minHeight = ListLineHeight;
            le.preferredHeight = -1f;
            le.flexibleHeight = 0f;
            le.minWidth = 0f;
            le.flexibleWidth = 1f;
        }

        /// <summary>Consecutive "&gt; " lines share one block with a drawn gray bar on the left.</summary>
        void EnsureQuote()
        {
            if (quoteContainer != null)
                return;

            var row = UIFactory.GO("Quote", transform);
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(4, 0, 0, 0);
            h.spacing = 12;
            h.childAlignment = TextAnchor.UpperLeft;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            var rle = row.AddComponent<LayoutElement>();
            rle.flexibleHeight = 0f;

            var bar = UIFactory.Panel("QuoteBar", row.transform, QuoteBarColor);
            bar.raycastTarget = false;
            var ble = bar.gameObject.AddComponent<LayoutElement>();
            ble.minWidth = ble.preferredWidth = 4f;
            ble.flexibleWidth = 0f;

            var column = UIFactory.GO("QuoteText", row.transform);
            var v = UIFactory.VLayout(column.transform, 2f, 0);
            v.padding = new RectOffset(0, 0, 2, 2);
            v.childAlignment = TextAnchor.UpperLeft;
            var cle = column.AddComponent<LayoutElement>();
            cle.minWidth = 0f;
            cle.flexibleWidth = 1f;
            quoteContainer = column.transform;
        }

        void EndQuote()
        {
            quoteContainer = null;
        }

        void AddQuoteText(string value)
        {
            var t = UIFactory.Text(Inline(value), quoteContainer, 17, TextAnchor.UpperLeft);
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.color = QuoteTextColor;
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.minHeight = ListLineHeight;
            le.preferredHeight = -1f;
            le.flexibleHeight = 0f;
        }

        void QuoteSpacer(float height)
        {
            var g = UIFactory.GO("Spacer", quoteContainer);
            var le = g.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
            le.flexibleHeight = 0;
        }

        void AddHeading(string text, int size, float topSpace)
        {
            // Markdown previews normally give headings a visual top margin.
            if (hasContent && !lastWasSpacer)
                Spacer(topSpace);

            AddText(text, size);
        }

        void AddText(string value, int size, Color? color = null)
        {
            var t = UIFactory.Text(Inline(value), transform, size, TextAnchor.UpperLeft);
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            if (color.HasValue)
                t.color = color.Value;

            // Do not force a fixed preferred height. Text implements ILayoutElement and
            // reports the correct wrapped preferred height after the parent width is known.
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.minHeight = Mathf.Max(28f, size * 1.45f);
            le.preferredHeight = -1f;
            le.flexibleHeight = 0f;
        }

        static string Inline(string value)
        {
            value = Regex.Replace(value, @"\*\*(.+?)\*\*", "<b>$1</b>");
            value = Regex.Replace(value, @"(?<!\*)\*(.+?)\*(?!\*)", "<i>$1</i>");
            value = Regex.Replace(value, @"`(.+?)`", "<color=#dddddd>$1</color>");
            return value;
        }

        void AddSpacerIfNeeded(float height)
        {
            if (!hasContent || lastWasSpacer)
                return;
            Spacer(height);
        }

        void Spacer(float height)
        {
            var g = UIFactory.GO("Spacer", transform);
            var le = g.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            le.flexibleHeight = 0;
            hasContent = true;
            lastWasSpacer = true;
        }

        void AddRule()
        {
            var i = UIFactory.Panel("Rule", transform, new Color(.6f, .62f, .65f, .8f));
            var le = UIFactory.Size(i, 2);
            le.minHeight = 2;
            le.flexibleHeight = 0;
        }

        /// <summary>
        /// Full-width row whose HorizontalLayoutGroup applies the authored alignment, containing a
        /// sized media box. MediaFit keeps the box sized from attributes and aspect ratio.
        /// </summary>
        RectTransform MediaRow(string name, MediaAttrs attrs, float extraHeight, out MediaFit fit)
        {
            var row = UIFactory.GO(name, transform);
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(0, 0, 0, 0);
            h.childAlignment = attrs.Align;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;

            var box = UIFactory.GO("Box", row.transform);
            var le = box.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 240;
            le.flexibleHeight = 0;
            le.flexibleWidth = 1;

            fit = row.AddComponent<MediaFit>();
            fit.Attrs = attrs;
            fit.Box = le;
            fit.ExtraHeight = extraHeight;
            fit.Rebuild = RebuildLayout;
            return (RectTransform)box.transform;
        }

        void AddImage(string relativePath, string altText, MediaAttrs attrs)
        {
            var box = MediaRow("Image:" + altText, attrs, 0f, out var fit);
            var frame = UIFactory.GO("Frame", box);
            var raw = frame.AddComponent<RawImage>();
            raw.color = Color.white;
            raw.raycastTarget = false;
            raw.texture = Texture2D.blackTexture;
            raw.uvRect = new Rect(0, 0, 1, 1);
            var aspect = frame.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = fit.Aspect;

            StartCoroutine(LoadImage(relativePath, altText, box, fit, raw, aspect));
        }

        IEnumerator LoadImage(string relativePath, string altText, RectTransform box,
            MediaFit fit, RawImage raw, AspectRatioFitter aspectFitter)
        {
            string url = null;
            try { url = RepositoryFactory.Experiments.AssetUrl(package, relativePath); }
            catch (System.Exception e) { Debug.LogWarning($"Instruction image '{relativePath}': {e.Message}"); }

            if (url == null)
            {
                ShowMediaFailure("[Image unavailable: " + altText + "]", box, fit, raw);
                yield break;
            }

            using var request = UnityWebRequestTexture.GetTexture(url);
            yield return request.SendWebRequest();

            if (!box)
                yield break;

            if (request.result != UnityWebRequest.Result.Success)
            {
                ShowMediaFailure("[Image unavailable: " + altText + "]", box, fit, raw);
                yield break;
            }

            var tex = DownloadHandlerTexture.GetContent(request);
            raw.texture = tex;
            raw.color = Color.white;

            // MediaFit sizes the box from the authored attributes, this aspect ratio and the
            // real column width, so the ScrollRect's ContentSizeFitter includes the full image.
            float aspect = tex.width / (float)Mathf.Max(1, tex.height);
            aspectFitter.aspectRatio = aspect;
            fit.SetAspect(aspect);
        }

        static void ShowMediaFailure(string text, RectTransform box, MediaFit fit, RawImage raw)
        {
            if (raw) raw.color = Color.clear;
            var message = UIFactory.Text(text, box, 15, TextAnchor.MiddleCenter);
            message.color = new Color(.9f, .65f, .65f, 1f);
            fit.Fail(48f);
        }

        void AddVideo(string relativePath, string altText, MediaAttrs attrs)
        {
            float extra = attrs.Controls ? InstructionVideo.ControlBarHeight : 0f;
            var box = MediaRow("Video:" + altText, attrs, extra, out var fit);

            string url = null;
            try { url = RepositoryFactory.Experiments.AssetUrl(package, relativePath); }
            catch (System.Exception e) { Debug.LogWarning($"Instruction video '{relativePath}': {e.Message}"); }

            if (url == null)
            {
                ShowMediaFailure("[Video unavailable: " + altText + "]", box, fit, null);
                return;
            }

            var video = box.gameObject.AddComponent<InstructionVideo>();
            video.VideoEvent += (type, path, position) => VideoEvent?.Invoke(type, path, position);
            video.Init(url, relativePath, altText, attrs, fit);
        }

        IEnumerator RebuildNextFrame()
        {
            yield return null;
            RebuildLayout();
        }

        void RebuildLayout()
        {
            Canvas.ForceUpdateCanvases();
            if (transform is RectTransform rect)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
                if (rect.parent is RectTransform parentRect)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
            }
            Canvas.ForceUpdateCanvases();
        }
    }
}
