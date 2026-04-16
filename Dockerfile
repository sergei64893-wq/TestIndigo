FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["Indigo/Indigo.csproj", "Indigo/"]
RUN dotnet restore "Indigo/Indigo.csproj"

COPY . .
RUN dotnet build "Indigo/Indigo.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Indigo/Indigo.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=publish /app/publish .

EXPOSE 5000 5001
ENTRYPOINT ["dotnet", "Indigo.dll"]

