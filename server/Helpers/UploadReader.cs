using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Server.Core.Uploads;

namespace Server.Helpers;

/// <summary>One uploaded file, however it arrived.</summary>
public sealed record UploadedFile(string Name, string ContentType, byte[] Bytes);

/// <summary>
/// Every file in an upload request, wherever it was put: in the request itself (local development,
/// or a server without staging), or in blob storage, named by the "staged" form field — a JSON list of
/// { blob, name, type }. Staged files are taken, so read once and deleted.
/// </summary>
public sealed class UploadReader
{
    public const string StagedField = "staged";

    private const int MaxFiles = 500;

    private readonly IUploadStaging _staging;

    public UploadReader(IUploadStaging staging) => _staging = staging;

    private sealed record StagedRef(string Blob, string Name, string? Type);

    /// <summary>
    /// Who staged a file, as it appears in the blob's name: a hash of the sign-in id, so a name says
    /// nothing about the person and one user can't take another's upload.
    /// </summary>
    public static string? Owner(ClaimsPrincipal user)
    {
        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrEmpty(id)
            ? null
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..16];
    }

    public async Task<List<UploadedFile>> ReadAsync(HttpRequest request, long maxBytesEach, CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            return [];
        }

        var form = await request.ReadFormAsync(ct);
        var files = new List<UploadedFile>();
        foreach (var file in form.Files)
        {
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            files.Add(new UploadedFile(file.FileName, file.ContentType ?? "", buffer.ToArray()));
        }

        var staged = form[StagedField].ToString();
        if (staged.Length == 0)
        {
            return files;
        }

        var owner = Owner(request.HttpContext.User);
        if (!_staging.Enabled || owner is null)
        {
            throw new StagedUploadException("This server takes files directly; send the file itself.");
        }

        List<StagedRef>? refs;
        try
        {
            refs = JsonSerializer.Deserialize<List<StagedRef>>(staged, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            refs = null;
        }

        if (refs is null || refs.Count > MaxFiles)
        {
            throw new StagedUploadException("The upload couldn't be read. Choose the files again.");
        }

        foreach (var r in refs)
        {
            var bytes = await _staging.TakeAsync(owner, r.Blob, maxBytesEach, ct);
            files.Add(new UploadedFile(r.Name, r.Type ?? "", bytes));
        }

        return files;
    }
}
