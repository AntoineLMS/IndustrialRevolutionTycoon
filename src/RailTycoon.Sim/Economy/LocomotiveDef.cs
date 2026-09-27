namespace RailTycoon.Sim.Economy;

// Catalogue de matériel roulant historique, chargé depuis data/locomotives.json.
// Ce fichier appartient au module `content` (voir docs/CONTRACTS.md) : il ne
// modifie pas Definitions.cs, qui reste la propriété du module `economy`.
//
// Toute valeur ici est soit une caractéristique physique documentée (année,
// effort de traction, vitesse), soit une valeur économique de jeu estimée pour
// l'équilibrage (coût d'achat, coût d'entretien, consommation). Les sources et
// la distinction documenté/estimé, locomotive par locomotive, sont dans
// docs/SOURCES.md — ce fichier ne contient que le schéma.

/// <summary>
/// Une locomotive historique du catalogue. Purement descriptif pour l'instant :
/// la simulation ne lit aucun de ces champs directement, elle continue de lire
/// <see cref="TrainDef.SpeedKmPerTick"/> et <see cref="TrainDef.CostPerKm"/>.
/// <see cref="TrainDef.Locomotive"/> permet de rattacher un train à une entrée
/// de ce catalogue à titre informatif, en attendant qu'un futur module (finance
/// ou contenu) s'en serve pour calculer ces champs automatiquement.
/// </summary>
public sealed class LocomotiveDef
{
    public string Id { get; set; } = "";

    /// <summary>Nom réel de la locomotive ou de sa classe.</summary>
    public string Name { get; set; } = "";

    /// <summary>Année de mise en service.</summary>
    public int Year { get; set; }

    /// <summary>Effort de traction maximal (au démarrage), en kilonewtons.</summary>
    public double TractiveEffortKn { get; set; }

    /// <summary>Vitesse maximale documentée, en km/h (record ou vitesse de service selon la source, voir SOURCES.md).</summary>
    public double TopSpeedKmh { get; set; }

    /// <summary>"coal" (bois/charbon/coke, brûlé dans une chaudière) ou "diesel".</summary>
    public string FuelType { get; set; } = "coal";

    /// <summary>Consommation de combustible en kilogrammes par kilomètre parcouru.</summary>
    public double FuelConsumptionKgPerKm { get; set; }

    /// <summary>Coût d'achat, en unités monétaires du jeu (estimation d'équilibrage, voir SOURCES.md).</summary>
    public double PurchaseCost { get; set; }

    /// <summary>Coût d'entretien par kilomètre parcouru, en unités monétaires du jeu (estimation d'équilibrage).</summary>
    public double MaintenanceCostPerKm { get; set; }
}

/// <summary>Racine du fichier data/locomotives.json.</summary>
public sealed class LocomotiveCatalogDef
{
    public List<LocomotiveDef> Locomotives { get; set; } = new();
}
