namespace RailTycoon.Sim.Economy;

// Catalogue de matériel roulant historique, chargé depuis data/locomotives.json.
// Ce fichier appartient au module `content` (voir docs/CONTRACTS.md) : il ne
// modifie pas Definitions.cs, qui reste la propriété du module `economy`.
//
// Toute valeur ici est soit une caractéristique physique documentée (année,
// effort de traction, vitesse, masse), soit une valeur estimée par une formule
// explicite (puissance quand elle n'est pas publiée, consommation, prix,
// entretien). Les sources et la distinction documenté/estimé, locomotive par
// locomotive, sont dans docs/SOURCES.md — ce fichier ne contient que le schéma.

/// <summary>
/// Une locomotive historique du catalogue.
/// <para>
/// Sans module <c>vehicles</c> actif, elle reste purement descriptive : un train
/// qui la référence (<see cref="TrainDef.Locomotive"/>) roule sur son
/// <c>speedKmPerTick</c> et son <c>costPerKm</c>. Avec le module actif
/// (<c>vehicles.enabled</c>), le train tire d'elle sa vitesse, sa puissance, son
/// adhérence, sa masse, son carburant, sa consommation, son prix et son entretien
/// (voir <c>Transport/Vehicles.cs</c>).
/// </para>
/// </summary>
public sealed class LocomotiveDef
{
    public string Id { get; set; } = "";

    /// <summary>Nom réel de la locomotive ou de sa classe.</summary>
    public string Name { get; set; } = "";

    /// <summary>Année de mise en service.</summary>
    public int Year { get; set; }

    /// <summary>
    /// Effort de traction maximal (au démarrage), en kilonewtons. C'est la limite
    /// d'adhérence : en dessous de la vitesse où la puissance prend le relais, la
    /// locomotive ne tire pas plus fort que ceci, quelle que soit sa chaudière.
    /// </summary>
    public double TractiveEffortKn { get; set; }

    /// <summary>Vitesse maximale, en km/h (record ou vitesse de service selon la source, voir SOURCES.md).</summary>
    public double TopSpeedKmh { get; set; }

    /// <summary>
    /// Puissance soutenue à la jante, en kilowatts. Au-delà de la vitesse
    /// <c>puissance ÷ effort de traction</c>, l'effort disponible vaut
    /// <c>puissance ÷ vitesse</c> : c'est elle qui fait la tenue en rampe d'un train
    /// chargé.
    /// </summary>
    public double PowerKw { get; set; }

    /// <summary>Masse en ordre de marche, locomotive et tender, en tonnes.</summary>
    public double MassTonnes { get; set; }

    /// <summary>
    /// Ce qu'elle brûle : <c>wood</c>, <c>coal</c> (houille, anthracite ou coke) ou
    /// <c>oil</c> (fioul, gazole). Le scénario dit quelle marchandise de sa carte
    /// correspond à chaque carburant (<c>vehicles.fuels</c>).
    /// </summary>
    public string FuelType { get; set; } = "coal";

    /// <summary>
    /// Consommation spécifique, en kilogrammes de carburant pour mille
    /// tonnes-kilomètres brutes (locomotive, wagons et chargement) sur le plat —
    /// l'unité des statistiques ferroviaires américaines (livres pour mille
    /// tonnes-milles brutes), convertie. Le relief la multiplie par son facteur,
    /// comme il multiplie le coût du modèle <c>mass</c>.
    /// </summary>
    public double FuelKgPer1000TonneKm { get; set; }

    /// <summary>Prix d'achat, en unités monétaires du jeu (estimation, voir SOURCES.md).</summary>
    public double PurchaseCost { get; set; }

    /// <summary>
    /// Entretien, en unités monétaires du jeu par tick : dû qu'elle roule ou non,
    /// comme l'entretien des voies (estimation, voir SOURCES.md).
    /// </summary>
    public double MaintenancePerTick { get; set; }
}

/// <summary>Racine du fichier data/locomotives.json.</summary>
public sealed class LocomotiveCatalogDef
{
    public List<LocomotiveDef> Locomotives { get; set; } = new();
}
