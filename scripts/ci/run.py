#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-only
"""
Runs a command, streams its output, and if it fails reports the tail of that output as a GitHub Actions error annotation
(`::error title=...::`). Annotations are visible in the run summary and through the checks API, so a failure explains
itself without opening the raw log.

Usage: python3 scripts/ci/run.py "Step title" command [args...]
"""
import collections
import os
import shutil
import subprocess
import sys

TAIL_LINES = int(os.environ.get("CI_TAIL_LINES", "40"))


def escape(text: str) -> str:
    return text.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")


def main() -> int:
    if len(sys.argv) < 3:
        print("usage: run.py <title> <command> [args...]", file=sys.stderr)
        return 2
    title, command = sys.argv[1], sys.argv[2:]
    command[0] = shutil.which(command[0]) or command[0]

    tail: collections.deque[str] = collections.deque(maxlen=TAIL_LINES)
    process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, errors="replace")
    assert process.stdout is not None
    for line in process.stdout:
        sys.stdout.write(line)
        sys.stdout.flush()
        tail.append(line.rstrip("\n"))
    code = process.wait()

    if code != 0 and os.environ.get("GITHUB_ACTIONS"):
        print(f"::error title={escape(title)} (exit {code})::{escape(chr(10).join(tail))}")
    return code


if __name__ == "__main__":
    sys.exit(main())
