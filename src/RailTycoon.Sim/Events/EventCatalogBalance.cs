using RailTycoon.Sim.Core;
using RailTycoon.Sim.Economy;

namespace RailTycoon.Sim.Events;

/// <summary>
/// Analyse statique du catalogue aléatoire : de combien, en espérance, il déplace
/// chaque levier de chaque marchandise à l'échelle de la carte, sans rien simuler.
/// <para>
/// C'est le pendant de <c>--balance</c> pour les événements, et il existe pour la
/// même raison : un déséquilibre structurel ne ressemble pas à un bug. Le premier
/// catalogue de <c>heartland-events</c> n'avait que des chocs à la hausse sur la
/// demande de nourriture — des afflux d'ouvriers sans épidémie. Tout tournait,
/// tous les invariants tenaient, et le transporteur perdait 41 000 de résultat net
/// sans que la dispersion bouge d'un iota : le catalogue mangeait le surplus de
/// nourriture de la carte, et le résultat suit ce surplus. Ce que la saison
/// garantit par construction — une moyenne annuelle égale au taux nominal —, un
/// catalogue aléatoire doit le garantir par ses données, et cet outil le vérifie.
/// </para>
/// <para>
/// Le calcul est une espérance, pas une simulation : occurrences par an × durée
/// utile moyenne (la durée moins une montée : une rampe linéaire vaut la moitié
/// de sa longueur en plein effet, montée et descente comprises) × écart moyen du
/// multiplicateur à 1 × poids nominal moyen des villes visées, rapporté au taux
/// nominal de toute la carte sur une année. Il ignore la règle de non-empilement,
/// qui retire quelques occurrences aux fréquences élevées, et les bornes de
/// sécurité, qui n'agissent que sur des superpositions.
/// </para>
/// </summary>
public static class EventCatalogBalance
{
    /// <summary>
    /// Biais d'un levier : écart relatif attendu, en moyenne annuelle, du taux
    /// nominal de toute la carte. 0 = catalogue équilibré ; −0,05 = le catalogue
    /// retire en moyenne 5 % de ce levier à la carte.
    /// </summary>
    public sealed record LeverBias(string Cargo, string On, double MapNominalRate, double Up, double Down)
    {
        public double Bias => Up + Down;
    }

    public static List<LeverBias> Compute(ScenarioDef scenario)
    {
        var up = new Dictionary<(string, string), double>();
        var down = new Dictionary<(string, string), double>();
        var order = new List<(string Cargo, string On)>();

        foreach (var type in scenario.Events.Random)
        {
            double usefulTicks = MeanUsefulTicks(type);
            foreach (var effect in type.Effects)
            {
                var key = (effect.Cargo, effect.On);
                if (!order.Contains(key)) order.Add(key);

                var eligible = Eligible(scenario, type).ToList();
                if (eligible.Count == 0) continue;
                double weight = eligible.Sum(c => Nominal(c, effect.Cargo, effect.On));
                if (type.Scope != "all") weight /= eligible.Count;

                double deviation = (effect.FactorMin + effect.FactorMax) / 2.0 - 1.0;
                double contribution = type.OccurrencesPerYear * usefulTicks * deviation * weight;
                var target = deviation >= 0 ? up : down;
                target[key] = target.GetValueOrDefault(key) + contribution;
            }
        }

        var result = new List<LeverBias>();
        foreach (var (cargo, on) in order)
        {
            double map = scenario.Cities.Sum(c => Nominal(c, cargo, on));
            double yearly = map * SimTick.TicksPerYear;
            result.Add(new LeverBias(cargo, on, map,
                yearly > 0 ? up.GetValueOrDefault((cargo, on)) / yearly : 0,
                yearly > 0 ? down.GetValueOrDefault((cargo, on)) / yearly : 0));
        }
        return result;
    }

    /// <summary>
    /// Durée de plein effet équivalente, en moyenne sur les durées tirées. Pour une
    /// durée D et une montée R (bornée à (D−1)/2 comme dans le solveur), la somme de
    /// l'enveloppe vaut exactement D − R.
    /// </summary>
    private static double MeanUsefulTicks(RandomEventTypeDef type)
    {
        int span = type.DurationMaxTicks - type.DurationMinTicks + 1;
        if (span <= 0) return 0;
        double sum = 0;
        for (int d = type.DurationMinTicks; d <= type.DurationMaxTicks; d++)
            sum += d - Math.Min(type.RampTicks, (d - 1) / 2);
        return sum / span;
    }

    private static IEnumerable<CityDef> Eligible(ScenarioDef scenario, RandomEventTypeDef type)
        => scenario.Cities.Where(c =>
            (type.Cities.Count == 0 || type.Cities.Contains(c.Id)) &&
            type.Effects.Any(e => Nominal(c, e.Cargo, e.On) > 0));

    private static double Nominal(CityDef city, string cargo, string on)
        => (on == EventLever.Production ? city.Production : city.Demand).GetValueOrDefault(cargo);
}
