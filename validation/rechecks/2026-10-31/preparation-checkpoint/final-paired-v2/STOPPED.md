# Incomplete second paired run

This entire cohort is incomplete and is not counted as passed. Both seed46 arms reached the same dense-update checkpoint (272 functions,212 gradients,466848 rays) under the original limits. Streaming persistence used approximately2seconds at this point.

The PowerShell background driver aborted with `Unhandled exception. System.UnauthorizedAccessException: Access to the path is denied.` while launching/resuming its child process. No stack trace was returned. All remaining cases are absent. The exact normal suspended prefix is retained in `paused/seed46/suspended.preparation.json`.

A subsequent read/attempt on the already in-flight file conservatively charged its unaccounted reservation once, producing `Interrupted/UnsettledReservationCharged`; that operation is not a successful segment comparison. The `access-diagnostic` sibling copied the intact suspended prefix solely to investigate process access, and was stopped separately. It is not part of the declared cohort.

Do not infer a solver defect, a resolved permission cause, or a final qualified result from these partial checkpoints. No cap, physical permission or acceptance gate was relaxed. A fresh directory/runtime declaration is required for a replacement complete study.
