using RailTycoon.Sim.Economy;
using RailTycoon.Sim.Network;

namespace RailTycoon.Sim.Transport;

/// <summary>
/// Un arrêt sur une ligne : une gare. <see cref="DistanceKm"/> est la distance
/// cumulée depuis l'origine, mesurée sur la voie réelle quand la ligne est posée sur
/// un relief.
/// <para>
/// Un train à l'arrêt achète et vend sur les marchés à portée de la gare
/// (<see cref="Markets"/>) — la place d'une ville et les installations qui
/// l'entourent —, et sur aucun autre. <see cref="CityId"/> nomme la ville de la gare ;
/// il ne dit pas avec qui le train échange.
/// </para>
/// </summary>
public sealed class RailStop
{
    public required string CityId { get; init; }
    public required double DistanceKm { get; init; }

    /// <summary>Rayon d'action déclaré par l'arrêt, ou <c>null</c> pour le défaut.</summary>
    public double? DeclaredRadiusKm { get; init; }

    /// <summary>Position de la gare sur la carte, en kilomètres.</summary>
    public double XKm { get; internal set; }
    public double YKm { get; internal set; }

    /// <summary>Rayon d'action effectif de la gare, en kilomètres.</summary>
    public double RadiusKm { get; internal set; }

    /// <summary>
    /// Les marchés à portée de la gare, dans l'ordre stable des villes puis de
    /// <c>WorldState.MarketsOf</c>. Fixés à la construction du monde.
    /// </summary>
    public IReadOnlyList<Market> Markets { get; internal set; } = Array.Empty<Market>();

    /// <summary>
    /// Nœud du graphe de voies correspondant, ou -1 en mode de compatibilité.
    /// C'est par là qu'un module ayant besoin de la topologie — la circulation —
    /// remonte de l'arrêt au réseau.
    /// </summary>
    public int NodeIndex { get; init; } = -1;
}

/// <summary>
/// Le trajet entre deux arrêts consécutifs, vu du réseau : les arêtes franchies et
/// ce que le relief y coûte.
/// <para>
/// C'est la pièce qui fait la charnière entre les deux modules. Le transport n'y
/// lit que <see cref="LengthKm"/> et les facteurs de coût — des scalaires. La
/// circulation, elle, y trouvera <see cref="Legs"/> : la liste exacte des tronçons
/// orientés à réserver pour laisser passer un train d'un arrêt au suivant.
/// </para>
/// </summary>
public sealed class RailSegment
{
    public required int FromStopIndex { get; init; }
    public required int ToStopIndex { get; init; }
    public required double LengthKm { get; init; }

    /// <summary>Coût kilométrique relatif dans le sens des arrêts croissants.</summary>
    public required double ForwardCostFactor { get; init; }

    /// <summary>Coût kilométrique relatif dans le sens des arrêts décroissants.</summary>
    public required double ReverseCostFactor { get; init; }

    public required IReadOnlyList<RouteLeg> Legs { get; init; }
}

/// <summary>
/// Une ligne de chemin de fer, telle que le transport la voit : une suite ordonnée
/// d'arrêts et de distances.
/// <para>
/// Cette forme plate est délibérément conservée. Elle était, dans le prototype
/// économique, tout ce qui existait — une ligne posée à la main dans le scénario.
/// Elle est désormais la <em>projection</em> d'un itinéraire du graphe de voies
/// (<see cref="Route"/>), et c'est ce qui permet au transport d'ignorer
/// complètement le relief : il demande une distance entre deux arrêts, il reçoit la
/// longueur réelle de la voie ; il demande un facteur de coût, il reçoit un nombre.
/// </para>
/// <para>
/// <b>Mode de compatibilité.</b> <see cref="Route"/> vaut <c>null</c> pour une
/// ligne déclarée dans <c>scenario.lines</c> avec des distances en kilomètres. Tout
/// fonctionne alors exactement comme avant, et <see cref="LegCostFactor"/> vaut 1.
/// C'est la voie par laquelle <c>data/heartland.json</c> continue de tourner sans
/// que ses traces de régression bougent d'un centime.
/// </para>
/// </summary>
public sealed class RailLine
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required List<RailStop> Stops { get; init; }

    /// <summary>Itinéraire dans le graphe de voies, ou <c>null</c> en mode de compatibilité.</summary>
    public TrackRoute? Route { get; init; }

    /// <summary>
    /// Trajets entre arrêts consécutifs, dans l'ordre. Vide en mode de
    /// compatibilité : sans graphe, il n'y a pas de tronçon à décrire.
    /// </summary>
    public IReadOnlyList<RailSegment> Segments { get; init; } = Array.Empty<RailSegment>();

    public double LengthKm => Stops.Count == 0 ? 0 : Stops[^1].DistanceKm - Stops[0].DistanceKm;

    public double DistanceBetween(int fromIndex, int toIndex)
        => Math.Abs(Stops[toIndex].DistanceKm - Stops[fromIndex].DistanceKm);

    /// <summary>
    /// Ce que le relief ajoute au coût kilométrique entre deux arrêts, dans le sens
    /// du parcours. 1 sur le plat et en mode de compatibilité.
    /// <para>
    /// C'est le seul endroit où le relief atteint l'économie. Le résumer en un
    /// facteur est un choix : un train qui franchit un col paie plus cher le
    /// kilomètre. Le transport n'a pas à savoir pourquoi.
    /// </para>
    /// <para>
    /// <b>Sous le modèle de coût <c>mass</c></b> (<c>haulage.costModel</c>, défaut),
    /// ce facteur entre dans la facture <em>et</em> dans la décision, par une seule
    /// formule (<see cref="TrainCost"/>) : il multiplie la part de la locomotive et
    /// celle de chaque chargement, et le transporteur décide sur la seconde. La trace
    /// des marchés dépend donc du relief — docs/FINDINGS.md, « Le coût marginal réel
    /// par défaut ».
    /// </para>
    /// <para>
    /// <b>Sous l'ancien modèle <c>flat</c></b>, mesuré sur data/sierra.json, il est
    /// facturé (<c>OpportunisticHaulageSolver.MoveTrain</c>) mais n'entre dans aucune
    /// décision : le coût imputé qui décide d'un achat s'y calcule sur la distance
    /// plate, et la trace des marchés est la même au bit près que le relief coûte ou
    /// non. Un impôt sur le kilomètre-train, pas une géographie : docs/FINDINGS.md,
    /// « Relief et économie ensemble ».
    /// </para>
    /// </summary>
    public double LegCostFactor(int fromIndex, int toIndex)
    {
        if (Segments.Count == 0 || fromIndex == toIndex) return 1.0;

        bool forward = toIndex > fromIndex;
        int low = Math.Min(fromIndex, toIndex);
        int high = Math.Max(fromIndex, toIndex);

        double weighted = 0;
        double total = 0;
        for (int i = low; i < high && i < Segments.Count; i++)
        {
            var segment = Segments[i];
            double factor = forward ? segment.ForwardCostFactor : segment.ReverseCostFactor;
            weighted += segment.LengthKm * factor;
            total += segment.LengthKm;
        }
        return total <= 0 ? 1.0 : weighted / total;
    }
}
