# v11 automatic lens diameter evidence

The shared Core sizes explicitly selected lens surfaces from the sampled real-ray envelope. New desktop experiments enable this policy by default. Historical specifications with an omitted flag retain fixed diameters. See the [implementation and limits](../../../../docs/INITIAL_STRUCTURE_AUTOMATIC_DIAMETER_2026-09-26.md).

Full Release regression on 2026-09-26: formal **1309/1309**, comparison-tool **104/104**, laboratory **200/200**, zero failures/skips. Both solutions built into their default Debug/Release outputs with zero warnings/errors. Whitespace verification and `git diff --check` passed. Focused test runs overlap these totals. The earlier `core-envelope.trx` fixture failure is retained; duplicate primary wavelengths in the test setup were corrected before the final focused and full runs.

- `verification.json`: TRX counters/hashes, observed build results, default binary hashes and evidence links.
- `source-freeze.json`: source hashes for formal Core/tests and laboratory runtime/tests at verification.
- `native-smoke.json`: native macOS default search observation and the saved checkpoint identity. The UI retained ten accepted candidates; the 246 candidate-bearing trial records are not 246 distinct retained candidates.
- `native-example-candidate.json`: an accepted prescription copied from that completed native search. Its three lens semi-diameters are 4.439785, 4.948951 and 6.420539 mm; both faces of each lens share the physical/mechanical size. This is a saved Core result, not a separately implemented optical calculation.

Raw TRX files and the native checkpoint copy are under `artifacts/validation/auto-diameter-20260926`; their hashes are recorded here. Build success came from tool results, without separate saved build logs. Automated desktop and engine tests verify STAROPT export/reload consistency; native smoke verified search and preview, without a v11 native export click.

There is no new Zemax/OpticStudio numerical comparison or screenshot certification. The sampled envelope plus margin does not guarantee coverage between samples. No complete v11 60+15 acceptance protocol was run. The v10 protocol was interrupted at 43/60 (30 successful), before holdout; frozen fixed-diameter targets have not been silently changed, and the P5 gate remains pending.
