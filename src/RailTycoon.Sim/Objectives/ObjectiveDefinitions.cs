namespace RailTycoon.Sim.Objectives;

// Données de conception du module objectives, chargées depuis le bloc
// « objectives » d'un scénario. Comme pour les autres modules, rien d'équilibrable
// ne vit dans le code : les objectifs, leurs cibles, leurs échéances, leurs paliers
// et la fenêtre de lecture de la fortune sont tous ici. Ce que la vision laisse aux
// scénarios — quels paliers valent victoire, ce qui fait perdre, la période, la
// géographie (docs/VISION.md, « Gagner ») — n'est pas décidé ici non plus : le bloc
// en porte les points d'accroche, et personne ne les interprète.

/// <summary>Les trois sortes d'objectifs que la vision décide, et elles seules.</summary>
public static class ObjectiveKinds
{
    /// <summary>
    /// Amasser une fortune personnelle : celle du magnat (<c>Tycoon.NetWorth</c>),
    /// jamais la trésorerie de sa compagnie. Exige le module finance.
    /// </summary>
    public const string Fortune = "fortune";

    /// <summary>
    /// Livrer des cargaisons d'une marchandise, au total ou vers une ville précise.
    /// « Livré » veut dire <em>laissé par le rail</em> : vendu moins racheté, ville
    /// par ville (<see cref="Transport.FreightLedger.NetDelivered"/>).
    /// </summary>
    public const string Deliveries = "deliveries";

    /// <summary>
    /// Relier deux villes : un même train de la compagnie, sur une même ligne, s'est
    /// arrêté dans l'une et dans l'autre.
    /// </summary>
    public const string Connect = "connect";
}

/// <summary>
/// Une échéance : un tick, ou une date du calendrier de jeu (douze mois de trente
/// jours, <see cref="Core.SimTick.TicksPerYear"/> = 360). L'un ou l'autre, jamais
/// les deux. Elle est <b>incluse</b> : un palier atteint le jour de son échéance est
/// atteint ; il est manqué au soir de ce jour s'il ne l'est pas.
/// </summary>
public sealed class DeadlineDef
{
    public int? Tick { get; set; }
    public int? Year { get; set; }
    public int? Month { get; set; }
    public int? Day { get; set; }
}

/// <summary>
/// Un palier d'un objectif : une cible et, s'il y en a une, une échéance.
/// <para>
/// <b>Point d'accroche, pas une règle.</b> Un objectif porte un ou plusieurs
/// paliers sur la même mesure — une fortune de 500 000 avant 1873 et de 1 000 000
/// avant 1876, ou deux échéances pour la même liaison. Le module dit, pour chacun,
/// s'il est atteint (et quand), en cours, ou manqué. Ce qu'un palier <em>vaut</em>
/// — victoire, médaille, simple jalon, défaite s'il est manqué — n'est pas décidé
/// (docs/VISION.md, « Gagner » : « les paliers de réussite, ce qui fait perdre… se
/// décideront avec les scénarios ») : rien dans le code ne le lit.
/// </para>
/// </summary>
public sealed class ObjectiveTierDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>
    /// Seuil de la mesure : une fortune (en monnaie, <c>decimal</c> comme toute la
    /// finance) ou un nombre de chargements. Obligatoire et strictement positif pour
    /// une fortune et des livraisons ; interdit pour une liaison, qui est atteinte ou
    /// ne l'est pas.
    /// </summary>
    public decimal? Target { get; set; }

    /// <summary>Échéance, incluse. Absente : le palier ne peut pas être manqué, seulement rester en cours.</summary>
    public DeadlineDef? Deadline { get; set; }
}

/// <summary>Un objectif de scénario : une mesure, et ses paliers.</summary>
public sealed class ObjectiveDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary><c>"fortune"</c>, <c>"deliveries"</c> ou <c>"connect"</c> (<see cref="ObjectiveKinds"/>).</summary>
    public string Kind { get; set; } = "";

    /// <summary>
    /// Fortune seulement, et obligatoire : sur combien de ticks la fortune se lit.
    /// 1 = la fortune au soir du tick courant ; N = la moyenne des N derniers soirs,
    /// et aucun palier ne peut être atteint avant que N soirs soient passés.
    /// <para>
    /// Obligatoire parce que ce n'est pas un détail : tant que le flottant est une
    /// contrepartie de profondeur infinie (docs/CONTRACTS.md, dette connue n° 1 de la
    /// finance), la fortune du magnat se manipule, et un tick d'ordre de bourse porte
    /// encore l'impact de l'ordre (dette n° 2). Lire au tick courant ou en moyenne
    /// glissante change la date à laquelle un palier tombe ; le choix revient au
    /// scénario, chiffré dans docs/FINDINGS.md, section « Les objectifs ».
    /// </para>
    /// </summary>
    public int? AverageTicks { get; set; }

    /// <summary>Livraisons seulement, et obligatoire : la marchandise.</summary>
    public string Cargo { get; set; } = "";

    /// <summary>Livraisons seulement : la ville de destination. Vide = au total, toutes villes confondues.</summary>
    public string City { get; set; } = "";

    /// <summary>Liaison seulement : les deux villes à relier, distinctes.</summary>
    public List<string> Cities { get; set; } = new();

    /// <summary>Un palier au moins.</summary>
    public List<ObjectiveTierDef> Tiers { get; set; } = new();
}

/// <summary>
/// Configuration du module objectives. Une seule propriété est ajoutée à
/// <see cref="Economy.ScenarioDef"/> — <c>Objectives</c>.
/// <para>
/// <b>Un observateur pur.</b> Actif ou non, le module ne touche ni stock, ni prix,
/// ni argent, ni décision, et ne tire aucun aléa : il lit l'état du monde au soir de
/// chaque tick et tient un journal. Un test vérifie que la partie est la même, au
/// bit près, avec et sans objectifs.
/// </para>
/// </summary>
public sealed class ObjectivesDef
{
    public bool Enabled { get; set; }

    /// <summary>
    /// Année calendaire du tick 0, pour les échéances données par date. Absente, le
    /// module prend celle du bloc <c>events</c> ; données toutes deux, elles doivent
    /// être égales — un scénario n'a qu'un calendrier.
    /// </summary>
    public int StartYear { get; set; }

    /// <summary>
    /// Les objectifs du scénario, évalués indépendamment les uns des autres, dans
    /// l'ordre des données. Comment ils se combinent en une victoire (tous ? un
    /// nombre ? un score ?) n'est pas décidé : le journal dit l'état de chacun.
    /// </summary>
    public List<ObjectiveDef> Goals { get; set; } = new();
}
