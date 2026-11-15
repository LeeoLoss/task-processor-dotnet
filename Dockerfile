# ---------- Etapa 1: compilação ----------
# Imagem com o SDK do .NET, que tem tudo para compilar. Ela não vai para a imagem final
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copia primeiro só os arquivos .csproj e restaura os pacotes.
# Assim o Docker reaproveita essa etapa (cache) enquanto os pacotes não mudarem
COPY src/TK.Core/TK.Core.csproj src/TK.Core/
COPY src/TK.Api/TK.Api.csproj src/TK.Api/
COPY src/TK.Worker/TK.Worker.csproj src/TK.Worker/
RUN dotnet restore src/TK.Api/TK.Api.csproj
RUN dotnet restore src/TK.Worker/TK.Worker.csproj

# Agora copia o código e gera os arquivos prontos para execução
COPY src/ src/
RUN dotnet publish src/TK.Api/TK.Api.csproj -c Release -o /app/api --no-restore
RUN dotnet publish src/TK.Worker/TK.Worker.csproj -c Release -o /app/worker --no-restore

# ---------- Imagem final da API ----------
# Imagem enxuta, só com o necessário para executar uma aplicação web
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS api
WORKDIR /app
COPY --from=build /app/api .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "TK.Api.dll"]

# ---------- Imagem final do Worker ----------
# O Worker não é uma aplicação web, então basta a imagem de execução do .NET
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS worker
WORKDIR /app
COPY --from=build /app/worker .
ENTRYPOINT ["dotnet", "TK.Worker.dll"]