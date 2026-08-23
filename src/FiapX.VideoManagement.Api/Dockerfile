FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY NuGet.Config ./
COPY FiapX.VideoManagementService.sln ./
COPY src/FiapX.VideoManagement.Domain/FiapX.VideoManagement.Domain.csproj src/FiapX.VideoManagement.Domain/
COPY src/FiapX.VideoManagement.Application/FiapX.VideoManagement.Application.csproj src/FiapX.VideoManagement.Application/
COPY src/FiapX.VideoManagement.Infrastructure/FiapX.VideoManagement.Infrastructure.csproj src/FiapX.VideoManagement.Infrastructure/
COPY src/FiapX.VideoManagement.Api/FiapX.VideoManagement.Api.csproj src/FiapX.VideoManagement.Api/

RUN dotnet restore src/FiapX.VideoManagement.Api/FiapX.VideoManagement.Api.csproj --configfile NuGet.Config

COPY . .
RUN dotnet publish src/FiapX.VideoManagement.Api/FiapX.VideoManagement.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "FiapX.VideoManagement.Api.dll"]
