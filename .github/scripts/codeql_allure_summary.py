#!/usr/bin/env python3
"""Summarise CodeQL SARIF output as one Allure test result.

Only counts go into the result: no rule names, file paths or code snippets, because the
Allure report is published to a public GitHub Pages site. The details stay in the repo's
Security tab, behind GitHub's permissions.

Usage:
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

SECURITY_LEVELS = ["critical", "high", "medium", "low"]
QUALITY_LEVELS = ["error", "warning", "note"]


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


def count_results(sarif_files):
    security = dict.fromkeys(SECURITY_LEVELS, 0)
    quality = dict.fromkeys(QUALITY_LEVELS, 0)

    for path in sarif_files:
        with open(path, encoding="utf-8") as f:
            sarif = json.load(f)

        for run in sarif.get("runs", []):
            tool = run.get("tool", {})
            rules = {}
            # CodeQL lists query rules under tool.extensions; other tools use tool.driver.
            for component in [tool.get("driver", {})] + tool.get("extensions", []):
                for rule in component.get("rules") or []:
                    rules[rule.get("id")] = rule

            for result in run.get("results", []):
                if result.get("suppressions"):
                    continue
                rule = rules.get(result.get("ruleId")) or {}
                severity = security_severity(rule)
                if severity:
                    security[severity] += 1
                    continue
                level = result.get("level") or (rule.get("defaultConfiguration") or {}).get("level", "warning")
                quality[level if level in quality else "note"] += 1

    return security, quality


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

    security, quality = count_results(sarif_files)
    blocking = security["critical"] + security["high"]

    rows = "".join(
        f"<tr><td>{html.escape(level.capitalize())}</td><td>{security[level]}</td></tr>" for level in SECURITY_LEVELS
    )
    quality_rows = "".join(
        f"<tr><td>{html.escape(level.capitalize())}</td><td>{quality[level]}</td></tr>" for level in QUALITY_LEVELS
    )
    result["descriptionHtml"] = (
        "<p>Counts only. Rule names, files and code are kept in the repository's Security tab.</p>"
        "<p><strong>Security alerts</strong></p>"
        f"<table><tr><th>Severity</th><th>Count</th></tr>{rows}</table>"
        "<p><strong>Code quality</strong></p>"
        f"<table><tr><th>Level</th><th>Count</th></tr>{quality_rows}</table>"
        "<p>This check fails when there is at least one critical or high security alert.</p>"
    )

    summary = (
        f"CodeQL: {security['critical']} critical, {security['high']} high, "
        f"{security['medium']} medium, {security['low']} low security alerts; "
        f"{sum(quality.values())} code quality notes"
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

    security, _ = count_results(sarif_files)
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
