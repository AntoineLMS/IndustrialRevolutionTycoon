using RailTycoon.Sim.Core;

namespace RailTycoon.Sim.Finance;

/// <summary>Identifiants des comptes du grand livre. Constantes plutôt que chaînes littérales : une faute de frappe devient une erreur de compilation.</summary>
public static class Accounts
{
    public const string Cash = "cash";
    public const string FixedAssets = "fixed-assets";
    public const string Investments = "investments";
    public const string Portfolio = "portfolio";

    public const string BondsPayable = "bonds-payable";

    /// <summary>
    /// Le découvert bancaire. Il porte au bilan, explicitement, ce que la
    /// trésorerie d'exploitation doit : une trésorerie négative silencieuse est
    /// exactement le mode de défaillance dont on ne veut pas.
    /// </summary>
    public const string Overdraft = "overdraft";
    public const string InterestPayable = "interest-payable";
    public const string AccruedPayable = "accrued-payable";
    public const string MarginLoan = "margin-loan";

    public const string ShareCapital = "share-capital";
    public const string RetainedEarnings = "retained-earnings";
    public const string PersonalEquity = "personal-equity";
}

/// <summary>Détenteurs conventionnels au registre des actions.</summary>
public static class Holders
{
    /// <summary>
    /// Le flottant : tous les actionnaires que la simulation ne modélise pas. Il
    /// est indispensable qu'il existe comme ligne du registre et non comme un
    /// « reste » calculé, sinon la somme des actions détenues ne peut pas être
    /// comparée au nombre d'actions émises — et c'est exactement l'invariant
    /// qu'on veut.
    /// </summary>
    public const string Float = "float";

    public const string Tycoon = "tycoon";
}

public sealed class ShareHolding
{
    public required string HolderId { get; init; }
    public long Shares;
}

/// <summary>
/// Registre des actions d'une compagnie. Les actions sont des entiers : il n'y a
/// aucune raison d'introduire un arrondi là où le monde réel n'en a pas, et une
/// action fractionnaire ferait réapparaître le problème que <see cref="Money"/>
/// élimine.
/// </summary>
public sealed class ShareRegister
{
    public long SharesIssued;

    private readonly List<ShareHolding> _holders = new();
    private readonly Dictionary<string, ShareHolding> _byId = new();

    /// <summary>Détenteurs dans leur ordre d'inscription. Ordre de parcours canonique.</summary>
    public IReadOnlyList<ShareHolding> Holders => _holders;

    public ShareHolding HolderOf(string holderId)
    {
        if (_byId.TryGetValue(holderId, out var existing)) return existing;
        var holding = new ShareHolding { HolderId = holderId };
        _holders.Add(holding);
        _byId[holderId] = holding;
        return holding;
    }

    public long HeldBy(string holderId) => _byId.TryGetValue(holderId, out var h) ? h.Shares : 0;

    /// <summary>Émission initiale. Seul endroit où le nombre d'actions émises augmente.</summary>
    public void Issue(long shares, string holderId)
    {
        if (shares <= 0) return;
        SharesIssued += shares;
        HolderOf(holderId).Shares += shares;
    }

    /// <summary>Cession de titres. Le total émis ne change pas : un marché secondaire ne crée pas d'actions.</summary>
    public void Transfer(string from, string to, long shares)
    {
        if (shares <= 0) return;
        var seller = HolderOf(from);
        if (seller.Shares < shares)
            throw new InvalidOperationException(
                $"{from} ne détient que {seller.Shares} actions, cession de {shares} impossible.");
        seller.Shares -= shares;
        HolderOf(to).Shares += shares;
    }

    /// <summary>Radiation du registre après fusion : les titres de l'absorbée n'existent plus.</summary>
    public void Retire()
    {
        foreach (var holder in _holders) holder.Shares = 0;
        SharesIssued = 0;
    }

    public long HeldTotal()
    {
        long total = 0;
        foreach (var holder in _holders) total += holder.Shares;
        return total;
    }
}

/// <summary>Une ligne de titres avec son prix de revient.</summary>
public sealed class SharePosition
{
    public required string CompanyId { get; init; }
    public long Shares;
    public decimal CostBasis;
}

/// <summary>
/// Portefeuille de titres tenu au prix de revient. Il n'est jamais réévalué au
/// cours du marché : une plus-value latente qui remonterait au bilan serait un
/// bénéfice non réalisé, donc de l'argent qu'on peut distribuer sans l'avoir
/// gagné. Le résultat n'apparaît qu'à la vente.
/// </summary>
public sealed class Portfolio
{
    private readonly List<SharePosition> _positions = new();
    private readonly Dictionary<string, SharePosition> _byId = new();

    /// <summary>Lignes dans leur ordre d'apparition. Ordre de parcours canonique.</summary>
    public IReadOnlyList<SharePosition> Positions => _positions;

    public SharePosition PositionOf(string companyId)
    {
        if (_byId.TryGetValue(companyId, out var existing)) return existing;
        var position = new SharePosition { CompanyId = companyId };
        _positions.Add(position);
        _byId[companyId] = position;
        return position;
    }

    public long SharesOf(string companyId) => _byId.TryGetValue(companyId, out var p) ? p.Shares : 0;

    public decimal CostBasisOf(string companyId) => _byId.TryGetValue(companyId, out var p) ? p.CostBasis : 0m;

    public void Add(string companyId, long shares, decimal cost)
    {
        var position = PositionOf(companyId);
        position.Shares += shares;
        position.CostBasis += cost;
    }

    /// <summary>
    /// Sortie de titres, au prix de revient moyen. Une liquidation totale sort
    /// exactement le prix de revient restant : sans ce cas particulier, l'arrondi
    /// du prorata laisserait un résidu de quelques centimes sur une ligne à zéro
    /// action, et ce résidu serait précisément la fuite qu'on cherche à interdire.
    /// </summary>
    public decimal Remove(string companyId, long shares)
    {
        var position = PositionOf(companyId);
        if (shares >= position.Shares)
        {
            decimal all = position.CostBasis;
            position.Shares = 0;
            position.CostBasis = 0m;
            return all;
        }
        decimal basisOut = Money.Round(position.CostBasis * shares / position.Shares);
        position.Shares -= shares;
        position.CostBasis -= basisOut;
        return basisOut;
    }

    public decimal CostBasisTotal()
    {
        decimal total = 0m;
        foreach (var position in _positions) total += position.CostBasis;
        return total;
    }
}

/// <summary>Un emprunt obligataire en cours. Capital remboursé en une fois à l'échéance, intérêts payés chaque tick.</summary>
public sealed class Bond
{
    public required string OfferId { get; init; }
    public required string Name { get; init; }
    public decimal Principal { get; init; }
    public decimal Outstanding;
    public decimal AnnualRatePercent { get; init; }

    /// <summary>
    /// Décomposition du taux à l'émission — taux facial, conjoncture, prime de risque,
    /// bonus du dirigeant. Nulle sans module cycle : le taux est alors le taux facial.
    /// </summary>
    public Cycle.CreditQuote? Quote { get; init; }

    public int IssuedTick { get; init; }
    public int MaturityTick { get; init; }
    public decimal InterestAccrued;
    public bool Alive => Outstanding > 0m;
}

/// <summary>
/// Une compagnie au sens financier : un grand livre, un registre d'actions, des
/// emprunts et un cours. La compagnie du joueur et les concurrents partagent la
/// même classe, ce qui garantit qu'une OPA n'a pas à traiter la cible comme un
/// objet de seconde classe — et que les invariants s'appliquent aux deux.
/// </summary>
public sealed class FinanceCompany
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required Ledger Book { get; init; }

    /// <summary>Non nul pour un concurrent : son comportement est piloté par ces données.</summary>
    public RivalCompanyDef? Rival { get; init; }

    public ShareRegister Register { get; } = new();
    public Portfolio Investments { get; } = new();
    public List<Bond> Bonds { get; } = new();

    /// <summary>
    /// Exploitations reprises par fusion, dans l'ordre des absorptions. Sans
    /// elles, une OPA ne rapporterait que la trésorerie et le matériel de la
    /// cible : son flux de résultat disparaîtrait à l'instant de la fusion, et
    /// racheter une compagnie rentable serait systématiquement une perte. Ce qu'on
    /// achète dans une OPA, c'est précisément ce flux.
    /// </summary>
    public List<RivalCompanyDef> AbsorbedOperations { get; } = new();

    public decimal SharePrice;

    /// <summary>Résultat par tick lissé, base de l'annualisation du cours.</summary>
    public decimal EarningsEma;

    /// <summary>Vrai une fois absorbée par une autre compagnie : son bilan est vide et son registre radié.</summary>
    public bool Merged;

    /// <summary>
    /// Sous administration : le découvert dépasse ce que les capitaux propres
    /// permettent de gager. Les trains ne roulent plus, les dividendes et les
    /// achats de titres sont suspendus.
    /// </summary>
    public bool InReceivership;

    /// <summary>Tick de la mise sous administration, -1 si cela n'est jamais arrivé.</summary>
    public int ReceivershipTick = -1;

    /// <summary>Nombre de mises sous administration. Plus d'une signifie qu'un renflouement a fonctionné puis n'a pas suffi.</summary>
    public int ReceivershipCount;

    /// <summary>Découvert autorisé au tick courant, gagé sur les capitaux propres.</summary>
    public decimal CreditFacility;

    // --- Cumuls servant aux invariants de dette. Ils ne sont pas cosmétiques :
    // c'est par eux qu'on vérifie qu'aucun intérêt ni aucun capital ne s'évapore.
    public decimal PrincipalIssuedTotal;
    public decimal PrincipalRepaidTotal;
    public decimal InterestAccruedTotal;
    public decimal InterestPaidTotal;
    public decimal DividendsPaidTotal;
    public decimal OperatingResultTotal;
    public decimal OverdraftInterestTotal;
    public decimal CapitalRaisedTotal;
    public long SharesIssuedInRescues;

    /// <summary>Report à nouveau au moment de la dernière distribution. Sert à mesurer le résultat de la période, pas celui de toute la partie.</summary>
    public decimal RetainedAtLastPayout;

    // --- Télémétrie du tick courant.
    public decimal OperatingResultThisTick;
    public decimal InterestThisTick;
    public decimal DividendThisTick;

    public void BeginTick()
    {
        OperatingResultThisTick = 0m;
        InterestThisTick = 0m;
        DividendThisTick = 0m;
    }

    public decimal Cash => Book[Accounts.Cash];
    public decimal Debt => Book[Accounts.BondsPayable];
    public decimal OverdraftBalance => Book[Accounts.Overdraft];
    public decimal UnpaidInterest => Book[Accounts.InterestPayable];
    public decimal BookEquity => Book.Equity;

    /// <summary>Capitalisation boursière : ce que vaut la compagnie pour un actionnaire.</summary>
    public decimal MarketCap => Register.SharesIssued * SharePrice;

    /// <summary>
    /// Valeur d'entreprise : ce que coûterait le rachat de l'ensemble, dettes
    /// reprises et trésorerie déduite. C'est le bon prix à comparer quand on
    /// envisage une OPA — une cible endettée est plus chère que sa seule
    /// capitalisation ne le suggère.
    /// </summary>
    public decimal EnterpriseValue =>
        MarketCap + Debt + OverdraftBalance + UnpaidInterest + Book[Accounts.AccruedPayable] - Cash;

    public decimal OutstandingPrincipal()
    {
        decimal total = 0m;
        foreach (var bond in Bonds) total += bond.Outstanding;
        return total;
    }
}

/// <summary>
/// Le magnat. Sa caisse est distincte de celle de la société, et c'est la
/// distinction qui fait le sel du genre : on peut ruiner la compagnie et
/// s'enrichir personnellement, en encaissant des dividendes qu'elle n'aurait pas
/// dû verser puis en vendant ses titres avant la chute.
/// </summary>
public sealed class Tycoon
{
    public required Ledger Book { get; init; }
    public Portfolio Portfolio { get; } = new();

    public decimal InterestAccruedTotal;
    public decimal InterestPaidTotal;
    public decimal DividendsReceivedTotal;
    public decimal RealizedResultTotal;
    public long SharesBoughtTotal;
    public long SharesSoldTotal;
    public int MarginCalls;

    public decimal Cash => Book[Accounts.Cash];
    public decimal MarginLoan => Book[Accounts.MarginLoan];
    public decimal PortfolioCost => Book[Accounts.Portfolio];

    /// <summary>
    /// Valeur du portefeuille aux cours du moment — hors bilan, justement parce
    /// qu'une plus-value latente n'est pas un actif comptable.
    /// </summary>
    public decimal PortfolioMarketValue(FinanceState state)
    {
        decimal total = 0m;
        foreach (var position in Portfolio.Positions)
        {
            var company = state.CompanyById(position.CompanyId);
            if (company is null) continue;
            total += position.Shares * company.SharePrice;
        }
        return total;
    }

    /// <summary>Fortune personnelle du magnat : ce que le joueur cherche vraiment à maximiser.</summary>
    public decimal NetWorth(FinanceState state)
        => Cash + PortfolioMarketValue(state) - MarginLoan - Book[Accounts.AccruedPayable];
}

/// <summary>Trace d'une fusion, pour la télémétrie et pour les tests.</summary>
public sealed record MergerRecord(
    int Tick,
    string AcquirerId,
    string TargetId,
    decimal TenderCost,
    decimal NetAssetsAbsorbed,
    decimal MergerResult);

/// <summary>
/// L'état financier du monde. Vit dans <see cref="WorldState"/> et n'est rempli
/// que si le scénario active le module : un scénario sans bloc <c>finance</c>
/// tourne exactement comme avant.
/// </summary>
public sealed class FinanceState
{
    public bool Enabled { get; internal set; }
    public FinanceDef Def { get; internal set; } = new();

    /// <summary>La compagnie du joueur, puis les concurrents dans l'ordre des données. Ordre de parcours canonique.</summary>
    public List<FinanceCompany> Companies { get; } = new();

    public FinanceCompany? Player { get; internal set; }
    public Tycoon? Magnate { get; internal set; }
    public List<MergerRecord> Mergers { get; } = new();

    /// <summary>
    /// Aléa du module, sur une séquence qui lui est propre. Partager le flux de
    /// <see cref="WorldState.Rng"/> ferait qu'activer la finance décalerait tous
    /// les tirages des autres modules, et changerait donc les traces de
    /// régression de l'économie sans qu'aucune règle économique n'ait bougé.
    /// </summary>
    public DeterministicRandom? Rng { get; internal set; }

    /// <summary>
    /// La conjoncture, quand le scénario en a une active ; nulle sinon. La finance y
    /// lit trois choses, et trois seulement : l'ajustement du taux d'un emprunt émis
    /// aujourd'hui et la prime de risque qui l'accompagne, le taux du découvert, et
    /// le facteur du multiple de valorisation. Nulle, chaque calcul suit le chemin
    /// d'avant le module, au centime.
    /// </summary>
    public Cycle.CycleState? Cycle { get; internal set; }

    /// <summary>
    /// Trésorerie d'exploitation déjà reflétée au bilan, arrondie au centime.
    /// C'est la frontière entre le monde en <c>double</c> et la comptabilité.
    /// </summary>
    public decimal ReflectedOperatingCash { get; private set; }

    public decimal OperatingResultThisTick { get; private set; }

    public FinanceCompany? CompanyById(string id)
    {
        foreach (var company in Companies)
            if (company.Id == id) return company;
        return null;
    }

    /// <summary>
    /// Le <b>seul</b> point de conversion du monde continu vers la comptabilité.
    /// <para>
    /// On ne convertit pas le flux du tick mais le <em>cumul</em>, et on écrit la
    /// différence avec ce qui a déjà été reflété. La propriété obtenue est la
    /// raison d'être de cette mécanique : la somme de tout ce qui a été passé en
    /// écriture vaut toujours exactement <c>Round(cumul, 2)</c>, quel que soit le
    /// nombre de ticks. Convertir le flux de chaque tick laisserait au contraire
    /// un demi-centime d'erreur par tick, soit jusqu'à 3,60 par an — ce qui est
    /// précisément la fuite dont on ne veut pas.
    /// </para>
    /// </summary>
    internal decimal PostOperatingResult(double cumulativeOperatingCash)
    {
        decimal target = Money.FromDouble(cumulativeOperatingCash);
        decimal delta = target - ReflectedOperatingCash;
        ReflectedOperatingCash = target;
        OperatingResultThisTick = delta;
        return delta;
    }
}
