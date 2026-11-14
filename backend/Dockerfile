FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Api/HRNexus.Api.csproj -c Release -o /publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /publish .
RUN mkdir -p logs

# Build stamp — surfaced on GET /api/health (Build:Version config key).
ARG BUILD_VERSION=dev
ARG BUILD_SHA=local
ARG BUILT_AT=
ENV Build__Version=$BUILD_VERSION \
    Build__Commit=$BUILD_SHA \
    Build__BuiltAt=$BUILT_AT

EXPOSE 8080
ENTRYPOINT ["dotnet", "HRNexus.Api.dll"]
