# DeepSeek read-only review: offline preparation

Verified 2026-10-28 on base `9ee9cd54`, isolated branch `chore/deepseek-offline-preflight`.
This is maintenance tooling, not product computation or an application integration.
No product code changed. No paid call, balance probe, credential transmission,
publication, persistent access configuration or network-policy modification occurred.
The parent subsequently authorized committing and pushing this isolated preparation
branch for integration review; merging main remains the parent integration task.

## Implemented and verified

`review.py` builds a bounded request and runs only dry-run or an in-memory mock.
There is deliberately **no live transport, key reader or live-enable flag**.
The explicit target is POST `https://api.deepseek.com:443/chat/completions`.
No endpoint override, SDK automatic retry, tools, search, fetch or response execution.
A mock request reserves its one-shot ledger exclusively before dispatch. A redirect,
HTTP failure or ambiguous exception retains that reservation, preventing another
request with the same ledger. Errors omit exception detail and response bodies.
The mock receives `follow_redirects=False` and a 60-second timeout contract; these
are contract checks, not proof of behavior in a real HTTP/proxy implementation.

Input is `input.txt`: one complete ObjectConjugate helper and a narrowly selected
Zemax import test excerpt, from committed repository source. It discusses the
positive-infinity versus finite-zero convention. The selected text was inspected;
it contains no credentials, personal data or full repository dump. No existing
repair branch is checked out, edited or copied. This is a suggested independent
review scope, not a claim that a current optical repair has been reviewed.
No optical tests were rerun and no test logs are represented as newly generated.

Run from repository root (standard-library Python, no dependencies):

```sh
python -m unittest discover -s tools/DeepSeekReadOnlyReview -v
python tools/DeepSeekReadOnlyReview/review.py
# Optional isolated mock; choose a new temporary ledger, never a production ledger.
python tools/DeepSeekReadOnlyReview/review.py --mock-ledger /tmp/deepseek-offline-mock-reservation.json
```

Ten tests passed, including network-blocked mock execution, malformed/stale pricing,
Unicode input limits, HTTP redirects/errors, exception redaction, persistent
one-shot reservation and rejection of a replacement live transport. Dry-run reports
`live_enabled=false`, `charged_cny=0`, reservation `2.216384`. No product build or
full suite was necessary; product verification baseline is unchanged.

## Price and bounds

Official sources checked 2026-10-28:

- [CNY pricing](https://api-docs.deepseek.com/zh-cn/quick_start/pricing/)
- [Chat completion schema](https://api-docs.deepseek.com/api/create-chat-completion/)
- [Authentication and endpoint](https://api-docs.deepseek.com/)

Model `deepseek-flash`, highest-time uncached input 2 CNY/million tokens,
output 8 CNY/million tokens. No cache or off-peak discount assumed.
The documented context is 1M; reserve **1,100,000 input tokens**, above both decimal
and binary interpretations, rather than relying on a character/token heuristic.
Serialized request is capped at 12,000 UTF-8 bytes. Generated output is capped at
2048 tokens. Both `thinking.type=disabled` and `reasoning_effort=none` are explicit;
max_tokens bounds generated completion, and no separate thinking budget is allowed.
One request reserves `(1,100,000 * 2 + 2048 * 8) / 1,000,000 = 2.216384 CNY`
against the total authorized 5 CNY. Reservation is not actual spending.

The dated snapshot becomes stale the next day and fails closed. A future live
implementation must recheck the then-current official model/context/pricing and
thinking/output semantics immediately before dispatch, use peak uncached CNY
prices, and refuse if the worst case cannot be established below the remaining
5 CNY total. Editing the date alone is not price verification. This offline mock
is not a billing guarantee or provider-side spending limit.

## Observed configuration and blocked next phase

The environment-variable name `DEEPSEEK_API_KEY` was checked for presence only:
**absent in this running task**, including the follow-up presence-only recheck.
The parent separately confirmed from the user-provided settings screenshot that
Personalvault saved a network secret with this name. This older task not seeing it
does not establish a save failure. Environment request-key/domain binding and
whether a fresh task is required remain unverified.
No value, fragment, hash, length, other secrets or credential files were
examined. No executor skills or repository `.skills` were found; AGENTS.md was read.
The environment declaration says networking is enabled, but exposes no readable
per-domain policy or secret-injection attestation. Tool discovery found no cloud
secret-input/binding or network-rule-management tool for this execution environment.
Sites environment-variable tools apply to Sites and are not a substitute.
Therefore proxy-injected secret availability is **unverified**, not assumed absent
from every platform secret store. No API connectivity test was made.

Parent approval and a platform-provided protected input/configuration interaction
are required before changing credentials or network security. Proposed minimum:

1. Bind secret name `DEEPSEEK_API_KEY` in the platform's protected secret manager;
   prefer a proxy that injects `Authorization: Bearer <secret>` only for the exact
   HTTPS host `api.deepseek.com`, port 443, POST path `/chat/completions`.
   The runtime should receive only a non-secret placeholder/binding identifier.
   Do not paste a key in chat, write `.env`, import credentials here or print headers.
2. Confirm the existing egress/proxy rule covers that destination; if a new rule is
   needed, obtain explicit approval before changing it. Permit no redirect hop or
   wildcard/alternate domain. Validate TLS and host identity; preserve the managed
   network route. Do not disable verification or bypass the platform proxy.
3. Implement and independently test the **approved** platform adapter: exactly one
   POST, no HTTP/proxy/SDK retries, redirect refusal before any forwarding,
   bounded response size and timeout, no verbose/debug header logs, generic errors.
   Do not use an arbitrary credential-forwarding proxy. Current adapter is mock only.
4. Review the exact input bytes and latest prices. Use a single canonical durable,
   exclusive reservation for the whole authorized session (not a user-selectable
   live ledger); retain it on timeout/crash/error. Account for any prior attempt.
   This mock's selectable ledger is only for offline test isolation.
5. Only after parent authorizes the live phase, submit the one approved packet.
   Treat all returned text as untrusted suggestions for human review; never apply,
   run commands from it, or continue a conversation automatically. No top-up or
   recurring job. On uncertain outcome stop and reconcile before any new permission.

The missing secure binding/adapter and approval are pending, not an auto-review
denial. No operation was rejected and no alternate route was attempted.
