using RailTycoon.Sim.Core;

namespace RailTycoon.Sim.Economy;

/// <summary>
/// Contrat du modèle de prix. Isolé derrière une interface parce que c'est la
/// pièce qu'on va réécrire le plus souvent pendant l'équilibrage : on veut
/// pouvoir comparer deux modèles sur la même trace de simulation.
/// </summary>
public interface IPriceModel
{
    string Name { get; }

    /// <summary>
    /// Mesure de saturation du marché : combien d'horizons de consommation le
    /// stock local couvre. 1 = à l'équilibre, 0 = pénurie, &gt;1 = surabondance.
    /// Exposée dans le contrat parce que la production primaire s'en sert pour
    /// se brider, et l'interface utilisateur pour colorer les marchés.
    /// </summary>
    double Coverage(Market market);

    double PriceFor(CargoDef cargo, Market market);
}

/// <summary>
/// Modèle hyperbolique : le prix dépend de la <em>couverture</em>, c'est-à-dire
/// du nombre de ticks de consommation que le stock local peut assurer.
/// <para>
/// couverture = stock / (demande_effective × horizon)<br/>
/// multiplicateur = (forme + 1) / (forme + couverture)
/// </para>
/// À couverture 1 le multiplicateur vaut exactement 1 : le stock couvre
/// l'horizon, le prix est celui de référence. Stock vide, le prix grimpe vers
/// (forme+1)/forme ; surabondance, il s'effondre vers le plancher.
/// <para>
/// C'est ce qui produit la boucle de jeu de Railroad Tycoon : livrer une
/// marchandise fait monter l'offre locale, donc baisser le prix, donc se refermer
/// l'arbitrage qu'on exploitait. Le joueur doit constamment trouver la prochaine
/// asymétrie au lieu de faire tourner une ligne rentable pour l'éternité.
/// </para>
/// </summary>
public sealed class HyperbolicPriceModel : IPriceModel
{
    /// <summary>
    /// Plafond de couverture. La couverture reste ainsi toujours un nombre fini :
    /// un infini se propagerait dans les statistiques, les CSV et les
    /// comparaisons, et transformerait un cas limite bénin en valeurs illisibles.
    /// </summary>
    public const double MaxCoverage = 999.0;

    private readonly PriceModelDef _cfg;

    public HyperbolicPriceModel(PriceModelDef cfg) => _cfg = cfg;

    public string Name => "hyperbolic";

    /// <inheritdoc/>
    public double Coverage(Market market)
    {
        double demand = market.BaseDemandRate + market.IndustryDemandRate;
        if (demand <= 0) return MaxCoverage;
        return Math.Min(MaxCoverage, market.Stock / (demand * _cfg.CoverageHorizonTicks));
    }

    public double PriceFor(CargoDef cargo, Market market)
    {
        // Aucun acheteur ici : la marchandise n'y vaut presque rien.
        //
        // Ce cas mérite un traitement explicite. En le noyant dans un « plancher
        // de demande » commun, la première version donnait à tout marché vide un
        // prix de pénurie — y compris pour des marchandises que la ville ne
        // consomme pas. Le transporteur y voyait des destinations lucratives et
        // acheminait blé et farine vers une ville qui n'en consommait pas un
        // gramme, où le fret s'accumulait sans jamais trouver preneur.
        double demand = market.BaseDemandRate + market.IndustryDemandRate;
        if (demand <= 0) return cargo.BasePrice * _cfg.MinMultiplier;

        double coverage = Coverage(market);
        double multiplier = (_cfg.Shape + 1.0) / (_cfg.Shape + coverage);
        multiplier = Maths.Clamp(multiplier, _cfg.MinMultiplier, _cfg.MaxMultiplier);
        return cargo.BasePrice * multiplier;
    }
}
