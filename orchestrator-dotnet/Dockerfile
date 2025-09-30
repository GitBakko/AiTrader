# syntax=docker/dockerfile:1.7

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore
COPY src/Orchestrator.csproj ./
RUN dotnet restore Orchestrator.csproj

# Publish
COPY src/ ./
RUN dotnet publish Orchestrator.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS="http://+:5010"

RUN apt-get update && apt-get install -y --no-install-recommends curl \
	&& rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

EXPOSE 5010

ENTRYPOINT ["dotnet", "Orchestrator.dll"]
