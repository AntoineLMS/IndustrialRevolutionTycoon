using RailTycoon.Sim.Core;

namespace RailTycoon.Sim.Economy;

/// <summary>
/// Contrat du solveur économique. C'est LE point d'extension du projet : tout ce
/// qui concerne production, consommation et formation des prix passe par ici.
/// <para>
/// Règles que toute implémentation doit respecter — elles sont vérifiées par les
/// tests d'invariants, pas seulement recommandées :
/// </para>
/// <list type="number">
///   <item>Ne jamais créer ni détruire de marchandise autrement que par
///   <see cref="Market.Produce"/> et <see cref="Market.Consume"/>, afin que le
///   bilan matière reste vérifiable.</item>
///   <item>Ne jamais laisser un stock négatif.</item>
///   <item>Être déterministe : à état et graine identiques, sortie identique.
///   Aucun parcours de dictionnaire non trié, aucun appel à l'horloge système.</item>
///   <item>Ne pas toucher à la trésorerie de la compagnie : l'argent du joueur
///   ne bouge que par le transport.</item>
///   <item>Composer ses taux du jour avec les multiplicateurs que le module events
///   publie sur chaque marché avant la phase 1 (<see cref="Market.EventDemandFactor"/>,
///   <see cref="Market.EventProductionFactor"/>), et dimensionner l'entrepôt d'un
///   site sur son débit nominal. Un solveur qui les ignore rend le module muet sans
///   erreur ; le test « un événement historique agit à sa date » le détecte, sous
///   chacun des deux solveurs livrés. Même chose pour la demande avec le
///   multiplicateur de la conjoncture (<see cref="Market.CycleDemandFactor"/>,
///   phase 0c), composé par un produit avec celui des événements.</item>
/// </list>
/// </summary>
public interface IEconomySolver
{
    string Name { get; }
    void Initialize(WorldState world);
    void Step(WorldState world, SimTick tick);
}

/// <summary>
/// Implémentation de référence, volontairement simple et lisible. Elle sert de
/// témoin : elle tourne, elle respecte les invariants, elle produit des courbes
/// exploitables. Un modèle plus riche (stocks anticipés, contrats, saisonnalité,
/// concurrence entre acheteurs) devra la remplacer derrière la même interface et
/// faire mieux <em>sur les mêmes scénarios de régression</em>.
/// </summary>
public sealed class ReferenceEconomySolver : IEconomySolver
{
    public string Name => "reference";

    public void Initialize(WorldState world)
    {
        // La demande induite par les usines est calculée par WorldBuilder : elle
        // découle de l'implantation des usines, pas du modèle économique, et les
        // stocks initiaux en dépendent.
        RecomputePrices(world);
    }

    public void Step(WorldState world, SimTick tick)
    {
        // L'ordre des phases est significatif et doit rester stable : le changer
        // change les résultats et invalide les traces de régression.
        ApplyRates(world);
        ProducePrimary(world);
        RunIndustries(world);
        ConsumeDemand(world);
        RecomputePrices(world);
    }

    /// <summary>
    /// Préambule : les taux du jour. La référence n'a pas de modulation propre —
    /// ni saison ni croissance — donc le taux du jour est le taux nominal, multiplié
    /// par ce que le module events a publié sur le marché — et, pour la demande, par
    /// ce que le module cycle y a publié (phase 0c). Les deux se composent par un
    /// produit ; ni l'un ni l'autre ne réécrit le nombre de l'autre.
    /// <para>
    /// Sans événement, les deux multiplicateurs valent 1 exactement, et
    /// <c>nominal × 1</c> est <c>nominal</c> au bit près : la trace de la référence
    /// est celle d'avant le module, ce que vérifient les traces de référence. Le
    /// même produit est composé par le solveur anticipant avec sa saison et la taille
    /// de ses villes. Toute la logique des événements — dates, enveloppes, tirages,
    /// cibles, bornes — vit dans le module events ; un solveur ne lit qu'un nombre
    /// par marché et par levier.
    /// </para>
    /// </summary>
    private static void ApplyRates(WorldState world)
    {
        foreach (var city in world.Cities)
            foreach (var market in world.MarketsOf(city))
            {
                market.BaseDemandRate = market.NominalDemandRate * market.EventDemandFactor * market.CycleDemandFactor;
                market.BaseProductionRate = market.NominalProductionRate * market.EventProductionFactor;
            }
    }

    /// <summary>
    /// Phase 1 — fermes, mines et forêts. La production s'étiole quand le stock
    /// local s'accumule : sans ce frein, une ferme non desservie accumulerait du
    /// blé à l'infini et le prix resterait collé au plancher.
    /// </summary>
    private void ProducePrimary(WorldState world)
    {
        var cfg = world.Def.Economy;

        foreach (var city in world.Cities)
        {
            foreach (var market in world.MarketsOf(city))
            {
                if (market.BaseProductionRate <= 0) continue;

                // Le frein est l'encombrement de l'entrepôt du site, pas le prix
                // ni la demande locale : plein régime jusqu'à mi-capacité, puis
                // décroissance linéaire jusqu'au débit résiduel.
                //
                // L'entrepôt est dimensionné sur le débit NOMINAL, pas sur celui du
                // jour. Une grève ne rétrécit pas le carreau : dimensionné sur le
                // débit du jour, un site touché verrait son frein mordre plus tôt
                // précisément quand il produit moins, et subirait l'événement deux
                // fois. Sans événement les deux débits sont égaux au bit près.
                double storage = market.NominalProductionRate * cfg.ProductionStorageTicks;
                double factor = storage > 0
                    ? Maths.Clamp(2.0 * (1.0 - market.Stock / storage), cfg.MinProductionFactor, 1.0)
                    : 1.0;

                market.Produce(market.BaseProductionRate * factor);
            }
        }
    }

    /// <summary>
    /// Phase 2 — usines. Une usine ne tourne que si transformer est rentable aux
    /// prix locaux du moment, et seulement dans la limite des intrants présents.
    /// </summary>
    private void RunIndustries(WorldState world)
    {
        var cfg = world.Def.Economy;

        foreach (var city in world.Cities)
        {
            foreach (var industry in city.Industries)
            {
                var recipe = industry.Recipe;
                double capacityRuns = recipe.RatePerTick * industry.Capacity;
                double maxRuns = capacityRuns;

                // Limite imposée par les intrants disponibles.
                foreach (var input in recipe.Inputs)
                {
                    double available = city.Market(input.Cargo).Stock;
                    maxRuns = Math.Min(maxRuns, available / input.Qty);
                }

                // Limite imposée par l'encombrement des entrepôts de sortie. Une
                // usine dont personne n'enlève la production finit par s'arrêter,
                // au lieu d'accumuler indéfiniment.
                foreach (var output in recipe.Outputs)
                {
                    double storage = output.Qty * capacityRuns * cfg.ProductionStorageTicks;
                    if (storage <= 0) continue;
                    double factor = Maths.Clamp(
                        2.0 * (1.0 - city.Market(output.Cargo).Stock / storage), 0.0, 1.0);
                    maxRuns = Math.Min(maxRuns, capacityRuns * factor);
                }

                if (maxRuns <= 0)
                {
                    industry.Utilization = 0;
                    continue;
                }

                // Les intrants sont valorisés au prix local — c'est ce que l'usine
                // paie réellement. Les produits le sont au prix de référence, et
                // non au prix local.
                //
                // La distinction est essentielle et la première version s'y est
                // cassée : un moulin ne consomme pas sa propre farine, donc la
                // farine ne vaut rien chez lui. Valoriser sa production au prix
                // local revenait à conclure que moudre n'est jamais rentable, et
                // toutes les usines du scénario se sont arrêtées. Une usine produit
                // pour expédier : sa production vaut ce qu'elle vaut dans
                // l'économie, pas ce qu'en donnerait un voisin qui n'en veut pas.
                double inputValue = 0;
                foreach (var input in recipe.Inputs)
                    inputValue += city.Market(input.Cargo).Price * input.Qty;

                double outputValue = 0;
                foreach (var output in recipe.Outputs)
                    outputValue += world.Cargo(output.Cargo).BasePrice * output.Qty;

                if (outputValue < inputValue * (1.0 + recipe.MinMargin))
                {
                    industry.Utilization = 0;
                    continue;
                }

                foreach (var input in recipe.Inputs)
                    city.Market(input.Cargo).Consume(maxRuns * input.Qty);

                foreach (var output in recipe.Outputs)
                    city.Market(output.Cargo).Produce(maxRuns * output.Qty);

                industry.Utilization = capacityRuns > 0 ? maxRuns / capacityRuns : 0;
            }
        }
    }

    /// <summary>
    /// Phase 3 — consommation des habitants, modulée par le prix. Cher, on se
    /// restreint ; bon marché, on consomme un peu plus. C'est ce qui empêche un
    /// marché saturé de rester saturé indéfiniment.
    /// </summary>
    private void ConsumeDemand(WorldState world)
    {
        var cfg = world.Def.Economy;

        foreach (var city in world.Cities)
        {
            foreach (var market in world.MarketsOf(city))
            {
                if (market.BaseDemandRate <= 0) continue;

                var cargo = world.Cargo(market.CargoId);
                double priceRatio = market.Price > 0 ? cargo.BasePrice / market.Price : 1.0;
                double response = Maths.Clamp(
                    Math.Pow(priceRatio, cargo.Elasticity),
                    cfg.MinConsumptionResponse,
                    cfg.MaxConsumptionResponse);
                market.Consume(market.BaseDemandRate * response);
            }
        }
    }

    private void RecomputePrices(WorldState world)
    {
        foreach (var city in world.Cities)
            foreach (var market in world.MarketsOf(city))
                market.Price = world.PriceModel.PriceFor(world.Cargo(market.CargoId), market);
    }
}
