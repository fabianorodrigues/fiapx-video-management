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
- Mailpit: `http://localhost:8025`
- RabbitMQ Management: `http://localhost:15672`

## RabbitMQ e status assincrono

A API HTTP nao depende do RabbitMQ para subir. O consumer de status roda em `BackgroundService`, faz retry inicial com backoff e reconecta quando o broker volta.

Topologia consumida pelo Management:

```text
video.events -- video.processing.started --> video.status-updates
video.events -- video.processing.completed --> video.status-updates
video.events -- video.processing.failed --> video.status-updates

video.status.retry.exchange -- started/completed/failed --> video.status-updates.retry
video.status-updates.retry -- DLX video.events, sem routing key fixa --> video.status-updates
video.status-updates -- DLX video.status.dlx/video.status.dlq --> video.status-updates.dlq
```

Os DTOs JSON continuam sem `eventType`; o dispatch usa a routing key. Redis e SMTP sao best-effort e nao provocam retry do evento.

As filas de negocio sao quorum queues. No Docker Compose local ha somente um broker RabbitMQ; quorum e usado aqui para durabilidade/dead-lettering mais seguro, nao para alta disponibilidade real.

## Mailpit e notificacoes

O envio de notificacao de falha usa SMTP best-effort. O PostgreSQL continua sendo a fonte de verdade:

```text
persistir ERRO -> invalidar cache -> tentar SMTP
```

Falha no SMTP nao desfaz o status `ERRO` e nao deve provocar retry do evento. Sem Outbox existe uma pequena janela em que o processo pode parar depois de persistir `ERRO` e antes do e-mail; essa perda de notificacao e aceita nesta etapa porque a notificacao e secundaria.

Dentro do Docker Compose, a API deve usar o nome do servico:

```text
SMTP_HOST=mailpit
SMTP_PORT=1025
SMTP_FROM=no-reply@fiapx.local
```

Para testes executados diretamente no host Windows, sobrescreva para:

```text
SMTP_HOST=localhost
SMTP_PORT=1025
```

Nao use `localhost` dentro do container da API para acessar o Mailpit, pois ele apontaria para o proprio container.

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

E2E RabbitMQ completo, a partir da raiz do repositorio:

```powershell
$env:KEYCLOAK_ADMIN_USERNAME = 'admin'
$env:KEYCLOAK_ADMIN_PASSWORD = '<senha-local-admin-keycloak>'
$env:FIAPX_E2E_ALICE_PASSWORD = '<senha-local-da-alice>'
.\scripts\e2e-rabbitmq.ps1
```

Testes reais opt-in:

```powershell
$env:FIAPX_RUN_POSTGRES_INTEGRATION = 'true'
dotnet test .\FiapX.VideoManagementService.sln --no-build

docker compose up -d mailpit
$env:FIAPX_RUN_MAILPIT_INTEGRATION = 'true'
$env:SMTP_HOST = 'localhost'
$env:SMTP_PORT = '1025'
dotnet test .\FiapX.VideoManagementService.sln --no-build --filter FullyQualifiedName~ProcessingEventPostgresIntegrationTests
```

Divida antes de CI/CD: a infraestrutura compartilhada precisa possuir uma estrategia de versionamento no GitHub, pois docker-compose, configuracao Keycloak, RabbitMQ/MinIO bootstrap e demais artefatos nao podem permanecer fora de controle de versao na entrega final.
