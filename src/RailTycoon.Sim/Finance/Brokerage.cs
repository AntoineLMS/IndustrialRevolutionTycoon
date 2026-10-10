using RailTycoon.Sim.Core;

namespace RailTycoon.Sim.Finance;

/// <summary>
/// Le compte du magnat chez son courtier : ses ordres sur les titres de sa compagnie,
/// la marge qui les finance, l'appel de marge, la ruine, et ce que deviennent ses
/// revenus quand il est ruiné.
/// <para>
/// Trois règles du courtier, toutes dans les données :
/// </para>
/// <list type="number">
///   <item><b>La ligne de crédit</b> (<c>creditLineRatio</c>) : on ne prête pas au-delà
///   d'une part de la valeur des titres, comptés au cours d'avant l'ordre. Un achat qui
///   la dépasserait est réduit, ou refusé.</item>
///   <item><b>L'appel de marge</b> (<c>marginMaintenanceRatio</c>) : au-delà de ce
///   ratio dette / valeur, le courtier vend d'office, dans le carnet, donc en faisant
///   baisser le cours — ce qui peut rouvrir le déficit. Il revend alors, au plus
///   <c>marginCallMaxRounds</c> tours dans la journée.</item>
///   <item><b>La ruine</b> : si, tous les titres vendus, la dette dépasse encore la
///   caisse, la caisse est saisie et le reste devient une dette résiduelle. Le magnat
///   ne peut plus rien acheter ; ses revenus la remboursent en priorité ; il sort de
///   la ruine le jour où elle tombe à zéro.</item>
/// </list>
/// </summary>
public static class Brokerage
{
    /// <summary>La ligne de crédit effective : celle des données, ou à défaut le seuil de maintien (le comportement d'avant).</summary>
    public static decimal CreditLine(TycoonPolicyDef policy) => policy.CreditLineRatio ?? policy.MarginMaintenanceRatio;

    public static void ServiceMargin(FinanceDef def, Tycoon tycoon)
    {
        decimal interest = Money.InterestForTick(tycoon.MarginLoan, def.Tycoon.MarginAnnualRatePercent);
        if (interest > 0m)
        {
            tycoon.Book.Post("intérêts de marge",
                new Leg(Accounts.PersonalEquity, interest),
                new Leg(Accounts.AccruedPayable, -interest));
            tycoon.InterestAccruedTotal += interest;
        }
        tycoon.InterestPaidTotal += SettleAccrued(tycoon.Book);
    }

    private static decimal SettleAccrued(Ledger book)
    {
        decimal pay = Math.Min(book[Accounts.AccruedPayable], book[Accounts.Cash]);
        if (pay <= 0m) return 0m;
        book.Post("paiement d'intérêts de marge", Accounts.AccruedPayable, Accounts.Cash, pay);
        return pay;
    }

    /// <summary>
    /// Le magnat n'opère que sur les titres de sa propre compagnie. C'est le cas
    /// intéressant : il connaît son entreprise mieux que le marché, et rien ne
    /// l'empêche de vendre avant que la sanction n'arrive au cours. Ruiné, il peut
    /// encore vendre ce qu'il détiendrait — le produit rembourse d'abord sa dette —,
    /// jamais acheter.
    /// </summary>
    public static void Trade(FinanceState state, FinanceDef def, Tycoon tycoon, SimTick tick)
    {
        var policy = def.Tycoon;
        if (policy.TradeIntervalTicks <= 0 || tick.Index <= 0) return;
        if (tick.Index % policy.TradeIntervalTicks != 0) return;

        var company = state.Player;
        if (company is null || company.Merged || company.SharePrice <= 0m) return;

        long held = tycoon.Portfolio.SharesOf(company.Id);
        decimal basis = tycoon.Portfolio.CostBasisOf(company.Id);
        long minLot = policy.MinLotShares * company.SplitFactor;

        if (held >= minLot && basis > 0m)
        {
            decimal averageCost = basis / held;
            if (company.SharePrice >= averageCost * policy.SellAboveCostRatio)
            {
                long lot = (long)(held * policy.SellFraction);
                if (lot >= minLot)
                {
                    SellShares(state, def, tycoon, company, lot, tick.Index);
                    return;
                }
            }
        }

        BuyShares(state, def, tycoon, company);
    }

    /// <summary>
    /// Achat au comptant et sur marge, dans le carnet. Renvoie le nombre de titres
    /// achetés (0 si l'ordre est refusé).
    /// <para>
    /// Le budget est une part de la caisse, démultipliée par la part empruntée ; le lot
    /// est ce que ce budget achète en remontant le carnet. La dette ne dépasse jamais la
    /// ligne de crédit, évaluée sur tous les titres détenus après l'achat, au cours
    /// d'avant l'ordre : le courtier ne prête pas sur la hausse que l'ordre va lui-même
    /// provoquer. Si la caisse ne suffit pas à payer ce que la ligne ne couvre pas, le
    /// lot est réduit, et la ligne recalculée sur ce lot réduit — sans ce recalcul, un
    /// petit achat pourrait emprunter ce qu'un gros aurait justifié.
    /// </para>
    /// </summary>
    public static long BuyShares(FinanceState state, FinanceDef def, Tycoon tycoon, FinanceCompany company)
    {
        if (tycoon.Ruined) return 0;

        var policy = def.Tycoon;
        decimal price = company.SharePrice;
        decimal ownFunds = Money.Round(tycoon.Cash * policy.BuyBudgetFraction);
        if (ownFunds <= 0m) return 0;

        // Achat sur marge : la fraction empruntée porte sur le montant total de
        // l'achat, pas sur l'apport. Un ratio de 0,5 double donc la force de
        // frappe, et double aussi la perte si le cours cède.
        decimal ratio = policy.MarginInitialRatio;
        if (ratio < 0m) ratio = 0m;
        if (ratio > 0.9m) ratio = 0.9m;
        decimal firepower = Money.Round(ownFunds / (1m - ratio));

        long available = company.Register.HeldBy(Holders.Float);
        long minLot = policy.MinLotShares * company.SplitFactor;
        long lot = StockMarket.MaxSharesFor(def, company, firepower, available);
        if (lot > available) lot = available;
        if (lot < minLot) return 0;

        decimal cost = StockMarket.Quote(def, company, lot, +1).Amount;
        decimal borrowed = Money.Round(cost * ratio);

        decimal line = CreditLine(policy);
        decimal holdings = tycoon.PortfolioMarketValue(state);
        decimal ceiling = Money.Round(line * (holdings + lot * price)) - tycoon.MarginLoan;
        if (borrowed > ceiling) borrowed = ceiling;
        if (borrowed < 0m) borrowed = 0m;
        if (cost - borrowed > tycoon.Cash)
        {
            lot = StockMarket.MaxSharesFor(def, company, tycoon.Cash + borrowed, available);
            if (lot < minLot) return 0;
            cost = StockMarket.Quote(def, company, lot, +1).Amount;
            if (borrowed > cost) borrowed = cost;
            decimal reduced = Money.Round(line * (holdings + lot * price)) - tycoon.MarginLoan;
            if (borrowed > reduced) borrowed = reduced < 0m ? 0m : reduced;
            if (cost - borrowed > tycoon.Cash)
            {
                // La ligne recalculée sur le lot réduit ne couvre plus ce que la caisse
                // ne paie pas : on cherche le plus gros lot que la caisse et la ligne
                // financent ensemble. Ce qui reste à payer comptant croît avec le lot
                // (on n'emprunte jamais plus que l'achat), donc une dichotomie suffit.
                (decimal Cost, decimal Borrowed) Finance(long n)
                {
                    decimal c = StockMarket.Quote(def, company, n, +1).Amount;
                    decimal b = Math.Min(Money.Round(c * ratio), Money.Round(line * (holdings + n * price)) - tycoon.MarginLoan);
                    return (c, b < 0m ? 0m : b);
                }
                long lo = 0, hi = lot;
                while (lo < hi)
                {
                    long mid = lo + (hi - lo + 1) / 2;
                    var (c, b) = Finance(mid);
                    if (c - b <= tycoon.Cash) lo = mid; else hi = mid - 1;
                }
                if (lo < minLot) return 0;
                lot = lo;
                (cost, borrowed) = Finance(lot);
            }
        }

        if (borrowed > 0m)
            tycoon.Book.Post("emprunt sur marge", Accounts.Cash, Accounts.MarginLoan, borrowed);

        decimal paid = StockMarket.Execute(def, company, lot, +1);
        tycoon.Book.Post($"achat {company.Id}", Accounts.Portfolio, Accounts.Cash, paid);
        tycoon.Portfolio.Add(company.Id, lot, paid);
        company.Register.Transfer(Holders.Float, Holders.Tycoon, lot);
        tycoon.SharesBoughtTotal += lot;
        return lot;
    }

    /// <summary>
    /// Vente dans le carnet. La plus-value n'apparaît qu'ici : c'est à la vente
    /// qu'elle devient de l'argent. Le produit rembourse la marge, puis, si le magnat
    /// est ruiné, sa dette résiduelle.
    /// </summary>
    public static void SellShares(FinanceState state, FinanceDef def, Tycoon tycoon, FinanceCompany company,
        long lot, int tick, bool forced = false)
    {
        long held = company.Register.HeldBy(Holders.Tycoon);
        if (lot > held) lot = held;
        if (lot <= 0) return;

        decimal proceeds = StockMarket.Execute(def, company, lot, -1);
        decimal basisOut = tycoon.Portfolio.Remove(company.Id, lot);
        decimal result = proceeds - basisOut;

        // Trois mouvements, dont la somme est nulle par construction.
        tycoon.Book.Post($"vente {company.Id}",
            new Leg(Accounts.Cash, proceeds),
            new Leg(Accounts.Portfolio, -basisOut),
            new Leg(Accounts.PersonalEquity, -result));

        company.Register.Transfer(Holders.Tycoon, Holders.Float, lot);
        tycoon.SharesSoldTotal += lot;
        tycoon.RealizedResultTotal += result;
        if (forced) tycoon.ForcedSaleShares += lot;

        decimal repay = Math.Min(tycoon.MarginLoan, tycoon.Cash);
        if (repay > 0m)
            tycoon.Book.Post("remboursement de marge", Accounts.MarginLoan, Accounts.Cash, repay);

        SettleResidualDebt(state, tycoon, tick);
    }

    /// <summary>
    /// Appel de marge. Vendre <c>n</c> titres au cours <c>p</c> réduit la dette et
    /// le portefeuille du même montant : la contrainte
    /// <c>dette − p·n ≤ maintien × (valeur − p·n)</c> donne le nombre de titres à
    /// liquider <em>au cours du moment</em>.
    /// <para>
    /// <b>La cascade.</b> Dans un carnet de profondeur finie, la vente fait baisser le
    /// cours pendant qu'elle s'exécute : elle rapporte moins que prévu, et les titres
    /// restants valent moins. Le déficit peut donc survivre à la vente qui devait le
    /// combler. Le courtier recommence alors, au nouveau cours. Chaque tour vend au
    /// moins un titre, donc la cascade s'arrête au plus tard quand il n'y a plus rien à
    /// vendre ; elle est de plus bornée à <c>marginCallMaxRounds</c> tours par jour, et
    /// reprend le lendemain si le déficit subsiste — le cours ayant eu la nuit pour se
    /// reprendre vers sa valeur.
    /// </para>
    /// <para>
    /// <b>La ruine.</b> Quand il n'y a plus rien à vendre et qu'une dette subsiste, le
    /// produit des ventes et toute la caisse sont déjà partis la rembourser : le reste
    /// ne sera jamais couvert par des titres. Il est constaté en dette résiduelle.
    /// </para>
    /// </summary>
    public static void EnforceMarginCall(FinanceState state, FinanceDef def, Tycoon tycoon, SimTick tick)
    {
        if (tycoon.Ruined) return;
        decimal loan = tycoon.MarginLoan;
        if (loan <= 0m) return;

        decimal maintenance = def.Tycoon.MarginMaintenanceRatio;
        decimal deficit = loan - Money.Round(maintenance * tycoon.PortfolioMarketValue(state));
        if (deficit <= 0m) return;

        tycoon.MarginCalls++;
        decimal priceBefore = state.Player?.SharePrice ?? 0m;
        long soldToday = 0;
        int rounds = 0;
        int maxRounds = Math.Max(1, def.Tycoon.MarginCallMaxRounds);
        bool cured = false;

        while (!cured && rounds < maxRounds)
        {
            bool soldThisRound = false;
            foreach (var position in tycoon.Portfolio.Positions)
            {
                if (position.Shares <= 0) continue;
                var company = state.CompanyById(position.CompanyId);
                if (company is null || company.Merged || company.SharePrice <= 0m) continue;

                decimal unitRelief = company.SharePrice * (1m - maintenance);
                if (unitRelief <= 0m) continue;

                // Ce qui suffirait au cours du moment. C'est un minimum : la vente fera
                // baisser le cours, donc rapportera moins et dépréciera le reste de la
                // ligne. Le tour suivant relit le compte et revend ce qui manque.
                long needed = (long)decimal.Ceiling(deficit / unitRelief);
                long lot = Math.Min(needed, position.Shares);
                if (lot <= 0) continue;

                SellShares(state, def, tycoon, company, lot, tick.Index, forced: true);
                soldToday += lot;
                soldThisRound = true;

                loan = tycoon.MarginLoan;
                deficit = loan - Money.Round(maintenance * tycoon.PortfolioMarketValue(state));
                if (loan <= 0m || deficit <= 0m)
                {
                    cured = true;
                    break;
                }
            }
            if (!soldThisRound) break;
            rounds++;
        }
        tycoon.ForcedSaleRounds += rounds;

        state.Journal.Add(new MarketRecord(tick.Index, MarketRecordKind.MarginCall, state.Player?.Id ?? "",
            soldToday, priceBefore, state.Player?.SharePrice ?? 0m, tycoon.MarginLoan, Rounds: rounds));

        if (tycoon.MarginLoan > 0m && !HoldsAnything(tycoon))
            DeclareRuin(state, tycoon, tick.Index);
    }

    private static bool HoldsAnything(Tycoon tycoon)
    {
        foreach (var position in tycoon.Portfolio.Positions)
            if (position.Shares > 0) return true;
        return false;
    }

    /// <summary>
    /// La ruine : tous les titres sont vendus, la caisse est saisie, et ce qui reste dû
    /// — la marge, et les intérêts de marge impayés — devient une dette résiduelle, au
    /// passif, qui ne porte plus intérêt. Rien ne disparaît : le bilan du magnat
    /// présente une caisse nulle, aucun titre, une dette, et des capitaux propres
    /// négatifs du même montant.
    /// </summary>
    public static void DeclareRuin(FinanceState state, Tycoon tycoon, int tick)
    {
        // La caisse va d'abord aux intérêts courus, puis à la marge : saisie.
        tycoon.InterestPaidTotal += SettleAccrued(tycoon.Book);
        decimal repay = Math.Min(tycoon.MarginLoan, tycoon.Cash);
        if (repay > 0m)
            tycoon.Book.Post("saisie de la caisse", Accounts.MarginLoan, Accounts.Cash, repay);

        decimal loan = tycoon.MarginLoan;
        decimal accrued = tycoon.Book[Accounts.AccruedPayable];
        decimal residual = loan + accrued;
        if (residual <= 0m) return;

        tycoon.Book.Post("ruine : dette résiduelle",
            new Leg(Accounts.MarginLoan, loan),
            new Leg(Accounts.AccruedPayable, accrued),
            new Leg(Accounts.ResidualDebt, -residual));
        tycoon.InterestCapitalizedAtRuin += accrued;

        tycoon.Ruined = true;
        tycoon.RuinTick = tick;
        tycoon.RuinCount++;
        tycoon.ResidualDebtAtRuin = residual;
        state.Journal.Add(new MarketRecord(tick, MarketRecordKind.Ruin, "", 0,
            0m, 0m, residual));
    }

    /// <summary>
    /// Un revenu du magnat — dividende, salaire, prime. Il entre dans sa caisse, puis,
    /// s'il est ruiné, part d'abord rembourser sa dette résiduelle.
    /// </summary>
    public static void ReceiveIncome(FinanceState state, Tycoon tycoon, string label, decimal amount, int tick)
    {
        if (amount <= 0m) return;
        tycoon.Book.Post(label,
            new Leg(Accounts.Cash, amount),
            new Leg(Accounts.PersonalEquity, -amount));
        SettleResidualDebt(state, tycoon, tick);
    }

    /// <summary>
    /// Pendant la ruine, toute la caisse va au courtier jusqu'à ce que la dette
    /// résiduelle soit soldée ; le jour où elle tombe à zéro, le magnat sort de la ruine
    /// et retrouve le droit d'acheter.
    /// </summary>
    public static void SettleResidualDebt(FinanceState state, Tycoon tycoon, int tick)
    {
        if (!tycoon.Ruined) return;
        decimal pay = Math.Min(tycoon.ResidualDebt, tycoon.Cash);
        if (pay > 0m)
        {
            tycoon.Book.Post("remboursement de la dette résiduelle", Accounts.ResidualDebt, Accounts.Cash, pay);
            tycoon.SeizedTotal += pay;
        }
        if (tycoon.ResidualDebt == 0m)
        {
            tycoon.Ruined = false;
            tycoon.RuinClearedTick = tick;
            state.Journal.Add(new MarketRecord(tick, MarketRecordKind.RuinCleared, "", 0,
                0m, 0m, tycoon.SeizedTotal));
        }
    }
}
