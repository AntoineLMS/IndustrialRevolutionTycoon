using System.Globalization;
using RailTycoon.Sim.Core;

namespace RailTycoon.Sim.Network;

/// <summary>Un point de la carte, en kilomètres depuis le coin sud-ouest.</summary>
public readonly record struct GeoPoint(double XKm, double YKm)
{
    public static GeoPoint operator +(GeoPoint a, GeoPoint b) => new(a.XKm + b.XKm, a.YKm + b.YKm);
    public static GeoPoint operator -(GeoPoint a, GeoPoint b) => new(a.XKm - b.XKm, a.YKm - b.YKm);
    public static GeoPoint operator *(GeoPoint a, double factor) => new(a.XKm * factor, a.YKm * factor);

    public double Norm => Math.Sqrt(XKm * XKm + YKm * YKm);

    public GeoPoint Normalized()
    {
        double n = Norm;
        return n <= 0 ? new GeoPoint(0, 0) : new GeoPoint(XKm / n, YKm / n);
    }

    public static double Cross(GeoPoint a, GeoPoint b) => a.XKm * b.YKm - a.YKm * b.XKm;
    public static double Dot(GeoPoint a, GeoPoint b) => a.XKm * b.XKm + a.YKm * b.YKm;
}

/// <summary>
/// Le relief, une grille régulière d'altitudes interpolée bilinéairement.
/// <para>
/// C'est la seule source de vérité du terrain, et elle est immuable une fois
/// construite. Tout le reste du module — tracé, terrassement, ponts, tunnels — ne
/// fait que l'interroger, ce qui garantit qu'un même tracé sur une même carte
/// donne toujours le même devis.
/// </para>
/// </summary>
public sealed class HeightField
{
    private readonly double[] _elevations;

    public int Columns { get; }
    public int Rows { get; }
    public double CellSizeKm { get; }

    public double WidthKm => (Columns - 1) * CellSizeKm;
    public double HeightKm => (Rows - 1) * CellSizeKm;

    /// <summary>
    /// Les fleuves de la carte. Le relief en porte déjà la vallée ; ils sont gardés ici
    /// pour ce qui doit savoir où passe l'eau — les charrettes, qui ne la franchissent
    /// qu'aux points de passage, les bateaux, et le géomètre, qui y jette un pont.
    /// </summary>
    public IReadOnlyList<River> Rivers { get; }

    public HeightField(int columns, int rows, double cellSizeKm, double[] elevations,
        IReadOnlyList<River>? rivers = null)
    {
        if (columns < 2 || rows < 2)
            throw new InvalidDataException("Le relief doit compter au moins deux colonnes et deux rangées.");
        if (cellSizeKm <= 0)
            throw new InvalidDataException("La taille de maille du relief doit être strictement positive.");
        if (elevations.Length != columns * rows)
            throw new InvalidDataException(
                $"Le relief déclare {columns}×{rows} mailles mais fournit {elevations.Length} altitudes.");

        Columns = columns;
        Rows = rows;
        CellSizeKm = cellSizeKm;
        _elevations = elevations;
        Rivers = rivers ?? Array.Empty<River>();
    }

    public double ElevationAtCell(int column, int row)
        => _elevations[Math.Clamp(row, 0, Rows - 1) * Columns + Math.Clamp(column, 0, Columns - 1)];

    /// <summary>
    /// Altitude en un point quelconque. Hors carte, l'altitude du bord est
    /// prolongée : un tracé ne doit jamais sortir de la carte, et
    /// <see cref="Contains"/> permet de le refuser au chargement, mais un
    /// arrondi d'un micromètre au ras du bord ne doit pas faire exploser un devis.
    /// </summary>
    public double ElevationAt(GeoPoint point)
    {
        double fx = Maths.Clamp(point.XKm / CellSizeKm, 0, Columns - 1);
        double fy = Maths.Clamp(point.YKm / CellSizeKm, 0, Rows - 1);

        int x0 = (int)Math.Floor(fx);
        int y0 = (int)Math.Floor(fy);
        if (x0 >= Columns - 1) x0 = Columns - 2;
        if (y0 >= Rows - 1) y0 = Rows - 2;

        double tx = fx - x0;
        double ty = fy - y0;

        double z00 = ElevationAtCell(x0, y0);
        double z10 = ElevationAtCell(x0 + 1, y0);
        double z01 = ElevationAtCell(x0, y0 + 1);
        double z11 = ElevationAtCell(x0 + 1, y0 + 1);

        double south = z00 + (z10 - z00) * tx;
        double north = z01 + (z11 - z01) * tx;
        return south + (north - south) * ty;
    }

    public bool Contains(GeoPoint point)
        => point.XKm >= 0 && point.YKm >= 0 && point.XKm <= WidthKm && point.YKm <= HeightKm;

    /// <summary>
    /// Empreinte de la carte, pour vérifier en test qu'une génération à graine
    /// identique produit un relief identique.
    /// </summary>
    public string Fingerprint()
    {
        // Somme de contrôle entière : deux relevés flottants identiques donnent la
        // même empreinte, et une différence d'un millimètre quelque part se voit.
        ulong hash = 1469598103934665603UL;
        foreach (double z in _elevations)
        {
            long q = (long)Math.Round(z * 1000.0);
            for (int b = 0; b < 8; b++)
            {
                hash ^= (byte)(q >> (b * 8));
                hash *= 1099511628211UL;
            }
        }
        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Construit une carte de hauteurs depuis les données du scénario. Déterministe
/// par construction : les formes sont analytiques et appliquées dans l'ordre de
/// déclaration, et la rugosité ne tire que de <see cref="DeterministicRandom"/>.
/// </summary>
public static class TerrainFactory
{
    public static HeightField Build(TerrainDef def, ulong scenarioSeed)
    {
        int columns = def.Columns;
        int rows = def.Rows;

        double[] elevations;
        if (def.Heights.Count > 0)
        {
            elevations = ParseExplicit(def, out columns, out rows);
        }
        else
        {
            if (columns < 2 || rows < 2)
                throw new InvalidDataException(
                    "Le relief doit déclarer columns et rows (≥ 2) ou fournir heights.");
            elevations = new double[columns * rows];
            for (int i = 0; i < elevations.Length; i++) elevations[i] = def.BaseElevationM;
        }

        ApplyFeatures(def, columns, rows, elevations);
        if (def.Noise is not null) ApplyNoise(def.Noise, def.CellSizeKm, columns, rows, elevations, scenarioSeed);

        var rivers = def.Features.Where(f => f.Kind == "river").Select(BuildRiver).ToList();
        return new HeightField(columns, rows, def.CellSizeKm, elevations, rivers);
    }

    /// <summary>
    /// Un fleuve tel que les données le déclarent. Ses points de passage et ses ports
    /// doivent être sur son cours, à moins d'une demi-largeur de vallée : un gué posé à
    /// côté de l'eau ne servirait à personne, sans que rien le dise.
    /// </summary>
    private static River BuildRiver(TerrainFeatureDef def)
    {
        string name = string.IsNullOrWhiteSpace(def.Name) ? "fleuve sans nom" : def.Name;
        var course = def.Points.Select(p => new GeoPoint(p.XKm, p.YKm)).ToList();
        var shell = new River { Name = name, Points = course, Crossings = [], Ports = [] };

        GeoPoint OnCourse(double x, double y, string what)
        {
            var point = new GeoPoint(x, y);
            var (_, distance) = shell.Project(point);
            if (distance > def.HalfWidthKm + 1e-9)
                throw new InvalidDataException(
                    $"Le fleuve « {name} » : {what} est à {distance:0.##} km de son cours, plus que sa demi-largeur ({def.HalfWidthKm} km).");
            return point;
        }

        var crossings = new List<RiverCrossing>();
        foreach (var c in def.Crossings)
        {
            var kind = c.Kind switch
            {
                "ford" => CrossingKind.Ford,
                "bridge" => CrossingKind.Bridge,
                "ferry" => CrossingKind.Ferry,
                _ => throw new InvalidDataException(
                    $"Le fleuve « {name} » : passage « {c.Name} » de sorte inconnue « {c.Kind} » (attendu « ford », « bridge » ou « ferry »)."),
            };
            crossings.Add(new RiverCrossing(c.Name, OnCourse(c.XKm, c.YKm, $"le passage « {c.Name} »"), kind));
        }

        var ports = new List<RiverPort>();
        foreach (var p in def.Ports)
        {
            var point = OnCourse(p.XKm, p.YKm, $"le port « {p.Name} »");
            ports.Add(new RiverPort(p.Name, point, shell.Project(point).ChainageKm));
        }
        if (ports.Select(p => p.Name).Distinct().Count() != ports.Count)
            throw new InvalidDataException($"Le fleuve « {name} » déclare deux ports du même nom.");

        return new River
        {
            Name = name, Points = course, Crossings = crossings,
            Ports = ports.OrderBy(p => p.ChainageKm).ThenBy(p => p.Name, StringComparer.Ordinal).ToList(),
        };
    }

    private static double[] ParseExplicit(TerrainDef def, out int columns, out int rows)
    {
        var grid = new List<double[]>();
        foreach (string line in def.Heights)
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var row = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out row[i]))
                    throw new InvalidDataException($"Altitude illisible dans le relief : « {parts[i]} ».");
            }
            grid.Add(row);
        }

        rows = grid.Count;
        columns = rows == 0 ? 0 : grid[0].Length;
        foreach (var row in grid)
            if (row.Length != columns)
                throw new InvalidDataException("Toutes les rangées du relief doivent avoir la même longueur.");

        var elevations = new double[columns * rows];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
                elevations[y * columns + x] = grid[y][x];
        return elevations;
    }

    private static void ApplyFeatures(TerrainDef def, int columns, int rows, double[] elevations)
    {
        foreach (var feature in def.Features)
        {
            if (feature.HalfWidthKm <= 0)
                throw new InvalidDataException("Une forme de relief doit avoir une portée strictement positive.");
            if (feature.Kind == "river" && feature.Points.Count < 2)
                throw new InvalidDataException("Un fleuve doit déclarer au moins deux points, d'amont en aval.");
            var course = feature.Points.Select(p => new GeoPoint(p.XKm, p.YKm)).ToList();

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    var point = new GeoPoint(x * def.CellSizeKm, y * def.CellSizeKm);
                    double distance = feature.Kind switch
                    {
                        "bowl" => (point - new GeoPoint(feature.XKm, feature.YKm)).Norm,
                        "ridge" => DistanceToSegment(
                            point,
                            new GeoPoint(feature.X1Km, feature.Y1Km),
                            new GeoPoint(feature.X2Km, feature.Y2Km)),
                        "river" => DistanceToPolyline(point, course),
                        _ => throw new InvalidDataException(
                            $"Forme de relief inconnue : « {feature.Kind} » (attendu « ridge », « bowl » ou « river »)."),
                    };
                    elevations[y * columns + x] += feature.AmplitudeM * Bell(distance / feature.HalfWidthKm);
                }
            }
        }
    }

    /// <summary>
    /// Profil en cloche cosinusoïdale : nul et de dérivée nulle au bord, maximal au
    /// centre. Un profil triangulaire produirait une arête vive dont la pente
    /// dépendrait du pas d'échantillonnage, donc un devis qui bougerait pour de
    /// mauvaises raisons.
    /// </summary>
    private static double Bell(double t)
        => t >= 1.0 ? 0.0 : 0.5 * (1.0 + Math.Cos(Math.PI * t));

    private static void ApplyNoise(
        TerrainNoiseDef noise, double cellSizeKm, int columns, int rows, double[] elevations, ulong scenarioSeed)
    {
        if (noise.AmplitudeM == 0 || noise.Octaves <= 0) return;
        if (noise.WavelengthKm <= 0)
            throw new InvalidDataException("La longueur d'onde de la rugosité doit être strictement positive.");

        double amplitude = noise.AmplitudeM;
        double wavelength = noise.WavelengthKm;

        for (int octave = 0; octave < noise.Octaves; octave++)
        {
            // Une séquence PCG distincte par octave : deux octaves d'un même relief
            // ne doivent pas être corrélées, et changer le nombre d'octaves ne doit
            // pas redessiner celles qu'on gardait.
            var rng = new DeterministicRandom(scenarioSeed ^ noise.Seed, (ulong)(octave + 1));

            double lattice = wavelength / cellSizeKm;
            int latticeColumns = (int)Math.Ceiling(columns / lattice) + 2;
            int latticeRows = (int)Math.Ceiling(rows / lattice) + 2;

            var values = new double[latticeColumns * latticeRows];
            for (int i = 0; i < values.Length; i++) values[i] = rng.NextRange(-1.0, 1.0);

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    double fx = x / lattice;
                    double fy = y / lattice;
                    int x0 = (int)Math.Floor(fx);
                    int y0 = (int)Math.Floor(fy);
                    double tx = Smooth(fx - x0);
                    double ty = Smooth(fy - y0);

                    double v00 = values[y0 * latticeColumns + x0];
                    double v10 = values[y0 * latticeColumns + x0 + 1];
                    double v01 = values[(y0 + 1) * latticeColumns + x0];
                    double v11 = values[(y0 + 1) * latticeColumns + x0 + 1];

                    double south = v00 + (v10 - v00) * tx;
                    double north = v01 + (v11 - v01) * tx;
                    elevations[y * columns + x] += amplitude * (south + (north - south) * ty);
                }
            }

            amplitude *= 0.5;
            wavelength *= 0.5;
        }
    }

    private static double Smooth(double t) => t * t * (3.0 - 2.0 * t);

    public static double DistanceToPolyline(GeoPoint point, IReadOnlyList<GeoPoint> points)
    {
        double best = double.MaxValue;
        for (int i = 1; i < points.Count; i++)
            best = Math.Min(best, DistanceToSegment(point, points[i - 1], points[i]));
        return best;
    }

    /// <summary>Le point où se coupent les droites (a, b) et (c, d) ; le milieu de [a, b] si elles sont parallèles.</summary>
    public static GeoPoint IntersectionPoint(GeoPoint a, GeoPoint b, GeoPoint c, GeoPoint d)
    {
        var r = b - a;
        var s = d - c;
        double denominator = GeoPoint.Cross(r, s);
        if (Math.Abs(denominator) < 1e-12) return a + r * 0.5;
        double t = GeoPoint.Cross(c - a, s) / denominator;
        return a + r * Maths.Clamp(t, 0, 1);
    }

    /// <summary>Vrai si les segments [a, b] et [c, d] se coupent, extrémités comprises.</summary>
    public static bool SegmentsIntersect(GeoPoint a, GeoPoint b, GeoPoint c, GeoPoint d)
    {
        double d1 = GeoPoint.Cross(b - a, c - a), d2 = GeoPoint.Cross(b - a, d - a);
        double d3 = GeoPoint.Cross(d - c, a - c), d4 = GeoPoint.Cross(d - c, b - c);
        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
        bool OnSegment(GeoPoint p, GeoPoint q, GeoPoint r, double cross)
            => Math.Abs(cross) < 1e-12
               && Math.Min(p.XKm, q.XKm) - 1e-12 <= r.XKm && r.XKm <= Math.Max(p.XKm, q.XKm) + 1e-12
               && Math.Min(p.YKm, q.YKm) - 1e-12 <= r.YKm && r.YKm <= Math.Max(p.YKm, q.YKm) + 1e-12;
        return OnSegment(a, b, c, d1) || OnSegment(a, b, d, d2) || OnSegment(c, d, a, d3) || OnSegment(c, d, b, d4);
    }

    public static double DistanceToSegment(GeoPoint point, GeoPoint a, GeoPoint b)
    {
        var ab = b - a;
        double lengthSquared = GeoPoint.Dot(ab, ab);
        if (lengthSquared <= 0) return (point - a).Norm;
        double t = Maths.Clamp(GeoPoint.Dot(point - a, ab) / lengthSquared, 0, 1);
        return (point - (a + ab * t)).Norm;
    }
}

/// <summary>Sorte d'un point de passage sur un fleuve.</summary>
public enum CrossingKind
{
    Ford,
    Bridge,
    Ferry,
}

/// <summary>Un point de passage sur un fleuve : là, et là seulement, une charrette le franchit.</summary>
public sealed record RiverCrossing(string Name, GeoPoint Point, CrossingKind Kind);

/// <summary>Un port sur un fleuve, avec sa position le long du cours, depuis la source.</summary>
public sealed record RiverPort(string Name, GeoPoint Point, double ChainageKm);

/// <summary>
/// Un fleuve : son cours d'amont en aval, ses points de passage et ses ports. Le
/// relief porte déjà sa vallée ; ce qui est ici sert à ce qui doit savoir où passe
/// l'eau — les charrettes, qui ne la franchissent qu'aux points de passage, les
/// bateaux, qui la suivent de port en port, et le géomètre, qui y jette un pont.
/// </summary>
public sealed class River
{
    public required string Name { get; init; }

    /// <summary>Le cours, d'amont en aval.</summary>
    public required IReadOnlyList<GeoPoint> Points { get; init; }

    public required IReadOnlyList<RiverCrossing> Crossings { get; init; }

    /// <summary>Les ports, de l'amont vers l'aval.</summary>
    public required IReadOnlyList<RiverPort> Ports { get; init; }

    /// <summary>Longueur du cours, en kilomètres.</summary>
    public double LengthKm
    {
        get
        {
            double total = 0;
            for (int i = 1; i < Points.Count; i++) total += (Points[i] - Points[i - 1]).Norm;
            return total;
        }
    }

    /// <summary>Distance le long du cours du point du fleuve le plus proche, et distance à lui.</summary>
    public (double ChainageKm, double DistanceKm) Project(GeoPoint point)
    {
        double best = double.MaxValue, bestChainage = 0, chainage = 0;
        for (int i = 1; i < Points.Count; i++)
        {
            var a = Points[i - 1];
            var ab = Points[i] - a;
            double length = ab.Norm;
            double t = length > 0 ? Maths.Clamp(GeoPoint.Dot(point - a, ab) / (length * length), 0, 1) : 0;
            double distance = (point - (a + ab * t)).Norm;
            if (distance < best) { best = distance; bestChainage = chainage + t * length; }
            chainage += length;
        }
        return (bestChainage, best);
    }

    /// <summary>Le point du cours à cette distance de la source.</summary>
    public GeoPoint PointAt(double chainageKm)
    {
        double chainage = 0;
        for (int i = 1; i < Points.Count; i++)
        {
            double length = (Points[i] - Points[i - 1]).Norm;
            if (chainageKm <= chainage + length || i == Points.Count - 1)
            {
                double t = length > 0 ? Maths.Clamp((chainageKm - chainage) / length, 0, 1) : 0;
                return Points[i - 1] + (Points[i] - Points[i - 1]) * t;
            }
            chainage += length;
        }
        return Points[^1];
    }

    /// <summary>Les sommets du cours strictement entre deux distances, dans le sens du trajet.</summary>
    public IEnumerable<(GeoPoint Point, double ChainageKm)> VerticesBetween(double fromKm, double toKm)
    {
        var vertices = new List<(GeoPoint, double)>();
        double chainage = 0;
        for (int i = 0; i < Points.Count; i++)
        {
            if (i > 0) chainage += (Points[i] - Points[i - 1]).Norm;
            if (chainage > Math.Min(fromKm, toKm) + 1e-9 && chainage < Math.Max(fromKm, toKm) - 1e-9)
                vertices.Add((Points[i], chainage));
        }
        if (toKm < fromKm) vertices.Reverse();
        return vertices;
    }

    /// <summary>
    /// Les points où le segment [a, b] coupe le cours. Un segment qui passe par un
    /// sommet du cours coupe les deux segments qui s'y rejoignent : le point n'est
    /// compté qu'une fois, sans quoi un passage se paierait deux fois.
    /// </summary>
    public IEnumerable<GeoPoint> Intersections(GeoPoint a, GeoPoint b)
    {
        var found = new List<GeoPoint>();
        for (int i = 1; i < Points.Count; i++)
        {
            if (!TerrainFactory.SegmentsIntersect(a, b, Points[i - 1], Points[i])) continue;
            var point = TerrainFactory.IntersectionPoint(a, b, Points[i - 1], Points[i]);
            if (found.All(p => (p - point).Norm > 1e-9)) found.Add(point);
        }
        return found;
    }
}
