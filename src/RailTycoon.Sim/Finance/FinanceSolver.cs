using RailTycoon.Sim.Core;

namespace RailTycoon.Sim.Finance;

/// <summary>
/// Contrat du module finance. Il occupe la phase 6 d'un tick, après le transport :
/// la finance constate ce que l'exploitation a produit, elle ne l'anticipe pas.
/// <para>
/// Règles que toute implémentation doit respecter — elles sont vérifiées par les
/// invariants, pas seulement recommandées :
/// </para>
/// <list type="number">
///   <item>Tout mouvement d'argent passe par une écriture de <see cref="Ledger"/>,
///   donc en partie double. Aucun champ monétaire ne se modifie à la main.</item>
///   <item>Tout montant est un <see cref="decimal"/> entier de centimes. La seule
///   conversion depuis le monde en <c>double</c> est
///   <see cref="FinanceState.PostOperatingResult"/>.</item>
///   <item>Ne jamais toucher à <see cref="Economy.Company.Cash"/> : la trésorerie
///   d'exploitation appartient au module transport, et l'invariant
///   <c>bilan-tresorerie</c> en dépend. La finance la reflète, elle ne l'écrit
///   pas.</item>
///   <item>Être déterministe : aucun parcours de dictionnaire, aucun aléa hors de
///   <see cref="DeterministicRandom"/>.</item>
/// </list>
/// </summary>
public interface IFinanceSolver
{
    string Name { get; }
    void Initialize(WorldState world);
    void Step(WorldState world, SimTick tick);
}

/// <summary>
/// Implémentation de référence : une société cotée, un magnat qui joue sur ses
/// propres titres à crédit, et une politique d'acquisition qui monte au capital
/// d'un concurrent puis lance une OPA.
/// <para>
/// Les décisions sont des règles, pas une IA : le module d'IA concurrente
/// (docs/CONTRACTS.md) viendra plus tard derrière cette même interface. Ce
/// solveur est un témoin, exactement comme <c>ReferenceEconomySolver</c> — il
/// existe pour que les mécanismes financiers soient exerçables et mesurables
/// aujourd'hui, avec des seuils entièrement dans les données.
/// </para>
/// </summary>
public sealed class ReferenceFinanceSolver : IFinanceSolver
{
    public string Name => "reference";

    private decimal _depreciationPerTick;

    public void Initialize(WorldState world)
    {
        var def = world.Def.Finance;
        var state = world.Finance;

        state.Def = def;
        if (!def.Enabled)
        {
            state.Enabled = false;
            return;
        }

        state.Enabled = true;
        state.Rng = new DeterministicRandom(world.Def.Seed, def.RandomSequence);

        // Le transporteur échange déjà pendant son initialisation : la caisse
        // d'exploitation n'est donc plus forcément égale à la mise de départ quand
        // la finance ouvre ses comptes. On reflète la caisse telle qu'elle est, et
        // l'écart avec la mise va au report à nouveau — c'est bien un résultat
        // d'exploitation, pas du capital apporté.
        double operatingCash = world.Def.StartingCash + world.Company.NetProfit;
        decimal opening = state.PostOperatingResult(operatingCash);
        decimal startingCash = Money.FromDouble(world.Def.StartingCash);
        decimal fixedAssets = Money.Round(def.FixedAssetsAtStart);

        var player = NewCompany(def.CompanyId, def.CompanyName, null);
        player.Book.Post("ouverture",
            new Leg(Accounts.Cash, opening),
            new Leg(Accounts.FixedAssets, fixedAssets),
            new Leg(Accounts.ShareCapital, -(startingCash + fixedAssets)),
            new Leg(Accounts.RetainedEarnings, -(opening - startingCash)));

        player.Register.Issue(def.SharesIssued, Holders.Float);
        state.Player = player;
        state.Companies.Add(player);

        _depreciationPerTick = Money.Round(
            fixedAssets * def.DepreciationAnnualPercent / 100m / SimTick.TicksPerYear);

        foreach (var rivalDef in def.Rivals)
        {
            var rival = NewCompany(rivalDef.Id, rivalDef.Name, rivalDef);
            decimal cash = Money.Round(rivalDef.StartingCash);
            decimal assets = Money.Round(rivalDef.FixedAssets);
            decimal debt = Money.Round(rivalDef.BondPrincipal);

            rival.Book.Post("ouverture",
                new Leg(Accounts.Cash, cash),
                new Leg(Accounts.FixedAssets, assets),
                new Leg(Accounts.BondsPayable, -debt),
                new Leg(Accounts.ShareCapital, -(cash + assets - debt)));

            if (debt > 0m)
            {
                rival.Bonds.Add(new Bond
                {
                    OfferId = $"{rivalDef.Id}-heritage",
                    Name = $"Emprunt historique {rivalDef.Name}",
                    Principal = debt,
                    Outstanding = debt,
                    AnnualRatePercent = rivalDef.BondAnnualRatePercent,
                    IssuedTick = 0,
                    MaturityTick = rivalDef.BondTermTicks,
                });
                rival.PrincipalIssuedTotal += debt;
            }

            rival.Register.Issue(rivalDef.SharesIssued, Holders.Float);
            rival.SharePrice = Money.Round(rivalDef.InitialSharePrice);
            if (rival.SharePrice < Money.Round(def.Valuation.MinSharePrice))
                rival.SharePrice = Money.Round(def.Valuation.MinSharePrice);

            state.Companies.Add(rival);
        }

        // Le cours d'ouverture de la compagnie du joueur ne tient qu'à son actif
        // net : elle n'a pas encore d'historique de résultat à capitaliser.
        player.SharePrice = SharePriceFloor(def,
            player.Register.SharesIssued > 0
                ? Money.Round(player.BookEquity / player.Register.SharesIssued)
                : def.Valuation.MinSharePrice);

        var tycoon = new Tycoon { Book = NewPersonalLedger(Holders.Tycoon) };
        decimal personalCash = Money.Round(def.Tycoon.StartingCash);
        long founderShares = Math.Min(def.Tycoon.StartingShares, player.Register.SharesIssued);
        decimal founderCost = founderShares * player.SharePrice;

        tycoon.Book.Post("dotation initiale",
            new Leg(Accounts.Cash, personalCash),
            new Leg(Accounts.Portfolio, founderCost),
            new Leg(Accounts.PersonalEquity, -(personalCash + founderCost)));

        if (founderShares > 0)
        {
            player.Register.Transfer(Holders.Float, Holders.Tycoon, founderShares);
            tycoon.Portfolio.Add(player.Id, founderShares, founderCost);
        }

        state.Magnate = tycoon;

        // Le résultat de la première période se mesure à partir d'ici, pas à
        // partir de zéro : le report à nouveau d'ouverture n'est pas un bénéfice.
        player.RetainedAtLastPayout = player.Book[Accounts.RetainedEarnings];

        // Le découvert autorisé existe dès l'ouverture : sans cela le transporteur
        // passerait son premier tick sans la facilité que son bilan lui donne.
        AssessSolvency(def, world, player);
    }

    /// <summary>
    /// Phase 6 d'un tick. L'ordre interne est fixe et fait partie du contrat, pour
    /// la même raison que l'ordre des phases : le changer change les résultats.
    /// On constate d'abord (exploitation, amortissement, intérêts), on valorise
    /// ensuite, on décide enfin — une décision prise sur un cours de la veille
    /// serait une information que le joueur n'a pas.
    /// </summary>
    public void Step(WorldState world, SimTick tick)
    {
        var state = world.Finance;
        if (!state.Enabled || state.Player is null || state.Magnate is null) return;

        var def = state.Def;
        var player = state.Player;
        var tycoon = state.Magnate;

        foreach (var company in state.Companies) company.BeginTick();

        // 1 — l'exploitation, reflétée depuis le module transport.
        decimal operating = state.PostOperatingResult(world.Def.StartingCash + world.Company.NetProfit);
        player.OperatingResultThisTick = operating;
        player.OperatingResultTotal += operating;
        if (operating != 0m)
            player.Book.Post("résultat d'exploitation",
                new Leg(Accounts.Cash, operating),
                new Leg(Accounts.RetainedEarnings, -operating));

        // 2 — amortissement du matériel. Une compagnie qui ne l'amortit pas
        // affiche un actif qui ne se dégrade jamais, et donc un cours qui ne
        // reflète pas l'usure de son parc.
        Depreciate(player);

        // 3 — exploitation des concurrents, et des activités reprises par fusion.
        // L'ordre de parcours est celui des données puis celui des absorptions :
        // il détermine l'ordre des tirages aléatoires, donc le résultat.
        foreach (var company in state.Companies)
        {
            if (company.Merged) continue;
            if (company.Rival is not null) RunOperations(state, company, company.Rival);
            foreach (var absorbed in company.AbsorbedOperations)
                RunOperations(state, company, absorbed);
        }

        // 4 — le découvert. Avant les intérêts, parce que le découvert du tick en
        // porte dès le tick où il naît : un jour de crédit gratuit, répété 720
        // fois, est exactement le genre de cadeau qui ne se voit jamais.
        SyncOverdraft(def, state, player);

        // 5 — intérêts et échéances, pour tout le monde.
        foreach (var company in state.Companies)
            if (!company.Merged) ServiceDebt(company, tick);

        // 6 — valorisation. Après les charges, avant les décisions.
        foreach (var company in state.Companies)
            if (!company.Merged) Revalue(def, company);

        // 7 — financement de la compagnie, puis distribution. Une société sous
        // administration ne distribue rien : le peu qui rentre va aux créanciers.
        RaiseCashIfNeeded(def, player, tick, def.BorrowWhenCashBelow);
        if (!player.InReceivership) PayDividend(state, player, tick);

        // 8 — le magnat : coût de sa marge, puis ses ordres, puis l'appel de
        // marge. Dans cet ordre, parce qu'un ordre passé ce tick doit pouvoir
        // déclencher l'appel de marge du même tick : différer le contrôle
        // laisserait exister une position intenable pendant tout un tick.
        ServiceMargin(def, tycoon);
        TradeAsTycoon(state, def, tycoon, tick);
        EnforceMarginCall(state, def, tycoon);

        // 9 — le renflouement, s'il est dans la politique du magnat. Après ses
        // ordres de bourse, parce que remettre de l'argent dans une société qui
        // sombre est une décision d'un autre ordre que jouer son cours.
        Recapitalize(state, def, player, tycoon, tick);

        // 10 — montée au capital d'un concurrent, puis OPA. Une société sous
        // administration n'achète pas de concurrent.
        if (!player.InReceivership) RunAcquisition(state, def, player, tick);

        // 11 — le verdict de solvabilité, en dernier : il doit porter sur le bilan
        // tel qu'il est à la fin du tick, une fois toutes les décisions prises.
        AssessSolvency(def, world, player);
    }

    // ------------------------------------------------------------ insolvabilité

    /// <summary>
    /// Porte au bilan, exactement, ce que la trésorerie d'exploitation doit. Le
    /// découvert vaut toujours la part négative de cette trésorerie, au centime :
    /// ni une approximation, ni un cumul de mouvements qu'il faudrait recoller.
    /// <para>
    /// La contrepartie est la caisse, et c'est correct : un découvert est un prêt,
    /// et l'argent prêté est exactement celui que le service du trafic a déjà
    /// dépensé. Le bilan cesse ainsi de présenter un actif négatif — une caisse
    /// négative silencieuse était le vrai défaut, puisque rien ne la distinguait
    /// d'une compagnie simplement pauvre.
    /// </para>
    /// </summary>
    private static void SyncOverdraft(FinanceDef def, FinanceState state, FinanceCompany player)
    {
        decimal shortfall = state.ReflectedOperatingCash < 0m ? -state.ReflectedOperatingCash : 0m;
        decimal delta = shortfall - player.OverdraftBalance;

        if (delta > 0m)
            player.Book.Post("découvert bancaire", Accounts.Cash, Accounts.Overdraft, delta);
        else if (delta < 0m)
            player.Book.Post("résorption du découvert", Accounts.Overdraft, Accounts.Cash, -delta);

        decimal interest = Money.InterestForTick(shortfall, def.Overdraft.AnnualRatePercent);
        if (interest > 0m)
        {
            AccrueExpense(player.Book, Accounts.RetainedEarnings, Accounts.InterestPayable,
                "intérêts de découvert", interest);
            player.InterestAccruedTotal += interest;
            player.OverdraftInterestTotal += interest;
            player.InterestThisTick += interest;
        }
    }

    /// <summary>
    /// Fixe le découvert autorisé, puis prononce ou lève l'administration
    /// judiciaire. Le découvert est gagé sur les capitaux propres : une société
    /// solide encaisse un mauvais trimestre, une société vide ne l'encaisse pas.
    /// <para>
    /// Sous administration, les trains cessent de rouler. Sans cela une compagnie
    /// insolvable creusait son déficit sans terme : le transporteur refusait
    /// d'acheter du fret, mais il continuait de payer ses kilomètres — plus elle
    /// roulait, plus elle creusait.
    /// </para>
    /// </summary>
    private static void AssessSolvency(FinanceDef def, WorldState world, FinanceCompany player)
    {
        decimal equity = player.BookEquity;
        decimal pledged = equity > 0m ? Money.Round(def.Overdraft.PledgeRatio * equity) : 0m;
        decimal facility = Math.Min(Money.Round(def.Overdraft.MaxFacility), pledged);
        if (facility < 0m) facility = 0m;

        player.CreditFacility = facility;
        decimal shortfall = player.OverdraftBalance;

        if (shortfall > facility && !player.InReceivership)
        {
            player.InReceivership = true;
            player.ReceivershipTick = world.Tick.Index;
            player.ReceivershipCount++;
        }
        else if (shortfall <= facility && player.InReceivership)
        {
            player.InReceivership = false;
        }

        // Ce que le transporteur lit. Sous administration, plus rien n'est engagé.
        world.Company.Grounded = player.InReceivership;
        world.Company.CreditLimit = player.InReceivership ? 0.0 : Money.ToDouble(facility);
    }

    /// <summary>
    /// Augmentation de capital souscrite par le magnat. Son argent personnel entre
    /// dans la société, des actions nouvelles sont émises à son profit, et les
    /// autres actionnaires sont dilués — à commencer par le flottant.
    /// <para>
    /// Ce que l'apport achète est précis, et il faut être franc là-dessus : il
    /// renforce les capitaux propres, donc le découvert autorisé, donc la capacité
    /// des trains à continuer de rouler et d'acheter du fret. Il ne
    /// <em>rembourse pas</em> le trou d'exploitation, qui ne se comble qu'en
    /// exploitant mieux. Le capital achète du temps, pas l'absolution.
    /// </para>
    /// </summary>
    private static void Recapitalize(FinanceState state, FinanceDef def, FinanceCompany company, Tycoon tycoon, SimTick tick)
    {
        var policy = def.Tycoon;
        if (!policy.RescuesCompany) return;
        if (policy.TradeIntervalTicks <= 0 || tick.Index <= 0) return;
        if (tick.Index % policy.TradeIntervalTicks != 0) return;
        if (company.OverdraftBalance <= 0m) return;

        decimal discount = policy.RescueDiscountPercent < 0m ? 0m
            : (policy.RescueDiscountPercent > 90m ? 90m : policy.RescueDiscountPercent);
        decimal issuePrice = Money.Round(company.SharePrice * (1m - discount / 100m));
        decimal floor = Money.Round(def.Valuation.MinSharePrice);
        if (issuePrice < floor) issuePrice = floor;
        if (issuePrice <= 0m) return;

        decimal budget = Money.Round(tycoon.Cash * policy.RescueCashFraction);
        if (budget < issuePrice) return;

        long shares = (long)(budget / issuePrice);
        if (shares <= 0) return;
        decimal amount = shares * issuePrice;
        if (amount > tycoon.Cash) return;

        company.Book.Post("augmentation de capital", Accounts.Cash, Accounts.ShareCapital, amount);
        tycoon.Book.Post($"souscription {company.Id}", Accounts.Portfolio, Accounts.Cash, amount);

        company.Register.Issue(shares, Holders.Tycoon);
        tycoon.Portfolio.Add(company.Id, shares, amount);
        company.CapitalRaisedTotal += amount;
        company.SharesIssuedInRescues += shares;
        tycoon.SharesBoughtTotal += shares;
    }

    // ----------------------------------------------------------------- comptes

    private static FinanceCompany NewCompany(string id, string name, RivalCompanyDef? rival)
    {
        var book = new Ledger { OwnerId = id };
        book.Open(Accounts.Cash, AccountKind.Asset);
        book.Open(Accounts.FixedAssets, AccountKind.Asset);
        book.Open(Accounts.Investments, AccountKind.Asset);
        book.Open(Accounts.BondsPayable, AccountKind.Liability);
        book.Open(Accounts.Overdraft, AccountKind.Liability);
        book.Open(Accounts.InterestPayable, AccountKind.Liability);
        book.Open(Accounts.AccruedPayable, AccountKind.Liability);
        book.Open(Accounts.ShareCapital, AccountKind.Equity);
        book.Open(Accounts.RetainedEarnings, AccountKind.Equity);

        return new FinanceCompany
        {
            Id = id,
            Name = name,
            Book = book,
            Rival = rival,
        };
    }

    private static Ledger NewPersonalLedger(string ownerId)
    {
        var book = new Ledger { OwnerId = ownerId };
        book.Open(Accounts.Cash, AccountKind.Asset);
        book.Open(Accounts.Portfolio, AccountKind.Asset);
        book.Open(Accounts.MarginLoan, AccountKind.Liability);
        book.Open(Accounts.AccruedPayable, AccountKind.Liability);
        book.Open(Accounts.PersonalEquity, AccountKind.Equity);
        return book;
    }

    /// <summary>
    /// Constate une charge sans la payer tout de suite : les capitaux propres
    /// baissent immédiatement, la dette de trésorerie apparaît au passif.
    /// <para>
    /// Cette dissociation n'est pas un raffinement comptable gratuit : c'est ce
    /// qui permet à une société à sec de rester équilibrée au centime. Payer
    /// « seulement ce qu'on peut » sans constater le reste ferait disparaître la
    /// charge impayée, donc créerait de l'argent, et personne ne le verrait.
    /// </para>
    /// </summary>
    private static void AccrueExpense(Ledger book, string equityAccount, string payable, string label, decimal amount)
    {
        if (amount <= 0m) return;
        book.Post(label,
            new Leg(equityAccount, amount),
            new Leg(payable, -amount));
    }

    /// <summary>Règle une dette de trésorerie dans la limite de la caisse disponible. Renvoie le montant réellement payé.</summary>
    private static decimal SettlePayable(Ledger book, string payable, string label)
    {
        decimal due = book[payable];
        decimal cash = book[Accounts.Cash];
        decimal pay = Math.Min(due, cash);
        if (pay <= 0m) return 0m;
        book.Post(label, payable, Accounts.Cash, pay);
        return pay;
    }

    private void Depreciate(FinanceCompany company)
    {
        decimal amount = Math.Min(_depreciationPerTick, company.Book[Accounts.FixedAssets]);
        if (amount <= 0m) return;
        company.Book.Post("amortissement",
            new Leg(Accounts.RetainedEarnings, amount),
            new Leg(Accounts.FixedAssets, -amount));
    }

    // -------------------------------------------------------------- concurrents

    /// <summary>
    /// Exploitation d'une compagnie pilotée par les données — un concurrent, ou
    /// une activité reprise par fusion. Son résultat est une donnée bruitée, pas
    /// une simulation : le but n'est pas de faire rouler ses trains mais de donner
    /// à l'OPA une cible dont le bilan bouge et dont le cours est incertain. Le
    /// vrai concurrent viendra du module <c>ai</c>.
    /// </summary>
    private static void RunOperations(FinanceState state, FinanceCompany company, RivalCompanyDef def)
    {
        decimal jitter = 1m;
        if (def.EarningsJitter != 0m && state.Rng is not null)
            jitter = 1m + (decimal)state.Rng.NextRange(-1.0, 1.0) * def.EarningsJitter;

        decimal result = Money.Round(def.EarningsPerTick * jitter);
        company.OperatingResultThisTick += result;
        company.OperatingResultTotal += result;

        if (result > 0m)
        {
            company.Book.Post("résultat d'exploitation",
                new Leg(Accounts.Cash, result),
                new Leg(Accounts.RetainedEarnings, -result));
        }
        else if (result < 0m)
        {
            AccrueExpense(company.Book, Accounts.RetainedEarnings, Accounts.AccruedPayable,
                "perte d'exploitation", -result);
            SettlePayable(company.Book, Accounts.AccruedPayable, "règlement de charges");
        }
    }

    // ------------------------------------------------------------------- dette

    private static void ServiceDebt(FinanceCompany company, SimTick tick)
    {
        foreach (var bond in company.Bonds)
        {
            if (!bond.Alive) continue;

            decimal interest = Money.InterestForTick(bond.Outstanding, bond.AnnualRatePercent);
            if (interest > 0m)
            {
                AccrueExpense(company.Book, Accounts.RetainedEarnings, Accounts.InterestPayable,
                    $"intérêts {bond.OfferId}", interest);
                bond.InterestAccrued += interest;
                company.InterestAccruedTotal += interest;
                company.InterestThisTick += interest;
            }

            // À l'échéance, on rembourse ce que la caisse permet. Le reliquat
            // continue de courir : une société incapable de rembourser ne voit pas
            // sa dette s'évaporer, elle la traîne — et paie des intérêts dessus.
            if (tick.Index >= bond.MaturityTick)
            {
                decimal repay = Math.Min(bond.Outstanding, Math.Max(0m, company.Cash));
                if (repay > 0m)
                {
                    company.Book.Post($"remboursement {bond.OfferId}",
                        Accounts.BondsPayable, Accounts.Cash, repay);
                    bond.Outstanding -= repay;
                    company.PrincipalRepaidTotal += repay;
                }
            }
        }

        company.InterestPaidTotal += SettlePayable(
            company.Book, Accounts.InterestPayable, "paiement d'intérêts");
    }

    /// <summary>Mobilise le premier emprunt disponible non encore souscrit, si la trésorerie passe sous le seuil.</summary>
    private static bool RaiseCashIfNeeded(FinanceDef def, FinanceCompany company, SimTick tick, decimal cashFloor)
    {
        if (company.Cash >= cashFloor) return false;
        var offer = NextOffer(def, company, tick, "");
        return offer is not null && IssueBond(company, offer, tick);
    }

    private static BondOfferDef? NextOffer(FinanceDef def, FinanceCompany company, SimTick tick, string preferredId)
    {
        BondOfferDef? fallback = null;
        foreach (var offer in def.BondOffers)
        {
            if (tick.Index < offer.AvailableFromTick) continue;
            if (AlreadyIssued(company, offer.Id)) continue;
            if (preferredId.Length > 0 && offer.Id == preferredId) return offer;
            fallback ??= offer;
        }
        return preferredId.Length > 0 ? null : fallback;
    }

    private static bool AlreadyIssued(FinanceCompany company, string offerId)
    {
        foreach (var bond in company.Bonds)
            if (bond.OfferId == offerId) return true;
        return false;
    }

    private static bool IssueBond(FinanceCompany company, BondOfferDef offer, SimTick tick)
    {
        decimal principal = Money.Round(offer.Principal);
        if (principal <= 0m) return false;

        company.Bonds.Add(new Bond
        {
            OfferId = offer.Id,
            Name = offer.Name,
            Principal = principal,
            Outstanding = principal,
            AnnualRatePercent = offer.AnnualRatePercent,
            IssuedTick = tick.Index,
            MaturityTick = tick.Index + Math.Max(1, offer.TermTicks),
        });
        company.PrincipalIssuedTotal += principal;
        company.Book.Post($"émission {offer.Id}", Accounts.Cash, Accounts.BondsPayable, principal);
        return true;
    }

    // -------------------------------------------------------------- valorisation

    private static decimal SharePriceFloor(FinanceDef def, decimal price)
    {
        decimal floor = Money.Round(def.Valuation.MinSharePrice);
        return price < floor ? floor : price;
    }

    /// <summary>
    /// Impact d'un ordre sur le cours. Un achat pousse le cours vers le haut, une
    /// vente vers le bas, proportionnellement à la fraction du capital échangée.
    /// <para>
    /// C'est l'analogue boursier du découpage en tranches d'une grosse livraison :
    /// sans lui, le flottant absorbe n'importe quel volume au cours affiché et
    /// l'aller-retour ne coûte rien. On l'applique au cours et non à l'ordre
    /// lui-même — l'ordre se règle au cours d'avant — parce que la trace reste
    /// ainsi lisible et l'écriture comptable exacte au centime.
    /// </para>
    /// </summary>
    private static void ApplyMarketImpact(FinanceDef def, FinanceCompany company, long shares, int direction)
    {
        decimal impact = def.Valuation.MarketImpact;
        if (impact <= 0m || shares <= 0 || company.Register.SharesIssued <= 0) return;

        decimal move = Money.Round(
            company.SharePrice * impact * shares / company.Register.SharesIssued);
        if (move <= 0m) return;

        company.SharePrice = SharePriceFloor(def, company.SharePrice + direction * move);
    }

    /// <summary>
    /// Cours = actif net comptable + un multiple du résultat annualisé, approché
    /// lentement. Le lissage compte : un cours qui suivrait le résultat du jour
    /// sauterait de dix pour cent chaque fois qu'un train décharge, et le magnat
    /// n'aurait plus qu'à acheter la veille des livraisons.
    /// </summary>
    private static void Revalue(FinanceDef def, FinanceCompany company)
    {
        var cfg = def.Valuation;
        decimal resultThisTick = company.OperatingResultThisTick - company.InterestThisTick;
        company.EarningsEma = Money.Round(
            company.EarningsEma + cfg.EarningsSmoothing * (resultThisTick - company.EarningsEma));

        decimal intrinsic = company.BookEquity + cfg.EarningsMultiple * company.EarningsEma * SimTick.TicksPerYear;
        decimal target = company.Register.SharesIssued > 0
            ? Money.Round(intrinsic / company.Register.SharesIssued)
            : cfg.MinSharePrice;
        target = SharePriceFloor(def, target);

        company.SharePrice = SharePriceFloor(def, Money.Round(
            company.SharePrice + cfg.PriceSmoothing * (target - company.SharePrice)));
    }

    // ---------------------------------------------------------------- dividende

    /// <summary>
    /// Distribution au prorata. Le dividende par action est arrondi au centime
    /// <em>inférieur</em> et le total distribué recalculé dessus : c'est la seule
    /// façon que la somme des parts égale exactement la somme décaissée. Partir du
    /// total et arrondir chaque part laisse un résidu, et un résidu est une fuite.
    /// </summary>
    private static void PayDividend(FinanceState state, FinanceCompany company, SimTick tick)
    {
        var policy = state.Def.Dividends;
        if (policy.IntervalTicks <= 0 || tick.Index <= 0) return;
        if (tick.Index % policy.IntervalTicks != 0) return;

        long shares = company.Register.SharesIssued;
        if (shares <= 0) return;

        // Résultat de la période, mesuré sur le report à nouveau. La référence est
        // remise à jour <em>après</em> la distribution, sinon le dividende versé
        // serait compté comme une perte de la période suivante et la société ne
        // distribuerait plus jamais rien.
        decimal periodResult = company.Book[Accounts.RetainedEarnings] - company.RetainedAtLastPayout;
        decimal total = 0m;

        if (periodResult > 0m)
        {
            decimal spendable = company.Cash - Money.Round(policy.MinCashBuffer);
            decimal budget = Math.Min(Money.Round(policy.PayoutRatio * periodResult), spendable);

            // Un dividende inférieur au centime par action n'est pas un dividende.
            decimal perShare = budget >= Money.Cent ? Money.Floor(budget / shares) : 0m;
            if (perShare >= Money.Cent) total = perShare * shares;

            if (total > 0m)
            {
                company.Book.Post("dividende",
                    new Leg(Accounts.RetainedEarnings, total),
                    new Leg(Accounts.Cash, -total));
                company.DividendsPaidTotal += total;
                company.DividendThisTick = total;
                Distribute(state, company, perShare);
            }
        }

        company.RetainedAtLastPayout = company.Book[Accounts.RetainedEarnings];
    }

    private static void Distribute(FinanceState state, FinanceCompany company, decimal perShare)
    {

        // Parcours du registre dans l'ordre d'inscription. La part du flottant
        // sort du modèle : ce sont des actionnaires que la simulation ne
        // représente pas. Le bilan de la société reste équilibré — caisse et
        // capitaux propres baissent du même montant — ce qui est bien le sens
        // économique d'un dividende.
        foreach (var holder in company.Register.Holders)
        {
            if (holder.Shares <= 0) continue;
            decimal amount = perShare * holder.Shares;

            if (holder.HolderId == Holders.Tycoon && state.Magnate is not null)
            {
                state.Magnate.Book.Post($"dividende {company.Id}",
                    new Leg(Accounts.Cash, amount),
                    new Leg(Accounts.PersonalEquity, -amount));
                state.Magnate.DividendsReceivedTotal += amount;
            }
            else
            {
                var holderCompany = state.CompanyById(holder.HolderId);
                if (holderCompany is null || holderCompany.Merged) continue;
                holderCompany.Book.Post($"dividende reçu {company.Id}",
                    new Leg(Accounts.Cash, amount),
                    new Leg(Accounts.RetainedEarnings, -amount));
            }
        }
    }

    // ------------------------------------------------------------------ magnat

    private static void ServiceMargin(FinanceDef def, Tycoon tycoon)
    {
        decimal interest = Money.InterestForTick(tycoon.MarginLoan, def.Tycoon.MarginAnnualRatePercent);
        if (interest > 0m)
        {
            AccrueExpense(tycoon.Book, Accounts.PersonalEquity, Accounts.AccruedPayable,
                "intérêts de marge", interest);
            tycoon.InterestAccruedTotal += interest;
        }
        tycoon.InterestPaidTotal += SettlePayable(
            tycoon.Book, Accounts.AccruedPayable, "paiement d'intérêts de marge");
    }

    /// <summary>
    /// Le magnat n'opère que sur les titres de sa propre compagnie. C'est le cas
    /// intéressant : il connaît son entreprise mieux que le marché, et rien ne
    /// l'empêche de vendre avant que la sanction n'arrive au cours.
    /// </summary>
    private static void TradeAsTycoon(FinanceState state, FinanceDef def, Tycoon tycoon, SimTick tick)
    {
        var policy = def.Tycoon;
        if (policy.TradeIntervalTicks <= 0 || tick.Index <= 0) return;
        if (tick.Index % policy.TradeIntervalTicks != 0) return;

        var company = state.Player;
        if (company is null || company.Merged || company.SharePrice <= 0m) return;

        long held = tycoon.Portfolio.SharesOf(company.Id);
        decimal basis = tycoon.Portfolio.CostBasisOf(company.Id);

        if (held >= policy.MinLotShares && basis > 0m)
        {
            decimal averageCost = basis / held;
            if (company.SharePrice >= averageCost * policy.SellAboveCostRatio)
            {
                long lot = (long)(held * policy.SellFraction);
                if (lot >= policy.MinLotShares)
                {
                    SellShares(state, def, tycoon, company, lot);
                    return;
                }
            }
        }

        BuyShares(state, def, tycoon, company);
    }

    private static void BuyShares(FinanceState state, FinanceDef def, Tycoon tycoon, FinanceCompany company)
    {
        var policy = def.Tycoon;
        decimal price = company.SharePrice;
        decimal ownFunds = Money.Round(tycoon.Cash * policy.BuyBudgetFraction);
        if (ownFunds <= 0m) return;

        // Achat sur marge : la fraction empruntée porte sur le montant total de
        // l'achat, pas sur l'apport. Un ratio de 0,5 double donc la force de
        // frappe, et double aussi la perte si le cours cède.
        decimal ratio = policy.MarginInitialRatio;
        if (ratio < 0m) ratio = 0m;
        if (ratio > 0.9m) ratio = 0.9m;
        decimal firepower = Money.Round(ownFunds / (1m - ratio));

        long available = company.Register.HeldBy(Holders.Float);
        long lot = (long)(firepower / price);
        if (lot > available) lot = available;
        if (lot < policy.MinLotShares) return;

        decimal cost = lot * price;
        decimal borrowed = Money.Round(cost * ratio);

        // On ne s'endette jamais au-delà du seuil d'appel de marge : emprunter
        // pour déclencher la liquidation au même tick n'est pas une stratégie,
        // c'est un bug.
        decimal valueAfter = tycoon.PortfolioMarketValue(state) + cost;
        decimal ceiling = Money.Round(policy.MarginMaintenanceRatio * valueAfter) - tycoon.MarginLoan;
        if (borrowed > ceiling) borrowed = ceiling;
        if (borrowed < 0m) borrowed = 0m;
        if (cost - borrowed > tycoon.Cash)
        {
            lot = (long)((tycoon.Cash + borrowed) / price);
            if (lot < policy.MinLotShares) return;
            cost = lot * price;
            if (borrowed > cost) borrowed = cost;
            if (cost - borrowed > tycoon.Cash) return;
        }

        if (borrowed > 0m)
            tycoon.Book.Post("emprunt sur marge", Accounts.Cash, Accounts.MarginLoan, borrowed);

        tycoon.Book.Post($"achat {company.Id}", Accounts.Portfolio, Accounts.Cash, cost);
        tycoon.Portfolio.Add(company.Id, lot, cost);
        company.Register.Transfer(Holders.Float, Holders.Tycoon, lot);
        tycoon.SharesBoughtTotal += lot;
        ApplyMarketImpact(def, company, lot, 1);
    }

    private static void SellShares(FinanceState state, FinanceDef def, Tycoon tycoon, FinanceCompany company, long lot)
    {
        long held = company.Register.HeldBy(Holders.Tycoon);
        if (lot > held) lot = held;
        if (lot <= 0) return;

        decimal proceeds = lot * company.SharePrice;
        decimal basisOut = tycoon.Portfolio.Remove(company.Id, lot);
        decimal result = proceeds - basisOut;

        // La plus-value n'apparaît qu'ici : c'est à la vente qu'elle devient de
        // l'argent. Trois mouvements, dont la somme est nulle par construction.
        tycoon.Book.Post($"vente {company.Id}",
            new Leg(Accounts.Cash, proceeds),
            new Leg(Accounts.Portfolio, -basisOut),
            new Leg(Accounts.PersonalEquity, -result));

        company.Register.Transfer(Holders.Tycoon, Holders.Float, lot);
        tycoon.SharesSoldTotal += lot;
        tycoon.RealizedResultTotal += result;
        ApplyMarketImpact(def, company, lot, -1);

        decimal repay = Math.Min(tycoon.MarginLoan, tycoon.Cash);
        if (repay > 0m)
            tycoon.Book.Post("remboursement de marge", Accounts.MarginLoan, Accounts.Cash, repay);
    }

    /// <summary>
    /// Appel de marge. Vendre <c>n</c> titres au cours <c>p</c> réduit la dette et
    /// le portefeuille du même montant : la contrainte
    /// <c>dette − p·n ≤ maintien × (valeur − p·n)</c> donne directement le nombre
    /// de titres à liquider. C'est ce calcul qui fait de l'achat à crédit un
    /// risque et non un multiplicateur gratuit.
    /// </summary>
    private static void EnforceMarginCall(FinanceState state, FinanceDef def, Tycoon tycoon)
    {
        decimal loan = tycoon.MarginLoan;
        if (loan <= 0m) return;

        decimal maintenance = def.Tycoon.MarginMaintenanceRatio;
        decimal value = tycoon.PortfolioMarketValue(state);
        decimal deficit = loan - Money.Round(maintenance * value);
        if (deficit <= 0m) return;

        tycoon.MarginCalls++;

        foreach (var position in tycoon.Portfolio.Positions)
        {
            if (position.Shares <= 0) continue;
            var company = state.CompanyById(position.CompanyId);
            if (company is null || company.Merged || company.SharePrice <= 0m) continue;

            decimal unitRelief = company.SharePrice * (1m - maintenance);
            if (unitRelief <= 0m) continue;

            long needed = (long)decimal.Ceiling(deficit / unitRelief);
            long lot = Math.Min(needed, position.Shares);
            if (lot <= 0) continue;

            SellShares(state, def, tycoon, company, lot);

            loan = tycoon.MarginLoan;
            if (loan <= 0m) return;
            deficit = loan - Money.Round(maintenance * tycoon.PortfolioMarketValue(state));
            if (deficit <= 0m) return;
        }
    }

    // ------------------------------------------------------------- acquisitions

    /// <summary>
    /// Montée au capital tranche par tranche, puis offre publique d'achat sur le
    /// flottant, puis fusion. Une seule opération par tick et une seule cible à la
    /// fois : c'est ce qui rend la séquence lisible dans une trace, et c'est aussi
    /// la contrainte réelle d'un conseil d'administration.
    /// </summary>
    private static void RunAcquisition(FinanceState state, FinanceDef def, FinanceCompany acquirer, SimTick tick)
    {
        var policy = def.Acquisition;
        if (policy.Targets.Count == 0) return;

        foreach (string targetId in policy.Targets)
        {
            var target = state.CompanyById(targetId);
            if (target is null || target.Merged || target.Register.SharesIssued <= 0) continue;

            long issued = target.Register.SharesIssued;
            long held = target.Register.HeldBy(acquirer.Id);
            long control = (long)decimal.Ceiling(policy.ControlFraction * issued);

            if (held >= control && held < issued)
            {
                LaunchTender(state, def, acquirer, target, tick);
                return;
            }
            if (held >= issued)
            {
                Absorb(state, acquirer, target, tick, 0m);
                return;
            }

            if (policy.StakeIntervalTicks > 0 && tick.Index > 0 &&
                tick.Index % policy.StakeIntervalTicks == 0)
            {
                BuyStake(state, def, acquirer, target, tick);
            }
            return;
        }
    }

    private static void BuyStake(FinanceState state, FinanceDef def, FinanceCompany acquirer, FinanceCompany target, SimTick tick)
    {
        var policy = def.Acquisition;
        decimal price = target.SharePrice;
        if (price <= 0m) return;

        long available = target.Register.HeldBy(Holders.Float);
        long lot = Math.Min(policy.StakeTrancheShares, available);
        if (lot <= 0) return;

        decimal buffer = Money.Round(policy.MinCashBuffer);
        if (lot * price > acquirer.Cash - buffer)
            RaiseCashIfNeeded(def, acquirer, tick, buffer + lot * price);

        decimal spendable = acquirer.Cash - buffer;
        if (spendable <= 0m) return;
        if (lot * price > spendable) lot = (long)(spendable / price);
        if (lot <= 0) return;

        decimal cost = lot * price;
        acquirer.Book.Post($"participation {target.Id}", Accounts.Investments, Accounts.Cash, cost);
        acquirer.Investments.Add(target.Id, lot, cost);
        target.Register.Transfer(Holders.Float, acquirer.Id, lot);
        ApplyMarketImpact(def, target, lot, 1);
    }

    /// <summary>
    /// Offre publique d'achat sur le flottant, à un cours majoré. Tant que la
    /// trésorerie ne suffit pas, l'offre attend : une OPA qu'on ne peut pas payer
    /// ne se lance pas à moitié, sinon la participation resterait bloquée entre le
    /// contrôle et la fusion.
    /// </summary>
    private static void LaunchTender(FinanceState state, FinanceDef def, FinanceCompany acquirer, FinanceCompany target, SimTick tick)
    {
        var policy = def.Acquisition;
        long floating = target.Register.HeldBy(Holders.Float);
        if (floating <= 0) return;

        decimal tenderPrice = Money.Round(target.SharePrice * (1m + policy.TenderPremiumPercent / 100m));
        if (tenderPrice <= 0m) return;
        decimal cost = floating * tenderPrice;
        decimal buffer = Money.Round(policy.MinCashBuffer);

        if (cost > acquirer.Cash - buffer && policy.FundingBondId.Length > 0)
        {
            var offer = NextOffer(def, acquirer, tick, policy.FundingBondId);
            if (offer is not null) IssueBond(acquirer, offer, tick);
        }
        // L'emprunt dédié peut avoir déjà servi : on se rabat alors sur n'importe
        // quelle ligne disponible plutôt que de laisser la participation coincée
        // entre le contrôle et la fusion pour le reste de la partie.
        if (cost > acquirer.Cash - buffer)
            RaiseCashIfNeeded(def, acquirer, tick, buffer + cost);
        if (cost > acquirer.Cash - buffer) return;

        acquirer.Book.Post($"OPA {target.Id}", Accounts.Investments, Accounts.Cash, cost);
        acquirer.Investments.Add(target.Id, floating, cost);
        target.Register.Transfer(Holders.Float, acquirer.Id, floating);

        // Les minoritaires autres que le flottant — le magnat, une autre
        // compagnie — ne sont pas expropriés : la fusion attend qu'ils aient
        // cédé. Dans l'état actuel du modèle, seul le flottant détient ce
        // reliquat, donc la fusion suit immédiatement.
        if (target.Register.HeldBy(acquirer.Id) >= target.Register.SharesIssued)
            Absorb(state, acquirer, target, tick, cost);
    }

    /// <summary>
    /// Fusion-absorption. L'actif et le passif de la cible passent chez
    /// l'absorbante, le coût des titres détenus sort du bilan, et l'écart va au
    /// report à nouveau — c'est le résultat de fusion, positif si on a payé moins
    /// que l'actif net, négatif sinon.
    /// <para>
    /// Les cumuls de dette suivent les emprunts repris. Les laisser derrière
    /// casserait l'invariant <c>dette-emprunts</c> des deux côtés : l'absorbante
    /// porterait un capital restant dû sans emprunt contracté, et l'absorbée
    /// l'inverse.
    /// </para>
    /// </summary>
    private static void Absorb(FinanceState state, FinanceCompany acquirer, FinanceCompany target, SimTick tick, decimal tenderCost)
    {
        decimal cash = target.Book[Accounts.Cash];
        decimal fixedAssets = target.Book[Accounts.FixedAssets];
        decimal investments = target.Book[Accounts.Investments];
        decimal bonds = target.Book[Accounts.BondsPayable];
        decimal interestPayable = target.Book[Accounts.InterestPayable];
        decimal accrued = target.Book[Accounts.AccruedPayable];
        decimal shareCapital = target.Book[Accounts.ShareCapital];
        decimal retained = target.Book[Accounts.RetainedEarnings];

        decimal netAssets = cash + fixedAssets + investments - bonds - interestPayable - accrued;
        long shares = acquirer.Investments.SharesOf(target.Id);
        decimal basis = acquirer.Investments.Remove(target.Id, shares);
        decimal result = netAssets - basis;

        acquirer.Book.Post($"fusion {target.Id}",
            new Leg(Accounts.Cash, cash),
            new Leg(Accounts.FixedAssets, fixedAssets),
            new Leg(Accounts.Investments, investments - basis),
            new Leg(Accounts.BondsPayable, -bonds),
            new Leg(Accounts.InterestPayable, -interestPayable),
            new Leg(Accounts.AccruedPayable, -accrued),
            new Leg(Accounts.RetainedEarnings, -result));

        target.Book.Post("absorption",
            new Leg(Accounts.Cash, -cash),
            new Leg(Accounts.FixedAssets, -fixedAssets),
            new Leg(Accounts.Investments, -investments),
            new Leg(Accounts.BondsPayable, bonds),
            new Leg(Accounts.InterestPayable, interestPayable),
            new Leg(Accounts.AccruedPayable, accrued),
            new Leg(Accounts.ShareCapital, shareCapital),
            new Leg(Accounts.RetainedEarnings, retained));

        foreach (var position in target.Investments.Positions)
        {
            if (position.Shares <= 0) continue;
            acquirer.Investments.Add(position.CompanyId, position.Shares, position.CostBasis);
            state.CompanyById(position.CompanyId)?.Register.Transfer(target.Id, acquirer.Id, position.Shares);
            position.Shares = 0;
            position.CostBasis = 0m;
        }

        acquirer.Bonds.AddRange(target.Bonds);
        target.Bonds.Clear();

        acquirer.PrincipalIssuedTotal += target.PrincipalIssuedTotal;
        acquirer.PrincipalRepaidTotal += target.PrincipalRepaidTotal;
        acquirer.InterestAccruedTotal += target.InterestAccruedTotal;
        acquirer.InterestPaidTotal += target.InterestPaidTotal;
        target.PrincipalIssuedTotal = 0m;
        target.PrincipalRepaidTotal = 0m;
        target.InterestAccruedTotal = 0m;
        target.InterestPaidTotal = 0m;

        target.Register.Retire();
        target.Merged = true;
        target.SharePrice = 0m;

        state.Mergers.Add(new MergerRecord(tick.Index, acquirer.Id, target.Id, tenderCost, netAssets, result));
    }
}
