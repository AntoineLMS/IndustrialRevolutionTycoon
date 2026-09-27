namespace RailTycoon.Sim.Transport;

/// <summary>
/// Un arrêt sur une ligne. <see cref="DistanceKm"/> est la distance cumulée
/// depuis l'origine.
/// </summary>
public sealed class RailStop
{
    public required string CityId { get; init; }
    public required double DistanceKm { get; init; }
}

/// <summary>
/// Une ligne de chemin de fer, réduite ici à une suite ordonnée d'arrêts et de
/// distances.
/// <para>
/// C'est délibérément une abstraction pauvre : dans ce prototype la ligne est
/// posée à la main et sert uniquement à donner au transport une <em>latence</em>
/// et un <em>coût kilométrique</em>. Le vrai module réseau (graphe de voies
/// posées sur un relief, coût de terrassement, ponts, tunnels, aiguillages,
/// signalisation) viendra derrière cette même façade, et personne d'autre que le
/// transport n'a besoin d'en connaître les détails.
/// </para>
/// </summary>
public sealed class RailLine
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required List<RailStop> Stops { get; init; }

    public double LengthKm => Stops.Count == 0 ? 0 : Stops[^1].DistanceKm - Stops[0].DistanceKm;

    public double DistanceBetween(int fromIndex, int toIndex)
        => Math.Abs(Stops[toIndex].DistanceKm - Stops[fromIndex].DistanceKm);
}
