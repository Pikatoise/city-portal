"""Compare the public API contract, ignoring deployment-specific server URLs."""

import difflib
import json
import sys


def normalized_document(path):
    with open(path, encoding="utf-8-sig") as source:
        document = json.load(source)
    document.pop("servers", None)
    return json.dumps(document, sort_keys=True, indent=2).splitlines(keepends=True)


expected = normalized_document(sys.argv[1])
actual = normalized_document(sys.argv[2])
if expected != actual:
    sys.stdout.writelines(
        difflib.unified_diff(expected, actual, fromfile=sys.argv[1], tofile=sys.argv[2])
    )
    raise SystemExit("Live OpenAPI differs from frontend/openapi.json; refresh the client.")

print("Live OpenAPI matches the frontend snapshot.")
