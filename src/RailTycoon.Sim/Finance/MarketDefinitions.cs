namespace RailTycoon.Sim.Finance;

// Données de la bourse et de la rémunération du président. Même règle que le reste
// du module : toute valeur équilibrable vit dans data/*.json. Chaque mécanique a une
// valeur par défaut qui la rend inerte — un scénario qui ne déclare pas ces blocs
// joue exactement la partie d'avant, au centime, et les scénarios de test construits
// dans le code aussi.

/// <summary>
/// La bourse : ce qui fait qu'un ordre ne s'exécute pas au cours affiché quel que soit
/// son volume, et que le cours ne reste pas où un ordre l'a porté.
/// </summary>
public sealed class StockMarketDef
{
    /// <summary>
    /// Profondeur du carnet : taille d'une tranche, en fraction du nombre d'actions
    /// émises. Un ordre se découpe en tranches ; chacune s'exécute au cours du moment,
    /// puis déplace le cours de l'impact de marché (<see cref="ValuationDef.MarketImpact"/>)
    /// avant la suivante. Un gros ordre paie donc ses dernières actions plus cher que
    /// les premières — le découpage d'une grosse livraison côté fret, transposé.
    /// <para>
    /// 0 = profondeur infinie : l'ordre entier au cours d'avant, puis l'impact d'un
    /// bloc. C'est le comportement d'avant, et la dette connue n° 1 du module.
    /// </para>
    /// </summary>
    public decimal BookTrancheFraction { get; set; }

    public ProfitTakingDef ProfitTaking { get; set; } = new();

    public StockSplitDef Split { get; set; } = new();
}

/// <summary>
/// Les prises de bénéfices : les autres actionnaires revendent une partie de leurs
/// titres quand le cours monte trop vite, trop haut.
/// <para>
/// <b>Le déclencheur.</b> Le cours du jour dépasse de plus de
/// <see cref="ThresholdPercent"/> la plus haute de deux références : la valeur
/// fondamentale par action (celle vers laquelle la valorisation fait converger le
/// cours), et le cours de référence des investisseurs, qui suit le cours <em>à la
/// baisse le jour même</em> et <em>à la hausse sur <see cref="ReferenceTicks"/>
/// jours</em>. Il faut les deux : une montée qui rattrape la valeur fondamentale (une
/// expansion, une fusion rentable) n'est pas une montée à vendre, et un cours encore
/// au-dessus de sa valeur mais qui baisse (le début d'une crise) n'est pas une montée
/// du tout — vendre alors accélérerait le krach, ce que personne n'a demandé.
/// </para>
/// </summary>
public sealed class ProfitTakingDef
{
    public bool Enabled { get; set; }

    /// <summary>Seuil de déclenchement, en pourcentage au-dessus de la référence.</summary>
    public decimal ThresholdPercent { get; set; } = 15m;

    /// <summary>Jours qu'il faut au cours de référence pour rattraper une hausse (moyenne glissante exponentielle à la hausse).</summary>
    public int ReferenceTicks { get; set; } = 30;

    /// <summary>
    /// Vitesse, dans ]0, 1] : part de l'excès au-dessus du seuil que les ventes du jour
    /// referment. 1 = le cours revient au seuil le jour même.
    /// </summary>
    public decimal Speed { get; set; } = 0.5m;

    /// <summary>Part vendue : au plus cette fraction des titres du flottant change de mains dans la journée.</summary>
    public decimal MaxSellFraction { get; set; } = 0.1m;
}

/// <summary>
/// La division d'actions. À la clôture de chaque année, une compagnie dont le cours
/// finit au-dessus du seuil voit chaque action divisée : chaque actionnaire en détient
/// <see cref="Ratio"/> fois plus, le cours est divisé d'autant, la capitalisation ne
/// bouge pas.
/// </summary>
public sealed class StockSplitDef
{
    public bool Enabled { get; set; }

    /// <summary>Cours de clôture annuelle au-delà duquel les actions sont divisées.</summary>
    public decimal PriceThreshold { get; set; } = 100m;

    /// <summary>
    /// Actions nouvelles pour une ancienne. Seuls les rapports de la forme 2^a·5^b sont
    /// acceptés (2, 4, 5, 10…) : ce sont ceux pour lesquels le cours divisé s'écrit
    /// exactement en décimal, donc pour lesquels la capitalisation et chaque fortune
    /// restent identiques au centime. Diviser par trois laisserait un reste.
    /// </summary>
    public int Ratio { get; set; } = 2;
}

/// <summary>
/// Le salaire du président : versé par la compagnie du joueur à la caisse personnelle
/// du magnat, révisé chaque année selon la performance, complété d'une prime en cas de
/// surperformance.
/// <para>
/// <b>La performance</b> de l'exercice est son résultat — variation du report à
/// nouveau, dividendes rajoutés puisqu'ils distribuent le résultat sans en être une
/// perte — rapporté à la valeur de la compagnie : sa capitalisation moyenne sur
/// l'exercice (<c>marketValue</c>, la lecture de la vision pour la sanction du score
/// de dirigeant), ou ses capitaux propres d'ouverture (<c>bookEquity</c>). La moyenne
/// sur l'année plutôt que le cours d'un jour : un ordre de bourse ne doit pas faire le
/// salaire du président.
/// </para>
/// </summary>
public sealed class CeoPayDef
{
    public bool Enabled { get; set; }

    /// <summary>Salaire annuel de base, à une performance égale à la cible.</summary>
    public decimal BaseAnnual { get; set; } = 12_000m;

    /// <summary>Intervalle entre deux versements, en ticks.</summary>
    public int IntervalTicks { get; set; } = 30;

    /// <summary><c>marketValue</c> (capitalisation moyenne de l'exercice) ou <c>bookEquity</c> (capitaux propres d'ouverture).</summary>
    public string PerformanceBase { get; set; } = "marketValue";

    /// <summary>Performance annuelle attendue, en pourcentage de la valeur : à ce niveau, le salaire vaut la base.</summary>
    public decimal TargetPercent { get; set; } = 8m;

    /// <summary>
    /// Sensibilité : variation relative du salaire par unité de performance au-dessus
    /// ou au-dessous de la cible. 10 = un point de performance de plus, 10 % de salaire
    /// en plus l'année suivante.
    /// </summary>
    public decimal Sensitivity { get; set; } = 10m;

    /// <summary>Bornes du facteur appliqué à la base.</summary>
    public decimal MinFactor { get; set; } = 0.5m;
    public decimal MaxFactor { get; set; } = 2m;

    /// <summary>Surperformance : au-delà de cette performance annuelle, en pourcentage, une prime tombe.</summary>
    public decimal BonusThresholdPercent { get; set; } = 12m;

    /// <summary>La prime : ce pourcentage du résultat obtenu au-delà du seuil de surperformance.</summary>
    public decimal BonusSharePercent { get; set; } = 2m;
}
