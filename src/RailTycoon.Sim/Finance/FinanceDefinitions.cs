namespace RailTycoon.Sim.Finance;

// Données de conception du module finance, chargées depuis data/*.json comme le
// reste du scénario. Aucune de ces valeurs ne doit apparaître en dur dans le
// code : ce sont toutes des réglages d'équilibrage, et un taux d'emprunt ou un
// taux de distribution se règle en jouant, pas en recompilant.
//
// Les montants sont en decimal — voir Money pour la raison. System.Text.Json
// désérialise un nombre JSON en decimal sans passer par un double, l'exactitude
// est donc préservée depuis le fichier.

/// <summary>Un emprunt obligataire proposé au joueur.</summary>
public sealed class BondOfferDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Principal { get; set; } = 50_000m;

    /// <summary>Taux annuel en pourcentage. Annuel parce que c'est ainsi qu'un emprunt se lit ; la conversion au tick est dans <see cref="Money.InterestForTick"/>.</summary>
    public decimal AnnualRatePercent { get; set; } = 6m;

    /// <summary>Durée en ticks. Le capital se rembourse en une fois à l'échéance.</summary>
    public int TermTicks { get; set; } = 360;

    /// <summary>Tick à partir duquel l'offre existe. Permet d'étaler l'accès au crédit sur la partie.</summary>
    public int AvailableFromTick { get; set; }
}

/// <summary>Politique de distribution de dividendes de la compagnie.</summary>
public sealed class DividendPolicyDef
{
    /// <summary>Intervalle entre deux distributions, en ticks. 0 désactive.</summary>
    public int IntervalTicks { get; set; } = 90;

    /// <summary>Part du résultat de la période distribuée aux actionnaires.</summary>
    public decimal PayoutRatio { get; set; } = 0.4m;

    /// <summary>
    /// Trésorerie que la compagnie refuse de descendre en distribuant. Sans ce
    /// matelas, une distribution laisse la société incapable de payer ses
    /// intérêts au tick suivant — ce qui est un choix stratégique possible, mais
    /// qui ne doit pas être le comportement par défaut.
    /// </summary>
    public decimal MinCashBuffer { get; set; } = 20_000m;
}

/// <summary>Modèle de valorisation d'une compagnie et de son action.</summary>
public sealed class ValuationDef
{
    /// <summary>Multiple appliqué au résultat annualisé, en plus de l'actif net comptable.</summary>
    public decimal EarningsMultiple { get; set; } = 8m;

    /// <summary>Vitesse de convergence du cours vers sa valeur intrinsèque, dans ]0, 1].</summary>
    public decimal PriceSmoothing { get; set; } = 0.08m;

    /// <summary>Lissage du résultat par tick servant à annualiser, dans ]0, 1].</summary>
    public decimal EarningsSmoothing { get; set; } = 0.02m;

    /// <summary>
    /// Plancher du cours. Une action ne tombe pas à zéro : elle garde une valeur
    /// d'option. Surtout, un cours nul rendrait gratuite la prise de contrôle
    /// d'une société ruinée, ce qui n'a pas de sens et casserait la valorisation
    /// des participations.
    /// </summary>
    public decimal MinSharePrice { get; set; } = 0.5m;

    /// <summary>
    /// Impact d'un ordre sur le cours : acheter une fraction <c>f</c> du capital
    /// déplace le cours de <c>f × ce facteur</c>. 0 = marché infiniment liquide.
    /// <para>
    /// C'est la transposition à la bourse de la propriété qui fait le jeu côté
    /// fret : livrer sur un marché fait baisser le prix qu'on exploitait. Sans
    /// impact, le flottant absorbe n'importe quel volume au cours affiché, et le
    /// magnat encaisse l'écart entre le cours lissé et la valeur intrinsèque
    /// autant de fois qu'il le veut — la version bourse du lavage de fret, avec la
    /// même signature : un enrichissement lisse, monotone, que rien ne referme.
    /// </para>
    /// </summary>
    public decimal MarketImpact { get; set; }
}

/// <summary>
/// Le découvert bancaire et l'administration judiciaire : ce qui arrive à une
/// compagnie dont la trésorerie d'exploitation passe sous zéro.
/// </summary>
public sealed class OverdraftDef
{
    /// <summary>
    /// Taux annuel du découvert, en pourcentage. Délibérément punitif par rapport
    /// aux obligations : un découvert subi doit coûter plus cher qu'un emprunt
    /// choisi, sinon le joueur n'a aucune raison de préférer l'emprunt.
    /// </summary>
    public decimal AnnualRatePercent { get; set; } = 14m;

    /// <summary>Plafond absolu du découvert, quelle que soit la solidité du bilan.</summary>
    public decimal MaxFacility { get; set; } = 60_000m;

    /// <summary>
    /// Part des capitaux propres que la banque accepte de gager. C'est ce qui lie
    /// la capacité à survivre à un mauvais trimestre à la solidité du bilan, et
    /// c'est aussi ce qui donne un effet mécanique au renflouement par le magnat :
    /// son apport augmente les capitaux propres, donc le découvert autorisé, donc
    /// la capacité des trains à continuer d'acheter du fret.
    /// </summary>
    public decimal PledgeRatio { get; set; } = 0.35m;
}

/// <summary>Politique du magnat : ce qu'il fait de sa caisse personnelle.</summary>
public sealed class TycoonPolicyDef
{
    /// <summary>Caisse personnelle au début de la partie. Distincte de celle de la société : c'est tout l'intérêt.</summary>
    public decimal StartingCash { get; set; } = 25_000m;

    /// <summary>Actions de sa propre compagnie détenues au départ.</summary>
    public long StartingShares { get; set; } = 30_000;

    /// <summary>Intervalle entre deux décisions de bourse, en ticks. 0 désactive.</summary>
    public int TradeIntervalTicks { get; set; } = 30;

    /// <summary>Part de sa caisse personnelle qu'il engage à chaque achat.</summary>
    public decimal BuyBudgetFraction { get; set; } = 0.5m;

    /// <summary>
    /// Part d'un achat qu'il finance à crédit. 0 = pas d'achat sur marge, 0,5 =
    /// il double sa force de frappe en empruntant autant qu'il apporte.
    /// </summary>
    public decimal MarginInitialRatio { get; set; } = 0.5m;

    /// <summary>
    /// Ratio dette / valeur du portefeuille au-delà duquel l'appel de marge
    /// tombe : le courtier liquide d'office. C'est le mécanisme qui rend l'achat
    /// à crédit dangereux et donc intéressant.
    /// </summary>
    public decimal MarginMaintenanceRatio { get; set; } = 0.7m;

    public decimal MarginAnnualRatePercent { get; set; } = 9m;

    /// <summary>Il vend quand le cours dépasse son prix de revient moyen de ce facteur.</summary>
    public decimal SellAboveCostRatio { get; set; } = 1.6m;

    /// <summary>Part de sa ligne qu'il allège alors.</summary>
    public decimal SellFraction { get; set; } = 0.25m;

    /// <summary>En dessous de ce nombre d'actions, on ne passe pas d'ordre.</summary>
    public long MinLotShares { get; set; } = 100;

    /// <summary>
    /// Le magnat souscrit-il à une augmentation de capital quand sa société
    /// s'enfonce ? C'est <em>la</em> décision du genre, et elle doit pouvoir se
    /// jouer dans les deux sens : renflouer sa compagnie avec son argent
    /// personnel, ou la laisser couler en ayant sorti le sien à temps. Les deux
    /// branches sont testées.
    /// </summary>
    public bool RescuesCompany { get; set; }

    /// <summary>Part de sa caisse personnelle qu'il accepte d'y remettre en une fois.</summary>
    public decimal RescueCashFraction { get; set; } = 0.5m;

    /// <summary>
    /// Décote consentie sur le cours à l'émission des actions nouvelles. Elle
    /// n'est pas une faveur : personne ne souscrit au cours du marché à une
    /// société en difficulté, et c'est cette décote qui dilue les autres
    /// actionnaires au profit de celui qui remet de l'argent.
    /// </summary>
    public decimal RescueDiscountPercent { get; set; } = 25m;
}

/// <summary>Une compagnie concurrente, pilotée par les données.</summary>
public sealed class RivalCompanyDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public long SharesIssued { get; set; } = 20_000;
    public decimal StartingCash { get; set; } = 40_000m;

    /// <summary>Valeur comptable de son matériel et de ses voies.</summary>
    public decimal FixedAssets { get; set; } = 60_000m;

    public decimal BondPrincipal { get; set; } = 30_000m;
    public decimal BondAnnualRatePercent { get; set; } = 7m;
    public int BondTermTicks { get; set; } = 540;

    /// <summary>Résultat d'exploitation par tick, avant intérêts. Peut être négatif : une compagnie mal gérée est une cible.</summary>
    public decimal EarningsPerTick { get; set; } = 40m;

    /// <summary>
    /// Amplitude relative du bruit sur ce résultat, tiré dans
    /// <see cref="Core.DeterministicRandom"/>. Un concurrent au résultat
    /// parfaitement constant rend son cours prévisible, donc l'arbitrage trivial.
    /// </summary>
    public decimal EarningsJitter { get; set; } = 0.3m;

    public decimal InitialSharePrice { get; set; } = 5m;
}

/// <summary>Politique de prise de participation puis d'offre publique d'achat.</summary>
public sealed class AcquisitionPolicyDef
{
    /// <summary>Identifiants des concurrents visés, dans l'ordre. Liste vide = la compagnie ne fait pas de bourse.</summary>
    public List<string> Targets { get; set; } = new();

    /// <summary>Intervalle entre deux achats de titres, en ticks. 0 désactive.</summary>
    public int StakeIntervalTicks { get; set; } = 30;

    /// <summary>Nombre d'actions achetées par tranche. Acheter d'un coup ferait l'économie de la montée au capital, qui est le mécanisme intéressant.</summary>
    public long StakeTrancheShares { get; set; } = 1_500;

    /// <summary>Trésorerie que la compagnie conserve quoi qu'il arrive avant d'acheter des titres.</summary>
    public decimal MinCashBuffer { get; set; } = 30_000m;

    /// <summary>Fraction du capital à partir de laquelle on contrôle la cible et où l'OPA se déclenche.</summary>
    public decimal ControlFraction { get; set; } = 0.5m;

    /// <summary>
    /// Prime payée au flottant lors de l'OPA. Elle n'est pas décorative :
    /// racheter les minoritaires au cours du marché n'emporterait aucune décision,
    /// et c'est ce surcoût qui rend la fusion un pari plutôt qu'une formalité.
    /// </summary>
    public decimal TenderPremiumPercent { get; set; } = 25m;

    /// <summary>Emprunt mobilisé quand la trésorerie ne suffit pas à l'OPA. Vide = pas de financement par la dette.</summary>
    public string FundingBondId { get; set; } = "";
}

/// <summary>
/// Configuration complète du module finance. Une seule propriété est ajoutée à
/// <see cref="Economy.ScenarioDef"/> — <c>Finance</c> — pour que ce module
/// n'entre pas en conflit de fusion avec les trois autres qui avancent en
/// parallèle.
/// </summary>
public sealed class FinanceDef
{
    /// <summary>Faux = le module ne fait rien du tout, pas même ouvrir un grand livre. Les scénarios existants restent inchangés.</summary>
    public bool Enabled { get; set; }

    public string CompanyId { get; set; } = "player";
    public string CompanyName { get; set; } = "Compagnie du joueur";

    /// <summary>Nombre d'actions émises par la compagnie du joueur.</summary>
    public long SharesIssued { get; set; } = 100_000;

    /// <summary>Valeur comptable du matériel roulant et des voies au départ. Ce que la compagnie possède en dehors de sa caisse.</summary>
    public decimal FixedAssetsAtStart { get; set; } = 150_000m;

    /// <summary>Amortissement annuel du matériel, en pourcentage de sa valeur d'origine.</summary>
    public decimal DepreciationAnnualPercent { get; set; } = 4m;

    /// <summary>Seuil de trésorerie en dessous duquel la compagnie mobilise un emprunt disponible.</summary>
    public decimal BorrowWhenCashBelow { get; set; } = 40_000m;

    /// <summary>Graine dérivée pour l'aléa du module. Une séquence dédiée évite que l'ajout de la finance ne décale le tirage des autres modules.</summary>
    public ulong RandomSequence { get; set; } = 7;

    public ValuationDef Valuation { get; set; } = new();
    public OverdraftDef Overdraft { get; set; } = new();
    public DividendPolicyDef Dividends { get; set; } = new();
    public TycoonPolicyDef Tycoon { get; set; } = new();
    public AcquisitionPolicyDef Acquisition { get; set; } = new();
    public List<BondOfferDef> BondOffers { get; set; } = new();
    public List<RivalCompanyDef> Rivals { get; set; } = new();
}
