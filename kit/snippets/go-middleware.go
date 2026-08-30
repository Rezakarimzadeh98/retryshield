// Go middleware sketch — single-process / learning only.
// Use RetryShield gateway when multiple instances share the API.
//
//	mux := http.NewServeMux()
//	mux.Handle("/payments", Idempotency(http.HandlerFunc(handlePayment)))

package sketches

import (
	"bytes"
	"crypto/sha256"
	"encoding/hex"
	"io"
	"net/http"
	"sync"
)

type entry struct {
	fingerprint string
	status      string // processing | completed
	statusCode  int
	body        []byte
	contentType string
}

var store sync.Map // key -> *entry

func fingerprint(method, path, contentType string, body []byte) string {
	sum := sha256.Sum256([]byte(method + ":" + path + ":" + contentType + ":" + string(body)))
	return hex.EncodeToString(sum[:])
}

// Idempotency wraps a handler with claim / replay / conflict semantics.
func Idempotency(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		key := r.Header.Get("Idempotency-Key")
		if key == "" {
			http.Error(w, `{"error":"Idempotency-Key required"}`, http.StatusBadRequest)
			return
		}

		body, err := io.ReadAll(r.Body)
		if err != nil {
			http.Error(w, `{"error":"cannot read body"}`, http.StatusBadRequest)
			return
		}
		_ = r.Body.Close()
		r.Body = io.NopCloser(bytes.NewReader(body))

		fp := fingerprint(r.Method, r.URL.Path, r.Header.Get("Content-Type"), body)

		if raw, ok := store.Load(key); ok {
			e := raw.(*entry)
			if e.fingerprint != fp {
				w.Header().Set("Idempotency-Status", "conflict")
				http.Error(w, `{"error":"conflict"}`, http.StatusUnprocessableEntity)
				return
			}
			if e.status == "completed" {
				w.Header().Set("Idempotency-Status", "replayed")
				if e.contentType != "" {
					w.Header().Set("Content-Type", e.contentType)
				}
				w.WriteHeader(e.statusCode)
				_, _ = w.Write(e.body)
				return
			}
			w.Header().Set("Idempotency-Status", "processing")
			http.Error(w, `{"error":"processing"}`, http.StatusConflict)
			return
		}

		store.Store(key, &entry{fingerprint: fp, status: "processing"})

		rec := &captureWriter{ResponseWriter: w, status: 200}
		next.ServeHTTP(rec, r)

		ct := rec.Header().Get("Content-Type")
		store.Store(key, &entry{
			fingerprint: fp,
			status:      "completed",
			statusCode:  rec.status,
			body:        append([]byte(nil), rec.buf.Bytes()...),
			contentType: ct,
		})
		w.Header().Set("Idempotency-Status", "created")
	})
}

type captureWriter struct {
	http.ResponseWriter
	status int
	buf    bytes.Buffer
}

func (c *captureWriter) WriteHeader(code int) {
	c.status = code
	c.ResponseWriter.WriteHeader(code)
}

func (c *captureWriter) Write(b []byte) (int, error) {
	_, _ = c.buf.Write(b)
	return c.ResponseWriter.Write(b)
}