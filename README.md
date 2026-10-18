# 2C2P Checkout

Checkout app using the 2C2P sandbox (Payment API v4.3, SGD): hosted-page payments (OneTime, and Recurring with card saving) and saved-card payments.

## Requirements

- Docker Desktop (Compose v2), running. Nothing else needs installing.
- Ports 3000 and 8080 free.
- A free [ngrok](https://ngrok.com) account, for hosted-page payments (see step 2).
- 2C2P sandbox credentials: merchant ID and secret key from the [2C2P Sandbox page](https://developer.2c2p.com/docs/sandbox).

## Setup

1. **Clone and create `.env`:**
```bash
   git clone https://github.com/ayethusint-ats/2c2p-checkout.git
   cd 2c2p-checkout
   cp .env.example .env
```

2. **Start a public tunnel (ngrok).** 2C2P confirms hosted-page payments (OneTime, Recurring) by calling the API's notification endpoint, which it cannot reach on `localhost`. Saved-card payments don't need this step; if you only test those, skip to step 3 and keep `PUBLIC_API_BASE_URL=http://localhost:8080`.

   Sign in to the [ngrok dashboard](https://dashboard.ngrok.com/get-started/share-localhost), copy your authtoken, then in a **separate terminal**:
```bash
   ngrok config add-authtoken <your-token>   # once
   ngrok http 8080                           # keep this terminal open
```
   Copy the `https://…` forwarding URL it prints. Requests that pass through the tunnel can be inspected at http://127.0.0.1:4040.

3. **Fill in `.env`.** Compose stops with a clear message if a value is empty. Keep the prefilled `MYSQL_DATABASE` and `MYSQL_USER`.

   | Variable | What to put |
   |---|---|
   | `MYSQL_ROOT_PASSWORD` | Any password |
   | `MYSQL_PASSWORD` | Any password |
   | `TWOCTWOP_MERCHANT_ID` | Sandbox merchant ID |
   | `TWOCTWOP_SECRET_KEY` | Sandbox secret key |
   | `PUBLIC_API_BASE_URL` | The ngrok `https://…` URL from step 2, with no trailing slash |

   `.env` is git-ignored, so credentials are never committed.

## Run

4. **Build and start** (the first build takes a few minutes):
```bash
   docker compose up --build -d
```
   Ready when `docker compose ps` shows `mysql` and `redis` as `healthy` and `api` and `frontend` as `Up`, and this returns 200:
```bash
   curl -i http://localhost:8080/health
```

5. **Open** the app at http://localhost:3000 and the API docs at http://localhost:8080/swagger.

   To try it: pick a customer and a product, click Checkout, and pay with a [Singapore test card](https://developer.2c2p.com/docs/reference-test-information-sg) (use a declined card from the same page to see the failure path). A saved-card payment needs a card saved earlier through a Recurring product.

## Stop and reset

```bash
docker compose down      # stop, keep the data
docker compose down -v   # stop and wipe the database and Redis data
```

If ngrok is restarted, its URL changes (free plan). Update `PUBLIC_API_BASE_URL` in `.env`, then run `docker compose up -d --force-recreate api`.