namespace RailTycoon.Sim.Events;

// Données de conception du module events, chargées depuis le bloc « events » d'un
// scénario. Comme pour les autres modules, rien d'équilibrable ne vit dans le
// code : dates, cibles, intensités, durées, fréquences et bornes de sécurité sont
// toutes ici, et le solveur ne fait que les appliquer.

/// <summary>
/// Un effet d'événement sur une marchandise : un multiplicateur appliqué soit au
/// débit de production primaire, soit à la demande des habitants.
/// <para>
/// Ce sont les deux seuls leviers, et c'est délibéré. Un événement ne crée ni ne
/// détruit de marchandise : il change le <em>taux du jour</em> que le solveur
/// économique utilise ensuite, et la matière continue de passer par
/// <c>Market.Produce</c> et <c>Market.Consume</c> (règle commune 2). Une grève
/// n'efface pas le charbon du carreau, elle arrête de l'en sortir.
/// </para>
/// </summary>
public sealed class EventEffectDef
{
    /// <summary>Identifiant de la marchandise touchée.</summary>
    public string Cargo { get; set; } = "";

    /// <summary>
    /// Ce qui est modulé : <c>"production"</c> (débit primaire — ferme, mine,
    /// forêt ; jamais une usine) ou <c>"demand"</c> (consommation des habitants ;
    /// jamais la demande induite par les usines).
    /// </summary>
    public string On { get; set; } = "";

    /// <summary>
    /// Multiplicateur au plus fort de l'événement, pour un événement historique.
    /// 0,5 = la moitié du taux nominal, 2 = le double.
    /// </summary>
    public double Factor { get; set; } = 1.0;

    /// <summary>
    /// Bornes du multiplicateur au plus fort, pour un type aléatoire. L'intensité
    /// est tirée uniformément entre les deux, <em>une seule fois</em> par
    /// occurrence et partagée par tous les effets du type : une mauvaise année est
    /// mauvaise pour toutes ses conséquences à la fois.
    /// </summary>
    public double FactorMin { get; set; } = 1.0;
    public double FactorMax { get; set; } = 1.0;
}

/// <summary>
/// Un événement historique : daté, ciblé, d'intensité et de durée fixées.
/// <para>
/// <b>Deux natures, et la différence n'est pas cosmétique.</b>
/// <c>"historical"</c> exige une source dans docs/SOURCES.md : la date et la nature
/// de l'événement y sont documentées. <c>"inspired"</c> dit qu'on s'inspire d'un
/// fait réel sans pouvoir en documenter l'effet — une sécheresse attestée dont
/// aucune source trouvée ne chiffre la récolte, par exemple. Dans les deux cas
/// l'intensité est un <em>paramètre de jeu</em>, jamais une mesure : comme le coût
/// d'une locomotive, convertir la ruine d'une filière de 1871 en multiplicateur de
/// demande est un choix de conception. Et la cible est une transposition : les
/// cartes sont fictives, Kingsport n'est pas Chicago.
/// </para>
/// </summary>
public sealed class HistoricalEventDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary><c>"historical"</c> (sourcé) ou <c>"inspired"</c> (inspiré de).</summary>
    public string Basis { get; set; } = "";

    /// <summary>Renvoi vers la section de docs/SOURCES.md. Obligatoire pour un événement historique.</summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Tick de début, pour un scénario qui raisonne en ticks. Exclusif de la date
    /// calendaire : on donne l'un ou l'autre, jamais les deux.
    /// </summary>
    public int? StartTick { get; set; }

    /// <summary>
    /// Date calendaire de début. L'année de jeu compte douze mois de trente jours
    /// (<c>SimTick.TicksPerYear</c> = 360), et le tick 0 est le 1er janvier de
    /// <see cref="EventsDef.StartYear"/>. Un 31 est refusé plutôt qu'arrondi : une
    /// date qui glisse en silence est une date fausse.
    /// </summary>
    public int? Year { get; set; }
    public int? Month { get; set; }
    public int? Day { get; set; }

    /// <summary>Durée totale en ticks, montée et descente comprises.</summary>
    public int DurationTicks { get; set; }

    /// <summary>
    /// Durée de la montée, et de la descente, en ticks. 0 = marche d'escalier. Un
    /// marché ne découvre pas une grève à minuit : la production s'étiole à mesure
    /// que les puits ferment, et reprend de même.
    /// </summary>
    public int RampTicks { get; set; }

    /// <summary>
    /// Villes touchées. Vide = toutes les villes où au moins un des effets a prise
    /// (demande ou production nominale non nulle de la marchandise).
    /// </summary>
    public List<string> Cities { get; set; } = new();

    public List<EventEffectDef> Effects { get; set; } = new();
}

/// <summary>
/// Un type d'événement aléatoire : une probabilité, des cibles admissibles, des
/// bornes d'intensité et de durée. Le tirage se fait dans
/// <see cref="Core.DeterministicRandom"/>, sur la séquence propre du module.
/// </summary>
public sealed class RandomEventTypeDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>
    /// Nombre moyen d'occurrences par année de jeu, sur la fenêtre de mois
    /// autorisée. La probabilité d'un déclenchement un jour donné vaut ce nombre
    /// divisé par le nombre de jours de la fenêtre. 0 désactive le type.
    /// </summary>
    public double OccurrencesPerYear { get; set; }

    /// <summary>
    /// Mois (1 à 12) où l'événement peut <em>commencer</em>. Vide = toute l'année.
    /// Une vague de froid en juillet ne serait pas un aléa, ce serait un bug.
    /// </summary>
    public List<int> Months { get; set; } = new();

    /// <summary>
    /// <c>"one"</c> : une seule ville, tirée parmi les admissibles — c'est ce qui
    /// déplace le meilleur débouché. <c>"all"</c> : toutes à la fois, comme une
    /// saison — la mesure a déjà montré que cela fait respirer la dispersion sans
    /// déplacer sa tête.
    /// </summary>
    public string Scope { get; set; } = "one";

    /// <summary>Villes admissibles. Vide = toutes celles où un effet a prise.</summary>
    public List<string> Cities { get; set; } = new();

    public int DurationMinTicks { get; set; }
    public int DurationMaxTicks { get; set; }

    /// <summary>Montée et descente, en ticks, ramenées à la moitié de la durée tirée si elle est plus courte.</summary>
    public int RampTicks { get; set; }

    public List<EventEffectDef> Effects { get; set; } = new();
}

/// <summary>
/// Configuration du module events. Une seule propriété est ajoutée à
/// <see cref="Economy.ScenarioDef"/> — <c>Events</c>.
/// <para>
/// <b>Inactif par défaut, et neutre au bit près.</b> Sans bloc, ou avec
/// <c>enabled = false</c>, le module n'ouvre rien, ne tire rien et ne touche aucun
/// marché : les multiplicateurs restent à 1 exactement, et multiplier un taux par
/// 1 ne change pas son dernier bit. C'est vérifié par les traces de référence de
/// tous les scénarios existants, qui n'ont pas bougé à l'arrivée du module.
/// </para>
/// </summary>
public sealed class EventsDef
{
    public bool Enabled { get; set; }

    /// <summary>Année calendaire du tick 0, pour les événements donnés par date.</summary>
    public int StartYear { get; set; }

    /// <summary>
    /// Séquence PCG propre au module, dérivée de la graine du scénario. Partager
    /// un flux avec un autre module ferait qu'activer les événements décalerait
    /// ses tirages — exactement ce que la finance évite avec sa propre séquence.
    /// Doit différer de 1 (le flux du monde) et de celle de la finance.
    /// </summary>
    public ulong RandomSequence { get; set; } = 11;

    /// <summary>
    /// Bornes de sécurité du multiplicateur composé d'un marché, quand plusieurs
    /// événements se superposent. Le plancher doit rester strictement positif : une
    /// demande annulée ferait sortir la ville du panel des acheteurs, et un marché
    /// sans acheteur vaut le prix plancher par construction — les statistiques et
    /// le transporteur verraient un marché disparaître au lieu d'un marché déprimé.
    /// </summary>
    public double MinFactor { get; set; } = 0.1;
    public double MaxFactor { get; set; } = 4.0;

    public List<HistoricalEventDef> Historical { get; set; } = new();
    public List<RandomEventTypeDef> Random { get; set; } = new();
}
