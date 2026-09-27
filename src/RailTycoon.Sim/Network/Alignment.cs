using RailTycoon.Sim.Core;

namespace RailTycoon.Sim.Network;

public enum StructureKind
{
    Embankment,
    Cutting,
    Bridge,
    Tunnel,
}

/// <summary>
/// Un ouvrage sur un tronçon : un pont ou un tunnel, avec sa position le long du
/// tracé. Exposé parce que c'est ce dont l'interface aura besoin pour dessiner la
/// ligne, et ce dont la circulation aura besoin le jour où un pont limitera la
/// charge ou un tunnel la vitesse.
/// </summary>
public sealed class TrackStructure
{
    public required StructureKind Kind { get; init; }
    public required double StartKm { get; init; }
    public required double LengthKm { get; init; }

    /// <summary>Hauteur du tablier, ou couverture au-dessus du tunnel.</summary>
    public required double MaxHeightM { get; init; }

    public required double Cost { get; init; }
}

/// <summary>
/// Une section homogène du profil en long, dans le sens de construction du
/// tronçon. <see cref="GradePercent"/> est positive en montée.
/// </summary>
public readonly record struct ProfileSection(
    double StartKm,
    double LengthKm,
    double GradePercent,
    double CurveRadiusM,
    StructureKind Kind);

/// <summary>
/// Le profil en long d'un tronçon : ce que le relief laisse d'un tracé une fois
/// les contraintes de pente et de courbe respectées.
/// <para>
/// C'est la pièce que le module de circulation consommera : la pente déterminante
/// dans chaque sens borne la charge remorquable, le rayon de courbe borne la
/// vitesse, et la longueur des sections donne le temps d'occupation d'un canton.
/// </para>
/// </summary>
public sealed class TrackProfile
{
    public required IReadOnlyList<ProfileSection> Sections { get; init; }

    /// <summary>Altitude aux deux extrémités, dans le sens de construction.</summary>
    public required double StartElevationM { get; init; }
    public required double EndElevationM { get; init; }

    /// <summary>Somme des dénivelés positifs, dans le sens de construction.</summary>
    public required double ClimbM { get; init; }

    /// <summary>Somme des dénivelés négatifs, en valeur absolue.</summary>
    public required double DescentM { get; init; }

    public double LengthKm
    {
        get
        {
            double total = 0;
            foreach (var section in Sections) total += section.LengthKm;
            return total;
        }
    }

    /// <summary>
    /// Pente déterminante dans un sens de marche : la plus forte montée qu'un train
    /// y rencontrera. C'est elle, et non la pente moyenne, qui fixe le tonnage
    /// qu'une machine peut remorquer.
    /// </summary>
    public double RulingGradePercent(bool forward)
    {
        double worst = 0;
        foreach (var section in Sections)
        {
            double grade = forward ? section.GradePercent : -section.GradePercent;
            if (grade > worst) worst = grade;
        }
        return worst;
    }

    /// <summary>Dénivelé positif cumulé dans un sens de marche.</summary>
    public double ClimbInDirection(bool forward) => forward ? ClimbM : DescentM;

    public double MinCurveRadiusM
    {
        get
        {
            double min = double.PositiveInfinity;
            foreach (var section in Sections)
                if (section.CurveRadiusM > 0 && section.CurveRadiusM < min)
                    min = section.CurveRadiusM;
            return double.IsPositiveInfinity(min) ? 0 : min;
        }
    }
}

/// <summary>
/// Le devis d'un tracé : ce qu'il coûte, ce qu'il mesure vraiment, et ce qu'il a
/// fallu construire pour le rendre praticable.
/// <para>
/// Le devis est détaillé poste par poste, et pas seulement totalisé. C'est ce qui
/// permet de comprendre <em>pourquoi</em> un tracé est cher — donc de chercher une
/// variante — au lieu de constater un nombre.
/// </para>
/// </summary>
public sealed class ConstructionEstimate
{
    public required double LengthKm { get; init; }
    public required double HorizontalLengthKm { get; init; }
    public required double MaxGradePercent { get; init; }
    public required double MinCurveRadiusM { get; init; }
    public required double CurveDegrees { get; init; }
    public required double CutVolumeM3 { get; init; }
    public required double FillVolumeM3 { get; init; }
    public required double TrackCost { get; init; }
    public required double EarthworkCost { get; init; }
    public required double BridgeCost { get; init; }
    public required double TunnelCost { get; init; }
    public required double CurveCost { get; init; }

    /// <summary>
    /// Stratégie de franchissement retenue : 0 = tout en déblai, 1 = tout en
    /// remblai, 0,5 = déblais et remblais équilibrés. Publiée parce que c'est
    /// l'explication du devis — un tracé qui choisit 0 dit qu'il valait mieux percer
    /// que franchir.
    /// </summary>
    public required double ProfileBias { get; init; }

    public required IReadOnlyList<TrackStructure> Structures { get; init; }
    public required TrackProfile Profile { get; init; }

    public double TotalCost => TrackCost + EarthworkCost + BridgeCost + TunnelCost + CurveCost;

    public double CostPerKm => LengthKm <= 0 ? 0 : TotalCost / LengthKm;
}

/// <summary>
/// Le géomètre : à partir d'une suite de points de passage et d'un relief, il
/// produit un tracé praticable et son devis.
/// <para>
/// Les deux étapes sont séparées et le restent. <b>En plan</b> : on relie les
/// points par des alignements droits raccordés par des arcs de cercle, et on refuse
/// un virage plus serré que le rayon minimal — un tracé impossible doit être un
/// message d'erreur, pas une voie que les trains prendraient à 300 km/h. <b>En
/// long</b> : on cherche l'altitude de la plateforme qui respecte la pente
/// maximale en remuant le moins de terre possible.
/// </para>
/// </summary>
public static class Surveyor
{
    public static ConstructionEstimate Survey(
        HeightField terrain,
        IReadOnlyList<GeoPoint> controlPoints,
        int trackCount,
        AlignmentDef alignment,
        ConstructionCostDef costs,
        string what)
    {
        if (controlPoints.Count < 2)
            throw new InvalidDataException($"{what} : un tracé exige au moins deux points.");
        if (trackCount < 1)
            throw new InvalidDataException($"{what} : le nombre de voies doit être au moins 1.");
        foreach (var point in controlPoints)
            if (!terrain.Contains(point))
                throw new InvalidDataException(
                    $"{what} : le point ({point.XKm:0.##}, {point.YKm:0.##}) est hors de la carte " +
                    $"({terrain.WidthKm:0.##} × {terrain.HeightKm:0.##} km).");

        var plan = HorizontalPlan.Trace(controlPoints, alignment.MinCurveRadiusM, what);
        var stations = plan.Sample(alignment.SampleSpacingKm);

        int n = stations.Count;
        var ground = new double[n];
        for (int i = 0; i < n; i++) ground[i] = terrain.ElevationAt(stations[i].Position);

        var envelope = ProfileEnvelope.Compute(stations, ground, alignment.MaxGradePercent, what);

        // On ne cherche pas le profil optimal, on choisit entre quelques stratégies
        // de franchissement et on retient la moins chère. C'est ce qui donne un sens
        // à l'arbitrage : le même relief se paie en déblais, en remblais, en ponts
        // ou en tunnels selon la stratégie, et le devis tranche.
        int candidates = Math.Max(1, alignment.ProfileCandidates);
        ConstructionEstimate? best = null;
        for (int k = 0; k < candidates; k++)
        {
            double bias = candidates == 1 ? 0.5 : (double)k / (candidates - 1);
            var estimate = Estimate(
                stations, ground, envelope.Blend(bias), plan, trackCount, alignment, costs, bias);
            if (best is null || estimate.TotalCost < best.TotalCost) best = estimate;
        }
        return best!;
    }

    private static ConstructionEstimate Estimate(
        IReadOnlyList<AlignmentStation> stations,
        double[] ground,
        double[] rail,
        HorizontalPlan plan,
        int trackCount,
        AlignmentDef alignment,
        ConstructionCostDef costs,
        double profileBias)
    {
        int n = stations.Count;

        // Chaque station porte la demi-distance vers chacun de ses voisins : la somme
        // des sections vaut donc exactement la longueur du tracé.
        var section = new double[n];
        for (int i = 0; i < n; i++)
        {
            double before = i == 0 ? 0 : (stations[i].ChainageKm - stations[i - 1].ChainageKm) * 0.5;
            double after = i == n - 1 ? 0 : (stations[i + 1].ChainageKm - stations[i].ChainageKm) * 0.5;
            section[i] = before + after;
        }

        var kinds = ClassifyStructures(stations, ground, rail, alignment);

        double width = alignment.FormationWidthM + alignment.ExtraWidthPerTrackM * (trackCount - 1);
        double structureFactor = 1.0 + costs.ExtraTrackStructureFactor * (trackCount - 1);

        double cutVolume = 0, fillVolume = 0;
        double earthworkCost = 0, bridgeCost = 0, tunnelCost = 0;
        var perStationCost = new double[n];

        for (int i = 0; i < n; i++)
        {
            double offset = rail[i] - ground[i];
            double height = Math.Abs(offset);
            double lengthM = section[i] * 1000.0;

            switch (kinds[i])
            {
                case StructureKind.Bridge:
                {
                    double cost = (costs.BridgePerKm + costs.BridgePerKmPerMetre * height)
                                  * section[i] * structureFactor;
                    bridgeCost += cost;
                    perStationCost[i] = cost;
                    break;
                }
                case StructureKind.Tunnel:
                {
                    double cost = costs.TunnelPerKm * section[i] * structureFactor;
                    tunnelCost += cost;
                    perStationCost[i] = cost;
                    break;
                }
                default:
                {
                    // Section trapézoïdale : la plateforme, plus deux talus. Le terme
                    // en h² est ce qui rend un grand remblai brutalement plus cher
                    // qu'un petit, et finit par donner raison au pont.
                    double area = width * height + alignment.SideSlopeRatio * height * height;
                    double volume = area * lengthM;
                    double cost;
                    if (offset >= 0)
                    {
                        fillVolume += volume;
                        cost = volume * costs.FillPerCubicMetre;
                    }
                    else
                    {
                        cutVolume += volume;
                        cost = volume * costs.CutPerCubicMetre;
                    }
                    earthworkCost += cost;
                    perStationCost[i] = cost;
                    break;
                }
            }
        }

        double length3D = 0;
        double climb = 0, descent = 0;
        double maxGrade = 0;
        for (int i = 1; i < n; i++)
        {
            double run = (stations[i].ChainageKm - stations[i - 1].ChainageKm) * 1000.0;
            double rise = rail[i] - rail[i - 1];
            length3D += Math.Sqrt(run * run + rise * rise) / 1000.0;
            if (rise > 0) climb += rise; else descent -= rise;
            double grade = run <= 0 ? 0 : Math.Abs(rise / run) * 100.0;
            if (grade > maxGrade) maxGrade = grade;
        }

        double trackCost = costs.TrackPerKmPerTrack * length3D * trackCount;
        double curveCost = costs.CurvePerDegree * plan.CurveDegrees;

        var structures = CollectStructures(stations, ground, rail, kinds, perStationCost);
        var profile = BuildProfile(stations, rail, kinds, alignment.ProfileMergeTolerancePercent, climb, descent);

        return new ConstructionEstimate
        {
            LengthKm = length3D,
            HorizontalLengthKm = stations[n - 1].ChainageKm,
            MaxGradePercent = maxGrade,
            MinCurveRadiusM = plan.MinRadiusM,
            CurveDegrees = plan.CurveDegrees,
            CutVolumeM3 = cutVolume,
            FillVolumeM3 = fillVolume,
            TrackCost = trackCost,
            EarthworkCost = earthworkCost,
            BridgeCost = bridgeCost,
            TunnelCost = tunnelCost,
            CurveCost = curveCost,
            ProfileBias = profileBias,
            Structures = structures,
            Profile = profile,
        };
    }

    /// <summary>
    /// Décide, station par station, si l'on est en remblai, en déblai, sur un pont
    /// ou en tunnel.
    /// <para>
    /// Un franchissement trop court reste du terrassement : un viaduc de cinquante
    /// mètres est un ponceau. Sans ce seuil de longueur, la moindre ondulation du
    /// relief engendrerait une nuée d'ouvrages d'art et le devis n'aurait plus de
    /// sens.
    /// </para>
    /// </summary>
    private static StructureKind[] ClassifyStructures(
        IReadOnlyList<AlignmentStation> stations, double[] ground, double[] rail, AlignmentDef alignment)
    {
        int n = stations.Count;
        var kinds = new StructureKind[n];
        for (int i = 0; i < n; i++)
        {
            double offset = rail[i] - ground[i];
            if (offset > alignment.BridgeMinHeightM) kinds[i] = StructureKind.Bridge;
            else if (-offset > alignment.TunnelMinDepthM) kinds[i] = StructureKind.Tunnel;
            else kinds[i] = offset >= 0 ? StructureKind.Embankment : StructureKind.Cutting;
        }

        int start = 0;
        while (start < n)
        {
            int end = start;
            while (end + 1 < n && kinds[end + 1] == kinds[start]) end++;
            if (kinds[start] is StructureKind.Bridge or StructureKind.Tunnel)
            {
                double runLength = stations[end].ChainageKm - stations[start].ChainageKm;
                if (runLength < alignment.MinStructureLengthKm)
                    for (int i = start; i <= end; i++)
                        kinds[i] = rail[i] - ground[i] >= 0 ? StructureKind.Embankment : StructureKind.Cutting;
            }
            start = end + 1;
        }

        return kinds;
    }

    private static List<TrackStructure> CollectStructures(
        IReadOnlyList<AlignmentStation> stations,
        double[] ground,
        double[] rail,
        StructureKind[] kinds,
        double[] perStationCost)
    {
        var structures = new List<TrackStructure>();
        int n = stations.Count;
        int start = 0;
        while (start < n)
        {
            int end = start;
            while (end + 1 < n && kinds[end + 1] == kinds[start]) end++;
            if (kinds[start] is StructureKind.Bridge or StructureKind.Tunnel)
            {
                double maxHeight = 0, cost = 0;
                for (int i = start; i <= end; i++)
                {
                    double height = Math.Abs(rail[i] - ground[i]);
                    if (height > maxHeight) maxHeight = height;
                    cost += perStationCost[i];
                }
                structures.Add(new TrackStructure
                {
                    Kind = kinds[start],
                    StartKm = stations[start].ChainageKm,
                    LengthKm = stations[end].ChainageKm - stations[start].ChainageKm,
                    MaxHeightM = maxHeight,
                    Cost = cost,
                });
            }
            start = end + 1;
        }
        return structures;
    }

    private static TrackProfile BuildProfile(
        IReadOnlyList<AlignmentStation> stations,
        double[] rail,
        StructureKind[] kinds,
        double mergeTolerancePercent,
        double climb,
        double descent)
    {
        int n = stations.Count;
        var sections = new List<ProfileSection>();

        int start = 0;
        while (start < n - 1)
        {
            double startKm = stations[start].ChainageKm;
            double grade = GradeBetween(stations, rail, start);
            int end = start + 1;
            while (end < n - 1
                   && Math.Abs(GradeBetween(stations, rail, end) - grade) <= mergeTolerancePercent
                   && kinds[end] == kinds[start]
                   && SameCurve(stations[end].CurveRadiusM, stations[start].CurveRadiusM))
                end++;

            double lengthKm = stations[end].ChainageKm - startKm;
            // La pente publiée est celle du segment complet, pas celle de sa première
            // maille : c'est ce que verra la circulation, et la somme des dénivelés
            // doit rester exacte après fusion.
            double published = lengthKm <= 0 ? 0 : (rail[end] - rail[start]) / (lengthKm * 1000.0) * 100.0;
            sections.Add(new ProfileSection(startKm, lengthKm, published, stations[start].CurveRadiusM, kinds[start]));
            start = end;
        }

        return new TrackProfile
        {
            Sections = sections,
            StartElevationM = rail[0],
            EndElevationM = rail[n - 1],
            ClimbM = climb,
            DescentM = descent,
        };
    }

    private static double GradeBetween(IReadOnlyList<AlignmentStation> stations, double[] rail, int i)
    {
        double run = (stations[i + 1].ChainageKm - stations[i].ChainageKm) * 1000.0;
        return run <= 0 ? 0 : (rail[i + 1] - rail[i]) / run * 100.0;
    }

    private static bool SameCurve(double a, double b) => (a <= 0) == (b <= 0) && Math.Abs(a - b) < 1e-6;
}

/// <summary>
/// L'ensemble des profils en long admissibles pour un tracé donné.
/// <para>
/// Le raisonnement tient en une phrase : une plateforme dont la pente ne dépasse
/// jamais <c>pente_max</c> est exactement une fonction lipschitzienne de la
/// distance, de constante <c>pente_max</c>. Cet ensemble est encadré par ses deux
/// bornes naturelles — la plus haute plateforme qui ne remonte jamais au-dessus du
/// sol (tout en déblai, donc en tunnels) et la plus basse qui n'en descend jamais
/// en dessous (tout en remblai, donc en ponts). Toute combinaison convexe des deux
/// est encore admissible, ce qui donne une famille de stratégies de franchissement à
/// un seul paramètre.
/// </para>
/// <para>
/// Sur un terrain plus doux que la pente maximale, les deux bornes se confondent
/// avec le sol : la voie suit le sol et ne coûte rien de plus que sa pose. Là où le
/// sol est plus raide, l'écart s'ouvre, et c'est là — et seulement là — qu'on paie.
/// </para>
/// </summary>
public sealed class ProfileEnvelope
{
    private readonly double[] _allCutting;
    private readonly double[] _allEmbankment;
    private readonly double[] _floor;
    private readonly double[] _ceiling;

    private ProfileEnvelope(double[] allCutting, double[] allEmbankment, double[] floor, double[] ceiling)
    {
        _allCutting = allCutting;
        _allEmbankment = allEmbankment;
        _floor = floor;
        _ceiling = ceiling;
    }

    public static ProfileEnvelope Compute(
        IReadOnlyList<AlignmentStation> stations, double[] ground, double maxGradePercent, string what)
    {
        int n = stations.Count;
        double metresPerKm = maxGradePercent * 10.0;
        if (metresPerKm <= 0)
            throw new InvalidDataException($"{what} : la pente maximale doit être strictement positive.");

        double totalKm = stations[n - 1].ChainageKm;

        // Les extrémités sont les raccords avec le reste du réseau : elles sont au
        // niveau du sol, non négociable. Si la pente maximale ne suffit pas à
        // rejoindre l'une depuis l'autre, le tracé est impossible et doit le dire.
        double endpointDrop = Math.Abs(ground[n - 1] - ground[0]);
        if (endpointDrop > metresPerKm * totalKm + 1e-9)
            throw new InvalidDataException(
                $"{what} : {endpointDrop:0.#} m de dénivelé sur {totalKm:0.##} km exigent " +
                $"{endpointDrop / Math.Max(totalKm, 1e-9) / 10.0:0.##} % de pente, au-delà de " +
                $"{maxGradePercent:0.##} % — il faut rallonger le tracé ou percer.");

        // La borne « tout en déblai » est le minimum, sur toutes les stations, du cône
        // montant partant du sol : elle passe par le sol là où le terrain est doux et
        // s'enfonce dessous partout où il est trop raide. La borne « tout en remblai »
        // est son symétrique. Les deux sont lipschitziennes par construction, comme
        // minimum et maximum de familles de fonctions lipschitziennes.
        var allCutting = new double[n];
        var allEmbankment = new double[n];
        var floor = new double[n];
        var ceiling = new double[n];

        for (int i = 0; i < n; i++)
        {
            double chainage = stations[i].ChainageKm;
            double low = double.PositiveInfinity;
            double high = double.NegativeInfinity;
            for (int j = 0; j < n; j++)
            {
                double reach = metresPerKm * Math.Abs(chainage - stations[j].ChainageKm);
                double candidateLow = ground[j] + reach;
                if (candidateLow < low) low = candidateLow;
                double candidateHigh = ground[j] - reach;
                if (candidateHigh > high) high = candidateHigh;
            }
            allCutting[i] = low;
            allEmbankment[i] = high;
            ceiling[i] = Math.Min(
                ground[0] + metresPerKm * chainage,
                ground[n - 1] + metresPerKm * (totalKm - chainage));
            floor[i] = Math.Max(
                ground[0] - metresPerKm * chainage,
                ground[n - 1] - metresPerKm * (totalKm - chainage));
        }

        return new ProfileEnvelope(allCutting, allEmbankment, floor, ceiling);
    }

    /// <summary>
    /// Une plateforme admissible. <paramref name="bias"/> = 0 est le profil tout en
    /// déblai, 1 le profil tout en remblai, 0,5 l'équilibre des deux.
    /// </summary>
    public double[] Blend(double bias)
    {
        int n = _allCutting.Length;
        var rail = new double[n];
        for (int i = 0; i < n; i++)
        {
            double blended = _allCutting[i] + (_allEmbankment[i] - _allCutting[i]) * bias;
            rail[i] = Maths.Clamp(blended, _floor[i], _ceiling[i]);
        }
        return rail;
    }
}

/// <summary>Une station du tracé : un point échantillonné et ce qu'on y sait.</summary>
public readonly record struct AlignmentStation(double ChainageKm, GeoPoint Position, double CurveRadiusM);

/// <summary>
/// Le tracé en plan : des alignements droits raccordés par des arcs de cercle.
/// <para>
/// Le rayon minimal n'est pas une préférence, c'est une contrainte physique. Un
/// tracé qui exigerait plus serré est <em>refusé</em> : le corriger en silence
/// donnerait au joueur une ligne qu'il croit avoir posée et qui n'est pas celle-là.
/// </para>
/// </summary>
public sealed class HorizontalPlan
{
    private readonly List<Element> _elements;

    public double LengthKm { get; }
    public double CurveDegrees { get; }
    public double MinRadiusM { get; }

    private HorizontalPlan(List<Element> elements, double curveDegrees, double minRadiusM)
    {
        _elements = elements;
        CurveDegrees = curveDegrees;
        MinRadiusM = minRadiusM;
        double total = 0;
        foreach (var element in elements) total += element.LengthKm;
        LengthKm = total;
    }

    public static HorizontalPlan Trace(IReadOnlyList<GeoPoint> points, double minRadiusM, string what)
    {
        double radiusKm = minRadiusM / 1000.0;
        var elements = new List<Element>();
        double curveDegrees = 0;
        double minRadiusUsed = 0;

        var cursor = points[0];
        for (int i = 1; i < points.Count; i++)
        {
            var vertex = points[i];
            var incoming = (vertex - points[i - 1]).Normalized();
            if (incoming.Norm <= 0)
                throw new InvalidDataException($"{what} : deux points de passage consécutifs sont confondus.");

            if (i == points.Count - 1)
            {
                elements.Add(Element.Tangent(cursor, incoming, (vertex - cursor).Norm));
                break;
            }

            var outgoing = (points[i + 1] - vertex).Normalized();
            double cos = Maths.Clamp(GeoPoint.Dot(incoming, outgoing), -1, 1);
            double deflection = Math.Acos(cos);

            if (deflection < 1e-9)
            {
                // Points alignés : pas de courbe, on continue tout droit.
                continue;
            }
            if (deflection > Math.PI - 1e-6)
                throw new InvalidDataException(
                    $"{what} : demi-tour sur place au point ({vertex.XKm:0.##}, {vertex.YKm:0.##}).");

            double tangentKm = radiusKm * Math.Tan(deflection * 0.5);
            double legIn = (vertex - points[i - 1]).Norm;
            double legOut = (points[i + 1] - vertex).Norm;
            double available = Math.Min(legIn, legOut) * 0.5;
            if (tangentKm > available + 1e-12)
                throw new InvalidDataException(
                    $"{what} : virage de {deflection * 180.0 / Math.PI:0.#}° au point " +
                    $"({vertex.XKm:0.##}, {vertex.YKm:0.##}) — un rayon de {minRadiusM:0} m exige " +
                    $"{tangentKm:0.###} km de tangente, il n'y en a que {available:0.###} km. " +
                    "Écarter les points de passage ou adoucir l'angle.");

            var curveStart = vertex - incoming * tangentKm;
            double straight = (curveStart - cursor).Norm;
            if (straight > 1e-12) elements.Add(Element.Tangent(cursor, incoming, straight));

            double sign = GeoPoint.Cross(incoming, outgoing) >= 0 ? 1.0 : -1.0;
            var normal = new GeoPoint(-incoming.YKm, incoming.XKm) * sign;
            var centre = curveStart + normal * radiusKm;
            double startAngle = Math.Atan2(curveStart.YKm - centre.YKm, curveStart.XKm - centre.XKm);
            elements.Add(Element.Arc(centre, radiusKm, startAngle, sign * deflection));

            cursor = vertex + outgoing * tangentKm;
            curveDegrees += deflection * 180.0 / Math.PI;
            minRadiusUsed = minRadiusM;
        }

        if (elements.Count == 0)
            throw new InvalidDataException($"{what} : tracé de longueur nulle.");

        return new HorizontalPlan(elements, curveDegrees, minRadiusUsed);
    }

    /// <summary>
    /// Échantillonne le tracé à pas régulier. Les deux extrémités sont toujours
    /// présentes : ce sont les raccords au reste du réseau.
    /// </summary>
    public List<AlignmentStation> Sample(double spacingKm)
    {
        if (spacingKm <= 0)
            throw new InvalidDataException("Le pas d'échantillonnage du tracé doit être strictement positif.");

        int count = Math.Max(2, (int)Math.Round(LengthKm / spacingKm) + 1);
        var stations = new List<AlignmentStation>(count);
        for (int i = 0; i < count; i++)
        {
            double chainage = LengthKm * i / (count - 1);
            var (position, radius) = At(chainage);
            stations.Add(new AlignmentStation(chainage, position, radius));
        }
        return stations;
    }

    public (GeoPoint Position, double RadiusM) At(double chainageKm)
    {
        double remaining = Maths.Clamp(chainageKm, 0, LengthKm);
        for (int i = 0; i < _elements.Count; i++)
        {
            var element = _elements[i];
            if (remaining <= element.LengthKm || i == _elements.Count - 1)
                return (element.PointAt(Math.Min(remaining, element.LengthKm)), element.RadiusM);
            remaining -= element.LengthKm;
        }
        return (_elements[^1].PointAt(_elements[^1].LengthKm), _elements[^1].RadiusM);
    }

    private readonly struct Element
    {
        private readonly bool _isArc;
        private readonly GeoPoint _origin;
        private readonly GeoPoint _direction;
        private readonly double _radiusKm;
        private readonly double _startAngle;
        private readonly double _sweep;

        public double LengthKm { get; }
        public double RadiusM => _isArc ? _radiusKm * 1000.0 : 0;

        private Element(
            bool isArc, GeoPoint origin, GeoPoint direction,
            double radiusKm, double startAngle, double sweep, double lengthKm)
        {
            _isArc = isArc;
            _origin = origin;
            _direction = direction;
            _radiusKm = radiusKm;
            _startAngle = startAngle;
            _sweep = sweep;
            LengthKm = lengthKm;
        }

        public static Element Tangent(GeoPoint start, GeoPoint direction, double lengthKm)
            => new(false, start, direction, 0, 0, 0, lengthKm);

        public static Element Arc(GeoPoint centre, double radiusKm, double startAngle, double sweep)
            => new(true, centre, default, radiusKm, startAngle, sweep, radiusKm * Math.Abs(sweep));

        public GeoPoint PointAt(double s)
        {
            if (!_isArc) return _origin + _direction * s;
            double angle = _startAngle + Math.Sign(_sweep) * (s / _radiusKm);
            return new GeoPoint(
                _origin.XKm + Math.Cos(angle) * _radiusKm,
                _origin.YKm + Math.Sin(angle) * _radiusKm);
        }
    }
}
