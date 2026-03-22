namespace SocialMediaBot.Shared.Storage;

public class MinioOptions
{
    public const string SectionName = "Minio";
    public required string Endpoint { get; set; }
    public required string AccessKey { get; set; }
    public required string SecretKey { get; set; }
    public bool Secure { get; set; } = true;
    public string BucketName { get; set; } = "social-media-bot";
}
