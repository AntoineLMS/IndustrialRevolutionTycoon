namespace RailTycoon.Sim.Telemetry;

/// <summary>
/// Les invariants que la simulation doit respecter à tout moment, quel que soit
/// le scénario et quelle que soit l'implémentation des solveurs.
/// <para>
/// Ces vérifications sont le filet de sécurité qui permet de confier un module à
/// quelqu'un d'autre — humain ou agent — sans relire chaque ligne : une
/// implémentation qui crée de la marchandise à partir de rien, laisse un stock
/// négatif ou fait fuiter de l'argent est détectée par un test, pas par un
/// joueur trois mois plus tard.
/// </para>
/// </summary>
public static class Invariants
{
    public sealed record Violation(string Rule, string Detail);

    public static List<Violation> Check(WorldState world, double initialStockTotal, double epsilon = 1e-6)
    {
        var violations = new List<Violation>();

        double stockTotal = 0, producedTotal = 0, consumedTotal = 0;

        foreach (var city in world.Cities)
        {
            foreach (var market in world.MarketsOf(city))
            {
                string where = $"{city.Id}/{market.CargoId}";

                if (double.IsNaN(market.Stock) || double.IsInfinity(market.Stock))
                    violations.Add(new("stock-fini", $"{where} : stock = {market.Stock}"));
                if (double.IsNaN(market.Price) || double.IsInfinity(market.Price))
                    violations.Add(new("prix-fini", $"{where} : prix = {market.Price}"));

                if (market.Stock < -epsilon)
                    violations.Add(new("stock-positif", $"{where} : stock = {market.Stock:0.######}"));

                var cargo = world.Cargo(market.CargoId);
                double minPrice = cargo.BasePrice * world.Def.PriceModel.MinMultiplier - epsilon;
                double maxPrice = cargo.BasePrice * world.Def.PriceModel.MaxMultiplier + epsilon;
                if (market.Price < minPrice || market.Price > maxPrice)
                    violations.Add(new("prix-borne",
                        $"{where} : prix = {market.Price:0.##} hors de [{minPrice:0.##}, {maxPrice:0.##}]"));

                stockTotal += market.Stock;
                producedTotal += market.TotalProduced;
                consumedTotal += market.TotalConsumed;
            }
        }

        double inTransit = 0;
        foreach (var train in world.Trains)
        {
            foreach (var kv in train.Cargo)
            {
                if (kv.Value < -epsilon)
                    violations.Add(new("chargement-positif",
                        $"train {train.Id} : {kv.Key} = {kv.Value:0.######}"));
                inTransit += kv.Value;
            }
            if (train.LoadedUnits > train.Capacity + epsilon)
                violations.Add(new("capacite",
                    $"train {train.Id} : {train.LoadedUnits:0.##} chargements pour une capacité de {train.Capacity:0.##}"));
        }

        // Bilan matière : rien n'apparaît ni ne disparaît en dehors de la
        // production et de la consommation. Le transport ne fait que déplacer.
        double expected = initialStockTotal + producedTotal - consumedTotal;
        double actual = stockTotal + inTransit;
        double tolerance = Math.Max(1e-4, Math.Abs(expected) * 1e-9);
        if (Math.Abs(actual - expected) > tolerance)
            violations.Add(new("conservation-matiere",
                $"attendu {expected:0.####}, observé {actual:0.####} " +
                $"(stocks {stockTotal:0.##} + en transit {inTransit:0.##}, " +
                $"produit {producedTotal:0.##}, consommé {consumedTotal:0.##})"));

        // Bilan comptable : la trésorerie doit s'expliquer entièrement par les
        // trois flux enregistrés. Tout écart est de l'argent créé ou perdu.
        var co = world.Company;
        double expectedCash = world.Def.StartingCash + co.NetProfit;
        if (Math.Abs(co.Cash - expectedCash) > Math.Max(1e-4, Math.Abs(expectedCash) * 1e-9))
            violations.Add(new("bilan-tresorerie",
                $"caisse {co.Cash:0.##}, attendu {expectedCash:0.##}"));

        return violations;
    }

    /// <summary>
    /// Total de la matière présente dans le monde, à capturer avant le premier
    /// tick. Le chargement des trains est inclus : le transporteur échange déjà
    /// au point de départ pendant l'initialisation, et cette matière n'a pas été
    /// produite — l'oublier ferait échouer le bilan matière dès le premier tick.
    /// </summary>
    public static double InitialStockTotal(WorldState world)
    {
        double total = 0;
        foreach (var city in world.Cities)
            foreach (var market in world.MarketsOf(city))
                total += market.Stock;
        foreach (var train in world.Trains)
            foreach (var kv in train.Cargo)
                total += kv.Value;
        return total;
    }
}
