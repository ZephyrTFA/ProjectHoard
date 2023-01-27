FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["Hoard2/Hoard2.csproj", "Hoard2/"]
RUN dotnet restore "Hoard2/Hoard2.csproj"
COPY . .
WORKDIR "/src/Hoard2"
RUN dotnet build "Hoard2.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Hoard2.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Hoard2.dll"]
