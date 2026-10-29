import subprocess, sys
from pathlib import Path

modded  = Path("modded/data.win")
vanilla = Path("vanilla/data.win")
out     = Path("out")

result = subprocess.run(
    ["GMDiffTool.exe", str(modded), str(vanilla), str(out)],
    capture_output=True, text=True,
)

print(result.stdout)
if result.returncode != 0:
    print(result.stderr, file=sys.stderr)
    errors = out / "errors.txt"
    if errors.exists():
        print(errors.read_text(), file=sys.stderr)
    sys.exit(result.returncode)