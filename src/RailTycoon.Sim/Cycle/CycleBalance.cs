using RailTycoon.Sim.Economy;

namespace RailTycoon.Sim.Cycle;

/// <summary>
/// Analyse statique de la conjoncture : de combien, en espérance, elle déplace la
/// demande moyenne, et dans quel sens le catalogue d'événements la pousse, sans
/// rien simuler. Le pendant de <see cref="Events.EventCatalogBalance"/>, et pour la
/// même raison : un déséquilibre structurel ne ressemble pas à un bug.
/// <para>
/// Un cycle est une oscillation <em>autour</em> de la tendance du scénario. Si la
/// demande moyenne sur un cycle s'écarte de 1, le cycle ne fait pas qu'animer : il
/// mange ou gonfle le surplus de la carte, et le résultat du transporteur suit ce
/// surplus (docs/FINDINGS.md, « un choc à sens unique est un déséquilibre
/// déguisé »). Même chose pour les poussées : un catalogue qui pousse plus souvent
/// dans un sens allonge les phases de ce sens-là, donc déplace la demande moyenne
/// et le cours moyen.
/// </para>
/// <para>
/// Les deux calculs sont des espérances sur un cycle stationnaire : durée moyenne
/// de chaque phase (milieu de ses bornes), effets pleins — la transition, linéaire
/// et symétrique d'une phase à la suivante, déplace peu la moyenne. Ils ignorent
/// la phase d'ouverture, les bascules forcées par les historiques et la durée
/// finie d'une partie ; la mesure dit ce que ces trois-là font.
/// </para>
/// </summary>
public static class CycleBalance
{
    /// <summary>Durée moyenne d'un cycle complet, en ticks.</summary>
    public static double MeanCycleTicks(CycleDef def)
        => def.Phases.Sum(p => (p.MinTicks + p.MaxTicks) / 2.0);

    /// <summary>Part du temps passée dans chaque phase, en espérance, dans l'ordre des données.</summary>
    public static IReadOnlyList<(string PhaseId, double Share)> PhaseShares(CycleDef def)
    {
        double total = MeanCycleTicks(def);
        return def.Phases.Select(p => (p.Id, total > 0 ? (p.MinTicks + p.MaxTicks) / 2.0 / total : 0)).ToList();
    }

    /// <summary>
    /// Écart relatif attendu de la demande moyenne sur un cycle : 0 = la conjoncture
    /// oscille autour de la demande du scénario ; +0,01 = elle l'augmente de 1 % en
    /// moyenne.
    /// </summary>
    public static double DemandBias(CycleDef def)
    {
        double total = MeanCycleTicks(def);
        if (total <= 0) return 0;
        return def.Phases.Sum(p => (p.MinTicks + p.MaxTicks) / 2.0 * (p.DemandFactor - 1.0)) / total;
    }

    /// <summary>
    /// Facteur moyen du multiple de valorisation sur un cycle : 1 = la conjoncture
    /// oscille autour du multiple du scénario.
    /// </summary>
    public static double MeanEarningsMultipleFactor(CycleDef def)
    {
        double total = MeanCycleTicks(def);
        if (total <= 0) return 1;
        return def.Phases.Sum(p => (p.MinTicks + p.MaxTicks) / 2.0 * (double)p.EarningsMultipleFactor) / total;
    }

    /// <summary>
    /// Poussée nette attendue du catalogue aléatoire, en jours de bonnes nouvelles
    /// par an : somme des occurrences annuelles × poussée. 0 = autant de bonnes que
    /// de mauvaises nouvelles en espérance. Renvoie aussi les deux moitiés.
    /// </summary>
    public static (double Good, double Bad, double Net) PushBalance(ScenarioDef scenario)
    {
        double good = 0, bad = 0;
        foreach (var type in scenario.Events.Random)
        {
            if (type.Cycle is not { PushTicks: not 0 } effect) continue;
            double yearly = type.OccurrencesPerYear * effect.PushTicks;
            if (yearly > 0) good += yearly; else bad += yearly;
        }
        return (good, bad, good + bad);
    }
}
