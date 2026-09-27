using RailTycoon.Sim.Finance;

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
        // flux enregistrés. Tout écart est de l'argent créé ou perdu.
        //
        // Le quatrième flux — les mouvements financiers — a été ajouté à cette
        // formule, et c'est une correction, pas un assouplissement. Tant que la
        // finance tenait sa propre caisse, cet invariant ne vérifiait qu'une moitié
        // de la vérité : un dividende de 88 000 sortait du bilan de la société sans
        // sortir de la trésorerie du transporteur, qui pouvait donc dépenser le
        // même argent. Les deux bilans s'équilibraient au centime et la fuite était
        // pourtant réelle — la signature exacte du lavage de fret. Avec ce terme,
        // il n'existe plus de mouvement d'argent que personne ne compte.
        var co = world.Company;
        double expectedCash = world.Def.StartingCash + co.NetProfit + co.TotalFinanceFlow;
        if (Math.Abs(co.Cash - expectedCash) > Math.Max(1e-4, Math.Abs(expectedCash) * 1e-9))
            violations.Add(new("bilan-tresorerie",
                $"caisse {co.Cash:0.##}, attendu {expectedCash:0.##}"));

        CheckFinance(world, violations, expectedCash);

        return violations;
    }

    /// <summary>
    /// Invariants du module finance. Ils se comparent à <b>zéro exactement</b>,
    /// sans tolérance, et c'est le point : la comptabilité est tenue en
    /// <see cref="decimal"/> et en partie double, donc un résidu même d'un centime
    /// est un bug, jamais un artefact de calcul. Accorder une tolérance ici
    /// reviendrait à autoriser la fuite qu'on cherche à interdire — une demi-unité
    /// par tick fait une fortune sur deux années de jeu.
    /// </summary>
    private static void CheckFinance(WorldState world, List<Violation> violations, double expectedOperatingCash)
    {
        var finance = world.Finance;
        if (!finance.Enabled) return;

        foreach (var company in finance.Companies)
        {
            string where = company.Id;

            // Aucun actif négatif, jamais. C'est la forme forte de « pas de
            // trésorerie négative silencieuse » : une caisse négative ne se
            // distingue en rien d'une compagnie simplement pauvre, alors qu'elle
            // est en réalité un emprunt que personne n'a consenti. Ici un
            // découvert est une dette au passif, pas un actif de signe inversé.
            foreach (var account in company.Book.Accounts)
                if (account.Kind == Finance.AccountKind.Asset && account.Balance < 0m)
                    violations.Add(new("tresorerie-non-negative",
                        $"{where} : {account.Id} = {account.Balance} " +
                        $"(dernière écriture : {company.Book.LastEntry})"));

            // Actif = passif + capitaux propres.
            if (company.Book.Residual != 0m)
                violations.Add(new("bilan-actif-passif",
                    $"{where} : actif {company.Book.Assets} ≠ passif {company.Book.Liabilities} " +
                    $"+ capitaux propres {company.Book.Equity}, écart {company.Book.Residual} " +
                    $"(dernière écriture : {company.Book.LastEntry})"));

            // Somme des actions détenues = actions émises.
            long held = company.Register.HeldTotal();
            if (held != company.Register.SharesIssued)
                violations.Add(new("actions-emises",
                    $"{where} : {held} actions détenues pour {company.Register.SharesIssued} émises"));

            foreach (var holder in company.Register.Holders)
                if (holder.Shares < 0)
                    violations.Add(new("actions-emises",
                        $"{where} : {holder.HolderId} détient {holder.Shares} actions"));

            // Dette et intérêts : rien ne s'évapore entre l'emprunt et sa fin.
            decimal outstanding = company.OutstandingPrincipal();
            if (company.PrincipalIssuedTotal != company.PrincipalRepaidTotal + outstanding)
                violations.Add(new("dette-emprunts",
                    $"{where} : {company.PrincipalIssuedTotal} emprunté ≠ " +
                    $"{company.PrincipalRepaidTotal} remboursé + {outstanding} restant dû"));

            if (company.Book[Accounts.BondsPayable] != outstanding)
                violations.Add(new("dette-emprunts",
                    $"{where} : passif obligataire {company.Book[Accounts.BondsPayable]} ≠ " +
                    $"capital restant dû {outstanding}"));

            if (company.InterestAccruedTotal != company.InterestPaidTotal + company.UnpaidInterest)
                violations.Add(new("dette-emprunts",
                    $"{where} : {company.InterestAccruedTotal} d'intérêts dus ≠ " +
                    $"{company.InterestPaidTotal} payés + {company.UnpaidInterest} à payer"));

            // Le coût de revient d'une participation soldée doit être sorti en
            // entier : quelques centimes oubliés sur une ligne à zéro action sont
            // exactement la fuite qu'un arrondi au prorata produit.
            foreach (var position in company.Investments.Positions)
                if (position.Shares == 0 && position.CostBasis != 0m)
                    violations.Add(new("bilan-actif-passif",
                        $"{where} : participation {position.CompanyId} soldée mais " +
                        $"{position.CostBasis} de prix de revient subsiste"));
        }

        // Le découvert d'exploitation : rien de négatif ne reste tacite.
        //
        // La trésorerie d'exploitation pouvait plonger indéfiniment dans le rouge
        // sans qu'aucun invariant ne s'en aperçoive — bilan-tresorerie est une
        // identité comptable, elle est tout aussi vraie avec une caisse à −48 830.
        // Désormais cette part négative existe au bilan, au centime, comme une
        // dette qui porte intérêt et qui est bornée par ce que le bilan peut gager.
        var playerCompany = finance.Player;
        if (playerCompany is not null)
        {
            decimal shortfall = playerCompany.OverdraftBalance;

            // On ne détient pas de la caisse et un découvert en même temps : le
            // solde est arrêté à chaque clôture, dans un sens ou dans l'autre.
            // Deux lignes simultanées voudraient dire qu'un mouvement n'a pas été
            // reclassé, donc qu'une part du déficit est redevenue tacite.
            if (shortfall > 0m && playerCompany.Cash != 0m)
                violations.Add(new("decouvert-explicite",
                    $"découvert de {shortfall} et caisse de {playerCompany.Cash} en même temps"));

            // Un découvert au-delà de ce que la banque accorde doit avoir une
            // conséquence, pas seulement une ligne au bilan.
            if (shortfall > playerCompany.CreditFacility && !playerCompany.InReceivership)
                violations.Add(new("decouvert-explicite",
                    $"découvert {shortfall} au-delà du découvert autorisé " +
                    $"{playerCompany.CreditFacility} sans mise sous administration"));

            if (world.Company.Grounded != playerCompany.InReceivership)
                violations.Add(new("decouvert-explicite",
                    $"trains à l'arrêt = {world.Company.Grounded} alors que " +
                    $"l'administration judiciaire = {playerCompany.InReceivership}"));

            double expectedLimit = playerCompany.InReceivership
                ? 0.0
                : Money.ToDouble(playerCompany.CreditFacility);
            if (Math.Abs(world.Company.CreditLimit - expectedLimit) > 1e-6)
                violations.Add(new("decouvert-explicite",
                    $"découvert accordé au transporteur {world.Company.CreditLimit:0.##}, " +
                    $"attendu {expectedLimit:0.##}"));
        }

        var tycoon = finance.Magnate;
        if (tycoon is not null)
        {
            foreach (var account in tycoon.Book.Accounts)
                if (account.Kind == Finance.AccountKind.Asset && account.Balance < 0m)
                    violations.Add(new("tresorerie-non-negative",
                        $"magnat : {account.Id} = {account.Balance} " +
                        $"(dernière écriture : {tycoon.Book.LastEntry})"));

            if (tycoon.Book.Residual != 0m)
                violations.Add(new("bilan-actif-passif",
                    $"magnat : écart {tycoon.Book.Residual} " +
                    $"(dernière écriture : {tycoon.Book.LastEntry})"));

            if (tycoon.InterestAccruedTotal != tycoon.InterestPaidTotal + tycoon.Book[Accounts.AccruedPayable])
                violations.Add(new("dette-emprunts",
                    $"magnat : {tycoon.InterestAccruedTotal} d'intérêts de marge dus ≠ " +
                    $"{tycoon.InterestPaidTotal} payés + {tycoon.Book[Accounts.AccruedPayable]} à payer"));

            // Le portefeuille au bilan est tenu au prix de revient : il doit valoir
            // exactement la somme des lignes.
            if (tycoon.Book[Accounts.Portfolio] != tycoon.Portfolio.CostBasisTotal())
                violations.Add(new("bilan-actif-passif",
                    $"magnat : portefeuille au bilan {tycoon.Book[Accounts.Portfolio]} ≠ " +
                    $"somme des lignes {tycoon.Portfolio.CostBasisTotal()}"));

            foreach (var position in tycoon.Portfolio.Positions)
            {
                if (position.Shares < 0)
                    violations.Add(new("actions-emises",
                        $"magnat : {position.Shares} actions de {position.CompanyId}"));
                if (position.Shares == 0 && position.CostBasis != 0m)
                    violations.Add(new("bilan-actif-passif",
                        $"magnat : ligne {position.CompanyId} soldée mais " +
                        $"{position.CostBasis} de prix de revient subsiste"));
            }
        }

        // La frontière entre le monde en double et la comptabilité, et du même coup
        // la sentinelle de la double représentation de la caisse.
        //
        // La position nette du bilan — caisse moins découvert — doit valoir la
        // trésorerie du transporteur, au demi-centime près et pas davantage. Un
        // prélèvement financier passé au bilan mais pas répercuté sur la trésorerie
        // fait immédiatement apparaître son montant ici : c'est ce qui rend
        // impossible de reconstituer la faille où la société et le transporteur
        // dépensaient deux fois le même argent.
        //
        // Le seuil est un demi-centime et il ne s'accumule pas : on reflète le
        // cumul arrondi, jamais la somme des flux arrondis. Mesuré sur le scénario
        // de finance, l'écart plafonne sous 0,005 de 720 à 6 000 ticks.
        if (playerCompany is not null)
        {
            double position = Money.ToDouble(playerCompany.Cash - playerCompany.OverdraftBalance);
            if (Math.Abs(position - world.Company.Cash) > 0.005 + 1e-6)
                violations.Add(new("frontiere-tresorerie",
                    $"position nette au bilan {position:0.####}, " +
                    $"trésorerie d'exploitation {world.Company.Cash:0.####}"));
        }

        double reflected = Money.ToDouble(finance.ReflectedOperatingCash);
        double operatingOnly = expectedOperatingCash - world.Company.TotalFinanceFlow;
        if (Math.Abs(reflected - operatingOnly) > 0.005 + 1e-9)
            violations.Add(new("frontiere-tresorerie",
                $"exploitation reflétée {reflected:0.####}, résultat d'exploitation {operatingOnly:0.####}"));
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
