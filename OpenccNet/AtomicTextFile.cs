using System.Text;

namespace OpenccNet;

internal static class AtomicTextFile
{
    internal static async Task WriteAllTextAsync(
        string destinationPath,
        string content,
        Encoding encoding,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(destinationPath);
        var tempPath = Path.Combine(Path.GetDirectoryName(fullPath)!, $".openccnet-{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(tempPath, content, encoding, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // Preserve the original write, cancellation, or publication exception.
            }

            throw;
        }
    }
}
