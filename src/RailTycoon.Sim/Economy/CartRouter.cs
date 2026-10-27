using RailTycoon.Sim.Network;

namespace RailTycoon.Sim.Economy;

/// <summary>
/// La route d'une charrette : une ligne brisée sur la carte, et la distance de route
/// cumulée à chaque point — kilomètres parcourus, plus ce que valent les montées et
/// les gués. C'est sur cette distance que se comptent le temps du trajet et son coût.
/// </summary>
public sealed class CartRoute
{
    public required IReadOnlyList<GeoPoint> Points { get; init; }

    /// <summary>Distance de route depuis le départ, à chaque point. Même longueur que <see cref="Points"/>.</summary>
    public required IReadOnlyList<double> RoadKm { get; init; }

    /// <summary>Distance de route totale. Infinie si aucune route ne passe.</summary>
    public double LengthKm => RoadKm.Count == 0 ? double.PositiveInfinity : RoadKm[^1];

    public bool Passable => !double.IsInfinity(LengthKm);

    /// <summary>Longueur sur la carte, sans les montées ni les gués.</summary>
    public double MapKm
    {
        get
        {
            double total = 0;
            for (int i = 1; i < Points.Count; i++) total += (Points[i] - Points[i - 1]).Norm;
            return total;
        }
    }

    /// <summary>Où est la charrette après <paramref name="roadKm"/> de route.</summary>
    public GeoPoint PointAt(double roadKm)
    {
        if (Points.Count == 0) return default;
        if (roadKm <= 0 || Points.Count == 1) return Points[0];
        for (int i = 1; i < Points.Count; i++)
        {
            if (roadKm > RoadKm[i] && i < Points.Count - 1) continue;
            double span = RoadKm[i] - RoadKm[i - 1];
            double t = span > 0 ? Math.Clamp((roadKm - RoadKm[i - 1]) / span, 0, 1) : 1;
            return Points[i - 1] + (Points[i] - Points[i - 1]) * t;
        }
        return Points[^1];
    }

    /// <summary>La route en ligne droite, sur une carte sans relief.</summary>
    public static CartRoute Straight(GeoPoint from, GeoPoint to)
        => new() { Points = [from, to], RoadKm = [0, (to - from).Norm] };

    public static CartRoute Impassable(GeoPoint from, GeoPoint to)
        => new() { Points = [from, to], RoadKm = [0, double.PositiveInfinity] };
}

/// <summary>
/// Trouve la route des charrettes sur le relief : le chemin le moins pénible sur la
/// grille de la carte, de nœud en nœud vers ses huit voisins.
/// <para>
/// Un pas vaut sa longueur, plus <see cref="CartageDef.ClimbKmPerMetre"/> par mètre
/// gravi, plus <see cref="CartageDef.RiverCrossingKm"/> s'il franchit un fleuve ; un
/// pas plus raide que <see cref="CartageDef.MaxGradePercent"/> ne passe pas. La
/// charrette longe donc les vallées et les fleuves, passe les cols, et ne franchit un
/// fleuve qu'une fois. Départ et arrivée se raccordent au nœud le plus proche en ligne
/// droite. Sans relief, la route est la ligne droite.
/// </para>
/// <para>
/// Les marchés ne bougent pas : une route se calcule une fois par couple et se garde.
/// Le chemin depuis un départ vers tous les nœuds est calculé une fois par départ.
/// Le calcul est déterministe : mêmes données, même relief, mêmes routes.
/// </para>
/// </summary>
public sealed class CartRouter
{
    private static readonly (int Dx, int Dy)[] Steps =
        [(1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, 1), (-1, -1), (1, -1)];

    private readonly HeightField? _terrain;
    private readonly CartageDef _cfg;
    private readonly Dictionary<(Market, Market), CartRoute> _routes = new();
    private readonly Dictionary<int, (double[] Cost, int[] Parent)> _fromNode = new();
    private bool[]? _crossesRiver;

    public CartRouter(HeightField? terrain, CartageDef cfg)
    {
        _terrain = terrain;
        _cfg = cfg;
    }

    public CartRoute Route(Market from, Market to)
    {
        if (_routes.TryGetValue((from, to), out var route)) return route;
        route = Compute(new GeoPoint(from.XKm, from.YKm), new GeoPoint(to.XKm, to.YKm));
        _routes[(from, to)] = route;
        return route;
    }

    private CartRoute Compute(GeoPoint from, GeoPoint to)
    {
        if (_terrain is null) return CartRoute.Straight(from, to);

        double cell = _terrain.CellSizeKm;
        // Deux marchés dans la même maille ou voisins : une seule ligne droite, qui
        // paie quand même sa montée et son gué.
        if ((to - from).Norm <= cell * 1.5)
            return new CartRoute { Points = [from, to], RoadKm = [0, Leg(from, to)] };

        int source = NearestNode(from), target = NearestNode(to);
        var (cost, parent) = FromNode(source);
        if (double.IsInfinity(cost[target])) return CartRoute.Impassable(from, to);

        var nodes = new List<int>();
        for (int n = target; n != -1; n = parent[n]) nodes.Add(n);
        nodes.Reverse();

        var points = new List<GeoPoint> { from };
        var road = new List<double> { 0 };
        double start = Leg(from, NodePoint(source));
        foreach (int n in nodes)
        {
            points.Add(NodePoint(n));
            road.Add(start + cost[n]);
        }
        points.Add(to);
        road.Add(road[^1] + Leg(NodePoint(target), to));
        return new CartRoute { Points = points, RoadKm = road };
    }

    /// <summary>Plus court chemin depuis un nœud vers tous les autres, gardé pour les routes suivantes.</summary>
    private (double[] Cost, int[] Parent) FromNode(int source)
    {
        if (_fromNode.TryGetValue(source, out var known)) return known;

        var terrain = _terrain!;
        int columns = terrain.Columns, count = columns * terrain.Rows;
        var cost = new double[count];
        var parent = new int[count];
        Array.Fill(cost, double.PositiveInfinity);
        Array.Fill(parent, -1);
        cost[source] = 0;

        // File de priorité sur (coût, nœud) : à coût égal, le plus petit indice sort
        // le premier, ce qui rend le chemin indépendant de la file elle-même.
        var queue = new PriorityQueue<int, (double, int)>();
        queue.Enqueue(source, (0, source));
        double maxGrade = _cfg.MaxGradePercent / 100.0;
        var crossing = CrossesRiver();

        while (queue.TryDequeue(out int node, out var priority))
        {
            if (priority.Item1 > cost[node]) continue;
            int x = node % columns, y = node / columns;
            double here = terrain.ElevationAtCell(x, y);
            for (int k = 0; k < Steps.Length; k++)
            {
                int nx = x + Steps[k].Dx, ny = y + Steps[k].Dy;
                if (nx < 0 || ny < 0 || nx >= columns || ny >= terrain.Rows) continue;
                double length = terrain.CellSizeKm * (k < 4 ? 1.0 : Math.Sqrt(2.0));
                double rise = terrain.ElevationAtCell(nx, ny) - here;
                if (Math.Abs(rise) > maxGrade * length * 1000.0) continue;

                double step = length + _cfg.ClimbKmPerMetre * Math.Max(0, rise);
                if (crossing is not null && crossing[node * Steps.Length + k]) step += _cfg.RiverCrossingKm;

                int next = ny * columns + nx;
                double candidate = cost[node] + step;
                if (candidate < cost[next])
                {
                    cost[next] = candidate;
                    parent[next] = node;
                    queue.Enqueue(next, (candidate, next));
                }
            }
        }

        _fromNode[source] = (cost, parent);
        return (cost, parent);
    }

    /// <summary>Pour chaque nœud et chaque direction, si le pas franchit un fleuve. <c>null</c> sans fleuve.</summary>
    private bool[]? CrossesRiver()
    {
        var terrain = _terrain!;
        if (terrain.Rivers.Count == 0) return null;
        if (_crossesRiver is not null) return _crossesRiver;

        int columns = terrain.Columns, count = columns * terrain.Rows;
        var flags = new bool[count * Steps.Length];
        for (int node = 0; node < count; node++)
        {
            int x = node % columns, y = node / columns;
            for (int k = 0; k < Steps.Length; k++)
            {
                int nx = x + Steps[k].Dx, ny = y + Steps[k].Dy;
                if (nx < 0 || ny < 0 || nx >= columns || ny >= terrain.Rows) continue;
                flags[node * Steps.Length + k] = Crosses(NodePoint(node), NodePoint(ny * columns + nx));
            }
        }
        _crossesRiver = flags;
        return flags;
    }

    private bool Crosses(GeoPoint a, GeoPoint b)
    {
        foreach (var river in _terrain!.Rivers)
            for (int i = 1; i < river.Count; i++)
                if (TerrainFactory.SegmentsIntersect(a, b, river[i - 1], river[i])) return true;
        return false;
    }

    /// <summary>Un raccord en ligne droite : sa longueur, sa montée, son gué. Sans limite de pente : il est court.</summary>
    private double Leg(GeoPoint a, GeoPoint b)
    {
        double length = (b - a).Norm;
        double rise = _terrain!.ElevationAt(b) - _terrain.ElevationAt(a);
        return length + _cfg.ClimbKmPerMetre * Math.Max(0, rise) + (Crosses(a, b) ? _cfg.RiverCrossingKm : 0);
    }

    private int NearestNode(GeoPoint point)
    {
        var terrain = _terrain!;
        int x = Math.Clamp((int)Math.Round(point.XKm / terrain.CellSizeKm), 0, terrain.Columns - 1);
        int y = Math.Clamp((int)Math.Round(point.YKm / terrain.CellSizeKm), 0, terrain.Rows - 1);
        return y * terrain.Columns + x;
    }

    private GeoPoint NodePoint(int node)
        => new((node % _terrain!.Columns) * _terrain.CellSizeKm, (node / _terrain.Columns) * _terrain.CellSizeKm);
}
