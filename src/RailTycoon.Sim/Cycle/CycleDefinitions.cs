namespace RailTycoon.Sim.Cycle;

// Données de conception du module cycle, chargées depuis le bloc « cycle » d'un
// scénario. Comme pour les autres modules, rien d'équilibrable ne vit dans le code :
// les phases, leur ordre, leurs bornes de durée, leurs effets et le prix du risque
// sont tous ici, et le solveur ne fait que les appliquer. Les grandeurs lues par la
// finance sont en decimal, comme toute la finance ; celle que lit l'économie est en
// double, comme toute l'économie (voir docs/ARCHITECTURE.md, « La monnaie »).

/// <summary>
/// Une phase de la conjoncture : combien de temps elle dure, et ce qu'elle fait aux
/// taux, à la bourse, aux investisseurs et à la demande des villes.
/// <para>
/// Les phases se succèdent dans l'ordre de la liste, en boucle : expansion,
/// ralentissement, crise, reprise, puis de nouveau expansion. Leur nom n'a aucun
/// sens pour le code — c'est <see cref="Favorable"/> qui dit dans quel sens un
/// événement la pousse, et <see cref="CycleDef.InitialPhase"/> ou l'attribut
/// <c>forcePhase</c> d'un événement qui la désignent par son identifiant.
/// </para>
/// </summary>
public sealed class CyclePhaseDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>
    /// Vrai pour une phase où les affaires vont bien (expansion, reprise), faux
    /// pour une phase où elles vont mal (ralentissement, crise). C'est ce qui donne
    /// son sens à la poussée d'un événement : une bonne nouvelle allonge une phase
    /// favorable et abrège une phase défavorable, une mauvaise fait l'inverse. Un
    /// seul signe dans les données, deux effets selon le moment : une fièvre de
    /// construction en pleine crise hâte la reprise, en pleine expansion elle
    /// retarde le ralentissement.
    /// </summary>
    public bool Favorable { get; set; }

    /// <summary>
    /// Bornes de la durée de la phase, en ticks, incluses. La durée est tirée
    /// uniformément entre les deux, une seule fois, au début de la phase, sur la
    /// séquence propre du module. Elle n'est pas publiée : voir
    /// <see cref="CycleState"/>.
    /// </summary>
    public int MinTicks { get; set; }
    public int MaxTicks { get; set; }

    /// <summary>
    /// Points de pourcentage ajoutés au taux facial d'un emprunt émis pendant la
    /// phase, et au taux du découvert tant qu'elle dure. −1 en expansion : le
    /// crédit est bon marché ; +3 en crise : il est cher.
    /// </summary>
    public decimal RateAdjustmentPercent { get; set; }

    /// <summary>
    /// Multiplicateur du multiple de valorisation (<c>valuation.earningsMultiple</c>
    /// du bloc finance). 1,3 en expansion, 0,6 en crise : c'est ce qui fait monter
    /// les cours dans l'une et les fait s'effondrer dans l'autre.
    /// </summary>
    public decimal EarningsMultipleFactor { get; set; } = 1m;

    /// <summary>
    /// Multiplicateur de la demande des habitants, dans toutes les villes et pour
    /// toutes les marchandises à la fois. Délibérément modeste — de l'ordre de
    /// quelques pour cent : un choc qui touche toutes les villes à la fois fait
    /// bouger tous les prix ensemble et ne crée aucun écart à exploiter
    /// (docs/FINDINGS.md, « Événements historiques et aléatoires », scope « all »).
    /// </summary>
    public double DemandFactor { get; set; } = 1.0;

    /// <summary>
    /// <b>Point d'accroche, lu par personne aujourd'hui.</b> Multiplicateur de
    /// l'apport des investisseurs à la fondation d'une compagnie. Le module qui
    /// fonde une compagnie n'existe pas encore (docs/VISION.md, « Devenir PDG ») ;
    /// quand il existera, il lira ce nombre dans <see cref="CycleState"/> plutôt
    /// que de réinventer une conjoncture. La vision dit « ils apportent moins en
    /// crise » : c'est tout ce que ce nombre porte.
    /// </summary>
    public decimal InvestorContributionFactor { get; set; } = 1m;

    /// <summary>
    /// <b>Point d'accroche, lu par personne aujourd'hui.</b> Multiplicateur de la
    /// patience des investisseurs envers le dirigeant (« ils patientent moins en
    /// crise »). Il attend le score de dirigeant et le vote d'éviction, qui
    /// n'existent pas encore.
    /// </summary>
    public decimal InvestorPatienceFactor { get; set; } = 1m;
}

/// <summary>
/// La prime de risque d'un emprunteur : ce qu'un prêteur ajoute au taux selon la
/// situation de la compagnie, en points de pourcentage.
/// <code>
/// levier = (dette obligataire + découvert + principal demandé) ÷ capitaux propres
/// ROE    = résultat annualisé (lissé) ÷ capitaux propres
/// prime  = min(maxPercent,
///              leverageSlopePercent × max(0, levier − leverageThreshold)
///            + lossSlopePercent     × max(0, −ROE))
/// </code>
/// Capitaux propres nuls ou négatifs : la prime maximale. Le levier compte le
/// principal demandé parce qu'un prêteur juge le bilan d'après l'emprunt, pas
/// d'avant.
/// </summary>
public sealed class RiskPremiumDef
{
    /// <summary>Levier en deçà duquel la dette ne coûte aucune prime.</summary>
    public decimal LeverageThreshold { get; set; } = 0.5m;

    /// <summary>Points de prime par unité de levier au-delà du seuil.</summary>
    public decimal LeverageSlopePercent { get; set; }

    /// <summary>Points de prime par unité de rentabilité négative (un ROE de −10 % à 10 points par unité coûte 1 point).</summary>
    public decimal LossSlopePercent { get; set; }

    /// <summary>Plafond de la prime. Au-delà, un prêteur ne prête plus : c'est une autre règle, qui n'existe pas encore.</summary>
    public decimal MaxPercent { get; set; } = 5m;
}

/// <summary>Le prix du crédit tel que la conjoncture le fixe.</summary>
public sealed class CreditDef
{
    /// <summary>Plancher du taux d'un emprunt, quelle que soit la conjoncture.</summary>
    public decimal MinRatePercent { get; set; } = 1m;

    public RiskPremiumDef RiskPremium { get; set; } = new();
}

/// <summary>
/// Configuration du module cycle. Une seule propriété est ajoutée à
/// <see cref="Economy.ScenarioDef"/> — <c>Cycle</c>.
/// <para>
/// <b>Inactif par défaut, et neutre au bit près.</b> Sans bloc, ou avec
/// <c>enabled = false</c>, le module ne tire rien, ne publie rien, et la finance
/// suit le chemin de code d'avant son arrivée : taux facial, multiple du scénario,
/// demande à ×1 exactement. Vérifié par toutes les traces de référence, qui n'ont
/// pas bougé.
/// </para>
/// </summary>
public sealed class CycleDef
{
    public bool Enabled { get; set; }

    /// <summary>
    /// Séquence PCG propre au module. Doit différer de 1 (le monde) et de celles de
    /// la finance et des événements : activer le cycle ne décale aucun autre tirage.
    /// Un seul tirage par phase, à son ouverture.
    /// </summary>
    public ulong RandomSequence { get; set; } = 13;

    /// <summary>Identifiant de la phase au tick 0. Sa durée est tirée comme celle de toute autre phase.</summary>
    public string InitialPhase { get; set; } = "";

    /// <summary>
    /// Durée, en ticks, du passage des effets d'une phase à ceux de la suivante,
    /// en ligne droite. 0 = marche d'escalier. Une phase change le jour dit ; ses
    /// effets s'installent sur un mois, comme la montée d'un événement.
    /// </summary>
    public int TransitionTicks { get; set; }

    /// <summary>Les phases, dans l'ordre où elles se succèdent, en boucle.</summary>
    public List<CyclePhaseDef> Phases { get; set; } = new();

    public CreditDef Credit { get; set; } = new();
}
