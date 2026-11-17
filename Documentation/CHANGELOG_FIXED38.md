# FIXED38: instruction videos and sized media

- Instruction Markdown can embed package videos (`.mp4`, `.m4v`, `.webm`, `.mov`) with the image syntax: `![alt](Videos/clip.mp4)`. H.264/AAC `.mp4` is the recommended cross-platform format.
- Images and videos accept an optional attribute block: `{width=640|50% height=360 align=left|center|right}`. Videos also accept `autoplay loop muted nocontrols`. Media stay in the document flow and re-fit when the column width changes. Existing image lines render unchanged.
- The video controls are a geometry-drawn Play/Pause button with a text label, a seek bar, and an elapsed/total time readout. Clicking the video toggles playback. Closing the instructions stops playback.
- The in-task instructions overlay records `Video_Played`, `Video_Paused`, `Video_Seeked` and `Video_Ended`, with the path in Alert_Type and the position in seconds in Alert_Text. The CSV header is unchanged.
- Server: package assets are served with HTTP Range support, which browsers need for video seeking, and with video/GIF content types.
- Unity: enabled the built-in Video module (`com.unity.modules.video`).
