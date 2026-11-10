using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Configuración de AWS (en desarrollo apunta a Floci)
var aws = builder.Configuration.GetSection("Aws");
var credenciales = new BasicAWSCredentials(aws["AccessKey"], aws["SecretKey"]);
var bucket = aws["Bucket"]!;
var tabla = aws["Table"]!;

builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(credenciales, new AmazonS3Config
{
    ServiceURL = aws["ServiceUrl"],
    ForcePathStyle = true, // necesario para emuladores locales
    AuthenticationRegion = aws["Region"]
}));

builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient(credenciales, new AmazonDynamoDBConfig
{
    ServiceURL = aws["ServiceUrl"],
    AuthenticationRegion = aws["Region"]
}));

var app = builder.Build();

// Endpoint de prueba: lista buckets y tablas
app.MapGet("/salud", async (IAmazonS3 s3, IAmazonDynamoDB dynamo) =>
{
    var buckets = await s3.ListBucketsAsync();
    var tablas = await dynamo.ListTablesAsync();

    return Results.Ok(new
    {
        buckets = buckets.Buckets?.Select(b => b.BucketName) ?? [],
        tablas = tablas.TableNames ?? []
    });
});

// 1) Subir un comprobante: el PDF va a S3 y sus datos a DynamoDB
app.MapPost("/comprobantes", async (
    IFormFile archivo,
    [FromForm] string cliente,
    [FromForm] decimal total,
    IAmazonS3 s3,
    IAmazonDynamoDB dynamo) =>
{
    if (archivo is null || archivo.Length == 0)
        return Results.BadRequest("Falta el archivo.");
    if (!archivo.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest("Solo se aceptan archivos PDF.");

    var id = $"F-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
    var clave = $"{id}.pdf";

    // Guardar el archivo en S3
    await using var contenido = archivo.OpenReadStream();
    await s3.PutObjectAsync(new PutObjectRequest
    {
        BucketName = bucket,
        Key = clave,
        InputStream = contenido,
        ContentType = "application/pdf"
    });

    // Guardar los datos en DynamoDB
    await dynamo.PutItemAsync(new PutItemRequest
    {
        TableName = tabla,
        Item = new Dictionary<string, AttributeValue>
        {
            ["Id"] = new AttributeValue { S = id },
            ["Cliente"] = new AttributeValue { S = cliente },
            ["Total"] = new AttributeValue { N = total.ToString(CultureInfo.InvariantCulture) },
            ["Pdf"] = new AttributeValue { S = $"s3://{bucket}/{clave}" },
            ["Fecha"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") }
        }
    });

    return Results.Created($"/comprobantes/{id}", new { id, cliente, total, pdf = $"s3://{bucket}/{clave}" });
})
.DisableAntiforgery(); // API de práctica sin formularios web; en producción va con autenticación

// 2) Listar los comprobantes guardados en DynamoDB
app.MapGet("/comprobantes", async (IAmazonDynamoDB dynamo) =>
{
    var resultado = await dynamo.ScanAsync(new ScanRequest { TableName = tabla });

    static string? Texto(Dictionary<string, AttributeValue> item, string campo) =>
        item.TryGetValue(campo, out var valor) ? valor.S ?? valor.N : null;

    var comprobantes = (resultado.Items ?? []).Select(item => new
    {
        id = Texto(item, "Id"),
        cliente = Texto(item, "Cliente"),
        total = Texto(item, "Total"),
        pdf = Texto(item, "Pdf"),
        fecha = Texto(item, "Fecha")
    });

    return Results.Ok(comprobantes);
});

// 3) Descargar el PDF de un comprobante desde S3
app.MapGet("/comprobantes/{id}/pdf", async (string id, IAmazonS3 s3) =>
{
    try
    {
        var objeto = await s3.GetObjectAsync(bucket, $"{id}.pdf");
        return Results.Stream(objeto.ResponseStream, "application/pdf", $"{id}.pdf");
    }
    catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound($"No existe el PDF del comprobante {id}.");
    }
});

app.Run();