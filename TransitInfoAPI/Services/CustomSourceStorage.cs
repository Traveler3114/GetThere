using TransitInfoAPI.Exceptions;

namespace TransitInfoAPI.Services;

/// <summary>
/// Resolves where a custom source's uploaded files live, refusing any name that would escape its
/// directory.
/// <para>
/// The same security control as <see cref="FeedStorage"/> and for the same reason: the file name
/// arrives from an admin-supplied request row and flows into <c>Path.Combine</c>, so without the
/// containment check a request pointing at <c>../../appsettings.json</c> reads whatever the process
/// can reach.
/// </para>
/// </summary>
public sealed class CustomSourceStorage
{
    private readonly string _root;

    public CustomSourceStorage(string contentRootPath)
    {
        _root = Path.GetFullPath(Path.Combine(contentRootPath, "custom-sources"));
    }

    public string DirectoryFor(int sourceId)
    {
        if (sourceId <= 0) throw new AppException("Invalid custom source id.", 400, "INVALID_SOURCE_ID");
        var resolved = Path.GetFullPath(Path.Combine(_root, sourceId.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        if (!resolved.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new AppException("Invalid custom source id.", 400, "INVALID_SOURCE_ID");

        return resolved;
    }

    public string FilePathFor(int sourceId, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.IsPathRooted(fileName))
            throw new AppException($"Invalid upload file name '{fileName}'.", 400, "INVALID_UPLOAD_NAME");

        var dir = DirectoryFor(sourceId);
        var resolved = Path.GetFullPath(Path.Combine(dir, fileName));

        if (!resolved.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new AppException($"Invalid upload file name '{fileName}'.", 400, "INVALID_UPLOAD_NAME");

        return resolved;
    }
}
