using UnityEngine; using SonarTask.UI; using SonarTask.Services;
namespace SonarTask.Bootstrap {
public sealed class SonarBootstrap:MonoBehaviour {
 void Awake(){if(!AppState.IsWeb)DesktopStorage.Initialize();UIFactory.EnsureEventSystem();UIFactory.EnsurePresentationCamera();if(!FindFirstObjectByType<WebAudioUnlock>()){var audioUnlock=new GameObject("SONAR Web Audio Unlock");DontDestroyOnLoad(audioUnlock);audioUnlock.AddComponent<WebAudioUnlock>();}if(FindFirstObjectByType<ScreenManager>())return;var go=new GameObject("SONAR Screen Manager");DontDestroyOnLoad(go);go.AddComponent<ScreenManager>();}
}
}
