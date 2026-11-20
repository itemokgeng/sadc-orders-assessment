# Build context: solution root
#   docker build -f SadcOrdersAssessment/Dockerfile -t sadc-orders-api .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore in its own layer, so code-only changes reuse the cached packages.
COPY Domain/Domain.csproj Domain/
COPY Core/Core.csproj Core/
COPY DataLayer/DataLayer.csproj DataLayer/
COPY LogicLayer/LogicLayer.csproj LogicLayer/
COPY SadcOrdersAssessment/SadcOrdersAssessment.csproj SadcOrdersAssessment/
RUN dotnet restore SadcOrdersAssessment/SadcOrdersAssessment.csproj

COPY Domain/ Domain/
COPY Core/ Core/
COPY DataLayer/ DataLayer/
COPY LogicLayer/ LogicLayer/
COPY SadcOrdersAssessment/ SadcOrdersAssessment/
RUN dotnet publish SadcOrdersAssessment/SadcOrdersAssessment.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# The .NET 8 images ship a non-root "app" user.
# Folder for the rolling log files (Serilog). The app runs as the non-root "app" user, so it must own it.
RUN mkdir -p /app/logs && chown app:app /app/logs
USER app
ENTRYPOINT ["dotnet", "SadcOrdersAssessment.dll"]