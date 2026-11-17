using System;
using SonarTask.Services;
using UnityEngine;
using UnityEngine.UI;

namespace SonarTask.UI {
public sealed class InstructionsOverlay : MonoBehaviour {
 Action closedCallback; bool isClosed;
 public bool IsClosed => isClosed;
 /// <summary>Closes the overlay (Close button or programmatic, e.g. AutoStart countdown expiry). Invokes the closed callback exactly once.</summary>
 public void Close(){if(isClosed)return;isClosed=true;Destroy(gameObject);closedCallback?.Invoke();}
 public static InstructionsOverlay Show(Transform parent,string package,string file,Action closed,Action<string,string,double> videoEvent=null) {
  var shade=UIFactory.Panel("InstructionsShade",parent,new Color(0,0,0,.72f));var r=shade.rectTransform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;shade.transform.SetAsLastSibling();
  var overlay=shade.gameObject.AddComponent<InstructionsOverlay>();overlay.closedCallback=closed;
  var box=UIFactory.Rect("InstructionsOverlay",shade.transform,new Vector2(.1f,.08f),new Vector2(.9f,.92f),Vector2.zero,Vector2.zero);var bg=UIFactory.Panel("BG",box);bg.rectTransform.anchorMin=Vector2.zero;bg.rectTransform.anchorMax=Vector2.one;bg.rectTransform.offsetMin=bg.rectTransform.offsetMax=Vector2.zero;bg.gameObject.AddComponent<LayoutElement>().ignoreLayout=true;UIFactory.VLayout(box,8,16);
  UIFactory.Size(UIFactory.Text("Instructions",box,27,TextAnchor.MiddleCenter),48);
  var scroll=UIFactory.GO("Scroll",box);UIFactory.Size(scroll.transform as RectTransform,400,-1,1);var sr=scroll.AddComponent<ScrollRect>();
  var vp=UIFactory.Panel("Viewport",scroll.transform,new Color(.035f,.045f,.055f,1));vp.rectTransform.anchorMin=Vector2.zero;vp.rectTransform.anchorMax=Vector2.one;vp.rectTransform.offsetMin=vp.rectTransform.offsetMax=Vector2.zero;vp.gameObject.AddComponent<Mask>().showMaskGraphic=false;
  var content=UIFactory.GO("Content",vp.transform);var cr=(RectTransform)content.transform;cr.anchorMin=new Vector2(0,1);cr.anchorMax=Vector2.one;cr.pivot=new Vector2(.5f,1);cr.sizeDelta=Vector2.zero;content.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;sr.viewport=vp.rectTransform;sr.content=cr;UIFactory.VerticalScroll(sr);
  var md=content.AddComponent<MarkdownRenderer>();if(videoEvent!=null)md.VideoEvent+=videoEvent;overlay.StartCoroutine(RepositoryFactory.Experiments.LoadTextAsset(package,file,text=>md.Render(package,text),e=>md.Render(package,"# Unable to load instructions\n\n"+e)));
  var close=UIFactory.Button("Close",box,overlay.Close);UIFactory.Size(close,48);
  return overlay;
 }
}
}
