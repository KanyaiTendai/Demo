# Demo: PayPal API Test Automation

An automated API test suite for the public [PayPal REST API sandbox](https://developer.paypal.com/api/rest/). It's a working example of a modern .NET test automation setup:

- **Playwright for .NET (C#)**: the HTTP client for API testing, through Playwright's `APIRequestContext`.
- **Reqnroll (BDD)**: tests written as Gherkin `.feature` files and run with NUnit.
- **Allure Report**: test reports with trend history, published to GitHub Pages.
- **CodeQL**: static security and code quality analysis, with a gate that blocks the merge on severe alerts.
- **GitHub Actions**: a pipeline that tests every pull request, publishes the report, and auto-merges when everything passes.
- **Dependabot**: weekly NuGet and GitHub Actions updates.

## What the tests cover

| Feature | Endpoint | Scenarios |
|---|---|---|
| `Authentication.feature` | `POST /v1/oauth2/token` | Valid credentials return a Bearer token (200). Invalid credentials are rejected (401). |
| `Orders.feature` | `POST /v2/checkout/orders` | Create an order (201). Missing token (401). Missing purchase unit amount (400). |
| `OrderAuthorization.feature` | `POST /v2/checkout/orders/{id}/authorize` | Order not approved by the payer (422). Order doesn't exist (404). Missing token (401). |
| `Performance.feature` (`@performance`) | `POST /v1/oauth2/token`, `POST /v2/checkout/orders` | Light, rate-limited load runs that assert on success rate, request rate and response times. Also an idempotency race test (skipped with `@ignore` because of a known PayPal sandbox issue) and checks that over-limit profiles and bursts are rejected. See [PERFORMANCE_TESTING.md](PERFORMANCE_TESTING.md). |

Example scenario:

```gherkin
Scenario: Successfully create an order with valid details
  Given a valid PayPal access token has been obtained
  And an order request for 1 "T-Shirt" item priced at "100.00" "USD" with intent "AUTHORIZE"
  When I create the order
  Then the response status code should be 201
  And the response should contain an order status of "CREATED"
```

## Tech stack

| Area | Tool |
|---|---|
| Language and runtime | C# on .NET 9 (pinned in `global.json`) |
| HTTP and API testing | Microsoft.Playwright |
| BDD and test runner | Reqnroll with NUnit |
| Reporting | Allure.Reqnroll and the Allure CLI |
| Configuration | Environment variables, with `.env` support through DotNetEnv |
| Security analysis | GitHub CodeQL (`security-and-quality` queries) |
| CI/CD | GitHub Actions, GitHub Pages, Dependabot |

## Project structure

```
Demo/
├── Features/           # Gherkin .feature files: the test scenarios
├── StepDefinitions/    # Reqnroll step bindings
├── Clients/            # API clients for authentication and orders, built on Playwright
├── Models/             # Request and response models
├── Configuration/      # ApiSettings (base URL, credentials) and PerformanceSettings (load safety limits)
├── Performance/        # Load runner, rate pacer, circuit breaker and result statistics
├── Support/            # Playwright setup, shared test context, token store, local Allure hook
└── allureConfig.json
.github/
├── workflows/test-and-deploy.yml       # The CI/CD pipeline
├── scripts/codeql_allure_summary.py    # Filters CodeQL results, adds them to the Allure report, runs the severity gate
└── dependabot.yml
```

## Running locally

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- A free [PayPal developer account](https://developer.paypal.com/) with a sandbox REST app, which gives you a client ID and secret
- Optional: the [Allure CLI](https://allurereport.org/docs/) to open the report automatically after a run (it needs Java)

### 1. Add your credentials

Copy the template to `.env` in the repository root and fill in your sandbox credentials. `.env` is in `.gitignore`, so it won't be committed, and `chmod 600` stops other accounts on the machine from reading it.

```bash
cp .env.example .env && chmod 600 .env
```

`PAYPAL_BASE_URL` is optional and defaults to `https://api-m.sandbox.paypal.com`. It must be an `https://` URL on `*.sandbox.paypal.com`, or a `localhost` mock. Anything else is refused before any request is sent, so the credentials can't be sent anywhere else by mistake.

The `.env` file is only loaded locally, and it never overrides a variable that is already set. When `CI=true`, it isn't loaded at all.

### 2. Run the tests

```bash
dotnet test
```

If the Allure CLI is installed, a report opens at `http://localhost:4567` once the run finishes. If it isn't installed, the raw results are in `Demo/bin/<configuration>/net9.0/allure-results`.

## CI/CD pipeline

The workflow in `.github/workflows/test-and-deploy.yml` runs on every pull request into `main`:

```
Pull request ─┬─► Test ───┬─► Allure Report ─► Publish Report ─► GitHub Pages
              │           │   (runs on pass or fail)
              └─► CodeQL ─┴─► Deploy ─► auto-merge into main
                              (only if both passed)
```

| Job | What it does |
|---|---|
| **Test** | Checks that credentials and project files are present, builds, and runs the suite against the PayPal sandbox. Uploads the Allure and TRX results. The PayPal secrets are passed only to the steps that need them. Dependabot PRs run the full suite too, using the Dependabot copies of the secrets, but are never auto-merged. |
| **CodeQL** | Scans the C# code, including the code Reqnroll generates. It removes quality findings in generated code that can't be fixed, uploads the results to the Security tab, and **fails on any critical or high security alert**. |
| **Allure Report** | Adds a CodeQL summary to the results, keeps trend history, and generates the report. It also uploads a single-file HTML copy you can open offline. It installs the Allure CLI, so it has a read-only token. |
| **Publish Report** | Pushes the generated report to the `gh-pages` branch. It is the only job with write access to the repository contents, and it runs no third-party code. |
| **Deploy** | Turns on GitHub auto-merge for the PR. Branch protection still decides when it merges (reviews and the required `Test` check). Without branch protection, auto-merge merges immediately, so keep `main` protected. |

Every action is pinned to a full commit SHA, with its version in a comment. Dependabot keeps the pins up to date.

Pushes to `main` and a weekly schedule (Mondays 06:00 UTC) run CodeQL only.

**Reports:** `https://<owner>.github.io/<repo>/latest/`. Each run's report is also kept under `runs/<run number>/`.

The public report shows **counts only** for security alerts. Full details stay in the repository's Security tab, which needs repo access.

### Setting it up in your own repository

1. Copy `.github/workflows/`, `.github/scripts/` and `.github/dependabot.yml`.
2. Add the `PAYPAL_CLIENT_ID` and `PAYPAL_CLIENT_SECRET` secrets under **Settings → Secrets and variables → Actions**. Add them again under **Dependabot**, because Dependabot PRs can't read Actions secrets. Ideally use a separate sandbox app for the Dependabot copies, so they can be revoked without affecting the main pipeline. `PAYPAL_BASE_URL` can be set as an Actions variable.
3. Turn off CodeQL **default setup** under **Settings → Code security**. GitHub rejects the workflow's upload while it's on.
4. Turn on **Allow auto-merge** under **Settings → General**.
5. Protect `main`: require a pull request, an approval and the `Test` check. Requiring `CodeQL` too is recommended. This step is required, not optional, because the Deploy job's auto-merge merges straight away when there is no protection.
6. Under **Settings → Actions → General**, set **Workflow permissions** to read-only, allow only actions by GitHub and verified creators, and require actions to be pinned to a full-length commit SHA.
7. Under **Settings → Code security**, turn on secret scanning **push protection**.
8. After the first report run, set **Pages** to deploy from the `gh-pages` branch.

If you're using a different API, update the secret names, the solution name in the CodeQL build step, and the environment info in the Allure Report job.
