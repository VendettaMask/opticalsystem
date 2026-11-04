# Final-Core full replay, 2026-11-04

Replays the original 3,024 rows, 95 source files, independent 170 afocal cases and 251 fixed-ray groups without changing prescriptions, sampling, native references or gates.

`Restore-Inputs.ps1 -RepositoryRoot <repository>` materializes only needed inputs from the immutable 2026-10-21 ZIP, checking its identity and each payload's original SHA256/length. `ExampleAccuracy20261031` rebases recorded storage paths to this checkout and accepts only newline-equivalent repository policy text; execution records the original policy bytes. Neither migration changes numerical values.

Build the main runner into `artifacts/validation/all-example-accuracy-20261104/runtime`. Run prepare/compute/compare/rays with the fresh `artifacts/validation/all-example-accuracy-20261104/study-final` root. Restore and verify references using the existing `ExampleAccuracy20261031/Verify-References.ps1 -StudyRoot <study>`.

Build the afocal runner against those same product assemblies. Preserve the executable comparison worker's runtimeconfig/deps, registry assets and Windows native dependencies as well as the managed libraries. The accepted afocal root is `artifacts/validation/all-example-afocal-20261104-final`; its runner junction reuses the original owned runtime. Run prepare-current/compute/audit, requiring all 170 worker processes to complete before accepting the audit. Earlier missing-startup-configuration attempts remain failed evidence.

`Verify-Cohort.ps1` checks compiled documents against portable PDBs and matching main/afocal product assemblies. `Write-Report.ps1 -Repository <repository>` rejects incomplete or mixed cohorts, compares all row identities against the 2026-11-03 full run, and separately checks the final 252-request and 156 cross-analysis scopes. Different scopes are not summed. Numerical results and reference convergence remain separate conclusions.
