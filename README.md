# VideoManagementService

Microsservico .NET 10 para registrar videos, listar status e gerar URLs presigned para upload/download no MinIO.

## Execucao local

Na raiz do repositorio:

```powershell
$env:KEYCLOAK_ADMIN_USERNAME = 'admin'
$env:KEYCLOAK_ADMIN_PASSWORD = '<senha-local-admin-keycloak>'
```

```powershell
docker compose up -d --build
```

Servicos:

- API: `http://localhost:8080`
- Keycloak: `http://localhost:8081`
- MinIO API: `http://localhost:9000`
- MinIO Console: `http://localhost:9001`
- PostgreSQL: `localhost:5432`
- Redis: `localhost:6379`

## Keycloak local

O realm `fiapx` e os clients sao importados por `../keycloak/fiapx-realm.json`.

Topologia local:

```text
Postman/cURL -> http://localhost:8081
VideoManagementService -> http://keycloak:8080
```

Clients:

- `fiapx-postman`: client publico para Postman/cURL, com Direct Access Grants habilitado somente para demonstracao local.
- `video-management-service`: audience/resource server da API, sem Direct Access Grants.

Crie manualmente dois usuarios no realm `fiapx`, ambos com e-mail verificado ou preenchido:

- `alice`, e-mail `alice@fiapx.local`
- `bob`, e-mail `bob@fiapx.local`

Nao versione senhas. Defina senhas locais descartaveis pela UI do Keycloak.

Exemplo para obter access token:

```powershell
$aliceToken = (Invoke-RestMethod `
  -Method Post `
  -Uri 'http://localhost:8081/realms/fiapx/protocol/openid-connect/token' `
  -ContentType 'application/x-www-form-urlencoded' `
  -Body @{
    grant_type = 'password'
    client_id = 'fiapx-postman'
    username = 'alice'
    password = '<senha-local-da-alice>'
  }).access_token
```

O access token deve conter `sub`, `email`, `iss`, `aud` e `exp`. A API rejeita tokens sem `sub` ou sem `email`.

## Endpoints

Todos os endpoints `/videos` exigem:

```http
Authorization: Bearer {access_token}
```

```http
POST /videos
GET /videos
GET /videos/{videoId}
GET /videos/{videoId}/download
```

Exemplo:

```powershell
$body = @{ fileName = 'video.mp4'; contentType = 'video/mp4' } | ConvertTo-Json
$video = Invoke-RestMethod -Method Post -Uri 'http://localhost:8080/videos' -Headers @{ Authorization = "Bearer $aliceToken" } -ContentType 'application/json' -Body $body
Invoke-WebRequest -Method Put -Uri $video.uploadUrl -ContentType 'video/mp4' -InFile '.\video.mp4'
```

## Validacao

```powershell
dotnet restore .\FiapX.VideoManagementService.sln --configfile .\NuGet.Config
dotnet build .\FiapX.VideoManagementService.sln --no-restore
dotnet test .\FiapX.VideoManagementService.sln --no-build
docker compose config
```

Divida antes de CI/CD: a infraestrutura compartilhada precisa possuir uma estrategia de versionamento no GitHub, pois docker-compose, configuracao Keycloak, RabbitMQ/MinIO bootstrap e demais artefatos nao podem permanecer fora de controle de versao na entrega final.
