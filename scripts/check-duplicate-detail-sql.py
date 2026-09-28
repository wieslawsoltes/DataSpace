#!/usr/bin/env python3
"""Check duplicate-detail SQL identities with an independent SQLite reference.

Uses the golden SQL also asserted by FindDuplicateDetailsTests. This does not
compile C#, execute DataSpace, validate Uno accessibility, or replace dotnet test.
The custom collations model only the fixture families below, not all ACE/.NET
collation or coercion rules. No network, browser profile, or production data is used.
"""
from __future__ import annotations

import argparse
import json
import random
import sqlite3
from datetime import datetime
from decimal import Decimal
from pathlib import Path
from typing import Callable
from uuid import UUID

ROOT = Path(__file__).resolve().parents[1]


def normalize_bool(value: str) -> bool:
    if value.upper() in ("YES", "TRUE", "1", "-1"):
        return True
    if value.upper() in ("NO", "FALSE", "0"):
        return False
    raise ValueError(f"Unsupported boolean fixture: {value}")


def check() -> dict:
    goldens = json.loads((ROOT / "tests/DataSpace.Tests/Fixtures/duplicate-detail-sql.json").read_text())
    cases = 0
    families: list[tuple[str, Callable, list[str | None]]] = [
        ("text", str.upper, ["A", "a", "B", "", "1:2", "line\ntext", "line\rtext", "Ω", None]),
        ("integer", int, ["1", "+1", "-1", "0", "00", "3", None]),
        ("decimal", Decimal, ["1.0", "1.00", "-2", "-2.000", "0", "0.0", None]),
        ("boolean", normalize_bool, ["Yes", "-1", "True", "False", "No", "0", None]),
        ("date", datetime.fromisoformat, ["2025-01-02T03:04:05", "2025-01-02T03:04:05.000000", "2025-01-03T03:04:05", None]),
        ("guid", UUID, ["AAAAAAAA-0000-0000-0000-000000000001", "aaaaaaaa-0000-0000-0000-000000000001", "bbbbbbbb-0000-0000-0000-000000000001", None]),
    ]
    rng = random.Random(1842)
    for family, normalize, choices in families:
        def compare(left: str, right: str) -> int:
            a, b = normalize(left), normalize(right)
            return (a > b) - (a < b)

        patterns = [[], [None], [None, None], [None] * 7, choices, choices * 2]
        patterns += [[rng.choice(choices) for _ in range(rng.randrange(1, 65))] for _ in range(64)]
        with sqlite3.connect(":memory:") as database:
            database.create_collation("DSKEY", compare)
            database.execute('CREATE TABLE "Source data" (ID INTEGER, "Key" TEXT COLLATE DSKEY, Other TEXT)')
            for values in patterns:
                database.execute('DELETE FROM "Source data"')
                database.executemany('INSERT INTO "Source data" VALUES (?, ?, ?)',
                    [(i + 1, value, "A" if i % 2 == 0 else "B") for i, value in enumerate(values)])
                for case in goldens:
                    include_nulls = case["includeNulls"]
                    expected = []
                    for i, value in enumerate(values):
                        matches = sum(1 for candidate in values if
                            (value is None and candidate is None and include_nulls) or
                            (value is not None and candidate is not None and normalize(value) == normalize(candidate)))
                        if matches > 1:
                            expected.append(i + 1)
                    before = database.total_changes
                    reference = [row[0] for row in database.execute(case["referenceSql"])]
                    optimized = [row[0] for row in database.execute(case["optimizedSql"])]
                    if reference != expected or optimized != expected:
                        raise AssertionError({"family": family, "values": values, "includeNulls": include_nulls,
                            "expected": expected, "reference": reference, "optimized": optimized})
                    if database.total_changes != before:
                        raise AssertionError("Read-only duplicate queries modified the fixture.")
                    cases += 1
    return {
        "status": "passed", "cases": cases, "randomSeed": 1842,
        "engine": "Python sqlite3 independent SQL reference",
        "scope": "Golden SQL compared with correlated SQL and direct pairwise fixture expectations; read-only verified.",
        "notValidated": ["C# compilation", "DataSpace executor regression suite", "Uno rendering/accessibility", "ACE/Jet or full .NET collation", "performance"],
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    result = check()
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(result, indent=2) + "\n")
    print(f"PASS: {result['cases']} independent SQL semantic cases. C#/Uno tests still required.")


if __name__ == "__main__":
    main()
