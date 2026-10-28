namespace RailTycoon.Sim.Economy;

/// <summary>
/// Un chargement en route sans train : une marchandise partie d'un marché vers un
/// autre, en charrette et parfois en bateau, qui avance chaque jour sur sa route et
/// n'arrive qu'au bout. Il n'appartient à personne ; un train peut le capter quand il
/// passe dans le rayon de sa gare.
/// </summary>
public sealed class CartShipment
{
    public required string CargoId { get; init; }
    public required Market From { get; init; }
    public required Market To { get; init; }

    /// <summary>La route qu'il suit : le terrain et les fleuves, ou la ligne droite sur une carte sans relief.</summary>
    public required CartRoute Route { get; init; }

    /// <summary>Prix d'un chargement au marché de départ, au moment du départ.</summary>
    public required double SourcePrice { get; init; }

    /// <summary>Chargements encore sur la charrette.</summary>
    public double Quantity;

    /// <summary>Jours de voyage déjà faits.</summary>
    public double TravelledDays;

    /// <summary>Vrai tant qu'il est sur un bateau.</summary>
    public bool OnWater
    {
        get
        {
            for (int i = 0; i < Route.ByWater.Count; i++)
                if (TravelledDays < Route.Days[i + 1] - 1e-12) return Route.ByWater[i];
            return false;
        }
    }

    /// <summary>Position actuelle sur la carte, le long de sa route.</summary>
    public (double XKm, double YKm) Position
    {
        get
        {
            var point = Route.PointAt(TravelledDays);
            return (point.XKm, point.YKm);
        }
    }

    /// <summary>
    /// Ce que coûte un chargement là où il se trouve : son prix de départ, plus le
    /// voyage déjà fait. C'est le prix qu'un train paie pour le capter.
    /// </summary>
    public double PriceHere(CartageDef cfg) => SourcePrice + cfg.CostPerLoad + Route.CostAt(TravelledDays);
}

/// <summary>Ce que le charroi a fait depuis le début de la partie, par marchandise. Télémétrie.</summary>
public sealed class CartageState
{
    /// <summary>Charrettes en route, dans l'ordre de leur départ.</summary>
    public List<CartShipment> Shipments { get; } = new();

    /// <summary>Les routes des charrettes, calculées une fois par couple de marchés.</summary>
    internal CartRouter? Router;

    // Jamais énumérés : les lectures passent par une marchandise.
    private readonly Dictionary<string, double> _dispatched = new();
    private readonly Dictionary<string, double> _delivered = new();
    private readonly Dictionary<string, double> _captured = new();

    public double Dispatched(string cargoId) => _dispatched.GetValueOrDefault(cargoId);
    public double Delivered(string cargoId) => _delivered.GetValueOrDefault(cargoId);
    public double Captured(string cargoId) => _captured.GetValueOrDefault(cargoId);

    private readonly Dictionary<string, double> _byWater = new();

    /// <summary>Chargements partis sur une route qui emprunte un fleuve.</summary>
    public double DispatchedByWater(string cargoId) => _byWater.GetValueOrDefault(cargoId);

    internal void AddDispatched(string cargoId, double qty, bool byWater)
    {
        _dispatched[cargoId] = Dispatched(cargoId) + qty;
        if (byWater) _byWater[cargoId] = DispatchedByWater(cargoId) + qty;
    }
    internal void AddDelivered(string cargoId, double qty) => _delivered[cargoId] = Delivered(cargoId) + qty;
    public void AddCaptured(string cargoId, double qty) => _captured[cargoId] = Captured(cargoId) + qty;

    /// <summary>Chargements en route, toutes marchandises.</summary>
    public double InTransit
    {
        get
        {
            double total = 0;
            foreach (var shipment in Shipments) total += shipment.Quantity;
            return total;
        }
    }
}

/// <summary>
/// Le charroi (phase 0c) : ce qui passe sans train, par charrettes, entre deux marchés
/// de la carte — dans une même ville ou d'une ville à l'autre.
/// <para>
/// Tout peut passer par charrette, sans limite de quantité ; c'est seulement plus lent
/// et plus cher qu'un train. Chaque jour, les charrettes en route avancent de
/// <see cref="CartageDef.SpeedKmPerTick"/> et celles qui arrivent déposent leur
/// chargement. Puis de nouvelles partent, une à une : chacune du marché le moins cher
/// vers le marché acheteur le plus cher, tant que l'écart de prix paie le trajet. Le
/// prix d'arrivée compte ce qui roule déjà vers ce marché — sans quoi, la marchandise
/// mettant des jours à arriver, chaque jour enverrait de quoi combler la même pénurie.
/// </para>
/// <para>
/// Le charroi ne coûte rien à la compagnie et ne crée ni ne détruit de marchandise : il
/// la déplace d'un stock à l'autre en passant par la route, et le bilan matière compte
/// ce qui est en route. Il ne livre qu'à un marché qui consomme la marchandise, et ne
/// prend à un marché que ce qu'il cède à un train (<see cref="Market.SellableStock"/>).
/// Il ne fixe aucun prix : le solveur économique les recalcule en phase 4 ; il
/// interroge seulement le modèle de prix sur l'état du moment pour choisir où aller.
/// </para>
/// </summary>
public static class Cartage
{
    /// <summary>
    /// Marché fictif pour interroger le modèle de prix sur un stock qui compte ce qui est
    /// en route. Il n'appartient pas au monde et n'entre dans aucun bilan.
    /// </summary>
    [ThreadStatic] private static Market? _probe;

    public static void Step(WorldState world)
    {
        var cfg = world.Def.Economy.Cartage;
        var state = world.Cartage;
        state.Router ??= new CartRouter(world);

        Advance(state);
        Dispatch(world, state, cfg);
    }

    private static void Advance(CartageState state)
    {
        var stillOnTheRoad = new List<CartShipment>(state.Shipments.Count);
        foreach (var shipment in state.Shipments)
        {
            if (shipment.Quantity <= 1e-12) continue;
            shipment.TravelledDays = Math.Min(shipment.Route.TotalDays, shipment.TravelledDays + 1);
            if (shipment.TravelledDays >= shipment.Route.TotalDays - 1e-9)
            {
                shipment.To.CartIn(shipment.Quantity);
                state.AddDelivered(shipment.CargoId, shipment.Quantity);
                continue;
            }
            stillOnTheRoad.Add(shipment);
        }
        state.Shipments.Clear();
        state.Shipments.AddRange(stillOnTheRoad);
    }

    private static void Dispatch(WorldState world, CartageState state, CartageDef cfg)
    {
        double horizon = world.Def.PriceModel.CoverageHorizonTicks;
        double retained = world.Def.Economy.RetainedCoverage;

        // Ce qui roule déjà vers chaque marché. Indexé par référence, jamais énuméré.
        var incoming = new Dictionary<Market, double>(ReferenceEqualityComparer.Instance);
        foreach (var shipment in state.Shipments)
            incoming[shipment.To] = incoming.GetValueOrDefault(shipment.To) + shipment.Quantity;

        foreach (string cargoId in world.CargoOrder)
        {
            var cargo = world.Cargo(cargoId);
            var markets = new List<Market>();
            foreach (var city in world.Cities)
                foreach (var market in world.MarketsOf(city))
                    if (market.CargoId == cargoId) markets.Add(market);
            if (markets.Count < 2) continue;

            for (int cart = 0; cart < cfg.MaxCartsPerTick; cart++)
            {
                // Le meilleur départ du moment : l'écart de prix, moins le trajet.
                // Parcours dans l'ordre de la carte, comparaison stricte : à gain égal,
                // le premier couple gagne.
                Market? from = null, to = null;
                double bestGain = 0, fromPrice = 0;
                foreach (var source in markets)
                {
                    if (source.SellableStock(horizon, retained) <= 1e-9) continue;
                    double sourcePrice = world.PriceModel.PriceFor(cargo, source);
                    foreach (var buyer in markets)
                    {
                        if (ReferenceEquals(buyer, source)) continue;
                        if (buyer.BaseDemandRate + buyer.IndustryDemandRate <= 0) continue;
                        double gain = PriceWithIncoming(world, cargo, buyer, incoming.GetValueOrDefault(buyer))
                                    - sourcePrice - Cost(world, source, buyer);
                        if (gain > bestGain) { bestGain = gain; from = source; to = buyer; fromPrice = sourcePrice; }
                    }
                }
                if (from is null || to is null) break;

                double qty = Math.Min(cfg.LoadsPerCart, from.SellableStock(horizon, retained));
                from.CartOut(qty);
                incoming[to] = incoming.GetValueOrDefault(to) + qty;
                var route = state.Router!.Route(from, to);
                state.Shipments.Add(new CartShipment
                {
                    CargoId = cargoId, From = from, To = to,
                    Route = route, SourcePrice = fromPrice, Quantity = qty,
                });
                state.AddDispatched(cargoId, qty, route.UsesWater);
            }
        }
    }

    private static double PriceWithIncoming(WorldState world, CargoDef cargo, Market buyer, double incoming)
    {
        if (incoming <= 0) return world.PriceModel.PriceFor(cargo, buyer);
        var probe = _probe ??= new Market { CargoId = "probe", CityId = "probe" };
        probe.BaseDemandRate = buyer.BaseDemandRate;
        probe.IndustryDemandRate = buyer.IndustryDemandRate;
        probe.StorageCapacity = buyer.StorageCapacity;
        probe.Stock = buyer.Stock + incoming;
        return world.PriceModel.PriceFor(cargo, probe);
    }

    /// <summary>La route d'une charrette entre deux marchés, qui suit le terrain.</summary>
    public static CartRoute Route(WorldState world, Market from, Market to)
    {
        var state = world.Cartage;
        state.Router ??= new CartRouter(world);
        return state.Router.Route(from, to);
    }

    /// <summary>Le routeur du monde : routes, points de passage, ports.</summary>
    public static CartRouter Router(WorldState world)
        => world.Cartage.Router ??= new CartRouter(world);

    /// <summary>
    /// Ce que coûte un chargement entre deux marchés sans train, sur sa route : le
    /// chargement au départ, plus la route et le fleuve. Infini si rien ne passe — une
    /// pente trop raide partout, un fleuve sans passage.
    /// </summary>
    public static double Cost(WorldState world, Market from, Market to)
        => world.Def.Economy.Cartage.CostPerLoad + Route(world, from, to).TotalCost;

    /// <summary>Refuse au chargement un transport par eau aux réglages absurdes.</summary>
    public static void Validate(WaterwaysDef cfg)
    {
        if (cfg.DownstreamKmPerTick <= 0 || cfg.UpstreamKmPerTick <= 0)
            throw new InvalidDataException("economy.waterways : un bateau qui n'avance pas n'arrive jamais.");
        if (cfg.CostPerLoad < 0 || cfg.CostPerLoadKm < 0)
            throw new InvalidDataException("economy.waterways : un coût ne peut pas être négatif.");
    }

    /// <summary>Refuse au chargement un charroi aux réglages absurdes.</summary>
    public static void Validate(CartageDef cfg)
    {
        if (cfg.SpeedKmPerTick <= 0)
            throw new InvalidDataException("economy.cartage.speedKmPerTick doit être strictement positif : une charrette qui n'avance pas n'arrive jamais.");
        if (cfg.CostPerLoad < 0 || cfg.CostPerLoadKm < 0)
            throw new InvalidDataException("economy.cartage : un coût de charroi ne peut pas être négatif.");
        if (cfg.LoadsPerCart <= 0)
            throw new InvalidDataException("economy.cartage.loadsPerCart doit être strictement positif.");
        if (cfg.MaxCartsPerTick < 0)
            throw new InvalidDataException("economy.cartage.maxCartsPerTick ne peut pas être négatif.");
        if (cfg.ClimbKmPerMetre < 0 || cfg.FordKm < 0 || cfg.BridgeKm < 0 || cfg.FerryKm < 0)
            throw new InvalidDataException("economy.cartage : une montée ou un passage ne peut pas raccourcir la route.");
        if (cfg.MaxGradePercent <= 0)
            throw new InvalidDataException("economy.cartage.maxGradePercent doit être strictement positif.");
    }
}
