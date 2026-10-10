using RailTycoon.Sim.Core;

namespace RailTycoon.Sim.Finance;

/// <summary>
/// Le salaire du président. La compagnie du joueur le verse à la caisse personnelle
/// du magnat, qui la dirige ; il est révisé à chaque clôture d'exercice selon la
/// performance, et complété d'une prime quand l'exercice surperforme.
/// <para>
/// C'est un flux financier comme un dividende : une charge de la compagnie (report à
/// nouveau en baisse), un revenu du magnat (caisse et capitaux propres personnels en
/// hausse), au centime, en partie double. Et comme un dividende, il sort de la
/// trésorerie du transporteur : la position de la compagnie est rapprochée du grand
/// livre en fin de tick, le quatrième flux de <c>bilan-tresorerie</c> le porte.
/// </para>
/// <para>
/// <b>Une charge constatée, payée sur la caisse disponible.</b> Le salaire et la prime
/// sont constatés à leur date, au passif (<c>salary-payable</c>), puis réglés dans la
/// limite de la caisse ; ce qui n'a pas pu l'être reste dû et se règle dès que la
/// caisse le permet. Payer d'office aurait mis la compagnie à découvert le jour où
/// une échéance d'emprunt vide sa caisse — et un découvert, même d'un jour, déclenche
/// le renflouement du magnat : un salaire de mille ferait souscrire deux cent mille.
/// Le magnat ne compte le salaire qu'une fois payé : sa caisse ne connaît pas de
/// créance sur sa propre compagnie.
/// </para>
/// <para>
/// <b>Lecture retenue</b> : sous administration judiciaire, rien ne se constate ni ne
/// se paie — le peu qui rentre va aux créanciers, comme pour les dividendes.
/// </para>
/// </summary>
public static class CeoPay
{
    /// <summary>Ouvre un exercice : ce qu'il faudra comparer à sa clôture.</summary>
    public static void OpenYear(FinanceCompany company)
    {
        company.YearStartRetained = company.Book[Accounts.RetainedEarnings];
        company.YearStartDividends = company.DividendsPaidTotal;
        company.YearStartEquity = company.BookEquity;
        company.YearMarketCapSum = 0m;
        company.YearTicks = 0;
    }

    /// <summary>
    /// Le travail du jour : relever la capitalisation du soir, constater le salaire à
    /// son échéance, clore l'exercice le dernier jour de l'année, puis régler ce qui
    /// est dû dans la limite de la caisse.
    /// </summary>
    public static void Step(FinanceState state, FinanceDef def, FinanceCompany company, Tycoon tycoon, SimTick tick)
    {
        var pay = def.CeoPay;
        if (!pay.Enabled) return;

        company.YearMarketCapSum += company.MarketCap;
        company.YearTicks++;

        if (!company.InReceivership && pay.IntervalTicks > 0 && tick.Index > 0 && tick.Index % pay.IntervalTicks == 0)
        {
            decimal salary = Money.Round(pay.BaseAnnual * company.SalaryFactor * pay.IntervalTicks / SimTick.TicksPerYear);
            Accrue(company, "salaire du président", salary);
            company.SalaryAccruedTotal += salary;
        }

        if (tick.DayOfYear == SimTick.TicksPerYear - 1)
            CloseYear(state, def, company, tick);

        if (!company.InReceivership)
            Settle(state, company, tycoon, tick.Index);
    }

    /// <summary>
    /// Performance de l'exercice : résultat rapporté à la valeur de la compagnie. Le
    /// résultat est la variation du report à nouveau, dividendes rajoutés — un
    /// dividende distribue le résultat, il n'en retranche rien. Nulle si la valeur
    /// n'est pas positive.
    /// </summary>
    public static decimal Performance(CeoPayDef pay, FinanceCompany company, out decimal result, out decimal value)
    {
        result = company.Book[Accounts.RetainedEarnings] - company.YearStartRetained
                 + (company.DividendsPaidTotal - company.YearStartDividends);
        value = pay.PerformanceBase == "bookEquity"
            ? company.YearStartEquity
            : (company.YearTicks > 0 ? Money.Round(company.YearMarketCapSum / company.YearTicks) : 0m);
        return value > 0m ? result / value : 0m;
    }

    /// <summary>
    /// Le facteur du salaire pour une performance donnée : 1 à la cible, plus ou moins
    /// <c>sensitivity</c> par unité d'écart, dans les bornes. Une valeur de compagnie
    /// nulle ou négative ne se rapporte à rien : le facteur plancher.
    /// </summary>
    public static decimal Factor(CeoPayDef pay, decimal performance, decimal value)
    {
        decimal factor = value > 0m ? 1m + pay.Sensitivity * (performance - pay.TargetPercent / 100m) : pay.MinFactor;
        if (factor < pay.MinFactor) factor = pay.MinFactor;
        if (factor > pay.MaxFactor) factor = pay.MaxFactor;
        return factor;
    }

    /// <summary>La prime : une part du résultat obtenu au-delà du seuil de surperformance ; nulle en deçà.</summary>
    public static decimal Bonus(CeoPayDef pay, decimal performance, decimal value)
    {
        decimal threshold = pay.BonusThresholdPercent / 100m;
        if (value <= 0m || performance <= threshold) return 0m;
        return Money.Round(pay.BonusSharePercent / 100m * (performance - threshold) * value);
    }

    private static void CloseYear(FinanceState state, FinanceDef def, FinanceCompany company, SimTick tick)
    {
        var pay = def.CeoPay;
        decimal performance = Performance(pay, company, out _, out decimal value);
        decimal factor = Factor(pay, performance, value);
        decimal bonus = company.InReceivership ? 0m : Bonus(pay, performance, value);
        if (bonus > 0m)
        {
            Accrue(company, "prime du président", bonus);
            company.BonusAccruedTotal += bonus;
        }

        company.SalaryFactor = factor;
        company.LastPerformance = performance;
        state.Journal.Add(new MarketRecord(tick.Index, MarketRecordKind.PayReview, company.Id, 0,
            0m, 0m, Money.Round(pay.BaseAnnual * factor), Rate: Math.Round(performance, 6), Bonus: bonus));

        // La prime est une charge de l'exercice clos : l'exercice suivant s'ouvre après elle.
        OpenYear(company);
    }

    private static void Accrue(FinanceCompany company, string label, decimal amount)
    {
        if (amount <= 0m) return;
        company.Book.Post(label,
            new Leg(Accounts.RetainedEarnings, amount),
            new Leg(Accounts.SalaryPayable, -amount));
    }

    /// <summary>Règle au président ce qui lui est dû, dans la limite de la caisse de la compagnie.</summary>
    private static void Settle(FinanceState state, FinanceCompany company, Tycoon tycoon, int tick)
    {
        decimal due = company.Book[Accounts.SalaryPayable];
        decimal paid = Math.Min(due, Math.Max(0m, company.Cash));
        if (paid <= 0m) return;
        company.Book.Post("paiement du président", Accounts.SalaryPayable, Accounts.Cash, paid);
        company.SalaryPaidTotal += paid;
        tycoon.PayReceivedTotal += paid;
        Brokerage.ReceiveIncome(state, tycoon, "rémunération de président", paid, tick);
    }
}
