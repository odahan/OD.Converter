using System.Security.Cryptography;

namespace OD.Converter.Services.Modules;

/// <summary>Streams HTTPS artifacts to disk, verifies pinned size/hash and removes interrupted downloads.</summary>
public sealed class ModuleDownloader(HttpClient client) : IModuleDownloader
{
    /// <inheritdoc />
    public async Task DownloadAsync(ModuleDownload download, string destination,
        IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        Validate(download);
        var created = false;
        try
        {
            using var response = await client.GetAsync(download.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("Module downloads must remain on HTTPS after redirects.");
            if (response.Content.Headers.ContentLength is long size && size != download.SizeBytes)
                throw new InvalidDataException($"Download size differs from the pinned distribution: {download.Url}. Expected {download.SizeBytes} bytes, received Content-Length {size}.");
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous);
            created = true;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long received = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, ct)) > 0)
            {
                received = checked(received + count);
                if (received > download.SizeBytes) throw new InvalidDataException("Download exceeds the pinned size.");
                hash.AppendData(buffer, 0, count);
                await output.WriteAsync(buffer.AsMemory(0, count), ct);
                progress?.Report(new ModuleProgress("Downloading", received, download.SizeBytes));
            }

            if (received != download.SizeBytes ||
                !Convert.ToHexString(hash.GetHashAndReset()).Equals(download.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Download integrity check failed (size or SHA-256 mismatch).");
        }
        catch
        {
            if (created) File.Delete(destination);
            throw;
        }
    }

    /// <summary>Rejects incomplete or unpinned recipe entries before any network access.</summary>
    public static void Validate(ModuleDownload download)
    {
        if (!download.Url.IsAbsoluteUri || download.Url.Scheme != Uri.UriSchemeHttps ||
            download.SizeBytes <= 0 || download.Sha256.Length != 64 || !download.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("A module artifact requires HTTPS, a positive pinned size and a SHA-256 digest.");
    }
}
