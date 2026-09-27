using RailTycoon.Sim.Core;

namespace RailTycoon.Sim.Economy;

/// <summary>
/// L'état d'une marchandise sur un marché local. C'est l'unité de base de toute
/// l'économie : il y a un Market par (ville, marchandise).
/// </summary>
public sealed class Market
{
    public required string CargoId { get; init; }
    public required string CityId { get; init; }

    /// <summary>Stock disponible, en chargements.</summary>
    public double Stock;

    /// <summary>Prix local courant, recalculé à chaque tick.</summary>
    public double Price;

    /// <summary>Consommation de base par tick, avant élasticité.</summary>
    public double BaseDemandRate;

    /// <summary>Production primaire par tick, avant saturation.</summary>
    public double BaseProductionRate;

    /// <summary>Demande induite par les usines locales (somme de leurs intrants).</summary>
    public double IndustryDemandRate;

    /// <summary>
    /// Consommation des habitants telle que le scénario la déclare. Fixée à la
    /// construction du monde, jamais modifiée : c'est la référence de toutes les
    /// modulations du jour — saison, taille de la ville, événements — sans quoi les
    /// facteurs se composeraient d'un tick au suivant et dériveraient sans retour.
    /// </summary>
    public double NominalDemandRate { get; internal set; }

    /// <summary>Production primaire telle que le scénario la déclare. Même rôle que <see cref="NominalDemandRate"/>.</summary>
    public double NominalProductionRate { get; internal set; }

    /// <summary>
    /// Multiplicateur du jour publié par le module events (phase 0b) sur la
    /// production primaire, et que le solveur économique compose dans
    /// <see cref="BaseProductionRate"/>. Vaut 1 exactement quand aucun événement
    /// n'agit — et multiplier par 1 ne change pas le dernier bit d'un taux, ce qui
    /// garde toutes les traces existantes intactes.
    /// </summary>
    public double EventProductionFactor = 1.0;

    /// <summary>Multiplicateur du jour publié par le module events sur la demande des habitants.</summary>
    public double EventDemandFactor = 1.0;

    // --- Télémétrie du tick courant. Remise à zéro au début de chaque tick.
    // Ces compteurs ne sont pas cosmétiques : ils servent à vérifier
    // l'invariant de conservation et à tracer les courbes.
    public double ProducedThisTick;
    public double ConsumedThisTick;
    public double ImportedThisTick;
    public double ExportedThisTick;

    // --- Cumuls sur toute la partie, pour l'invariant de conservation.
    public double TotalProduced;
    public double TotalConsumed;

    public void BeginTick()
    {
        ProducedThisTick = 0;
        ConsumedThisTick = 0;
        ImportedThisTick = 0;
        ExportedThisTick = 0;
    }

    public void Produce(double qty)
    {
        if (qty <= 0) return;
        Stock += qty;
        ProducedThisTick += qty;
        TotalProduced += qty;
    }

    /// <summary>Consomme au plus <paramref name="qty"/>, et renvoie la quantité réellement consommée.</summary>
    public double Consume(double qty)
    {
        if (qty <= 0) return 0;
        double actual = Math.Min(qty, Stock);
        Stock = Maths.SnapToZero(Stock - actual);
        ConsumedThisTick += actual;
        TotalConsumed += actual;
        return actual;
    }

    /// <summary>
    /// Part du stock qu'un train peut acheter : l'excédent au-delà de ce que la
    /// ville garde pour elle. Voir <see cref="EconomyDef.RetainedCoverage"/> pour
    /// la raison d'être de cette réserve.
    /// </summary>
    public double SellableStock(double horizonTicks, double retainedCoverage)
    {
        double demand = BaseDemandRate + IndustryDemandRate;
        if (demand <= 0) return Stock; // aucun besoin local : tout est cessible
        return Math.Max(0, Stock - demand * horizonTicks * retainedCoverage);
    }

    /// <summary>
    /// Retire du stock pour chargement dans un train. Contrairement à
    /// <see cref="Consume"/>, la marchandise n'est pas détruite : elle change de
    /// détenteur, et l'invariant de conservation en tient compte.
    /// </summary>
    public double Withdraw(double qty)
    {
        if (qty <= 0) return 0;
        double actual = Math.Min(qty, Stock);
        Stock = Maths.SnapToZero(Stock - actual);
        ExportedThisTick += actual;
        return actual;
    }

    public void Deposit(double qty)
    {
        if (qty <= 0) return;
        Stock += qty;
        ImportedThisTick += qty;
    }
}

public sealed class Industry
{
    public required RecipeDef Recipe { get; init; }
    public double Capacity;

    /// <summary>Taux d'utilisation du dernier tick, dans [0, 1]. Sert au diagnostic d'équilibrage.</summary>
    public double Utilization;
}

public sealed class City
{
    public required CityDef Def { get; init; }
    public string Id => Def.Id;

    public readonly Dictionary<string, Market> Markets = new();
    public readonly List<Industry> Industries = new();

    public Market Market(string cargoId) => Markets[cargoId];
}

/// <summary>
/// La compagnie du joueur. Dans ce prototype on ne modélise que la trésorerie
/// et le compte d'exploitation ; la bourse, les obligations et la distinction
/// entre caisse personnelle et caisse de la société arrivent avec le module
/// finance (voir docs/CONTRACTS.md).
/// </summary>
public sealed class Company
{
    public double Cash;

    public double TotalHaulRevenue;
    public double TotalCargoPurchases;
    public double TotalOperatingCost;

    public double RevenueThisTick;
    public double CostThisTick;

    /// <summary>
    /// Découvert autorisé sur la trésorerie d'exploitation, accordé par le module
    /// finance et gagé sur les capitaux propres de la société.
    /// <para>
    /// Il existe parce que la trésorerie pouvait plonger indéfiniment dans le
    /// rouge sans que rien ne se passe : le transporteur refusait d'acheter du
    /// fret à découvert, mais les coûts kilométriques étaient prélevés sans
    /// condition. Plus la compagnie roulait, plus elle creusait, et aucun
    /// invariant ne s'en apercevait — la trésorerie négative était parfaitement
    /// cohérente avec le compte d'exploitation. Le découvert rend cette dette
    /// explicite : elle figure au bilan, elle porte intérêt, et elle est bornée.
    /// </para>
    /// <para>
    /// Zéro par défaut, donc un scénario sans module finance se comporte
    /// exactement comme avant.
    /// </para>
    /// </summary>
    public double CreditLimit;

    /// <summary>
    /// Vrai quand le module finance a placé la compagnie sous administration : le
    /// découvert dépasse ce que son bilan peut gager. Les trains cessent de rouler
    /// — un réseau qu'on ne peut plus financer ne creuse pas son déficit en
    /// continuant à brûler du charbon. Le magnat peut lever cet état en
    /// recapitalisant.
    /// </summary>
    public bool Grounded;

    /// <summary>Ce que la compagnie peut encore engager : sa caisse, plus le découvert qu'on lui accorde.</summary>
    public double SpendableCash => Cash + CreditLimit;

    public void BeginTick()
    {
        RevenueThisTick = 0;
        CostThisTick = 0;
    }

    public void Earn(double amount)
    {
        Cash += amount;
        RevenueThisTick += amount;
        TotalHaulRevenue += amount;
    }

    public void PayForCargo(double amount)
    {
        Cash -= amount;
        CostThisTick += amount;
        TotalCargoPurchases += amount;
    }

    public void PayOperating(double amount)
    {
        Cash -= amount;
        CostThisTick += amount;
        TotalOperatingCost += amount;
    }

    /// <summary>
    /// Cumul net des mouvements financiers passés en trésorerie : positif si la
    /// finance a apporté de l'argent (emprunt, apport en capital, cession de
    /// titres), négatif si elle en a prélevé (intérêts, dividende, achat de
    /// titres, remboursement).
    /// <para>
    /// Ce quatrième flux existe pour refermer une fuite réelle. Le module finance
    /// tenait sa propre caisse et la trésorerie d'exploitation en ignorait les
    /// mouvements : un dividende de 88 000 ou une OPA de 75 000 sortaient du bilan
    /// de la société sans jamais sortir de la caisse du transporteur, qui pouvait
    /// donc dépenser le même argent. Chaque bilan s'équilibrait pourtant au
    /// centime — c'est exactement la signature du lavage de fret, une fuite que
    /// tous les invariants laissent passer parce qu'ils vérifient chacun une
    /// moitié de la vérité.
    /// </para>
    /// <para>
    /// <see cref="Cash"/> reste la seule vérité sur l'argent disponible à
    /// l'exploitation. La finance ne tient plus de caisse parallèle : elle écrit
    /// ici, et <c>bilan-tresorerie</c> exige que la trésorerie s'explique
    /// intégralement par recettes − achats − exploitation + flux financiers.
    /// </para>
    /// </summary>
    public double TotalFinanceFlow;

    /// <summary>
    /// Mouvement de trésorerie décidé par le module finance. Le signe porte le
    /// sens : positif apporte, négatif prélève.
    /// </summary>
    public void ApplyFinanceFlow(double amount)
    {
        if (amount == 0) return;
        Cash += amount;
        TotalFinanceFlow += amount;
    }

    /// <summary>
    /// Résultat net cumulé de l'exploitation depuis le début de la partie.
    /// <para>
    /// Volontairement <b>hors</b> flux financiers : un emprunt n'est pas une
    /// recette et un dividende n'est pas une charge d'exploitation. C'est ce qui
    /// garde la marge au kilomètre interprétable comme une mesure du transport, et
    /// la sentinelle anti-lavage-de-fret utilisable.
    /// </para>
    /// </summary>
    public double NetProfit => TotalHaulRevenue - TotalCargoPurchases - TotalOperatingCost;
}
