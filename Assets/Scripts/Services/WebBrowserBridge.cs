using System; using System.Runtime.InteropServices; using UnityEngine;
namespace SonarTask.Services {
public static class WebBrowserBridge {
#if UNITY_WEBGL && !UNITY_EDITOR
 [DllImport("__Internal")] static extern void SonarOpenUrl(string url);
 [DllImport("__Internal")] static extern void SonarNavigateUrl(string url);
 [DllImport("__Internal")] static extern void SonarPickZip(string go,string method);
#endif
 public static void OpenUrl(string url){
#if UNITY_WEBGL && !UNITY_EDITOR
  SonarOpenUrl(url);
#else
  Application.OpenURL(url);
#endif
 }
 public static void NavigateUrl(string url){
#if UNITY_WEBGL && !UNITY_EDITOR
  SonarNavigateUrl(url);
#else
  Application.OpenURL(url);
#endif
 }
 public static void PickZip(GameObject receiver,string method){
#if UNITY_WEBGL && !UNITY_EDITOR
  SonarPickZip(receiver.name,method);
#else
  Debug.LogWarning("ZIP picker is Web-only in runtime; use server filesystem or Web settings.");
#endif
 }
}
}
