using RailTycoon.Sim.Core;

namespace RailTycoon.Sim.Finance;

/// <summary>
/// La bourse : comment un ordre s'exécute contre le flottant, comment les autres
/// actionnaires réagissent à une montée soudaine, et comment une action se divise.
/// <para>
/// <b>Pourquoi un fichier à part.</b> Ces règles valent pour toutes les compagnies
/// cotées et pour tous les acheteurs — le magnat, une compagnie qui monte au capital
/// d'une autre, le courtier qui liquide. Les tenir en un seul endroit est ce qui
/// garantit qu'aucun acheteur n'échappe au carnet : la dette n° 1 du module venait
/// précisément d'un flottant qui absorbait n'importe quel volume au cours affiché.
/// </para>
/// <para>
/// Aucune de ces règles ne tire d'aléa, et aucune n'écrit au grand livre : un ordre
/// renvoie son montant, et c'est l'appelant qui passe l'écriture. Les prises de
/// bénéfices font changer des titres de mains entre actionnaires que la simulation ne
/// modélise pas — le flottant vend au flottant — donc aucun centime n'entre ni ne sort
/// d'un bilan tenu : seul le cours bouge.
/// </para>
/// </summary>
public static class StockMarket
{
    // ------------------------------------------------------------------ carnet

    /// <summary>Taille d'une tranche du carnet, en actions ; 0 quand la profondeur est infinie.</summary>
    public static long TrancheShares(FinanceDef def, FinanceCompany company)
    {
        decimal fraction = def.Market.BookTrancheFraction;
        if (fraction <= 0m || company.Register.SharesIssued <= 0) return 0;
        long tranche = (long)(fraction * company.Register.SharesIssued);
        return tranche < 1 ? 1 : tranche;
    }

    public static decimal PriceFloor(FinanceDef def, decimal price)
    {
        decimal floor = Money.Round(def.Valuation.MinSharePrice);
        return price < floor ? floor : price;
    }

    /// <summary>
    /// Le cours après l'échange de <paramref name="shares"/> titres au cours
    /// <paramref name="price"/>, dans le sens <paramref name="direction"/> (+1 achat,
    /// −1 vente) : l'impact de marché, proportionnel à la fraction du capital échangée,
    /// arrondi au centime, borné par le plancher. Pur : ne touche à rien.
    /// </summary>
    public static decimal ImpactedPrice(FinanceDef def, FinanceCompany company, decimal price, long shares, int direction)
    {
        decimal impact = def.Valuation.MarketImpact;
        if (impact <= 0m || shares <= 0 || company.Register.SharesIssued <= 0) return price;

        decimal move = Money.Round(price * impact * shares / company.Register.SharesIssued);
        if (move <= 0m) return price;
        return PriceFloor(def, price + direction * move);
    }

    /// <summary>
    /// Ce que coûterait (ou rapporterait) l'échange de <paramref name="shares"/> titres,
    /// et le cours où il laisserait la cote, sans rien exécuter.
    /// <para>
    /// Tranche par tranche : chacune au cours du moment, puis l'impact de la tranche
    /// avant la suivante. Profondeur infinie : un seul bloc au cours d'avant, puis
    /// l'impact du bloc — exactement l'exécution d'avant la profondeur finie, au
    /// centime. Chaque tranche se règle en centimes entiers : un nombre entier de
    /// titres à un cours en centimes.
    /// </para>
    /// </summary>
    public static (decimal Amount, decimal FinalPrice) Quote(FinanceDef def, FinanceCompany company, long shares, int direction)
    {
        decimal price = company.SharePrice;
        if (shares <= 0) return (0m, price);

        long tranche = TrancheShares(def, company);
        if (tranche <= 0) tranche = shares;

        decimal amount = 0m;
        long remaining = shares;
        while (remaining > 0)
        {
            long q = Math.Min(tranche, remaining);
            amount += q * price;
            price = ImpactedPrice(def, company, price, q, direction);
            remaining -= q;
        }
        return (amount, price);
    }

    /// <summary>Exécute l'échange : renvoie son montant et laisse la cote au cours d'après. L'écriture est à l'appelant.</summary>
    public static decimal Execute(FinanceDef def, FinanceCompany company, long shares, int direction)
    {
        var (amount, finalPrice) = Quote(def, company, shares, direction);
        company.SharePrice = finalPrice;
        return amount;
    }

    /// <summary>
    /// Le plus grand nombre de titres qu'un budget achète en remontant le carnet, dans
    /// la limite de <paramref name="limit"/>. Profondeur infinie : le budget divisé par
    /// le cours, comme avant.
    /// </summary>
    public static long MaxSharesFor(FinanceDef def, FinanceCompany company, decimal budget, long limit = long.MaxValue)
    {
        decimal price = company.SharePrice;
        if (budget <= 0m || price <= 0m) return 0;

        long tranche = TrancheShares(def, company);
        if (tranche <= 0) return (long)(budget / price);

        long bought = 0;
        while (bought < limit && price > 0m)
        {
            long q = Math.Min(tranche, limit - bought);
            decimal cost = q * price;
            if (cost > budget)
            {
                bought += (long)(budget / price);
                break;
            }
            budget -= cost;
            bought += q;
            price = ImpactedPrice(def, company, price, q, +1);
        }
        return Math.Min(bought, limit);
    }

    // --------------------------------------------------------------- références

    /// <summary>
    /// Après la valorisation du jour : la valeur fondamentale par action est retenue,
    /// et le cours de référence des investisseurs avance — jusqu'au cours du jour s'il
    /// a baissé, d'une fraction <c>1 / referenceTicks</c> de l'écart s'il a monté.
    /// <para>
    /// Asymétrique à dessein. Ce que les investisseurs jugent « monté trop vite »,
    /// c'est une hausse par rapport à ce qu'ils ont récemment payé : après un krach,
    /// le prix récent est le bas, pas le haut d'avant la crise. Une référence qui
    /// traînerait le haut d'avant ne verrait pas une manipulation au plus bas.
    /// </para>
    /// </summary>
    public static void UpdateReferences(FinanceDef def, FinanceCompany company, decimal intrinsicPrice)
    {
        company.IntrinsicPrice = intrinsicPrice;
        int window = Math.Max(1, def.Market.ProfitTaking.ReferenceTicks);
        decimal price = company.SharePrice;
        decimal rising = Money.Round(company.ReferencePrice + (price - company.ReferencePrice) / window);
        company.ReferencePrice = price < rising ? price : rising;
    }

    // -------------------------------------------------------- prises de bénéfices

    /// <summary>
    /// Les prises de bénéfices du jour sur une compagnie. Si le cours dépasse le seuil
    /// — la plus haute des deux références, majorée de <c>thresholdPercent</c> —, les
    /// investisseurs vendent au flottant, tranche par tranche dans le carnet, jusqu'à
    /// avoir refermé la part <c>speed</c> de l'excès, sans céder plus de
    /// <c>maxSellFraction</c> de leurs titres. Renvoie le nombre de titres qui ont
    /// changé de mains.
    /// <para>
    /// <b>Qui vend à qui.</b> Les vendeurs sont des actionnaires du flottant, les
    /// acheteurs aussi : « d'autres investisseurs », que la simulation ne modélise pas
    /// un par un. Au registre, la ligne du flottant cède et reçoit le même nombre de
    /// titres ; celles du magnat et des compagnies ne bougent pas, et le total émis
    /// non plus. Aucun argent tenu au bilan ne bouge : c'est le prix auquel le reste
    /// du marché accepte de racheter qui baisse, et c'est tout ce que la mécanique doit
    /// faire.
    /// </para>
    /// </summary>
    public static long TakeProfits(FinanceState state, FinanceDef def, FinanceCompany company, int tick)
    {
        var rule = def.Market.ProfitTaking;
        if (!rule.Enabled || company.Merged || company.Register.SharesIssued <= 0) return 0;

        decimal before = company.SharePrice;
        decimal reference = Math.Max(company.IntrinsicPrice, company.ReferencePrice);
        if (reference <= 0m) return 0;
        decimal trigger = Money.Round(reference * (1m + rule.ThresholdPercent / 100m));
        if (before <= trigger) return 0;

        decimal speed = rule.Speed <= 0m ? 0m : (rule.Speed > 1m ? 1m : rule.Speed);
        decimal goal = before - Money.Round(speed * (before - trigger));
        long floating = company.Register.HeldBy(Holders.Float);
        long cap = (long)(rule.MaxSellFraction * floating);
        if (cap <= 0 || goal >= before) return 0;

        long sold = 0;
        long tranche = TrancheShares(def, company);
        if (tranche <= 0)
        {
            // Profondeur infinie : un bloc, dimensionné pour atteindre l'objectif d'un
            // coup — la vente d'un bloc ne se découpe pas.
            decimal impact = def.Valuation.MarketImpact;
            if (impact <= 0m) return 0;
            decimal needed = (before - goal) * company.Register.SharesIssued / (before * impact);
            sold = Math.Min(cap, (long)decimal.Ceiling(needed));
            company.SharePrice = ImpactedPrice(def, company, before, sold, -1);
        }
        else
        {
            while (company.SharePrice > goal && sold < cap)
            {
                long q = Math.Min(tranche, cap - sold);
                decimal next = ImpactedPrice(def, company, company.SharePrice, q, -1);
                if (next == company.SharePrice) break;
                company.SharePrice = next;
                sold += q;
            }
        }
        if (sold <= 0) return 0;

        // Au registre : le flottant vend au flottant, la ligne ne change pas. Le
        // transfert est passé quand même : il ne déplace rien, mais il refuse une
        // cession de plus de titres que le flottant n'en détient.
        company.Register.Transfer(Holders.Float, Holders.Float, sold);
        company.ProfitTakingShares += sold;
        company.ProfitTakingDays++;
        state.Journal.Add(new MarketRecord(tick, MarketRecordKind.ProfitTaking, company.Id, sold,
            before, company.SharePrice, 0m, Rate: trigger));
        return sold;
    }

    // --------------------------------------------------------- division d'actions

    /// <summary>
    /// À la clôture de l'année (dernier jour de l'exercice), divise les actions de
    /// chaque compagnie cotée dont le cours finit au-dessus du seuil.
    /// </summary>
    public static void SplitIfDue(FinanceState state, FinanceDef def, SimTick tick)
    {
        var rule = def.Market.Split;
        if (!rule.Enabled || tick.DayOfYear != SimTick.TicksPerYear - 1) return;
        foreach (var company in state.Companies)
        {
            if (company.Merged || company.Register.SharesIssued <= 0) continue;
            if (company.SharePrice > rule.PriceThreshold)
                Split(state, company, rule.Ratio, tick.Index);
        }
    }

    /// <summary>
    /// Division d'actions : chaque actionnaire — magnat, flottant, compagnie qui détient
    /// une participation — détient <paramref name="ratio"/> fois plus de titres ; le
    /// cours, la valeur fondamentale et le cours de référence sont divisés d'autant ;
    /// les prix de revient totaux ne bougent pas, les prix de revient unitaires suivent
    /// donc d'eux-mêmes.
    /// <para>
    /// Le cours est divisé <em>exactement</em>, sans arrondi au centime : 101,23 devient
    /// 50,615. C'est ce qui garde la capitalisation et chaque fortune identiques au
    /// centime. Le demi-centime ne vit que jusqu'à la valorisation du lendemain, qui
    /// arrondit ; aucun ordre ne s'exécute entre les deux.
    /// </para>
    /// </summary>
    public static void Split(FinanceState state, FinanceCompany company, int ratio, int tick)
    {
        if (ratio < 2) return;
        decimal before = company.SharePrice;

        company.Register.Split(ratio);
        state.Magnate?.Portfolio.Split(company.Id, ratio);
        foreach (var holder in state.Companies)
            holder.Investments.Split(company.Id, ratio);

        company.SharePrice = before / ratio;
        company.IntrinsicPrice /= ratio;
        company.ReferencePrice /= ratio;
        company.SplitFactor *= ratio;
        company.SharesIssuedInRescues *= ratio;

        state.Journal.Add(new MarketRecord(tick, MarketRecordKind.Split, company.Id, ratio,
            before, company.SharePrice, company.MarketCap));
    }

    /// <summary>Un rapport de division qui s'écrit exactement en décimal : de la forme 2^a·5^b.</summary>
    public static bool IsExactRatio(int ratio)
    {
        if (ratio < 2) return false;
        while (ratio % 2 == 0) ratio /= 2;
        while (ratio % 5 == 0) ratio /= 5;
        return ratio == 1;
    }
}
