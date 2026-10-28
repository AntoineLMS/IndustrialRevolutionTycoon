using RailTycoon.Sim.Network;

namespace RailTycoon.Sim.Economy;

/// <summary>
/// La route d'un chargement qui voyage sans train : une ligne brisée sur la carte, et à
/// chaque point le coût et le temps cumulés depuis le départ. Elle peut emprunter la
/// route et le fleuve ; le coût et le temps ne suivent pas la même échelle — un bateau
/// est bon marché et lent —, d'où deux cumuls.
/// </summary>
public sealed class CartRoute
{
    public required IReadOnlyList<GeoPoint> Points { get; init; }

    /// <summary>Coût par chargement depuis le départ, à chaque point, hors chargement au départ.</summary>
    public required IReadOnlyList<double> Cost { get; init; }

    /// <summary>Jours de voyage depuis le départ, à chaque point.</summary>
    public required IReadOnlyList<double> Days { get; init; }

    /// <summary>Par segment, vrai s'il se fait en bateau. Un de moins que <see cref="Points"/>.</summary>
    public required IReadOnlyList<bool> ByWater { get; init; }

    public double TotalCost => Cost.Count == 0 ? double.PositiveInfinity : Cost[^1];
    public double TotalDays => Days.Count == 0 ? double.PositiveInfinity : Days[^1];
    public bool Passable => !double.IsInfinity(TotalCost);
    public bool UsesWater => ByWater.Any(w => w);

    /// <summary>Longueur sur la carte.</summary>
    public double MapKm => Length(_ => true);

    /// <summary>Longueur parcourue en bateau.</summary>
    public double WaterKm => Length(i => ByWater[i]);

    private double Length(Func<int, bool> include)
    {
        double total = 0;
        for (int i = 1; i < Points.Count; i++)
            if (include(i - 1)) total += (Points[i] - Points[i - 1]).Norm;
        return total;
    }

    /// <summary>Où est le chargement après <paramref name="days"/> jours de voyage.</summary>
    public GeoPoint PointAt(double days)
    {
        int i = SegmentAt(days, out double t);
        return i < 0 ? Points[0] : Points[i] + (Points[i + 1] - Points[i]) * t;
    }

    /// <summary>Ce que le voyage a coûté après <paramref name="days"/> jours.</summary>
    public double CostAt(double days)
    {
        int i = SegmentAt(days, out double t);
        return i < 0 ? Cost[0] : Cost[i] + (Cost[i + 1] - Cost[i]) * t;
    }

    private int SegmentAt(double days, out double t)
    {
        t = 0;
        if (Points.Count < 2) return -1;
        for (int i = 0; i < Points.Count - 1; i++)
        {
            if (days > Days[i + 1] && i < Points.Count - 2) continue;
            double span = Days[i + 1] - Days[i];
            t = span > 0 ? Math.Clamp((days - Days[i]) / span, 0, 1) : (days >= Days[i + 1] ? 1 : 0);
            return i;
        }
        return -1;
    }

    /// <summary>La ligne droite par la route, sur une carte sans relief.</summary>
    public static CartRoute Straight(GeoPoint from, GeoPoint to, CartageDef cartage)
    {
        double km = (to - from).Norm;
        return new CartRoute
        {
            Points = [from, to], Cost = [0, cartage.CostPerLoadKm * km],
            Days = [0, km / cartage.SpeedKmPerTick], ByWater = [false],
        };
    }

    public static CartRoute Impassable(GeoPoint from, GeoPoint to)
        => new()
        {
            Points = [from, to], Cost = [0, double.PositiveInfinity],
            Days = [0, double.PositiveInfinity], ByWater = [false],
        };
}

/// <summary>
/// Trouve la route la moins chère d'un chargement qui voyage sans train, sur le relief
/// et les fleuves.
/// <para>
/// <b>Par la route</b>, de nœud en nœud de la grille du relief vers ses huit voisins.
/// Un pas vaut sa longueur, plus <see cref="CartageDef.ClimbKmPerMetre"/> par mètre
/// gravi ; plus raide que <see cref="CartageDef.MaxGradePercent"/>, il ne passe pas. Un
/// pas qui coupe un fleuve ne passe qu'à un point de passage proche — un gué, un pont,
/// un bac déclaré sur le cours, ou un pont du chemin de fer —, et paie ce passage. La
/// charrette longe donc les vallées et les fleuves, passe les cols, contourne ce qui est
/// trop raide, et converge vers les points de passage.
/// </para>
/// <para>
/// <b>Par l'eau</b>, de port en port en suivant le cours, plus vite à la descente qu'à
/// la remontée ; chaque chargement et chaque déchargement dans un port se paie. Un port
/// se raccorde au nœud de la grille le plus proche, et à lui seul : un port n'est pas un
/// passage d'une rive à l'autre.
/// </para>
/// <para>
/// La route minimise le coût ; le temps se compte sur la route retenue. Les marchés ne
/// bougent pas : une route se calcule une fois par couple et se garde, et le chemin depuis
/// un nœud vers tous les autres une fois par nœud de départ. Sans relief, la route est la
/// ligne droite, par la route. Le calcul est déterministe.
/// </para>
/// </summary>
public sealed class CartRouter
{
    private static readonly (int Dx, int Dy)[] Steps =
        [(1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, 1), (-1, -1), (1, -1)];

    private readonly HeightField? _terrain;
    private readonly CartageDef _cart;
    private readonly WaterwaysDef _water;

    /// <summary>Points de passage de chaque fleuve, ponts du chemin de fer compris, dans l'ordre des fleuves.</summary>
    private readonly List<List<RiverCrossing>> _crossings = new();

    /// <summary>Les ports de tous les fleuves, et leur fleuve. Le port k est le nœud G + k du graphe.</summary>
    private readonly List<(River River, RiverPort Port, int Node)> _ports = new();
    private readonly Dictionary<int, List<int>> _portsAtNode = new();

    private readonly Dictionary<(Market, Market), CartRoute> _routes = new();
    private readonly Dictionary<int, (double[] Cost, int[] Parent)> _fromNode = new();

    /// <summary>Par nœud et direction : 0 sans fleuve, le passage en km de route sinon, l'infini sans passage.</summary>
    private double[]? _stepCrossingKm;

    public CartRouter(WorldState world)
    {
        _terrain = world.Terrain;
        _cart = world.Def.Economy.Cartage;
        _water = world.Def.Economy.Waterways;
        if (_terrain is null) return;

        foreach (var river in _terrain.Rivers)
        {
            var crossings = new List<RiverCrossing>(river.Crossings);
            // Les ponts du chemin de fer sont des points de passage : là où un tronçon
            // de voie coupe le fleuve, une charrette passe.
            if (world.Network is { } network)
                foreach (var edge in network.Graph.Edges)
                    for (int i = 1; i < edge.ControlPoints.Count; i++)
                        foreach (var point in river.Intersections(edge.ControlPoints[i - 1], edge.ControlPoints[i]))
                            crossings.Add(new RiverCrossing($"pont du chemin de fer « {edge.Id} »", point, CrossingKind.Bridge));
            _crossings.Add(crossings);

            foreach (var port in river.Ports)
            {
                int node = NearestNode(port.Point);
                _ports.Add((river, port, node));
                if (!_portsAtNode.TryGetValue(node, out var list)) _portsAtNode[node] = list = new List<int>();
                list.Add(_ports.Count - 1);
            }
        }
    }

    /// <summary>Les points de passage de chaque fleuve, ponts du chemin de fer compris.</summary>
    public IReadOnlyList<IReadOnlyList<RiverCrossing>> Crossings => _crossings;

    public CartRoute Route(Market from, Market to)
    {
        if (_routes.TryGetValue((from, to), out var route)) return route;
        route = Compute(new GeoPoint(from.XKm, from.YKm), new GeoPoint(to.XKm, to.YKm));
        _routes[(from, to)] = route;
        return route;
    }

    // ------------------------------------------------------------------ route

    private CartRoute Compute(GeoPoint from, GeoPoint to)
    {
        if (_terrain is null) return CartRoute.Straight(from, to, _cart);

        // Deux marchés voisins : une seule ligne droite, qui paie sa montée et son
        // passage, ou ne passe pas.
        double direct = RoadLegKm(from, to);
        if ((to - from).Norm <= _terrain.CellSizeKm * 1.5)
            return double.IsInfinity(direct)
                ? CartRoute.Impassable(from, to)
                : Assemble([from, to], [RoadSegment(direct)], [false]);

        // Départ et arrivée se raccordent au nœud le plus proche qu'on atteint sans
        // franchir de fleuve hors d'un passage.
        var (source, startKm) = Connect(from, toNode: true);
        var (target, endKm) = Connect(to, toNode: false);
        if (source < 0 || target < 0) return CartRoute.Impassable(from, to);

        var (cost, parent) = FromNode(source);
        if (double.IsInfinity(cost[target])) return CartRoute.Impassable(from, to);

        var nodes = new List<int>();
        for (int n = target; n != -1; n = parent[n]) nodes.Add(n);
        nodes.Reverse();

        var points = new List<GeoPoint> { from, NodePoint(source) };
        var segments = new List<(double Cost, double Days)> { RoadSegment(startKm) };
        var water = new List<bool> { false };
        for (int i = 1; i < nodes.Count; i++)
            AppendEdge(nodes[i - 1], nodes[i], points, segments, water);
        points.Add(to);
        segments.Add(RoadSegment(endKm));
        water.Add(false);
        return Assemble(points, segments, water);
    }

    private static CartRoute Assemble(List<GeoPoint> points, List<(double Cost, double Days)> segments, List<bool> water)
    {
        var cost = new List<double> { 0 };
        var days = new List<double> { 0 };
        foreach (var (c, d) in segments)
        {
            cost.Add(cost[^1] + c);
            days.Add(days[^1] + d);
        }
        return new CartRoute { Points = points, Cost = cost, Days = days, ByWater = water };
    }

    private (double Cost, double Days) RoadSegment(double roadKm)
        => (_cart.CostPerLoadKm * roadKm, roadKm / _cart.SpeedKmPerTick);

    /// <summary>Ajoute à la route le passage d'un nœud du graphe à un autre, en suivant le cours sur l'eau.</summary>
    private void AppendEdge(int u, int v, List<GeoPoint> points, List<(double, double)> segments, List<bool> water)
    {
        int grid = GridCount;
        if (u < grid && v < grid)
        {
            points.Add(NodePoint(v));
            segments.Add(RoadSegment(StepKm(u, v)));
            water.Add(false);
        }
        else if (u < grid)
        {
            // Au port : la route jusqu'au quai, puis le chargement sur le bateau.
            var port = _ports[v - grid];
            var (c, d) = RoadSegment((port.Port.Point - NodePoint(u)).Norm);
            points.Add(port.Port.Point);
            segments.Add((c + _water.CostPerLoad, d));
            water.Add(false);
        }
        else if (v < grid)
        {
            // Le déchargement, puis la route depuis le quai.
            var port = _ports[u - grid];
            var (c, d) = RoadSegment((NodePoint(v) - port.Port.Point).Norm);
            points.Add(NodePoint(v));
            segments.Add((c + _water.CostPerLoad, d));
            water.Add(false);
        }
        else
        {
            // Le fleuve, de port en port, en suivant son cours.
            var a = _ports[u - grid];
            var b = _ports[v - grid];
            double speed = b.Port.ChainageKm > a.Port.ChainageKm ? _water.DownstreamKmPerTick : _water.UpstreamKmPerTick;
            double previous = a.Port.ChainageKm;
            var vertices = a.River.VerticesBetween(a.Port.ChainageKm, b.Port.ChainageKm).ToList();
            vertices.Add((b.Port.Point, b.Port.ChainageKm));
            foreach (var (point, chainage) in vertices)
            {
                double km = Math.Abs(chainage - previous);
                points.Add(point);
                segments.Add((_water.CostPerLoadKm * km, km / speed));
                water.Add(true);
                previous = chainage;
            }
        }
    }

    // ------------------------------------------------------------- le graphe

    private int GridCount => _terrain!.Columns * _terrain.Rows;

    /// <summary>Route la moins chère depuis un nœud vers tous les autres, gardée pour les routes suivantes.</summary>
    private (double[] Cost, int[] Parent) FromNode(int source)
    {
        if (_fromNode.TryGetValue(source, out var known)) return known;

        int count = GridCount + _ports.Count;
        var cost = new double[count];
        var parent = new int[count];
        Array.Fill(cost, double.PositiveInfinity);
        Array.Fill(parent, -1);
        cost[source] = 0;

        // File de priorité sur (coût, nœud) : à coût égal, le plus petit indice sort
        // le premier, ce qui rend la route indépendante de la file elle-même.
        var queue = new PriorityQueue<int, (double, int)>();
        queue.Enqueue(source, (0, source));
        while (queue.TryDequeue(out int node, out var priority))
        {
            if (priority.Item1 > cost[node]) continue;
            foreach (var (next, edgeCost) in Neighbours(node))
            {
                double candidate = cost[node] + edgeCost;
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

    /// <summary>Les voisins d'un nœud, et ce qu'il coûte d'y aller.</summary>
    private IEnumerable<(int Node, double Cost)> Neighbours(int node)
    {
        int grid = GridCount;
        if (node < grid)
        {
            int columns = _terrain!.Columns;
            int x = node % columns, y = node / columns;
            for (int k = 0; k < Steps.Length; k++)
            {
                int nx = x + Steps[k].Dx, ny = y + Steps[k].Dy;
                if (nx < 0 || ny < 0 || nx >= columns || ny >= _terrain.Rows) continue;
                int next = ny * columns + nx;
                double km = StepKm(node, next, k);
                if (!double.IsInfinity(km)) yield return (next, _cart.CostPerLoadKm * km);
            }
            if (_portsAtNode.TryGetValue(node, out var ports))
                foreach (int p in ports)
                    yield return (grid + p, _cart.CostPerLoadKm * (_ports[p].Port.Point - NodePoint(node)).Norm + _water.CostPerLoad);
        }
        else
        {
            var (river, port, gridNode) = _ports[node - grid];
            yield return (gridNode, _cart.CostPerLoadKm * (port.Point - NodePoint(gridNode)).Norm + _water.CostPerLoad);
            // Les ports voisins sur le même fleuve, en amont et en aval : un bateau
            // descend ou remonte le cours de port en port.
            for (int k = 0; k < _ports.Count; k++)
            {
                if (k == node - grid || !ReferenceEquals(_ports[k].River, river)) continue;
                if (!Adjacent(river, port, _ports[k].Port)) continue;
                yield return (grid + k, _water.CostPerLoadKm * Math.Abs(_ports[k].Port.ChainageKm - port.ChainageKm));
            }
        }
    }

    /// <summary>Vrai si aucun autre port du fleuve n'est entre ces deux-là.</summary>
    private static bool Adjacent(River river, RiverPort a, RiverPort b)
    {
        double low = Math.Min(a.ChainageKm, b.ChainageKm), high = Math.Max(a.ChainageKm, b.ChainageKm);
        foreach (var other in river.Ports)
            if (!ReferenceEquals(other, a) && !ReferenceEquals(other, b)
                && other.ChainageKm > low && other.ChainageKm < high) return false;
        return true;
    }

    /// <summary>Kilomètres de route d'un pas de grille : longueur, montée, passage ; infini s'il ne passe pas.</summary>
    private double StepKm(int node, int next, int? direction = null)
    {
        var terrain = _terrain!;
        int columns = terrain.Columns;
        int k = direction ?? DirectionOf(node, next);
        double length = terrain.CellSizeKm * (k < 4 ? 1.0 : Math.Sqrt(2.0));
        double rise = terrain.ElevationAtCell(next % columns, next / columns) - terrain.ElevationAtCell(node % columns, node / columns);
        if (Math.Abs(rise) > _cart.MaxGradePercent / 100.0 * length * 1000.0) return double.PositiveInfinity;
        double crossing = StepCrossing(node, k);
        return length + _cart.ClimbKmPerMetre * Math.Max(0, rise) + crossing;
    }

    private int DirectionOf(int node, int next)
    {
        int columns = _terrain!.Columns;
        int dx = next % columns - node % columns, dy = next / columns - node / columns;
        for (int k = 0; k < Steps.Length; k++)
            if (Steps[k].Dx == dx && Steps[k].Dy == dy) return k;
        throw new InvalidOperationException("Deux nœuds non voisins.");
    }

    private double StepCrossing(int node, int k)
    {
        var terrain = _terrain!;
        if (terrain.Rivers.Count == 0) return 0;
        if (_stepCrossingKm is null)
        {
            int columns = terrain.Columns, count = GridCount;
            var table = new double[count * Steps.Length];
            for (int n = 0; n < count; n++)
            {
                int x = n % columns, y = n / columns;
                for (int d = 0; d < Steps.Length; d++)
                {
                    int nx = x + Steps[d].Dx, ny = y + Steps[d].Dy;
                    if (nx < 0 || ny < 0 || nx >= columns || ny >= terrain.Rows) continue;
                    table[n * Steps.Length + d] = CrossingKm(NodePoint(n), NodePoint(ny * columns + nx));
                }
            }
            _stepCrossingKm = table;
        }
        return _stepCrossingKm[node * Steps.Length + k];
    }

    /// <summary>
    /// Ce que coûte de franchir les fleuves que coupe le segment [a, b] : rien s'il n'en
    /// coupe aucun, le passage le moins cher à portée de chaque coupure, l'infini si une
    /// coupure n'a aucun passage à portée. La portée est d'une maille du relief.
    /// </summary>
    private double CrossingKm(GeoPoint a, GeoPoint b)
    {
        double reach = _terrain!.CellSizeKm;
        double total = 0;
        for (int r = 0; r < _terrain.Rivers.Count; r++)
            foreach (var point in _terrain.Rivers[r].Intersections(a, b))
            {
                double best = double.PositiveInfinity;
                foreach (var crossing in _crossings[r])
                    if ((crossing.Point - point).Norm <= reach + 1e-9)
                        best = Math.Min(best, crossing.Kind switch
                        {
                            CrossingKind.Bridge => _cart.BridgeKm,
                            CrossingKind.Ferry => _cart.FerryKm,
                            _ => _cart.FordKm,
                        });
                total += best;
            }
        return total;
    }

    /// <summary>Un raccord en ligne droite : longueur, montée et passage ; infini s'il coupe un fleuve hors d'un passage. Sans limite de pente : il est court.</summary>
    private double RoadLegKm(GeoPoint a, GeoPoint b)
    {
        double rise = _terrain!.ElevationAt(b) - _terrain.ElevationAt(a);
        return (b - a).Norm + _cart.ClimbKmPerMetre * Math.Max(0, rise) + CrossingKm(a, b);
    }

    /// <summary>
    /// Le nœud de la grille auquel un point se raccorde : le moins cher à atteindre des
    /// quatre nœuds de sa maille, sans franchir de fleuve hors d'un passage. -1 s'il n'y
    /// en a aucun.
    /// </summary>
    private (int Node, double RoadKm) Connect(GeoPoint point, bool toNode)
    {
        var terrain = _terrain!;
        int x0 = Math.Clamp((int)Math.Floor(point.XKm / terrain.CellSizeKm), 0, terrain.Columns - 1);
        int y0 = Math.Clamp((int)Math.Floor(point.YKm / terrain.CellSizeKm), 0, terrain.Rows - 1);
        int best = -1;
        double bestKm = double.PositiveInfinity;
        foreach (var (dx, dy) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
        {
            int x = Math.Min(x0 + dx, terrain.Columns - 1), y = Math.Min(y0 + dy, terrain.Rows - 1);
            int node = y * terrain.Columns + x;
            double km = toNode ? RoadLegKm(point, NodePoint(node)) : RoadLegKm(NodePoint(node), point);
            if (km < bestKm) { bestKm = km; best = node; }
        }
        return (best, bestKm);
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
