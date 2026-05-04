FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY jobtracker.csproj ./
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish

# Some .NET 10 SDK configurations don't physically materialize the Blazor
# framework JS in the publish output, so /_framework/blazor.web.js 404s in
# production. As a robust fallback, locate the file in the SDK image and
# copy it into wwwroot/_framework/. Fail the build if it can't be found.
RUN set -eux; \
    if [ ! -f /app/publish/wwwroot/_framework/blazor.web.js ]; then \
        SRC="$(find / -name 'blazor.web.js' -type f 2>/dev/null | head -1)"; \
        echo "Locating blazor.web.js: ${SRC:-NOT FOUND}"; \
        test -n "$SRC"; \
        mkdir -p /app/publish/wwwroot/_framework; \
        cp "$SRC" /app/publish/wwwroot/_framework/blazor.web.js; \
    fi; \
    test -f /app/publish/wwwroot/_framework/blazor.web.js

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
