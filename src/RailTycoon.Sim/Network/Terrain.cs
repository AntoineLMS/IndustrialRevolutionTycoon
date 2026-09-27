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

    public HeightField(int columns, int rows, double cellSizeKm, double[] elevations)
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

        return new HeightField(columns, rows, def.CellSizeKm, elevations);
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
                        _ => throw new InvalidDataException(
                            $"Forme de relief inconnue : « {feature.Kind} » (attendu « ridge » ou « bowl »)."),
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

    public static double DistanceToSegment(GeoPoint point, GeoPoint a, GeoPoint b)
    {
        var ab = b - a;
        double lengthSquared = GeoPoint.Dot(ab, ab);
        if (lengthSquared <= 0) return (point - a).Norm;
        double t = Maths.Clamp(GeoPoint.Dot(point - a, ab) / lengthSquared, 0, 1);
        return (point - (a + ab * t)).Norm;
    }
}
