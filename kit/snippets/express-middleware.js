/**
 * Express middleware sketch — single-process claim + replay.
 *
 * NOT safe across multiple Node processes. Use RetryShield gateway
 * (PostgreSQL authority) when you scale horizontally.
 */

import { createHash } from "node:crypto";

const store = new Map(); // key -> { fingerprint, status, statusCode, headers, body }

function fingerprint(req) {
  const raw = `${req.method}:${req.path}:${req.headers["content-type"] ?? ""}:${JSON.stringify(req.body ?? null)}`;
  return createHash("sha256").update(raw).digest("hex");
}

export function idempotencyMiddleware({ header = "idempotency-key" } = {}) {
  return async function idempotency(req, res, next) {
    const key = req.header(header);
    if (!key) {
      res.status(400).json({ error: "Idempotency-Key required" });
      return;
    }

    const fp = fingerprint(req);
    const existing = store.get(key);

    if (existing) {
      if (existing.fingerprint !== fp) {
        res.status(422).set("Idempotency-Status", "conflict").json({ error: "conflict" });
        return;
      }
      if (existing.status === "completed") {
        res
          .status(existing.statusCode)
          .set("Idempotency-Status", "replayed")
          .set(existing.headers)
          .send(existing.body);
        return;
      }
      if (existing.status === "processing") {
        res.status(409).set("Idempotency-Status", "processing").json({ error: "processing" });
        return;
      }
    }

    store.set(key, { fingerprint: fp, status: "processing" });

    const originalJson = res.json.bind(res);
    res.json = (body) => {
      store.set(key, {
        fingerprint: fp,
        status: "completed",
        statusCode: res.statusCode || 200,
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      res.set("Idempotency-Status", "created");
      return originalJson(body);
    };

    next();
  };
}
