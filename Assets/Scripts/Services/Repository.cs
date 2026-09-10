using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using SonarTask.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace SonarTask.Services {
public interface IExperimentRepository {
    IEnumerator List(Action<List<ExperimentSummary>> ok, Action<string> fail);
    IEnumerator LoadDefinition(string packageName, Action<ExperimentDefinition> ok, Action<string> fail);
    IEnumerator LoadTextAsset(string packageName,string relativePath,Action<string> ok,Action<string> fail);
    string AssetUrl(string packageName,string relativePath);
    IEnumerator HasCompleted(string subject,string packageName,Action<bool> ok,Action<string> fail);
}

public static class RepositoryFactory {
    public static IExperimentRepository Experiments => AppState.IsLocalWebDevelopment ? new LocalWebExperimentRepository() : (AppState.IsWeb ? new WebExperimentRepository() : new DesktopExperimentRepository());
    public static string DesktopDataRoot => DesktopStorage.DataRoot;
    public static string SafePackagePath(string package,string relative){
        if(string.IsNullOrWhiteSpace(package))throw new ArgumentException("Package is required.");
        var root=Path.GetFullPath(Path.Combine(DesktopDataRoot,"Experiments",package));
        var full=Path.GetFullPath(Path.Combine(root,relative??""));
        var cmp = Application.platform == RuntimePlatform.WindowsPlayer ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if(!full.StartsWith(root+Path.DirectorySeparatorChar,cmp)&&!string.Equals(full,root,cmp))throw new InvalidOperationException("Asset path escapes experiment package.");
        return full;
    }
}

public sealed class DesktopExperimentRepository:IExperimentRepository {
    string Root=>Path.Combine(RepositoryFactory.DesktopDataRoot,"Experiments");
    public IEnumerator List(Action<List<ExperimentSummary>> ok,Action<string> fail){
        try{Directory.CreateDirectory(Root);var l=new List<ExperimentSummary>();foreach(var d in Directory.GetDirectories(Root)){var f=Path.Combine(d,"experiment.json");if(!File.Exists(f))continue;try{var x=JsonConvert.DeserializeObject<ExperimentDefinition>(File.ReadAllText(f));l.Add(new ExperimentSummary{PackageName=Path.GetFileName(d),Name=x?.Name??Path.GetFileName(d),Version=x?.Version??""});}catch(Exception e){Debug.LogWarning($"Skipping {d}: {e.Message}");}}l.Sort((a,b)=>string.Compare(a.Name,b.Name,StringComparison.OrdinalIgnoreCase));ok(l);}catch(Exception e){fail(e.Message);}yield break;
    }
    public IEnumerator LoadDefinition(string packageName,Action<ExperimentDefinition> ok,Action<string> fail){try{var f=RepositoryFactory.SafePackagePath(packageName,"experiment.json");var raw=File.ReadAllText(f);AppState.LoadedDefinitionJson=raw;var d=JsonConvert.DeserializeObject<ExperimentDefinition>(raw);var errs=ExperimentValidator.Validate(d);if(errs.Count>0)throw new InvalidDataException(string.Join("\n",errs));ok(d);}catch(Exception e){fail(e.Message);}yield break;}
    public IEnumerator LoadTextAsset(string packageName,string relativePath,Action<string> ok,Action<string> fail){try{ok(File.ReadAllText(RepositoryFactory.SafePackagePath(packageName,relativePath)));}catch(Exception e){fail(e.Message);}yield break;}
    public string AssetUrl(string packageName,string relativePath)=>"file://"+RepositoryFactory.SafePackagePath(packageName,relativePath).Replace("\\","/");
    public IEnumerator HasCompleted(string subject,string packageName,Action<bool> ok,Action<string> fail){try{var root=Path.Combine(RepositoryFactory.DesktopDataRoot,"Results");Directory.CreateDirectory(root);bool hit=false;foreach(var f in Directory.GetFiles(root,"*.meta.json")){try{var m=JsonConvert.DeserializeObject<RunMetadata>(File.ReadAllText(f));if(m!=null&&m.SubjectId==subject&&m.ExperimentPackage==packageName&&m.Completed){hit=true;break;}}catch{}}ok(hit);}catch(Exception e){fail(e.Message);}yield break;}
}

public sealed class LocalWebExperimentRepository:IExperimentRepository {
    const string Root = "Experiments/";

    static string EncodedPath(string value) {
        var parts = (value ?? "").Replace('\\','/').Split('/');
        var encoded = new List<string>();
        foreach (var part in parts) {
            if (string.IsNullOrWhiteSpace(part) || part == ".") continue;
            if (part == "..") throw new InvalidOperationException("Asset path escapes experiment package.");
            encoded.Add(UnityWebRequest.EscapeURL(part));
        }
        return string.Join("/", encoded);
    }

    IEnumerator GetText(string url, Action<string> ok, Action<string> fail) {
        using var r = UnityWebRequest.Get(url);
        yield return r.SendWebRequest();
        if (r.result == UnityWebRequest.Result.Success) ok(r.downloadHandler.text);
        else fail($"Local WebGL development asset failed: {url}: {r.error}");
    }

    public IEnumerator List(Action<List<ExperimentSummary>> ok, Action<string> fail) {
        using var r = UnityWebRequest.Get(Root + "index.json");
        yield return r.SendWebRequest();
        if (r.result != UnityWebRequest.Result.Success) {
            fail("Local WebGL experiment manifest was not found. Use Unity Build and Run so the SONAR post-build step can copy the Experiments folder. " + r.error);
            yield break;
        }
        try {
            var list = JsonConvert.DeserializeObject<List<ExperimentSummary>>(r.downloadHandler.text) ?? new List<ExperimentSummary>();
            ok(list);
        } catch (Exception e) { fail(e.Message); }
    }

    public IEnumerator LoadDefinition(string packageName, Action<ExperimentDefinition> ok, Action<string> fail) {
        string url;
        try { url = Root + EncodedPath(packageName) + "/experiment.json"; }
        catch (Exception e) { fail(e.Message); yield break; }
        using var r = UnityWebRequest.Get(url);
        yield return r.SendWebRequest();
        if (r.result != UnityWebRequest.Result.Success) { fail(r.error); yield break; }
        try {
            AppState.LoadedDefinitionJson = r.downloadHandler.text;
            var d = JsonConvert.DeserializeObject<ExperimentDefinition>(r.downloadHandler.text);
            var errs = ExperimentValidator.Validate(d);
            if (errs.Count > 0) throw new InvalidDataException(string.Join("\n", errs));
            ok(d);
        } catch (Exception e) { fail(e.Message); }
    }

    public IEnumerator LoadTextAsset(string packageName, string relativePath, Action<string> ok, Action<string> fail) {
        string url;
        try { url = AssetUrl(packageName, relativePath); }
        catch (Exception e) { fail(e.Message); yield break; }
        yield return GetText(url, ok, fail);
    }

    public string AssetUrl(string packageName, string relativePath) => Root + EncodedPath(packageName) + "/" + EncodedPath(relativePath);

    public IEnumerator HasCompleted(string subject, string packageName, Action<bool> ok, Action<string> fail) {
        ok(false); // Local browser tests intentionally do not use production result state.
        yield break;
    }
}

public sealed class WebExperimentRepository:IExperimentRepository {
    const string Api="/api";
    IEnumerator Get<T>(string url,Action<T> ok,Action<string> fail){using var r=UnityWebRequest.Get(url);yield return r.SendWebRequest();if(r.result!=UnityWebRequest.Result.Success){fail(r.downloadHandler.text+" "+r.error);yield break;}try{ok(JsonConvert.DeserializeObject<T>(r.downloadHandler.text));}catch(Exception e){fail(e.Message);}}
    public IEnumerator List(Action<List<ExperimentSummary>> ok,Action<string> fail)=>Get(Api+"/experiments",ok,fail);
    public IEnumerator LoadDefinition(string packageName,Action<ExperimentDefinition> ok,Action<string> fail){using var r=UnityWebRequest.Get(Api+"/experiments/"+UnityWebRequest.EscapeURL(packageName)+"/definition");yield return r.SendWebRequest();if(r.result!=UnityWebRequest.Result.Success){fail(r.downloadHandler.text+" "+r.error);yield break;}try{AppState.LoadedDefinitionJson=r.downloadHandler.text;var d=JsonConvert.DeserializeObject<ExperimentDefinition>(r.downloadHandler.text);var errs=ExperimentValidator.Validate(d);if(errs.Count>0)throw new InvalidDataException(string.Join("\n",errs));ok(d);}catch(Exception e){fail(e.Message);}}
    public IEnumerator LoadTextAsset(string packageName,string relativePath,Action<string> ok,Action<string> fail){using var r=UnityWebRequest.Get(AssetUrl(packageName,relativePath));yield return r.SendWebRequest();if(r.result==UnityWebRequest.Result.Success)ok(r.downloadHandler.text);else fail(r.downloadHandler.text+" "+r.error);}
    public string AssetUrl(string packageName,string relativePath)=>Api+"/experiments/"+UnityWebRequest.EscapeURL(packageName)+"/assets/"+string.Join("/",(relativePath??"").Split('/')).Replace("..","");
    public IEnumerator HasCompleted(string subject,string packageName,Action<bool> ok,Action<string> fail)=>Get<CompletionReply>(Api+"/results/completed?subject="+UnityWebRequest.EscapeURL(subject)+"&experiment="+UnityWebRequest.EscapeURL(packageName),x=>ok(x.completed),fail);
    [Serializable] sealed class CompletionReply{public bool completed;}
}
}
