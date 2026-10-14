# Project context
Take-home assessment: a checkout app using the 2C2P sandbox
(Payment API v4.3, Singapore, SGD).

Stack: .NET 10 Web API (C#), EF Core with a MySQL provider (Pomelo 9.x on
net10.0), MySQL 8, Redis (StackExchange.Redis), Next.js + TypeScript +
Tailwind, Docker Compose, xUnit + Testcontainers.

Architecture: clean architecture with Domain, Application, Infrastructure and
Api projects. 2C2P, MySQL and Redis sit behind interfaces defined in
Application and implemented in Infrastructure.

Rules:
- No 2C2P SDK. Use a typed HttpClient via IHttpClientFactory and sign/verify
  HS256 JWTs myself.
- Never commit secrets. Keep appsettings.Development.json in .gitignore.
- Money is DECIMAL(12,2). Never store full card numbers or CVV.
- Work in small steps. Do only what the current step asks, no extra features.
- Briefly explain each design decision, because I must explain every line in
  an interview.
