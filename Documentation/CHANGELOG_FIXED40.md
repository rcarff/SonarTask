# FIXED40: score reset per phase and phase results

- New optional `ResetScoreWhenPhaseStarts` (`Yes`/`No`, default `Yes`) in `ExperimentSettings`, overridable per phase. When the resolved value is `Yes`, the Correct/Partial/Incorrect tally in the feedback section resets to 0 when that phase starts. `No` carries the tally over from the previous phase.
- **Behavior change:** previously the tally always accumulated across phases. Packages that relied on that must set `ResetScoreWhenPhaseStarts` to `No`.
- The setting affects only the on-screen tally. CSV output, the CSV header and the overall end-of-experiment totals are unchanged.
- With `ShowResultsAtEnd` = `Yes`, the end popup (and the external-study completion popup) of a multi-phase experiment now lists a `Phase Results` line per phase, showing the tally as displayed when that phase ended plus the phase's signal count, followed by `Overall Results`. Single-phase experiments show the original `Results` block.
- Popups with long messages now use a larger box and shrink their text to fit.
- Server package validation accepts the new Yes/No field and rejects invalid values. Schema updated.
