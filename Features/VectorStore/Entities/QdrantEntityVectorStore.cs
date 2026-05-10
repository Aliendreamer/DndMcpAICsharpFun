using System.Diagnostics.CodeAnalysis;
using DndMcpAICsharpFun.Infrastructure;
using System.Security.Cryptography;
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace DndMcpAICsharpFun.Features.VectorStore.Entities;

[ExcludeFromCodeCoverage]
public sealed class QdrantEntityVectorStore(
    QdrantClient client,
    IOptions<QdrantOptions> options) : IEntityVectorStore
{
    private readonly string _collection = options.Value.EntitiesCollectionName;

    public async Task UpsertAsync(IList<EntityPoint> points, CancellationToken ct = default)
    {
        if (points.Count == 0) return;
        var qdrantPoints = points.Select(ToPoint).ToList();
        await client.UpsertAsync(_collection, qdrantPoints, cancellationToken: ct);
    }

    public async Task DeleteByFileHashAsync(string fileHash, CancellationToken ct = default)
    {
        var filter = MatchKeyword(EntityPayloadFields.FileHash, fileHash);
        await client.DeleteAsync(_collection, filter, cancellationToken: ct);
    }

    public async Task<EntityEnvelope?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var filter = MatchKeyword(EntityPayloadFields.Id, id);
        var results = await client.ScrollAsync(_collection, filter, limit: 1, payloadSelector: true, cancellationToken: ct);
        var first = results.Result.FirstOrDefault();
        return first is null ? null : ToEnvelope(first.Payload);
    }

    public async Task<IList<EntitySearchHit>> SearchAsync(float[] queryVector, EntityFilters filters, int topK, CancellationToken ct = default)
    {
        var filter = BuildFilter(filters);
        var results = await client.SearchAsync(
            _collection,
            queryVector,
            filter: filter,
            limit: (ulong)topK,
            payloadSelector: true,
            cancellationToken: ct);
        return results.Select(p => new EntitySearchHit(ToEnvelope(p.Payload), p.Score, p.Id.Uuid)).ToList();
    }

    public async Task<IReadOnlyDictionary<string, EntityEnvelope>> GetByIdsAsync(
        IReadOnlyList<string> entityIds, CancellationToken ct = default)
    {
        if (entityIds.Count == 0) return new Dictionary<string, EntityEnvelope>();

        var filter = new Filter();
        foreach (var id in entityIds)
            filter.Should.Add(KW(EntityPayloadFields.Id, id));

        const uint PageSize = 1000;
        var result = new Dictionary<string, EntityEnvelope>(entityIds.Count);
        PointId? offset = null;
        do
        {
            var page = await client.ScrollAsync(
                _collection, filter,
                offset: offset,
                limit: PageSize,
                payloadSelector: true,
                cancellationToken: ct);
            foreach (var p in page.Result.Where(p => p.Payload.ContainsKey(EntityPayloadFields.Id)))
            {
                var envelope = ToEnvelope(p.Payload);
                result[envelope.Id] = envelope;
            }
            offset = page.NextPageOffset;
        } while (offset is not null);

        return result;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetDataSourcesAsync(
        IReadOnlyList<string> entityIds, CancellationToken ct = default)
    {
        if (entityIds.Count == 0) return new Dictionary<string, string>();
        var filter = new Filter();
        foreach (var id in entityIds)
            filter.Should.Add(KW(EntityPayloadFields.Id, id));

        const uint PageSize = 1000;
        var sources = new Dictionary<string, string>();
        PointId? offset = null;
        do
        {
            var page = await client.ScrollAsync(
                _collection, filter,
                offset: offset,
                limit: PageSize,
                payloadSelector: true,
                cancellationToken: ct);
            foreach (var p in page.Result.Where(p => p.Payload.ContainsKey(EntityPayloadFields.Id)))
                sources[p.Payload[EntityPayloadFields.Id].StringValue] =
                    p.Payload.TryGetValue(EntityPayloadFields.DataSource, out var ds) ? ds.StringValue : "";
            offset = page.NextPageOffset;
        } while (offset is not null);

        return sources;
    }

    private static readonly Guid s_entityNs = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

    private static PointStruct ToPoint(EntityPoint p)
    {
        var payload = new Dictionary<string, Value>
        {
            [EntityPayloadFields.Id]            = p.Envelope.Id,
            [EntityPayloadFields.Type]          = p.Envelope.Type.ToString(),
            [EntityPayloadFields.Name]          = p.Envelope.Name,
            [EntityPayloadFields.SourceBook]    = p.Envelope.SourceBook,
            [EntityPayloadFields.Edition]       = p.Envelope.Edition,
            [EntityPayloadFields.CanonicalText] = p.Envelope.CanonicalText,
            [EntityPayloadFields.FirstBook]     = p.Envelope.FirstAppearedIn.Book,
            [EntityPayloadFields.FirstEdition]  = p.Envelope.FirstAppearedIn.Edition,
            [EntityPayloadFields.FileHash]      = p.FileHash,
            [EntityPayloadFields.DataSource]    = p.Envelope.DataSource,
            [EntityPayloadFields.Srd]           = p.Envelope.Srd ? "true" : "false",
            [EntityPayloadFields.Srd52]         = p.Envelope.Srd52 ? "true" : "false",
            [EntityPayloadFields.BasicRules2024]= p.Envelope.BasicRules2024 ? "true" : "false",
            [EntityPayloadFields.SettingTags]   = StringList(p.Envelope.SettingTags),
            [EntityPayloadFields.FieldsJson]    = p.Envelope.Fields.GetRawText(),
        };
        if (p.Envelope.Page is { } page) payload[EntityPayloadFields.Page] = page;
        if (p.Envelope.Keywords.Count > 0)
            payload[EntityPayloadFields.Keywords] = StringList(p.Envelope.Keywords);

        FlattenIndexedFields(p.Envelope, payload);

        var point = new PointStruct
        {
            Id = new PointId { Uuid = UuidV5.Create(s_entityNs, System.Text.Encoding.UTF8.GetBytes(p.Envelope.Id)).ToString() },
            Vectors = p.Vector,
        };
        foreach (var (k, v) in payload) point.Payload[k] = v;
        return point;
    }

    private static void FlattenIndexedFields(EntityEnvelope envelope, Dictionary<string, Value> payload)
    {
        if (envelope.Type == EntityType.Monster && envelope.Fields.TryGetProperty("cr", out var cr))
        {
            double? crNumeric = null;
            if (cr.ValueKind == JsonValueKind.String && TryParseCr(cr.GetString(), out var n1)) crNumeric = n1;
            else if (cr.ValueKind == JsonValueKind.Object && cr.TryGetProperty("cr", out var crInner)
                     && TryParseCr(crInner.GetString(), out var n2)) crNumeric = n2;
            if (crNumeric.HasValue) payload[EntityPayloadFields.CrNumeric] = crNumeric.Value;
        }

        if (envelope.Type == EntityType.Spell && envelope.Fields.TryGetProperty("level", out var lvl)
            && lvl.TryGetInt32(out var lvlInt))
            payload[EntityPayloadFields.SpellLevel] = lvlInt;

        if (envelope.Type == EntityType.Spell && envelope.Fields.TryGetProperty("damageInflict", out var dt)
            && dt.ValueKind == JsonValueKind.Array)
        {
            var types = dt.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
            if (types.Count > 0) payload[EntityPayloadFields.DamageType] = StringList(types);
        }

        if (envelope.Type == EntityType.Weapon && envelope.Fields.TryGetProperty("damage", out var dmg)
            && dmg.TryGetProperty("type", out var dmgType) && dmgType.ValueKind == JsonValueKind.String)
            payload[EntityPayloadFields.DamageType] = StringList(new[] { dmgType.GetString()! });
    }

    private static Value StringList(IEnumerable<string> values)
    {
        var list = new ListValue();
        foreach (var v in values) list.Values.Add(v);
        return new Value { ListValue = list };
    }

    private static EntityEnvelope ToEnvelope(Google.Protobuf.Collections.MapField<string, Value> p)
    {
        var fieldsJson = p.TryGetValue(EntityPayloadFields.FieldsJson, out var fv) ? fv.StringValue : "{}";
        var fields = JsonDocument.Parse(fieldsJson).RootElement.Clone();
        var envelope = new EntityEnvelope(
            Id: p[EntityPayloadFields.Id].StringValue,
            Type: Enum.Parse<EntityType>(p[EntityPayloadFields.Type].StringValue),
            Name: p[EntityPayloadFields.Name].StringValue,
            SourceBook: p[EntityPayloadFields.SourceBook].StringValue,
            Edition: p[EntityPayloadFields.Edition].StringValue,
            Page: p.TryGetValue(EntityPayloadFields.Page, out var pp) ? (int?)pp.IntegerValue : null,
            FirstAppearedIn: new FirstAppearance(
                p[EntityPayloadFields.FirstBook].StringValue,
                p[EntityPayloadFields.FirstEdition].StringValue),
            RevisedIn: Array.Empty<Revision>(),
            SettingTags: p.TryGetValue(EntityPayloadFields.SettingTags, out var st)
                ? st.ListValue.Values.Select(v => v.StringValue).ToList()
                : Array.Empty<string>(),
            CanonicalText: p[EntityPayloadFields.CanonicalText].StringValue,
            Fields: fields,
            DataSource: p.TryGetValue(EntityPayloadFields.DataSource, out var ds) ? ds.StringValue : "",
            Srd:            p.TryGetValue(EntityPayloadFields.Srd,            out var srdV)   && srdV.StringValue   == "true",
            Srd52:          p.TryGetValue(EntityPayloadFields.Srd52,          out var srd52V) && srd52V.StringValue  == "true",
            BasicRules2024: p.TryGetValue(EntityPayloadFields.BasicRules2024, out var brV)    && brV.StringValue     == "true",
            Keywords: p.TryGetValue(EntityPayloadFields.Keywords, out var kw)
                ? kw.ListValue.Values.Select(v => v.StringValue).ToList()
                : Array.Empty<string>());
        return envelope;
    }

    private static Filter MatchKeyword(string field, string value)
    {
        var filter = new Filter();
        filter.Must.Add(KW(field, value));
        return filter;
    }

    private static Filter? BuildFilter(EntityFilters f)
    {
        var must = new List<Condition>();
        if (f.Type is { } t)
            must.Add(KW(EntityPayloadFields.Type, t.ToString()));
        if (!string.IsNullOrEmpty(f.SourceBook)) must.Add(KW(EntityPayloadFields.SourceBook, f.SourceBook));
        if (!string.IsNullOrEmpty(f.Edition))    must.Add(KW(EntityPayloadFields.Edition, f.Edition));
        if (!string.IsNullOrEmpty(f.BookType))   must.Add(KW(EntityPayloadFields.BookType, f.BookType));
        if (!string.IsNullOrEmpty(f.SettingTag)) must.Add(KW(EntityPayloadFields.SettingTags, f.SettingTag));
        if (!string.IsNullOrEmpty(f.Keyword))    must.Add(KW(EntityPayloadFields.Keywords, f.Keyword));
        if (!string.IsNullOrEmpty(f.DamageType)) must.Add(KW(EntityPayloadFields.DamageType, f.DamageType));
        if (f.SpellLevel is { } sl)
            must.Add(new Condition { Field = new FieldCondition { Key = EntityPayloadFields.SpellLevel, Match = new Match { Integer = sl } } });
        if (f.CrNumericLte is not null || f.CrNumericGte is not null)
        {
            var range = new Qdrant.Client.Grpc.Range();
            if (f.CrNumericLte is { } v1) range.Lte = v1;
            if (f.CrNumericGte is { } v2) range.Gte = v2;
            must.Add(new Condition { Field = new FieldCondition { Key = EntityPayloadFields.CrNumeric, Range = range } });
        }
        if (f.Srd == true)            must.Add(KW(EntityPayloadFields.Srd, "true"));
        if (f.Srd52 == true)          must.Add(KW(EntityPayloadFields.Srd52, "true"));
        if (f.BasicRules2024 == true) must.Add(KW(EntityPayloadFields.BasicRules2024, "true"));
        if (must.Count == 0) return null;
        var filter = new Filter();
        foreach (var c in must) filter.Must.Add(c);
        return filter;
    }

    private static Condition KW(string field, string value) =>
        new() { Field = new FieldCondition { Key = field, Match = new Match { Keyword = value } } };

    private static bool TryParseCr(string? cr, out double value)
    {
        if (cr is null) { value = 0; return false; }
        if (cr == "1/8") { value = 0.125; return true; }
        if (cr == "1/4") { value = 0.25; return true; }
        if (cr == "1/2") { value = 0.5; return true; }
        if (double.TryParse(cr, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out value)) return true;
        value = 0; return false;
    }

}
