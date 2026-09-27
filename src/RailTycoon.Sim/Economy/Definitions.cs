namespace RailTycoon.Sim.Economy;

// Ces classes sont les données de conception, chargées depuis data/*.json.
// Elles sont mutables uniquement pour la désérialisation ; la simulation ne les
// modifie jamais en cours de partie. Toute valeur équilibrable doit vivre ici,
// pas en dur dans le code : c'est ce qui permet d'itérer sur l'économie sans
// recompiler, et à un designer de travailler sans toucher au C#.

/// <summary>Une marchandise transportable.</summary>
public sealed class CargoDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>Prix de référence, celui d'un marché à l'équilibre.</summary>
    public double BasePrice { get; set; } = 10.0;

    /// <summary>
    /// Élasticité de la demande au prix. 0 = demande rigide (le charbon en
    /// hiver), 1 = très élastique (un produit de luxe).
    /// </summary>
    public double Elasticity { get; set; } = 0.6;
}

public sealed class IngredientDef
{
    public string Cargo { get; set; } = "";
    public double Qty { get; set; }
}

/// <summary>Une recette de transformation : le maillon d'une chaîne de production.</summary>
public sealed class RecipeDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<IngredientDef> Inputs { get; set; } = new();
    public List<IngredientDef> Outputs { get; set; } = new();

    /// <summary>Nombre de fois que la recette peut tourner par tick, pour une capacité de 1.</summary>
    public double RatePerTick { get; set; } = 0.5;

    /// <summary>
    /// Marge minimale exigée pour que l'usine tourne. En dessous, elle se met à
    /// l'arrêt : c'est ce qui force le joueur à approvisionner en intrants bon
    /// marché plutôt qu'à compter sur une production automatique.
    /// </summary>
    public double MinMargin { get; set; } = 0.15;
}

/// <summary>Une usine implantée dans une ville.</summary>
public sealed class IndustryDef
{
    public string Recipe { get; set; } = "";
    public double Capacity { get; set; } = 1.0;
}

public sealed class CityDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double Population { get; set; } = 10_000;

    /// <summary>Consommation de base, en chargements par tick, par marchandise.</summary>
    public Dictionary<string, double> Demand { get; set; } = new();

    /// <summary>Production primaire (ferme, mine, forêt), en chargements par tick.</summary>
    public Dictionary<string, double> Production { get; set; } = new();

    public List<IndustryDef> Industries { get; set; } = new();

    /// <summary>Stock initial par marchandise. Absent = 0.</summary>
    public Dictionary<string, double> InitialStock { get; set; } = new();
}

public sealed class StopDef
{
    public string City { get; set; } = "";

    /// <summary>Distance cumulée depuis l'origine de la ligne, en kilomètres.</summary>
    public double DistanceKm { get; set; }
}

public sealed class LineDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<StopDef> Stops { get; set; } = new();
}

public sealed class TrainDef
{
    public string Id { get; set; } = "";
    public string Line { get; set; } = "";

    /// <summary>Capacité en chargements.</summary>
    public double Capacity { get; set; } = 24;

    public double SpeedKmPerTick { get; set; } = 120;

    /// <summary>Coût d'exploitation par kilomètre parcouru, à vide comme en charge.</summary>
    public double CostPerKm { get; set; } = 0.8;

    /// <summary>
    /// Arrêt de départ, en index dans la ligne. Décaler les trains les uns par
    /// rapport aux autres évite qu'ils prennent tous les mêmes décisions au même
    /// endroit au même moment, ce qui n'apprendrait rien de plus qu'un seul train.
    /// </summary>
    public int StartStop { get; set; }
}

/// <summary>Paramètres du modèle de prix. Le cœur de l'équilibrage.</summary>
public sealed class PriceModelDef
{
    /// <summary>
    /// Horizon de couverture, en ticks. Un stock couvrant exactement cet horizon
    /// de consommation donne le prix de référence.
    /// </summary>
    public double CoverageHorizonTicks { get; set; } = 30;

    /// <summary>
    /// Raideur de la courbe. Plus la valeur est basse, plus le prix réagit
    /// violemment à la pénurie.
    /// </summary>
    public double Shape { get; set; } = 0.5;
    public double MinMultiplier { get; set; } = 0.12;
    public double MaxMultiplier { get; set; } = 3.0;
}

/// <summary>Paramètres du solveur économique de référence.</summary>
public sealed class EconomyDef
{
    /// <summary>
    /// Capacité de stockage d'un site de production, exprimée en ticks de son
    /// propre débit. Un site produit à plein régime jusqu'à la moitié de son
    /// entrepôt, puis ralentit, et s'arrête presque quand il est plein.
    /// <para>
    /// C'est le bon régulateur pour une ferme ou une mine : ce qui arrête une mine
    /// n'est pas le prix local, c'est un carreau encombré faute de wagons. Brider
    /// la production sur la couverture locale — comme le faisait la première
    /// version — punit précisément les sites que personne ne dessert encore, alors
    /// que ce sont eux que le joueur doit avoir envie d'aller chercher.
    /// </para>
    /// </summary>
    public double ProductionStorageTicks { get; set; } = 30;

    /// <summary>Débit résiduel d'un site dont l'entrepôt est plein.</summary>
    public double MinProductionFactor { get; set; } = 0.1;

    /// <summary>Bornes de la réponse de la consommation au prix.</summary>
    public double MinConsumptionResponse { get; set; } = 0.3;
    public double MaxConsumptionResponse { get; set; } = 1.5;

    /// <summary>
    /// Couverture qu'une ville conserve pour ses propres besoins avant de céder
    /// quoi que ce soit. Seul l'excédent au-delà est achetable par un train.
    /// <para>
    /// Sans cette notion, une ville revend ce qu'on vient de lui livrer. Le
    /// transporteur déchargeait ses vingt-quatre chargements, le prix local
    /// s'effondrait, il les rachetait aussitôt au même arrêt puisque la ville
    /// suivante était encore en pénurie, et il encaissait une marge à chaque
    /// tronçon. La compagnie a gagné deux millions pendant que toutes les usines
    /// étaient à l'arrêt et tous les marchés au plafond : le fret tournait en
    /// rond sans jamais nourrir personne.
    /// </para>
    /// <para>
    /// Un site sans consommation locale — une ferme, une mine, un moulin vis-à-vis
    /// de sa farine — n'a rien à conserver et cède tout son stock.
    /// </para>
    /// </summary>
    public double RetainedCoverage { get; set; } = 1.0;
}

/// <summary>Paramètres du transporteur de référence.</summary>
public sealed class HaulageDef
{
    /// <summary>Marge minimale exigée avant d'acheter ou de vendre.</summary>
    public double MinMargin { get; set; } = 0.10;

    /// <summary>
    /// Nombre de tranches utilisées pour appliquer l'impact d'un gros échange sur
    /// le prix local. 1 = pas d'impact (le train vend tout au prix affiché),
    /// au-delà le prix se dégrade au fur et à mesure du déchargement.
    /// </summary>
    public int PriceImpactSlices { get; set; } = 8;

    /// <summary>En dessous de cette quantité, on ne se déplace pas pour si peu.</summary>
    public double MinTradeQty { get; set; } = 0.25;

    /// <summary>
    /// Marge absolue minimale par chargement, en plus de la marge relative. Une
    /// marge de 10 % sur une marchandise à 2 ne paie pas le voyage : sans plancher
    /// absolu, le transporteur s'épuise en micro-arbitrages déficitaires.
    /// </summary>
    public double MinProfitPerUnit { get; set; } = 1.5;

    /// <summary>
    /// Taux de remplissage escompté, utilisé pour imputer le coût kilométrique à
    /// un chargement. Un train paie ses kilomètres qu'il soit plein ou vide :
    /// répartir le coût sur une capacité théoriquement pleine sous-estime
    /// systématiquement le coût réel et fait paraître rentables des trajets qui ne
    /// le sont pas.
    /// </summary>
    public double ExpectedLoadFactor { get; set; } = 0.6;
}

public sealed class ScenarioDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public ulong Seed { get; set; } = 1;
    public double StartingCash { get; set; } = 100_000;

    /// <summary>
    /// Couverture initiale appliquée aux marchés qui ne déclarent pas de stock
    /// explicite. 1 = chaque ville démarre au prix de référence. 0 désactive.
    /// </summary>
    public double InitialCoverage { get; set; }

    public PriceModelDef PriceModel { get; set; } = new();
    public EconomyDef Economy { get; set; } = new();
    public HaulageDef Haulage { get; set; } = new();
    public List<CargoDef> Cargos { get; set; } = new();
    public List<RecipeDef> Recipes { get; set; } = new();
    public List<CityDef> Cities { get; set; } = new();
    public List<LineDef> Lines { get; set; } = new();
    public List<TrainDef> Trains { get; set; } = new();
}
