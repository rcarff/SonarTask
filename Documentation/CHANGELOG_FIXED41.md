# FIXED41: Settings downloads, audio preview, list scrolling and fixed-height buttons

## Downloads and audio preview

- **Experiments:** each package row has a **Download** button. It saves `<Package>.zip`, which holds one top-level `<Package>/` folder with the complete package (experiment.json, instructions, Images/, Sounds/ and any other files). The ZIP has the same layout the upload expects, so it can be re-uploaded unchanged. The button is disabled in Local WebGL Test.
- **Common Audio Files:** each sound has a **Download** button and a **Play/Pause** preview button. The preview shows a triangle with "Play" or two bars with "Pause". Pausing and playing again resumes from the same point. Starting another sound stops the first one. Playback also stops when the sound ends, when you leave the view, and when a sound is uploaded or deleted. Both buttons also work in Local WebGL Test, using the bundled sounds.
- **Results:** a row's **Download** button now saves `<stem>.zip`, which holds every file for that run: the `.csv`, `.meta.json` and `.experiment.json`, plus any other file that shares the run's stem. Before, it downloaded the CSV alone. **Download Selected** uses the same stem matching.
- Downloads now go through a hidden `<a download>` link, not `window.open`, so they no longer leave blank browser tabs open.
- Deleting a common sound with a space in its name now works. The URL encoded the space as `+`, so the server could not find the file.

New API routes (all behind operator auth plus the elevated Settings token):
- `GET /api/admin/experiments/{package}/download`
- `GET /api/admin/sounds/{name}/download`
- `GET /api/admin/results/run/{fileName}`

`GET /api/sounds/{name}` now supports HTTP range requests, so the preview can resume and seek.

## Layout

- The Web Settings lists for Experiments, Common Audio Files, Results and External Studies are now scroll panes. Each pane fills the space left on the page and scrolls once its rows overflow, so long lists no longer spill past the Settings panel or push the Back button off screen.
- Buttons and button rows no longer stretch to fill spare vertical space. Before this fix, a short list stretched its rows (and their Delete / Download / Edit buttons) far taller than needed. This also applies to the Results action row, the External Study editor rows and the popup button row.
- External Study editor: the Groups / Sequences area uses the same scroll pane and fills the remaining space. A group card now grows when experiments are added to it, where before the extra experiment lines overlapped the next group.
- New `UIFactory.FixedRow` and `UIFactory.ScrollList` helpers for building these layouts.

No changes to the CSV header, result content, the schema or SQLite.
