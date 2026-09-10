using System; using UnityEngine; using UnityEngine.UI;
namespace SonarTask.UI {
public static class Modal {
 public static void Show(Transform parent,string title,string message,Action yes=null,Action no=null,string yesText="OK",string noText="Cancel"){
  var shade=UIFactory.Panel("ModalShade",parent,new Color(0,0,0,.72f));var sr=shade.rectTransform;sr.anchorMin=Vector2.zero;sr.anchorMax=Vector2.one;sr.offsetMin=sr.offsetMax=Vector2.zero;shade.transform.SetAsLastSibling();
  var box=UIFactory.Rect("Modal",shade.transform,new Vector2(.25f,.3f),new Vector2(.75f,.7f),Vector2.zero,Vector2.zero);UIFactory.Panel("BG",box).rectTransform.Stretch();var v=UIFactory.VLayout(box,12,20);var t=UIFactory.Text(title,box,25,TextAnchor.MiddleCenter);UIFactory.Size(t,50);var m=UIFactory.Text(message,box,18,TextAnchor.MiddleCenter);UIFactory.Size(m,100,-1,1);var row=UIFactory.GO("Buttons",box.transform);UIFactory.Size(row.transform as RectTransform,52);var h=UIFactory.HLayout(row.transform,12,0);h.childAlignment=TextAnchor.MiddleCenter;var y=UIFactory.Button(yesText,row.transform,()=>{UnityEngine.Object.Destroy(shade.gameObject);yes?.Invoke();});UIFactory.Size(y,48,150);if(no!=null){var n=UIFactory.Button(noText,row.transform,()=>{UnityEngine.Object.Destroy(shade.gameObject);no();});UIFactory.Size(n,48,150);}
 }
 static void Stretch(this RectTransform r){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;}
}
}
