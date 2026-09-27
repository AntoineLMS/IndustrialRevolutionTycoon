namespace RailTycoon.Sim.Network;

// Données de conception du module réseau, chargées depuis data/*.json comme le
// reste du scénario. Elles vivent dans ce fichier et dans ce fichier seulement :
// ScenarioDef n'expose qu'une propriété vers RailNetworkDef, ce qui permet à
// plusieurs modules d'évoluer en parallèle sans se marcher dessus.
//
// Aucune de ces valeurs n'a vocation à être équilibrée ici : ce sont des ordres de
// grandeur choisis pour que l'arbitrage « contourner / franchir / percer » ait un
// gagnant différent selon le relief. Voir docs/ARCHITECTURE.md.

/// <summary>Un point de la carte, en kilomètres depuis le coin sud-ouest.</summary>
public sealed class MapPointDef
{
    public double XKm { get; set; }
    public double YKm { get; set; }
}

/// <summary>
/// Une forme du relief, ajoutée à la carte de hauteurs.
/// <para>
/// Deux primitives suffisent à décrire une plaine, une vallée et un col :
/// <c>ridge</c> (une crête linéaire, d'amplitude négative pour un sillon) et
/// <c>bowl</c> (une cuvette radiale, d'amplitude positive pour un mamelon). Les
/// décrire analytiquement plutôt que par une grille de nombres rend les cartes de
/// test lisibles : on voit dans les données pourquoi un tracé coûte cher.
/// </para>
/// </summary>
public sealed class TerrainFeatureDef
{
    /// <summary>« ridge » (crête linéaire) ou « bowl » (cuvette radiale).</summary>
    public string Kind { get; set; } = "ridge";

    /// <summary>Hauteur ajoutée au sommet de la forme. Négative pour creuser.</summary>
    public double AmplitudeM { get; set; }

    /// <summary>Portée latérale : la forme est nulle au-delà de cette distance.</summary>
    public double HalfWidthKm { get; set; } = 1.0;

    /// <summary>Axe de la crête, pour <c>ridge</c>.</summary>
    public double X1Km { get; set; }
    public double Y1Km { get; set; }
    public double X2Km { get; set; }
    public double Y2Km { get; set; }

    /// <summary>Centre de la cuvette, pour <c>bowl</c>.</summary>
    public double XKm { get; set; }
    public double YKm { get; set; }
}

/// <summary>
/// Rugosité procédurale ajoutée au relief. Tirée de <c>DeterministicRandom</c> et
/// d'elle seule : à graine identique, la carte est identique, ce qui est la
/// condition pour qu'un coût de construction soit une valeur de référence.
/// </summary>
public sealed class TerrainNoiseDef
{
    public double AmplitudeM { get; set; }

    /// <summary>Longueur d'onde de la première octave.</summary>
    public double WavelengthKm { get; set; } = 8.0;

    public int Octaves { get; set; } = 3;

    /// <summary>
    /// Graine locale, combinée à celle du scénario. Deux cartes d'un même scénario
    /// peuvent ainsi différer sans qu'on ait à changer la graine de la partie.
    /// </summary>
    public ulong Seed { get; set; } = 1;
}

/// <summary>Le relief, sous forme de carte de hauteurs régulière.</summary>
public sealed class TerrainDef
{
    public int Columns { get; set; } = 2;
    public int Rows { get; set; } = 2;
    public double CellSizeKm { get; set; } = 1.0;
    public double BaseElevationM { get; set; }

    /// <summary>
    /// Altitudes explicites, une chaîne de nombres séparés par des espaces par
    /// ligne, du sud au nord. Prioritaire sur <see cref="BaseElevationM"/>. Utile
    /// pour les cas limites d'un test ; les cartes réelles passent par les formes.
    /// </summary>
    public List<string> Heights { get; set; } = new();

    /// <summary>Formes appliquées dans l'ordre de déclaration.</summary>
    public List<TerrainFeatureDef> Features { get; set; } = new();

    public TerrainNoiseDef? Noise { get; set; }
}

/// <summary>Contraintes géométriques du tracé.</summary>
public sealed class AlignmentDef
{
    /// <summary>
    /// Pente maximale admissible, en pourcent. C'est la contrainte qui coûte de
    /// l'argent : là où le sol est plus raide que cette valeur, il faut déblayer,
    /// remblayer, ou percer.
    /// </summary>
    public double MaxGradePercent { get; set; } = 2.0;

    /// <summary>
    /// Rayon de courbe minimal. Il borne la vivacité des changements de direction :
    /// un tracé qui exigerait un virage plus serré est refusé au chargement plutôt
    /// que corrigé en silence.
    /// </summary>
    public double MinCurveRadiusM { get; set; } = 400.0;

    /// <summary>
    /// Pas d'échantillonnage du tracé. Il fixe la résolution de tout le calcul de
    /// terrassement : le changer change les coûts de référence.
    /// </summary>
    public double SampleSpacingKm { get; set; } = 0.25;

    /// <summary>Largeur de la plateforme, pour une voie.</summary>
    public double FormationWidthM { get; set; } = 6.0;

    /// <summary>Largeur ajoutée par voie supplémentaire.</summary>
    public double ExtraWidthPerTrackM { get; set; } = 4.5;

    /// <summary>
    /// Fruit des talus, en horizontal pour un vertical. Il fait croître le volume
    /// de terrassement comme le <em>carré</em> de la hauteur : c'est la raison pour
    /// laquelle un grand remblai devient vite moins cher en pont.
    /// </summary>
    public double SideSlopeRatio { get; set; } = 1.5;

    /// <summary>Hauteur de remblai au-delà de laquelle on construit un pont.</summary>
    public double BridgeMinHeightM { get; set; } = 15.0;

    /// <summary>Profondeur de déblai au-delà de laquelle on perce un tunnel.</summary>
    public double TunnelMinDepthM { get; set; } = 18.0;

    /// <summary>
    /// Longueur minimale d'un ouvrage. En dessous, le franchissement reste du
    /// terrassement : un viaduc de cinquante mètres est un ponceau, pas un pont, et
    /// le compter comme tel gonflerait artificiellement les devis.
    /// </summary>
    public double MinStructureLengthKm { get; set; } = 0.3;

    /// <summary>
    /// Nombre de stratégies de franchissement mises en concurrence, du tracé tout en
    /// déblai au tracé tout en remblai. 1 = un seul profil, équilibré. Au-delà, le
    /// géomètre chiffre chaque stratégie et garde la moins chère : c'est ainsi que
    /// « percer » peut battre « franchir » là où un remblai deviendrait un viaduc de
    /// cent mètres. Le changer change les devis de référence.
    /// </summary>
    public int ProfileCandidates { get; set; } = 5;

    /// <summary>
    /// Écart de pente en dessous duquel deux sections voisines sont fusionnées dans
    /// le profil publié. Le profil est destiné à être lu par le module de
    /// circulation : mille sections à 0,01 % près ne lui apprendraient rien.
    /// </summary>
    public double ProfileMergeTolerancePercent { get; set; } = 0.1;
}

/// <summary>Prix unitaires de construction.</summary>
public sealed class ConstructionCostDef
{
    /// <summary>Voie posée, par kilomètre et par voie, terrain préparé.</summary>
    public double TrackPerKmPerTrack { get; set; } = 3_000;

    public double CutPerCubicMetre { get; set; } = 0.30;

    /// <summary>
    /// Le remblai est moins cher que le déblai parce qu'il réemploie les déblais du
    /// même chantier. Ce n'est vrai que tant que les deux s'équilibrent — ce que le
    /// tracé cherche justement à faire.
    /// </summary>
    public double FillPerCubicMetre { get; set; } = 0.22;

    public double BridgePerKm { get; set; } = 40_000;

    /// <summary>Surcoût d'un pont par mètre de hauteur et par kilomètre.</summary>
    public double BridgePerKmPerMetre { get; set; } = 1_800;

    public double TunnelPerKm { get; set; } = 120_000;

    /// <summary>Coût des courbes, par degré de déviation cumulée.</summary>
    public double CurvePerDegree { get; set; } = 400;

    /// <summary>
    /// Facteur appliqué aux ouvrages par voie supplémentaire. Un tunnel à deux
    /// voies coûte plus cher qu'à une seule, mais pas le double : on ne perce
    /// qu'une fois.
    /// </summary>
    public double ExtraTrackStructureFactor { get; set; } = 0.6;
}

/// <summary>Ce que le relief coûte à l'exploitation, et non à la construction.</summary>
public sealed class TractionDef
{
    /// <summary>
    /// Kilomètres de plat équivalents à un mètre d'élévation gagné. Traduit la
    /// résistance de rampe en distance : c'est ainsi que le relief atteint
    /// l'économie sans que le transport ait à connaître le profil.
    /// <para>
    /// 0,03 correspond à l'ordre de grandeur classique — une rampe de 1 % triple la
    /// résistance au roulement d'un train de marchandises.
    /// </para>
    /// </summary>
    public double ClimbEquivalentKm { get; set; } = 0.03;
}

/// <summary>Un nœud du graphe de voies.</summary>
public sealed class TrackNodeDef
{
    public string Id { get; set; } = "";

    /// <summary>Ville desservie, si le nœud est une gare. Vide sinon.</summary>
    public string City { get; set; } = "";

    public double XKm { get; set; }
    public double YKm { get; set; }

    /// <summary>
    /// Nombre de trains qui peuvent y stationner ou s'y croiser. 1 = pas
    /// d'évitement possible. C'est l'information dont la signalisation aura besoin
    /// pour décider si deux trains peuvent se rencontrer ici.
    /// </summary>
    public int PassingTracks { get; set; } = 2;
}

/// <summary>Une arête du graphe : un tronçon de voie entre deux nœuds.</summary>
public sealed class TrackEdgeDef
{
    public string Id { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";

    /// <summary>1 = voie unique. C'est la source des conflits de circulation.</summary>
    public int TrackCount { get; set; } = 1;

    /// <summary>
    /// Points de passage imposés entre les deux nœuds. C'est le tracé choisi par le
    /// constructeur : contourner un relief se décrit ici, et se paie dans le devis.
    /// </summary>
    public List<MapPointDef> Via { get; set; } = new();
}

/// <summary>
/// Un itinéraire nommé : la suite de gares et de points de passage qu'un train
/// desservira. Les tronçons intermédiaires sont trouvés dans le graphe.
/// </summary>
public sealed class TrackRouteDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Nodes { get; set; } = new();
}

/// <summary>
/// Le réseau ferré d'un scénario. Absent ou sans nœud, le scénario reste en mode
/// de compatibilité : les lignes de <c>scenario.lines</c> gardent leurs distances
/// saisies à la main et rien ne change.
/// </summary>
public sealed class RailNetworkDef
{
    public TerrainDef? Terrain { get; set; }
    public AlignmentDef Alignment { get; set; } = new();
    public ConstructionCostDef Costs { get; set; } = new();
    public TractionDef Traction { get; set; } = new();
    public List<TrackNodeDef> Nodes { get; set; } = new();
    public List<TrackEdgeDef> Edges { get; set; } = new();
    public List<TrackRouteDef> Routes { get; set; } = new();
}
