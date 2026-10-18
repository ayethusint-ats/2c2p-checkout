# 2C2P Checkout

Checkout app using the 2C2P sandbox (Payment API v4.3, SGD): hosted-page payments (OneTime, and Recurring with card saving) and saved-card payments.

## Run it

**Requirements:** Docker Desktop (Compose v2). Nothing else needs installing. Ports 3000 and 8080 must be free.

1. **Clone and create `.env`:**
```bash
   git clone https://github.com/ayethusint-ats/2c2p-checkout.git
   cd 2c2p-checkout
   cp .env.example .env
```

2. **Fill in `.env`.** Compose stops with a clear message if a value is empty. Keep the prefilled `MYSQL_DATABASE` and `MYSQL_USER`.

   | Variable | What to put | Where to get it |
   |---|---|---|
   | `MYSQL_ROOT_PASSWORD` | Any password | Choose one |
   | `MYSQL_PASSWORD` | Any password | Choose one |
   | `TWOCTWOP_MERCHANT_ID` | Sandbox merchant ID | [2C2P Sandbox](https://developer.2c2p.com/docs/sandbox) |
   | `TWOCTWOP_SECRET_KEY` | Sandbox secret key | Same page |
   | `PUBLIC_API_BASE_URL` | `http://localhost:8080` for now (replaced in step 5) | ngrok |

   `.env` is git-ignored, so credentials are never committed. Use no trailing slash in URLs.

3. **Build and start** (the first build takes a few minutes):
```bash
   docker compose up --build -d
```
   Ready when `docker compose ps` shows `mysql` and `redis` as `healthy`, `api` and `frontend` as `Up`, and this returns 200:
```bash
   curl -i http://localhost:8080/health
```

4. **Open** the app at http://localhost:3000 and the API docs at http://localhost:8080/swagger.
   To try it: pick a customer and a product, click Checkout, and pay with a [Singapore test card](https://developer.2c2p.com/docs/reference-test-information-sg). A saved-card payment needs a card saved earlier through a Recurring product.

5. **Hosted-page payments (OneTime, Recurring) need a public URL.** 2C2P confirms them by calling the API's notification endpoint, which it can't reach on `localhost`. Saved-card payments don't need this step.
```bash
   ngrok config add-authtoken <your-token>   # once, from your ngrok dashboard
   ngrok http 8080
   # copy the https://… URL into .env as PUBLIC_API_BASE_URL, then:
   docker compose up -d --force-recreate api
```

6. **Stop and reset:**
```bash
   docker compose down      # stop, keep the data
   docker compose down -v   # stop and wipe the database and Redis data
```