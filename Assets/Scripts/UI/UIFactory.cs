using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace SonarTask.UI {
public static class UIFactory {
    static Font font => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    static Sprite radioRingSprite;
    static Sprite radioDotSprite;

    static Sprite CircleSprite(bool ring) {
        var cached = ring ? radioRingSprite : radioDotSprite;
        if (cached) return cached;
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true) {
            name = ring ? "RuntimeRadioRing" : "RuntimeRadioDot",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
        var px = new Color32[size * size];
        float c = (size - 1) * .5f;
        float outer = size * .44f;
        float inner = ring ? size * .32f : 0f;
        for (int y = 0; y < size; y++) {
            for (int x = 0; x < size; x++) {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                byte a = (byte)((d <= outer && (!ring || d >= inner)) ? 255 : 0);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size);
        sprite.name = ring ? "RuntimeRadioRingSprite" : "RuntimeRadioDotSprite";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        if (ring) radioRingSprite = sprite; else radioDotSprite = sprite;
        return sprite;
    }

    public static Image CircleGraphic(string name, Transform parent, bool ring, Color color) {
        var g = GO(name, parent);
        var i = g.AddComponent<Image>();
        i.sprite = CircleSprite(ring);
        i.color = color;
        i.preserveAspect = true;
        return i;
    }

    public static RectTransform DownChevron(Transform parent, Color color, float width = 16f, float height = 9f, float thickness = 2.5f) {
        var root = GO("ChevronDown", parent);
        var rr = (RectTransform)root.transform;
        rr.anchorMin = rr.anchorMax = new Vector2(.5f, .5f);
        rr.pivot = new Vector2(.5f, .5f);
        rr.sizeDelta = new Vector2(width, height);

        float barLength = Mathf.Sqrt((width * .5f) * (width * .5f) + height * height) * .62f;
        void Bar(string name, float x, float rotation) {
            var img = Panel(name, rr, color);
            img.raycastTarget = false;
            var r = img.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(.5f, .5f);
            r.pivot = new Vector2(.5f, .5f);
            r.sizeDelta = new Vector2(barLength, thickness);
            r.anchoredPosition = new Vector2(x, 0);
            r.localRotation = Quaternion.Euler(0, 0, rotation);
        }
        Bar("Left", -width * .18f, -38f);
        Bar("Right", width * .18f, 38f);
        return rr;
    }

    public static GameObject GO(string name, Transform parent) {
        var g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        return g;
    }

    public static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax) {
        var g = GO(name, parent);
        var r = (RectTransform)g.transform;
        r.anchorMin = min; r.anchorMax = max; r.offsetMin = offMin; r.offsetMax = offMax;
        return r;
    }

    public static Text Text(string text, Transform parent, int size = 18, TextAnchor anchor = TextAnchor.MiddleLeft) {
        var g = GO("Text", parent);
        var t = g.AddComponent<Text>();
        t.font = font;
        t.text = text;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        var r = t.rectTransform;
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(6, 4); r.offsetMax = new Vector2(-6, -4);
        return t;
    }

    public static Image LineGraphic(string name, Transform parent, Color color, Vector2 size, Vector2 anchoredPosition, float rotationDeg) {
        var img = Panel(name, parent, color);
        img.raycastTarget = false;
        var r = img.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(.5f, .5f);
        r.pivot = new Vector2(.5f, .5f);
        r.sizeDelta = size;
        r.anchoredPosition = anchoredPosition;
        r.localRotation = Quaternion.Euler(0, 0, rotationDeg);
        return img;
    }

    public static Image Panel(string name, Transform parent, Color? color = null) {
        var g = GO(name, parent);
        var i = g.AddComponent<Image>();
        i.color = color ?? new Color(.08f, .1f, .12f, .96f);
        return i;
    }

    public static Button Button(string label, Transform parent, System.Action click, int fontSize = 17) {
        var i = Panel("Button:" + label, parent, new Color(.20f, .25f, .30f, 1));
        var b = i.gameObject.AddComponent<Button>();
        Text(label, i.transform, fontSize, TextAnchor.MiddleCenter);
        b.targetGraphic = i;
        if (click != null) b.onClick.AddListener(() => click());
        return b;
    }

    public static InputField Input(string placeholder, Transform parent, bool password = false) {
        var i = Panel("Input", parent, new Color(.12f, .14f, .16f, 1));
        var f = i.gameObject.AddComponent<InputField>();
        var text = Text("", i.transform, 18);
        text.color = Color.white;
        var ph = Text(placeholder, i.transform, 18);
        ph.color = new Color(.7f, .7f, .7f, .8f);
        f.textComponent = text;
        f.placeholder = ph;
        if (password) f.contentType = InputField.ContentType.Password;
        return f;
    }

    public static Toggle Toggle(string label, Transform parent) {
        var root = GO("Toggle:" + label, parent);
        var bg = Panel("Box", root.transform, new Color(.2f, .22f, .25f, 1));
        var br = bg.rectTransform;
        br.anchorMin = new Vector2(0, .15f); br.anchorMax = new Vector2(0, .85f);
        br.pivot = new Vector2(0, .5f); br.sizeDelta = new Vector2(28, 0);
        var ck = Panel("Checkmark", bg.transform, new Color(.85f, .85f, .85f, 1));
        ck.rectTransform.anchorMin = new Vector2(.2f, .2f);
        ck.rectTransform.anchorMax = new Vector2(.8f, .8f);
        ck.rectTransform.offsetMin = ck.rectTransform.offsetMax = Vector2.zero;
        var t = root.AddComponent<Toggle>();
        t.targetGraphic = bg; t.graphic = ck; t.SetIsOnWithoutNotify(false);
        var tx = Text(label, root.transform, 16);
        tx.rectTransform.offsetMin = new Vector2(36, 0);
        return t;
    }

    // Larger radio-style confidence control with visible pressed/selected feedback.
    public static Toggle RadioToggle(string label, Transform parent) {
        var root = GO("RadioToggle:" + label, parent);
        var layout = root.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 6;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;

        var labelText = Text(label, root.transform, 18, TextAnchor.MiddleRight);
        Size(labelText, 38, 26);

        var circle = GO("Radio", root.transform);
        Size((RectTransform)circle.transform, 40, 40);
        var outer = CircleGraphic("OuterRing", circle.transform, true, Color.white);
        outer.rectTransform.anchorMin = Vector2.zero; outer.rectTransform.anchorMax = Vector2.one;
        outer.rectTransform.offsetMin = outer.rectTransform.offsetMax = Vector2.zero;
        outer.raycastTarget = true;
        var inner = CircleGraphic("InnerDot", circle.transform, false, new Color(.45f, .9f, 1f, 1f));
        inner.rectTransform.anchorMin = new Vector2(.22f, .22f); inner.rectTransform.anchorMax = new Vector2(.78f, .78f);
        inner.rectTransform.offsetMin = inner.rectTransform.offsetMax = Vector2.zero;
        inner.raycastTarget = false;

        var t = root.AddComponent<Toggle>();
        t.targetGraphic = outer;
        t.graphic = inner;
        t.SetIsOnWithoutNotify(false);
        var colors = t.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(.75f, .93f, 1f, 1f);
        colors.pressedColor = new Color(.45f, .65f, .75f, 1f);
        colors.disabledColor = new Color(.45f, .45f, .45f, .65f);
        colors.fadeDuration = .06f;
        t.colors = colors;
        root.AddComponent<RadioToggleFeedback>().Configure(t, (RectTransform)circle.transform, outer);
        return t;
    }

    public static PopupDropdown PopupDropdown(string initialText, Transform parent) {
        var g = GO("PopupDropdown", parent);
        var d = g.AddComponent<PopupDropdown>();
        d.EnsureVisuals(initialText);
        return d;
    }

    public static EditableComboBox EditableCombo(string placeholder, Transform parent) {
        var g = GO("EditableCombo", parent);
        var c = g.AddComponent<EditableComboBox>();
        c.EnsureVisuals(placeholder);
        return c;
    }

    public static VerticalLayoutGroup VLayout(Transform parent, float spacing = 6, int pad = 8) {
        var v = parent.GetComponent<VerticalLayoutGroup>() ?? parent.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(pad, pad, pad, pad);
        v.spacing = spacing;
        v.childControlHeight = true; v.childControlWidth = true;
        v.childForceExpandHeight = false; v.childForceExpandWidth = true;
        return v;
    }

    public static HorizontalLayoutGroup HLayout(Transform parent, float spacing = 6, int pad = 8) {
        var h = parent.GetComponent<HorizontalLayoutGroup>() ?? parent.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(pad, pad, pad, pad);
        h.spacing = spacing;
        h.childControlHeight = true; h.childControlWidth = true;
        h.childForceExpandHeight = true; h.childForceExpandWidth = false;
        return h;
    }

    public static LayoutElement Size(Component c, float preferredHeight = 40, float preferredWidth = -1, float flexibleHeight = -1, float flexibleWidth = -1) {
        var e = c.gameObject.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
        e.preferredHeight = preferredHeight;
        if (preferredWidth >= 0) e.preferredWidth = preferredWidth;
        if (flexibleHeight >= 0) e.flexibleHeight = flexibleHeight;
        if (flexibleWidth >= 0) e.flexibleWidth = flexibleWidth;
        return e;
    }

    public static void EnsureEventSystem() {
        if (Object.FindFirstObjectByType<EventSystem>()) return;
        var g = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        Object.DontDestroyOnLoad(g);
    }

    public static void EnsurePresentationCamera() {
        var cam = Object.FindFirstObjectByType<Camera>();
        if (!cam) {
            var g = new GameObject("SONAR Presentation Camera", typeof(Camera), typeof(AudioListener));
            cam = g.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.025f, .035f, .045f, 1f);
            cam.cullingMask = 0;
            cam.orthographic = true;
            cam.depth = -100;
            Object.DontDestroyOnLoad(g);
        } else if (!Object.FindFirstObjectByType<AudioListener>()) {
            cam.gameObject.AddComponent<AudioListener>();
        }
    }

    public static Canvas Canvas() {
        var g = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var c = g.GetComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        var s = g.GetComponent<CanvasScaler>();
        s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = new Vector2(1920, 1080);
        s.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        s.matchWidthOrHeight = .5f;
        return c;
    }
}
}
