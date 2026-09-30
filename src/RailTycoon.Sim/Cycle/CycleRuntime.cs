using System.Globalization;
using RailTycoon.Sim.Core;

namespace RailTycoon.Sim.Cycle;

/// <summary>Ce qui a fait changer la conjoncture.</summary>
public enum CycleCause
{
    /// <summary>La phase du tick 0, fixée par le scénario.</summary>
    Opening,

    /// <summary>La phase précédente est arrivée à son terme.</summary>
    Elapsed,

    /// <summary>Un événement a forcé la phase (attribut <c>forcePhase</c>).</summary>
    Forced,

    /// <summary>La poussée d'un événement a amené la phase précédente à son terme le jour même.</summary>
    Pushed,
}

/// <summary>Nature d'une entrée du journal de la conjoncture.</summary>
public enum CycleRecordKind
{
    /// <summary>Changement de phase.</summary>
    Phase,

    /// <summary>Un événement a déplacé la fin de la phase en cours.</summary>
    Shift,
}

/// <summary>
/// Une entrée du journal public de la conjoncture.
/// <para>
/// <b>Ce que le journal dit, et ce qu'il tait.</b> Il publie la phase courante, le
/// jour où elle a commencé, ce qui l'a ouverte, et chaque poussée d'un événement
/// avec son ampleur. Il ne publie <em>jamais</em> la date prévue de la fin d'une
/// phase. C'est la différence délibérée avec le journal des événements, qui annonce
/// la fin d'une grève dès son début : personne ne sait quand une expansion
/// s'arrête, et « garder de la trésorerie avant la crise » (docs/VISION.md) doit
/// rester un pari, pas une lecture. Un concurrent IA lit ce journal, et rien de
/// plus.
/// </para>
/// </summary>
public sealed class CycleRecord
{
    public required int Tick { get; init; }
    public required CycleRecordKind Kind { get; init; }

    /// <summary>Phase en vigueur après l'entrée.</summary>
    public required string PhaseId { get; init; }

    /// <summary>Phase quittée, pour un changement de phase ; vide à l'ouverture et pour une poussée.</summary>
    public string PreviousPhaseId { get; init; } = "";

    public CycleCause Cause { get; init; }

    /// <summary>Occurrence d'événement responsable (identifiant d'instance du journal des événements), s'il y en a une.</summary>
    public string EventInstanceId { get; init; } = "";

    /// <summary>
    /// Pour une poussée : de combien de jours la phase en cours est allongée
    /// (positif) ou abrégée (négatif). C'est la poussée de l'événement, signée selon
    /// la phase : une bonne nouvelle allonge une phase favorable.
    /// </summary>
    public int ShiftTicks { get; init; }
}

/// <summary>
/// Le taux d'un emprunt, décomposé. La vision (docs/VISION.md, « Emprunter ») veut
/// un taux qui dépende de trois choses ; les voici, terme à terme, pour qu'un
/// joueur lise pourquoi son emprunt lui coûte ce qu'il coûte.
/// <code>
/// taux = max(plancher, facial + conjoncture + prime de risque − bonus du dirigeant)
/// </code>
/// </summary>
/// <param name="FacialPercent">Taux de l'offre dans les données : le prix du crédit en conjoncture neutre.</param>
/// <param name="PhaseAdjustmentPercent">Ajustement de la phase au jour de l'émission, transition comprise.</param>
/// <param name="RiskPremiumPercent">Prime de risque selon la compagnie : levier et rentabilité.</param>
/// <param name="LeaderBonusPercent">
/// Bonus selon le score du dirigeant. <b>Toujours 0 aujourd'hui</b> : le score
/// n'existe pas encore (docs/VISION.md, « Le score de dirigeant »). C'est le point
/// d'accroche, et il est à sa place dans la formule plutôt qu'inventé.
/// </param>
/// <param name="RatePercent">Taux retenu, en pourcentage annuel.</param>
/// <param name="PhaseId">Phase au jour de l'émission ; vide si le module est inactif.</param>
public sealed record CreditQuote(
    decimal FacialPercent,
    decimal PhaseAdjustmentPercent,
    decimal RiskPremiumPercent,
    decimal LeaderBonusPercent,
    decimal RatePercent,
    string PhaseId);

/// <summary>
/// L'état du module cycle : la phase en cours, les conditions du jour que les autres
/// modules lisent, et le journal public. Inactif et vide tant qu'un scénario ne
/// l'active pas.
/// <para>
/// <b>Les conditions du jour</b> sont la seule chose que les autres modules lisent :
/// la finance y prend l'ajustement des taux et le facteur du multiple, l'économie y
/// prend la demande (publiée sur chaque marché, <see cref="Economy.Market.CycleDemandFactor"/>),
/// et le futur module de fondation y prendra l'appétit des investisseurs. Toute la
/// logique — tirages, bascules, poussées, transitions — reste ici.
/// </para>
/// </summary>
public sealed class CycleState
{
    public bool Enabled { get; internal set; }
    public CycleDef Def { get; internal set; } = new();

    /// <summary>Aléa du module, sur une séquence qui lui est propre.</summary>
    public DeterministicRandom? Rng { get; internal set; }

    /// <summary>Phase en cours. Nulle tant que le module est inactif.</summary>
    public CyclePhaseDef? Phase { get; internal set; }

    /// <summary>Premier tick de la phase en cours.</summary>
    public int PhaseStartTick { get; internal set; }

    /// <summary>
    /// Dernier tick prévu de la phase en cours, poussées comprises. <b>Interne</b> :
    /// ce n'est pas une information de jeu (voir <see cref="CycleRecord"/>).
    /// </summary>
    internal int PhaseEndTick { get; set; }

    // --- Conditions du jour. Valeurs neutres tant que le module est inactif.

    /// <summary>Points ajoutés au taux facial d'un emprunt émis aujourd'hui, et au taux du découvert.</summary>
    public decimal RateAdjustmentPercent { get; internal set; }

    /// <summary>Multiplicateur du multiple de valorisation de la bourse.</summary>
    public decimal EarningsMultipleFactor { get; internal set; } = 1m;

    /// <summary>Multiplicateur de la demande des habitants, publié aussi sur chaque marché.</summary>
    public double DemandFactor { get; internal set; } = 1.0;

    /// <summary>Point d'accroche du futur module de fondation : appétit des investisseurs. Lu par personne aujourd'hui.</summary>
    public decimal InvestorContributionFactor { get; internal set; } = 1m;

    /// <summary>Point d'accroche du futur score de dirigeant : patience des investisseurs. Lu par personne aujourd'hui.</summary>
    public decimal InvestorPatienceFactor { get; internal set; } = 1m;

    private readonly List<CycleRecord> _journal = new();

    /// <summary>Journal public : changements de phase et poussées, dans l'ordre.</summary>
    public IReadOnlyList<CycleRecord> Journal => _journal;

    internal void Log(CycleRecord record) => _journal.Add(record);

    /// <summary>
    /// Prix d'un emprunt émis aujourd'hui. Module inactif : le taux facial, tel
    /// quel, sans aucune opération — c'est ce qui garde la finance d'avant le module
    /// au centime près.
    /// </summary>
    /// <param name="facialPercent">Taux de l'offre dans les données.</param>
    /// <param name="debtAfter">Dette obligataire, découvert et principal demandé, additionnés.</param>
    /// <param name="bookEquity">Capitaux propres comptables avant l'emprunt.</param>
    /// <param name="annualEarnings">Résultat annualisé, lissé comme celui de la valorisation.</param>
    public CreditQuote QuoteBond(decimal facialPercent, decimal debtAfter, decimal bookEquity, decimal annualEarnings)
    {
        if (!Enabled || Phase is null)
            return new CreditQuote(facialPercent, 0m, 0m, 0m, facialPercent, "");

        var risk = Def.Credit.RiskPremium;
        decimal premium;
        if (bookEquity <= 0m)
        {
            // Un bilan sans capitaux propres ne se lit pas en levier : la dette y est
            // infinie par rapport au gage. La prime est maximale.
            premium = risk.MaxPercent;
        }
        else
        {
            decimal leverage = debtAfter / bookEquity;
            decimal roe = annualEarnings / bookEquity;
            premium = risk.LeverageSlopePercent * Math.Max(0m, leverage - risk.LeverageThreshold)
                    + risk.LossSlopePercent * Math.Max(0m, -roe);
            if (premium > risk.MaxPercent) premium = risk.MaxPercent;
        }
        premium = Math.Round(premium, 2, MidpointRounding.ToEven);

        // Point d'accroche du score de dirigeant (docs/VISION.md, « Emprunter » :
        // « moins un bonus selon le score du dirigeant »). Le score n'existe pas :
        // le bonus vaut 0, et c'est ici qu'il se soustraira.
        const decimal leaderBonus = 0m;

        decimal rate = facialPercent + RateAdjustmentPercent + premium - leaderBonus;
        if (rate < Def.Credit.MinRatePercent) rate = Def.Credit.MinRatePercent;
        return new CreditQuote(facialPercent, RateAdjustmentPercent, premium, leaderBonus, rate, Phase.Id);
    }

    /// <summary>
    /// Taux du découvert aujourd'hui. Le découvert est une facilité renégociée au jour
    /// le jour : son taux suit la conjoncture en continu, contrairement à celui d'une
    /// obligation, fixé à l'émission. Il ne porte pas de prime de risque : le
    /// découvert est déjà gagé sur les capitaux propres et délibérément punitif.
    /// </summary>
    public decimal OverdraftRate(decimal basePercent)
    {
        if (!Enabled) return basePercent;
        decimal rate = basePercent + RateAdjustmentPercent;
        return rate < Def.Credit.MinRatePercent ? Def.Credit.MinRatePercent : rate;
    }

    /// <summary>
    /// Résumé du journal, pour l'empreinte des traces de référence. Chaîne vide quand
    /// le module est inactif : l'empreinte des scénarios sans cycle reste celle d'avant
    /// le module.
    /// </summary>
    public string Summary()
    {
        if (!Enabled) return "";
        var ci = CultureInfo.InvariantCulture;
        return "|cycle|" + string.Join(";", _journal.Select(r => r.Kind == CycleRecordKind.Phase
            ? $"{r.Tick.ToString(ci)}:{r.PreviousPhaseId}>{r.PhaseId}:{r.Cause}:{r.EventInstanceId}"
            : $"{r.Tick.ToString(ci)}:{r.PhaseId}{r.ShiftTicks.ToString("+0;-0", ci)}:{r.EventInstanceId}"));
    }
}
