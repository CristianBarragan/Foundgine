#!/usr/bin/env python3
"""Fail CI when required JPA integration methods did not execute."""
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

report_dir = Path(__file__).parents[1] / "foundgine-providers" / "target" / "surefire-reports"
reports = sorted(report_dir.glob("TEST-*.xml"))
if not reports:
    raise SystemExit(f"No Surefire XML reports found under {report_dir}")

required = {
    "aggregateOrderingUsesAuthorizedPrivateTupleSlotsAndNullsLast",
    "fetchRequestCompilesAggregateContinuationAgainstPrivateOrderingExpressions",
    "aggregateCursorPagesHaveNoDuplicatesOrOmissionsAcrossTiesAndNulls",
    "aggregateCursorCannotBeReplayedAcrossTenantAuthorization",
    "aggregateCursorIsRejectedWhenChildAuthorizationChangesWithinTenant",
    "criteriaContinuationCompilesTypedLexicographicPredicateWithBoundValues",
    "postgresExecutesTheSameScalarCriteriaContractWhenConfigured",
    "postgresExecutesAggregateCursorPagesWithoutDuplicatesOrOmissionsWhenConfigured",
    "postgresRejectsAggregateCursorReplayAcrossTenantAndChangedChildAuthorizationWhenConfigured",
    "postgresCharacterizesAggregateCursorAnomaliesWhenChildAggregatesChangeBetweenPages",
    "h2SnapshotPageSessionKeepsAggregatePagesStableAcrossConcurrentChildChanges",
    "postgresSnapshotPageSessionKeepsAggregatePagesStableAcrossConcurrentChildChanges",
}
executed = set()
skipped = set()
failures = []
for report in reports:
    root = ET.parse(report).getroot()
    for case in root.iter("testcase"):
        name = case.attrib.get("name", "")
        if name:
            if case.find("skipped") is not None:
                skipped.add(name)
            else:
                executed.add(name)
        failure = case.find("failure")
        error = case.find("error")
        if failure is not None or error is not None:
            failures.append(f"{report.name}::{name}")

missing = required - executed
if missing:
    raise SystemExit("Required JPA integration methods did not execute: " + ", ".join(sorted(missing)))
postgres_required = {
    "postgresExecutesTheSameScalarCriteriaContractWhenConfigured",
    "postgresExecutesAggregateCursorPagesWithoutDuplicatesOrOmissionsWhenConfigured",
    "postgresRejectsAggregateCursorReplayAcrossTenantAndChangedChildAuthorizationWhenConfigured",
    "postgresCharacterizesAggregateCursorAnomaliesWhenChildAggregatesChangeBetweenPages",
    "postgresSnapshotPageSessionKeepsAggregatePagesStableAcrossConcurrentChildChanges",
}
skipped_postgres = postgres_required & skipped
if skipped_postgres:
    raise SystemExit("PostgreSQL integration tests were skipped despite CI PostgreSQL configuration: " + ", ".join(sorted(skipped_postgres)))
if failures:
    raise SystemExit("Failing JPA tests: " + ", ".join(failures))
print(f"Verified {len(required)} required JPA integration methods executed, including PostgreSQL aggregate pagination, authorization replay, concurrent-data anomaly characterization, and repeatable-read snapshot consistency; PostgreSQL lane was not skipped; {len(reports)} Surefire reports inspected.")
