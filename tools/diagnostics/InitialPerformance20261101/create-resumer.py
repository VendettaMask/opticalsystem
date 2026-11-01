"""Reuse the normal-stop/new-process verifier against the exact frozen performance kernel."""
from pathlib import Path
import sys
repo=Path(sys.argv[1]).resolve();out=repo/'tools/diagnostics/InitialPerformanceResume20261101';out.mkdir(exist_ok=True)
(out/'Program.cs').write_bytes((repo/'tools/diagnostics/PreparationResume20261031/Program.cs').read_bytes())
(out/'InitialPerformanceResume20261101.csproj').write_bytes((repo/'tools/diagnostics/PreparationCheckpointAudit20261031/PreparationCheckpointAudit20261031.csproj').read_bytes())
print('Created exact frozen-kernel normal-stop/new-process resume verifier.')
