# VideoManagementService

Microsservico .NET 10 para registrar videos, listar status e gerar URLs presigned para upload/download no MinIO.

## Execucao local

Na raiz do repositorio:

```powershell
docker compose up -d --build
```

Servicos:

- API: `http://localhost:8080`
- MinIO API: `http://localhost:9000`
- MinIO Console: `http://localhost:9001`
- PostgreSQL: `localhost:5432`
- Redis: `localhost:6379`

## Endpoints

```http
POST /videos
GET /videos
GET /videos/{videoId}
GET /videos/{videoId}/download
```

Exemplo:

```powershell
$body = @{ fileName = 'video.mp4'; contentType = 'video/mp4' } | ConvertTo-Json
$video = Invoke-RestMethod -Method Post -Uri 'http://localhost:8080/videos' -ContentType 'application/json' -Body $body
Invoke-WebRequest -Method Put -Uri $video.uploadUrl -ContentType 'video/mp4' -InFile '.\video.mp4'
```

## Validacao

```powershell
dotnet restore .\FiapX.VideoManagementService.sln --configfile .\NuGet.Config
dotnet build .\FiapX.VideoManagementService.sln --no-restore
dotnet test .\FiapX.VideoManagementService.sln --no-build
docker compose config
```
