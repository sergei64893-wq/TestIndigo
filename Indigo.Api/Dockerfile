FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Indigo.Api/Indigo.Api.csproj", "Indigo.Api/"]
COPY ["Indigo.Domain/Indigo.Domain.csproj", "Indigo.Domain/"]
COPY ["Indigo.Infrastructure/Indigo.Infrastructure.csproj", "Indigo.Infrastructure/"]
RUN dotnet restore "Indigo.Api/Indigo.Api.csproj"
COPY . .
WORKDIR "/src/Indigo.Api"
RUN dotnet build "Indigo.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Indigo.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Indigo.Api.dll"]
