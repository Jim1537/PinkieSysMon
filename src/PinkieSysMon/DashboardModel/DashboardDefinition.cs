using System.Text.Json.Serialization;

namespace PinkieSysMon.DashboardModel;

internal sealed class DashboardDefinition
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = global::PinkieSysMon.DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion;

    [JsonPropertyName("canvas")]
    public CanvasDefinition Canvas { get; set; } = new();

    [JsonPropertyName("widgets")]
    public List<WidgetDefinition> Widgets { get; set; } = [];

    [JsonIgnore]
    public string BaseDirectory { get; set; } = string.Empty;

    public static DashboardDefinition Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Dashboard definition not found.", path);

        return Parse(File.ReadAllText(path), path);
    }

    public static DashboardDefinition Parse(string json, string path)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(json, documentOptions: new System.Text.Json.JsonDocumentOptions
        {
            CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) as System.Text.Json.Nodes.JsonObject
            ?? throw new InvalidDataException("Dashboard definition is empty.");

        global::PinkieSysMon.DashboardSchemaMigration.UpgradeToPropertyModelNormalization(root);
        return global::PinkieSysMon.DashboardJson.DeserializeCurrent(root, path);
    }

    internal void Validate()
    {
        var requiredSchemaVersion = global::PinkieSysMon.DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion;
        if (SchemaVersion != requiredSchemaVersion)
            throw new InvalidDataException($"Canonical property model requires dashboard schemaVersion {requiredSchemaVersion}, got {SchemaVersion}.");
        if (Canvas is null)
            throw new InvalidDataException("Dashboard canvas is required.");
        if (Widgets is null)
            throw new InvalidDataException("Dashboard widgets collection is required.");

        Canvas.Validate(BaseDirectory);

        var widgetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var widget in Widgets)
        {
            if (widget is null)
                throw new InvalidDataException("Dashboard widgets collection cannot contain null entries.");
            if (string.IsNullOrWhiteSpace(widget.Id))
                throw new InvalidDataException($"Dashboard widget of type '{widget.Type}' requires a non-empty Id.");
            if (!widgetIds.Add(widget.Id))
                throw new InvalidDataException($"Duplicate dashboard widget Id: '{widget.Id}'.");
            widget.Validate(BaseDirectory);
        }
    }
}
