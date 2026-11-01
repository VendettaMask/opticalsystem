"""Adapt the existing independent original-acceptance audit to performance arms."""
from pathlib import Path
import sys
repo=Path(sys.argv[1]).resolve();out=repo/'tools/diagnostics/InitialPerformanceAudit20261101';out.mkdir(exist_ok=True)
text=(repo/'tools/diagnostics/PreparationCheckpointAudit20261031/Program.cs').read_text(encoding='utf-8-sig')
text=text.replace('new[] { "continuous", "paused" }','new[] { "baseline", "candidate" }')
text=text.replace('using var protocol = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "protocol.json")));\n','')
start=text.index('    var segment=JsonDocument.Parse(')
end=text.index('    var work =',start)
text=text[:start]+'''    using var segment=JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(directory,"summary.json")));
    Check(segment.RootElement.GetProperty("actualRays").GetInt64()==checkpoint.Consumed.TracedRays,"actual ray settlement");
''' + text[end:]
(out/'Program.cs').write_text(text,encoding='utf8')
project=(repo/'tools/diagnostics/PreparationCheckpointAudit20261031/PreparationCheckpointAudit20261031.csproj').read_text(encoding='utf-8-sig')
(out/'InitialPerformanceAudit20261101.csproj').write_text(project,encoding='utf8')
print('Created original F8 physical/anchored-bound/native-aperture auditor for both performance arms.')
