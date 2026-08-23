# Postman Runner

Arquivos:

- `VideoManagementService.postman_collection.json`
- `VideoManagementService.local.postman_environment.json`

## Como rodar

1. Suba a aplicacao na raiz do projeto:

   ```powershell
   $env:KEYCLOAK_ADMIN_USERNAME = 'admin'
   $env:KEYCLOAK_ADMIN_PASSWORD = '<senha-local-admin-keycloak>'
   ```

   ```powershell
   docker compose up -d --build
   ```

2. Importe no Postman:

   - Collection: `VideoManagementService.postman_collection.json`
   - Environment: `VideoManagementService.local.postman_environment.json`

3. Selecione o environment `VideoManagementService Local`.

4. Ajuste somente se voce mudou os defaults do `docker-compose.yml`:

   - `apiBaseUrl`
   - `keycloakBaseUrl`
   - `keycloakClientId`
   - `jwtAudience`
   - `aliceUsername`
   - `aliceEmail`
   - `alicePassword`
   - `bobUsername`
   - `bobEmail`
   - `bobPassword`
   - `minioPublicEndpoint`
   - `minioBucket`
   - `minioAccessKey`
   - `minioSecretKey`
   - `minioRegion`

5. Crie manualmente no Keycloak, realm `fiapx`, os usuarios abaixo, defina senhas locais e coloque essas senhas no environment do Postman:

   - `alice`, e-mail `alice@fiapx.local`, senha em `alicePassword`
   - `bob`, e-mail `bob@fiapx.local`, senha em `bobPassword`

6. Configure obrigatoriamente o arquivo local:

   - `videoFilePath`: caminho completo do `.mp4` no seu computador.

   Exemplo:

   ```text
   C:/Users/Fabiano/Videos/VID-20260324-WA0022.mp4
   ```

   A collection deriva `videoFileName` automaticamente a partir desse caminho.

   Se o Postman avisar que nao consegue ler o arquivo no Runner, abra `Settings > Working Directory` e permita leitura do diretorio onde o video esta salvo, ou selecione o mesmo arquivo manualmente no body do request `03 - Upload Local Video File To MinIO With Returned URL`.

7. Abra o Runner do Postman, selecione a collection `FIAP X - VideoManagementService` e rode todos os requests em ordem.

## O que a collection valida

- API health.
- Login real no Keycloak usando o client publico `fiapx-postman`.
- Access token contendo `sub`, `email`, `iss`, `aud` e `exp`.
- `POST /videos` cria registro `RECEBIDO`.
- A URL presigned retornada usa `minioPublicEndpoint`.
- Upload real do arquivo configurado em `videoFilePath` para MinIO usando exatamente a URL retornada.
- Objeto existe no bucket privado do MinIO via AWS Signature.
- `GET /videos` lista videos do usuario autenticado pelo claim `sub`.
- Segunda listagem exercita caminho de cache.
- `GET /videos/{videoId}` retorna detalhe correto.
- `GET /videos/{videoId}/download` retorna `409` enquanto status ainda e `RECEBIDO`.
- Requests invalidos retornam `400`.
- Video inexistente retorna `404`.
- `GET /videos` sem JWT retorna `401`.
- Bob nao lista video criado pela Alice.
- Bob consultando video da Alice recebe `404`.
