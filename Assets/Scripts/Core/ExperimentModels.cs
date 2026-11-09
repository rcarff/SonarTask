using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace SonarTask.Core {
[JsonConverter(typeof(YesNoConverter))] public enum YesNo { No, Yes }
public sealed class YesNoConverter : JsonConverter {
 public override bool CanConvert(Type t) => t == typeof(YesNo) || t == typeof(YesNo?);
 public override object ReadJson(JsonReader r,Type t,object existing,JsonSerializer serializer) {
  if(r.TokenType==JsonToken.Null && t==typeof(YesNo?))return null;
  string value=Convert.ToString(r.Value,System.Globalization.CultureInfo.InvariantCulture);
  if(string.Equals(value,"Yes",StringComparison.OrdinalIgnoreCase)||string.Equals(value,"True",StringComparison.OrdinalIgnoreCase)||r.TokenType==JsonToken.Integer&&value=="1")return YesNo.Yes;
  if(string.Equals(value,"No",StringComparison.OrdinalIgnoreCase)||string.Equals(value,"False",StringComparison.OrdinalIgnoreCase)||r.TokenType==JsonToken.Integer&&value=="0")return YesNo.No;
  throw new JsonSerializationException("Expected Yes/No, True/False, boolean or 1/0 at " + r.Path);
 }
 public override void WriteJson(JsonWriter w,object value,JsonSerializer serializer){if(value==null)w.WriteNull();else w.WriteValue((YesNo)value==YesNo.Yes?"Yes":"No");}
}
[Serializable] public sealed class ExperimentDefinition { public int SchemaVersion=1; public string Name=""; public string Version="1.0"; public string InstructionsFile="instructions.md"; public ExperimentSettings ExperimentSettings=new(); public Dictionary<string,PhaseDefinition> Phases=new(); }
[Serializable] public sealed class ExperimentSettings {
 public YesNo DeselectBTHSignalAfterClassification=YesNo.Yes,DisablePauseAndExit=YesNo.No,AutoShowInstructions=YesNo.No; public float? LoFarJitterFactor,LoFarBackgroundNoise;
 public List<string> PhaseOrder=new(); public Dictionary<string,SignalDefinition> SignalDefinitions=new(); public YesNo AutoStart=YesNo.Yes;
 public string BackgroundAudio=""; public float BackgroundAudioVolume=.8f; public YesNo TrainingAreaEnabled=YesNo.Yes; public YesNo FeedbackEnabled=YesNo.Yes; public YesNo ClassificationTallyEnabled=YesNo.Yes; public YesNo AlertsEnabled=YesNo.Yes; public YesNo BTHOverlapAudio=YesNo.No; public YesNo SignalAudioEnabled=YesNo.Yes; public YesNo HideLofarSignalsIfNoSelectedBTH=YesNo.No;
 public float JitterFactor=.5f; public float BackgroundNoise=.18f; public BTHView BTHView=new(); public List<LOFARView> LOFARViews=new(); public float SignalDurationSec=30; public float AlertOffsetSec=5; public float AlertDurationSec=10; public long RandomizationSeed; public YesNo ShowElapsedTime=YesNo.Yes; public YesNo ShowRemainingTime=YesNo.Yes; public YesNo ShowResultsAtEnd=YesNo.No; public float WaterfallScanRateHz=20; public string WaterfallPalette="Green";
}
[Serializable] public sealed class BTHView { public int DegStart=180,DegEnd=180,DegTicksEvery=90,DegLabelsEvery=90,TimeStart=0,TimeEnd=30,TimeTicksEvery=15,TimeLabelsEvery=15; public float SelectionWidth=15; }
[Serializable] public sealed class LOFARView { public int HzStart=0,HzEnd=1750,HzTicksEvery=50,HzLabelsEvery=250,TimeStart=0,TimeEnd=30,TimeTicksEvery=15,TimeLabelsEvery=15; }
[Serializable] public sealed class SignalStrength { public float Width=20,Alpha=.6f; }
[Serializable] public sealed class FreqTuple { public float Frequency,Width=5,Alpha=.8f; }
[Serializable] public sealed class SignalDefinition { public float? LoFarJitterFactor,LoFarBackgroundNoise; public string Name=""; public YesNo TrainingAllowed=YesNo.Yes; public string AudioFile=""; public float AudioVolume=.8f; public string Classification="Unknown"; public SignalStrength SignalStrength=new(); public List<FreqTuple> FreqTuples=new(); }
[Serializable] public sealed class SignalOverride { public float? LoFarJitterFactor,LoFarBackgroundNoise; public string Name=""; public YesNo? TrainingAllowed; public string AudioFile; public float? AudioVolume; public string Classification; public SignalStrength SignalStrength; public List<FreqTuple> FreqTuples; }
[Serializable] public sealed class SignalInstance { public float? LoFarJitterFactor,LoFarBackgroundNoise; public string Signal=""; public float AppearSec,Bearing,RateDegSec; public float? DurationSec; public string ID,AudioFile,Classification,AlertText,AlertType="VALID"; public float? AudioVolume,AlertOffsetSec,AlertDurationSec; public int? AlertBearing; public SignalStrength SignalStrength; public List<FreqTuple> FreqTuples; public YesNo? AlertEnabled; }
[Serializable] public sealed class AlertDefinition { public string Text="",Type="INVALID",SignalID=""; public float AppearSec; public int Bearing; public float? DurationSec; }
[Serializable] public sealed class PhaseDefinition {
 public YesNo? DeselectBTHSignalAfterClassification,DisablePauseAndExit,AutoShowInstructions; public float? LoFarJitterFactor,LoFarBackgroundNoise; public string InstructionsFile;
 public YesNo? AutoStart,TrainingAreaEnabled,FeedbackEnabled,ClassificationTallyEnabled,AlertsEnabled,BTHOverlapAudio,SignalAudioEnabled,HideLofarSignalsIfNoSelectedBTH,ShowElapsedTime,ShowRemainingTime; public string BackgroundAudio,WaterfallPalette; public float? BackgroundAudioVolume,JitterFactor,BackgroundNoise,SignalDurationSec,AlertOffsetSec,AlertDurationSec,WaterfallScanRateHz; public BTHView BTHView; public List<LOFARView> LOFARViews; public List<SignalOverride> SignalDefinitions=new(); public List<SignalInstance> Signals=new(); public List<AlertDefinition> Alerts=new();
}
public sealed class ExperimentSummary { public string PackageName,Name,Version; }
public sealed class ResolvedExperiment { public ExperimentDefinition Source; public YesNo ShowResultsAtEnd=YesNo.No; public List<ResolvedPhase> Phases=new(); }
public sealed class ResolvedPhase {
 public bool DeselectBTHSignalAfterClassification=true,DisablePauseAndExit,AutoShowInstructions; public float LoFarJitterFactor,LoFarBackgroundNoise; public string InstructionsFile;
 public string Name,BackgroundAudio,WaterfallPalette; public int Index; public float DurationSec,BackgroundAudioVolume,JitterFactor,BackgroundNoise,SignalDurationSec,AlertOffsetSec,AlertDurationSec,WaterfallScanRateHz; public YesNo AutoStart,TrainingAreaEnabled,FeedbackEnabled,ClassificationTallyEnabled,AlertsEnabled,BTHOverlapAudio,SignalAudioEnabled,HideLofarSignalsIfNoSelectedBTH,ShowElapsedTime,ShowRemainingTime; public BTHView BTHView; public List<LOFARView> LOFARViews; public Dictionary<string,SignalDefinition> AvailableSignals=new(); public List<SignalDefinition> PhaseSignalDefinitions=new(); public List<ResolvedSignal> Signals=new(); public List<ResolvedAlert> Alerts=new(); public List<string> ClassificationOrder=new();
}
public sealed class ResolvedSignal { public float? LoFarJitterFactor,LoFarBackgroundNoise; public string SignalType,ID,FriendlyName,AudioFile,Classification,AlertText,AlertType; public float AppearSec,Bearing,RateDegSec,DurationSec,AudioVolume,AlertAppearSec,AlertDurationSec; public int AlertBearing; public SignalStrength SignalStrength; public List<FreqTuple> FreqTuples; public bool TrainingAllowed,AlertEnabled; }
public sealed class ResolvedAlert { public string Text,Type,SignalID; public float AppearSec,DurationSec; public int Bearing; }
}
