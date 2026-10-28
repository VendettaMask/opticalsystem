# Optical System Design cloud development

Use the existing isolated checkout at `/workspace/opticalsystem`. Do not create a Git worktree unless the user explicitly requests one. GitHub is the source of code and version history. Never import workstation uncommitted/private files or credentials. Use work branches and pull requests for development; do not push changes directly to main, publish releases, edit OTA feeds, sign packages, or regenerate frozen reference data as part of startup.

Before work, read AGENTS.md, README.md, the current sections of docs/CURRENT_STATUS.md and .github/workflows/ci.yml. Preserve all tracked artifacts, validation data, fonts, samples and resources even when their directories are ignored. Do not clean the checkout with git clean or overwrite existing changes.

## Activate retained tools

Run in bash:

```bash
source /workspace/opticalsystem-cloud/activate.sh
cd /workspace/opticalsystem
git status --short --untracked-files=all
git log -1 --format=fuller
git remote -v
git rev-parse --is-shallow-repository
dotnet --info
pwsh --version
```

The snapshot retains .NET SDK 10.0.300, PowerShell 7.6.9, Git, verified download archives and NuGet packages. global.json selects 10.0.300/latestPatch without preview SDKs. Activation sets DOTNET_ROOT, PATH, DOTNET_CLI_HOME, NUGET_PACKAGES and writable XDG data/config/cache paths without replacing HOME or the inherited proxy/CA configuration. If a retained tool is absent, rerun the saved install script. No application server needs to survive publication or be restarted for this build/test workflow.

The validated base is Debian 13 x64 with Fontconfig, FreeType, HarfBuzz, ICU, OpenSSL and X11 libraries. Do not disable TLS, NuGet auditing, signature checks or artifact checksums. Keep the managed HTTPS proxy for Git and package downloads. Networked commands and the test runner may need the tool's normal sandbox escalation to access the proxy or local VSTest sockets.

## Build the complete product

From `/workspace/opticalsystem`:

```bash
dotnet restore OpticalSystemDesign.slnx --locked-mode -p:ContinuousIntegrationBuild=true
dotnet build OpticalSystemDesign.slnx -c Release --no-restore /m:1 /nr:false -p:ContinuousIntegrationBuild=true
```

The solution includes the shared Core, desktop App, mandatory InitialStructure and CoatingDesign modules, tools and all four test projects. Use the normal output directories. Do not replace this with a partial solution build.

## Bounded readiness checks

Use a fresh result directory for each run, for example `artifacts/codex-cloud-migration/reuse-$(date -u +%Y%m%dT%H%M%SZ)`; retain logs and underlying exit statuses. Run the requested main smoke first:

```bash
results="artifacts/codex-cloud-migration/reuse-$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$results"
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~ParaxialTransferMatrixTests|FullyQualifiedName~SingleRayTraceAnalysisTests|FullyQualifiedName~OpticalDrawingStandardSelectionTests|FullyQualifiedName~LegacyGb1991DrawingTests' --logger 'trx;LogFileName=cloud-main-smoke.trx' --results-directory "$results" /m:1 /nr:false
dotnet test tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~RayAuditContractTests|FullyQualifiedName~RmsFieldSamplingContractTests|FullyQualifiedName~OpticalSystemDesign.ZemaxComparison.Tests.ComparisonTests' --logger 'trx;LogFileName=cloud-portable-contracts.trx' --results-directory "$results" /m:1 /nr:false
dotnet test labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/OpticalSystemDesign.InitialStructure.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~InitialStructureLabTests.FrozenBenchmarkSpecificationsAreValidAndUnique|FullyQualifiedName~InitialStructureLabTests.FlatRootIsAnExactIndependentPlaneParallelSystem|FullyQualifiedName~InitialStructureLabTests.FirstOrderSeedPreservesFlatRootLineageAndBendsSurfaces|FullyQualifiedName~AutoLensManagedTests.AdamUsesExactGradientsAndSerializedMomentsResumeIdentically|FullyQualifiedName~AutoLensManagedTests.CosineRestartHasCorrectPeriod|FullyQualifiedName~AutoLensManagedTests.UnsupportedConfigurationDoesNotSilentlyFallBack|FullyQualifiedName~AutoLensManagedTests.DefaultNativeSpecificationFingerprintDoesNotAcquireAutoLensSettings' --logger 'trx;LogFileName=cloud-initial-light.trx' --results-directory "$results" /m:1 /nr:false
dotnet test labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/OpticalSystemDesign.CoatingDesign.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~ThinFilmPhysicsTests|FullyQualifiedName~WorkflowTests.RealExamplesGenerateOptimizeAndVerifyWithoutChangingInput|FullyQualifiedName~WorkflowTests.SaveOpenRecalculatesIdenticallyAndExportPreservesPrecision|FullyQualifiedName~WorkflowTests.ToleranceUsesSameSolverAndDeterministicSharedSampler' --logger 'trx;LogFileName=cloud-coating-light.trx' --results-directory "$results" /m:1 /nr:false
pwsh -NoProfile -File tools/validation/GitFrozenIntegrity.ps1 -RepositoryRoot . -ManifestPath ./artifacts/validation/n02-layer-diagnosis-20261009/frozen-after.json -ExpectedAggregateSha256 3a5fde98e1c881700f00be1c62d6ab4ff6f4feacc90454046ed787e80bf28c10 -RelocationMapPath tools/validation/frozen-evidence-relocations.json -ExpectedRelocationMapSha256 6eefb25cbb1c35a120f73e8bbb761aac16759e7b8491b4606475caec8dfd4f6e -OutputPath "$results/frozen-integrity.json"
```

For the frozen checker regression scripts, use a fresh directory because they deliberately refuse existing outputs:

```bash
checks=$(mktemp -d /workspace/opticalsystem-cloud/frozen-checks.XXXXXX)
pwsh -NoProfile -File tools/validation/FrozenIntegrity.Tests.ps1 -OutputDirectory "$checks/working"
pwsh -NoProfile -File tools/validation/GitFrozenIntegrity.Tests.ps1 -OutputDirectory "$checks/git"
git diff --exit-code
git diff --cached --exit-code
git status --short --untracked-files=all
```

Initial validation at source commit efbd209562de92b04b9f6489a73bb425d5704087: full Release build 0 warnings/0 errors; main 21, portable contracts 65, InitialStructure 13, CoatingDesign 14 passed (113 total, 0 failed, 0 skipped); frozen working/committed 2668/2668 each; checker regressions 28+22 passed. These are setup-session results, not proof of fresh-task restoration. Recheck the actual source and execute readiness checks in a task launched from the published environment before claiming reuse.

Expand checks for actual failures. Do not automatically run all long searches, the entire lens matrix, or independent Cooke/Tessar residual investigations. Off-screen tests exercise Avalonia/Skia; native Windows/Mac GUI acceptance, licensed Zemax execution and actual CUDA/GPU validation remain separate. Do not create localhost preview links.

Review/save the configuration and publish through the environment UI. Publication and starting a new task from that published environment require product UI actions; never claim them complete from a draft-save result. Important new evidence should be committed to an appropriate work branch or uploaded through the repository's normal artifact workflow, not left solely in temporary cloud storage.
