using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Infrastructure.Docling;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public sealed partial class DoclingPdfConverter : IDoclingPdfConverter, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private readonly TimeSpan _maxWait;
    private readonly ILogger<DoclingPdfConverter> _logger;

    public DoclingPdfConverter(IOptions<DoclingOptions> options, ILogger<DoclingPdfConverter> logger)
    {
        var opts = options.Value;
        _http = new HttpClient
        {
            BaseAddress = new Uri(opts.BaseUrl),
            // Per-HTTP-call timeout. Async submission and polls are quick; the
            // long wait is on our side via _maxWait, polled in PollInterval steps.
            Timeout = TimeSpan.FromSeconds(60),
        };
        _maxWait = TimeSpan.FromSeconds(opts.RequestTimeoutSeconds);
        _logger = logger;
    }

    public async Task<DoclingDocument> ConvertAsync(string filePath, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(filePath);
        LogConvertStart(_logger, fileName);

        await WaitUntilHealthyAsync(ct);

        var taskId = await SubmitAsync(filePath, fileName, ct);
        LogTaskSubmitted(_logger, fileName, taskId);

        await PollUntilDoneAsync(taskId, fileName, ct);

        var doc = await FetchResultAsync(taskId, ct);
        LogConvertDone(_logger, fileName, doc.Items.Count);
        return doc;
    }

    private async Task WaitUntilHealthyAsync(CancellationToken ct)
    {
        var waits = new[] { 0, 5, 10, 15, 30, 30, 30, 30 }; // total ~2 min
        Exception? last = null;
        foreach (var seconds in waits)
        {
            ct.ThrowIfCancellationRequested();
            if (seconds > 0)
            {
                LogWaitingForHealthy(_logger, seconds);
                await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
            }
            try
            {
                using var response = await _http.GetAsync("/health", ct);
                if (response.IsSuccessStatusCode) return;
                last = new InvalidOperationException(
                    $"docling-serve /health returned {(int)response.StatusCode}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                last = ex;
            }
        }
        throw new InvalidOperationException(
            "docling-serve never became healthy within the pre-flight window", last);
    }

    private async Task<string> SubmitAsync(string filePath, string fileName, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        using var multipart = new MultipartFormDataContent();
        multipart.Add(fileContent, "files", fileName);
        multipart.Add(new StringContent("json"), "to_formats");
        // PHB-style books have embedded text; OCR is wasted CPU and (more
        // importantly) wasted memory. Skip it. If a future PDF needs OCR,
        // expose this as a per-book flag.
        multipart.Add(new StringContent("false"), "do_ocr");
        // Avoid embedding base64 images in the JSON response — for a 310-page
        // book this can balloon the response to hundreds of MB and OOM the
        // .NET HttpContent buffer. We don't index images anyway.
        multipart.Add(new StringContent("placeholder"), "image_export_mode");

        using var response = await _http.PostAsync("/v1/convert/file/async", multipart, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw DoclingError("async submit", response, body);

        var node = JsonNode.Parse(body) as JsonObject
            ?? throw new InvalidOperationException("docling-serve async submit response was not a JSON object");
        return node["task_id"]?.GetValue<string>()
            ?? throw new InvalidOperationException("docling-serve async submit response missing task_id");
    }

    private async Task PollUntilDoneAsync(string taskId, string fileName, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + _maxWait;
        var iteration = 0;
        var transientFailures = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(PollInterval, ct);

            string body;
            HttpStatusCode statusCode;
            try
            {
                using var response = await _http.GetAsync($"/v1/status/poll/{taskId}", ct);
                body = await response.Content.ReadAsStringAsync(ct);
                statusCode = response.StatusCode;

                if (statusCode == HttpStatusCode.NotFound)
                    throw new InvalidOperationException(
                        $"docling-serve task {taskId} no longer exists (server likely restarted, in-memory task state lost)");

                if (!response.IsSuccessStatusCode)
                    throw DoclingError($"poll {taskId}", response, body);
            }
            catch (HttpRequestException ex)
            {
                transientFailures++;
                LogPollTransient(_logger, taskId, transientFailures, ex.Message);
                if (transientFailures >= 6)   // ~1 minute of failures with the 10s interval
                    throw new InvalidOperationException(
                        $"docling-serve poll for task {taskId} failed {transientFailures} times in a row", ex);
                await WaitUntilHealthyAsync(ct);
                continue;
            }
            transientFailures = 0;

            var node = JsonNode.Parse(body) as JsonObject;
            var taskStatus = node?["task_status"]?.GetValue<string>() ?? "unknown";

            iteration++;
            LogPoll(_logger, fileName, taskId, taskStatus, iteration);

            switch (taskStatus)
            {
                case "success":
                case "partial_success":
                case "skipped":
                    return;
                case "failure":
                    throw new InvalidOperationException(
                        $"docling-serve task {taskId} reported failure: {body}");
                case "pending":
                case "started":
                    break;
                default:
                    LogUnknownStatus(_logger, taskId, taskStatus);
                    break;
            }

            if (DateTimeOffset.UtcNow > deadline)
                throw new TimeoutException(
                    $"docling-serve task {taskId} did not complete within {_maxWait.TotalSeconds:N0}s (last status: {taskStatus})");
        }
    }

    private async Task<DoclingDocument> FetchResultAsync(string taskId, CancellationToken ct)
    {
        using var response = await _http.GetAsync(
            $"/v1/result/{taskId}",
            HttpCompletionOption.ResponseHeadersRead,
            ct);
        if (!response.IsSuccessStatusCode)
        {
            // Body small enough to read on error paths.
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw DoclingError($"result {taskId}", response, errorBody);
        }

        // Stream parsing — for big books the JSON can be 100s of MB.
        // ReadAsStringAsync would buffer the whole thing into a single string,
        // which OOMs on large allocations.
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await ParseResponseStreamAsync(stream, ct);
    }

    private static InvalidOperationException DoclingError(string stage, HttpResponseMessage response, string body)
    {
        var snippet = body.Length > 500 ? body[..500] : body;
        return new InvalidOperationException(
            $"docling-serve {stage} returned {(int)response.StatusCode} {response.StatusCode}: {snippet}");
    }

    /// <summary>
    /// Parses docling-serve's <c>ConvertDocumentResponse</c> into our internal
    /// <see cref="DoclingDocument"/>. Expected shape:
    /// <code>
    /// { "document": {
    ///     "md_content": "...",
    ///     "json_content": {
    ///       "texts": [ { "label": "...", "text": "...", "prov": [ { "page_no": N } ] }, ... ]
    ///     }
    ///   } }
    /// </code>
    /// </summary>
    public static DoclingDocument ParseResponse(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("docling-serve response was not a JSON object");

        var docNode = (root["document"] as JsonObject) ?? root;
        var markdown =
            docNode["md_content"]?.GetValue<string>()
            ?? docNode["markdown"]?.GetValue<string>()
            ?? string.Empty;

        var jsonContent = docNode["json_content"] as JsonObject;
        var items = new List<DoclingItem>();

        if (jsonContent?["texts"] is JsonArray texts)
        {
            foreach (var node in texts)
            {
                if (node is JsonObject obj && MapTextItem(obj) is { } item) items.Add(item);
            }
        }
        else
        {
            var fallback = jsonContent?["main_text"] as JsonArray
                ?? jsonContent?["body"] as JsonArray
                ?? jsonContent?["items"] as JsonArray
                ?? [];
            foreach (var node in fallback)
            {
                if (node is JsonObject obj && MapTextItem(obj) is { } item) items.Add(item);
            }
        }

        return new DoclingDocument(markdown, items);
    }


    /// <summary>
    /// Stream-based equivalent of <see cref="ParseResponse(string)"/> for
    /// large documents. Same semantics; never materialises the full JSON
    /// payload as a single string.
    /// </summary>
    private static async Task<DoclingDocument> ParseResponseStreamAsync(Stream stream, CancellationToken ct)
    {
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var docElement = root.TryGetProperty("document", out var d) ? d : root;
        var markdown =
            (docElement.TryGetProperty("md_content", out var mc) && mc.ValueKind == JsonValueKind.String)
                ? mc.GetString() ?? string.Empty
                : (docElement.TryGetProperty("markdown", out var mk) && mk.ValueKind == JsonValueKind.String)
                    ? mk.GetString() ?? string.Empty
                    : string.Empty;

        var items = new List<DoclingItem>();

        if (docElement.TryGetProperty("json_content", out var jc) && jc.ValueKind == JsonValueKind.Object)
        {
            if (jc.TryGetProperty("texts", out var texts) && texts.ValueKind == JsonValueKind.Array)
                AppendTextItems(texts, items);
            else if (jc.TryGetProperty("main_text", out var mt) && mt.ValueKind == JsonValueKind.Array)
                AppendTextItems(mt, items);
            else if (jc.TryGetProperty("body", out var bd) && bd.ValueKind == JsonValueKind.Array)
                AppendTextItems(bd, items);
            else if (jc.TryGetProperty("items", out var its) && its.ValueKind == JsonValueKind.Array)
                AppendTextItems(its, items);
        }

        return new DoclingDocument(markdown, items);
    }

    private static void AppendTextItems(JsonElement array, List<DoclingItem> sink)
    {
        foreach (var node in array.EnumerateArray())
        {
            if (node.ValueKind != JsonValueKind.Object) continue;

            var text = node.TryGetProperty("text", out var te) && te.ValueKind == JsonValueKind.String
                ? te.GetString() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(text)) continue;

            var type = node.TryGetProperty("label", out var lb) && lb.ValueKind == JsonValueKind.String
                ? lb.GetString() ?? "text"
                : node.TryGetProperty("type", out var ty) && ty.ValueKind == JsonValueKind.String
                    ? ty.GetString() ?? "text"
                    : "text";

            var page = 1;
            if (node.TryGetProperty("prov", out var prov) && prov.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in prov.EnumerateArray())
                {
                    if (p.ValueKind != JsonValueKind.Object) continue;
                    if (p.TryGetProperty("page_no", out var pn) && pn.ValueKind == JsonValueKind.Number) { page = pn.GetInt32(); break; }
                    if (p.TryGetProperty("page",    out var p2) && p2.ValueKind == JsonValueKind.Number) { page = p2.GetInt32(); break; }
                }
            }
            else if (node.TryGetProperty("page_no", out var pno) && pno.ValueKind == JsonValueKind.Number) page = pno.GetInt32();
            else if (node.TryGetProperty("page",    out var p3)  && p3.ValueKind == JsonValueKind.Number) page = p3.GetInt32();

            int? level = null;
            if (node.TryGetProperty("level", out var lv) && lv.ValueKind == JsonValueKind.Number)
                level = lv.GetInt32();

            sink.Add(new DoclingItem(type, text, page, level));
        }
    }

    private static DoclingItem? MapTextItem(JsonObject obj)
    {
        var text = obj["text"]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return null;

        var type = obj["label"]?.GetValue<string>()
            ?? obj["type"]?.GetValue<string>()
            ?? "text";

        var page = 1;
        if (obj["prov"] is JsonArray prov && prov.Count > 0 && prov[0] is JsonObject p0)
        {
            if (p0["page_no"] is JsonValue pn && pn.TryGetValue<int>(out var pi)) page = pi;
            else if (p0["page"] is JsonValue p2 && p2.TryGetValue<int>(out var p2i)) page = p2i;
        }
        else if (obj["page_no"] is JsonValue pno && pno.TryGetValue<int>(out var pi2)) page = pi2;
        else if (obj["page"]    is JsonValue p3  && p3.TryGetValue<int>(out var p3i))  page = p3i;

        int? level = null;
        if (obj["level"] is JsonValue lv && lv.TryGetValue<int>(out var lvi)) level = lvi;

        return new DoclingItem(type, text, page, level);
    }

    public void Dispose() => _http.Dispose();

    [LoggerMessage(Level = LogLevel.Information, Message = "Docling conversion starting for {FileName}")]
    private static partial void LogConvertStart(ILogger logger, string fileName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Docling task {TaskId} submitted for {FileName}")]
    private static partial void LogTaskSubmitted(ILogger logger, string fileName, string taskId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Docling task {TaskId} ({FileName}) status={Status} (poll #{Iteration})")]
    private static partial void LogPoll(ILogger logger, string fileName, string taskId, string status, int iteration);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Docling task {TaskId} reported unknown status '{Status}'; continuing to poll")]
    private static partial void LogUnknownStatus(ILogger logger, string taskId, string status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Docling conversion done for {FileName}: {ItemCount} items")]
    private static partial void LogConvertDone(ILogger logger, string fileName, int itemCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "docling-serve not yet healthy, waiting {Seconds}s before retry")]
    private static partial void LogWaitingForHealthy(ILogger logger, int seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Transient HTTP error polling docling task {TaskId} (attempt {Attempt}): {Message}")]
    private static partial void LogPollTransient(ILogger logger, string taskId, int attempt, string message);
}
