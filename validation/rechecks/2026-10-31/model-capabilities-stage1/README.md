# Evidence receipt

`verification.json` identifies the immutable base archive and its metadata correction overlay. All 6,883 base payloads were read back and hashed. Apply the overlay's summary and native-controls-summary files to obtain the current aggregates; capability-progress.json, native-controls.json, numerical data, source snapshots, settings and gates are unchanged. The initial summaries remain recorded beside the corrected files and in the base archive. This correction does not change any numerical verdict.

The previous 95-example report and prior result file are separately checked against their frozen manifest in `prior-audit-integrity.json`.

The final receipt identified in verification.json contains the current report files and document. It also records the 14 coarse-step return controls that remain different; the converged 0.025-step mapped states pass. Apply this receipt after the base and initial metadata overlay.
