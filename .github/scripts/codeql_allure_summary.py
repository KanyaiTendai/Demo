#!/usr/bin/env python3
"""Summarise CodeQL SARIF output as one Allure test result.

Security alerts are reported as counts only: no rule names, file paths or code snippets,
because the Allure report is published to a public GitHub Pages site and those details would
point at exploitable code. They stay in the repo's Security tab, behind GitHub's permissions.

Code quality findings (no security-severity) are broken down by rule and file, so the team can
see what to fix. They are maintainability issues, not vulnerabilities, and the source is public
anyway. Code snippets and messages are still left out.

Usage:
  codeql_allure_summary.py --filter-generated <sarif-dir>
      Remove code quality findings in Reqnroll-generated code from the SARIF files, in place,
      before they are uploaded. Security alerts in generated code are kept.
  codeql_allure_summary.py <sarif-dir> <allure-results-dir> <alerts-url> <run-url>
      Write the Allure result.
  codeql_allure_summary.py --check <sarif-dir>
      Print the counts and exit 1 if there is any critical or high security alert
      (or no results at all), so the pipeline can block the merge.
"""
import glob
import html
import json
import os
import sys
import time
import uuid
from collections import defaultdict

SECURITY_LEVELS = ["critical", "high", "medium", "low"]
QUALITY_LEVELS = ["error", "warning", "note"]
# Reqnroll generates C# from .feature files; CodeQL maps findings in that code back to the
# .feature file (through #line directives) or reports them in the .feature.cs file.
GENERATED_SUFFIXES = (".feature", ".feature.cs")
# Written to each SARIF run by --filter-generated, so the report can say what was left out.
EXCLUDED_PROPERTY = "generatedQualityResultsExcluded"
MAX_LINES_PER_FILE = 10


def security_severity(rule):
    """Map CodeQL's numeric security-severity score to GitHub's alert severity."""
    score = (rule.get("properties") or {}).get("security-severity")
    if score is None:
        return None
    score = float(score)
    if score >= 9.0:
        return "critical"
    if score >= 7.0:
        return "high"
    if score >= 4.0:
        return "medium"
    return "low"


def result_location(result):
    for location in result.get("locations") or []:
        physical = location.get("physicalLocation") or {}
        uri = (physical.get("artifactLocation") or {}).get("uri")
        if uri:
            return uri, (physical.get("region") or {}).get("startLine")
    return "(unknown file)", None


def rules_by_id(run):
    tool = run.get("tool", {})
    rules = {}
    # CodeQL lists query rules under tool.extensions; other tools use tool.driver.
    for component in [tool.get("driver", {})] + tool.get("extensions", []):
        for rule in component.get("rules") or []:
            rules[rule.get("id")] = rule
    return rules


def is_generated(uri):
    return uri.endswith(GENERATED_SUFFIXES)


def rule_title(rule, rule_id):
    return (
        (rule.get("shortDescription") or {}).get("text")
        or (rule.get("properties") or {}).get("name")
        or rule_id
    )


def count_results(sarif_files):
    """Return security counts, quality counts, quality findings grouped by rule, and the
    number of generated-code quality findings that --filter-generated removed.

    The grouping is {(level, rule_id): {"title": str, "files": {uri: [line, ...]}}}.
    """
    security = dict.fromkeys(SECURITY_LEVELS, 0)
    quality = dict.fromkeys(QUALITY_LEVELS, 0)
    quality_rules = {}
    excluded = 0

    for path in sarif_files:
        with open(path, encoding="utf-8") as f:
            sarif = json.load(f)

        for run in sarif.get("runs", []):
            rules = rules_by_id(run)
            excluded += (run.get("properties") or {}).get(EXCLUDED_PROPERTY, 0)

            for result in run.get("results", []):
                if result.get("suppressions"):
                    continue
                rule = rules.get(result.get("ruleId")) or {}
                severity = security_severity(rule)
                if severity:
                    security[severity] += 1
                    continue
                level = result.get("level") or (rule.get("defaultConfiguration") or {}).get("level", "warning")
                level = level if level in quality else "note"
                quality[level] += 1

                rule_id = result.get("ruleId") or "(unknown rule)"
                entry = quality_rules.setdefault(
                    (level, rule_id), {"title": rule_title(rule, rule_id), "files": defaultdict(list)}
                )
                uri, line = result_location(result)
                entry["files"][uri].append(line)

    return security, quality, quality_rules, excluded


def filter_generated(sarif_dir):
    """Drop code quality findings in generated code. Security alerts are always kept, so the
    severity gate still sees anything CodeQL flags in generated code."""
    sarif_files = find_sarif_files(sarif_dir)
    removed = 0
    for path in sarif_files:
        with open(path, encoding="utf-8") as f:
            sarif = json.load(f)

        for run in sarif.get("runs", []):
            rules = rules_by_id(run)
            kept = []
            for result in run.get("results", []):
                rule = rules.get(result.get("ruleId")) or {}
                if is_generated(result_location(result)[0]) and not security_severity(rule):
                    continue
                kept.append(result)
            run_removed = len(run.get("results", [])) - len(kept)
            run["results"] = kept
            properties = run.setdefault("properties", {})
            properties[EXCLUDED_PROPERTY] = properties.get(EXCLUDED_PROPERTY, 0) + run_removed
            removed += run_removed

        with open(path, "w", encoding="utf-8") as f:
            json.dump(sarif, f)

    print(f"Removed {removed} code quality finding(s) in generated code from {len(sarif_files)} SARIF file(s).")
    return 0


def format_lines(lines):
    known = sorted({line for line in lines if line is not None})
    if not known:
        return ""
    shown = ", ".join(str(line) for line in known[:MAX_LINES_PER_FILE])
    if len(known) > MAX_LINES_PER_FILE:
        shown += f", +{len(known) - MAX_LINES_PER_FILE} more"
    return f" (line {shown})" if len(known) == 1 else f" (lines {shown})"


def quality_breakdown_html(quality_rules, excluded):
    """One row per rule, most severe level first, then by number of findings."""
    note = ""
    if excluded:
        note = (
            f"<p>{excluded} code quality finding(s) in code Reqnroll generates from "
            "<code>.feature</code> files were excluded: they come from Reqnroll's generator and "
            "can't be fixed in this project. Security alerts in generated code are still counted.</p>"
        )
    if not quality_rules:
        return f"<p>No code quality findings.</p>{note}"

    def sort_key(item):
        (level, rule_id), entry = item
        return QUALITY_LEVELS.index(level), -sum(len(v) for v in entry["files"].values()), rule_id

    rows = []
    for (level, rule_id), entry in sorted(quality_rules.items(), key=sort_key):
        files = sorted(entry["files"].items(), key=lambda kv: (-len(kv[1]), kv[0]))
        count = sum(len(lines) for _, lines in files)
        where = "<br>".join(
            f"<code>{html.escape(uri)}</code>: {len(lines)}{html.escape(format_lines(lines))}"
            for uri, lines in files
        )
        rows.append(
            f"<tr><td>{html.escape(level.capitalize())}</td>"
            f"<td>{html.escape(entry['title'])}<br><code>{html.escape(rule_id)}</code></td>"
            f"<td>{count}</td><td>{where}</td></tr>"
        )

    return (
        "<table><tr><th>Level</th><th>Rule</th><th>Count</th><th>Where</th></tr>"
        f"{''.join(rows)}</table>{note}"
    )


def build_result(sarif_files, alerts_url, run_url):
    now = int(time.time() * 1000)
    result = {
        "uuid": str(uuid.uuid4()),
        "historyId": "codeql-csharp-summary",
        "name": "CodeQL analysis",
        "fullName": "Security: CodeQL static analysis (C#)",
        "stage": "finished",
        "start": now,
        "stop": now,
        "labels": [
            {"name": "parentSuite", "value": "Security"},
            {"name": "suite", "value": "Static analysis"},
            {"name": "feature", "value": "CodeQL code scanning"},
            {"name": "tag", "value": "security"},
        ],
        "links": [
            {"name": "Code scanning alerts (repo access required)", "url": alerts_url, "type": "link"},
            {"name": "Pipeline run", "url": run_url, "type": "link"},
        ],
    }

    if not sarif_files:
        result["status"] = "broken"
        result["statusDetails"] = {
            "message": "CodeQL did not produce results for this run. Check the CodeQL job in the pipeline run."
        }
        result["descriptionHtml"] = "<p>No CodeQL results were available to summarise.</p>"
        result["labels"].append({"name": "severity", "value": "normal"})
        return result, "CodeQL: no results available"

    security, quality, quality_rules, excluded = count_results(sarif_files)
    blocking = security["critical"] + security["high"]

    rows = "".join(
        f"<tr><td>{html.escape(level.capitalize())}</td><td>{security[level]}</td></tr>" for level in SECURITY_LEVELS
    )
    quality_rows = "".join(
        f"<tr><td>{html.escape(level.capitalize())}</td><td>{quality[level]}</td></tr>" for level in QUALITY_LEVELS
    )
    result["descriptionHtml"] = (
        "<p><strong>Security alerts</strong></p>"
        "<p>Counts only. Rule names, files and code are kept in the repository's Security tab.</p>"
        f"<table><tr><th>Severity</th><th>Count</th></tr>{rows}</table>"
        "<p><strong>Code quality</strong></p>"
        f"<table><tr><th>Level</th><th>Count</th></tr>{quality_rows}</table>"
        "<p><strong>Code quality by source</strong></p>"
        f"{quality_breakdown_html(quality_rules, excluded)}"
        "<p>This check fails when there is at least one critical or high security alert.</p>"
    )

    summary = (
        f"CodeQL: {security['critical']} critical, {security['high']} high, "
        f"{security['medium']} medium, {security['low']} low security alerts; "
        f"{quality['error']} error, {quality['warning']} warning, {quality['note']} note code quality findings"
    )
    if blocking:
        result["status"] = "failed"
        result["statusDetails"] = {
            "message": f"{blocking} critical or high security alert(s). See the Security tab for details."
        }
        result["labels"].append({"name": "severity", "value": "critical"})
    else:
        result["status"] = "passed"
        result["labels"].append({"name": "severity", "value": "normal"})

    return result, summary


def find_sarif_files(sarif_dir):
    return sorted(glob.glob(os.path.join(sarif_dir, "**", "*.sarif"), recursive=True))


def check(sarif_dir):
    sarif_files = find_sarif_files(sarif_dir)
    if not sarif_files:
        print("::error::No CodeQL results found, so the security check cannot pass.")
        return 1

    security, _, _, _ = count_results(sarif_files)
    blocking = security["critical"] + security["high"]
    print(
        f"Security alerts: {security['critical']} critical, {security['high']} high, "
        f"{security['medium']} medium, {security['low']} low"
    )
    if blocking:
        print(
            f"::error::{blocking} critical or high security alert(s). The PR will not auto-merge. "
            "See the repository's Security tab > Code scanning for details."
        )
        return 1
    return 0


def main(argv):
    if len(argv) == 3 and argv[1] == "--check":
        return check(argv[2])
    if len(argv) == 3 and argv[1] == "--filter-generated":
        return filter_generated(argv[2])

    if len(argv) != 5:
        print(__doc__, file=sys.stderr)
        return 2

    sarif_dir, out_dir, alerts_url, run_url = argv[1:]
    sarif_files = find_sarif_files(sarif_dir)

    result, summary = build_result(sarif_files, alerts_url, run_url)

    os.makedirs(out_dir, exist_ok=True)
    with open(os.path.join(out_dir, f"{result['uuid']}-result.json"), "w", encoding="utf-8") as f:
        json.dump(result, f, indent=2)

    print(summary)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
