import http from "k6/http";
import { check, sleep } from "k6";
import { Counter, Rate } from "k6/metrics";

const replays = new Counter("retryshield_test_replays");
const created = new Counter("retryshield_test_created");
const conflicts = new Counter("retryshield_test_conflicts");
const failures = new Rate("retryshield_test_failures");
const baseUrl = __ENV.BASE_URL || "http://localhost:8080";

export const options = {
  scenarios: {
    fifty_concurrent_claims: {
      executor: "shared-iterations",
      vus: 50,
      iterations: 50,
      maxDuration: "30s",
    },
  },
  thresholds: {
    http_req_failed: ["rate<0.01"],
    http_req_duration: ["p(95)<1000"],
    retryshield_test_failures: ["rate<0.01"],
    retryshield_test_created: ["count==1"],
  },
};

export default function () {
  const key = __ENV.IDEMPOTENCY_KEY || `k6-${__ENV.RUN_ID || "local"}`;
  const response = http.post(
    `${baseUrl}/proxy/payments`,
    JSON.stringify({ amount: 42.5, currency: "USD" }),
    {
      headers: {
        "Content-Type": "application/json",
        "Idempotency-Key": key,
      },
      tags: { name: "claim-order" },
    },
  );

  const ok = check(response, {
    "accepted success or replay": (r) => [200, 201, 202].includes(r.status),
    "not indeterminate": (r) => r.status !== 503,
  });
  failures.add(!ok);
  if (response.headers["Idempotency-Status"] === "created") created.add(1);
  if (response.headers["Idempotency-Status"] === "replayed") replays.add(1);
  if (response.status === 409) conflicts.add(1);
  sleep(0.1);
}
