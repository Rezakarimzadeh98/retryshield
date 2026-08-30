/**
 * Retry-safe operation store (Node.js)
 *
 * Teaching / single-process sketch. Persist to your DB in production.
 * Pair with RetryShield gateway when multiple instances share the same API.
 */

import { randomUUID } from "node:crypto";

/** @typedef {{ id: string, idempotencyKey: string, payload: unknown }} Operation */

const operations = new Map();

export function beginOperation(payload) {
  const operation = {
    id: randomUUID(),
    idempotencyKey: randomUUID(),
    payload,
  };
  // Persist BEFORE the first HTTP attempt.
  operations.set(operation.id, operation);
  return operation;
}

export function getOperation(id) {
  return operations.get(id) ?? null;
}

/**
 * @param {string} baseUrl e.g. http://localhost:8080
 * @param {string} path e.g. /proxy/payments
 * @param {string} operationId
 */
export async function postWithStableKey(baseUrl, path, operationId) {
  const operation = operations.get(operationId);
  if (!operation) throw new Error(`Unknown operation: ${operationId}`);

  const deadline = Date.now() + 30_000;
  let attempt = 0;

  while (Date.now() < deadline) {
    attempt += 1;
    let response;
    try {
      response = await fetch(`${baseUrl}${path}`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Key": operation.idempotencyKey,
        },
        body: JSON.stringify(operation.payload),
      });
    } catch {
      await sleep(backoffMs(attempt));
      continue;
    }

    const status = response.headers.get("Idempotency-Status");

    if (status === "created" || status === "replayed") return response;
    if (response.status === 409 && status === "processing") {
      await sleep(backoffMs(attempt));
      continue;
    }
    if (status === "indeterminate") {
      throw new Error("Indeterminate outcome — stop automatic retries and reconcile.");
    }
    if (response.status === 422 && status === "conflict") {
      throw new Error("Key reused with different input — fix the client.");
    }
    return response;
  }

  throw new Error("Still pending after retry deadline.");
}

function backoffMs(attempt) {
  return Math.min(1000 * 2 ** (attempt - 1), 5000) + Math.random() * 250;
}

function sleep(ms) {
  return new Promise((r) => setTimeout(r, ms));
}
