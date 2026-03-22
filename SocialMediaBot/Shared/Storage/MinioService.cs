using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace SocialMediaBot.Shared.Storage;

public class MinioService(IOptions<MinioOptions> options, ILogger<MinioService> logger)
{
    private readonly MinioOptions _opts = options.Value;

    public async Task<string?> GetTextAsync(string objectName, CancellationToken ct = default)
    {
        var client = new MinioClient()
            .WithEndpoint(_opts.Endpoint)
            .WithCredentials(_opts.AccessKey, _opts.SecretKey)
            .WithSSL(_opts.Secure)
            .Build();

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

        await client.GetObjectAsync(args, ct);

        logger.LogInformation("Loaded prompt from S3: {ObjectName}", objectName);
        return result;
    }
}
