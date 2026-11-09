using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace SonarTask.Services {
public static class WebBrowserBridge {
#if UNITY_WEBGL && !UNITY_EDITOR
 [DllImport("__Internal")] static extern void SonarOpenUrl(string url);
 [DllImport("__Internal")] static extern void SonarNavigateUrl(string url);
 [DllImport("__Internal")] static extern void SonarPickFile(string go,string method,string url,string accept,int maxBytes,string label);
 [DllImport("__Internal")] static extern void SonarPickZip(string go,string method);
 [DllImport("__Internal")] static extern void SonarCopyText(string text);
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
 public static void CopyText(string text){
#if UNITY_WEBGL && !UNITY_EDITOR
  SonarCopyText(text ?? "");
#else
  GUIUtility.systemCopyBuffer = text ?? "";
#endif
 }
 public static void PickFile(GameObject receiver,string method,string url,string accept,int maxBytes,string label){
#if UNITY_WEBGL && !UNITY_EDITOR
  SonarPickFile(receiver.name,method,url,accept,maxBytes,label);
#else
  Debug.LogWarning("File picker is Web-only in runtime.");
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
