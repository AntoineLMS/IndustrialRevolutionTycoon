using RailTycoon.Sim.Core;
using RailTycoon.Sim.Economy;

namespace RailTycoon.Sim.Transport;

public sealed class Train
{
    public required string Id { get; init; }
    public required RailLine Line { get; init; }
    public required double Capacity { get; init; }
    public required double SpeedKmPerTick { get; init; }
    public required double CostPerKm { get; init; }

    /// <summary>Index du dernier arrêt atteint.</summary>
    public int StopIndex;

    /// <summary>+1 vers la fin de la ligne, -1 vers l'origine.</summary>
    public int Direction = 1;

    /// <summary>Kilomètres restant à parcourir sur le tronçon en cours.</summary>
    public double DistanceToNextStop;

    /// <summary>Chargement courant, par marchandise.</summary>
    public readonly Dictionary<string, double> Cargo = new();

    /// <summary>
    /// Prix de revient unitaire moyen de la marchandise détenue. Sans ça on ne
    /// sait pas si une vente est un profit ou une perte : dans ce modèle le train
    /// <em>achète</em> réellement sa cargaison au marché de départ.
    /// </summary>
    public readonly Dictionary<string, double> UnitCost = new();

    public double TotalKmTravelled;

    /// <summary>
    /// Chargements × kilomètres parcourus, à plat : la charge portée sur chaque pas,
    /// multipliée par sa longueur. Télémétrie seulement — divisée par
    /// <see cref="TotalKmTravelled"/>, c'est la charge moyenne du train, celle qui
    /// calibre le modèle de coût <c>mass</c> (voir <see cref="TrainCost"/>).
    /// </summary>
    public double TotalLoadKm;

    /// <summary>
    /// Tonnes de chargement, wagons compris, × kilomètres parcourus, à plat. Même
    /// rôle que <see cref="TotalLoadKm"/> pour la facture du modèle <c>mass</c> ;
    /// nul sous le modèle <c>flat</c>, qui ne lit aucune masse.
    /// </summary>
    public double TotalTonneKm;

    public double LoadedUnits
    {
        get
        {
            double total = 0;
            foreach (var kv in Cargo) total += kv.Value;
            return total;
        }
    }

    public double FreeCapacity => Math.Max(0, Capacity - LoadedUnits);
}

/// <summary>
/// Contrat du module de transport : déplacer les trains et échanger avec les
/// marchés. Comme le solveur économique, il ne doit ni créer ni détruire de
/// marchandise — seulement en transférer entre un marché et un train.
/// </summary>
public interface IHaulageSolver
{
    string Name { get; }
    void Initialize(WorldState world);
    void Step(WorldState world, SimTick tick);
}

/// <summary>
/// Transporteur opportuniste de référence : à chaque arrêt, il vend ce qui se
/// vend et achète ce qui promet la meilleure marge en aval de son parcours.
/// <para>
/// Ce n'est pas l'IA d'un concurrent, et ce n'est pas non plus ce que fera le
/// joueur — c'est un <em>instrument de mesure</em>. Un trader glouton avec une
/// information parfaite sur les prix courants révèle si l'économie présente des
/// gradients exploitables. S'il n'arrive pas à gagner d'argent, aucun joueur n'y
/// arrivera ; s'il en gagne sans jamais avoir à changer de route, l'économie est
/// trop statique pour être intéressante.
/// </para>
/// </summary>
public sealed class OpportunisticHaulageSolver : IHaulageSolver
{
    public string Name => "opportunistic";

    public void Initialize(WorldState world)
    {
        // Premier échange au point de départ, pour que les trains ne parcourent
        // pas leur premier tronçon à vide sans raison.
        foreach (var train in world.Trains)
            TradeAtStop(world, train);
    }

    public void Step(WorldState world, SimTick tick)
    {
        // L'entretien des voies est dû avant la porte ci-dessous, et c'est voulu :
        // une compagnie sous administration arrête ses trains, pas son réseau. Une
        // voie posée coûte ce qu'elle coûte, qu'on y roule ou non. Nul sans réseau
        // déclaré, donc heartland et les scénarios à lignes saisies à la main ne
        // voient rien changer.
        if (world.Network is { } network)
        {
            double upkeep = network.UpkeepPerTick;
            if (upkeep > 0) world.Company.PayTrackUpkeep(upkeep);
        }

        // Une compagnie sous administration ne fait pas rouler ses trains. Sans
        // cette porte, plus elle roulait plus elle creusait : les coûts
        // kilométriques étaient prélevés sans condition alors que les achats de
        // fret, eux, étaient déjà coupés. Voir Company.Grounded, posé par le
        // module finance ; faux tant qu'aucun scénario n'active la finance.
        if (world.Company.Grounded) return;

        foreach (var train in world.Trains)
            MoveTrain(world, train);
    }

    private void MoveTrain(WorldState world, Train train)
    {
        var stops = train.Line.Stops;
        if (stops.Count < 2) return;

        double budget = train.SpeedKmPerTick;
        double kmThisTick = 0;
        double loadKmThisTick = 0;
        double tonneKmThisTick = 0;
        // Kilomètres facturés : les kilomètres parcourus, pondérés par ce que le
        // réseau dit du relief du tronçon. Vaut kmThisTick sur une ligne plate ou
        // sans relief déclaré.
        double chargeableKm = 0;
        // Les mêmes, pondérés en plus par la masse des chargements portée sur chaque
        // pas, wagons compris : la part de la facture qui dépend de ce que le train
        // transporte (modèle « mass »). La charge est celle d'avant l'arrêt, puisque
        // l'échange se fait à l'arrivée : un pas se paie avec ce qu'on a porté
        // pendant ce pas.
        double chargeableTonneKm = 0;

        // Garde-fou : un train très rapide sur une ligne très courte pourrait
        // enchaîner un grand nombre d'arrêts dans un seul tick. On borne pour ne
        // pas boucler indéfiniment sur une ligne dégénérée.
        int stopsVisited = 0;
        const int maxStopsPerTick = 64;

        while (budget > 1e-9 && stopsVisited < maxStopsPerTick)
        {
            int next = train.StopIndex + train.Direction;
            if (next < 0 || next >= stops.Count)
            {
                train.Direction = -train.Direction;
                next = train.StopIndex + train.Direction;
                if (next < 0 || next >= stops.Count) break;
            }

            if (train.DistanceToNextStop <= 0)
                train.DistanceToNextStop = train.Line.DistanceBetween(train.StopIndex, next);

            double step = Math.Min(budget, train.DistanceToNextStop);
            train.DistanceToNextStop -= step;
            budget -= step;
            kmThisTick += step;
            double chargeableStep = step * train.Line.LegCostFactor(train.StopIndex, next);
            chargeableKm += chargeableStep;
            double load = train.LoadedUnits;
            double tonnes = TrainCost.LoadedTonnes(world, train);
            chargeableTonneKm += chargeableStep * tonnes;
            loadKmThisTick += step * load;
            tonneKmThisTick += step * tonnes;

            if (train.DistanceToNextStop <= 1e-9)
            {
                train.DistanceToNextStop = 0;
                train.StopIndex = next;
                stopsVisited++;

                // Le sens de départ est fixé AVANT d'échanger, et c'est essentiel.
                // Les décisions d'achat regardent les arrêts à venir ; à un
                // terminus, le sens courant pointe encore vers ceux qu'on vient de
                // quitter. Sans cette ligne, un train arrivant au bout de la ligne
                // ne voyait aucune destination devant lui et repartait à vide —
                // Fairview, la ferme à blé, est le terminus de la ligne, et ses
                // moulins sont restés à 1 % pendant toutes les mesures précédentes.
                if (train.StopIndex == 0) train.Direction = 1;
                else if (train.StopIndex == stops.Count - 1) train.Direction = -1;

                TradeAtStop(world, train);
            }
        }

        train.TotalKmTravelled += kmThisTick;
        train.TotalLoadKm += loadKmThisTick;
        train.TotalTonneKm += tonneKmThisTick;
        // Une seule formule pour la facture et pour la décision : voir TrainCost.
        // Sous le modèle « flat », elle vaut chargeableKm × costPerKm au bit près.
        var rates = TrainCost.Rates(world.Def.Haulage, train);
        world.Company.PayOperating(TrainCost.Charge(rates, chargeableKm, chargeableTonneKm));
    }

    private void TradeAtStop(WorldState world, Train train)
    {
        var stop = train.Line.Stops[train.StopIndex];
        SellHere(world, train, stop);
        BuyHere(world, train, stop);
    }

    /// <summary>
    /// Le marché à portée de la gare qui paie le mieux cette marchandise, ou
    /// <c>null</c> si aucun n'en tient. À prix égaux, le premier dans l'ordre de la
    /// gare, pour que le choix soit reproductible.
    /// </summary>
    private static Market? BestBuyerAt(RailStop stop, string cargoId)
    {
        Market? best = null;
        foreach (var market in stop.Markets)
            if (market.CargoId == cargoId && (best is null || market.Price > best.Price)) best = market;
        return best;
    }

    private void SellHere(WorldState world, Train train, RailStop stop)
    {
        double minMargin = world.Def.Haulage.MinMargin;

        // Parcours dans l'ordre canonique : l'ordre des ventes influe sur les
        // prix, donc sur le résultat. Il doit être reproductible.
        foreach (string cargoId in world.CargoOrder)
        {
            if (!train.Cargo.TryGetValue(cargoId, out double held) || held <= 0) continue;

            // Le train vend au marché de la gare qui paie le mieux : la place de la
            // ville ou l'une des installations à portée.
            var market = BestBuyerAt(stop, cargoId);
            if (market is null) continue;
            double unitCost = train.UnitCost.TryGetValue(cargoId, out double c) ? c : 0;

            bool profitable = market.Price >= unitCost * (1.0 + minMargin);
            // Si rien en aval ne paie mieux, on solde ici plutôt que de traîner
            // la cargaison indéfiniment — mais jamais en dessous du prix de
            // revient : vendre à perte pour libérer de la place est une façon
            // discrète de faire disparaître de l'argent, et c'est exactement ce
            // que le premier jet faisait.
            bool noBetterAhead = BestPriceAhead(world, train, cargoId) <= market.Price;

            if (!profitable && !(noBetterAhead && market.Price >= unitCost)) continue;

            double proceeds = MoveToMarket(world, market, cargoId, held);
            world.Company.Earn(proceeds);
            train.Cargo[cargoId] = 0;
            train.UnitCost[cargoId] = 0;
        }
    }

    /// <summary>
    /// Ce qu'un train peut acheter à l'arrêt : un marché à portée de la gare, ou une
    /// charrette qui passe dans son rayon d'action.
    /// </summary>
    private sealed record Offer(string CargoId, string Key, double Price, double Available,
        Market? Market, CartShipment? Cart);

    private List<Offer> OffersAt(WorldState world, RailStop stop)
    {
        var offers = new List<Offer>();
        foreach (var market in stop.Markets)
            offers.Add(new Offer(market.CargoId, market.Key, market.Price, Sellable(world, market), market, null));

        // Les charrettes dans le rayon de la gare, au prix qu'elles coûtent ici : leur
        // prix de départ, plus le charroi déjà parcouru. Décision de l'équipe du
        // 27 octobre 2026. Dans l'ordre de leur départ, pour que le choix soit
        // reproductible.
        var cfg = world.Def.Economy.Cartage;
        var shipments = world.Cartage.Shipments;
        for (int i = 0; i < shipments.Count; i++)
        {
            var cart = shipments[i];
            if (cart.Quantity <= 1e-12) continue;
            var (x, y) = cart.Position;
            double dx = x - stop.XKm, dy = y - stop.YKm;
            if (Math.Sqrt(dx * dx + dy * dy) > stop.RadiusKm + 1e-9) continue;
            offers.Add(new Offer(cart.CargoId, "charrette:" + i.ToString("D6", System.Globalization.CultureInfo.InvariantCulture),
                cart.PriceHere(cfg), cart.Quantity, null, cart));
        }
        return offers;
    }

    private void BuyHere(WorldState world, Train train, RailStop stop)
    {
        double minMargin = world.Def.Haulage.MinMargin;
        double free = train.FreeCapacity;
        if (free <= world.Def.Haulage.MinTradeQty) return;

        // On évalue chaque offre à portée de la gare — marchés et charrettes —, puis
        // on sert d'abord la plus rentable. Une marchandise peut s'acheter à plusieurs
        // endroits : la forêt, la place de la ville et la charrette qui passe ne la
        // vendent pas au même prix.
        var candidates = new List<(Offer Offer, double ProfitPerUnit)>();
        foreach (var offer in OffersAt(world, stop))
        {
            if (offer.Available <= world.Def.Haulage.MinTradeQty) continue;

            double bestAhead = BestPriceAhead(world, train, offer.CargoId);
            double haulCost = HaulCostPerUnitAhead(world, train, offer.CargoId);
            double profit = bestAhead - offer.Price * (1.0 + minMargin) - haulCost;
            if (profit >= world.Def.Haulage.MinProfitPerUnit) candidates.Add((offer, profit));
        }

        // Tri stable et total : marge décroissante, puis marchandise et offre par
        // identifiant croissant pour départager. Sans ces critères, deux offres de
        // marge identique pourraient être servies dans un ordre arbitraire.
        candidates.Sort((a, b) =>
        {
            int cmp = b.ProfitPerUnit.CompareTo(a.ProfitPerUnit);
            if (cmp != 0) return cmp;
            cmp = string.CompareOrdinal(a.Offer.CargoId, b.Offer.CargoId);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.Offer.Key, b.Offer.Key);
        });

        foreach (var (offer, _) in candidates)
        {
            if (free <= world.Def.Haulage.MinTradeQty) break;

            string cargoId = offer.CargoId;
            double wanted = Math.Min(free, offer.Available);

            // La compagnie n'achète qu'avec ce qu'elle a, plus le découvert que le
            // module finance lui accorde. Sans finance, ce découvert vaut zéro et
            // la règle est celle d'avant : pas d'achat de fret à crédit.
            if (world.Company.SpendableCash <= 0) break;
            double affordable = offer.Price > 0 ? world.Company.SpendableCash / offer.Price : wanted;
            wanted = Math.Min(wanted, affordable);
            if (wanted <= world.Def.Haulage.MinTradeQty) continue;

            var (bought, cost) = offer.Market is { } market
                ? TakeFromMarket(world, market, cargoId, wanted)
                : TakeFromCart(world, offer.Cart!, offer.Price, wanted);
            if (bought <= 0) continue;

            world.Company.PayForCargo(cost);

            double held = train.Cargo.TryGetValue(cargoId, out double h) ? h : 0;
            double prevUnit = train.UnitCost.TryGetValue(cargoId, out double u) ? u : 0;
            double newHeld = held + bought;
            train.Cargo[cargoId] = newHeld;
            train.UnitCost[cargoId] = newHeld > 0 ? (held * prevUnit + cost) / newHeld : 0;

            free = train.FreeCapacity;
        }
    }

    /// <summary>
    /// Charge sur le train une partie d'une charrette, au prix qu'elle coûte à la gare.
    /// Pas d'effet de prix : la charrette n'est pas un marché, son prix est celui de
    /// son chargement.
    /// </summary>
    private static (double Bought, double Cost) TakeFromCart(WorldState world, CartShipment cart, double price, double qty)
    {
        double taken = Math.Min(qty, cart.Quantity);
        if (taken <= 0) return (0, 0);
        cart.Quantity = Maths.SnapToZero(cart.Quantity - taken);
        world.Cartage.AddCaptured(cart.CargoId, taken);
        return (taken, taken * price);
    }

    /// <summary>
    /// Ce qu'une ville accepte de céder : son excédent, une fois ses propres
    /// besoins couverts.
    /// </summary>
    private static double Sellable(WorldState world, Market market)
        => market.SellableStock(
            world.Def.PriceModel.CoverageHorizonTicks,
            world.Def.Economy.RetainedCoverage);

    /// <summary>
    /// Meilleur prix observable sur les arrêts situés en aval, dans le sens de
    /// marche courant, terminus inclus — sur le meilleur marché à portée de chaque
    /// gare.
    /// <para>
    /// C'est une information que le joueur a aussi (les prix sont affichés), mais
    /// le trader l'exploite sans anticiper que ses propres livraisons vont faire
    /// baisser ce prix. Cette myopie est volontaire : un modèle qui anticiperait
    /// parfaitement rendrait l'économie inerte.
    /// </para>
    /// </summary>
    private double BestPriceAhead(WorldState world, Train train, string cargoId)
    {
        var stops = train.Line.Stops;
        double best = 0;
        for (int i = train.StopIndex + train.Direction;
             i >= 0 && i < stops.Count;
             i += train.Direction)
        {
            double price = BestBuyerAt(stops[i], cargoId)?.Price ?? 0;
            if (price > best) best = price;
        }
        return best;
    }

    /// <summary>
    /// Le coût que le transporteur impute à un chargement de <paramref name="cargoId"/>
    /// s'il l'achète là où se trouve le train, pour le porter jusqu'au meilleur
    /// acheteur en aval. C'est ce nombre, et lui seul, que <c>BuyHere</c> retranche
    /// du gain espéré.
    /// <para>
    /// Public parce que c'est une promesse vérifiable : sous le modèle de coût
    /// <c>mass</c>, il doit valoir exactement ce qu'un chargement de plus, wagon
    /// compris, ajoute à la facture de <c>MoveTrain</c> sur ce trajet, et un test le
    /// vérifie (MarginalCostTests). Sous le modèle <c>flat</c>, il ne le vaut pas : il
    /// répartit le coût du train sur une charge escomptée, à plat.
    /// </para>
    /// </summary>
    public double HaulCostPerUnitAhead(WorldState world, Train train, string cargoId)
    {
        var stops = train.Line.Stops;
        int bestIndex = -1;
        double best = 0;
        for (int i = train.StopIndex + train.Direction;
             i >= 0 && i < stops.Count;
             i += train.Direction)
        {
            double price = BestBuyerAt(stops[i], cargoId)?.Price ?? 0;
            if (price > best) { best = price; bestIndex = i; }
        }
        if (bestIndex < 0) return 0;

        // Modèle « mass » : ce qu'un chargement de plus, avec son wagon, ajoutera
        // réellement à la facture d'ici à la destination visée, relief compris — la
        // dérivée de ce que MoveTrain prélèvera. Rien n'y est réparti sur une charge
        // escomptée : la locomotive est due quoi qu'on achète, elle ne décide de rien.
        var haulage = world.Def.Haulage;
        if (TrainCost.IsMassModel(haulage))
            return TrainCost.MarginalCostPerLoad(
                TrainCost.Rates(haulage, train), haulage, world.Cargo(cargoId), train.Line, train.StopIndex, bestIndex);

        double km = train.Line.DistanceBetween(train.StopIndex, bestIndex);
        // Le coût est réparti sur la charge réellement escomptée, pas sur la
        // capacité théorique : un train à moitié vide paie les mêmes kilomètres.
        double effectiveCapacity = Math.Max(1.0, train.Capacity * world.Def.Haulage.ExpectedLoadFactor);
        return km * train.CostPerKm / effectiveCapacity;
    }

    /// <summary>
    /// Verse une quantité sur un marché en tranches, en recalculant le prix entre
    /// chaque tranche.
    /// <para>
    /// Le découpage est ce qui rend le modèle honnête : déverser vingt
    /// chargements d'un coup au prix affiché reviendrait à ignorer que l'offre
    /// vient d'exploser. En tranches, les derniers chargements se vendent moins
    /// cher que les premiers — exactement ce qui décourage de saturer une même
    /// ville indéfiniment.
    /// </para>
    /// </summary>
    private double MoveToMarket(WorldState world, Market market, string cargoId, double qty)
    {
        var cargo = world.Cargo(cargoId);
        int slices = Math.Max(1, world.Def.Haulage.PriceImpactSlices);
        double sliceQty = qty / slices;
        double proceeds = 0;
        double remaining = qty;

        for (int i = 0; i < slices && remaining > 1e-9; i++)
        {
            double q = Math.Min(sliceQty, remaining);
            proceeds += market.Price * q;
            market.Deposit(q);
            market.Price = world.PriceModel.PriceFor(cargo, market);
            remaining -= q;
        }
        return proceeds;
    }

    private (double Bought, double Cost) TakeFromMarket(
        WorldState world, Market market, string cargoId, double qty)
    {
        var cargo = world.Cargo(cargoId);
        int slices = Math.Max(1, world.Def.Haulage.PriceImpactSlices);
        double sliceQty = qty / slices;
        double bought = 0, cost = 0, remaining = qty;

        for (int i = 0; i < slices && remaining > 1e-9; i++)
        {
            double q = Math.Min(sliceQty, remaining);
            double taken = market.Withdraw(q);
            if (taken <= 0) break;
            cost += market.Price * taken;
            bought += taken;
            market.Price = world.PriceModel.PriceFor(cargo, market);
            remaining -= taken;
        }
        return (Maths.SnapToZero(bought), cost);
    }
}
