# fiapx-video-management

API HTTP da solução FiapX para autenticar requisições, registrar vídeos, gerar URLs pré-assinadas de upload/download, consultar status e refletir eventos de processamento.

[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)]()
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-EF%20Core-4169E1?logo=postgresql&logoColor=white)]()
[![Postman](https://img.shields.io/badge/Postman-collection-FF6C37?logo=postman&logoColor=white)](postman/fiapx-video-management.postman_collection.json)
[![CI](https://github.com/fabianorodrigues/fiapx-video-management/actions/workflows/ci.yml/badge.svg)](https://github.com/fabianorodrigues/fiapx-video-management/actions/workflows/ci.yml)
[![CD](https://github.com/fabianorodrigues/fiapx-video-management/actions/workflows/cd.yml/badge.svg)](https://github.com/fabianorodrigues/fiapx-video-management/actions/workflows/cd.yml)
[![Coverage](.github/badges/coverage.svg)](https://github.com/fabianorodrigues/fiapx-video-management/actions/workflows/ci.yml)

## Sumário

- [Visão geral](#visão-geral)
- [Arquitetura](#arquitetura)
- [Configuração](#configuração)
- [Execução local isolada](#execução-local-isolada)
- [Autenticação](#autenticação)
- [Endpoints](#endpoints)
- [Swagger e Postman](#swagger-e-postman)
- [CI/CD](#cicd)
- [Validação](#validação)
- [Próxima etapa](#próxima-etapa)

---

## Visão geral

Este repositório implementa a API da solução FiapX. Ela é a entrada HTTP para o usuário final e mantém a fonte de verdade dos metadados do vídeo.

Para configuração e provisionamento do ambiente integrado, consulte o [fiapx-infra](https://github.com/fabianorodrigues/fiapx-infra).

| Responsabilidade | Implementação |
| --- | --- |
| Autenticação | JWT Bearer validado contra o realm `fiapx` do Keycloak |
| Upload | `POST /videos` registra o vídeo e devolve presigned URL `PUT` para o MinIO |
| Status | `GET /videos` e `GET /videos/{videoId}` consultam o PostgreSQL com cache Redis best-effort |
| Download | `GET /videos/{videoId}/download` devolve presigned URL quando o status é `CONCLUIDO` |
| Eventos | Consumer RabbitMQ atualiza status a partir de `video.processing.started`, `completed` e `failed` |
| Notificação | Falha de processamento dispara e-mail best-effort via SMTP/Mailpit |

**Tecnologias:** .NET 10, ASP.NET Core Minimal APIs, Entity Framework Core, Npgsql, PostgreSQL, Redis, RabbitMQ.Client, AWSSDK.S3, MinIO, Keycloak, Swagger/OpenAPI, Postman, Mailpit e Docker.

---

## Arquitetura

```mermaid
%%{init: {"flowchart": {"nodeSpacing": 34, "rankSpacing": 46}} }%%
flowchart LR
    classDef client fill:#EFEFEF,color:#222,stroke:#999
    classDef api fill:#512BD4,color:#fff,stroke:#39208A
    classDef auth fill:#6D28D9,color:#fff,stroke:#4C1D95
    classDef store fill:#2563EB,color:#fff,stroke:#1E3A8A
    classDef broker fill:#FF6600,color:#fff,stroke:#B34700
    classDef mail fill:#3F3F46,color:#fff,stroke:#18181B

    USER([Cliente ou Postman]):::client
    KC["Keycloak<br/>token OIDC"]:::auth
    API["fiapx-video-management<br/>API .NET 10"]:::api
    PG[("PostgreSQL<br/>vídeos")]:::store
    REDIS[("Redis<br/>cache")]:::store
    MINIO[("MinIO<br/>upload/download")]:::store
    RABBIT["RabbitMQ<br/>status updates"]:::broker
    MAIL["Mailpit<br/>SMTP local"]:::mail

    USER -- "login" --> KC
    USER -- "Authorization: Bearer" --> API
    API --> PG
    API -. best-effort .-> REDIS
    API -- "presigned URLs" --> MINIO
    RABBIT -- "eventos de processamento" --> API
    API -. "erro de processamento" .-> MAIL
```

A API não processa vídeo. Ela registra o upload, controla autorização por usuário, atualiza status a partir de eventos e entrega a URL de download do ZIP final.

---

## Configuração

Para configuração e provisionamento do ambiente integrado, consulte o [fiapx-infra](https://github.com/fabianorodrigues/fiapx-infra).

Variáveis consumidas pela API nos Compose atuais:

| Variável | Classificação | Uso |
| --- | --- | --- |
| `POSTGRES_CONNECTION_STRING` | MANUAL OBRIGATÓRIO fora do Compose | Persistência EF Core |
| `REDIS_CONNECTION_STRING` | OPCIONAL | Cache de listagem/detalhe; falha não derruba a requisição |
| `JWT_METADATA_ADDRESS`, `JWT_ISSUER`, `JWT_AUDIENCE` | MANUAL OBRIGATÓRIO fora do Compose | Validação JWT |
| `MINIO_INTERNAL_ENDPOINT`, `MINIO_PUBLIC_ENDPOINT` | MANUAL OBRIGATÓRIO fora do Compose | Cliente interno e URL pública das presigned URLs |
| `MINIO_ACCESS_KEY`, `MINIO_SECRET_KEY`, `MINIO_BUCKET`, `MINIO_REGION` | MANUAL OBRIGATÓRIO fora do Compose | Acesso ao bucket |
| `PRESIGNED_URL_EXPIRES_SECONDS` | OPCIONAL | Expiração das URLs pré-assinadas |
| `SMTP_HOST`, `SMTP_PORT`, `SMTP_FROM` | OPCIONAL | Notificação de falha |
| `RABBITMQ_HOST`, `RABBITMQ_PORT`, `RABBITMQ_USERNAME`, `RABBITMQ_PASSWORD`, `RABBITMQ_VHOST` | MANUAL OBRIGATÓRIO fora do Compose | Consumer de status |
| `STATUS_MAX_ATTEMPTS`, `STATUS_RETRY_DELAY_MS` | OPCIONAL | Retry de eventos de status |

Credenciais DEMO do ambiente integrado ficam em `fiapx-infra/.env.example`. Elas são exclusivamente DEMO/local.

Para CD deste repositório, configure:

```text
Settings > Secrets and variables > Actions > Variables
DEPLOY_INFRA_PATH=C:\Projetos\fiap-fase5\fiapx-infra
```

`DEPLOY_INFRA_PATH` aponta para a working copy operacional da Infra usada pelo script de deploy.

---

## Execução local isolada

Este repositório possui um Compose local para desenvolvimento da API sem depender do Worker nem do ambiente integrado.

```powershell
docker compose -f docker-compose.local.yml up -d --build
```

Para parar preservando volumes:

```powershell
docker compose -f docker-compose.local.yml down
```

Serviços locais:

| Serviço | URL |
| --- | --- |
| API | `http://localhost:8080` |
| Swagger UI | `http://localhost:8080/swagger` |
| Keycloak | `http://localhost:8081` |
| MinIO API | `http://localhost:9000` |
| MinIO Console | `http://localhost:9001` |
| RabbitMQ Management | `http://localhost:15672` |
| Mailpit | `http://localhost:8025` |
| PostgreSQL | `localhost:5432` |
| Redis | `localhost:6379` |

As portas são as mesmas do ambiente integrado. Execute apenas um dos ambientes por vez quando usar os defaults.

---

## Autenticação

Todos os endpoints `/videos` exigem:

```http
Authorization: Bearer <access_token>
```

Condição validada no código:

- `Program.cs` usa JWT Bearer.
- `MetadataAddress`, `Issuer` e `Audience` vêm de configuração.
- A API exige as claims `sub` e `email`.
- O `UserId` da aplicação é o claim `sub`, não o nome de usuário.

Keycloak local:

| Item | Valor |
| --- | --- |
| URL | `http://localhost:8081` |
| Realm | `fiapx` |
| Client Postman | `fiapx-postman` |
| Caminho de usuários | `Manage realms > fiapx > Users` |

Usuários DEMO oficiais:

| Usuário | E-mail |
| --- | --- |
| `usertest1` | `usertest1@fiapx.local` |
| `usertest2` | `usertest2@fiapx.local` |

As senhas DEMO desses usuários ficam nas fixtures locais e no `fiapx-infra/.env.example`.

Exemplo de token no ambiente local:

```powershell
$token = (Invoke-RestMethod `
  -Method Post `
  -Uri 'http://localhost:8081/realms/fiapx/protocol/openid-connect/token' `
  -ContentType 'application/x-www-form-urlencoded' `
  -Body @{
    grant_type = 'password'
    client_id = 'fiapx-postman'
    username = 'usertest1'
    password = 'fiapx_usertest1_demo_password'
  }).access_token
```

---

## Endpoints

| Verbo | Rota | Autenticação | Resultado |
| --- | --- | --- | --- |
| `GET` | `/health` | Não | Health check da API |
| `POST` | `/videos` | Sim | Cria registro `RECEBIDO` e devolve `uploadUrl` |
| `GET` | `/videos` | Sim | Lista vídeos do usuário autenticado |
| `GET` | `/videos/{videoId}` | Sim | Consulta detalhe/status do vídeo |
| `GET` | `/videos/{videoId}/download` | Sim | Devolve `downloadUrl` se o vídeo estiver `CONCLUIDO` |

Criar vídeo:

```powershell
$body = @{
  fileName = 'video.mp4'
  contentType = 'video/mp4'
} | ConvertTo-Json

$video = Invoke-RestMethod `
  -Method Post `
  -Uri 'http://localhost:8080/videos' `
  -Headers @{ Authorization = "Bearer $token" } `
  -ContentType 'application/json' `
  -Body $body
```

Enviar o arquivo para a URL retornada:

```powershell
Invoke-WebRequest `
  -Method Put `
  -Uri $video.uploadUrl `
  -ContentType 'video/mp4' `
  -InFile '.\video.mp4'
```

Consultar status:

```powershell
Invoke-RestMethod `
  -Method Get `
  -Uri "http://localhost:8080/videos/$($video.videoId)" `
  -Headers @{ Authorization = "Bearer $token" }
```

Baixar o resultado quando o status estiver `CONCLUIDO`:

```powershell
$download = Invoke-RestMethod `
  -Method Get `
  -Uri "http://localhost:8080/videos/$($video.videoId)/download" `
  -Headers @{ Authorization = "Bearer $token" }

Invoke-WebRequest -Method Get -Uri $download.downloadUrl -OutFile .\resultado.zip
```

Status retornados pela API:

| Status | Significado |
| --- | --- |
| `RECEBIDO` | Registro criado e aguardando upload/evento |
| `PROCESSANDO` | Worker iniciou processamento |
| `CONCLUIDO` | ZIP final disponível |
| `ERRO` | Processamento falhou; notificação é tentada via SMTP |

---

## Swagger e Postman

### Swagger

Condição real de disponibilidade:

- `Program.cs` registra Swagger/OpenAPI.
- `UseSwagger()` e `UseSwaggerUI()` são habilitados somente quando `app.Environment.IsDevelopment()`.
- Os Compose atuais deste repositório e da infra definem `ASPNETCORE_ENVIRONMENT=Development` para a API.

Nesses modos locais por Compose:

```text
http://localhost:8080/swagger
```

Em ambiente que não esteja com `ASPNETCORE_ENVIRONMENT=Development`, o Swagger UI não é habilitado por esta aplicação.

### Postman

Arquivos versionados:

- [Collection Postman](postman/fiapx-video-management.postman_collection.json)
- [Environment local](postman/fiapx-video-management.local.postman_environment.json)

Como usar:

1. Suba o ambiente integrado pelo [fiapx-infra](https://github.com/fabianorodrigues/fiapx-infra) ou o Compose local deste repositório.
2. Importe a Collection e o Environment no Postman.
3. Selecione o Environment `fiapx-video-management Local`.
4. Configure `videoFilePath` com o caminho absoluto de um arquivo `.mp4` local.
5. Habilite leitura do arquivo local no Postman se necessário em `Settings > Working Directory`.
6. Rode a Collection `FIAP X - fiapx-video-management` no Runner, em ordem.

O Runner valida:

- `/health`;
- login real no Keycloak;
- token com `sub`, `email`, `iss`, `aud` e `exp`;
- criação de vídeo `RECEBIDO`;
- upload real no MinIO usando a URL retornada pela API;
- listagem e detalhe por usuário autenticado;
- caminho de cache Redis na segunda listagem;
- bloqueio de download antes de `CONCLUIDO`;
- respostas `400`, `401`, `404` e isolamento entre usuários.

Para o fluxo ponta a ponta com Worker, use o ambiente integrado da infra, envie o vídeo, aguarde `CONCLUIDO`, baixe `resultado.zip` e valide os frames no arquivo.

Fluxo de erro simples:

1. Aponte `videoFilePath` para um arquivo com extensão `.mp4`, mas conteúdo inválido.
2. Registre e envie o upload.
3. Consulte `GET /videos/{videoId}` até status `ERRO`.
4. Acesse o Mailpit em `http://localhost:8025` e valide a notificação.

---

## CI/CD

### CI

O workflow `.github/workflows/ci.yml` roda em `ubuntu-24.04` para:

- `pull_request`;
- `push` na branch `main`;
- `workflow_dispatch`.

Pipeline:

1. Configura .NET 10.
2. Executa restore com `NuGet.Config`.
3. Compila a solution `FiapX.VideoManagementService.sln`.
4. Executa testes xUnit com coverage Cobertura.
5. Gera summary e badge SVG de coverage.
6. Publica `.trx` e coverage como artifacts.
7. Faz build Docker da API.
8. Em `push main` com CI verde, publica imagem no GHCR.

A publicação gera tag com o SHA do commit e também `latest`. O deploy usa a imagem por SHA imutável; `latest` não é a referência operacional do CD.

O badge de coverage é gerado por `scripts/coverage-summary.ps1` a partir do `coverage.cobertura.xml`. O mesmo script falha o CI quando a cobertura de linhas fica abaixo de 85%. Em `push` na `main`, o workflow tenta atualizar `.github/badges/coverage.svg`; se essa etapa falhar, ela não bloqueia o fluxo de publicação.

O escopo de coverage fica em `coverlet.runsettings`, excluindo código gerado, bootstrap e adapters externos que dependem de serviços em execução, como migrations, host, persistência, cache, S3 e conexões RabbitMQ. Componentes críticos com teste local determinístico, como o envio SMTP de falha, permanecem dentro do coverage.

### CD

O workflow `.github/workflows/cd.yml` roda por `workflow_run` depois do CI aprovado em `push` na `main`. Ele usa `head_sha`, valida se o commit ainda é o HEAD atual da `main` e só então executa no runner self-hosted Windows.

`workflow_dispatch` é útil para CI, mas o caminho normal de CI + CD completo é `push` ou merge na `main`.

Configuração necessária:

| Item | Valor |
| --- | --- |
| Runner directory | `C:\actions-runner-management` |
| Runner name | `fiapx-management-deploy` |
| Runner group | `Default` |
| Additional labels | `fiap-fase5` |
| Work folder | `_work` |
| Run as service | `N` |
| Labels esperadas | `self-hosted`, `Windows`, `X64`, `fiap-fase5` |
| GitHub Variable | `DEPLOY_INFRA_PATH=C:\Projetos\fiap-fase5\fiapx-infra` |

Crie a variável em `Settings > Secrets and variables > Actions > Variables`.

O script `.github/scripts/deploy-management.ps1`:

- usa `Global\FiapXDeployLock`;
- exige que o ambiente integrado já exista e esteja com serviços base em execução;
- calcula `DEPLOY_IMAGE=ghcr.io/<owner>/<repo>:<head_sha>`;
- executa migrations antes de recriar a API;
- valida Docker health e `http://localhost:8080/health`;
- preserva containers não alvo;
- só persiste `VIDEO_MANAGEMENT_IMAGE` no `.env` da infra após validação.

---

## Validação

Validação local de build/teste:

```powershell
dotnet restore .\FiapX.VideoManagementService.sln --configfile .\NuGet.Config
dotnet build .\FiapX.VideoManagementService.sln --configuration Release --no-restore
dotnet test .\FiapX.VideoManagementService.sln --configuration Release --no-build
docker compose -f docker-compose.local.yml config --quiet
```

Validação local com coverage:

```powershell
dotnet test .\FiapX.VideoManagementService.sln `
  --configuration Release `
  --no-build `
  --logger "trx;LogFilePrefix=test-results" `
  --results-directory TestResults `
  --collect:"XPlat Code Coverage" `
  --settings coverlet.runsettings

powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\coverage-summary.ps1
```

Validação funcional mínima:

1. Suba o ambiente.
2. Valide `GET /health`.
3. Abra o Swagger em `http://localhost:8080/swagger`.
4. Autentique no Keycloak.
5. Chame `POST /videos`.
6. Faça upload do `.mp4` com a `uploadUrl`.
7. Consulte `GET /videos/{videoId}` até `CONCLUIDO`.
8. Chame `GET /videos/{videoId}/download`.
9. Baixe `resultado.zip`.
10. Abra o ZIP e confirme os frames PNG.

Validação de erro:

1. Envie arquivo com extensão `.mp4`, mas conteúdo inválido.
2. Aguarde status `ERRO`.
3. Valide a notificação no Mailpit.

---

## Próxima etapa

Após enviar o vídeo, acompanhe o status até `CONCLUIDO` e faça o download do resultado.
