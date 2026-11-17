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
 [DllImport("__Internal")] static extern void SonarDownloadUrl(string url,string fileName);
 [DllImport("__Internal")] static extern void SonarAudioPreviewPlay(string go,string method,string url,string key);
 [DllImport("__Internal")] static extern void SonarAudioPreviewPause();
 [DllImport("__Internal")] static extern void SonarAudioPreviewStop();
#endif
 // Downloads a same-origin URL as a file (the server's Content-Disposition name wins when fileName is empty).
 public static void DownloadUrl(string url,string fileName=""){
#if UNITY_WEBGL && !UNITY_EDITOR
  SonarDownloadUrl(url,fileName ?? "");
#else
  Application.OpenURL(url);
#endif
 }
 // Browser-side audio preview. receiver.method gets "PLAYING|key", "PAUSED|key", "ENDED|key" or "ERR|key|message".
 // Playing again with the same key resumes from the paused position; a different key stops the previous sound.
 public static void AudioPreviewPlay(GameObject receiver,string method,string url,string key){
#if UNITY_WEBGL && !UNITY_EDITOR
  SonarAudioPreviewPlay(receiver.name,method,url,key);
#else
  Debug.LogWarning("Audio preview is Web-only in runtime.");
  receiver.SendMessage(method,"ERR|"+key+"|Audio preview is available only in the Web build.",SendMessageOptions.DontRequireReceiver);
#endif
 }
 public static void AudioPreviewPause(){
#if UNITY_WEBGL && !UNITY_EDITOR
  SonarAudioPreviewPause();
#endif
 }
 public static void AudioPreviewStop(){
#if UNITY_WEBGL && !UNITY_EDITOR
  SonarAudioPreviewStop();
#endif
 }
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
