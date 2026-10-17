# API image. Build from the repo root (the build needs all four src projects):
#   docker build -f src/Checkout.Api/Dockerfile .

# ---- build stage: full SDK, thrown away after publishing ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first, then restore. This layer is reused until a .csproj changes,
# so editing C# code doesn't re-download every NuGet package.
COPY src/Checkout.Domain/Checkout.Domain.csproj src/Checkout.Domain/
COPY src/Checkout.Application/Checkout.Application.csproj src/Checkout.Application/
COPY src/Checkout.Infrastructure/Checkout.Infrastructure.csproj src/Checkout.Infrastructure/
COPY src/Checkout.Api/Checkout.Api.csproj src/Checkout.Api/
RUN dotnet restore src/Checkout.Api/Checkout.Api.csproj

# Then the sources (tests and the frontend are excluded by .dockerignore), and publish.
COPY src/ src/
RUN dotnet publish src/Checkout.Api/Checkout.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---- runtime stage: ASP.NET runtime only, no SDK or source code ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Plain HTTP on 8080 inside the container (a non-root user can't bind ports below 1024).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# The .NET images include a non-root "app" user; $APP_UID is its id.
USER $APP_UID
ENTRYPOINT ["dotnet", "Checkout.Api.dll"]
