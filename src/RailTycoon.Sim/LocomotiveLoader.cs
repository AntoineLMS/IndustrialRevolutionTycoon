using System.Text.Json;
using RailTycoon.Sim.Economy;

namespace RailTycoon.Sim;

/// <summary>
/// Chargement du catalogue de locomotives (data/locomotives.json). Séparé de
/// <see cref="ScenarioLoader"/> parce que le catalogue n'est pas un scénario :
/// c'est une référence commune à toutes les cartes, que
/// <see cref="ScenarioLoader.Load"/> charge pour un scénario qui active le module
/// <c>vehicles</c> (clé <c>vehicles.catalog</c>).
/// </summary>
public static class LocomotiveLoader
{
    /// <summary>Les carburants que le catalogue connaît. Le scénario dit à quelle marchandise chacun correspond.</summary>
    public static IReadOnlyList<string> FuelTypes { get; } = ["wood", "coal", "oil"];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static LocomotiveCatalogDef Load(string path) => Parse(File.ReadAllText(path), path);

    public static LocomotiveCatalogDef Parse(string json, string origin = "<inline>")
    {
        LocomotiveCatalogDef catalog;
        try
        {
            catalog = JsonSerializer.Deserialize<LocomotiveCatalogDef>(json, Options)
                ?? throw new InvalidDataException($"Catalogue de locomotives vide : {origin}");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Catalogue de locomotives illisible ({origin}) : {ex.Message}", ex);
        }

        Validate(catalog, origin);
        return catalog;
    }

    /// <summary>
    /// Même discipline que ScenarioLoader/Simulation.Validate : on échoue fort et
    /// tôt sur une donnée incohérente plutôt que de laisser une faute de frappe
    /// silencieuse dans le catalogue.
    /// </summary>
    private static void Validate(LocomotiveCatalogDef catalog, string origin)
    {
        if (catalog.Locomotives.Count == 0)
            throw new InvalidDataException($"Le catalogue de locomotives ne déclare aucune machine ({origin}).");

        var ids = new HashSet<string>();
        foreach (var loco in catalog.Locomotives)
        {
            if (string.IsNullOrWhiteSpace(loco.Id))
                throw new InvalidDataException($"Une locomotive n'a pas d'identifiant ({origin}).");
            if (!ids.Add(loco.Id))
                throw new InvalidDataException($"Locomotive en double : '{loco.Id}' ({origin}).");
            if (string.IsNullOrWhiteSpace(loco.Name))
                throw new InvalidDataException($"La locomotive '{loco.Id}' n'a pas de nom ({origin}).");
            if (loco.Year < 1750 || loco.Year > 2100)
                throw new InvalidDataException($"La locomotive '{loco.Id}' a une année invraisemblable : {loco.Year} ({origin}).");
            if (loco.TractiveEffortKn <= 0)
                throw new InvalidDataException($"La locomotive '{loco.Id}' a un effort de traction nul ou négatif ({origin}).");
            if (loco.TopSpeedKmh <= 0)
                throw new InvalidDataException($"La locomotive '{loco.Id}' a une vitesse nulle ou négative ({origin}).");
            if (loco.PowerKw <= 0)
                throw new InvalidDataException($"La locomotive '{loco.Id}' a une puissance nulle ou négative ({origin}).");
            if (loco.MassTonnes <= 0)
                throw new InvalidDataException($"La locomotive '{loco.Id}' a une masse nulle ou négative ({origin}).");
            if (loco.FuelKgPer1000TonneKm <= 0)
                throw new InvalidDataException($"La locomotive '{loco.Id}' a une consommation nulle ou négative ({origin}).");
            if (loco.PurchaseCost <= 0)
                throw new InvalidDataException($"La locomotive '{loco.Id}' a un prix d'achat nul ou négatif ({origin}).");
            if (loco.MaintenancePerTick < 0)
                throw new InvalidDataException($"La locomotive '{loco.Id}' a un entretien négatif ({origin}).");
            if (!FuelTypes.Contains(loco.FuelType))
                throw new InvalidDataException(
                    $"La locomotive '{loco.Id}' a un carburant inconnu : '{loco.FuelType}' " +
                    $"(attendu : {string.Join(", ", FuelTypes)}) ({origin}).");
        }
    }
}
