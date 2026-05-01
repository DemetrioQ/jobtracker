FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY jobtracker.csproj ./
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN mkdir -p /app/Data && chown -R app:app /app
USER app

COPY --from=build --chown=app:app /app/publish ./

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    ConnectionStrings__DefaultConnection="DataSource=/app/Data/app.db;Cache=Shared"

EXPOSE 8080

ENTRYPOINT ["dotnet", "jobtracker.dll"]
