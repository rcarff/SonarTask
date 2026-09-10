using System.Runtime.InteropServices;
using UnityEngine;

namespace SonarTask.Services
{
    // WebGL browsers require AudioContext.resume() to run from a real browser gesture.
    // The JS plugin installs capture-phase listeners so Unity audio is unlocked on the
    // first pointer/touch/key interaction without intercepting the user's UI action.
    public sealed class WebAudioUnlock : MonoBehaviour
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void SonarInstallAudioUnlock();
#endif

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
#if UNITY_WEBGL && !UNITY_EDITOR
            SonarInstallAudioUnlock();
#endif
        }
    }
}
