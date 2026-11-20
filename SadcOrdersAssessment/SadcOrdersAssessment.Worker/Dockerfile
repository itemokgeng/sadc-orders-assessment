# Build context: solution root
#   docker build -f SadcOrdersAssessment.Worker/Dockerfile -t sadc-orders-worker .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Domain/Domain.csproj Domain/
COPY Core/Core.csproj Core/
COPY DataLayer/DataLayer.csproj DataLayer/
COPY SadcOrdersAssessment.Worker/SadcOrdersAssessment.Worker.csproj SadcOrdersAssessment.Worker/
RUN dotnet restore SadcOrdersAssessment.Worker/SadcOrdersAssessment.Worker.csproj

COPY Domain/ Domain/
COPY Core/ Core/
COPY DataLayer/ DataLayer/
COPY SadcOrdersAssessment.Worker/ SadcOrdersAssessment.Worker/
RUN dotnet publish SadcOrdersAssessment.Worker/SadcOrdersAssessment.Worker.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# A worker has no HTTP endpoints, so the plain .NET runtime is enough (no ASP.NET).
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
# Folder for the rolling log files (Serilog). The app runs as the non-root "app" user, so it must own it.
RUN mkdir -p /app/logs && chown app:app /app/logs
USER app
ENTRYPOINT ["dotnet", "SadcOrdersAssessment.Worker.dll"]