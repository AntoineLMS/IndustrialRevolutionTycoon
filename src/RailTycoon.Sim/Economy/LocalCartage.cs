namespace RailTycoon.Sim.Economy;

/// <summary>
/// Le charroi local (phase 0c) : ce qui passe sans train entre deux marchés d'une
/// même ville — de la forêt à la scierie, de la boulangerie à la place.
/// <para>
/// Chaque installation a son propre marché ; sans charroi, rien ne passerait de
/// l'une à l'autre sans un train. Le charroi en laisse passer au plus
/// <see cref="CartageDef.CapacityPerTick"/> chargements par tick, par ville et par
/// marchandise, en tranches : chaque tranche part du marché le moins cher vers le
/// marché acheteur le plus cher, tant que l'écart de prix paie la charrette —
/// <see cref="CartageDef.CostPerLoad"/> plus <see cref="CartageDef.CostPerLoadKm"/>
/// par kilomètre entre les deux. Au-delà de sa capacité, l'écart reste entier : c'est
/// lui qu'un train qui reste dans la même ville peut exploiter.
/// </para>
/// <para>
/// Le charroi ne coûte rien à la compagnie et ne crée ni ne détruit de marchandise :
/// il la déplace d'un stock à l'autre, comme un train, et le bilan matière n'y voit
/// qu'un transfert. Il ne vend qu'à un marché qui consomme la marchandise, et ne
/// prend à un marché que ce qu'il cède à un train (<see cref="Market.SellableStock"/>).
/// Il ne touche pas aux prix : le solveur économique les recalcule en phase 4 ; il
/// interroge seulement le modèle de prix sur l'état du moment pour choisir où aller.
/// </para>
/// </summary>
public static class LocalCartage
{
    public static void Step(WorldState world)
    {
        var cfg = world.Def.Economy.Cartage;
        if (cfg.CapacityPerTick <= 0) return;

        int slices = Math.Max(1, cfg.Slices);
        double sliceQty = cfg.CapacityPerTick / slices;
        double horizon = world.Def.PriceModel.CoverageHorizonTicks;
        double retained = world.Def.Economy.RetainedCoverage;

        foreach (var city in world.Cities)
            foreach (string cargoId in world.CargoOrder)
            {
                var markets = new List<Market>();
                foreach (var market in world.MarketsOf(city))
                    if (market.CargoId == cargoId) markets.Add(market);
                if (markets.Count < 2) continue;

                var cargo = world.Cargo(cargoId);
                for (int slice = 0; slice < slices; slice++)
                {
                    // Le meilleur couple du moment : l'écart de prix, moins ce que coûte
                    // la charrette. Parcours dans l'ordre de la ville, comparaison
                    // stricte : à gain égal, le premier couple gagne.
                    Market? from = null, to = null;
                    double bestGain = 0;
                    foreach (var source in markets)
                    {
                        if (source.SellableStock(horizon, retained) <= 1e-9) continue;
                        double sourcePrice = world.PriceModel.PriceFor(cargo, source);
                        foreach (var buyer in markets)
                        {
                            if (ReferenceEquals(buyer, source)) continue;
                            if (buyer.BaseDemandRate + buyer.IndustryDemandRate <= 0) continue;
                            double gain = world.PriceModel.PriceFor(cargo, buyer) - sourcePrice - Cost(cfg, source, buyer);
                            if (gain > bestGain) { bestGain = gain; from = source; to = buyer; }
                        }
                    }
                    if (from is null || to is null) break;

                    double qty = Math.Min(sliceQty, from.SellableStock(horizon, retained));
                    from.CartOut(qty);
                    to.CartIn(qty);
                }
            }
    }

    /// <summary>Ce que coûte une charrette par chargement entre deux marchés.</summary>
    public static double Cost(CartageDef cfg, Market from, Market to)
    {
        double dx = to.XKm - from.XKm, dy = to.YKm - from.YKm;
        return cfg.CostPerLoad + cfg.CostPerLoadKm * Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Refuse au chargement un charroi aux réglages absurdes.</summary>
    public static void Validate(CartageDef cfg)
    {
        if (cfg.CapacityPerTick < 0)
            throw new InvalidDataException("economy.cartage.capacityPerTick ne peut pas être négatif.");
        if (cfg.CostPerLoad < 0 || cfg.CostPerLoadKm < 0)
            throw new InvalidDataException("economy.cartage : un coût de charroi ne peut pas être négatif.");
        if (cfg.Slices < 1)
            throw new InvalidDataException("economy.cartage.slices doit valoir au moins 1.");
    }
}
