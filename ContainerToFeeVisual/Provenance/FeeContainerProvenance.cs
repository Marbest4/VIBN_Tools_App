using System.IO.Compression;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace VIBN_Tools.ContainerToFeeVisual;

/// <summary>
/// Versioned, self-contained provenance stored on the generated FEE BasicFrame.
/// It contains the selected source containers instead of trying to infer their
/// original semantics from mutable FEE object names later.
/// </summary>
public sealed record FeeContainerProvenanceSnapshot(
    IReadOnlyDictionary<string, string> Tags,
    XDocument ContainerDocument,
    int ContainerCount,
    int SignalCount,
    string SourceFingerprint);

public static class FeeContainerProvenanceCodec
{
    public const string SchemaKey = "vibn.container2fee.schema";
    public const string FormatKey = "vibn.container2fee.format";
    public const string HashKey = "vibn.container2fee.sha256";
    public const string SourceHashKey = "vibn.container2fee.source-sha256";
    public const string PartCountKey = "vibn.container2fee.part-count";
    public const string PartPrefix = "vibn.container2fee.part.";
    public const string CurrentSchema = "1";
    public const string CurrentFormat = "gzip-base64-utf8-xml";

    private const int ChunkLength = 3000;
    private const int MaximumParts = 20_000;
    private const int MaximumUncompressedBytes = 50 * 1024 * 1024;

    public static FeeContainerProvenanceSnapshot Create(
        XDocument source,
        IReadOnlySet<string> includedContainerIds,
        string sourceFingerprint)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(includedContainerIds);

        var projected = new XDocument(source);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var container in projected
                     .Descendants()
                     .Where(element => element.Name.LocalName == "Container")
                     .ToArray())
        {
            var sourceId = container.Attribute("id")?.Value ?? string.Empty;
            var component = ChildValue(container, "Component");
            var type = ChildValue(container, "Type");
            var identity = $"{sourceId}\u001f{component}\u001f{type}";
            occurrences.TryGetValue(identity, out var occurrence);
            occurrences[identity] = ++occurrence;
            var containerId = ContainerXmlVisualPlanParser.CreateContainerId(
                sourceId,
                component,
                type,
                occurrence);
            if (!includedContainerIds.Contains(containerId))
                container.Remove();
        }

        var xml = projected.ToString(SaveOptions.DisableFormatting);
        var plainBytes = Encoding.UTF8.GetBytes(xml);
        var encoded = Convert.ToBase64String(Compress(plainBytes));
        var partCount = (encoded.Length + ChunkLength - 1) / ChunkLength;
        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SchemaKey] = CurrentSchema,
            [FormatKey] = CurrentFormat,
            [HashKey] = Convert.ToHexString(SHA256.HashData(plainBytes)).ToLowerInvariant(),
            [SourceHashKey] = sourceFingerprint ?? string.Empty,
            [PartCountKey] = partCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        for (var index = 0; index < partCount; index++)
        {
            var offset = index * ChunkLength;
            tags[$"{PartPrefix}{index:D5}"] = encoded.Substring(
                offset,
                Math.Min(ChunkLength, encoded.Length - offset));
        }

        return BuildSnapshot(tags, projected, sourceFingerprint ?? string.Empty);
    }

    public static bool TryRead(
        IReadOnlyDictionary<string, string>? tags,
        out FeeContainerProvenanceSnapshot? snapshot,
        out string error)
    {
        snapshot = null;
        error = string.Empty;
        if (tags is null || !tags.TryGetValue(SchemaKey, out var schema))
        {
            error = "Der FEE-Root besitzt keine Container2FEE-Provenienz.";
            return false;
        }
        if (!string.Equals(schema, CurrentSchema, StringComparison.Ordinal))
        {
            error = $"Die Container2FEE-Provenienzversion '{schema}' wird nicht unterstützt.";
            return false;
        }
        if (!tags.TryGetValue(FormatKey, out var format) ||
            !string.Equals(format, CurrentFormat, StringComparison.Ordinal))
        {
            error = "Das Format der Container2FEE-Provenienz wird nicht unterstützt.";
            return false;
        }
        if (!tags.TryGetValue(PartCountKey, out var countText) ||
            !int.TryParse(countText, out var partCount) ||
            partCount is < 1 or > MaximumParts)
        {
            error = "Die Container2FEE-Provenienz enthält eine ungültige Teileanzahl.";
            return false;
        }

        var encoded = new StringBuilder(partCount * ChunkLength);
        for (var index = 0; index < partCount; index++)
        {
            if (!tags.TryGetValue($"{PartPrefix}{index:D5}", out var part))
            {
                error = $"Teil {index + 1} der Container2FEE-Provenienz fehlt.";
                return false;
            }
            encoded.Append(part);
        }

        try
        {
            var plainBytes = Decompress(Convert.FromBase64String(encoded.ToString()));
            if (!tags.TryGetValue(HashKey, out var expectedHash) ||
                !string.Equals(
                    expectedHash,
                    Convert.ToHexString(SHA256.HashData(plainBytes)).ToLowerInvariant(),
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "Die Prüfsumme der Container2FEE-Provenienz ist ungültig.";
                return false;
            }

            using var stream = new MemoryStream(plainBytes, writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumUncompressedBytes,
            });
            var document = XDocument.Load(reader);
            var sourceHash = tags.TryGetValue(SourceHashKey, out var value) ? value : string.Empty;
            snapshot = BuildSnapshot(tags, document, sourceHash);
            return true;
        }
        catch (Exception exception) when (
            exception is FormatException or InvalidDataException or IOException or XmlException)
        {
            error = $"Die Container2FEE-Provenienz ist beschädigt: {exception.Message}";
            return false;
        }
    }

    public static void SaveAtomically(FeeContainerProvenanceSnapshot snapshot, string targetPath)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var fullPath = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Der Exportpfad besitzt kein Verzeichnis.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            snapshot.ContainerDocument.Save(temporaryPath, SaveOptions.None);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static FeeContainerProvenanceSnapshot BuildSnapshot(
        IReadOnlyDictionary<string, string> tags,
        XDocument document,
        string sourceFingerprint) =>
        new(
            new Dictionary<string, string>(tags, StringComparer.Ordinal),
            new XDocument(document),
            document.Descendants().Count(element => element.Name.LocalName == "Container"),
            document.Descendants().Count(element => element.Name.LocalName == "Entry"),
            sourceFingerprint);

    private static byte[] Compress(byte[] value)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(value);
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] value)
    {
        using var input = new MemoryStream(value, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
            if (output.Length > MaximumUncompressedBytes)
                throw new InvalidDataException("Die entpackte Provenienz überschreitet 50 MB.");
        }
        return output.ToArray();
    }

    private static string ChildValue(XElement element, string localName) =>
        element.Elements().FirstOrDefault(child => child.Name.LocalName == localName)?.Value ?? string.Empty;
}
