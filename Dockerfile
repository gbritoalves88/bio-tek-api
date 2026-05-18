# ===== ESTÁGIO 1: BUILD =====
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copia os arquivos .csproj e restaura pacotes
COPY src/BioTek.Domain/BioTek.Domain.csproj src/BioTek.Domain/
COPY src/BioTek.Application/BioTek.Application.csproj src/BioTek.Application/
COPY src/BioTek.Infrastructure/BioTek.Infrastructure.csproj src/BioTek.Infrastructure/
COPY src/BioTek.Api/BioTek.Api.csproj src/BioTek.Api/
RUN dotnet restore src/BioTek.Api/BioTek.Api.csproj

# Copia todo o código e faz o build
COPY . .
RUN dotnet publish src/BioTek.Api/BioTek.Api.csproj -c Release -o /app/out

# ===== ESTÁGIO 2: RUNTIME =====
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/out .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "BioTek.Api.dll"]