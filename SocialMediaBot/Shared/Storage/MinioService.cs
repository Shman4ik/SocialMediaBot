using System.Text;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace SocialMediaBot.Shared.Storage;

public class MinioService(IOptions<MinioOptions> options, ILogger<MinioService> logger)
{
    private readonly MinioOptions _opts = options.Value;
    private readonly IMinioClient _client = BuildClient(options.Value);

    private static IMinioClient BuildClient(MinioOptions opts)
    {
        if (string.IsNullOrWhiteSpace(opts.Endpoint))
            throw new InvalidOperationException("Minio:Endpoint is not configured.");
        if (string.IsNullOrWhiteSpace(opts.AccessKey))
            throw new InvalidOperationException("Minio:AccessKey is not configured.");
        if (string.IsNullOrWhiteSpace(opts.SecretKey))
            throw new InvalidOperationException("Minio:SecretKey is not configured.");

        var builder = new MinioClient()
            .WithEndpoint(opts.Endpoint)
            .WithCredentials(opts.AccessKey, opts.SecretKey);

        if (opts.Secure)
            builder = builder.WithSSL();

        return builder.Build();
    }

    public async Task<string?> GetTextAsync(string objectName, CancellationToken ct = default)
    {
        string? result = null;

        var args = new GetObjectArgs()
            .WithBucket(_opts.BucketName)
            .WithObject(objectName)
            .WithCallbackStream((stream, token) =>
            {
                using var reader = new StreamReader(stream);
                result = reader.ReadToEndAsync(token).GetAwaiter().GetResult();
                return Task.CompletedTask;
            });

        await _client.GetObjectAsync(args, ct);

        logger.LogInformation("Loaded prompt from S3: {ObjectName}", objectName);
        return result;
    }

    public async Task PutTextAsync(string objectName, string content, CancellationToken ct = default)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        using var stream = new MemoryStream(bytes);

        var args = new PutObjectArgs()
            .WithBucket(_opts.BucketName)
            .WithObject(objectName)
            .WithStreamData(stream)
            .WithObjectSize(bytes.Length)
            .WithContentType("text/plain; charset=utf-8");

        await _client.PutObjectAsync(args, ct);
        logger.LogInformation("Saved prompt to S3: {ObjectName}", objectName);
    }
}
