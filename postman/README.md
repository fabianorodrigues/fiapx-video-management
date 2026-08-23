# Postman Runner

Arquivos:

- `VideoManagementService.postman_collection.json`
- `VideoManagementService.local.postman_environment.json`

## Como rodar

1. Suba a aplicacao na raiz do projeto:

   ```powershell
   docker compose up -d --build
   ```

2. Importe no Postman:

   - Collection: `VideoManagementService.postman_collection.json`
   - Environment: `VideoManagementService.local.postman_environment.json`

3. Selecione o environment `VideoManagementService Local`.

4. Ajuste somente se voce mudou os defaults do `docker-compose.yml`:

   - `apiBaseUrl`
   - `minioPublicEndpoint`
   - `minioBucket`
   - `minioAccessKey`
   - `minioSecretKey`
   - `minioRegion`
   - `devUserId`

5. Configure obrigatoriamente o arquivo local:

   - `videoFilePath`: caminho completo do `.mp4` no seu computador.

   Exemplo:

   ```text
   C:/Users/Fabiano/Videos/VID-20260324-WA0022.mp4
   ```

   A collection deriva `videoFileName` automaticamente a partir desse caminho.

   Se o Postman avisar que nao consegue ler o arquivo no Runner, abra `Settings > Working Directory` e permita leitura do diretorio onde o video esta salvo, ou selecione o mesmo arquivo manualmente no body do request `03 - Upload Local Video File To MinIO With Returned URL`.

6. Abra o Runner do Postman, selecione a collection `FIAP X - VideoManagementService` e rode todos os requests em ordem.

## O que a collection valida

- API health.
- `POST /videos` cria registro `RECEBIDO`.
- A URL presigned retornada usa `minioPublicEndpoint`.
- Upload real do arquivo configurado em `videoFilePath` para MinIO usando exatamente a URL retornada.
- Objeto existe no bucket privado do MinIO via AWS Signature.
- `GET /videos` lista videos do usuario dev.
- Segunda listagem exercita caminho de cache.
- `GET /videos/{videoId}` retorna detalhe correto.
- `GET /videos/{videoId}/download` retorna `409` enquanto status ainda e `RECEBIDO`.
- Requests invalidos retornam `400`.
- Video inexistente retorna `404`.
