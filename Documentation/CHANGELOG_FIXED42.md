# FIXED42: Completion codes and instruction list/quote rendering

## Completion code

- New experiment setting `ShowCompletionCode` (`Yes`/`No`, default `No`, in `ExperimentSettings` only). When it is `Yes` and the final phase ends naturally, the end popup shows a unique completion code (for example `K7QMX-3PRDA`) and a **Copy Code** button. The external-study **Study Complete** popup shows it too.
- Codes are 10 characters from an alphabet without look-alike characters, drawn from a cryptographic random generator. They are independent of the experiment seed, so the simulation stays deterministic.
- The code is saved in the run's `.meta.json` as `CompletionCode` and as a `Completion_Code` CSV event, with the code in `Alert_Text`. The CSV header is unchanged. Exited and aborted runs get no code.
- Server: `POST /api/results/runs/{runId}/finish` accepts an optional `completionCode`. It is written to `.meta.json` only for completed runs whose code matches `^[A-Z0-9-]+$` (32 characters at most). Server upload validation accepts `ShowCompletionCode` as a Yes/No field.

## Instruction Markdown

- Bullets and quote bars are now drawn shapes instead of the `•` and `▌` characters. WebGL builds have no system font fallback, so before this change the bullets were missing on Web and quotes had no bar.
- Bulleted and numbered lists are indented in all versions. Wrapped lines line up under the item text. Two leading spaces per level indent an item one more level, up to four levels.
- Consecutive `>` lines form one quote block with a single gray bar. A bare `>` line adds a small gap inside the block.

## Deployment

Rebuild the Web player and the API (`docker compose up -d --build`). Older clients still work with the new server, because `completionCode` is optional.
