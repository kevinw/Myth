import re
import sys
from pathlib import Path


path = Path(sys.argv[1])
source = path.read_text()
output, count = re.subn(r"(texture_storage_[^<]+<[^,>]+),read_write>", r"\1,write>", source)
if count == 0 and ",write>" not in source:
    raise RuntimeError(f"no storage texture access found in {path}")
if output != source:
    path.write_text(output)
