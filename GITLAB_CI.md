# Running the pipeline on GitLab

The repository has two pipelines that run the same tests:

- `.github/workflows/test-and-deploy.yml` for GitHub Actions
- `.gitlab-ci.yml` for GitLab CI/CD

The test code, `.env` handling and `dotnet test` commands are the same in both. This page covers the GitLab version. It is built for **GitLab.com Free with shared Docker runners** and notes where a paid tier would help.

## What the pipeline does

```
Merge request ─┬─► test ─────────────────┬─► allure-report ─► publish-report ─► GitLab Pages
               │                         │   (runs on pass or fail)
               ├─► semgrep-sast ────┐    │
               └─► secret_detection ┴─► security-gate
                                         (fails on critical/high findings or any secret)

"Pipelines must succeed" + "Set to auto-merge" ─► merge into main once everything passes
```

| Job | What it does |
|---|---|
| **test** | Checks that credentials and project files are present, then restores, builds and runs the suite against the PayPal sandbox. Restore and build run with the PayPal variables removed from their environment, because NuGet packages can run their own code in those steps. Uploads the Allure results and a JUnit file, so the merge request shows a test summary. |
| **semgrep-sast** | GitLab's SAST scanner (Semgrep) for the C# code. It comes from GitLab's `Jobs/SAST.gitlab-ci.yml` template. |
| **secret_detection** | Scans the commits for leaked credentials. It comes from GitLab's `Jobs/Secret-Detection.gitlab-ci.yml` template. |
| **security-gate** | Reads both scanner reports. It **fails on any critical or high SAST finding, any detected secret, or a missing report**, which blocks the merge. It also writes a counts-only summary for the Allure report. |
| **allure-report** | Combines the test results and the security summary, adds trend history from the previous report, and generates the report plus a single-file copy you can open offline. It adds an **Allure report** link to the merge request. The Allure CLI is downloaded from Maven Central and its checksum is checked, so no npm install scripts run. |
| **publish-report** | Deploys the report to GitLab Pages with the same layout as the GitHub `gh-pages` branch: `latest/`, `runs/<pipeline number>/` for the last 20 runs, and a root page that redirects to `latest/`. It adds a **View deployment** link to the merge request. GitLab deploys the files itself, so this job needs no token and runs no third-party code. |

Pushes to `main` and the weekly schedule run only the security scans and the gate, never the tests.

Merge requests from forks run in the fork, which has no credentials, so the test and report jobs are skipped there. After reviewing the code, a maintainer can run the pipeline in this project from the merge request's **Pipelines** tab.

## How it maps to the GitHub version

| GitHub Actions | GitLab (Free) |
|---|---|
| Pull request trigger | Merge request pipelines into the default branch |
| `workflow_dispatch` | **Build → Pipelines → Run pipeline** |
| `concurrency` with `cancel-in-progress` | `workflow:auto_cancel` and `interruptible` jobs |
| Actions pinned to commit SHAs | Images pinned by digest (`image:tag@sha256:...`) |
| Secrets passed only to two steps | `env -u` removes them from restore and build. Other jobs still see them on Free (see below). |
| CodeQL and the severity gate | Semgrep SAST, Secret Detection and the `security-gate` job |
| Security tab | Scanner job artifacts, downloadable by the Developer role and above |
| TRX results | JUnit report shown in the merge request (`JunitXml.TestLogger`) |
| Report pushed to `gh-pages`: `latest/` and `runs/<run number>/` for the last 20 runs | The same layout on Pages, numbered by pipeline. Pages replaces the whole site on each deployment, so earlier runs are carried over in `runs.tar.gz` (see below). |
| Deploy job runs `gh pr merge --auto` | GitLab's built-in **Pipelines must succeed** and **Set to auto-merge**. No token needed in the pipeline. |
| Ruleset protecting `main` | Protected branch and the merge checks below |
| Dependabot | Renovate (`renovate.json5`) |

## Setting it up

### 1. Get the code into GitLab

Create a GitLab project and push this repository to it:

```bash
git remote add gitlab git@gitlab.com:<group>/<project>.git
git push gitlab main
```

If you want to keep GitHub as well, push to both remotes. You can also set up a push mirror from GitLab to GitHub under **Settings → Repository → Mirroring repositories**. Pull mirroring from GitHub into GitLab needs Premium.

### 2. Add the CI/CD variables

Under **Settings → CI/CD → Variables**, add:

| Key | Visibility | Protect variable | Notes |
|---|---|---|---|
| `PAYPAL_CLIENT_ID` | Masked | Off | Sandbox app client ID |
| `PAYPAL_CLIENT_SECRET` | Masked and hidden | Off | Sandbox app secret |
| `PAYPAL_BASE_URL` | Visible | Off | Optional. Defaults to `https://api-m.sandbox.paypal.com`. Must be an `https://` URL on `*.sandbox.paypal.com`, or a localhost mock. |

Leave **Protect variable** off. Protected variables only reach pipelines on protected branches, and merge request pipelines run on feature branches, so the tests would get no credentials.

Use a separate sandbox app from the one GitHub uses. Then either platform's credentials can be revoked without breaking the other.

### 3. Block merges until the pipeline passes

Under **Settings → Merge requests**:

- **Merge checks**: turn on **Pipelines must succeed**. Turn off **Skipped pipelines are considered successful**.
- Optionally turn on **All threads must be resolved**.

To merge, open the merge request and select **Set to auto-merge**. It merges once the pipeline passes. This replaces the GitHub Deploy job.

### 4. Protect `main`

Under **Settings → Repository → Protected branches**, protect `main`:

- **Allowed to merge**: Maintainers
- **Allowed to push and merge**: No one
- **Allowed to force push**: off

Required approvals need Premium. On Free, agree as a team that someone reviews each merge request before setting it to auto-merge.

### 5. Lock down CI/CD

- **Settings → CI/CD → General pipelines**: keep **Auto-cancel redundant pipelines** on.
- **Settings → CI/CD → Job token permissions**: limit access to this project only.
- **Settings → General → Visibility**: set **CI/CD** to **Only project members**, so job logs and artifacts aren't public.

### 6. Turn on Pages

After the first merge request pipeline, the site URL appears under **Deploy → Pages**. The reports are at:

- `<site>/latest/`: the newest report
- `<site>/runs/<pipeline number>/`: each of the last 20 runs

GitLab Pages replaces the whole site on every deployment, unlike a `gh-pages` branch, which keeps its files. So each deployment also publishes `runs.tar.gz`, an archive of `runs/`. The next pipeline downloads it, adds its own run, removes the oldest beyond 20 and deploys the result. `REPORT_HISTORY_RUNS` in `.gitlab-ci.yml` sets how many runs are kept.

Choose who can see the site under **Settings → General → Visibility → Pages**:

- **Everyone** keeps the run history and the trend graphs, because the pipeline can read the previous site. The report shows security findings as counts only, but the test results and attachments are public.
- **Only project members** keeps the reports private, but the pipeline can't read a private site. Each deployment then shows only its own run, with no trend graphs. Each run's single-file report is still linked from its merge request for 30 days.

### 7. Schedule the weekly security scan

Under **Build → Pipeline schedules**, add a schedule on `main`, for example `0 6 * * 1` (Mondays 06:00 UTC). It runs SAST, Secret Detection and the gate only.

### 8. Set up Renovate

Renovate replaces Dependabot. It reads `renovate.json5` and opens merge requests for NuGet packages and the CI images. It runs every update through the full pipeline and never merges on its own. Choose one of these:

- **Mend's hosted Renovate app.** It's free. See the [Renovate docs](https://docs.renovatebot.com/) for the GitLab install steps.
- **A separate runner project.** Use GitLab's [renovate-runner](https://gitlab.com/renovate-bot/renovate-runner) in its own project, with a bot account's token stored there.

Don't run Renovate as a job in this project. On GitLab Free, every job receives every CI/CD variable, so the PayPal credentials and Renovate's token would be exposed to each other.

## Security differences on GitLab Free

| Topic | What to know |
|---|---|
| Secrets in every job | Free has no environment scopes, so every job in the pipeline receives the PayPal variables. The pipeline still removes them from restore and build. On Premium or Ultimate, set both variables' **environment scope** to `paypal-sandbox`. The `test` job already declares that environment, so it becomes the only job that gets them. |
| Pipeline file edits | Anyone with the Developer role can change `.gitlab-ci.yml` in a merge request, and that pipeline runs with the credentials. This is the same trust model as GitHub. Only give the Developer role to people you'd trust with the sandbox credentials. |
| Secret push protection | Needs Ultimate. On Free, the `secret_detection` job finds leaked secrets after they're pushed and fails the pipeline. Rotate any secret it reports. |
| Scanner templates | GitLab maintains the SAST and Secret Detection templates. Their analyzer images are pinned to a major version, not a digest. |
| Allure CLI version | `ALLURE_VERSION` and `ALLURE_SHA256` in `.gitlab-ci.yml` aren't tracked by Renovate. Update both together. |

## Checking the pipeline file

Open **Build → Pipeline editor** in GitLab. It validates `.gitlab-ci.yml` and shows the jobs each pipeline type would run.
