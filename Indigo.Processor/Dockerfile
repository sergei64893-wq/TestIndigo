FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Indigo.Processor/Indigo.Processor.csproj", "Indigo.Processor/"]
COPY ["Indigo.Infrastructure/Indigo.Infrastructure.csproj", "Indigo.Infrastructure/"]
COPY ["Indigo.Domain/Indigo.Domain.csproj", "Indigo.Domain/"]
RUN dotnet restore "Indigo.Processor/Indigo.Processor.csproj"
COPY . .
WORKDIR "/src/Indigo.Processor"
RUN dotnet build "Indigo.Processor.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Indigo.Processor.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Indigo.Processor.dll"]
