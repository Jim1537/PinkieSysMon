using System.Text.Json;
using System.Text.Json.Serialization;

namespace PinkieSysMon.Editor;

internal sealed class EditorGroupDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ParentGroupId { get; set; }
}

internal sealed class DashboardEditorMetadata
{
    // Legacy v1 format: widget ID -> flat group name. Read only for one-time migration.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Groups { get; set; }

    public List<EditorGroupDefinition> GroupDefinitions { get; set; } = [];
    public Dictionary<string, string> WidgetGroupIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static DashboardEditorMetadata Load(
        string path,
        out Exception? loadError,
        out string? invalidBackupPath)
    {
        loadError = null;
        invalidBackupPath = null;

        if (!File.Exists(path))
            return new DashboardEditorMetadata();

        try
        {
            var value = JsonSerializer.Deserialize<DashboardEditorMetadata>(File.ReadAllText(path), JsonOptions())
                ?? new DashboardEditorMetadata();
            value.Normalize();
            return value;
        }
        catch (Exception ex)
        {
            loadError = ex;
            invalidBackupPath = TryBackupInvalidMetadata(path);
            return new DashboardEditorMetadata();
        }
    }

    private static string? TryBackupInvalidMetadata(string path)
    {
        try
        {
            var backupPath = $"{path}.invalid-{DateTime.Now:yyyyMMdd-HHmmssfff}.bak";
            File.Copy(path, backupPath, overwrite: false);
            return backupPath;
        }
        catch
        {
            return null;
        }
    }

    public string ToCompactJson() => JsonSerializer.Serialize(this);

    public static DashboardEditorMetadata FromJson(string json)
    {
        var value = JsonSerializer.Deserialize<DashboardEditorMetadata>(json, JsonOptions())
            ?? new DashboardEditorMetadata();
        value.Normalize();
        return value;
    }

    public void Save(string path)
    {
        Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, true);
    }

    public EditorGroupDefinition? GetGroup(string? groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId))
            return null;
        return GroupDefinitions.FirstOrDefault(group =>
            string.Equals(group.Id, groupId, StringComparison.OrdinalIgnoreCase));
    }

    public string? GetWidgetGroupId(string? widgetId)
    {
        if (string.IsNullOrWhiteSpace(widgetId))
            return null;
        return WidgetGroupIds.TryGetValue(widgetId, out var groupId) && GetGroup(groupId) is not null
            ? groupId
            : null;
    }

    public void SetWidgetGroup(string? widgetId, string? groupId)
    {
        if (string.IsNullOrWhiteSpace(widgetId))
            return;

        if (string.IsNullOrWhiteSpace(groupId))
        {
            WidgetGroupIds.Remove(widgetId);
            return;
        }

        if (GetGroup(groupId) is null)
            throw new InvalidOperationException($"Unknown editor group '{groupId}'.");
        WidgetGroupIds[widgetId] = groupId;
    }

    public void RemoveWidget(string? widgetId)
    {
        if (!string.IsNullOrWhiteSpace(widgetId))
            WidgetGroupIds.Remove(widgetId);
    }

    public bool NameExistsAmongSiblings(string name, string? parentGroupId, string? exceptGroupId = null) =>
        GroupDefinitions.Any(group =>
            !SameId(group.Id, exceptGroupId) &&
            SameId(group.ParentGroupId, parentGroupId) &&
            string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase));

    public EditorGroupDefinition CreateGroup(string name, string? parentGroupId)
    {
        name = name.Trim();
        if (name.Length == 0)
            throw new ArgumentException("Group name cannot be empty.", nameof(name));
        if (!string.IsNullOrWhiteSpace(parentGroupId) && GetGroup(parentGroupId) is null)
            throw new InvalidOperationException($"Unknown parent group '{parentGroupId}'.");
        if (NameExistsAmongSiblings(name, parentGroupId))
            throw new InvalidOperationException($"A group named '{name}' already exists at this level.");

        var group = new EditorGroupDefinition
        {
            Id = CreateUniqueGroupId(),
            Name = name,
            ParentGroupId = string.IsNullOrWhiteSpace(parentGroupId) ? null : parentGroupId
        };
        GroupDefinitions.Add(group);
        return group;
    }

    public void DeleteGroupPromoteContents(string groupId)
    {
        var group = GetGroup(groupId);
        if (group is null)
            return;

        var parentId = group.ParentGroupId;
        foreach (var child in GroupDefinitions.Where(candidate => SameId(candidate.ParentGroupId, groupId)))
            child.ParentGroupId = parentId;

        foreach (var widgetId in WidgetGroupIds
                     .Where(pair => SameId(pair.Value, groupId))
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            SetWidgetGroup(widgetId, parentId);
        }

        GroupDefinitions.Remove(group);
    }

    public bool IsDescendantGroup(string candidateGroupId, string ancestorGroupId)
    {
        var current = GetGroup(candidateGroupId);
        var guard = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (current is not null && !string.IsNullOrWhiteSpace(current.ParentGroupId))
        {
            if (!guard.Add(current.Id))
                return false;
            if (SameId(current.ParentGroupId, ancestorGroupId))
                return true;
            current = GetGroup(current.ParentGroupId);
        }
        return false;
    }

    public int GetDepth(string groupId)
    {
        var depth = 0;
        var current = GetGroup(groupId);
        var guard = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (current is not null && !string.IsNullOrWhiteSpace(current.ParentGroupId) && guard.Add(current.Id))
        {
            depth++;
            current = GetGroup(current.ParentGroupId);
        }
        return depth;
    }

    private void Normalize()
    {
        GroupDefinitions ??= [];
        WidgetGroupIds = new Dictionary<string, string>(WidgetGroupIds ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);

        MigrateLegacyGroups();

        var normalizedGroups = new List<EditorGroupDefinition>();
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in GroupDefinitions)
        {
            if (group is null)
                continue;
            group.Id = group.Id?.Trim() ?? string.Empty;
            group.Name = group.Name?.Trim() ?? string.Empty;
            group.ParentGroupId = string.IsNullOrWhiteSpace(group.ParentGroupId) ? null : group.ParentGroupId.Trim();
            if (group.Id.Length == 0 || group.Name.Length == 0 || !usedIds.Add(group.Id))
                continue;
            normalizedGroups.Add(group);
        }
        GroupDefinitions = normalizedGroups;

        // Break invalid/orphaned/cyclic parents at the root rather than losing the group.
        foreach (var group in GroupDefinitions)
        {
            if (group.ParentGroupId is null)
                continue;
            if (GetGroup(group.ParentGroupId) is null || SameId(group.ParentGroupId, group.Id) || WouldCreateCycle(group.Id, group.ParentGroupId))
                group.ParentGroupId = null;
        }

        foreach (var widgetId in WidgetGroupIds.Keys.ToArray())
        {
            var groupId = WidgetGroupIds[widgetId];
            if (string.IsNullOrWhiteSpace(widgetId) || GetGroup(groupId) is null)
                WidgetGroupIds.Remove(widgetId);
        }
    }

    private void MigrateLegacyGroups()
    {
        if (Groups is null || Groups.Count == 0)
        {
            Groups = null;
            return;
        }

        var byName = new Dictionary<string, EditorGroupDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in Groups)
        {
            var name = pair.Value?.Trim();
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(name))
                continue;

            if (!byName.TryGetValue(name, out var group))
            {
                group = GroupDefinitions.FirstOrDefault(candidate =>
                    candidate.ParentGroupId is null && string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
                if (group is null)
                {
                    group = new EditorGroupDefinition
                    {
                        Id = CreateUniqueGroupId(),
                        Name = name,
                        ParentGroupId = null
                    };
                    GroupDefinitions.Add(group);
                }
                byName[name] = group;
            }

            WidgetGroupIds[pair.Key] = group.Id;
        }

        Groups = null;
    }

    private bool WouldCreateCycle(string groupId, string parentGroupId)
    {
        var current = GetGroup(parentGroupId);
        var guard = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (current is not null && guard.Add(current.Id))
        {
            if (SameId(current.Id, groupId))
                return true;
            current = string.IsNullOrWhiteSpace(current.ParentGroupId) ? null : GetGroup(current.ParentGroupId);
        }
        return false;
    }

    private string CreateUniqueGroupId()
    {
        var used = new HashSet<string>(GroupDefinitions.Select(group => group.Id), StringComparer.OrdinalIgnoreCase);
        for (var i = 1; ; i++)
        {
            var candidate = $"group-{i}";
            if (!used.Contains(candidate))
                return candidate;
        }
    }

    private static bool SameId(string? left, string? right) =>
        string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}
