# Checkout API — 2C2P Payment Integration

A checkout backend integrated with the **2C2P sandbox (Payment API v4.3, Singapore, SGD)**.
Customers pay on the 2C2P hosted payment page or with a saved card. Payment results arrive
through a signed server-to-server notification, and the checkout is safe to retry.

**Stack:** .NET 10 Web API (C#) · EF Core (Pomelo MySQL provider) · MySQL 8 · Redis ·
Docker Compose · xUnit + Testcontainers

## Architecture

Clean architecture in four projects:

| Project | Responsibility |
|---|---|
| `Checkout.Domain` | Entities (`Transaction`, `Customer`, `Product`, `CustomerCard`) and their rules |
| `Checkout.Application` | Use cases (checkout, payment-result processing, status) and the interfaces they need |
| `Checkout.Infrastructure` | The interface implementations: EF Core/MySQL, Redis, the 2C2P client |
| `Checkout.Api` | Minimal API endpoints, error handling, Swagger |

2C2P, MySQL and Redis are behind interfaces defined in Application. No 2C2P SDK is used:
requests go through a typed `HttpClient`, and HS256 JWTs are signed and verified in our own code.

## Running it locally

**Prerequisites:** .NET 10 SDK, Docker, and 2C2P sandbox credentials (Merchant ID and Secret Key).

```bash
# 1. Local secrets (both files are git-ignored)
cp .env.example .env
#    → set MYSQL_ROOT_PASSWORD and MYSQL_PASSWORD
cp src/Checkout.Api/appsettings.Development.example.json src/Checkout.Api/appsettings.Development.json
#    → set the same MySQL password and your 2C2P sandbox Merchant ID and Secret Key

# 2. MySQL and Redis
docker compose up -d

# 3. The API (applies migrations and seeds demo data on startup)
dotnet run --project src/Checkout.Api --launch-profile http
```

Swagger UI: **http://localhost:5099/swagger** (Development environment only).

Seeded data: 3 customers (ids 1–3) and 4 SGD products: two one-time (ids 1–2) and two
recurring (ids 3–4). Recurring products save the card for later payments.

## API

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/api/customers` | List customers (cached in Redis) |
| `GET` | `/api/products` | List products (cached in Redis) |
| `GET` | `/api/customers/{id}/cards` | A customer's saved cards (masked numbers only) |
| `POST` | `/api/checkout` | Start a payment. Requires an `Idempotency-Key` header (a new GUID per attempt) |
| `POST` | `/api/payments/2c2p/notification` | 2C2P's backend notification (signed JWT) |
| `GET` | `/api/transactions/{invoiceNo}` | Current payment status (never cached) |
| `GET` | `/health` | Liveness probe |

### Checkout flows

- **Hosted payment page** (no `customerCardId`): returns `Pending` and a `redirectUrl` to the
  2C2P payment page. The final result arrives through the backend notification.
- **Saved card** (`customerCardId` set): Payment Token → Do Payment → Payment Inquiry. The
  response is `Succeeded`/`Failed`; or `Pending` with a `redirectUrl` when 3-D Secure is needed;
  or `Pending` without one when the outcome is not known yet. In that last case, poll
  `GET /api/transactions/{invoiceNo}`, which asks 2C2P for the result.

### Receiving real notifications locally

2C2P cannot reach `localhost`. To receive real notifications, expose the API through a tunnel
(e.g. `ngrok http 5099`), set `App:PublicApiBaseUrl` to the tunnel URL, and restart. The URL is
sent with every payment request, so nothing needs changing in the 2C2P portal.

## Key design decisions

- **Idempotent checkout.** The same `Idempotency-Key` always gives the same transaction and
  calls 2C2P at most once. The guards: a stored-row replay, a Redis lock per key (concurrent
  duplicates wait for the first request's result), and a MySQL unique index on the key. The
  index is the final guard, and also covers Redis being down.
- **The row is saved before 2C2P is called**, so a crash can never leave a 2C2P payment without
  our record of it.
- **Every 2C2P response and notification is signature-verified** (HS256, constant-time compare,
  only `alg: HS256` accepted) before anything in it is read. Amount and currency must match
  the stored transaction.
- **Status changes are atomic and one-way:** a conditional `UPDATE … WHERE status = 'Pending'`,
  so duplicate or out-of-order notifications can never overwrite a final result.
- **An unknown outcome is never marked as failed.** If a saved-card charge times out, the
  transaction stays `Pending` until a verified result (inquiry or notification) arrives.
- **Money and card data:** amounts are `DECIMAL(12,2)` and come from the product, never the
  request. Only 2C2P's customer token and a masked card number are stored, never a full card
  number or CVV.
- **Caching:** the catalog is cached in Redis (cache-aside, with a TTL, falling back to MySQL if
  Redis fails). Saved cards and transaction status are never cached.

## Tests

```bash
dotnet test                                              # everything (Docker must be running)
dotnet test --filter "FullyQualifiedName~Integration"    # integration tests only
dotnet test --filter "FullyQualifiedName!~Integration"   # unit tests only (no Docker needed)
```

- **Unit tests** cover the services, domain rules and the 2C2P client, using in-memory fakes.
- **Integration tests** start real MySQL 8.4 and Redis 7 containers (Testcontainers) and run the
  whole API in memory. Only the network hop to 2C2P is replaced, by a fake that signs its
  responses. They cover:
  - 10 and 25 parallel checkouts with one key → exactly one transaction and one 2C2P call
  - the same scenario with a working lock, no lock, and Redis down
  - concurrent duplicate notifications
  - tampered signatures
  - the database constraints, and what is cached and what is not

The tests use made-up credentials, so no secrets or 2C2P account are needed to run them.
