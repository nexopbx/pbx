#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-only
"""
License gate for NuGet dependencies (ADR-012).

Reads the packages actually resolved by `dotnet restore` (obj/project.assets.json of every project), finds each
package's declared license in its .nuspec, and fails if any is not on the allow-list. With --write-notices it also
regenerates THIRD_PARTY_NOTICES.md.

Usage: python3 scripts/ci/check_licenses.py [--write-notices] [--check-notices]
"""
import json
import os
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
NOTICES = ROOT / "THIRD_PARTY_NOTICES.md"

# SPDX identifiers accepted without further review (permissive, compatible with AGPL-3.0).
ALLOWED = {
    "MIT", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause", "ISC", "MS-PL", "Unlicense", "CC0-1.0", "Zlib", "0BSD",
    "BSL-1.0", "PostgreSQL",
}

# Packages that only carry a legacy <licenseUrl> (no SPDX expression). Each entry was checked by hand.
LEGACY_OVERRIDES = {
    "google.api.commonprotos": "Apache-2.0",  # licenseUrl -> Apache-2.0 (googleapis)
    "otp.net": "MIT",                          # LICENSE.txt inside the package is the MIT text
    "yamldotnet": "MIT",                       # LICENSE.txt inside the package is the MIT text
    "xunit.abstractions": "Apache-2.0",        # test-only; xunit license.txt is Apache-2.0
}

# Reviewed exceptions that are NOT on the allow-list. Keep this empty unless an ADR says otherwise.
# (SIPSorcery will appear here only if ADR-012 option 1 is chosen.)
EXCEPTIONS: dict[str, str] = {}


def packages_folder(assets: dict) -> list[Path]:
    folders = [Path(p) for p in assets.get("packageFolders", {})]
    env = os.environ.get("NUGET_PACKAGES")
    if env:
        folders.insert(0, Path(env))
    return folders


def read_license(nuspec: Path) -> tuple[str | None, str | None]:
    """Returns (expression, url). The nuspec namespace changes between versions, so match tags without it."""
    root = ET.parse(nuspec).getroot()
    expr = url = None
    for el in root.iter():
        tag = el.tag.split("}")[-1]
        if tag == "license" and el.get("type") == "expression":
            expr = (el.text or "").strip()
        elif tag == "licenseUrl":
            url = (el.text or "").strip()
    return expr, url


def expression_allowed(expr: str) -> bool:
    """`A OR B` is fine if either side is; `A AND B` needs both. Parentheses are flattened (good enough for NuGet)."""
    expr = expr.replace("(", " ").replace(")", " ")
    for alternative in re.split(r"\s+OR\s+", expr):
        ids = [t for t in re.split(r"\s+(?:AND|WITH)\s+", alternative.strip()) if t]
        if ids and all(i.rstrip("+") in ALLOWED for i in ids):
            return True
    return False


def main() -> int:
    write = "--write-notices" in sys.argv
    check_notices = "--check-notices" in sys.argv
    assets_files = sorted(p for d in ("src", "samples", "tests") for p in (ROOT / d).glob("**/obj/project.assets.json"))
    if not assets_files:
        print("No project.assets.json found: run `dotnet restore` first.", file=sys.stderr)
        return 2

    found: dict[tuple[str, str], tuple[str, str]] = {}  # (id, version) -> (license, shipped?)
    problems: list[str] = []
    for assets_file in assets_files:
        assets = json.loads(assets_file.read_text(encoding="utf-8"))
        shipped = "tests" not in assets_file.relative_to(ROOT).parts
        folders = packages_folder(assets)
        for lib, info in assets.get("libraries", {}).items():
            if info.get("type") != "package":
                continue
            name, version = lib.split("/")
            key = (name, version)
            nuspec = next((f / name.lower() / version / f"{name.lower()}.nuspec" for f in folders
                           if (f / name.lower() / version / f"{name.lower()}.nuspec").exists()), None)
            if nuspec is None:
                problems.append(f"{name} {version}: package not found in the NuGet cache")
                continue
            expr, url = read_license(nuspec)
            license_text = expr or LEGACY_OVERRIDES.get(name.lower())
            if license_text is None:
                problems.append(f"{name} {version}: no SPDX license expression (licenseUrl={url!r}); review it and add an override")
                continue
            if name.lower() in EXCEPTIONS:
                license_text = f"{license_text} (exception: {EXCEPTIONS[name.lower()]})"
            elif not expression_allowed(license_text):
                problems.append(f"{name} {version}: license '{license_text}' is not on the allow-list")
            previous = found.get(key)
            found[key] = (license_text, "yes" if shipped or (previous and previous[1] == "yes") else "no")

    rows = sorted(found.items(), key=lambda kv: kv[0][0].lower())
    print(f"{len(rows)} packages checked across {len(assets_files)} projects.")

    notices = ["# Third-party notices", "",
               "NexoPBX is licensed under the GNU AGPL-3.0 (see `LICENSE`). It uses the open-source packages below,",
               "each under its own license. This file is generated by `scripts/ci/check_licenses.py --write-notices`;",
               "do not edit by hand.", "",
               "| Package | Version | License | Shipped |", "|---|---|---|---|"]
    notices += [f"| {n} | {v} | {lic} | {shipped} |" for (n, v), (lic, shipped) in rows]
    notices += ["", "Protocol definitions `google/api/annotations.proto` and `google/api/http.proto` are vendored from",
                "https://github.com/googleapis/googleapis under the Apache License 2.0.", ""]
    text = "\n".join(notices)

    if write:
        NOTICES.write_text(text, encoding="utf-8", newline="\n")
        print(f"Wrote {NOTICES.name}")
    elif check_notices:
        current = NOTICES.read_text(encoding="utf-8") if NOTICES.exists() else ""
        if current.replace("\r\n", "\n") != text:
            problems.append("THIRD_PARTY_NOTICES.md is out of date: run `python3 scripts/ci/check_licenses.py --write-notices` and commit it")

    for problem in sorted(set(problems)):
        print(f"::error::{problem}" if os.environ.get("GITHUB_ACTIONS") else f"ERROR: {problem}", file=sys.stderr)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
