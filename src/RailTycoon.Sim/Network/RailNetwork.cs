using RailTycoon.Sim.Economy;
using RailTycoon.Sim.Transport;

namespace RailTycoon.Sim.Network;

/// <summary>
/// La façade du module réseau. C'est la seule porte d'entrée : ni le transport, ni
/// l'économie, ni plus tard la circulation ne parlent au relief directement.
/// <para>
/// Elle répond à trois questions et pas une de plus :
/// </para>
/// <list type="number">
/// <item><description><b>Combien coûterait ce tracé ?</b> — <see cref="Survey"/>,
/// qui chiffre un tracé <em>candidat</em> sans rien construire. C'est l'outil de
/// décision : contourner, franchir ou percer se compare ici.</description></item>
/// <item><description><b>Par où passe-t-on ?</b> — <see cref="TryConnect"/> et
/// <see cref="Routes"/>, qui rendent un itinéraire sous forme d'arêtes orientées.
/// </description></item>
/// <item><description><b>Que coûte l'exploitation de ce tronçon ?</b> —
/// <see cref="CostFactor"/>, qui résume le relief en un scalaire. Le transport
/// multiplie son coût kilométrique par ce facteur et n'en sait pas plus.
/// </description></item>
/// </list>
/// <para>
/// Ce que la façade garantit au futur module <c>dispatch</c> est documenté dans
/// docs/ARCHITECTURE.md, section « Le réseau ferré ».
/// </para>
/// </summary>
public interface IRailNetwork
{
    string Name { get; }

    HeightField Terrain { get; }

    /// <summary>Le graphe de voies : nœuds et arêtes indexés de façon stable.</summary>
    TrackGraph Graph { get; }

    /// <summary>Itinéraires déclarés par le scénario, dans l'ordre de déclaration.</summary>
    IReadOnlyList<TrackRoute> Routes { get; }

    TrackRoute? RouteById(string id);

    /// <summary>
    /// Itinéraire le plus court entre deux nœuds existants. Rend <c>false</c> si le
    /// graphe ne les relie pas : un réseau en deux morceaux est un cas normal.
    /// </summary>
    bool TryConnect(string fromNodeId, string toNodeId, out TrackRoute route);

    /// <summary>
    /// Chiffre un tracé candidat sur le relief courant, sans le construire. Lève
    /// <see cref="InvalidDataException"/> si le tracé est géométriquement
    /// impossible — virage plus serré que le rayon minimal, ou dénivelé
    /// inatteignable à la pente maximale.
    /// </summary>
    ConstructionEstimate Survey(IReadOnlyList<GeoPoint> controlPoints, int trackCount);

    /// <summary>Coût de construction de tout ce qui est posé sur la carte.</summary>
    double BuiltCost { get; }

    /// <summary>
    /// Kilomètres de voie posée : longueur réelle de chaque tronçon, multipliée
    /// par son nombre de voies.
    /// </summary>
    double TrackKm { get; }

    /// <summary>
    /// Entretien du réseau pour un tick : <see cref="TrackKm"/> au tarif
    /// <c>costs.upkeepPerTrackKmPerTick</c>. Dû que les trains roulent ou non.
    /// </summary>
    double UpkeepPerTick { get; }

    /// <summary>
    /// Kilomètres de plat équivalents à un mètre gagné en altitude. C'est la
    /// conversion qui permet de résumer un profil en un coût kilométrique.
    /// </summary>
    double ClimbEquivalentKm { get; }

    /// <summary>
    /// Ce que le relief ajoute au coût kilométrique d'un train sur une arête, dans
    /// un sens de marche. 1 sur le plat.
    /// </summary>
    double CostFactor(TrackEdge edge, bool forward);
}

/// <summary>
/// Réseau posé sur une carte de hauteurs. Tout est calculé une fois, au
/// chargement : le relief ne change pas en cours de partie, donc un devis non plus.
/// </summary>
public sealed class TerrainRailNetwork : IRailNetwork
{
    private readonly RailNetworkDef _def;
    private readonly List<TrackRoute> _routes = new();
    private readonly Dictionary<string, TrackRoute> _routesById = new();

    public string Name => "terrain";
    public HeightField Terrain { get; }
    public TrackGraph Graph { get; } = new();
    public IReadOnlyList<TrackRoute> Routes => _routes;
    public double BuiltCost { get; private set; }
    public double TrackKm { get; private set; }
    public double UpkeepPerTick => TrackKm * _def.Costs.UpkeepPerTrackKmPerTick;
    public double ClimbEquivalentKm => _def.Traction.ClimbEquivalentKm;

    internal TerrainRailNetwork(RailNetworkDef def, HeightField terrain)
    {
        _def = def;
        Terrain = terrain;
    }

    public TrackRoute? RouteById(string id) => _routesById.GetValueOrDefault(id);

    public bool TryConnect(string fromNodeId, string toNodeId, out TrackRoute route)
    {
        var from = Graph.Node(fromNodeId);
        var to = Graph.Node(toNodeId);
        route = null!;
        if (!Graph.TryFindPath(from.Index, to.Index, out var legs)) return false;
        route = Assemble($"{fromNodeId}→{toNodeId}", $"{from.Id} → {to.Id}", from.Index, legs);
        return true;
    }

    public ConstructionEstimate Survey(IReadOnlyList<GeoPoint> controlPoints, int trackCount)
        => Surveyor.Survey(Terrain, controlPoints, trackCount, _def.Alignment, _def.Costs, "étude de tracé");

    public double CostFactor(TrackEdge edge, bool forward)
        => edge.CostFactor(forward, _def.Traction.ClimbEquivalentKm);

    internal TrackRoute Assemble(string id, string name, int originNodeIndex, List<RouteLeg> legs)
    {
        var nodes = new List<TrackNode> { Graph.Nodes[originNodeIndex] };
        foreach (var leg in legs) nodes.Add(Graph.Nodes[leg.ToNodeIndex]);
        return new TrackRoute { Id = id, Name = name, Legs = legs, Nodes = nodes };
    }

    internal void AddRoute(TrackRoute route)
    {
        if (_routesById.ContainsKey(route.Id))
            throw new InvalidDataException($"Itinéraire en double : « {route.Id} ».");
        _routesById[route.Id] = route;
        _routes.Add(route);
    }

    internal void AddBuiltCost(double cost) => BuiltCost += cost;

    internal void AddTrack(double km) => TrackKm += km;
}

/// <summary>
/// Construit le réseau depuis les données du scénario et en dérive les lignes que
/// le transport utilisera.
/// <para>
/// <b>Voie de compatibilité.</b> Un scénario sans relief — c'est le cas de
/// <c>data/heartland.json</c> — ne passe pas ici : ses lignes gardent les distances
/// saisies à la main dans <c>scenario.lines</c>, et le réseau vaut <c>null</c>.
/// C'est ce qui permet au module réseau d'arriver sans invalider les traces de
/// régression existantes. Un scénario qui déclare des nœuds bascule entièrement
/// dans le graphe, et ses lignes sont calculées sur le relief.
/// </para>
/// </summary>
public static class NetworkBuilder
{
    /// <summary>
    /// Construit le réseau du scénario et ajoute ses lignes à la liste fournie.
    /// Rend <c>null</c> si le scénario ne déclare pas de réseau.
    /// </summary>
    public static IRailNetwork? Attach(ScenarioDef scenario, List<RailLine> lines, Func<string, bool> cityExists)
    {
        var def = scenario.Network;
        if (def.Nodes.Count == 0)
        {
            if (def.Edges.Count > 0 || def.Routes.Count > 0)
                throw new InvalidDataException(
                    "Le réseau déclare des tronçons ou des itinéraires sans aucun nœud de voie.");
            return null;
        }
        if (def.Terrain is null)
            throw new InvalidDataException("Le réseau déclare des nœuds sans relief : il manque « terrain ».");

        var terrain = TerrainFactory.Build(def.Terrain, scenario.Seed);
        var network = new TerrainRailNetwork(def, terrain);

        BuildNodes(def, terrain, network, cityExists);
        BuildEdges(def, terrain, network);
        BuildRoutes(def, network);

        foreach (var route in network.Routes)
            lines.Add(ToRailLine(route, network));

        return network;
    }

    private static void BuildNodes(
        RailNetworkDef def, HeightField terrain, TerrainRailNetwork network, Func<string, bool> cityExists)
    {
        for (int i = 0; i < def.Nodes.Count; i++)
        {
            var nodeDef = def.Nodes[i];
            if (string.IsNullOrWhiteSpace(nodeDef.Id))
                throw new InvalidDataException("Un nœud de voie n'a pas d'identifiant.");
            var position = new GeoPoint(nodeDef.XKm, nodeDef.YKm);
            if (!terrain.Contains(position))
                throw new InvalidDataException(
                    $"Le nœud « {nodeDef.Id} » est hors de la carte de relief.");
            if (nodeDef.PassingTracks < 1)
                throw new InvalidDataException(
                    $"Le nœud « {nodeDef.Id} » doit avoir au moins une voie de stationnement.");

            string? cityId = string.IsNullOrWhiteSpace(nodeDef.City) ? null : nodeDef.City;
            if (cityId is not null && !cityExists(cityId))
                throw new InvalidDataException(
                    $"Le nœud « {nodeDef.Id} » dessert la ville inconnue « {cityId} ».");

            network.Graph.Add(new TrackNode
            {
                Index = i,
                Id = nodeDef.Id,
                CityId = cityId,
                Position = position,
                ElevationM = terrain.ElevationAt(position),
                PassingTracks = nodeDef.PassingTracks,
            });
        }
    }

    private static void BuildEdges(RailNetworkDef def, HeightField terrain, TerrainRailNetwork network)
    {
        for (int i = 0; i < def.Edges.Count; i++)
        {
            var edgeDef = def.Edges[i];
            string id = string.IsNullOrWhiteSpace(edgeDef.Id) ? $"{edgeDef.From}-{edgeDef.To}" : edgeDef.Id;
            var from = network.Graph.Node(edgeDef.From);
            var to = network.Graph.Node(edgeDef.To);
            if (from.Index == to.Index)
                throw new InvalidDataException($"Le tronçon « {id} » boucle sur le nœud « {from.Id} ».");
            if (edgeDef.TrackCount < 1)
                throw new InvalidDataException($"Le tronçon « {id} » doit avoir au moins une voie.");

            var controlPoints = new List<GeoPoint> { from.Position };
            foreach (var via in edgeDef.Via) controlPoints.Add(new GeoPoint(via.XKm, via.YKm));
            controlPoints.Add(to.Position);

            var estimate = Surveyor.Survey(
                terrain, controlPoints, edgeDef.TrackCount, def.Alignment, def.Costs, $"tronçon « {id} »");

            network.Graph.Add(new TrackEdge
            {
                Index = i,
                Id = id,
                FromNodeIndex = from.Index,
                ToNodeIndex = to.Index,
                TrackCount = edgeDef.TrackCount,
                Construction = estimate,
                ControlPoints = controlPoints,
            });
            network.AddBuiltCost(estimate.TotalCost);
            network.AddTrack(estimate.LengthKm * edgeDef.TrackCount);
        }
    }

    private static void BuildRoutes(RailNetworkDef def, TerrainRailNetwork network)
    {
        foreach (var routeDef in def.Routes)
        {
            if (routeDef.Nodes.Count < 2)
                throw new InvalidDataException(
                    $"L'itinéraire « {routeDef.Id} » doit passer par au moins deux nœuds.");

            var legs = new List<RouteLeg>();
            var origin = network.Graph.Node(routeDef.Nodes[0]);
            for (int i = 1; i < routeDef.Nodes.Count; i++)
            {
                var previous = network.Graph.Node(routeDef.Nodes[i - 1]);
                var next = network.Graph.Node(routeDef.Nodes[i]);
                if (!network.Graph.TryFindPath(previous.Index, next.Index, out var segment))
                    throw new InvalidDataException(
                        $"L'itinéraire « {routeDef.Id} » : aucune voie ne relie « {previous.Id} » à « {next.Id} ».");
                legs.AddRange(segment);
            }

            network.AddRoute(network.Assemble(
                routeDef.Id,
                string.IsNullOrWhiteSpace(routeDef.Name) ? routeDef.Id : routeDef.Name,
                origin.Index,
                legs));
        }
    }

    /// <summary>
    /// Projette un itinéraire en <see cref="RailLine"/> : la vue plate — une suite
    /// de gares et de distances — dont le transport se sert, et qui ne laisse rien
    /// filtrer du relief.
    /// </summary>
    private static RailLine ToRailLine(TrackRoute route, IRailNetwork network)
    {
        var stops = new List<RailStop>();
        var segments = new List<RailSegment>();

        if (!route.Nodes[0].IsStation || !route.Nodes[^1].IsStation)
            throw new InvalidDataException(
                $"L'itinéraire « {route.Id} » doit commencer et finir dans une gare : un train a besoin " +
                "d'un terminus où échanger.");

        double chainage = 0;
        var pending = new List<RouteLeg>();
        double pendingLength = 0;
        double pendingClimbForward = 0;
        double pendingClimbReverse = 0;

        void Flush(TrackNode node)
        {
            if (stops.Count > 0)
            {
                segments.Add(new RailSegment
                {
                    FromStopIndex = stops.Count - 1,
                    ToStopIndex = stops.Count,
                    LengthKm = pendingLength,
                    ForwardCostFactor = Factor(pendingLength, pendingClimbForward),
                    ReverseCostFactor = Factor(pendingLength, pendingClimbReverse),
                    Legs = pending.ToList(),
                });
            }
            stops.Add(new RailStop
            {
                CityId = node.CityId!,
                DistanceKm = chainage,
                NodeIndex = node.Index,
            });
            pending.Clear();
            pendingLength = 0;
            pendingClimbForward = 0;
            pendingClimbReverse = 0;
        }

        for (int i = 0; i < route.Nodes.Count; i++)
        {
            var node = route.Nodes[i];
            if (node.IsStation) Flush(node);
            if (i < route.Legs.Count)
            {
                var leg = route.Legs[i];
                pending.Add(leg);
                pendingLength += leg.LengthKm;
                pendingClimbForward += leg.Edge.Profile.ClimbInDirection(leg.Forward);
                pendingClimbReverse += leg.Edge.Profile.ClimbInDirection(!leg.Forward);
                chainage += leg.LengthKm;
            }
        }

        if (stops.Count < 2)
            throw new InvalidDataException(
                $"L'itinéraire « {route.Id} » ne dessert que {stops.Count} gare(s) : il en faut deux.");

        return new RailLine
        {
            Id = route.Id,
            Name = route.Name,
            Stops = stops,
            Route = route,
            Segments = segments,
        };

        double Factor(double lengthKm, double climbM)
            => lengthKm <= 0 ? 1.0 : 1.0 + climbM * network.ClimbEquivalentKm / lengthKm;
    }
}
