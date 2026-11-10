# Comprobantes AWS (.NET 8 + S3 + DynamoDB)

API REST en .NET 8 que gestiona comprobantes: guarda el PDF en **Amazon S3** y sus datos en **Amazon DynamoDB**.
Se desarrolla y prueba en local con [Floci](https://github.com/floci-io/floci), un emulador de AWS, sin cuenta ni costos.

## Tecnologías

- .NET 8 · ASP.NET Core Minimal APIs
- AWS SDK para .NET (S3, DynamoDB)
- Floci (emulador local de AWS) · Docker
- AWS CLI

## Endpoints

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/salud` | Verifica la conexión con S3 y DynamoDB |
| POST | `/comprobantes` | Sube un PDF a S3 y registra sus datos en DynamoDB |
| GET | `/comprobantes` | Lista los comprobantes |
| GET | `/comprobantes/{id}/pdf` | Descarga el PDF desde S3 |

## Cómo ejecutarlo

```powershell
# 1. Levantar AWS local
docker run -d --name floci -p 4566:4566 floci/floci:latest

# 2. Configurar un perfil que apunte a Floci
aws configure set aws_access_key_id test --profile floci
aws configure set aws_secret_access_key test --profile floci
aws configure set region us-east-1 --profile floci
aws configure set endpoint_url http://localhost:4566 --profile floci

# 3. Crear bucket y tabla
aws s3 mb s3://tito-lab --profile floci
aws dynamodb create-table --table-name Comprobantes --attribute-definitions AttributeName=Id,AttributeType=S --key-schema AttributeName=Id,KeyType=HASH --billing-mode PAY_PER_REQUEST --profile floci

# 4. Ejecutar la API
cd ComprobantesApi
dotnet run
```

Subir un comprobante:

```powershell
curl.exe -F "archivo=@factura.pdf" -F "cliente=Almacen Lopez" -F "total=15400" http://localhost:5235/comprobantes
```

## Próximos pasos

- [ ] Procesamiento asíncrono con SQS + Lambda
- [ ] Tests de integración contra Floci
- [ ] CI con GitHub Actions
