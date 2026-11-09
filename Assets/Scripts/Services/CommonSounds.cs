using System;
using System.IO;
namespace SonarTask.Services {
public static class CommonSounds {
 public static string Normalize(string name) {
  if(name!=null && name.StartsWith("Sounds/",StringComparison.OrdinalIgnoreCase))name=name.Substring(7);
  if(string.IsNullOrWhiteSpace(name)||name.Contains("..")||name.IndexOfAny("/\\<>:\"|?*".ToCharArray())>=0||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||name.Trim()!=name||name.EndsWith(".")||Array.Exists(name.ToCharArray(),char.IsControl))throw new ArgumentException("Common sounds require a flat file name.");
  return name;
 }
 public static bool IsAudio(string name)=>string.Equals(Path.GetExtension(name),".wav",StringComparison.OrdinalIgnoreCase)||string.Equals(Path.GetExtension(name),".mp3",StringComparison.OrdinalIgnoreCase);
}
}
