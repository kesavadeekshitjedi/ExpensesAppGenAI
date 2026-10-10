# Known Issues

A lightweight log of bugs and their status. Newest first.

---

## #1 — Cost Management 429 throttling surfaces as a hard error

- **Status:** Fixed (2026-10-09)
- **Severity:** Medium — intermittent, blocks the view when it happens
- **Affects:** WinUI 3 app (and any front-end, since the cause is in `AzureCost.Core`)

### Symptom
Flicking between subscriptions, periods, or resource groups fires a burst of Cost
Management queries. The service throttles and returns **HTTP 429 TooManyRequests**, which
bubbled straight up into the error InfoBar:

> Could not load cost data — Azure request failed (429 TooManyRequests) (throttled by Cost
> Management — retry shortly): Too many requests. Please retry.

The data was fine; the app just didn't retry the transient throttle.

### Cause
`ArmClient` sent each query once and threw `AzureCostException` on any non-success status.
There was no retry/backoff, so a 429 (which Cost Management expects clients to retry) became
a user-visible failure.

### Fix
Added `Internal/ThrottleRetryHandler.cs`, a `DelegatingHandler` registered **outside**
`BearerTokenHandler` in the HTTP pipeline (`ServiceCollectionExtensions`). It:
- retries 429 and transient 502/503/504 responses,
- honours a server-supplied `Retry-After` header, else backs off exponentially with jitter,
- re-sends each attempt on a fresh request clone (a buffered body) so the auth handler stamps
  a fresh bearer token each time.

Tunable via `AzureCostOptions.MaxRetries` (default 4) and `RetryBaseDelay` (default 1s).

### Follow-ups
- ✅ Debounce on rapid selection changes (300ms) in `MainViewModel` — subscription/period/
  resource-group changes now coalesce, so a flurry of changes fires one query, not a burst.
- ✅ Unit tests for `ThrottleRetryHandler` in `tests/AzureCost.Core.Tests` (retry-then-success,
  max-retries exhaustion, transient 5xx, no-retry on 4xx, `Retry-After` honoured, body resent).
