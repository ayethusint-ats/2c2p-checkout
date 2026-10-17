# 2C2P Checkout

A checkout app that takes payments through the 2C2P sandbox (Payment API v4.3, SGD).

## Run it

1. **Prerequisites:** Docker Desktop (Compose v2, `docker compose`). A free [ngrok](https://ngrok.com) account, only needed for hosted-page payments (step 6).

2. **Clone and create `.env`:**
   ```bash
   git clone https://github.com/ayethusint-ats/2c2p-checkout.git
   cd 2c2p-checkout
   cp .env.example .env
   ```

3. **Fill in `.env`.** Compose stops with a clear message if any of these is empty. Keep the prefilled `MYSQL_DATABASE` and `MYSQL_USER`.

   | Variable | What to put | Where to get it |
   |---|---|---|
   | `MYSQL_ROOT_PASSWORD` | Any password | Choose one |
   | `MYSQL_PASSWORD` | Any password | Choose one |
   | `TWOCTWOP_MERCHANT_ID` | 2C2P sandbox merchant ID | 2C2P sandbox ("Sandbox" link in the assessment brief) |
   | `TWOCTWOP_SECRET_KEY` | 2C2P sandbox secret key | Same place |
   | `PUBLIC_API_BASE_URL` | Public HTTPS URL of the API (step 6); until then `http://localhost:8080` | ngrok |

   `.env` is git-ignored, so the 2C2P credentials are never committed.

4. **Build and start:**
   ```bash
   docker compose up --build
   ```

5. **Open** the frontend at http://localhost:3000 and Swagger at http://localhost:8080/swagger. A healthy start: `docker compose ps` (in a second terminal) shows `mysql` and `redis` as `healthy`, and `api` and `frontend` as `Up`.

6. **Hosted-page payments (OneTime, Recurring)** are confirmed by 2C2P calling the API's notification endpoint, which it can't reach on `localhost`. Saved-card payments don't need this step.
   ```bash
   ngrok http 8080
   # put the https://… URL it prints into .env as PUBLIC_API_BASE_URL, then:
   docker compose up -d --force-recreate api
   ```

7. **Stop and reset:**
   ```bash
   docker compose down      # stop, keep the data
   docker compose down -v   # stop and wipe the database and Redis data
   ```
