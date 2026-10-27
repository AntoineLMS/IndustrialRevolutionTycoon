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

    /// <summary>
    /// Type de wagon qui porte un chargement de cette marchandise, parmi ceux de
    /// <see cref="MassCostDef.WagonTypes"/> : <c>box</c> (couvert), <c>flat</c>
    /// (plat), <c>gondola</c> (tombereau), <c>tank</c> (citerne).
    /// <para>
    /// Le wagon suit le chargement : il est attelé quand la marchandise est achetée,
    /// dételé quand elle est vendue. Sa tare fait donc partie de ce que coûte un
    /// chargement à porter, au même titre que la marchandise elle-même, et c'est
    /// elle qui distingue deux marchandises de même prix sous le modèle de coût
    /// <c>mass</c> : un chargement léger à porter voyage plus loin.
    /// </para>
    /// <para>
    /// Le défaut, le wagon couvert, est le wagon générique de l'époque ; les
    /// scénarios livrés déclarent tous le leur, et un test le vérifie.
    /// </para>
    /// </summary>
    public string Wagon { get; set; } = "box";
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

    /// <summary>
    /// Position de l'usine par rapport au centre de sa ville, en kilomètres. Au
    /// centre par défaut. Voir <see cref="SiteOffsetDef"/>.
    /// </summary>
    public double DxKm { get; set; }
    public double DyKm { get; set; }
}

/// <summary>
/// Position d'une installation par rapport au centre de sa ville, en kilomètres.
/// <para>
/// Chaque installation — une forêt, une mine, une usine — a son propre marché : la
/// forêt vend ses grumes, la scierie achète les siennes, et rien ne passe de l'une à
/// l'autre sans un train ou une charrette (<see cref="CartageDef"/>), même dans la
/// même ville. Leur position décide quelle gare les dessert
/// (<see cref="HaulageDef.StationRadiusKm"/>) et ce que coûte le charroi entre elles.
/// </para>
/// </summary>
public sealed class SiteOffsetDef
{
    public double DxKm { get; set; }
    public double DyKm { get; set; }
}

public sealed class CityDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double Population { get; set; } = 10_000;

    /// <summary>Consommation de base, en chargements par tick, par marchandise.</summary>
    public Dictionary<string, double> Demand { get; set; } = new();

    /// <summary>
    /// Production primaire (ferme, mine, forêt), en chargements par tick. Chaque
    /// marchandise produite est une installation à part, avec son entrepôt et son
    /// marché.
    /// </summary>
    public Dictionary<string, double> Production { get; set; } = new();

    /// <summary>
    /// Position des installations de production primaire, par marchandise. Une
    /// marchandise absente est produite au centre de la ville.
    /// </summary>
    public Dictionary<string, SiteOffsetDef> ProductionSites { get; set; } = new();

    public List<IndustryDef> Industries { get; set; } = new();

    /// <summary>
    /// Stock initial par marchandise. Absent = 0. Il est posé dans l'entrepôt de la
    /// production primaire si la ville produit la marchandise, sur la place de la
    /// ville sinon.
    /// </summary>
    public Dictionary<string, double> InitialStock { get; set; } = new();

    /// <summary>
    /// Position du centre de la ville, en kilomètres, sur la carte du réseau. Absente,
    /// elle est celle du premier nœud du réseau rattaché à la ville, ou à défaut celle
    /// de son premier arrêt sur une ligne saisie à la main (l'arrêt à
    /// <c>distanceKm</c> est au point (distanceKm, 0)).
    /// </summary>
    public double? XKm { get; set; }
    public double? YKm { get; set; }
}

public sealed class StopDef
{
    public string City { get; set; } = "";

    /// <summary>Distance cumulée depuis l'origine de la ligne, en kilomètres.</summary>
    public double DistanceKm { get; set; }

    /// <summary>
    /// Rayon d'action de la gare, en kilomètres. Absent, celui de
    /// <see cref="HaulageDef.StationRadiusKm"/>.
    /// </summary>
    public double? RadiusKm { get; set; }
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

    /// <summary>
    /// Identifiant optionnel d'une locomotive du catalogue (data/locomotives.json,
    /// voir <see cref="LocomotiveDef"/>). Purement informatif pour l'instant : la
    /// simulation continue de lire <see cref="SpeedKmPerTick"/> et
    /// <see cref="CostPerKm"/> directement sur le train, quelle que soit la
    /// locomotive référencée. Ce champ est additif et n'est pas validé au
    /// chargement du scénario — un futur module pourra s'en servir pour dériver
    /// vitesse et coût automatiquement.
    /// </summary>
    public string? Locomotive { get; set; }
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

    /// <summary>
    /// Prix, en multiple du prix de référence, d'une marchandise chez un producteur
    /// qui ne la consomme pas, quand son entrepôt est vide. Entrepôt plein, elle
    /// vaut <see cref="MinMultiplier"/> ; entre les deux, le prix suit le
    /// remplissage en ligne droite : plus il y en a, moins c'est cher.
    /// <para>
    /// Le producteur n'a pas de consommation qui donnerait une couverture, mais il a
    /// un entrepôt (<see cref="EconomyDef.ProductionStorageTicks"/>), et c'est lui
    /// qui dit si la marchandise abonde. Sans cette règle, une mine vidée par les
    /// trains vendait son dernier chargement au prix du premier, et l'occasion ne se
    /// refermait qu'à l'arrivée. 0,5 reste sous le prix d'un acheteur à l'équilibre
    /// (1) : un producteur sollicité devient cher, il ne devient pas un acheteur.
    /// Décision de l'équipe du 27 octobre 2026.
    /// </para>
    /// </summary>
    public double ProducerMaxMultiplier { get; set; } = 0.5;
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

    /// <summary>Le charroi, entre tous les marchés de la carte. Voir <see cref="CartageDef"/>.</summary>
    public CartageDef Cartage { get; set; } = new();
}

/// <summary>
/// Le charroi : ce qui passe sans train, par charrettes, d'un marché à n'importe quel
/// autre — de la forêt à la scierie d'une même ville, ou d'une ville à sa voisine.
/// <para>
/// Tout peut passer par charrette, sans limite de quantité ; c'est seulement plus lent
/// et plus cher qu'un train. Une charrette part du marché le moins cher vers le marché
/// acheteur le plus cher tant que l'écart de prix paie le trajet —
/// <see cref="CostPerLoad"/> plus <see cref="CostPerLoadKm"/> par kilomètre de route —,
/// en comptant à l'arrivée ce qui y roule déjà. La route suit le terrain : sur une
/// carte à relief, la charrette prend le chemin le moins pénible, où chaque mètre
/// gravi compte <see cref="ClimbKmPerMetre"/> kilomètre de plus, où une pente au-delà
/// de <see cref="MaxGradePercent"/> ne passe pas, et où franchir un fleuve à gué
/// compte <see cref="RiverCrossingKm"/> : elle longe les vallées et les fleuves, et
/// passe les cols. Sans relief déclaré, elle va en ligne droite. Elle avance de
/// <see cref="SpeedKmPerTick"/> par jour, et la marchandise n'arrive qu'au bout du
/// trajet. Le charroi ne coûte rien à la compagnie : la marchandise n'appartient à
/// personne, et le coût n'est qu'un seuil. Une charrette qui passe dans le rayon d'une
/// gare peut être captée par un train à l'arrêt, au prix qu'elle coûte à la gare : son
/// prix de départ, plus le charroi déjà parcouru. Décisions de l'équipe du 27 octobre
/// 2026.
/// </para>
/// </summary>
public sealed class CartageDef
{
    /// <summary>Kilomètres parcourus par jour. 15 : une charrette lourde sur les routes de l'époque, huit fois plus lente qu'un train.</summary>
    public double SpeedKmPerTick { get; set; } = 15;

    /// <summary>Coût d'une charrette par chargement, quelle que soit la distance.</summary>
    public double CostPerLoad { get; set; } = 1.0;

    /// <summary>Coût d'une charrette par chargement et par kilomètre de route, montées et gués compris.</summary>
    public double CostPerLoadKm { get; set; } = 0.5;

    /// <summary>
    /// Kilomètres de route que vaut un mètre gravi. Une montée ralentit la charrette
    /// autant qu'elle la renchérit : la vitesse et le coût se comptent sur la même
    /// distance. 0,02 : cent mètres de montée valent deux kilomètres de plat
    /// [estimé, docs/SOURCES.md, « Routes des charrettes »]. La descente ne coûte rien
    /// de plus.
    /// </summary>
    public double ClimbKmPerMetre { get; set; } = 0.02;

    /// <summary>Pente, en pourcent, au-delà de laquelle une charrette chargée ne passe pas [estimé].</summary>
    public double MaxGradePercent { get; set; } = 10;

    /// <summary>
    /// Kilomètres de route que vaut le passage d'un fleuve à gué : le détour jusqu'au
    /// gué, l'attente, le déchargement [estimé]. Une charrette ne franchit donc un
    /// fleuve qu'une fois, et le longe sinon.
    /// </summary>
    public double RiverCrossingKm { get; set; } = 5;

    /// <summary>
    /// Chargements par charrette. Les départs se décident charrette par charrette : le
    /// prix est réévalué entre deux, à l'arrivée comme au départ, si bien que la
    /// première part vers le marché le plus cher et la suivante vers celui qui l'est
    /// devenu.
    /// </summary>
    public double LoadsPerCart { get; set; } = 0.5;

    /// <summary>
    /// Garde-fou : départs au plus par marchandise et par jour, sur toute la carte. Il
    /// ne doit jamais mordre en régime normal ; il empêche une donnée absurde de faire
    /// boucler un tick.
    /// </summary>
    public int MaxCartsPerTick { get; set; } = 400;
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
    /// Rayon d'action d'une gare, en kilomètres, quand l'arrêt ne déclare pas le sien.
    /// Un train à l'arrêt achète et vend sur tous les marchés à cette distance de la
    /// gare au plus — la place d'une ville et les installations qui l'entourent —, et
    /// sur aucun autre (VISION.md, « Les ordres de train et le chargement »). Les
    /// installations sont au centre de leur ville par défaut : à 3 km, une gare de
    /// centre-ville les dessert toutes.
    /// </summary>
    public double StationRadiusKm { get; set; } = 3.0;

    /// <summary>
    /// Taux de remplissage escompté, utilisé pour imputer le coût kilométrique à
    /// un chargement. Un train paie ses kilomètres qu'il soit plein ou vide :
    /// répartir le coût sur une capacité théoriquement pleine sous-estime
    /// systématiquement le coût réel et fait paraître rentables des trajets qui ne
    /// le sont pas.
    /// <para>
    /// Lu par le seul modèle de coût <c>flat</c>. C'est lui, et non la distance,
    /// qui fabrique le rayon économique de ce modèle (docs/FINDINGS.md, « Le rayon
    /// économique est un réglage de l'instrument ») ; le modèle <c>mass</c> ne le
    /// lit pas, parce qu'il ne répartit aucun coût : il décide sur ce qu'un
    /// chargement ajoute réellement à la facture.
    /// </para>
    /// </summary>
    public double ExpectedLoadFactor { get; set; } = 0.6;

    /// <summary>
    /// Modèle de coût d'exploitation des trains : <c>mass</c> (défaut) ou
    /// <c>flat</c>. Voir <c>Transport/TrainCost.cs</c>.
    /// <para>
    /// <c>mass</c> — le coût kilométrique est proportionnel à la masse remorquée :
    /// la locomotive, plus chaque chargement avec son wagon. Les deux sont
    /// multipliés par le facteur de relief du tronçon et du sens. Le transporteur
    /// décide d'acheter sur le seul surcoût qu'un chargement ajoute à cette facture,
    /// relief compris : refuser un chargement économise exactement ce qu'il aurait
    /// coûté. Défaut depuis le 27 octobre 2026 ; voir docs/FINDINGS.md, « Le coût
    /// marginal réel » et « Le coût marginal réel par défaut ».
    /// </para>
    /// <para>
    /// <c>flat</c> — l'ancien défaut, conservé pour relire les mesures antérieures :
    /// le train paie <c>costPerKm</c> par kilomètre, relief compris, qu'il soit plein
    /// ou vide, et le transporteur impute à un chargement ce coût réparti sur
    /// <see cref="ExpectedLoadFactor"/> de sa capacité, sur la distance à plat.
    /// </para>
    /// </summary>
    public string CostModel { get; set; } = "mass";

    /// <summary>Masses du modèle <c>mass</c>. Ignorées par le modèle <c>flat</c>.</summary>
    public MassCostDef MassCost { get; set; } = new();
}

/// <summary>
/// Un type de wagon du modèle de coût <c>mass</c>. Seule sa tare le distingue : la
/// charge d'un wagon est celle d'un chargement du jeu,
/// <see cref="MassCostDef.TonnesPerLoad"/>, quel que soit le wagon.
/// </summary>
public sealed class WagonTypeDef
{
    public string Name { get; set; } = "";

    /// <summary>Masse du wagon vide, en tonnes.</summary>
    public double TareTonnes { get; set; }
}

/// <summary>
/// Masses du modèle de coût <c>mass</c> (<see cref="HaulageDef.CostModel"/>), et
/// la calibration qui relie le coût à la tonne au <c>costPerKm</c> de chaque train.
/// <para>
/// Un chargement est un wagon, et <b>le wagon suit le chargement</b> : un train vide
/// n'est que sa locomotive et son tender ; chaque chargement acheté y attelle un
/// wagon du type que déclare sa marchandise (<see cref="CargoDef.Wagon"/>), qui est
/// dételé à la vente. La part fixe d'un train est donc sa seule locomotive, et la
/// part de chaque chargement est sa marchandise plus la tare de son wagon. Les
/// valeurs par défaut sont celles d'un train de marchandises américain des années
/// 1870 ; sources et estimations dans docs/SOURCES.md, « Masses du train ».
/// </para>
/// </summary>
public sealed class MassCostDef
{
    /// <summary>Locomotive et tender, en tonnes. Dus que le train soit plein ou vide.</summary>
    public double LocomotiveTonnes { get; set; } = 48;

    /// <summary>Masse de marchandise d'un chargement, en tonnes, quel que soit son wagon.</summary>
    public double TonnesPerLoad { get; set; } = 9;

    /// <summary>
    /// Les types de wagons, par identifiant. Chaque marchandise du scénario doit
    /// nommer l'un d'eux ; un scénario qui déclare ce bloc remplace la liste entière.
    /// </summary>
    public Dictionary<string, WagonTypeDef> WagonTypes { get; set; } = new()
    {
        ["box"] = new WagonTypeDef { Name = "Wagon couvert", TareTonnes = 9 },
        ["flat"] = new WagonTypeDef { Name = "Wagon plat", TareTonnes = 6 },
        ["gondola"] = new WagonTypeDef { Name = "Tombereau", TareTonnes = 7 },
        ["tank"] = new WagonTypeDef { Name = "Wagon-citerne", TareTonnes = 8 },
    };

    /// <summary>
    /// Le wagon de la calibration : celui que porte le train de référence décrit par
    /// <see cref="CalibrationLoadFactor"/>. Le wagon couvert, le plus répandu de
    /// l'époque, et le plus lourd des types livrés.
    /// </summary>
    public string CalibrationWagon { get; set; } = "box";

    /// <summary>
    /// Remplissage, en fraction de la capacité, auquel le train coûte exactement son
    /// <c>costPerKm</c> s'il porte des wagons de <see cref="CalibrationWagon"/>.
    /// C'est une conversion d'unité : elle fixe le prix de la tonne-kilomètre pour
    /// qu'à la charge moyenne mesurée le train coûte ce qu'il coûtait dans le modèle
    /// <c>flat</c>, et qu'on compare deux façons de décider sur une même économie.
    /// Plus elle est haute, moins la tonne coûte : le train vide et chaque
    /// chargement paient moins.
    /// <para>
    /// Elle n'a pas le rôle de <see cref="HaulageDef.ExpectedLoadFactor"/>. Celui-là
    /// divise tout le coût du train, part fixe comprise, par une charge escomptée pour
    /// en imputer une part à chaque chargement : c'est un coût moyen, et refuser un
    /// chargement n'économise pas ce qu'on lui a imputé. Celle-ci ne fait que fixer
    /// le prix de la tonne ; la décision lit la dérivée de la facture par rapport à
    /// la charge, qui dépend de ce prix — donc de cette calibration — mais d'aucune
    /// charge escomptée au moment de décider. Elle se mesure, elle ne se règle pas :
    /// c'est le point fixe sur heartland, sous la référence, où le coût facturé aux
    /// trains égale celui du modèle <c>flat</c> (docs/FINDINGS.md, « Le coût
    /// marginal réel par défaut »). Elle se remesure à chaque changement de masses
    /// ou de <c>costPerKm</c>.
    /// </para>
    /// </summary>
    public double CalibrationLoadFactor { get; set; } = 0.77;
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

    /// <summary>
    /// Réglages propres à <see cref="AnticipatingEconomySolver"/>. Absent du
    /// scénario, ses défauts neutres le font se comporter comme la référence.
    /// </summary>
    public AnticipatingEconomyDef Anticipating { get; set; } = new();
    public HaulageDef Haulage { get; set; } = new();

    /// <summary>
    /// Le réseau ferré : relief, graphe de voies, coûts de construction. Sans nœud
    /// déclaré, le scénario reste sur ses <see cref="Lines"/> aux distances saisies
    /// à la main. Voir RailTycoon.Sim.Network.RailNetworkDef.
    /// </summary>
    public RailTycoon.Sim.Network.RailNetworkDef Network { get; set; } = new();

    /// <summary>Configuration du module finance. Voir <c>Finance/FinanceDefinitions.cs</c> ; inactive par défaut.</summary>
    public RailTycoon.Sim.Finance.FinanceDef Finance { get; set; } = new();

    /// <summary>
    /// Événements historiques et aléatoires. Voir <c>Events/EventDefinitions.cs</c> ;
    /// inactif par défaut, et neutre au bit près tant qu'il l'est.
    /// </summary>
    public RailTycoon.Sim.Events.EventsDef Events { get; set; } = new();
    public List<CargoDef> Cargos { get; set; } = new();
    public List<RecipeDef> Recipes { get; set; } = new();
    public List<CityDef> Cities { get; set; } = new();
    public List<LineDef> Lines { get; set; } = new();
    public List<TrainDef> Trains { get; set; } = new();
}
