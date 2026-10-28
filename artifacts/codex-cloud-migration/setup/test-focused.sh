#!/usr/bin/env bash
set -uo pipefail
source /workspace/opticalsystem-cloud/activate.sh
cd /workspace/opticalsystem
out="$PWD/artifacts/codex-cloud-migration"
mkdir -p "$out/logs" "$out/test-results"
failed=0
run() {
  local name=$1 project=$2 filter=$3
  shift 3
  local args=(dotnet test "$project" -c Release --no-build --no-restore --filter "$filter" --logger "trx;LogFileName=$name.trx" --results-directory "$out/test-results" /m:1 /nr:false)
  printf '%q ' "${args[@]}" > "$out/logs/$name.command"
  printf '\n' >> "$out/logs/$name.command"
  "${args[@]}" > "$out/logs/$name.log" 2>&1
  local status=$?
  printf '%s\n' "$status" > "$out/logs/$name.exit"
  if [ "$status" -ne 0 ]; then failed=1; fi
}
run cloud-main-smoke tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj 'FullyQualifiedName~ParaxialTransferMatrixTests|FullyQualifiedName~SingleRayTraceAnalysisTests|FullyQualifiedName~OpticalDrawingStandardSelectionTests|FullyQualifiedName~LegacyGb1991DrawingTests'
run cloud-portable-contracts tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj 'FullyQualifiedName~RayAuditContractTests|FullyQualifiedName~RmsFieldSamplingContractTests|FullyQualifiedName~OpticalSystemDesign.ZemaxComparison.Tests.ComparisonTests'
run cloud-initial-light labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/OpticalSystemDesign.InitialStructure.Tests.csproj 'FullyQualifiedName~InitialStructureLabTests.FrozenBenchmarkSpecificationsAreValidAndUnique|FullyQualifiedName~InitialStructureLabTests.FlatRootIsAnExactIndependentPlaneParallelSystem|FullyQualifiedName~InitialStructureLabTests.FirstOrderSeedPreservesFlatRootLineageAndBendsSurfaces|FullyQualifiedName~AutoLensManagedTests.AdamUsesExactGradientsAndSerializedMomentsResumeIdentically|FullyQualifiedName~AutoLensManagedTests.CosineRestartHasCorrectPeriod|FullyQualifiedName~AutoLensManagedTests.UnsupportedConfigurationDoesNotSilentlyFallBack|FullyQualifiedName~AutoLensManagedTests.DefaultNativeSpecificationFingerprintDoesNotAcquireAutoLensSettings'
run cloud-coating-light labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/OpticalSystemDesign.CoatingDesign.Tests.csproj 'FullyQualifiedName~ThinFilmPhysicsTests|FullyQualifiedName~WorkflowTests.RealExamplesGenerateOptimizeAndVerifyWithoutChangingInput|FullyQualifiedName~WorkflowTests.SaveOpenRecalculatesIdenticallyAndExportPreservesPrecision|FullyQualifiedName~WorkflowTests.ToleranceUsesSameSolverAndDeterministicSharedSampler'
exit "$failed"
