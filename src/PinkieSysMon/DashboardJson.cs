using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CanonicalDashboard = PinkieSysMon.DashboardModel;

namespace PinkieSysMon;

internal static class DashboardJson
{
    private static readonly JsonSerializerOptions CompactOptions = CreateOptions(writeIndented: false);
    private static readonly JsonSerializerOptions IndentedOptions = CreateOptions(writeIndented: true);

    public static CanonicalDashboard.DashboardDefinition ParseCurrent(string json, string path)
    {
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) as JsonObject ?? throw new InvalidDataException("Dashboard definition is empty.");

        return DeserializeCurrent(root, path);
    }

    public static CanonicalDashboard.DashboardDefinition DeserializeCurrent(JsonObject root, string path)
    {
        ArgumentNullException.ThrowIfNull(root);
        EnsureCurrentSchema(root);

        CanonicalDashboard.DashboardDefinition definition;
        try
        {
            definition = root.Deserialize<CanonicalDashboard.DashboardDefinition>(CompactOptions)
                ?? throw new InvalidDataException("Dashboard definition is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Dashboard schemaVersion {DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion} is invalid.",
                ex);
        }

        definition.BaseDirectory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new InvalidDataException("Dashboard path has no parent directory.");
        definition.Validate();
        return definition;
    }

    public static string Serialize(CanonicalDashboard.DashboardDefinition definition, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.SchemaVersion != DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion)
        {
            throw new InvalidDataException(
                $"Canonical dashboard serialization requires schemaVersion {DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion}.");
        }

        definition.Validate();
        return JsonSerializer.Serialize(definition, indented ? IndentedOptions : CompactOptions);
    }

    // Editor undo/redo snapshots may legitimately contain a transient incomplete source selection
    // (for example immediately after file <-> icon clears Source). They still use the one canonical
    // schema/options owner, but persistence remains strict through Serialize().
    internal static string SerializeEditorSnapshot(CanonicalDashboard.DashboardDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.SchemaVersion != DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion)
            throw new InvalidDataException("Editor snapshots require the current canonical dashboard schema.");
        return JsonSerializer.Serialize(definition, CompactOptions);
    }

    internal static CanonicalDashboard.DashboardDefinition ParseEditorSnapshot(string json, string path)
    {
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) as JsonObject ?? throw new InvalidDataException("Dashboard snapshot is empty.");
        EnsureCurrentSchema(root);

        CanonicalDashboard.DashboardDefinition definition;
        try
        {
            definition = root.Deserialize<CanonicalDashboard.DashboardDefinition>(CompactOptions)
                ?? throw new InvalidDataException("Dashboard snapshot is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Dashboard snapshot is invalid.", ex);
        }

        definition.BaseDirectory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new InvalidDataException("Dashboard path has no parent directory.");
        return definition;
    }

    internal static JsonObject SerializeToObjectUnchecked(CanonicalDashboard.DashboardDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var node = JsonSerializer.SerializeToNode(definition, CompactOptions) as JsonObject;
        return node ?? throw new InvalidDataException("Canonical dashboard could not be serialized.");
    }

    internal static CanonicalDashboard.WidgetDefinition CloneWidget(CanonicalDashboard.WidgetDefinition widget)
    {
        ArgumentNullException.ThrowIfNull(widget);
        var json = JsonSerializer.Serialize<CanonicalDashboard.WidgetDefinition>(widget, CompactOptions);
        return JsonSerializer.Deserialize<CanonicalDashboard.WidgetDefinition>(json, CompactOptions)
            ?? throw new InvalidDataException("Canonical widget could not be cloned.");
    }

    private static void EnsureCurrentSchema(JsonObject root)
    {
        if (root["schemaVersion"] is not JsonValue schemaNode ||
            !schemaNode.TryGetValue<int>(out var schemaVersion))
        {
            throw new InvalidDataException("Dashboard schemaVersion is missing or invalid.");
        }

        if (schemaVersion != DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion)
        {
            throw new InvalidDataException(
                $"Canonical dashboard reader requires schemaVersion {DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion}, got {schemaVersion}.");
        }
    }

    private static JsonSerializerOptions CreateOptions(bool writeIndented) => new()
    {
        WriteIndented = writeIndented,
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
}
