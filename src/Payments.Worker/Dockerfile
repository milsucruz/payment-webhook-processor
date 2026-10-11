# Build context is the repository root, because Directory.Build.props and the
# project references live outside this folder:
#   docker build -f src/Payments.Worker/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Restore first, from the project files only, so this layer stays cached
# until a dependency changes.
COPY global.json Directory.Build.props ./
COPY src/Payments.Domain/Payments.Domain.csproj src/Payments.Domain/
COPY src/Payments.Application/Payments.Application.csproj src/Payments.Application/
COPY src/Payments.Infrastructure/Payments.Infrastructure.csproj src/Payments.Infrastructure/
COPY src/Payments.Worker/Payments.Worker.csproj src/Payments.Worker/
RUN dotnet restore src/Payments.Worker/Payments.Worker.csproj

COPY src/ src/
RUN dotnet publish src/Payments.Worker/Payments.Worker.csproj -c Release --no-restore -o /app

# The runtime image has no SDK and no source code.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app .
# Non-root user that ships with the .NET images.
USER $APP_UID
ENTRYPOINT ["dotnet", "Payments.Worker.dll"]
