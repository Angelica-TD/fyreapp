using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace FyreApp.Services.Files;

// Where FyreApp keeps files (docs/uptick-migration.md §8): Amazon S3 in production, a local folder in development.
// Keys are paths such as "uptick/documentfiles/<id>.jpg".
public interface IFileStore
{
    // False when storage isn't set up (Storage:Provider), so nothing can be stored
    bool IsConfigured { get; }

    // e.g. "S3 bucket fyreapp-files (ap-southeast-2)"
    string Description { get; }

    Task PutAsync(string key, Stream content, string? contentType, CancellationToken ct = default);

    // Null when there's no such file
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default);
}

public sealed class FileStorageOptions
{
    // "S3" or "Local"; empty means not set up
    public string Provider { get; set; } = string.Empty;

    public LocalOptions Local { get; set; } = new();
    public S3Options S3 { get; set; } = new();

    public sealed class LocalOptions
    {
        // Relative to the app's folder
        public string Root { get; set; } = "App_Data/files";
    }

    public sealed class S3Options
    {
        public string Bucket { get; set; } = string.Empty;
        public string Region { get; set; } = "ap-southeast-2";

        // For an S3-compatible provider (R2, B2, …); empty for Amazon
        public string ServiceUrl { get; set; } = string.Empty;

        // Secrets (Storage__S3__AccessKey / SecretKey in /etc/fyreapp.env); empty uses the AWS SDK's own lookup
        public string AccessKey { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
    }
}

public static class FileStores
{
    public static IFileStore Create(IOptions<FileStorageOptions> options, IWebHostEnvironment env) =>
        options.Value.Provider.Trim().ToUpperInvariant() switch
        {
            "S3" => new S3FileStore(options.Value.S3),
            "LOCAL" => new LocalFileStore(Path.Combine(env.ContentRootPath, options.Value.Local.Root)),
            _ => new UnconfiguredFileStore()
        };
}

public sealed class UnconfiguredFileStore : IFileStore
{
    public bool IsConfigured => false;
    public string Description => "Not set up (Storage:Provider)";

    public Task PutAsync(string key, Stream content, string? contentType, CancellationToken ct = default) =>
        throw new InvalidOperationException("File storage isn't set up (Storage:Provider).");

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) => Task.FromResult<Stream?>(null);
}

public sealed class LocalFileStore : IFileStore
{
    private readonly string _root;

    public LocalFileStore(string root) => _root = Path.GetFullPath(root);

    public bool IsConfigured => true;
    public string Description => $"Local folder {_root}";

    public async Task PutAsync(string key, Stream content, string? contentType, CancellationToken ct = default)
    {
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Written alongside, then moved into place, so a cut-off write never leaves a partial file under the key
        var temp = path + ".part";
        await using (var file = File.Create(temp))
            await content.CopyToAsync(file, ct);
        File.Move(temp, path, overwrite: true);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var path = PathOf(key);
        return Task.FromResult<Stream?>(File.Exists(path) ? File.OpenRead(path) : null);
    }

    // Keys are FyreApp's own ("uptick/…"); anything that would leave the root is refused
    private string PathOf(string key)
    {
        var path = Path.GetFullPath(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException($"Invalid file key: {key}", nameof(key));
        return path;
    }
}

public sealed class S3FileStore : IFileStore
{
    private readonly FileStorageOptions.S3Options _options;
    private readonly Lazy<IAmazonS3> _client;

    public S3FileStore(FileStorageOptions.S3Options options)
    {
        _options = options;
        _client = new Lazy<IAmazonS3>(() =>
        {
            var config = new AmazonS3Config();
            if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
            {
                config.ServiceURL = options.ServiceUrl;
                config.ForcePathStyle = true;
                config.AuthenticationRegion = options.Region;
            }
            else config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);

            return string.IsNullOrWhiteSpace(options.AccessKey)
                ? new AmazonS3Client(config)
                : new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);
        });
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Bucket);
    public string Description => $"S3 bucket {_options.Bucket} ({_options.Region})";

    public async Task PutAsync(string key, Stream content, string? contentType, CancellationToken ct = default)
    {
        await _client.Value.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            InputStream = content,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            AutoCloseStream = false
        }, ct);
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Value.GetObjectAsync(_options.Bucket, key, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
