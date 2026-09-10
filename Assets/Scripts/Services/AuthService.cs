using System;
using System.Collections;
using System.Text;
using Newtonsoft.Json;
using UnityEngine.Networking;

namespace SonarTask.Services {
public static class AuthService {
    [Serializable] public sealed class Reply { public bool ok; public string message; }

    static IEnumerator Post(string path, object body, Action<Reply> done) {
        var data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(body));
        using var r = new UnityWebRequest("/api" + path, "POST");
        r.uploadHandler = new UploadHandlerRaw(data);
        r.downloadHandler = new DownloadHandlerBuffer();
        r.SetRequestHeader("Content-Type", "application/json");
        yield return r.SendWebRequest();
        Reply x = null;
        try { x = JsonConvert.DeserializeObject<Reply>(r.downloadHandler.text); } catch { }
        done(x ?? new Reply { ok = false, message = string.IsNullOrEmpty(r.error) ? "Request failed" : r.error });
    }

    static IEnumerator LocalLogin(string password, Action<Reply> done) {
        bool ok = string.Equals(password ?? "", AppState.LocalWebDevPassword, StringComparison.Ordinal);
        done(new Reply {
            ok = ok,
            message = ok ? "Local WebGL development login accepted." : "Incorrect local development password."
        });
        yield break;
    }

    public static IEnumerator Login(string password, Action<Reply> done) =>
        AppState.IsLocalWebDevelopment ? LocalLogin(password, done) : Post("/auth/login", new { password }, done);

    public static IEnumerator Reauth(string password, Action<Reply> done) =>
        AppState.IsLocalWebDevelopment ? LocalLogin(password, done) : Post("/auth/reauth", new { password }, done);

    public static IEnumerator ChangePassword(string currentPassword, string newPassword, Action<Reply> done) {
        if (AppState.IsLocalWebDevelopment) {
            done(new Reply { ok = false, message = "Password changes are disabled in local WebGL development mode." });
            yield break;
        }
        yield return Post("/auth/change-password", new { currentPassword, newPassword }, done);
    }
}
}
