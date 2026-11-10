using System;
using System.Collections.Generic;

namespace SonarTask.Services {
public enum WaterfallStream { Bth = 1, Lofar = 2 }
public sealed class LslStreamConfig {
    public string sourceId, appVersion, platform;
    public int bins, views, bridgePort;
    public float stateRate, waterfallRate;
    public bool waterfalls;
}
public interface ILslTransport {
    bool IsConnected { get; }
    double Clock { get; }
    void Open(LslStreamConfig info);
    void PushMarker(string json, double timestamp);
    void PushState(float[] sample);
    void PushWaterfall(WaterfallStream stream, float[] header, IReadOnlyList<float[]> lines);
    bool TryReadCommand(out string command);
    void Close();
}
public sealed class NullLslTransport : ILslTransport {
    public bool IsConnected => false;
    public double Clock => 0;
    public void Open(LslStreamConfig info) { }
    public void PushMarker(string json, double timestamp) { }
    public void PushState(float[] sample) { }
    public void PushWaterfall(WaterfallStream stream, float[] header, IReadOnlyList<float[]> lines) { }
    public bool TryReadCommand(out string command) { command = null; return false; }
    public void Close() { }
}
public static class LslSamples {
    public static float[] Pack(int bins, int views, float[] header, IReadOnlyList<float[]> lines) {
        var result = new float[header.Length + bins * views];
        Array.Copy(header, result, header.Length);
        for (int v = 0; v < views; v++) {
            var line = v < lines.Count ? lines[v] : null;
            for (int b = 0; b < bins; b++) {
                float sum = 0;
                int start = line == null ? 0 : b * line.Length / bins;
                int end = line == null ? 0 : (b + 1) * line.Length / bins;
                for (int j = start; j < end; j++) sum += line[j];
                result[header.Length + v * bins + b] = end > start ? sum / (end - start) : float.NaN;
            }
        }
        return result;
    }
}
}
