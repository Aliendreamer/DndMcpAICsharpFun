using System.Reflection;

using DndMcpAICsharpFun.Domain.Entities.Fields;

using NJsonSchema;
using NJsonSchema.Generation;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: SchemaGenerator <output-directory>");
    return 1;
}

var outputDir = args[0];
Directory.CreateDirectory(outputDir);

var fieldsAssembly = typeof(ClassFields).Assembly;
var fieldsTypes = fieldsAssembly.GetTypes()
    .Where(t => t.Namespace == "DndMcpAICsharpFun.Domain.Entities.Fields"
                && t.IsClass && !t.IsAbstract
                && t.Name.EndsWith("Fields", StringComparison.Ordinal))
    .OrderBy(t => t.Name)
    .ToList();

var serializerOptions = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
serializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

var settings = new SystemTextJsonSchemaGeneratorSettings
{
    SerializerOptions = serializerOptions,
    GenerateAbstractProperties = false,
    AlwaysAllowAdditionalObjectProperties = false,
};

var generator = new JsonSchemaGenerator(settings);

foreach (var t in fieldsTypes)
{
    var schema = generator.Generate(t);
    var path = Path.Combine(outputDir, $"{t.Name}.schema.json");

    if (File.Exists(path) && File.ReadAllText(path).Contains("// SCHEMA-OVERRIDE", StringComparison.Ordinal))
    {
        Console.WriteLine($"SKIP (override): {t.Name}");
        continue;
    }

    var json = schema.ToJson();
    File.WriteAllText(path, json);
    Console.WriteLine($"WROTE: {t.Name}");
}

return 0;