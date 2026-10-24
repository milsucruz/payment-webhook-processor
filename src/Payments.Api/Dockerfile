# Build context is the repository root, because Directory.Build.props and the
# project references live outside this folder:
#   docker build -f src/Payments.Api/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Restore first, from the project files only, so this layer stays cached
# until a dependency changes.
COPY global.json Directory.Build.props ./
COPY src/Payments.Domain/Payments.Domain.csproj src/Payments.Domain/
COPY src/Payments.Application/Payments.Application.csproj src/Payments.Application/
COPY src/Payments.Infrastructure/Payments.Infrastructure.csproj src/Payments.Infrastructure/
COPY src/Payments.Api/Payments.Api.csproj src/Payments.Api/
RUN dotnet restore src/Payments.Api/Payments.Api.csproj

COPY src/ src/
COPY web/ web/
RUN dotnet publish src/Payments.Api/Payments.Api.csproj -c Release --no-restore -o /app

# The runtime image has no SDK and no source code.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
# Non-root user that ships with the .NET images.
USER $APP_UID
# ASP.NET Core images listen on 8080 by default.
EXPOSE 8080
ENTRYPOINT ["dotnet", "Payments.Api.dll"]
