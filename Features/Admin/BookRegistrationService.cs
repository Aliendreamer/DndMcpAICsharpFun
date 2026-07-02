using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Ingestion;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace DndMcpAICsharpFun.Features.Admin;

public abstract record BookRegistrationResult
{
    public sealed record Success(IngestionRecord Record, IReadOnlyList<string> Suggestions) : BookRegistrationResult;

    public sealed record BadRequest(string Message) : BookRegistrationResult;

    public sealed record Unprocessable(string Message) : BookRegistrationResult;

    private BookRegistrationResult() { }
}

public sealed partial class BookRegistrationService(
    IIngestionTracker tracker,
    BookSourceRegistry registry,
    IOptions<IngestionOptions> ingestionOptions,
    ILogger<BookRegistrationService> logger)
{
    public async Task<BookRegistrationResult> RegisterAsync(
        string? contentType, Stream body, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(contentType) ||
            !contentType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
            return new BookRegistrationResult.BadRequest("Expected multipart/form-data.");

        var boundary = HeaderUtilities.RemoveQuotes(
            MediaTypeHeaderValue.Parse(contentType).Boundary).Value;
        if (string.IsNullOrEmpty(boundary))
            return new BookRegistrationResult.BadRequest("Missing multipart boundary.");

        var booksPath = ingestionOptions.Value.BooksPath;
        Directory.CreateDirectory(booksPath);

        string? version = null, displayName = null, originalFileName = null, filePath = null;
        string? bookTypeRaw = null;
        string? fivetoolsSourceKey = null;

        var reader = new MultipartReader(boundary, body);
        var section = await reader.ReadNextSectionAsync(ct);
        try
        {
            while (section is not null)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var cd))
                {
                    section = await reader.ReadNextSectionAsync(ct);
                    continue;
                }

                if (cd.FileName.HasValue || cd.FileNameStar.HasValue)
                {
                    var rawName = (cd.FileNameStar.HasValue ? cd.FileNameStar.Value : cd.FileName.Value) ?? string.Empty;
                    originalFileName = SanitizeDisplayFileName(rawName);
                    if (!originalFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        return new BookRegistrationResult.BadRequest("Only PDF files are accepted.");

                    filePath = Path.Combine(booksPath, $"{Guid.NewGuid():N}.pdf");
                    await using var dest = File.Create(filePath);
                    await section.Body.CopyToAsync(dest, ct);
                }
                else if (cd.Name.HasValue)
                {
                    using var sr = new StreamReader(section.Body);
                    var value = await sr.ReadToEndAsync(ct);
                    switch (cd.Name.Value)
                    {
                        case "version": version = value; break;
                        case "displayName": displayName = value; break;
                        case "bookType": bookTypeRaw = value; break;
                        case "fivetoolsSourceKey": fivetoolsSourceKey = value; break;
                    }
                }

                section = await reader.ReadNextSectionAsync(ct);
            }

            if (filePath is null || originalFileName is null)
                return new BookRegistrationResult.BadRequest("file is required.");
            if (string.IsNullOrEmpty(displayName))
                return new BookRegistrationResult.BadRequest("displayName is required.");
            if (!Enum.TryParse<DndVersion>(version, ignoreCase: true, out var parsedVersion))
                return new BookRegistrationResult.BadRequest(
                    $"Invalid version '{version}'. Valid values: {string.Join(", ", Enum.GetNames<DndVersion>())}");

            var bookType = Enum.TryParse<BookType>(bookTypeRaw, ignoreCase: true, out var parsedType)
                ? parsedType
                : BookType.Unknown;

            if (fivetoolsSourceKey is not null && registry.TryGetBook(fivetoolsSourceKey) is null)
                return new BookRegistrationResult.Unprocessable(
                    $"Unknown fivetoolsSourceKey '{fivetoolsSourceKey}'. Call GET /admin/5etools/sources for valid values.");

            var record = new IngestionRecord
            {
                FilePath = filePath,
                FileName = originalFileName,
                FileHash = string.Empty,
                Version = parsedVersion.ToString(),
                DisplayName = displayName,
                Status = IngestionStatus.Pending,
                BookType = bookType,
                FivetoolsSourceKey = fivetoolsSourceKey,
            };

            var created = await tracker.CreateAsync(record, ct);
            LogBookRegistered(logger, created.DisplayName, created.Id, originalFileName);
            filePath = null;
            var suggestions = fivetoolsSourceKey is null
                ? registry.SuggestByName(displayName ?? "")
                : (IReadOnlyList<string>)Array.Empty<string>();
            return new BookRegistrationResult.Success(created, suggestions);
        }
        finally
        {
            if (filePath is not null && File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered book {DisplayName} (id={Id}, file={File})")]
    private static partial void LogBookRegistered(ILogger logger, string displayName, int id, string file);

    private static string SanitizeDisplayFileName(string raw)
    {
        var name = Path.GetFileName(raw.Replace('\\', '/'));
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name
            .Where(c => !char.IsControl(c) && Array.IndexOf(invalid, c) < 0)
            .ToArray();
        var cleaned = new string(chars).Trim().Trim('.');
        if (cleaned.Length > 200) cleaned = cleaned[..200];
        return cleaned.Length == 0 ? "upload.pdf" : cleaned;
    }
}
