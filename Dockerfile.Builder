FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first for restore layer caching
COPY src/Directory.Build.props .
COPY src/TextServices.Core/TextServices.Core.csproj TextServices.Core/
COPY src/TextServices.Storage/TextServices.Storage.csproj TextServices.Storage/
COPY src/TextServices.Infrastructure/TextServices.Infrastructure.csproj TextServices.Infrastructure/
COPY src/TextServices.Builder.Api/TextServices.Builder.Api.csproj TextServices.Builder.Api/
RUN dotnet restore TextServices.Builder.Api/TextServices.Builder.Api.csproj

COPY src/ .
RUN dotnet publish TextServices.Builder.Api/TextServices.Builder.Api.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

RUN apt-get update && apt-get install -y libgssapi-krb5-2 libkrb5-3 krb5-user
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "TextServices.Builder.Api.dll"]
