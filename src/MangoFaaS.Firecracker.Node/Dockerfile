FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["MangoFaaS.Firecracker.Node/MangoFaaS.Firecracker.Node.csproj", "MangoFaaS.Firecracker.Node/"]
RUN dotnet restore "MangoFaaS.Firecracker.Node/MangoFaaS.Firecracker.Node.csproj"
COPY . .
WORKDIR "/src/MangoFaaS.Firecracker.Node"
RUN dotnet build "./MangoFaaS.Firecracker.Node.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./MangoFaaS.Firecracker.Node.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MangoFaaS.Firecracker.Node.dll"]
