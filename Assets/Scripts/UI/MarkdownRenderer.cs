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
    /// Supports headings, paragraphs, emphasis, lists, rules, quotes and local images.
    /// Images participate in the normal VerticalLayoutGroup so they cannot overlap
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

        public void Render(string packageName, string markdown)
        {
            package = packageName;
            hasContent = false;
            lastWasSpacer = false;
            listContainer = null;
            listIsOrdered = false;

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
                    AddSpacerIfNeeded(10);
                    continue;
                }

                bool orderedListItem = Regex.IsMatch(line, @"^\s*\d+\.\s");
                bool unorderedListItem = Regex.IsMatch(line, @"^\s*[-*]\s");

                if (orderedListItem)
                {
                    EnsureList(true);
                    AddListText(Regex.Replace(line.TrimStart(), @"^(\d+)\.\s", "$1.  "));
                    hasContent = true;
                    lastWasSpacer = false;
                    continue;
                }

                if (unorderedListItem)
                {
                    EnsureList(false);
                    string trimmed = line.TrimStart();
                    AddListText("•  " + trimmed.Substring(2));
                    hasContent = true;
                    lastWasSpacer = false;
                    continue;
                }

                EndList();

                var image = Regex.Match(line.Trim(), @"^!\[(.*?)\]\((.*?)\)$");
                if (image.Success)
                {
                    AddImagePlaceholder(image.Groups[2].Value, image.Groups[1].Value);
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
                else if (line.StartsWith("> "))
                    AddText("▌ " + line.Substring(2), 17, new Color(.82f, .84f, .86f));
                else if (line.StartsWith("---"))
                    AddRule();
                else
                    AddText(line, 17);

                hasContent = true;
                lastWasSpacer = false;
            }

            EndList();
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

        void AddListText(string value)
        {
            var t = UIFactory.Text(Inline(value), listContainer, 17, TextAnchor.UpperLeft);
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;

            // Lists should be compact. The parent list container has only 1px spacing,
            // while the text element uses a slightly tighter minimum line height than
            // normal paragraphs. Wrapped items still expand to their preferred height.
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 24f;
            le.preferredHeight = -1f;
            le.flexibleHeight = 0f;
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

        void AddImagePlaceholder(string relativePath, string altText)
        {
            var holder = UIFactory.GO("Image:" + altText, transform);
            var rect = (RectTransform)holder.transform;
            var layout = holder.AddComponent<LayoutElement>();
            layout.minHeight = 80;
            layout.preferredHeight = 240;
            layout.flexibleHeight = 0;

            var raw = holder.AddComponent<RawImage>();
            raw.color = Color.white;
            raw.raycastTarget = false;
            raw.texture = Texture2D.blackTexture;
            raw.uvRect = new Rect(0, 0, 1, 1);

            StartCoroutine(LoadImage(relativePath, altText, rect, layout, raw));
        }

        IEnumerator LoadImage(string relativePath, string altText, RectTransform holder,
            LayoutElement layout, RawImage raw)
        {
            using var request = UnityWebRequestTexture.GetTexture(
                RepositoryFactory.Experiments.AssetUrl(package, relativePath));
            yield return request.SendWebRequest();

            if (!holder)
                yield break;

            if (request.result != UnityWebRequest.Result.Success)
            {
                raw.color = Color.clear;
                var message = UIFactory.Text("[Image unavailable: " + altText + "]",
                    holder, 15, TextAnchor.MiddleCenter);
                message.color = new Color(.9f, .65f, .65f, 1f);
                layout.preferredHeight = 48;
                RebuildLayout();
                yield break;
            }

            var tex = DownloadHandlerTexture.GetContent(request);
            raw.texture = tex;
            raw.color = Color.white;

            // Wait until the VerticalLayoutGroup has assigned the image its real width.
            yield return null;
            Canvas.ForceUpdateCanvases();

            float width = holder.rect.width;
            if (width <= 1f && holder.parent is RectTransform parentRect)
                width = Mathf.Max(1f, parentRect.rect.width - 24f);

            float aspect = tex.width / (float)Mathf.Max(1, tex.height);
            float imageHeight = width / Mathf.Max(.01f, aspect);

            // Give the layout system the real image height. The ScrollRect's
            // ContentSizeFitter then includes the complete image, including a final image.
            layout.minHeight = imageHeight;
            layout.preferredHeight = imageHeight;
            layout.flexibleHeight = 0;

            RebuildLayout();
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
