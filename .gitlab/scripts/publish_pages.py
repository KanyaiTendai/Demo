#!/usr/bin/env python3
"""Build the GitLab Pages site for the Allure report, with the same layout as the GitHub
workflow's gh-pages branch:

  latest/       the newest report
  runs/<n>/     the most recent reports, by pipeline number
  index.html    redirects to latest/

GitLab Pages replaces the whole site on every deployment, so the earlier runs are carried over
through runs.tar.gz: an archive of runs/ that each deployment publishes with the site, and the
next deployment downloads. That download only works while the Pages site is public. A private
site returns its sign-in page instead, and the run history starts again.

Usage:
  publish_pages.py <report-dir> <public-dir> <pages-url> <run-number> <runs-to-keep>

Exits 3 when there is no report to publish.
"""
import io
import os
import shutil
import sys
import tarfile
import urllib.error
import urllib.request

ARCHIVE = "runs.tar.gz"
MAX_ARCHIVE_BYTES = 500 * 1024 * 1024


def download_previous_runs(pages_url, dest):
    """Extract the runs/ folder of the live site into dest. Returns False if there isn't one."""
    url = f"{pages_url.rstrip('/')}/{ARCHIVE}"
    try:
        with urllib.request.urlopen(url, timeout=60) as response:
            data = response.read(MAX_ARCHIVE_BYTES + 1)
    except (urllib.error.URLError, TimeoutError) as error:
        print(f"No earlier runs found at {url} ({error}). Starting a new run history.")
        return False

    if len(data) > MAX_ARCHIVE_BYTES:
        print(f"The earlier runs archive is larger than {MAX_ARCHIVE_BYTES} bytes. Starting a new run history.")
        return False

    try:
        with tarfile.open(fileobj=io.BytesIO(data), mode="r:gz") as archive:
            # The archive comes from a public website, so it is treated as untrusted: the "data"
            # filter refuses absolute paths, ".." and links that point outside dest.
            archive.extractall(dest, filter="data")
    except (tarfile.TarError, OSError) as error:
        print(
            f"Could not read the earlier runs archive ({error}). If the Pages site is private, "
            "the run history can't be carried over. Starting a new run history."
        )
        shutil.rmtree(dest, ignore_errors=True)
        os.makedirs(dest)
        return False
    return True


def main(argv):
    if len(argv) != 6:
        print(__doc__, file=sys.stderr)
        return 2

    report_dir, public_dir, pages_url, run_number, runs_to_keep = argv[1:]
    runs_to_keep = int(runs_to_keep)

    if not os.path.isfile(os.path.join(report_dir, "index.html")):
        print("No report to publish.")
        return 3

    previous_dir = "previous-site"
    shutil.rmtree(previous_dir, ignore_errors=True)
    os.makedirs(previous_dir)
    download_previous_runs(pages_url, previous_dir)

    shutil.rmtree(public_dir, ignore_errors=True)
    runs_dir = os.path.join(public_dir, "runs")
    os.makedirs(runs_dir)

    # Only numbered run folders are carried over, so nothing else in the archive reaches the site.
    previous_runs_dir = os.path.join(previous_dir, "runs")
    if os.path.isdir(previous_runs_dir):
        for name in os.listdir(previous_runs_dir):
            source = os.path.join(previous_runs_dir, name)
            if name.isdigit() and name != run_number and os.path.isdir(source):
                shutil.copytree(source, os.path.join(runs_dir, name))

    shutil.copytree(report_dir, os.path.join(runs_dir, run_number))

    # Keep only the most recent runs so the site doesn't grow forever.
    runs = sorted((name for name in os.listdir(runs_dir) if name.isdigit()), key=int)
    for name in runs[:-runs_to_keep]:
        shutil.rmtree(os.path.join(runs_dir, name))
    kept = runs[-runs_to_keep:]

    shutil.copytree(report_dir, os.path.join(public_dir, "latest"))

    with open(os.path.join(public_dir, "index.html"), "w", encoding="utf-8") as f:
        f.write('<meta http-equiv="refresh" content="0; url=latest/">\n')

    with tarfile.open(os.path.join(public_dir, ARCHIVE), "w:gz") as archive:
        archive.add(runs_dir, arcname="runs")

    site = pages_url.rstrip("/")
    print(f"Published run {run_number}. Keeping runs: {', '.join(kept)}")
    print(f"Report for this run: {site}/runs/{run_number}/")
    print(f"Latest report: {site}/latest/")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
