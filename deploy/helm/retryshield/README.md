# RetryShield Helm Chart

This chart deploys a minimal RetryShield control plane in Kubernetes:

- Gateway
- Admin API
- Admin Dashboard

The chart does not deploy PostgreSQL or Redis. Provide connection strings through a Kubernetes Secret.

## Quick Start

1. Create a namespace:

```bash
kubectl create namespace retryshield
```

2. Create the required secret:

```bash
kubectl -n retryshield create secret generic retryshield-secrets \
  --from-literal=postgresConnectionString='Host=postgres;Port=5432;Database=retryshield;Username=retryshield;Password=change-me' \
  --from-literal=redisConnectionString='redis:6379,password=change-me,ssl=false' \
  --from-literal=encryptionKeyBase64='replace-with-base64-key' \
  --from-literal=adminBearerToken='replace-with-strong-token'
```

3. Install:

```bash
helm upgrade --install retryshield ./deploy/helm/retryshield \
  --namespace retryshield \
  --set gateway.upstreamBaseUrl='https://your-upstream.example/'
```

4. Validate rendering before install:

```bash
helm lint ./deploy/helm/retryshield
helm template retryshield ./deploy/helm/retryshield --namespace retryshield
```

The chart includes `values.schema.json`, so `helm lint` validates value structure and required settings before deployment.

For example, this fails fast if required values are missing or invalid:

```bash
helm lint ./deploy/helm/retryshield --set gateway.upstreamBaseUrl=''
```

## Important Values

- `secrets.existingSecret`: Secret name containing required credentials
- `gateway.upstreamBaseUrl`: Protected upstream base URL
- `gateway.maxBodyBytes`, `gateway.maxResponseBodyBytes`
- `gateway.duplicateWait`, `gateway.recordTtl`, `gateway.processingTimeout`
- `adminApi.allowedOrigins`: CORS allowed origins for admin API

## Secret-managed Settings

- `RetryShield__PostgresConnectionString`
- `RetryShield__RedisConnectionString`
- `RetryShield__EncryptionKeyBase64`
- `Admin__BearerToken`
