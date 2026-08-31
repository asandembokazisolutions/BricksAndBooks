# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY BricksAndBooks.csproj ./
RUN dotnet restore "BricksAndBooks.csproj"

COPY . ./
RUN dotnet publish "BricksAndBooks.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    --property:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000

COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "BricksAndBooks.dll"]
