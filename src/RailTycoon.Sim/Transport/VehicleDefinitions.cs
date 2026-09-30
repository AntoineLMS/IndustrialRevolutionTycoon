using System.Text.Json.Serialization;
using RailTycoon.Sim.Economy;

namespace RailTycoon.Sim.Transport;

/// <summary>
/// Le bloc <c>vehicles</c> d'un scénario : des trains tirés par des locomotives du
/// catalogue, achetées, alimentées au prix du marché local et entretenues. Inactif
/// par défaut, et neutre au bit près tant qu'il l'est. Voir
/// <c>Transport/Vehicles.cs</c> pour les règles, docs/CONTRACTS.md (module
/// <c>vehicles</c>) pour le contrat.
/// <para>
/// Ce que le module fait d'un train dont <see cref="TrainDef.Locomotive"/> désigne
/// une machine du catalogue, et qui <b>remplace</b> ce que le train déclare :
/// </para>
/// <list type="bullet">
///   <item><c>speedKmPerTick</c> devient <c>topSpeedKmh × runningHoursPerTick</c>,
///   ralenti tronçon par tronçon par la puissance, l'adhérence et la masse du train
///   (<see cref="TrainDynamics"/>) ;</item>
///   <item><c>costPerKm</c> devient <see cref="OtherCostPerKm"/> : ce qui n'est ni
///   le carburant ni l'entretien de la locomotive ; le carburant se paie à part, en
///   gare, au prix local, et l'entretien par tick ;</item>
///   <item><c>haulage.massCost.locomotiveTonnes</c> devient la masse de la machine,
///   pour ce train.</item>
/// </list>
/// </summary>
public sealed class VehiclesDef
{
    /// <summary>Faux (défaut) : aucun train n'est acheté, aucun carburant payé, rien ne bouge.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Chemin du catalogue, relatif au fichier du scénario. Lu par
    /// <c>ScenarioLoader.Load</c>, qui remplit <see cref="Catalog"/>.
    /// </summary>
    public string CatalogPath { get; set; } = "locomotives.json";

    /// <summary>
    /// Le catalogue résolu. Rempli par <c>ScenarioLoader.Load</c> (ou par un test) ;
    /// jamais lu dans le JSON du scénario, pour qu'un scénario ne puisse pas
    /// redéfinir en douce une machine historique.
    /// </summary>
    [JsonIgnore]
    public LocomotiveCatalogDef? Catalog { get; set; }

    /// <summary>
    /// Quelle marchandise de la carte chaque carburant du catalogue désigne
    /// (<c>wood</c>, <c>coal</c>, <c>oil</c>). Une locomotive dont le carburant n'est
    /// pas déclaré ici, ou désigne une marchandise inconnue, est refusée au
    /// chargement : on ne fait pas le plein d'une marchandise qui n'existe pas.
    /// </summary>
    public Dictionary<string, string> Fuels { get; set; } = new();

    /// <summary>
    /// Coût kilométrique d'un train tiré par une locomotive du catalogue, hors
    /// carburant et hors entretien de la locomotive : équipe, usure des wagons,
    /// graissage, eau. Il <b>remplace</b> le <c>costPerKm</c> du train, et se
    /// répartit comme lui entre part fixe et part par chargement sous le modèle
    /// <c>mass</c>. C'est ce qui empêche de compter deux fois le carburant : le
    /// <c>costPerKm</c> d'un scénario sans véhicules le contenait déjà.
    /// </summary>
    public double OtherCostPerKm { get; set; } = 0.8;

    /// <summary>
    /// Heures de marche à la vitesse calculée par jour de jeu : une conversion
    /// d'unité entre le temps physique d'un trajet et le tick, qui absorbe arrêts,
    /// croisements et temps en gare. Elle se mesure (point fixe), elle ne se règle
    /// pas : voir docs/FINDINGS.md, « Les véhicules ».
    /// </summary>
    public double RunningHoursPerTick { get; set; } = 1.667;

    /// <summary>
    /// Résistance au roulement, sans dimension (newtons par newton de poids). 0,004
    /// vaut 8 livres par tonne longue, l'ordre de grandeur mesuré par Stephenson en
    /// 1829 ; voir docs/SOURCES.md.
    /// </summary>
    public double RollingResistance { get; set; } = 0.004;

    /// <summary>
    /// Vitesse limite d'un train de marchandises, en km/h, quelle que soit la
    /// locomotive : ce que permettent les wagons, leurs freins et la voie. 0 (défaut) :
    /// pas de limite, la vitesse maximale du catalogue fait foi — y compris celle
    /// d'une machine de vitesse attelée à des wagons de 1870, ce que la mesure
    /// montre absurde (docs/FINDINGS.md, « Les véhicules »).
    /// </summary>
    public double MaxTrainSpeedKmh { get; set; }

    /// <summary>
    /// Vrai (défaut) : le temps d'un tronçon compte la mise en vitesse au départ de
    /// chaque gare, limitée par l'adhérence puis par la puissance. Faux : le train
    /// roule d'emblée à sa vitesse de croisière — la variante qui isole l'effet de
    /// l'accélération dans la mesure.
    /// </summary>
    public bool Acceleration { get; set; } = true;
}
