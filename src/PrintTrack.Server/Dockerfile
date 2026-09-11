# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY PrintTrack.slnx ./
COPY src/PrintTrack.Shared/PrintTrack.Shared.csproj src/PrintTrack.Shared/
COPY src/PrintTrack.Server/PrintTrack.Server.csproj src/PrintTrack.Server/
RUN dotnet restore src/PrintTrack.Server/PrintTrack.Server.csproj
COPY src/PrintTrack.Shared/ src/PrintTrack.Shared/
COPY src/PrintTrack.Server/ src/PrintTrack.Server/
RUN dotnet publish src/PrintTrack.Server/PrintTrack.Server.csproj -c Release -o /app

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
# Allow the outbound HTTPS client to negotiate legacy TLS with old printer web servers.
COPY src/PrintTrack.Server/openssl-legacy.cnf /etc/ssl/openssl-legacy.cnf
ENV OPENSSL_CONF=/etc/ssl/openssl-legacy.cnf
EXPOSE 8095
ENTRYPOINT ["dotnet", "PrintTrack.Server.dll"]
