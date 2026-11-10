using System.Collections.Generic;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using UnityEngine;

namespace SonarTask.Services {
public sealed class WebSocketLslTransport : ILslTransport {
    readonly Queue<string> commands = new();
    LslStreamConfig config;
    public bool IsConnected { get; private set; }
    public double Clock => Time.realtimeSinceStartupAsDouble;
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void SonarLslConnect(string target, string hello, int port);
    [DllImport("__Internal")] static extern void SonarLslSend(string json);
    [DllImport("__Internal")] static extern void SonarLslBinary(int stream, float[] values, int count);
    [DllImport("__Internal")] static extern void SonarLslClose();
#endif
    public void Open(LslStreamConfig info) {
        config = info;
#if UNITY_WEBGL && !UNITY_EDITOR
        SonarLslConnect("SONAR LSL", JsonConvert.SerializeObject(info), info.bridgePort);
#endif
    }
    public void Receive(string json) { if (commands.Count < 256) commands.Enqueue(json); }
    public void SetConnected(bool value) => IsConnected = value;
    public void PushMarker(string json, double timestamp) => Send(new { kind = "marker", json, age = System.Math.Max(0, Clock - timestamp) });
    public void PushState(float[] sample) { if (IsConnected) Send(new { kind = "state", sample }); }
    void Send(object message) {
#if UNITY_WEBGL && !UNITY_EDITOR
        SonarLslSend(JsonConvert.SerializeObject(message));
#endif
    }
    public void PushWaterfall(WaterfallStream stream, float[] header, IReadOnlyList<float[]> lines) {
        if (!IsConnected || !config.waterfalls) return;
        var sample = LslSamples.Pack(config.bins, stream == WaterfallStream.Bth ? 1 : config.views, header, lines);
#if UNITY_WEBGL && !UNITY_EDITOR
        SonarLslBinary((int)stream, sample, sample.Length);
#endif
    }
    public bool TryReadCommand(out string command) {
        command = commands.Count > 0 ? commands.Dequeue() : null;
        return command != null;
    }
    public void Close() {
#if UNITY_WEBGL && !UNITY_EDITOR
        SonarLslClose();
#endif
        IsConnected = false;
        commands.Clear();
    }
}
}
