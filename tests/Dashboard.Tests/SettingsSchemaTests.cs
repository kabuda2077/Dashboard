using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dashboard.Tests;

public sealed class SettingsSchemaTests
{
    [Theory]
    [InlineData("schemaVersion")]
    [InlineData("activeCoreKind")]
    [InlineData("profiles")]
    [InlineData("desktopOptions")]
    [InlineData("setupCompleted")]
    [InlineData("preferencesRevision")]
    [InlineData("dashboardPreferences")]
    [InlineData("profiles.mihomo")]
    [InlineData("profiles.singBox.protectedSecret")]
    [InlineData("profiles.mihomo.revision")]
    [InlineData("desktopOptions.minimizeToTray")]
    public void MissingCurrentFormatFieldsAreRejectedWithoutRewritingTheFile(string path)
    {
        var root = Path.Combine(Path.GetTempPath(), "Dashboard.Schema", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var file = Path.Combine(root, "settings.json");
        try
        {
            var document = JsonNode.Parse(JsonSerializer.Serialize(AppSettings.CreateDefault(root), AppSettings.JsonOptions))!;
            var parts = path.Split('.'); var parent = document;
            foreach (var part in parts[..^1]) parent = parent[part]!;
            parent.AsObject().Remove(parts[^1]);
            var original = document.ToJsonString(); File.WriteAllText(file, original);
            Assert.Throws<AppSettingsLoadException>(() => new SettingsStore(root));
            Assert.Equal(original, File.ReadAllText(file));
        }
        finally { Directory.Delete(root, true); }
    }
}
