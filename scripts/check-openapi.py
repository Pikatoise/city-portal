"""Compare the public API contract, ignoring deployment-specific server URLs."""

import difflib
import json
from pathlib import Path
import sys


def normalized_document(source):
    document = json.load(source)
    document.pop("servers", None)
    return json.dumps(document, sort_keys=True, indent=2).splitlines(keepends=True)


snapshot = Path(__file__).resolve().parents[1] / "frontend/openapi.json"
with snapshot.open(encoding="utf-8-sig") as source:
    expected = normalized_document(source)
actual = normalized_document(sys.stdin.buffer)
if expected != actual:
    sys.stdout.writelines(
        difflib.unified_diff(expected, actual, fromfile="frontend/openapi.json", tofile="live OpenAPI")
    )
    raise SystemExit("Live OpenAPI differs from frontend/openapi.json; refresh the client.")

print("Live OpenAPI matches the frontend snapshot.")
