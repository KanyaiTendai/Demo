# Performance Testing

This document explains how the performance tests fit into the suite, what they do on each run, and how to make them heavier later.

The endpoints belong to PayPal, not to us. Because of that, the tests set a **safe, light baseline** that stays well within PayPal's rate limits and doesn't look like abuse. They show how the framework works. They are not a stress test. Read [Safety guardrails](#safety-guardrails) before you raise any limit.

## Summary

| | |
|---|---|
| **Feature file** | `Demo/Features/Performance.feature`, tagged `@performance` |
| **Style** | The same Reqnroll/Gherkin BDD style as the functional suite, reusing its existing steps |
| **Endpoints** | `POST /v1/oauth2/token` and `POST /v2/checkout/orders` |
| **Default load** | About 19 paced requests per run (never more than 1 per second or 2 at the same time), plus one race test that sends 3 requests at once and then 1 more |
| **Run time** | About 30 seconds |
| **What it checks** | Every request succeeds, nothing is rate limited, the request rate stays under its limit, average and p95 response times stay within budget, and simultaneous requests that share a `PayPal-Request-Id` create only one order |
| **Reporting** | A per-request timing table in the console and attached to each scenario in the Allure report |
| **CI** | Runs in the existing **Test** job with the rest of the suite. The idempotency race test is skipped with `@ignore` (see [Known issue](#known-issue-paypal-sandbox-idempotency-race)) |

## How it fits into the project

The performance layer reuses the existing parts of the project and adds a small load engine:

```
Performance.feature  ──►  PerformanceSteps  ──►  LoadRunner ──► existing API clients ──► PayPal sandbox
                              │                    │  ├─ RequestPacer   (rate limit)
                              │                    │  └─ CircuitBreaker (stop on 429/401/403/5xx)
                              │                    └─► LoadTestResult  (stats + summary)
                              ├─► BurstRunner ──► existing API clients ──► PayPal sandbox
                              │     (releases N requests at the same instant for race tests)
                              ├─► PerformanceSettings (safety limits + sandbox-only check)
                              └─► PerformanceTestContext (scenario state)
```

| File | Role |
|---|---|
| `Features/Performance.feature` | The scenarios and their load profiles and thresholds, written in Gherkin |
| `StepDefinitions/PerformanceSteps.cs` | Step bindings. Checks the profile or burst against the limits, runs it, asserts on the results and attaches the summary to Allure |
| `Configuration/PerformanceSettings.cs` | The safety limits (overridable with environment variables) and the sandbox-only check |
| `Performance/LoadProfile.cs` | Request count, concurrent users and the maximum requests per second |
| `Performance/LoadRunner.cs` | Runs the virtual users, records a sample for each request, and contains the circuit breaker |
| `Performance/RequestPacer.cs` | A rate limiter shared by all virtual users, so the total request rate never goes over the limit |
| `Performance/LoadTestResult.cs` | Error rate, min, average, p50, p90, p95, max, observed rate and the text summary |
| `Performance/BurstRunner.cs` | Holds a small group of requests until a single start signal, then lets them all go at once. Used for race-condition tests |
| `Performance/BurstResult.cs` | Each burst response with its body, the spread between the first and last request starting, and the text summary |
| `Performance/RequestSample.cs` / `PerformanceTarget.cs` | A single timed request, and the endpoint being tested |
| `Support/PerformanceTestContext.cs` | Scenario-scoped state, injected by Reqnroll in the same way as `ApiTestContext` |

**What is reused from the functional suite:**

- **API clients.** `AuthenticationApiClient` and `OrdersApiClient` send every request, so the performance tests send exactly the same requests as the functional tests.
- **Steps.** `the PayPal sandbox API base URL is configured`, `valid PayPal client credentials`, `a valid PayPal access token has been obtained` and `an order request for 1 "T-Shirt" item ...` come from `AuthenticationSteps` and `OrdersSteps`.
- **Hooks and DI.** `PlaywrightHooks` sets up the Playwright `APIRequestContext` and registers the clients for each scenario, as it does for every other feature.
- **Token cache.** The order scenarios get their token from `AccessTokenStore`, so the whole run uses **one** OAuth token instead of authenticating for every request.

## The scenarios

### 1. Access token endpoint, sequential load

```gherkin
Given valid PayPal client credentials
And a load profile of 3 requests with 1 concurrent user at no more than 0.5 requests per second
When I run the load profile against the access token endpoint
Then every request in the load run should have succeeded
And no request in the load run should have been rate limited
And the observed request rate should not have exceeded the load profile limit
And the 95th percentile response time should be below 3000 milliseconds
```

This sends 3 requests to the token endpoint, one at a time and 2 seconds apart. The count is kept this low on purpose because repeatedly exchanging credentials is the pattern most likely to trigger fraud or abuse detection (see [Safety guardrails](#safety-guardrails)).

### 2. Order creation, light load (Scenario Outline)

```gherkin
Examples:
  | profile    | requests | users | rate | average ms | p95 ms |
  | sequential | 5        | 1     | 0.5  | 2000       | 3000   |
  | concurrent | 10       | 2     | 1    | 2000       | 3000   |
```

Each row is a separate load run that creates sandbox orders. The `concurrent` row has two virtual users sharing the same 1 request per second limit, which shows how the pacer handles concurrency. Each request sends a new `PayPal-Request-Id`, so PayPal really creates an order every time. Without that, PayPal would return its cached response for the first request.

### 3. Safety guardrail self-test

```gherkin
Given a load profile of 1000 requests with 50 concurrent users at no more than 100 requests per second
When I check the load profile against the safety limits for the create order endpoint
Then the load profile should be rejected by the safety limits
And no request should have been sent to PayPal
```

This scenario makes no network calls. It proves that a profile that is too aggressive is blocked before anything is sent, so a mistake in a feature file can't put load on PayPal.

### 4. Idempotency race (`@known-issue @ignore`)

```gherkin
@known-issue @ignore
Scenario: Simultaneous order requests sharing a PayPal-Request-Id create only one order
  Given a valid PayPal access token has been obtained
  And an order request for 1 "T-Shirt" item priced at "100.00" "USD" with intent "AUTHORIZE"
  And a burst of 3 simultaneous order requests sharing one PayPal-Request-Id
  When I release the burst against the create order endpoint
  And I resend the order request with the same PayPal-Request-Id after the burst
  Then the requests in the burst should have been released within 250 milliseconds of each other
  And no request in the burst should have been rate limited
  And exactly one order should have been created by the burst
  And every request in the burst should have returned that order
  And the resent request should return the same order
```

This is a race-condition test, not a latency test. PayPal treats `PayPal-Request-Id` as an idempotency key: requests that share one should all resolve to the **same** order. That is what prevents double orders, and double charges, when a client retries a request it thinks has timed out. Sending the requests one after another only proves that PayPal replays a saved response. To test the race, the requests have to arrive while the first one is still being processed.

How it works:

1. One `PayPal-Request-Id` is generated for the whole burst.
2. `BurstRunner` starts 3 requests that all wait on a single `TaskCompletionSource`. When it is released, each request continues on its own thread-pool thread (`RunContinuationsAsynchronously`), so all of them are sent at the same moment. The pacer is deliberately bypassed here.
3. The first step checks how far apart the requests actually started, and fails if it was 250 ms or more. This makes sure the test really created contention and isn't passing by accident. In practice the spread is under 2 ms.
4. The order ID is read from every 2xx response. There must be exactly one distinct ID.
5. One more request is sent with the same ID after the burst has finished. It must return that same order, which confirms the stored result PayPal ends up with.

**Current result: it fails, so it is skipped.** This scenario finds a real defect in PayPal's sandbox. It is tagged `@ignore` so it never breaks a test run, but the scenario and its implementation stay in place to reuse. See [Known issue](#known-issue-paypal-sandbox-idempotency-race).

### 5. Burst safety guardrail self-test

```gherkin
Scenario: A burst above the safety limit is rejected before any request is sent
  Given a burst of 50 simultaneous order requests sharing one PayPal-Request-Id
  When I check the burst against the safety limits
  Then the burst should be rejected by the safety limits
  And no request should have been sent to PayPal
```

Like scenario 3, this makes no network calls. It proves that `PERF_MAX_BURST_SIZE` stops an oversized burst before anything is sent.

## What happens during a run

1. **Background.** The base URL must point at the PayPal sandbox or a local mock, otherwise the scenario fails at once. Then the safety limits are loaded from the environment, or the defaults are used.
2. **Given.** Valid credentials or a cached token are prepared, and the load profile is read from the step text.
3. **When.**
   1. The profile is checked against the safety limits for that endpoint. Any violation fails the scenario **before any request is sent**.
   2. `LoadRunner` starts `ConcurrentUsers` virtual users. Each one takes the next request number and waits for a slot from the shared `RequestPacer`. Slots are at least `1 / MaxRequestsPerSecond` seconds apart across **all** users. Then it sends the request through the existing API client and times it with a `Stopwatch`.
   3. After each response, the **circuit breaker** decides whether to continue. A 429, 401 or 403 stops the run at once. So do 3 server errors (5xx) or network errors in a row. Requests are **never retried**.
   4. The samples are collected into a `LoadTestResult`. Its summary is written to the console and attached to the Allure scenario as **Performance summary: \<target\>**.
4. **Then.** The scenario asserts that every request succeeded, none was rate limited, the observed rate stayed within the limit (with 5% tolerance for timer resolution), and the response-time thresholds were met.

Example output, taken from a real sandbox run:

```
Performance run: CreateOrder
Profile:       10 requests, 2 concurrent users, max 1 req/s
Outcome:       completed
Duration:      10.25 s
Requests sent: 10 (10 succeeded, 0 rate limited)
Error rate:    0.0 %
Observed rate: 1.00 req/s
Response time: min 786 ms, avg 1116 ms, p50 1134 ms, p90 1246 ms, p95 1562 ms, max 1562 ms
  #  start (s)  duration (ms)  status
  1       0.00           1134  201
  2       1.00           1562  201
  ...
```

## Safety guardrails

PayPal protects its APIs with rate limiting and fraud and abuse monitoring. Sudden bursts, high request rates, many failed authentications, or anything that looks like scanning can get an app's credentials throttled or flagged. The tests prevent this in several ways:

| Guardrail | Where it lives | Why |
|---|---|---|
| **Sandbox only** | `PerformanceSettings.IsAllowedTarget` | Load is only sent to `*.sandbox.paypal.com` or `localhost`. Production `api-m.paypal.com` is always refused. |
| **Hard limits checked before sending** | `PerformanceSettings.Validate` | Each profile is checked against the maximum requests, concurrent users and requests per second. A profile that breaks any limit fails without sending anything. |
| **Lower limit for the token endpoint** | `PERF_MAX_TOKEN_REQUESTS` (default 5) | Many credential exchanges in a short time is a classic abuse signal. |
| **Global rate pacing** | `RequestPacer` | The overall rate stays under the limit however many virtual users there are. Requests are spread out evenly, with no bursts. |
| **Small, separately capped bursts** | `PerformanceSettings.ValidateBurst` (default 3) | Race tests have to skip the pacer, so they get their own very small limit. Three valid requests sharing one ID look like normal client retries, not abuse. A burst is a one-off: the scenario sends one burst and one follow-up request. |
| **Circuit breaker** | `LoadRunner.CircuitBreaker` | Stops at the first 429 (PayPal pushing back) and the first 401 or 403 (to avoid a run of failed authentication attempts). Also stops after repeated 5xx or network errors. A burst can't be stopped part-way because all its requests are already sent, which is why it is kept so small. It still fails if any request was rate limited. |
| **No retries** | `LoadRunner` | Retrying after errors increases load at the worst moment. |
| **Valid requests only** | `Performance.feature` | Invalid credentials, bogus IDs and malformed bodies are never sent under load. Repeated 401s look like credential stuffing, and repeated 404s look like enumeration. The functional suite covers those cases with a single request each. |
| **One cached token** | `AccessTokenStore` | The order scenarios use one token for the whole run, as PayPal recommends. |
| **Unique request IDs** | `PerformanceSteps` | Each order request gets a new `PayPal-Request-Id`, so no two requests look like replays of each other. |

### Default limits

| Environment variable | Default | Meaning |
|---|---|---|
| `PERF_MAX_TOTAL_REQUESTS` | `20` | Maximum requests in one load run (all endpoints except the token endpoint) |
| `PERF_MAX_TOKEN_REQUESTS` | `5` | Maximum requests in one load run against `/v1/oauth2/token` |
| `PERF_MAX_CONCURRENT_USERS` | `2` | Maximum requests in flight at the same time |
| `PERF_MAX_REQUESTS_PER_SECOND` | `1` | Maximum request rate (decimals are allowed, for example `0.5`) |
| `PERF_MAX_CONSECUTIVE_FAILURES` | `3` | Number of 5xx or network errors in a row that stops a run |
| `PERF_MAX_BURST_SIZE` | `3` | Maximum number of requests a race-condition burst may send at the same instant |

An invalid value, such as text, zero or a negative number, makes the scenario fail with a clear message. It never falls back silently to a default.

## Running the tests

```bash
# Performance scenarios only
dotnet test --filter "Category=performance"

# Functional scenarios only (everything except performance)
dotnet test --filter "Category!=performance"

# Everything (passes; the idempotency race test is reported as skipped)
dotnet test

# What CI runs: everything except known issues
dotnet test --filter "Category!=known-issue"
```

The idempotency race test is tagged `@ignore`, which Reqnroll treats as skip, so NUnit skips it before it starts: it shows as **Skipped** in `dotnet test`, sends no requests, and doesn't appear in the Allure report. To run it, for example to check whether PayPal has fixed the sandbox, remove `@ignore` from the scenario temporarily and run `dotnet test --filter "Category=known-issue"`.

Reqnroll turns the `@performance` tag into an NUnit category, which is why `--filter "Category=..."` works. Credentials come from the same `.env` file and `PAYPAL_*` variables described in the [README](README.md).

## CI integration

The performance scenarios are part of the normal `dotnet test` run in the **Test** job of `.github/workflows/test-and-deploy.yml`. The only pipeline change is `--filter "Category!=known-issue"` on the **Run tests** step, which is a second safeguard on top of `@ignore`:

- They use the same `PAYPAL_CLIENT_ID` and `PAYPAL_CLIENT_SECRET` secrets.
- Their results, including the attached performance summaries, are collected into the same Allure report and published to GitHub Pages.
- A failing threshold fails the **Test** check, which blocks auto-merge just like a functional failure does.
- Scenarios tagged `@known-issue` are skipped. They record defects in the API under test rather than in this repository, so they shouldn't block every PR. They also carry `@ignore`, so they are skipped in local runs too.

The thresholds are generous (2 s average, 3 s p95) so the shared sandbox doesn't cause flaky failures. Typical sandbox response times are around 1 s.

To run them as a separate step, for example on a schedule only, change the existing **Run tests** filter to `--filter "Category!=performance&Category!=known-issue"`. Then add a second step, or a separate job, with `--filter "Category=performance"`.

## Customising for more intense testing

### Change the load or thresholds of an existing scenario

Edit the numbers in the feature file, or add rows to the `Examples` table:

```gherkin
| soak-lite  | 20       | 2     | 1    | 2000       | 3000   |
```

If a profile goes over the default limits, the scenario fails and lists every limit it breaks. Raise the matching environment variable for that run:

```bash
PERF_MAX_TOTAL_REQUESTS=50 PERF_MAX_REQUESTS_PER_SECOND=2 dotnet test --filter "Category=performance"
```

In CI, set them as Actions **variables**, and pass them through in the `env:` block of the Test job.

### Add a new endpoint

1. Add the call to the relevant API client, as you would for a functional test.
2. Add a value to `PerformanceTarget`.
3. Add a `When I run the load profile against the <endpoint> endpoint` step to `PerformanceSteps` that calls `RunLoadProfileAsync(PerformanceTarget.<NewTarget>, () => client.CallAsync(...))`.
4. If the endpoint is sensitive, give it its own limit in `PerformanceSettings.Validate`, as the token endpoint has.
5. Write the scenario in `Performance.feature` using the existing `Given a load profile ...` and `Then ...` steps.

### Add a new assertion

The `Then` steps read from `LoadTestResult`, which already has `Percentile(n)`, `MinimumResponseTime`, `MaximumResponseTime`, `AverageResponseTime`, `ErrorRatePercent`, `ObservedRequestsPerSecond` and every raw `RequestSample`. Any percentile works without new code, for example `the 90th percentile response time should be below 2500 milliseconds`.

### Before going beyond a light baseline

- **Talk to PayPal first.** PayPal's sandbox is shared infrastructure, and its rate limits aren't published as fixed numbers. Real load or stress testing should be agreed with PayPal, or run against a mock of the API. `localhost` targets are already allowed for that reason. The Postman CLI or a WireMock-style server can serve the PayPal responses.
- **Use dedicated credentials.** Use a separate sandbox app for performance runs, so that if it does get throttled, the functional pipeline's credentials aren't affected.
- **Use a dedicated tool for high volumes.** This runner is built for small, controlled, assertion-driven runs inside the BDD suite. For hundreds of requests per second or long soak tests, use a tool such as k6, NBomber or JMeter. The safety rules in this document still apply.

## Known issue: PayPal sandbox idempotency race

**What happens:** when several create-order requests with the same `PayPal-Request-Id` arrive at the same time, the PayPal sandbox can create **more than one order**. The documented behaviour is a single order. The duplicates happen whenever the requests truly overlap.

**Evidence** (3 simultaneous requests, started less than 2 ms apart):

| Run | Responses | Distinct orders |
|---|---|---|
| 1 | `200 3GY24774F1929561M`, `201 3GY24774F1929561M`, `201 9JY63126N0583205D` | 2 |
| 2 | `201 33779090HW4897836`, `201 2A9308617K5868026`, `201 1MM546408C689752J` | 3 |
| 3 | `201 6F258771XB6974848`, `200 4X802107CA2855130`, `201 4X802107CA2855130` | 2 |

The 200 responses are PayPal replaying a saved response, which proves the header does reach PayPal. The key is recognised, but the check isn't atomic: requests that arrive before the first one has finished each create their own order.

**Why it matters:** a client that retries a create-order call it believes has timed out, while the first call is still being processed, can end up with duplicate orders. Each duplicate could then be approved and charged. Production may behave differently from the sandbox, so treat this as a sandbox result until it's confirmed. It's still worth guarding against in any integration, for example by deduplicating on your own order reference before calling PayPal, or by checking for an existing order before retrying.

**How it is handled:** the scenario keeps its strict assertion and is tagged `@known-issue @ignore`. `@ignore` skips it in every run, local and CI, without deleting it, and `@known-issue` records why. To check whether PayPal has fixed the sandbox, remove `@ignore` and run `dotnet test --filter "Category=known-issue"`. Once it passes, remove both tags so it guards against regressions on every PR. If you raise this with PayPal, the burst summary attached to the Allure report has the shared request ID and every order ID returned.

## Reading the numbers correctly

- **What a time includes.** Each time is measured around the API client call, so it covers the network round trip from the test machine and Playwright's small overhead, as well as PayPal's processing time. Times from a GitHub runner and from a laptop will differ.
- **Sandbox times are not production times.** The sandbox has different capacity and caching. Treat the results as a way to spot regressions over time, not as a production SLA.
- **Small samples.** With 3 to 10 requests, p95 is effectively the slowest request. The percentile assertions become meaningful as the sample size grows.
