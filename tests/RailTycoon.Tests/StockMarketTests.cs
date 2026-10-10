using System.Globalization;
using RailTycoon.Sim;
using RailTycoon.Sim.Core;
using RailTycoon.Sim.Economy;
using RailTycoon.Sim.Finance;
using RailTycoon.Sim.Telemetry;

namespace RailTycoon.Tests;

/// <summary>
/// La bourse solide : profondeur finie du carnet, prises de bénéfices, ligne de crédit,
/// cascade des ventes forcées, ruine du magnat et sortie de ruine, salaire du président,
/// division d'actions. Même organisation que <see cref="FinanceTests"/> : un fichier à
/// part, un seul appel dans <c>Program</c>.
/// <para>
/// La plupart de ces tests posent une compagnie « stable » — sans train ni résultat, un
/// cours égal à son actif net par action, ramené chaque soir à sa valeur — et y passent
/// des ordres à la main : un cours qui ne bouge que par les ordres se lit sans bruit.
/// </para>
/// </summary>
internal static class StockMarketTests
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    private static string DataPath(string file) => Path.Combine(Fixtures.RepoRoot(), "data", file);

    public static void Register(TestRunner runner)
    {
        // ------------------------------------------------------------ carnet

        runner.Add("bourse — profondeur finie : un gros ordre coûte plus cher que plusieurs petits", () =>
        {
            var sim = new Simulation(Stable());
            sim.Step();
            var (state, def, player, _) = Parts(sim);
            Check.True(player.SharePrice == 10.10m, $"cours de départ {player.SharePrice}, attendu l'actif net 10,10");

            // Au comptant et d'un coup : 20 000 titres remontent vingt tranches.
            decimal oneBlock = StockMarket.Quote(def, player, 20_000, +1).Amount;
            decimal oneSmall = StockMarket.Quote(def, player, 1_000, +1).Amount;
            Check.True(oneSmall == 1_000 * 10.10m, $"une tranche se paie au cours affiché : {oneSmall}");
            Check.Less((double)(20 * oneSmall), (double)oneBlock,
                "vingt petits ordres sur un carnet reconstitué doivent coûter moins qu'un bloc");
            Check.True(oneBlock / 20_000 > 10.10m * 1.10m,
                $"le bloc doit se payer plus de 10 % au-dessus du cours affiché : {oneBlock / 20_000:0.0000}");

            // Et le carnet se reconstitue d'un jour à l'autre : des tranches achetées un
            // jour après l'autre se paient chacune au cours du jour, le bloc non.
            decimal spread = 0m;
            for (int day = 0; day < 20; day++)
            {
                decimal before = player.SharePrice;
                spread += StockMarket.Execute(def, player, 1_000, +1);
                Check.True(player.SharePrice > before, "l'ordre doit faire monter le cours");
                sim.Step();
                Check.True(player.SharePrice == 10.10m, "la nuit ramène le cours à sa valeur : le carnet se reconstitue");
            }
            Check.True(spread == 20 * oneSmall, $"vingt jours d'une tranche : {spread}, attendu {20 * oneSmall}");

            // Profondeur infinie (le comportement d'avant) : le bloc au cours d'avant,
            // proportionnel au nombre de titres. C'est la dette n° 1 que ce test referme.
            def.Market.BookTrancheFraction = 0m;
            Check.True(StockMarket.Quote(def, player, 20_000, +1).Amount == 20 * oneSmall,
                "sans profondeur, un bloc coûte exactement vingt petits ordres");
        });

        runner.Add("bourse — un ordre dans le carnet se règle au centime, et le budget ne se dépasse pas", () =>
        {
            var sim = new Simulation(Stable());
            sim.Step();
            var (state, def, player, tycoon) = Parts(sim);
            foreach (decimal budget in new[] { 0.5m, 10.10m, 12_345.67m, 250_000m, 5_000_000m })
            {
                long lot = StockMarket.MaxSharesFor(def, player, budget, 80_000);
                decimal cost = StockMarket.Quote(def, player, lot, +1).Amount;
                Check.True(cost <= budget, $"budget {budget} : {lot} titres coûtent {cost}");
                Check.True(Money.Round(cost) == cost, $"le montant d'un ordre est un nombre entier de centimes : {cost}");
                if (lot < 80_000)
                    Check.Less((double)budget, (double)StockMarket.Quote(def, player, lot + 1, +1).Amount,
                        $"budget {budget} : un titre de plus devait le dépasser");
            }

            // Un achat réel paie exactement le devis du carnet, et laisse la cote au
            // cours que le devis annonçait.
            decimal priceBefore = player.SharePrice;
            decimal basisBefore = tycoon.Portfolio.CostBasisOf(player.Id);
            long bought = Brokerage.BuyShares(state, def, tycoon, player);
            Check.True(bought > 0, "le magnat doit avoir acheté");
            decimal paid = tycoon.Portfolio.CostBasisOf(player.Id) - basisBefore;
            decimal priceAfter = player.SharePrice;
            player.SharePrice = priceBefore;
            var quote = StockMarket.Quote(def, player, bought, +1);
            player.SharePrice = priceAfter;
            Check.True(paid == quote.Amount && priceAfter == quote.FinalPrice,
                $"payé {paid} pour un devis de {quote.Amount} ; cours {priceAfter} pour {quote.FinalPrice}");
            Check.True(tycoon.Book.Residual == 0m && tycoon.PortfolioCost == tycoon.Portfolio.CostBasisTotal(),
                "bilan du magnat au centime après un achat dans le carnet");
        });

        // ---------------------------------------------------- ligne de crédit

        runner.Add("bourse — la ligne de crédit : la dette ne dépasse jamais la part prêtée, et l'achat au-delà est refusé", () =>
        {
            // Premier achat, ligne à 30 % et 90 % demandés à crédit : l'achat est réduit
            // à ce que la ligne et la caisse permettent.
            var sim = new Simulation(Stable(f =>
            {
                f.Tycoon.CreditLineRatio = 0.3m;
                f.Tycoon.MarginInitialRatio = 0.9m;
            }));
            sim.Step();
            var (state, def, player, tycoon) = Parts(sim);
            for (int order = 0; order < 6; order++)
            {
                decimal priceBefore = player.SharePrice;
                long bought = Brokerage.BuyShares(state, def, tycoon, player);
                long held = tycoon.Portfolio.SharesOf(player.Id);
                if (bought > 0)
                    Check.True(tycoon.MarginLoan <= Money.Round(0.3m * held * priceBefore),
                        $"ordre {order} : dette {tycoon.MarginLoan} au-delà de 30 % de {held} titres à {priceBefore}");
                sim.Step();
            }
            Check.True(tycoon.MarginLoan > 0m, "le test perdrait son sens sans emprunt");

            // Le refus : ligne saturée, caisse maigre. Au seuil de maintien (la ligne
            // d'avant), le même ordre passe — il s'endette jusqu'à l'appel de marge.
            decimal Refused(decimal? line)
            {
                var s = new Simulation(Stable(f =>
                {
                    f.Tycoon.CreditLineRatio = line;
                    f.Tycoon.MarginInitialRatio = 0.9m;
                }));
                s.Step();
                var (st, d, p, t) = Parts(s);
                // Une dette de marge à 30 % de la position, dont le produit a quitté sa
                // caisse ; il ne lui reste que 500.
                decimal loan = Money.Round(0.3m * t.PortfolioMarketValue(st));
                t.Book.Post("emprunt sur marge", Accounts.Cash, Accounts.MarginLoan, loan);
                t.Book.Post("dépense hors modèle", Accounts.PersonalEquity, Accounts.Cash, t.Cash - 500m);
                return Brokerage.BuyShares(st, d, t, p);
            }
            Check.True(Refused(0.3m) == 0, "ligne saturée : l'achat doit être refusé");
            Check.True(Refused(null) > 0, "sans ligne, le même achat passe : le test ne prouverait rien sinon");
        });

        // ------------------------------------------- appel de marge et cascade

        runner.Add("bourse — une baisse déclenche la vente forcée, qui pèse sur le cours ; la cascade ramène sous le seuil", () =>
        {
            // Un porteur de 10 % du capital, endetté à 59 % ; le cours tombe de 10,10 à 8.
            // La première vente, calculée au cours du moment, ne suffit pas : elle fait
            // baisser le cours. Le courtier revend, et le compte revient sous le seuil.
            var (sim, tycoon) = Leveraged(founderShares: 10_000, loan: 60_000m, crashTo: 8.00m, rounds: 20);
            var (state, def, player, _) = Parts(sim);
            decimal before = player.SharePrice;
            Brokerage.EnforceMarginCall(state, def, tycoon, sim.World.Tick);

            var call = state.Journal.Single(r => r.Kind == MarketRecordKind.MarginCall);
            Check.True(tycoon.MarginCalls == 1 && tycoon.ForcedSaleShares > 0, "appel de marge et vente forcée attendus");
            Check.Less((double)player.SharePrice, (double)before, "la vente forcée doit faire baisser le cours");
            Check.True(call.Rounds >= 2, $"la cascade devait demander plusieurs tours, {call.Rounds} obtenu(s)");
            Check.True(tycoon.MarginLoan <= Money.Round(0.7m * tycoon.PortfolioMarketValue(state)),
                $"dette {tycoon.MarginLoan} encore au-delà de 70 % de {tycoon.PortfolioMarketValue(state)}");
            Check.True(tycoon.Portfolio.SharesOf(player.Id) > 0 && !tycoon.Ruined,
                "un petit porteur ne doit pas être liquidé entièrement");
            Check.True(tycoon.Book.Residual == 0m && player.Book.Residual == 0m, "bilans au centime");

            // Une seule passe — le comportement d'avant — laisse le déficit ouvert :
            // c'est bien la cascade qui ramène le compte sous le seuil.
            var (once, t1) = Leveraged(founderShares: 10_000, loan: 60_000m, crashTo: 8.00m, rounds: 1);
            var (s1, d1, _, _) = Parts(once);
            Brokerage.EnforceMarginCall(s1, d1, t1, once.World.Tick);
            Check.Less((double)Money.Round(0.7m * t1.PortfolioMarketValue(s1)), (double)t1.MarginLoan,
                "une seule passe dans un carnet fini ne suffit pas");
        });

        runner.Add("bourse — la cascade converge : bornée, et au-delà de 29 % du capital elle liquide l'essentiel de la ligne", () =>
        {
            // Un porteur de 40 % : chaque vente déprécie tout le reste de sa ligne, et
            // au-delà de (1 − 0,7) / (0,7 × 1,5) = 29 % du capital, elle creuse le
            // déficit au lieu de le combler. La cascade s'arrête quand même, de l'autre
            // côté : la ligne vendue jusqu'à repasser sous ce seuil, le compte conforme,
            // en un nombre de tours borné.
            var (sim, tycoon) = Leveraged(founderShares: 40_000, loan: 235_000m, crashTo: 8.00m, rounds: 20);
            var (state, def, player, _) = Parts(sim);
            decimal deficitBefore = tycoon.MarginLoan - Money.Round(0.7m * tycoon.PortfolioMarketValue(state));

            // Un seul tour, pour voir le déficit se creuser.
            var (probe, tp) = Leveraged(founderShares: 40_000, loan: 235_000m, crashTo: 8.00m, rounds: 1);
            var (sp, dp, _, _) = Parts(probe);
            Brokerage.EnforceMarginCall(sp, dp, tp, probe.World.Tick);
            decimal deficitAfterOne = tp.MarginLoan - Money.Round(0.7m * tp.PortfolioMarketValue(sp));
            Check.Less((double)deficitBefore, (double)deficitAfterOne,
                "au-delà de 29 % du capital, la première vente forcée doit creuser le déficit");

            Brokerage.EnforceMarginCall(state, def, tycoon, sim.World.Tick);
            var call = state.Journal.Single(r => r.Kind == MarketRecordKind.MarginCall);
            Check.True(call.Rounds <= 20, $"{call.Rounds} tours pour une borne de 20");
            long left = tycoon.Portfolio.SharesOf(player.Id);
            Check.True(left < 29_000 && tycoon.MarginLoan <= Money.Round(0.7m * tycoon.PortfolioMarketValue(state)),
                $"la cascade devait vendre sous le seuil de 29 % et rendre le compte conforme : {left} titres, dette {tycoon.MarginLoan}");
            Check.True(!tycoon.Ruined, "la vente couvre la dette : pas de ruine");
            Check.True(tycoon.Book.Residual == 0m, "bilan du magnat au centime");

            // Et la borne tient : à deux tours par jour, la cascade s'arrête le soir et
            // reprend le lendemain, sans jamais dépasser sa borne.
            var (bounded, tb) = Leveraged(founderShares: 40_000, loan: 235_000m, crashTo: 8.00m, rounds: 2);
            var (sb, db, _, _) = Parts(bounded);
            Brokerage.EnforceMarginCall(sb, db, tb, bounded.World.Tick);
            Check.True(sb.Journal.Single(r => r.Kind == MarketRecordKind.MarginCall).Rounds == 2,
                "la cascade doit s'arrêter à sa borne quotidienne");
        });

        // ------------------------------------------------------------- ruine

        runner.Add("bourse — la ruine : tout vendu, caisse saisie, dette résiduelle constatée, bilans au centime", () =>
        {
            var (sim, tycoon) = Leveraged(founderShares: 40_000, loan: 300_000m, crashTo: 8.00m, rounds: 20);
            var (state, def, player, _) = Parts(sim);
            decimal loan = tycoon.MarginLoan;
            decimal cashBefore = tycoon.Cash;
            Brokerage.EnforceMarginCall(state, def, tycoon, sim.World.Tick);

            Check.True(tycoon.Ruined && tycoon.RuinTick == sim.World.Tick.Index && tycoon.RuinCount == 1, "le magnat doit être ruiné ce jour");
            Check.True(tycoon.Portfolio.SharesOf(player.Id) == 0 && player.Register.HeldBy(Holders.Tycoon) == 0, "tous ses titres vendus");
            Check.True(tycoon.Cash == 0m && tycoon.MarginLoan == 0m, $"caisse {tycoon.Cash}, marge {tycoon.MarginLoan} : saisies et versées dans la dette résiduelle");

            // Aucun centime ne disparaît : ce qui était dû est remboursé par la vente et
            // la caisse, ou constaté en dette résiduelle.
            decimal proceeds = tycoon.RealizedResultTotal + 40_000 * 10.10m;
            Check.True(proceeds + cashBefore + tycoon.ResidualDebt == loan,
                $"vente {proceeds} + caisse {cashBefore} + dette résiduelle {tycoon.ResidualDebt} ≠ dette de marge {loan}");
            Check.True(tycoon.ResidualDebt == tycoon.ResidualDebtAtRuin && tycoon.NetWorth(state) == -tycoon.ResidualDebt,
                "la fortune d'un ruiné est l'opposé de sa dette résiduelle");
            Check.True(tycoon.Book.Residual == 0m && player.Book.Residual == 0m, "bilans au centime");
            Check.True(state.Journal.Any(r => r.Kind == MarketRecordKind.Ruin && r.Amount == tycoon.ResidualDebt), "la ruine est au journal public");
            Check.True(Invariants.Check(sim.World, 0).All(v => v.Rule != "bilan-actif-passif" && v.Rule != "dette-emprunts" && v.Rule != "ruine-explicite"),
                "les invariants financiers doivent tenir après la ruine");

            // Et le lendemain, rien ne recommence : ni appel de marge, ni intérêts.
            int calls = tycoon.MarginCalls;
            decimal accrued = tycoon.InterestAccruedTotal;
            sim.Run(30);
            Check.True(tycoon.MarginCalls == calls && tycoon.InterestAccruedTotal == accrued && tycoon.Ruined,
                "un ruiné n'a plus de compte sur marge : ni appel, ni intérêts");
        });

        runner.Add("bourse — un ruiné ne peut rien acheter ; ses revenus remboursent d'abord sa dette ; il sort de la ruine le jour exact où elle tombe à zéro", () =>
        {
            var (sim, tycoon) = Leveraged(founderShares: 40_000, loan: 300_000m, crashTo: 8.00m, rounds: 20);
            var (state, def, player, _) = Parts(sim);
            Brokerage.EnforceMarginCall(state, def, tycoon, sim.World.Tick);
            Check.True(tycoon.Ruined, "préalable : ruiné");
            decimal residual = tycoon.ResidualDebt;
            int tick = sim.World.Tick.Index;

            // Il ne peut rien acheter, même si une caisse lui restait.
            tycoon.Book.Post("caisse hors saisie (essai)", Accounts.Cash, Accounts.PersonalEquity, 50_000m);
            Check.True(Brokerage.BuyShares(state, def, tycoon, player) == 0, "un ruiné ne doit rien acheter");
            tycoon.Book.Post("retrait de l'essai", Accounts.PersonalEquity, Accounts.Cash, 50_000m);

            // Un revenu va d'abord à la dette ; il reste ruiné tant qu'un centime est dû.
            Brokerage.ReceiveIncome(state, tycoon, "revenu", residual - 0.01m, tick);
            Check.True(tycoon.Ruined && tycoon.ResidualDebt == 0.01m && tycoon.Cash == 0m,
                $"un centime dû : encore ruiné (dette {tycoon.ResidualDebt}, caisse {tycoon.Cash})");
            Brokerage.ReceiveIncome(state, tycoon, "revenu", 5.00m, tick + 1);
            Check.True(!tycoon.Ruined && tycoon.ResidualDebt == 0m && tycoon.Cash == 4.99m && tycoon.RuinClearedTick == tick + 1,
                "dette soldée : sorti de la ruine le jour même, le reste du revenu en caisse");
            Check.True(state.Journal.Any(r => r.Kind == MarketRecordKind.RuinCleared && r.Tick == tick + 1), "la sortie est au journal public");
            Check.True(tycoon.SeizedTotal == residual && tycoon.Book.Residual == 0m, "tout ce qui a été saisi a remboursé la dette, au centime");

            // Et il retrouve le droit d'acheter.
            Brokerage.ReceiveIncome(state, tycoon, "revenu", 20_000m, tick + 2);
            Check.True(Brokerage.BuyShares(state, def, tycoon, player) > 0, "sorti de la ruine, il doit pouvoir acheter");
        });

        runner.Add("bourse — un ruiné : son salaire de président et ses dividendes remboursent sa dette, jusqu'au jour où elle est soldée", () =>
        {
            // heartland-finance, magnat mis en ruine à la main au tick 60 avec une dette
            // de 20 000, et à qui l'on confie 1 000 titres : le salaire et le premier
            // dividende doivent aller au courtier jusqu'à ce que la dette soit soldée.
            var sim = new Simulation(ScenarioLoader.Load(DataPath("heartland-finance.json")));
            sim.Run(60);
            var (state, def, player, tycoon) = Parts(sim);
            ForceRuin(sim, 20_000m);
            Check.True(tycoon.Ruined && tycoon.ResidualDebt == 20_000m, "préalable : ruiné, 20 000 dus");

            // 1 000 titres confiés (écriture équilibrée) : de quoi toucher un dividende.
            long gift = 1_000;
            player.Register.Transfer(Holders.Float, Holders.Tycoon, gift);
            tycoon.Portfolio.Add(player.Id, gift, gift * player.SharePrice);
            tycoon.Book.Post("titres confiés (essai)", Accounts.Portfolio, Accounts.PersonalEquity, gift * player.SharePrice);
            // Il ne vend pas ses titres : on isole ses revenus. Et le salaire tombe tous
            // les 31 jours, pas le jour d'un dividende (tous les 90) : chaque revenu doit
            // être saisi pour lui-même, pas balayé par la saisie de l'autre.
            def.Tycoon.SellAboveCostRatio = 1_000m;
            def.CeoPay.IntervalTicks = 31;

            decimal debt = tycoon.ResidualDebt;
            bool sawDividend = false, sawSalary = false;
            int clearedAt = -1;
            for (int i = 0; i < 1400 && clearedAt < 0; i++)
            {
                decimal pay = tycoon.PayReceivedTotal, dividends = tycoon.DividendsReceivedTotal;
                sim.Step();
                decimal income = tycoon.PayReceivedTotal - pay + tycoon.DividendsReceivedTotal - dividends;
                if (tycoon.PayReceivedTotal > pay) sawSalary = true;
                if (tycoon.DividendsReceivedTotal > dividends) sawDividend |= tycoon.Ruined || tycoon.RuinClearedTick == sim.World.Tick.Index;
                if (tycoon.Ruined)
                {
                    Check.True(tycoon.Cash == 0m && tycoon.ResidualDebt == debt - income,
                        $"tick {sim.World.Tick.Index} : revenu {income} non versé à la dette ({debt} → {tycoon.ResidualDebt}, caisse {tycoon.Cash})");
                    Check.True(tycoon.ResidualDebt > 0m, "ruiné sans dette");
                }
                else
                {
                    clearedAt = sim.World.Tick.Index;
                    Check.True(debt <= income && tycoon.ResidualDebt == 0m && tycoon.RuinClearedTick == clearedAt,
                        $"sorti de la ruine au tick {clearedAt} avec {debt} dus et {income} de revenu");
                }
                debt = tycoon.ResidualDebt;
                Check.True(tycoon.Book.Residual == 0m && player.Book.Residual == 0m, "bilans au centime");
            }
            Check.True(sawSalary && sawDividend, $"salaire {sawSalary}, dividende {sawDividend} : les deux revenus devaient jouer");
            Check.True(clearedAt > 0, "la dette devait être soldée par ses revenus");
        });

        runner.Add("bourse — un ruiné ne renfloue pas sa compagnie", () =>
        {
            // La compagnie qui coule, magnat décidé à renflouer — mais ruiné, avec une
            // caisse posée à la main pour que seul l'état de ruine l'en empêche.
            var sim = new Simulation(SinkingWithRescue());
            sim.Step();
            var (_, _, player, tycoon) = Parts(sim);
            ForceRuin(sim, 1_000m);
            tycoon.Book.Post("caisse hors saisie (essai)", Accounts.Cash, Accounts.PersonalEquity, 50_000m);
            sim.Run(300);
            Check.True(player.OverdraftBalance > 0m || player.InReceivership, "la compagnie devait passer à découvert");
            Check.True(player.CapitalRaisedTotal == 0m && player.SharesIssuedInRescues == 0, "un ruiné ne doit rien apporter");

            // Le même, solvable, renfloue : sinon le test ne prouverait rien.
            var healthy = new Simulation(SinkingWithRescue());
            healthy.Run(300);
            Check.True(healthy.World.Finance.Player!.CapitalRaisedTotal > 0m, "un magnat solvable renfloue");
        });

        // ------------------------------------------------ prises de bénéfices

        runner.Add("bourse — une montée brutale déclenche les prises de bénéfices : le cours baisse, les actions se conservent", () =>
        {
            var sim = new Simulation(Stable(f =>
                f.Market.ProfitTaking = new ProfitTakingDef { Enabled = true, ThresholdPercent = 15m, ReferenceTicks = 30, Speed = 0.5m, MaxSellFraction = 0.5m }));
            sim.Step();
            var (state, def, player, tycoon) = Parts(sim);

            // Un gros achat du magnat porte le cours bien au-delà de 15 %.
            Brokerage.BuyShares(state, def, tycoon, player);
            decimal pumped = player.SharePrice;
            Check.True(pumped > 10.10m * 1.15m, $"le test veut une montée brutale : {pumped}");
            var holdings = player.Register.Holders.ToDictionary(h => h.HolderId, h => h.Shares);
            long issued = player.Register.SharesIssued;

            long sold = StockMarket.TakeProfits(state, def, player, sim.World.Tick.Index);
            decimal trigger = Money.Round(10.10m * 1.15m);
            Check.True(sold > 0, "les investisseurs devaient vendre");
            Check.Less((double)player.SharePrice, (double)pumped, "les prises de bénéfices doivent faire baisser le cours");
            Check.True(player.SharePrice <= pumped - 0.5m * (pumped - trigger) && player.SharePrice > trigger,
                $"la moitié de l'excès refermée, sans passer sous le seuil : {pumped} → {player.SharePrice} (seuil {trigger})");
            Check.True(player.Register.SharesIssued == issued && player.Register.HeldTotal() == issued,
                "aucune action créée ni détruite");
            foreach (var h in player.Register.Holders)
                Check.True(h.Shares == holdings[h.HolderId], $"{h.HolderId} : {holdings[h.HolderId]} → {h.Shares} ; le flottant vend au flottant");
            Check.True(player.ProfitTakingShares == sold && state.Journal.Any(r => r.Kind == MarketRecordKind.ProfitTaking && r.Shares == sold),
                "la cession est au journal public");
            Check.True(tycoon.Book.Residual == 0m && player.Book.Residual == 0m, "aucun bilan tenu ne bouge");

            // Le soir même, la fortune du magnat porte le cours d'après les ventes : sans
            // prises de bénéfices, elle garderait le cours que son ordre a fabriqué.
            var noProfit = new Simulation(Stable());
            noProfit.Step();
            var (s2, d2, p2, t2) = Parts(noProfit);
            Brokerage.BuyShares(s2, d2, t2, p2);
            Check.Less((double)tycoon.NetWorth(state), (double)t2.NetWorth(s2),
                "un achat qui fait monter le cours doit moins enrichir le soir même quand les autres prennent leurs gains");
        });

        runner.Add("bourse — pas de prises de bénéfices sur une baisse, ni sur un cours qui rattrape sa valeur", () =>
        {
            var sim = new Simulation(Stable(f =>
                f.Market.ProfitTaking = new ProfitTakingDef { Enabled = true, ThresholdPercent = 15m, ReferenceTicks = 30, Speed = 0.5m, MaxSellFraction = 0.5m }));
            sim.Step();
            var (state, def, player, _) = Parts(sim);

            // Un krach : le cours était à 30, il tombe à 20 ; sa valeur est 10,10. Le cours
            // reste bien au-dessus de sa valeur, mais il baisse : personne ne prend de
            // bénéfices sur une chute, la référence des investisseurs l'a suivie.
            player.SharePrice = 30m;
            StockMarket.UpdateReferences(def, player, 10.10m);
            player.ReferencePrice = 30m;
            player.SharePrice = 20m;
            StockMarket.UpdateReferences(def, player, 10.10m);
            Check.True(player.ReferencePrice == 20m, $"la référence suit une baisse le jour même : {player.ReferencePrice}");
            Check.True(StockMarket.TakeProfits(state, def, player, 2) == 0 && player.SharePrice == 20m,
                "une baisse ne déclenche pas de prises de bénéfices");

            // Un rattrapage : la valeur est montée à 20, le cours passe de 10 à 13. Il a
            // monté de 30 % au-dessus de la référence, mais reste sous sa valeur.
            player.SharePrice = 10m;
            player.ReferencePrice = 10m;
            player.SharePrice = 13m;
            StockMarket.UpdateReferences(def, player, 20m);
            Check.True(StockMarket.TakeProfits(state, def, player, 3) == 0 && player.SharePrice == 13m,
                "un cours qui rattrape sa valeur ne déclenche pas de prises de bénéfices");
        });

        // -------------------------------------------------------- président

        runner.Add("président — le salaire suit la performance, et la prime ne tombe qu'en cas de surperformance", () =>
        {
            var pay = new CeoPayDef { TargetPercent = 8m, Sensitivity = 10m, MinFactor = 0.5m, MaxFactor = 2m, BonusThresholdPercent = 12m, BonusSharePercent = 2m };
            Check.True(CeoPay.Factor(pay, 0.08m, 1m) == 1m, "à la cible, le salaire de base");
            Check.True(CeoPay.Factor(pay, 0.10m, 1m) == 1.2m && CeoPay.Factor(pay, 0.05m, 1m) == 0.7m, "dix pour cent par point d'écart");
            Check.True(CeoPay.Factor(pay, 0.50m, 1m) == 2m && CeoPay.Factor(pay, -0.45m, 1m) == 0.5m, "dans les bornes");
            Check.True(CeoPay.Factor(pay, 0.20m, 0m) == 0.5m, "une compagnie sans valeur ne se rapporte à rien : le plancher");
            Check.True(CeoPay.Bonus(pay, 0.12m, 1_000_000m) == 0m && CeoPay.Bonus(pay, 0.10m, 1_000_000m) == 0m,
                "pas de prime au seuil ni en deçà");
            Check.True(CeoPay.Bonus(pay, 0.15m, 1_000_000m) == 600m, "2 % des 30 000 obtenus au-delà du seuil");

            // Sur six ans de heartland-cycle : chaque clôture révise le salaire selon la
            // performance, et une prime tombe exactement les années au-dessus du seuil.
            var sim = new Simulation(ScenarioLoader.Load(DataPath("heartland-cycle.json")));
            sim.Run(CycleTests.Ticks);
            var reviews = sim.World.Finance.Journal.Where(r => r.Kind == MarketRecordKind.PayReview).ToList();
            Check.True(reviews.Count == 6 && reviews.All(r => r.Tick % SimTick.TicksPerYear == SimTick.TicksPerYear - 1),
                $"une clôture par an, le 30 décembre : {string.Join(",", reviews.Select(r => r.Tick))}");
            foreach (var r in reviews)
            {
                Check.True((r.Bonus > 0m) == (r.Rate > 0.12m), $"tick {r.Tick} : performance {r.Rate}, prime {r.Bonus}");
                decimal factor = CeoPay.Factor(sim.World.Def.Finance.CeoPay, r.Rate, 1m);
                Check.Near((double)Money.Round(12_000m * factor), (double)r.Amount, 1.0,
                    $"tick {r.Tick} : salaire révisé {r.Amount} pour une performance {r.Rate} (arrondie au millionième au journal)");
            }
            Check.True(reviews.Any(r => r.Bonus > 0m) && reviews.Any(r => r.Bonus == 0m), "il faut des années avec et sans prime");

            // Et c'est bien ce salaire révisé qui est versé : douze mois au facteur de
            // l'exercice (1 la première année), au centime près par mois — le journal
            // arrondit la performance au millionième.
            var payDef = sim.World.Def.Finance.CeoPay;
            decimal expected = 0m;
            decimal yearFactor = 1m;
            for (int t = 1; t <= CycleTests.Ticks; t++)
            {
                if (t % payDef.IntervalTicks == 0)
                    expected += Money.Round(payDef.BaseAnnual * yearFactor * payDef.IntervalTicks / SimTick.TicksPerYear);
                var review = reviews.FirstOrDefault(r => r.Tick == t);
                if (review is not null) yearFactor = CeoPay.Factor(payDef, review.Rate, 1m);
            }
            var company = sim.World.Finance.Player!;
            Check.Near((double)expected, (double)company.SalaryAccruedTotal, 0.01 * CycleTests.Ticks / payDef.IntervalTicks,
                $"salaire constaté {company.SalaryAccruedTotal}, attendu {expected} aux facteurs révisés");
        });

        runner.Add("président — le salaire sort de la caisse de la compagnie vers celle du magnat, au centime, et le transporteur le voit", () =>
        {
            var sim = new Simulation(ScenarioLoader.Load(DataPath("heartland-finance.json")));
            var (_, _, player, tycoon) = Parts(sim);
            var co = sim.World.Company;
            double initial = Invariants.InitialStockTotal(sim.World);
            int salaryDays = 0;
            for (int i = 0; i < 720; i++)
            {
                decimal paid = player.SalaryPaidTotal;
                double flow = co.TotalFinanceFlow;
                decimal dividend = player.DividendsPaidTotal;
                sim.Step();
                var violations = Invariants.Check(sim.World, initial);
                Check.True(violations.Count == 0, $"tick {sim.World.Tick.Index} : {(violations.Count > 0 ? violations[0].Rule + " — " + violations[0].Detail : "")}");
                if (player.SalaryPaidTotal > paid && player.DividendsPaidTotal == dividend)
                {
                    salaryDays++;
                    // Le jour d'un salaire sans dividende, le flux financier du transporteur
                    // baisse au moins du salaire (d'autres flux peuvent s'y ajouter).
                    Check.True(flow - co.TotalFinanceFlow >= (double)(player.SalaryPaidTotal - paid) - 0.01 || player.OverdraftBalance > 0m,
                        $"tick {sim.World.Tick.Index} : un salaire de {player.SalaryPaidTotal - paid} ne sort pas de la trésorerie");
                }
            }
            Check.True(salaryDays >= 12, $"{salaryDays} jours de salaire sans dividende sur deux ans");
            Check.True(player.SalaryAccruedTotal + player.BonusAccruedTotal == player.SalaryPaidTotal + player.Book[Accounts.SalaryPayable]
                       && tycoon.PayReceivedTotal == player.SalaryPaidTotal,
                "constaté = payé + dû, et payé = reçu, au centime");
        });

        runner.Add("président — le salaire attend la caisse : il ne met pas la compagnie à découvert", () =>
        {
            // La compagnie qui coule, avec un président payé : le découvert doit rester
            // exactement le déficit d'exploitation, le salaire impayé rester dû.
            var scenario = SinkingWithRescue(rescues: false);
            scenario.Finance.CeoPay = new CeoPayDef { Enabled = true, BaseAnnual = 24_000m };
            var sim = new Simulation(scenario);
            var (state, _, player, _) = Parts(sim);
            for (int i = 0; i < 240; i++)
            {
                sim.Step();
                decimal shortfall = state.ReflectedOperatingCash < 0m ? -state.ReflectedOperatingCash : 0m;
                Check.True(player.OverdraftBalance == shortfall,
                    $"tick {sim.World.Tick.Index} : découvert {player.OverdraftBalance} pour un déficit d'exploitation de {shortfall}");
            }
            Check.True(player.SalaryAccruedTotal > player.SalaryPaidTotal && player.Book[Accounts.SalaryPayable] > 0m,
                "une part du salaire doit être restée due");
        });

        // ------------------------------------------------ division d'actions

        runner.Add("bourse — la division double chaque position, divise le cours, et ne change ni capitalisation ni fortune", () =>
        {
            // heartland-finance au tick 45 : le magnat détient des titres de sa compagnie,
            // la compagnie une participation dans Great Plains. On divise les deux.
            var sim = new Simulation(ScenarioLoader.Load(DataPath("heartland-finance.json")));
            sim.Run(45);
            var (state, _, player, tycoon) = Parts(sim);
            var target = state.CompanyById("greatplains")!;
            Check.True(player.Investments.SharesOf(target.Id) > 0 && tycoon.Portfolio.SharesOf(player.Id) > 0,
                "le test veut une participation d'une compagnie et une ligne du magnat");
            // Un cours aux centimes impairs : la division laisse un demi-centime.
            if (player.SharePrice * 100m % 2m == 0m) player.SharePrice += 0.01m;

            foreach (var company in new[] { player, target })
            {
                var holders = company.Register.Holders.ToDictionary(h => h.HolderId, h => h.Shares);
                long issued = company.Register.SharesIssued;
                decimal price = company.SharePrice, cap = company.MarketCap;
                decimal worth = tycoon.NetWorth(state), basis = tycoon.Portfolio.CostBasisOf(company.Id);
                decimal invested = player.Investments.CostBasisOf(company.Id);
                long mine = tycoon.Portfolio.SharesOf(company.Id), stake = player.Investments.SharesOf(company.Id);
                decimal reference = company.ReferencePrice, intrinsic = company.IntrinsicPrice;

                StockMarket.Split(state, company, 2, sim.World.Tick.Index);

                Check.True(company.Register.SharesIssued == 2 * issued && company.Register.HeldTotal() == company.Register.SharesIssued,
                    $"{company.Id} : actions émises {issued} → {company.Register.SharesIssued}");
                foreach (var h in company.Register.Holders)
                    Check.True(h.Shares == 2 * holders[h.HolderId], $"{company.Id} : {h.HolderId} {holders[h.HolderId]} → {h.Shares}");
                Check.True(company.SharePrice * 2 == price && company.MarketCap == cap,
                    $"{company.Id} : cours {price} → {company.SharePrice}, capitalisation {cap} → {company.MarketCap}");
                Check.True(tycoon.NetWorth(state) == worth, $"fortune du magnat {worth} → {tycoon.NetWorth(state)}");
                Check.True(tycoon.Portfolio.SharesOf(company.Id) == 2 * mine && tycoon.Portfolio.CostBasisOf(company.Id) == basis,
                    "la ligne du magnat double, son prix de revient total ne bouge pas");
                Check.True(player.Investments.SharesOf(company.Id) == 2 * stake && player.Investments.CostBasisOf(company.Id) == invested,
                    "la participation d'une compagnie double, son prix de revient ne bouge pas");
                Check.True(company.ReferencePrice * 2 == reference && company.IntrinsicPrice * 2 == intrinsic && company.SplitFactor == 2,
                    "les références par action suivent la division");
            }
            Check.True(state.Journal.Count(r => r.Kind == MarketRecordKind.Split) == 2, "deux divisions au journal public");

            // La partie continue : bilans, registres et invariants tiennent.
            double initial = Invariants.InitialStockTotal(sim.World);
            for (int i = 0; i < 120; i++)
            {
                sim.Step();
                var violations = Invariants.Check(sim.World, initial).Where(v => v.Rule != "conservation-matiere").ToList();
                Check.True(violations.Count == 0, $"tick {sim.World.Tick.Index} : {(violations.Count > 0 ? violations[0].Rule + " — " + violations[0].Detail : "")}");
            }
        });

        runner.Add("bourse — un cours qui finit l'année au-dessus de 100 est divisé le 30 décembre, sans que la fortune bouge", () =>
        {
            // Une compagnie riche et immobile : 1,5 million d'actif pour 10 000 actions,
            // un cours de 151 qui ne bouge pas. Le magnat ne passe pas d'ordre.
            var scenario = Stable(f =>
            {
                f.SharesIssued = 10_000;
                f.FixedAssetsAtStart = 1_500_000m;
                f.Tycoon.StartingShares = 3_000;
                f.Market.Split = new StockSplitDef { Enabled = true, PriceThreshold = 100m, Ratio = 2 };
            });
            var sim = new Simulation(scenario);
            var (state, _, player, tycoon) = Parts(sim);
            decimal worth = 0m;
            for (int t = 1; t <= 800; t++)
            {
                sim.Step();
                if (t == 1) worth = tycoon.NetWorth(state);
                Check.True(tycoon.NetWorth(state) == worth, $"tick {t} : fortune {worth} → {tycoon.NetWorth(state)}");
                if (t == 358) Check.True(player.SplitFactor == 1 && player.SharePrice > 100m, "pas de division avant la clôture");
                if (t == 359) Check.True(player.SplitFactor == 2 && player.Register.SharesIssued == 20_000, "division à la clôture de l'exercice");
            }
            var splits = state.Journal.Where(r => r.Kind == MarketRecordKind.Split).ToList();
            Check.True(splits.Count == 1 && splits[0].Tick == 359 && splits[0].Shares == 2,
                "une seule division : l'année suivante, le cours divisé reste sous 100");
            Check.True(player.SharePrice < 100m && tycoon.Portfolio.SharesOf(player.Id) == 6_000, "cours divisé, ligne doublée");
        });

        // -------------------------------------------------------- neutralité

        runner.Add("bourse — sans les nouveaux blocs, heartland-finance et heartland-cycle rejouent la partie d'avant au bit près", () =>
        {
            // Les empreintes d'avant la bourse solide, avec le résumé financier d'alors :
            // la ligne de crédit au seuil de maintien, une passe d'appel de marge, un
            // carnet infini, ni prises de bénéfices, ni salaire, ni division. La ruine,
            // elle, est un état nouveau ; aucune de ces parties n'y tombe.
            var cases = new (string File, string Solver, int Ticks, string Expected)[]
            {
                ("heartland-finance.json", "reference", 720, "227CB2EF4504BDBE"),
                ("heartland-cycle.json", "reference", CycleTests.Ticks, "E8210450987FCE4E"),
                ("heartland-cycle.json", "anticipating", CycleTests.Ticks, "3CA30C95E38BE915"),
            };
            foreach (var (file, solver, ticks, expected) in cases)
            {
                var scenario = ScenarioLoader.Load(DataPath(file));
                Check.True(scenario.Finance.Market.ProfitTaking.Enabled && scenario.Finance.CeoPay.Enabled,
                    $"{file} doit livrer la bourse solide, sinon ce test ne prouve rien");
                scenario.Finance.Market = new StockMarketDef();
                scenario.Finance.CeoPay = new CeoPayDef();
                scenario.Finance.Tycoon.CreditLineRatio = null;
                scenario.Finance.Tycoon.MarginCallMaxRounds = 1;
                var sim = new Simulation(scenario, solver == "reference" ? new ReferenceEconomySolver() : new AnticipatingEconomySolver());
                var recorder = new CsvRecorder();
                recorder.Record(sim.World);
                for (int i = 0; i < ticks; i++)
                {
                    sim.Step();
                    recorder.Record(sim.World);
                }
                Check.True(!sim.World.Finance.Magnate!.Ruined && sim.World.Finance.Magnate.RuinCount == 0, $"{file} : aucune ruine attendue");
                Check.Equal(expected, recorder.TraceFingerprint(OldFinanceSummary(sim.World) + sim.World.Events.Summary() + sim.World.Cycle.Summary()),
                    $"{file} sans les nouveaux blocs (solveur {solver})");
            }
        });

        runner.Add("bourse — un scénario sans finance n'a ni journal de bourse ni statistique financière", () =>
        {
            foreach (string file in new[] { "heartland.json", "heartland-events.json", "sierra.json" })
            {
                var sim = new Simulation(ScenarioLoader.Load(DataPath(file)));
                var stats = new FinanceStatistics { WarmupTicks = 0 };
                for (int i = 0; i < 60; i++)
                {
                    sim.Step();
                    stats.Sample(sim.World);
                }
                Check.True(!sim.World.Finance.Enabled && sim.World.Finance.Journal.Count == 0 && sim.World.Finance.JournalSummary() == "",
                    $"{file} : la bourse ne doit rien écrire");
                Check.True(stats.Samples == 0, $"{file} : aucune statistique financière sans finance");
                Check.Equal("tick,kind,company,shares,price_before,price_after,amount,rate,rounds,bonus\n", CsvRecorder.FinanceJournalCsv(sim.World),
                    $"{file} : bourse.csv réduit à son en-tête");
            }
        });

        runner.Add("bourse — statistiques moyennées : la fortune se lit en moyenne, et le saut d'un soir se voit", () =>
        {
            // La dette n° 2 : une moyenne dans le temps, un écart-type, et le plus grand
            // saut d'un soir au suivant — ce que la fortune du dernier soir ne dit pas.
            var sim = new Simulation(ScenarioLoader.Load(DataPath("heartland-finance.json")));
            var stats = new FinanceStatistics { WarmupTicks = 90 };
            var worths = new List<double>();
            for (int i = 0; i < 720; i++)
            {
                sim.Step();
                stats.Sample(sim.World);
                if (sim.World.Tick.Index >= 90) worths.Add((double)sim.World.Finance.Magnate!.NetWorth(sim.World.Finance));
            }
            Check.True(stats.Samples == worths.Count, $"{stats.Samples} soirs relevés pour {worths.Count}");
            Check.Near(worths.Average(), stats.NetWorth.Mean, 1e-6 * Math.Abs(worths.Average()), "moyenne de la fortune");
            double jump = worths.Zip(worths.Skip(1), (a, b) => Math.Abs(b - a)).Max();
            Check.Near(jump, stats.LargestJump, 1e-6, "plus grand saut d'un soir au suivant");
            Check.True(stats.NetWorth.StdDev > 0 && stats.NetWorth.Min <= stats.NetWorth.Mean && stats.NetWorth.Mean <= stats.NetWorth.Max,
                "écart-type et bornes cohérents");
        });

        // -------------------------------------------------------- validation

        runner.Add("bourse — des réglages absurdes sont refusés au chargement", () =>
        {
            void Refused(string why, Action<FinanceDef> tweak)
            {
                var scenario = Stable(tweak);
                Check.Throws<InvalidDataException>(() => new Simulation(scenario), why);
            }
            Refused("ligne de crédit au-delà du seuil d'appel", f => f.Tycoon.CreditLineRatio = 0.8m);
            Refused("division 3 pour 1", f => f.Market.Split = new StockSplitDef { Enabled = true, Ratio = 3 });
            Refused("division 1 pour 1", f => f.Market.Split = new StockSplitDef { Enabled = true, Ratio = 1 });
            Refused("vitesse nulle", f => f.Market.ProfitTaking = new ProfitTakingDef { Enabled = true, Speed = 0m });
            Refused("prises de bénéfices sans impact", f =>
            {
                f.Valuation.MarketImpact = 0m;
                f.Market.ProfitTaking = new ProfitTakingDef { Enabled = true };
            });
            Refused("tranche au-delà du capital", f => f.Market.BookTrancheFraction = 1.5m);
            Refused("aucun tour d'appel de marge", f => f.Tycoon.MarginCallMaxRounds = 0);
            Refused("base de performance inconnue", f => f.CeoPay = new CeoPayDef { Enabled = true, PerformanceBase = "cours" });
            Check.True(new Simulation(Stable(f => f.Market.Split = new StockSplitDef { Enabled = true, Ratio = 10 })).World.Finance.Enabled,
                "une division 10 pour 1 s'écrit exactement : acceptée");
        });
    }

    // --------------------------------------------------------------- outils

    private static (FinanceState State, FinanceDef Def, FinanceCompany Player, Tycoon Tycoon) Parts(Simulation sim)
    {
        var state = sim.World.Finance;
        return (state, state.Def, state.Player!, state.Magnate!);
    }

    /// <summary>
    /// Une compagnie stable : sans train ni résultat, un cours égal à son actif net par
    /// action (10,10), ramené chaque soir à sa valeur. Le magnat détient 20 000 titres
    /// et 100 000 en caisse, et ne passe d'ordre que quand le test le lui demande. Un
    /// carnet à tranches de 1 % du capital, un impact de 1,5.
    /// </summary>
    private static ScenarioDef Stable(Action<FinanceDef>? tweak = null)
    {
        var scenario = Fixtures.Bare("bourse-stable");
        scenario.StartingCash = 10_000;
        scenario.Finance = new FinanceDef
        {
            Enabled = true,
            CompanyId = "player",
            CompanyName = "Compagnie stable",
            SharesIssued = 100_000,
            FixedAssetsAtStart = 1_000_000m,
            DepreciationAnnualPercent = 0m,
            BorrowWhenCashBelow = 0m,
            Valuation = new ValuationDef
            {
                EarningsMultiple = 0m,
                PriceSmoothing = 1m,
                EarningsSmoothing = 1m,
                MinSharePrice = 0.01m,
                MarketImpact = 1.5m,
            },
            Dividends = new DividendPolicyDef { IntervalTicks = 0 },
            Tycoon = new TycoonPolicyDef
            {
                StartingCash = 100_000m,
                StartingShares = 20_000,
                TradeIntervalTicks = 0,
                BuyBudgetFraction = 0.6m,
                MarginInitialRatio = 0.65m,
                MarginMaintenanceRatio = 0.7m,
                MarginAnnualRatePercent = 9m,
                SellAboveCostRatio = 99m,
                SellFraction = 0.25m,
                MinLotShares = 100,
            },
            Market = new StockMarketDef { BookTrancheFraction = 0.01m },
        };
        tweak?.Invoke(scenario.Finance);
        return scenario;
    }

    /// <summary>
    /// Un magnat fondateur de <paramref name="founderShares"/> titres (au cours de
    /// 10,10), endetté sur marge de <paramref name="loan"/> — un emprunt posé à la main,
    /// dont le produit a quitté sa caisse —, puis le cours tombé à
    /// <paramref name="crashTo"/>. Il ne reste qu'à appeler la marge.
    /// </summary>
    private static (Simulation Sim, Tycoon Tycoon) Leveraged(long founderShares, decimal loan, decimal crashTo, int rounds)
    {
        var sim = new Simulation(Stable(f =>
        {
            f.Tycoon.StartingShares = founderShares;
            f.Tycoon.StartingCash = 0m;
            f.Tycoon.MarginCallMaxRounds = rounds;
        }));
        sim.Step();
        var (_, _, player, tycoon) = Parts(sim);
        tycoon.Book.Post("emprunt sur marge", Accounts.Cash, Accounts.MarginLoan, loan);
        tycoon.Book.Post("dépense hors modèle", Accounts.PersonalEquity, Accounts.Cash, loan);
        player.SharePrice = crashTo;
        return (sim, tycoon);
    }

    /// <summary>
    /// Met le magnat en ruine à la main : il vend tous ses titres, sa caisse et sa marge
    /// sortent du modèle par des écritures équilibrées, il lui reste une dette de marge de
    /// <paramref name="residual"/>, et le courtier constate la ruine.
    /// </summary>
    private static void ForceRuin(Simulation sim, decimal residual)
    {
        var (state, def, player, tycoon) = Parts(sim);
        int tick = sim.World.Tick.Index;
        long held = tycoon.Portfolio.SharesOf(player.Id);
        if (held > 0) Brokerage.SellShares(state, def, tycoon, player, held, tick);
        if (tycoon.MarginLoan > 0m)
            tycoon.Book.Post("marge soldée hors modèle", Accounts.MarginLoan, Accounts.PersonalEquity, tycoon.MarginLoan);
        if (tycoon.Book[Accounts.AccruedPayable] > 0m)
        {
            tycoon.InterestPaidTotal += tycoon.Book[Accounts.AccruedPayable];
            tycoon.Book.Post("intérêts soldés hors modèle", Accounts.AccruedPayable, Accounts.PersonalEquity, tycoon.Book[Accounts.AccruedPayable]);
        }
        if (tycoon.Cash > 0m) tycoon.Book.Post("caisse sortie du modèle", Accounts.PersonalEquity, Accounts.Cash, tycoon.Cash);
        tycoon.Book.Post("emprunt sur marge", Accounts.Cash, Accounts.MarginLoan, residual);
        tycoon.Book.Post("dépense hors modèle", Accounts.PersonalEquity, Accounts.Cash, residual);
        Brokerage.DeclareRuin(state, tycoon, tick);
    }

    /// <summary>La compagnie qui coule de <see cref="FinanceTests"/>, reconstruite ici : des trains qui brûlent 960 par tick, un magnat qui renfloue (ou non).</summary>
    private static ScenarioDef SinkingWithRescue(bool rescues = true)
    {
        var scenario = Fixtures.Bare("compagnie-qui-coule");
        scenario.StartingCash = 20_000;
        scenario.Cities.Add(Fixtures.City("ferme", production: 2.0, stock: 200));
        scenario.Cities.Add(Fixtures.City("ville", demand: 0.4));
        scenario.Lines.Add(new LineDef
        {
            Id = "l1", Name = "l1",
            Stops = { new StopDef { City = "ferme", DistanceKm = 0 }, new StopDef { City = "ville", DistanceKm = 100 } },
        });
        scenario.Trains.Add(new TrainDef { Id = "t1", Line = "l1", Capacity = 24, SpeedKmPerTick = 120, CostPerKm = 8.0 });
        scenario.Finance = new FinanceDef
        {
            Enabled = true,
            SharesIssued = 100_000,
            FixedAssetsAtStart = 150_000m,
            DepreciationAnnualPercent = 0m,
            BorrowWhenCashBelow = 0m,
            Valuation = new ValuationDef { EarningsMultiple = 0m, PriceSmoothing = 0.5m, EarningsSmoothing = 1m, MinSharePrice = 0.25m },
            Overdraft = new OverdraftDef { AnnualRatePercent = 14m, MaxFacility = 60_000m, PledgeRatio = 0.35m },
            Dividends = new DividendPolicyDef { IntervalTicks = 0 },
            Tycoon = new TycoonPolicyDef
            {
                StartingCash = 60_000m,
                StartingShares = 20_000,
                TradeIntervalTicks = 30,
                BuyBudgetFraction = 0m,
                SellAboveCostRatio = 99m,
                MarginInitialRatio = 0m,
                MarginMaintenanceRatio = 0.7m,
                MinLotShares = 100,
                RescuesCompany = rescues,
                RescueCashFraction = 0.5m,
                RescueDiscountPercent = 25m,
            },
        };
        return scenario;
    }

    /// <summary>Le résumé financier des empreintes tel qu'il était avant la bourse solide.</summary>
    private static string OldFinanceSummary(WorldState world)
    {
        var finance = world.Finance;
        var player = finance.Player!;
        var tycoon = finance.Magnate!;
        string rates = world.Cycle.Enabled
            ? ";" + string.Join(",", player.Bonds.Select(b => b.AnnualRatePercent.ToString(Ci)))
            : "";
        return rates + string.Join(";",
            player.Cash.ToString(Ci), player.BookEquity.ToString(Ci), player.SharePrice.ToString(Ci),
            player.Debt.ToString(Ci), player.OverdraftBalance.ToString(Ci), player.DividendsPaidTotal.ToString(Ci),
            tycoon.NetWorth(finance).ToString(Ci), tycoon.MarginCalls.ToString(Ci), finance.Mergers.Count.ToString(Ci));
    }
}
