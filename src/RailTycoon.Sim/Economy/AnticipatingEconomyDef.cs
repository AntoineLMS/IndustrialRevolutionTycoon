namespace RailTycoon.Sim.Economy;

/// <summary>
/// Profil saisonnier d'une marchandise. Une seule entrée par marchandise porte à
/// la fois sa saison de production et sa saison de consommation : ce sont deux
/// phénomènes distincts et rarement en phase — le blé se récolte à l'automne, le
/// charbon se brûle en hiver.
/// <para>
/// La saison est une fonction du <em>tick</em>, jamais d'un tirage : à graine
/// identique, la même année produit la même courbe. C'est aussi ce qui la rend
/// lisible pour un joueur — une récolte qu'on peut attendre est une décision, une
/// récolte aléatoire est une loterie.
/// </para>
/// </summary>
public sealed class CargoSeasonDef
{
    public string Cargo { get; set; } = "";

    /// <summary>
    /// Amplitude relative de la saison de production. 0,5 = ±50 % autour du débit
    /// nominal. La moyenne annuelle reste exactement le débit nominal, ce qui
    /// laisse le bilan statique de <c>--balance</c> valide.
    /// </summary>
    public double ProductionAmplitude { get; set; }

    /// <summary>
    /// Moment de l'année où la production culmine, en fraction d'année dans
    /// [0, 1). 0 = premier jour de l'année, 0,5 = plein été.
    /// </summary>
    public double ProductionPeak { get; set; }

    /// <summary>Amplitude relative de la saison de consommation des habitants.</summary>
    public double DemandAmplitude { get; set; }

    /// <summary>Moment de l'année où la consommation culmine, en fraction d'année.</summary>
    public double DemandPeak { get; set; }
}

/// <summary>
/// Paramètres du solveur <see cref="AnticipatingEconomySolver"/>. Séparés de
/// <see cref="EconomyDef"/> parce que la référence reste le témoin de comparaison :
/// elle doit continuer à tourner sur les mêmes scénarios sans voir ces réglages.
/// <para>
/// <b>Tous les défauts sont neutres.</b> Un scénario qui ne déclare pas de bloc
/// <c>anticipating</c> fait produire au solveur anticipant exactement la trace de
/// la référence, à l'empreinte près. Ce n'est pas une commodité : c'est ce qui
/// permet d'attribuer toute différence mesurée à un réglage nommé, et non à une
/// divergence d'implémentation.
/// </para>
/// </summary>
public sealed class AnticipatingEconomyDef
{
    /// <summary>
    /// Horizon d'anticipation des acheteurs, en ticks. Un marché dont le stock
    /// fond valorise sa marchandise au prix qu'elle aura dans autant de ticks, et
    /// non au prix du stock présent. 0 désactive l'anticipation.
    /// <para>
    /// C'est le terme dérivé de la boucle de prix, et c'est bien son rôle de
    /// déstabiliser : un prix qui ne dépend que du stock courant converge vers le
    /// point fixe de la consommation et y reste. Un prix qui dépend aussi de la
    /// <em>pente</em> du stock dépasse la cible, se corrige, et produit des vagues.
    /// </para>
    /// </summary>
    public double AnticipationTicks { get; set; }

    /// <summary>
    /// Poids de l'anticipation dans le prix affiché, dans [0, 1]. 0 = prix au
    /// comptant pur (comportement de la référence), 1 = prix entièrement projeté.
    /// </summary>
    public double AnticipationWeight { get; set; }

    /// <summary>
    /// Lissage exponentiel de la pente du stock, dans [0, 1]. Bas, l'acheteur a
    /// une longue mémoire et ignore un train isolé ; haut, il réagit au dernier
    /// tick et le prix devient du bruit.
    /// </summary>
    public double DriftSmoothing { get; set; } = 0.25;

    /// <summary>
    /// Vitesse de croissance d'une ville par tick, pour un écart de satisfaction
    /// de 1. 0 gèle la démographie, et c'est le réglage du scénario de référence.
    /// <para>
    /// On attendait de cette rétroaction lente qu'elle empêche la carte de se figer.
    /// La mesure dit le contraire : elle comprime la dispersion au lieu de la
    /// déplacer. Voir <see cref="AnticipatingEconomySolver.UpdateCitySizes"/> pour le
    /// mécanisme et les chiffres.
    /// </para>
    /// </summary>
    public double GrowthRatePerTick { get; set; }

    /// <summary>
    /// Mémoire démographique, en ticks. Une ville ne réagit pas au prix du jour
    /// mais à celui des derniers mois : on ne déménage pas parce que le pain a
    /// monté cette semaine. 0 ou 1 = réaction immédiate.
    /// <para>
    /// Ce retard était l'hypothèse de rattrapage de la croissance : une ville qui
    /// continue de grandir après que le prix a déjà corrigé dépasse la cible, et le
    /// couple retard-intégration devait produire un cycle au lieu d'un équilibre.
    /// Testé de 1 à 180 ticks : aucun cycle n'apparaît, la mobilité reste sous celle
    /// de la référence. Le paramètre reste parce qu'il modélise quelque chose de
    /// vrai — on ne déménage pas parce que le pain a monté cette semaine — pas parce
    /// qu'il a résolu quoi que ce soit.
    /// </para>
    /// </summary>
    public double GrowthMemoryTicks { get; set; }

    /// <summary>
    /// Bande morte de la satisfaction en dessous de laquelle une ville ne bouge
    /// pas. Sans elle, l'écart résiduel entre le prix d'équilibre et le prix de
    /// référence — quelques pourcents — pousserait lentement toutes les villes
    /// contre la même borne, ce qui est un biais et non une dynamique.
    /// </summary>
    public double GrowthDeadband { get; set; } = 0.04;

    /// <summary>Bornes de la taille d'une ville, relatives à sa taille de scénario.</summary>
    public double MinCitySize { get; set; } = 1.0;
    public double MaxCitySize { get; set; } = 1.0;

    /// <summary>
    /// Plafond de sécurité des amplitudes saisonnières. Une amplitude de 1
    /// annulerait la demande d'un marché une fois par an ; un marché sans acheteur
    /// vaut le prix plancher par construction, et ce cas limite ferait clignoter
    /// les statistiques et les décisions du transporteur sans rien modéliser.
    /// </summary>
    public double MaxSeasonalSwing { get; set; } = 0.9;

    public List<CargoSeasonDef> Seasons { get; set; } = new();
}
