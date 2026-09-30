using System.Text.Json;
using RailTycoon.Sim.Economy;

namespace RailTycoon.Sim;

public static class ScenarioLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ScenarioDef Load(string path)
    {
        string json = File.ReadAllText(path);
        return Parse(json, path);
    }

    public static ScenarioDef Parse(string json, string origin = "<inline>")
    {
        try
        {
            return JsonSerializer.Deserialize<ScenarioDef>(json, Options)
                ?? throw new InvalidDataException($"Scénario vide : {origin}");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Scénario illisible ({origin}) : {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Les blocs de configuration que chaque module lit dans un scénario. Un bloc
    /// absent n'est pas neutre, il est <em>silencieusement inerte</em> : le module
    /// tourne sur ses défauts et personne ne le voit. Passer ironpeak au solveur
    /// anticipant ne changeait rien, au bit près, pour cette seule raison.
    /// </summary>
    public static IReadOnlyList<string> ModuleBlocks { get; } = ["anticipating", "network", "finance", "events", "cycle", "objectives"];

    /// <summary>
    /// Blocs de module ni déclarés, ni écartés explicitement. Un scénario peut
    /// renoncer à un bloc, mais il doit le dire : une clé de commentaire
    /// <c>"//&lt;bloc&gt;"</c> qui explique pourquoi suffit. Liste vide : le
    /// scénario a pris position sur chaque module.
    /// </summary>
    public static IReadOnlyList<string> UndeclaredModuleBlocks(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in doc.RootElement.EnumerateObject())
            keys.Add(property.Name);

        return ModuleBlocks
            .Where(block => !keys.Contains(block) && !keys.Contains("//" + block))
            .ToList();
    }
}
