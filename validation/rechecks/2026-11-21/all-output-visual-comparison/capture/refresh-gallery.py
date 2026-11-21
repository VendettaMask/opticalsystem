"""Refresh the existing HTML/data with declared recaptures; never recapture optics."""
import json
import sys
from pathlib import Path
from gallery_corrections import apply_corrections

output = Path(sys.argv[1]).resolve()
data = json.loads((output / "gallery-data.json").read_text(encoding="utf-8"))
applied = apply_corrections(data, output)
template = (Path(__file__).parent / "gallery-template.html").read_text(encoding="utf-8")
(output / "gallery-data.json").write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
(output / "index.html").write_text(template.replace("/*DATA*/", json.dumps(data, ensure_ascii=False)), encoding="utf-8")
print(json.dumps({"output": str(output), "appliedCorrections": applied}, ensure_ascii=False))
