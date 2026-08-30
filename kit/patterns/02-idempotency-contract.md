# Idempotency contract

Implementations in this repo—and anything you copy from the kit—should behave like this:

| Situation | Expected behavior |
| --- | --- |
| First valid key | Claim ownership, then run the side effect once |
| Same key + same request after success | Return the stored response (`replayed`) |
| Same key + different body/fingerprint | Reject (`422` / conflict)—do not run again |
| Same key while still processing | Wait briefly or return `409` / processing—do not start a second side effect |
| Outcome unknowable after dispatch | Mark indeterminate—**no automatic retry with a new key** |

## Client rules

1. Create one key per logical operation (one checkout, one payout, one booking).  
2. Persist the key **before** the first network attempt.  
3. On timeout or transport error, retry with the **same** key and payload.  
4. Never mint a replacement key because you are unsure.  
5. Treat indeterminate as an incident, not a green light to try again.

## Server rules

1. Claim the key **before** calling payment providers or writing irreversible state.  
2. Store enough of the response to replay exactly.  
3. Fingerprint the request so key reuse with different input is rejected.  
4. Prefer an explicit indeterminate state over guessing.

Full gateway semantics: [guarantees](../../docs/guarantees.md) and [client-integration](../../docs/client-integration.md).
