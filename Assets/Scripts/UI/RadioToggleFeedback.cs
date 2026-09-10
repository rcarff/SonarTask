using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SonarTask.UI
{
    public sealed class RadioToggleFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] Toggle toggle;
        [SerializeField] RectTransform circle;
        [SerializeField] Graphic outer;

        public void Configure(Toggle t, RectTransform circleTransform, Graphic outerGraphic)
        {
            toggle = t;
            circle = circleTransform;
            outer = outerGraphic;
            toggle.onValueChanged.AddListener(Refresh);
            Refresh(toggle.isOn);
        }

        void Refresh(bool on)
        {
            if (outer) outer.color = on ? new Color(.45f, .9f, 1f, 1f) : Color.white;
            if (circle) circle.localScale = on ? Vector3.one * .94f : Vector3.one;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (toggle && toggle.interactable && circle) circle.localScale = Vector3.one * .84f;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (toggle && circle) circle.localScale = toggle.isOn ? Vector3.one * .94f : Vector3.one;
        }
    }
}
