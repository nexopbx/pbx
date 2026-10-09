#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-only
"""
Coverage gate for the critical packages. Merges every Cobertura report under ./coverage (one per test project), ignores
generated code, prints line coverage per assembly, and fails if a gated assembly is below its threshold.

Usage: python3 scripts/ci/coverage_gate.py [coverage-dir]
"""
import re
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path

# Minimum line coverage (percent) per assembly. Raise them as the code matures; never lower them to make CI pass.
THRESHOLDS = {
    "NexoPBX.Abstractions": 95,
    "NexoPBX.Auth": 90,
    "NexoPBX.Store": 90,
    "NexoPBX.Api": 80,
}
# CI builds are deterministic, so the same source file is reported under different prefixes:
#   NexoPBX.Auth/F.cs  |  home/runner/work/pbx/pbx/src/NexoPBX.Auth/F.cs  |  D:\a\pbx\pbx\src\NexoPBX.Auth\F.cs
# Reduce every path to "<Project>/<file>" before merging reports, otherwise a file counts twice.
REPO_RELATIVE = re.compile(r"(?:^|/)(?:(?:src|samples|tests)/)?(NexoPBX\.[^/]+/.*)$")


def normalise(filename: str) -> str:
    unified = filename.replace("\\", "/")
    match = REPO_RELATIVE.search(unified)
    return match.group(1) if match else unified


IGNORED_PARTS = ("/Migrations/", "/obj/", "/Protos/", "\\Migrations\\", "\\obj\\")


def main() -> int:
    directory = Path(sys.argv[1] if len(sys.argv) > 1 else "coverage")
    reports = sorted(directory.glob("**/coverage.cobertura.xml"))
    if not reports:
        print(f"No Cobertura reports under {directory}/", file=sys.stderr)
        return 2

    hits: dict[str, dict[tuple[str, int], int]] = defaultdict(dict)
    for report in reports:
        for package in ET.parse(report).getroot().iter("package"):
            name = package.get("name", "")
            for cls in package.iter("class"):
                filename = cls.get("filename", "")
                if any(part in filename for part in IGNORED_PARTS):
                    continue
                for line in cls.iter("line"):
                    key = (normalise(filename), int(line.get("number", "0")))
                    hits[name][key] = max(hits[name].get(key, 0), int(line.get("hits", "0")))

    failed = False
    print(f"{'Assembly':<28}{'Lines':>8}{'Covered':>9}{'%':>8}{'Gate':>7}")
    for name in sorted(hits):
        total = len(hits[name])
        covered = sum(1 for h in hits[name].values() if h > 0)
        percent = 100.0 * covered / total if total else 100.0
        gate = THRESHOLDS.get(name)
        status = ""
        if gate is not None:
            status = f"{gate}%"
            if percent < gate:
                failed = True
                status += "  FAIL"
        print(f"{name:<28}{total:>8}{covered:>9}{percent:>7.1f}%{status:>9}")

    missing = [n for n in THRESHOLDS if n not in hits]
    if missing:
        print(f"No coverage data for gated assemblies: {', '.join(missing)}", file=sys.stderr)
        failed = True
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
