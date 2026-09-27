namespace RailTycoon.Sim.Network;

/// <summary>
/// Un nœud du graphe de voies : une gare, une bifurcation, ou un simple point de
/// raccordement.
/// <para>
/// <see cref="PassingTracks"/> est là pour la signalisation et pour elle seule : à
/// une voie, deux trains ne peuvent pas se croiser ici, et c'est de cette
/// information que naissent les interblocages.
/// </para>
/// </summary>
public sealed class TrackNode
{
    public required int Index { get; init; }
    public required string Id { get; init; }

    /// <summary>Ville desservie, ou <c>null</c> si le nœud n'est pas une gare.</summary>
    public required string? CityId { get; init; }

    public required GeoPoint Position { get; init; }
    public required double ElevationM { get; init; }
    public required int PassingTracks { get; init; }

    /// <summary>
    /// Arêtes incidentes, dans l'ordre de déclaration du scénario. L'ordre est
    /// stable et fait partie du contrat : un parcours de graphe qui dépendrait d'un
    /// ordre de dictionnaire donnerait des itinéraires différents d'une exécution à
    /// l'autre.
    /// </summary>
    public List<int> EdgeIndices { get; } = new();

    public bool IsStation => CityId is not null;
}

/// <summary>
/// Une arête du graphe : un tronçon de voie posé sur le relief, avec sa longueur
/// réelle, son profil en long et son devis de construction.
/// <para>
/// L'arête est le grain de réservation du module de circulation : c'est elle qu'un
/// train occupe, et <see cref="TrackCount"/> dit combien peuvent l'occuper à la
/// fois. Le sens de construction va de <see cref="FromNodeIndex"/> vers
/// <see cref="ToNodeIndex"/> ; tout ce qui dépend du sens de marche est exposé en
/// fonction d'un booléen <c>forward</c> plutôt que dupliqué.
/// </para>
/// </summary>
public sealed class TrackEdge
{
    public required int Index { get; init; }
    public required string Id { get; init; }
    public required int FromNodeIndex { get; init; }
    public required int ToNodeIndex { get; init; }
    public required int TrackCount { get; init; }
    public required ConstructionEstimate Construction { get; init; }

    /// <summary>Points de passage du tracé, extrémités incluses.</summary>
    public required IReadOnlyList<GeoPoint> ControlPoints { get; init; }

    /// <summary>Longueur réelle de la voie, relief compris.</summary>
    public double LengthKm => Construction.LengthKm;

    public TrackProfile Profile => Construction.Profile;

    public int OtherEnd(int nodeIndex) => nodeIndex == FromNodeIndex ? ToNodeIndex : FromNodeIndex;

    public bool IsForwardFrom(int nodeIndex) => nodeIndex == FromNodeIndex;

    /// <summary>
    /// Ce que le relief ajoute au coût kilométrique d'exploitation dans un sens de
    /// marche : 1 sur le plat, davantage en rampe.
    /// <para>
    /// Le dénivelé positif est converti en kilomètres de plat équivalents. C'est le
    /// seul canal par lequel le relief atteint l'économie, et il est volontairement
    /// scalaire : le transport n'a pas à connaître le profil pour en payer le prix.
    /// </para>
    /// </summary>
    public double CostFactor(bool forward, double climbEquivalentKm)
    {
        if (LengthKm <= 0) return 1.0;
        return 1.0 + Profile.ClimbInDirection(forward) * climbEquivalentKm / LengthKm;
    }
}

/// <summary>Le franchissement d'une arête dans un sens donné.</summary>
public sealed class RouteLeg
{
    public required TrackEdge Edge { get; init; }

    /// <summary>Vrai si le train parcourt l'arête dans son sens de construction.</summary>
    public required bool Forward { get; init; }

    public required int FromNodeIndex { get; init; }
    public required int ToNodeIndex { get; init; }

    public double LengthKm => Edge.LengthKm;

    public double RulingGradePercent => Edge.Profile.RulingGradePercent(Forward);
}

/// <summary>
/// Un itinéraire : la suite ordonnée des arêtes franchies, avec le sens de
/// parcours de chacune.
/// <para>
/// C'est la forme sous laquelle la circulation réservera un parcours : une liste
/// de franchissements orientés, chacun désignant une arête, un sens et deux nœuds.
/// Rien d'autre n'est nécessaire pour poser des cantons dessus.
/// </para>
/// </summary>
public sealed class TrackRoute
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<RouteLeg> Legs { get; init; }

    /// <summary>Nœuds traversés, origine incluse. Compte toujours <c>Legs + 1</c> éléments.</summary>
    public required IReadOnlyList<TrackNode> Nodes { get; init; }

    public double LengthKm
    {
        get
        {
            double total = 0;
            foreach (var leg in Legs) total += leg.LengthKm;
            return total;
        }
    }

    /// <summary>Gares desservies, dans l'ordre de parcours.</summary>
    public List<TrackNode> Stations()
    {
        var stations = new List<TrackNode>();
        foreach (var node in Nodes)
            if (node.IsStation) stations.Add(node);
        return stations;
    }
}

/// <summary>
/// Le graphe de voies : des nœuds, des arêtes, et de quoi calculer un itinéraire.
/// <para>
/// Nœuds et arêtes sont indexés par des entiers contigus attribués dans l'ordre de
/// déclaration du scénario. C'est délibéré : un module de circulation pourra
/// allouer ses propres tableaux parallèles (occupation, réservations, cantons) sans
/// dictionnaire, et l'ordre de parcours du graphe sera stable par construction.
/// </para>
/// </summary>
public sealed class TrackGraph
{
    private readonly Dictionary<string, int> _nodeIndexById = new();
    private readonly Dictionary<string, int> _edgeIndexById = new();

    public List<TrackNode> Nodes { get; } = new();
    public List<TrackEdge> Edges { get; } = new();

    internal void Add(TrackNode node)
    {
        if (_nodeIndexById.ContainsKey(node.Id))
            throw new InvalidDataException($"Nœud de voie en double : « {node.Id} ».");
        _nodeIndexById[node.Id] = node.Index;
        Nodes.Add(node);
    }

    internal void Add(TrackEdge edge)
    {
        if (_edgeIndexById.ContainsKey(edge.Id))
            throw new InvalidDataException($"Tronçon de voie en double : « {edge.Id} ».");
        _edgeIndexById[edge.Id] = edge.Index;
        Edges.Add(edge);
        Nodes[edge.FromNodeIndex].EdgeIndices.Add(edge.Index);
        Nodes[edge.ToNodeIndex].EdgeIndices.Add(edge.Index);
    }

    public bool HasNode(string id) => _nodeIndexById.ContainsKey(id);

    public TrackNode Node(string id)
        => _nodeIndexById.TryGetValue(id, out int index)
            ? Nodes[index]
            : throw new InvalidDataException($"Nœud de voie inconnu : « {id} ».");

    public TrackEdge Edge(string id)
        => _edgeIndexById.TryGetValue(id, out int index)
            ? Edges[index]
            : throw new InvalidDataException($"Tronçon de voie inconnu : « {id} ».");

    /// <summary>
    /// Plus court chemin en longueur réelle de voie, par Dijkstra.
    /// <para>
    /// Les égalités sont départagées par l'indice d'arête, jamais laissées au hasard
    /// d'un tas ou d'un dictionnaire. Deux tronçons de longueur identique existent
    /// dès qu'une carte est un peu régulière, et un itinéraire qui changerait d'une
    /// exécution à l'autre rendrait toute la partie non reproductible.
    /// </para>
    /// </summary>
    public bool TryFindPath(int fromNodeIndex, int toNodeIndex, out List<RouteLeg> legs)
    {
        legs = new List<RouteLeg>();
        int n = Nodes.Count;
        var distance = new double[n];
        var viaEdge = new int[n];
        var settled = new bool[n];
        for (int i = 0; i < n; i++)
        {
            distance[i] = double.PositiveInfinity;
            viaEdge[i] = -1;
        }
        distance[fromNodeIndex] = 0;

        // O(n²) assumé : on compare les nœuds dans l'ordre de leur indice, donc le
        // résultat ne dépend d'aucune structure de données. Une carte de jeu compte
        // des centaines de nœuds, pas des millions.
        for (int step = 0; step < n; step++)
        {
            int current = -1;
            double best = double.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                if (settled[i] || double.IsPositiveInfinity(distance[i])) continue;
                if (distance[i] < best - 1e-12)
                {
                    best = distance[i];
                    current = i;
                }
            }
            if (current < 0) break;
            if (current == toNodeIndex) break;
            settled[current] = true;

            foreach (int edgeIndex in Nodes[current].EdgeIndices)
            {
                var edge = Edges[edgeIndex];
                int other = edge.OtherEnd(current);
                if (settled[other]) continue;
                double candidate = distance[current] + edge.LengthKm;
                bool better = candidate < distance[other] - 1e-12;
                bool tie = Math.Abs(candidate - distance[other]) <= 1e-12 && viaEdge[other] > edgeIndex;
                if (better || tie)
                {
                    distance[other] = candidate;
                    viaEdge[other] = edgeIndex;
                }
            }
        }

        if (fromNodeIndex == toNodeIndex) return true;
        if (viaEdge[toNodeIndex] < 0) return false;

        var reversed = new List<RouteLeg>();
        int cursor = toNodeIndex;
        while (cursor != fromNodeIndex)
        {
            var edge = Edges[viaEdge[cursor]];
            int previous = edge.OtherEnd(cursor);
            reversed.Add(new RouteLeg
            {
                Edge = edge,
                Forward = edge.IsForwardFrom(previous),
                FromNodeIndex = previous,
                ToNodeIndex = cursor,
            });
            cursor = previous;
        }
        reversed.Reverse();
        legs = reversed;
        return true;
    }
}
