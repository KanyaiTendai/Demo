#!/usr/bin/env python3
"""Summarise GitLab SAST and Secret Detection reports as one Allure test result, and gate on them.

This is the GitLab counterpart of .github/scripts/codeql_allure_summary.py. GitLab's analyzers
write their own JSON format (gl-sast-report.json, gl-secret-detection-report.json) rather than SARIF.

Findings are reported as counts only: no rule names, file paths or code snippets, because the
Allure report is published to GitLab Pages and those details would point at exploitable code.
The full reports stay in the scanner jobs' artifacts, which only the Developer role and above
can download.

Usage:
  security_allure_summary.py <reports-dir> <allure-results-dir> <pipeline-url>
      Write the Allure result.
  security_allure_summary.py --check <reports-dir>
      Print the counts and exit 1 if there is any critical or high SAST finding, any detected
      secret, or a missing report, so the pipeline fails and the merge request can't be merged.
"""
import glob
import html
import json
import os
import sys
import time
import uuid

SEVERITIES = ["critical", "high", "medium", "low", "info", "unknown"]
REPORTS = {
    "sast": "gl-sast-report.json",
    "secret_detection": "gl-secret-detection-report.json",
}


def find_report(reports_dir, file_name):
    # GitLab extracts the scanner jobs' artifacts into the project root, so look there first.
    direct = os.path.join(reports_dir, file_name)
    if os.path.isfile(direct):
        return direct
    matches = sorted(glob.glob(os.path.join(reports_dir, "**", file_name), recursive=True))
    return matches[0] if matches else None


def count_findings(path):
    """Return severity counts for one report, or None if it is missing or the scan failed."""
    if not path:
        return None
    with open(path, encoding="utf-8") as f:
        report = json.load(f)
    if (report.get("scan") or {}).get("status", "success") != "success":
        return None

    counts = dict.fromkeys(SEVERITIES, 0)
    for finding in report.get("vulnerabilities") or []:
        severity = (finding.get("severity") or "unknown").lower()
        counts[severity if severity in counts else "unknown"] += 1
    return counts


def load_counts(reports_dir):
    return {kind: count_findings(find_report(reports_dir, name)) for kind, name in REPORTS.items()}


def blocking_reasons(counts):
    reasons = []
    for kind, label in (("sast", "SAST"), ("secret_detection", "Secret Detection")):
        if counts[kind] is None:
            reasons.append(f"{label} produced no report, so the security check cannot pass")
    if counts["sast"]:
        blocking = counts["sast"]["critical"] + counts["sast"]["high"]
        if blocking:
            reasons.append(f"{blocking} critical or high SAST finding(s)")
    if counts["secret_detection"]:
        secrets = sum(counts["secret_detection"].values())
        if secrets:
            reasons.append(f"{secrets} secret(s) detected in the repository")
    return reasons


def describe(counts):
    sast = counts["sast"]
    secrets = counts["secret_detection"]
    sast_text = (
        "SAST: no report"
        if sast is None
        else "SAST: " + ", ".join(f"{sast[level]} {level}" for level in SEVERITIES)
    )
    secrets_text = (
        "Secret Detection: no report"
        if secrets is None
        else f"Secret Detection: {sum(secrets.values())} secret(s)"
    )
    return f"{sast_text}; {secrets_text}"


def counts_table(counts):
    if counts is None:
        return "<p>No report was available. Check the scanner job in the pipeline.</p>"
    rows = "".join(
        f"<tr><td>{html.escape(level.capitalize())}</td><td>{counts[level]}</td></tr>" for level in SEVERITIES
    )
    return f"<table><tr><th>Severity</th><th>Count</th></tr>{rows}</table>"


def build_result(counts, pipeline_url):
    now = int(time.time() * 1000)
    reasons = blocking_reasons(counts)
    result = {
        "uuid": str(uuid.uuid4()),
        "historyId": "gitlab-security-summary",
        "name": "GitLab security scans",
        "fullName": "Security: GitLab SAST and Secret Detection",
        "stage": "finished",
        "start": now,
        "stop": now,
        "labels": [
            {"name": "parentSuite", "value": "Security"},
            {"name": "suite", "value": "Static analysis"},
            {"name": "feature", "value": "GitLab SAST and Secret Detection"},
            {"name": "tag", "value": "security"},
            {"name": "severity", "value": "critical" if reasons else "normal"},
        ],
        "links": [
            {"name": "Pipeline (scanner job artifacts need the Developer role)", "url": pipeline_url, "type": "link"},
        ],
        "descriptionHtml": (
            "<p>Counts only. The full reports are artifacts of the <code>semgrep-sast</code> and "
            "<code>secret_detection</code> jobs in the pipeline, visible to the Developer role and above.</p>"
            "<p><strong>SAST (Semgrep)</strong></p>"
            f"{counts_table(counts['sast'])}"
            "<p><strong>Secret Detection</strong></p>"
            f"{counts_table(counts['secret_detection'])}"
            "<p>This check fails on any critical or high SAST finding, any detected secret, "
            "or a missing report.</p>"
        ),
    }

    missing_only = reasons and all("no report" in reason for reason in reasons)
    if not reasons:
        result["status"] = "passed"
    else:
        result["status"] = "broken" if missing_only else "failed"
        result["statusDetails"] = {"message": "; ".join(reasons) + "."}
    return result


def check(reports_dir):
    counts = load_counts(reports_dir)
    print(describe(counts))
    reasons = blocking_reasons(counts)
    for reason in reasons:
        print(f"ERROR: {reason}.")
    if reasons:
        print("The merge request can't be merged until this passes. Download the scanner job artifacts for details.")
        return 1
    return 0


def main(argv):
    if len(argv) == 3 and argv[1] == "--check":
        return check(argv[2])

    if len(argv) != 4:
        print(__doc__, file=sys.stderr)
        return 2

    reports_dir, out_dir, pipeline_url = argv[1:]
    counts = load_counts(reports_dir)
    result = build_result(counts, pipeline_url)

    os.makedirs(out_dir, exist_ok=True)
    with open(os.path.join(out_dir, f"{result['uuid']}-result.json"), "w", encoding="utf-8") as f:
        json.dump(result, f, indent=2)

    print(describe(counts))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
