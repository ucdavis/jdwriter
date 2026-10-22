using System.Text.RegularExpressions;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace Server.Core.Uploads;

/// <summary>
/// Where uploaded files wait between the browser and the app. Set one of the two to turn staging on:
/// <see cref="BlobServiceUri"/> in Azure, where the app signs upload links with its own managed
/// identity (no storage keys anywhere), or <see cref="ConnectionString"/> locally, for Azurite.
/// With neither, files are sent to the app directly, as they always were.
/// </summary>
public sealed class UploadOptions
{
    public const string Section = "Uploads";

    /// <summary>The storage account's blob endpoint, e.g. https://stjdwriterprod.blob.core.windows.net/.</summary>
    public string? BlobServiceUri { get; set; }

    /// <summary>A connection string with an account key: Azurite's "UseDevelopmentStorage=true" locally.</summary>
    public string? ConnectionString { get; set; }

    public string Container { get; set; } = "uploads";

    /// <summary>How long an upload link stays valid.</summary>
    public int LinkMinutes { get; set; } = 10;

    public bool Enabled => !string.IsNullOrWhiteSpace(BlobServiceUri) || !string.IsNullOrWhiteSpace(ConnectionString);

    /// <summary>The origin the browser uploads to, for the content security policy; null when staging is off.</summary>
    public string? BrowserOrigin()
    {
        if (!string.IsNullOrWhiteSpace(BlobServiceUri) && Uri.TryCreate(BlobServiceUri, UriKind.Absolute, out var uri))
        {
            return uri.GetLeftPart(UriPartial.Authority);
        }

        return null;
    }
}

/// <summary>Where the browser puts one file, and the name it reports back when it's there.</summary>
public sealed record StagedSlot(string BlobName, Uri UploadUrl, DateTimeOffset ExpiresAt);

/// <summary>A staged file that can't be used — not the caller's, gone, or too large — said for the user.</summary>
public sealed class StagedUploadException(string message) : Exception(message);

/// <summary>
/// Uploads that go to blob storage first, never through the front door's web application firewall:
/// the browser puts each file straight into a private container with a short-lived, write-only link
/// for one blob, and the app then takes it — reads it and deletes it. A storage lifecycle rule
/// removes anything left behind (a closed tab) within a day.
/// </summary>
public interface IUploadStaging
{
    bool Enabled { get; }

    Task<StagedSlot> StageAsync(string owner, CancellationToken ct = default);

    /// <summary>The staged file's bytes, removed from storage. Refuses one that isn't the owner's or is too large.</summary>
    Task<byte[]> TakeAsync(string owner, string blobName, long maxBytes, CancellationToken ct = default);
}

/// <summary>Staging off: uploads come in the request itself.</summary>
public sealed class DirectUploads : IUploadStaging
{
    public bool Enabled => false;

    public Task<StagedSlot> StageAsync(string owner, CancellationToken ct = default) =>
        throw new InvalidOperationException("Upload staging is not configured.");

    public Task<byte[]> TakeAsync(string owner, string blobName, long maxBytes, CancellationToken ct = default) =>
        throw new StagedUploadException("This server takes files directly; send the file itself.");
}

public sealed partial class BlobUploadStaging : IUploadStaging
{
    /// <summary>A staged blob's name: its owner, then a random id. Never the file's own name — HRTMS file names carry position numbers.</summary>
    [GeneratedRegex("^[0-9a-f]{16}/[0-9a-f]{32}$")]
    private static partial Regex BlobName();

    private readonly BlobServiceClient _service;
    private readonly BlobContainerClient _container;
    private readonly UploadOptions _options;
    private readonly bool _local;
    private bool _localReady;

    public BlobUploadStaging(UploadOptions options, TokenCredential? credential = null)
    {
        _options = options;
        _local = !string.IsNullOrWhiteSpace(options.ConnectionString);
        _service = _local
            ? new BlobServiceClient(options.ConnectionString)
            : new BlobServiceClient(new Uri(options.BlobServiceUri!), credential ?? new DefaultAzureCredential());
        _container = _service.GetBlobContainerClient(options.Container);
    }

    public bool Enabled => true;

    public async Task<StagedSlot> StageAsync(string owner, CancellationToken ct = default)
    {
        if (_local && !_localReady)
        {
            // Azurite starts empty and open to no browser; in Azure the container and its CORS rules
            // are the infrastructure's. Locally the dev server is a different origin from Azurite.
            await _container.CreateIfNotExistsAsync(cancellationToken: ct);
            var properties = (await _service.GetPropertiesAsync(ct)).Value;
            properties.Cors =
            [
                new BlobCorsRule
                {
                    AllowedOrigins = "*",
                    AllowedMethods = "PUT,OPTIONS",
                    AllowedHeaders = "*",
                    ExposedHeaders = "*",
                    MaxAgeInSeconds = 300,
                },
            ];
            await _service.SetPropertiesAsync(properties, ct);
            _localReady = true;
        }

        var name = $"{owner}/{Guid.NewGuid():N}";
        var blob = _container.GetBlobClient(name);
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(_options.LinkMinutes);
        var sas = new BlobSasBuilder(BlobSasPermissions.Create | BlobSasPermissions.Write, expires)
        {
            BlobContainerName = _container.Name,
            BlobName = name,
            Resource = "b",
            // Clock skew between the browser and storage.
            StartsOn = now.AddMinutes(-5),
            Protocol = _local ? SasProtocol.HttpsAndHttp : SasProtocol.Https,
        };

        Uri url;
        if (blob.CanGenerateSasUri)
        {
            url = blob.GenerateSasUri(sas);
        }
        else
        {
            // Signed with a key Entra issues to the app's identity for this link's lifetime.
            var key = await _service.GetUserDelegationKeyAsync(now.AddMinutes(-5), expires, ct);
            url = new BlobUriBuilder(blob.Uri) { Sas = sas.ToSasQueryParameters(key.Value, _service.AccountName) }.ToUri();
        }

        return new StagedSlot(name, url, expires);
    }

    public async Task<byte[]> TakeAsync(string owner, string blobName, long maxBytes, CancellationToken ct = default)
    {
        if (!BlobName().IsMatch(blobName) || !blobName.StartsWith($"{owner}/", StringComparison.Ordinal))
        {
            throw new StagedUploadException("That upload isn't one of yours. Choose the file again.");
        }

        var blob = _container.GetBlobClient(blobName);
        try
        {
            var size = (await blob.GetPropertiesAsync(cancellationToken: ct)).Value.ContentLength;
            if (size > maxBytes)
            {
                throw new StagedUploadException($"That file is too large here (the limit is {maxBytes / (1024 * 1024)} MB).");
            }

            var content = await blob.DownloadContentAsync(ct);
            return content.Value.Content.ToArray();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new StagedUploadException("The upload wasn't found — it may have taken too long. Choose the file again.");
        }
        finally
        {
            await blob.DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
        }
    }
}
