using RailTycoon.Sim.Network;

namespace RailTycoon.Sim.Transport;

/// <summary>
/// Un arrêt sur une ligne. <see cref="DistanceKm"/> est la distance cumulée
/// depuis l'origine, mesurée sur la voie réelle quand la ligne est posée sur un
/// relief.
/// </summary>
public sealed class RailStop
{
    public required string CityId { get; init; }
    public required double DistanceKm { get; init; }

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

    /// <summary>
    /// Dénivelé positif cumulé, en mètres, dans le sens des arrêts croissants : les
    /// mètres que le train gravit, rugosité comprise. Le facteur de coût en est tiré
    /// (<c>climbEquivalentKm</c>) ; la dynamique des véhicules en tire la rampe
    /// moyenne, qui ralentit un train lourd (<see cref="RailLine.LegClimbGradient"/>).
    /// </summary>
    public double ForwardClimbM { get; init; }

    /// <summary>Dénivelé positif cumulé dans le sens des arrêts décroissants.</summary>
    public double ReverseClimbM { get; init; }

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
    /// <b>Ce que ce facteur ne fait pas, mesuré sur data/sierra.json.</b> Il est
    /// facturé (<c>OpportunisticHaulageSolver.MoveTrain</c>) mais n'entre dans
    /// aucune décision : le coût imputé qui décide d'un achat se calcule sur la
    /// distance plate. Il ne rétrécit donc <em>pas</em> le rayon économique des
    /// marchandises à bas prix, contrairement à ce qu'annonçait ce commentaire : la
    /// trace des marchés est la même au bit près que le relief coûte ou non. C'est un
    /// impôt sur le kilomètre-train, pas une géographie. Les options et leurs
    /// chiffres sont dans docs/FINDINGS.md, « Relief et économie ensemble ».
    /// </para>
    /// <para>
    /// <b>Sous le modèle de coût <c>mass</c></b> (<c>haulage.costModel</c>, opt-in),
    /// ce même facteur entre dans la facture <em>et</em> dans la décision, par une
    /// seule formule (<see cref="TrainCost"/>) : il multiplie la part fixe du train et
    /// la part de chaque chargement, et le transporteur décide sur la seconde. Là, il
    /// fait une géographie — docs/FINDINGS.md, « Le coût marginal réel ».
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

    /// <summary>
    /// Rampe moyenne gravie entre deux arrêts, dans le sens du parcours, sans
    /// dimension (0,01 = 1 %) : les mètres gravis divisés par la longueur. 0 sur le
    /// plat et en mode de compatibilité.
    /// <para>
    /// C'est la rampe que lit la dynamique des véhicules (<see cref="TrainDynamics"/>),
    /// et c'est une moyenne, pas la rampe déterminante : un train franchit une bosse
    /// courte sur son élan, et le temps d'un trajet limité par la puissance ne dépend
    /// que du travail total à fournir, donc des mètres gravis. Le géomètre publie
    /// aussi la rampe déterminante (<c>TrackProfile.RulingGradePercent</c>) ; elle ne
    /// sert pas ici — voir docs/FINDINGS.md, « Les véhicules ».
    /// </para>
    /// </summary>
    public double LegClimbGradient(int fromIndex, int toIndex)
    {
        if (Segments.Count == 0 || fromIndex == toIndex) return 0.0;

        bool forward = toIndex > fromIndex;
        int low = Math.Min(fromIndex, toIndex);
        int high = Math.Max(fromIndex, toIndex);

        double climb = 0;
        double total = 0;
        for (int i = low; i < high && i < Segments.Count; i++)
        {
            var segment = Segments[i];
            climb += forward ? segment.ForwardClimbM : segment.ReverseClimbM;
            total += segment.LengthKm;
        }
        return total <= 0 ? 0.0 : climb / (total * 1000.0);
    }
}
