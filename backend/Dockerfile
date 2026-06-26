# --- build -----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY MediaHub.Api/MediaHub.Api.csproj MediaHub.Api/
RUN dotnet restore MediaHub.Api/MediaHub.Api.csproj

COPY . .
RUN dotnet publish MediaHub.Api/MediaHub.Api.csproj -c Release -o /app /p:UseAppHost=false

# --- runtime ---------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "MediaHub.Api.dll"]
