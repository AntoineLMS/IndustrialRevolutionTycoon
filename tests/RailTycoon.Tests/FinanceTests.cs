using RailTycoon.Sim;
using RailTycoon.Sim.Economy;
using RailTycoon.Sim.Finance;
using RailTycoon.Sim.Telemetry;

namespace RailTycoon.Tests;

/// <summary>
/// Invariants du module finance. Ils vivent dans leur propre fichier et
/// s'enregistrent par un seul appel dans <c>Program</c> : trois autres modules
/// avancent en parallèle, et une liste de tests est exactement le genre de
/// fichier qui produit un conflit de fusion à chaque ligne ajoutée.
/// </summary>
internal static class FinanceTests
{
    /// <summary>
    /// Le scénario dédié au module finance. <c>heartland.json</c> reste la trace de
    /// régression de l'<em>économie</em> et n'active pas la finance : tous les
    /// chiffres de docs/FINDINGS.md la supposent exempte d'effets financiers, et un
    /// scénario doit éprouver une chose à la fois. Celui-ci reprend la même économie
    /// au caractère près et n'ajoute que le bloc <c>finance</c>.
    /// </summary>
    private static string FinancePath()
        => Path.Combine(Fixtures.RepoRoot(), "data", "heartland-finance.json");

    public static void Register(TestRunner runner)
    {
        // LE test du contrat finance. Il ne vérifie pas une tolérance mais une
        // égalité : le pire écart de bilan sur 720 ticks doit valoir zéro. Une
        // fuite d'un millième par tick devient une fortune en deux années de jeu,
        // et une tolérance relative de 1e-9 ne la verrait jamais.
        runner.Add("finance — 720 ticks : tout bilan s'équilibre au centime", () =>
        {
            var sim = new Simulation(ScenarioLoader.Load(FinancePath()));
            double initialStock = Invariants.InitialStockTotal(sim.World);
            var finance = sim.World.Finance;
            Check.True(finance.Enabled, "le scénario de référence doit activer le module finance");

            decimal worst = 0m;
            int worstTick = 0;
            string worstWho = "";

            for (int i = 0; i < 720; i++)
            {
                sim.Step();

                foreach (var (who, book) in Books(finance))
                {
                    decimal residual = Math.Abs(book.Residual);
                    if (residual > worst)
                    {
                        worst = residual;
                        worstTick = sim.World.Tick.Index;
                        worstWho = who;
                    }
                }

                var violations = Invariants.Check(sim.World, initialStock);
                if (violations.Count > 0)
                    Check.True(false,
                        $"tick {sim.World.Tick.Index} : {violations[0].Rule} — {violations[0].Detail}");
            }

            Check.True(worst == 0m,
                $"écart de bilan de {worst} au tick {worstTick} sur {worstWho} — la comptabilité fuit");

            // Un bilan équilibré ne prouve rien si rien ne s'est passé : une
            // comptabilité vide s'équilibre parfaitement. On exige donc que les
            // trois mécanismes se soient réellement exercés pendant la course.
            var player = finance.Player!;
            var tycoon = finance.Magnate!;

            Check.True(player.PrincipalIssuedTotal > 0m, "aucun emprunt contracté");
            Check.True(player.PrincipalRepaidTotal > 0m, "aucune échéance remboursée");
            Check.True(player.InterestPaidTotal > 0m, "aucun intérêt payé");
            Check.True(player.DividendsPaidTotal > 0m, "aucun dividende versé");
            Check.True(tycoon.DividendsReceivedTotal > 0m, "le magnat n'a rien encaissé");
            Check.True(tycoon.SharesBoughtTotal > 0, "le magnat n'a acheté aucune action");
            Check.True(tycoon.SharesSoldTotal > 0, "le magnat n'a vendu aucune action");
            Check.True(tycoon.MarginLoan >= 0m, "dette de marge négative");
            Check.True(finance.Mergers.Count >= 1,
                $"aucune fusion en 720 ticks : l'OPA n'est pas exercée ({finance.Mergers.Count})");
        });

        runner.Add("finance — la dette plus les intérêts payés correspond aux emprunts", () =>
        {
            var sim = new Simulation(ScenarioLoader.Load(FinancePath()));
            sim.Run(720);

            foreach (var company in sim.World.Finance.Companies)
            {
                decimal outstanding = company.OutstandingPrincipal();
                Check.True(
                    company.PrincipalIssuedTotal == company.PrincipalRepaidTotal + outstanding,
                    $"{company.Id} : {company.PrincipalIssuedTotal} emprunté ≠ " +
                    $"{company.PrincipalRepaidTotal} remboursé + {outstanding} restant dû");
                Check.True(
                    company.InterestAccruedTotal == company.InterestPaidTotal + company.UnpaidInterest,
                    $"{company.Id} : {company.InterestAccruedTotal} d'intérêts dus ≠ " +
                    $"{company.InterestPaidTotal} payés + {company.UnpaidInterest} à payer");
            }

            var player = sim.World.Finance.Player!;
            Check.True(player.PrincipalIssuedTotal > player.PrincipalRepaidTotal,
                "le test perdrait son intérêt si toute la dette était éteinte");
        });

        runner.Add("finance — la somme des actions détenues égale le nombre d'actions émises", () =>
        {
            var sim = new Simulation(ScenarioLoader.Load(FinancePath()));
            sim.Run(720);

            foreach (var company in sim.World.Finance.Companies)
            {
                Check.True(company.Register.HeldTotal() == company.Register.SharesIssued,
                    $"{company.Id} : {company.Register.HeldTotal()} détenues pour " +
                    $"{company.Register.SharesIssued} émises");
                foreach (var holder in company.Register.Holders)
                    Check.True(holder.Shares >= 0,
                        $"{company.Id} : {holder.HolderId} détient {holder.Shares} actions");
            }

            // Le magnat a réellement bougé au capital : sinon le registre serait
            // trivialement juste parce que personne n'aurait échangé.
            var player = sim.World.Finance.Player!;
            Check.True(player.Register.HeldBy(Holders.Tycoon) != sim.World.Def.Finance.Tycoon.StartingShares,
                "la participation du magnat n'a pas bougé de toute la partie");
        });

        runner.Add("finance — un dividende transfère au centime de la société au magnat", () =>
        {
            // La caisse personnelle du magnat est distincte de celle de la
            // société : ce qui sort de l'une entre dans l'autre, à l'action près.
            // C'est la propriété qui permet de ruiner la compagnie et de
            // s'enrichir soi-même, donc celle qu'il faut vérifier exactement.
            var sim = new Simulation(ScenarioLoader.Load(FinancePath()));
            var player = sim.World.Finance.Player!;
            var tycoon = sim.World.Finance.Magnate!;

            // On cherche la première distribution plutôt que de la supposer au
            // premier intervalle : les quatre-vingt-dix premiers ticks sont
            // déficitaires — le transporteur constitue son stock avant d'encaisser
            // — donc la société n'a rien à distribuer avant plusieurs trimestres.
            int found = 0;
            decimal total = 0m, companyCashBefore = 0m, received = 0m;
            long tycoonShares = 0, issued = 0;

            for (int i = 0; i < 720 && found == 0; i++)
            {
                decimal paidBefore = player.DividendsPaidTotal;
                decimal receivedBefore = tycoon.DividendsReceivedTotal;
                long sharesBefore = player.Register.HeldBy(Holders.Tycoon);
                long issuedBefore = player.Register.SharesIssued;

                sim.Step();

                if (player.DividendThisTick <= 0m) continue;
                found = sim.World.Tick.Index;
                total = player.DividendThisTick;
                companyCashBefore = player.DividendsPaidTotal - paidBefore;
                received = tycoon.DividendsReceivedTotal - receivedBefore;
                tycoonShares = sharesBefore;
                issued = issuedBefore;
            }

            Check.True(found > 0, "aucune distribution en 720 ticks");
            Check.True(companyCashBefore == total,
                $"tick {found} : la société a décaissé {companyCashBefore} " +
                $"pour un dividende de {total}");

            // Le dividende par action est exact au centime : le total distribué
            // est un multiple entier du nombre d'actions, donc il ne reste aucun
            // résidu à répartir. C'est la condition pour que la somme des parts
            // égale la somme décaissée, et donc qu'un dividende ne fuie pas.
            decimal perShare = total / issued;
            Check.True(Money.Round(perShare) * issued == total,
                $"tick {found} : {total} pour {issued} actions ne se répartit pas " +
                $"au centime ({perShare})");

            Check.True(received > 0m, $"tick {found} : le magnat n'a rien reçu");
            Check.True(received * issued == total * tycoonShares,
                $"tick {found} : le magnat a reçu {received} pour {tycoonShares} actions " +
                $"sur {issued}, dividende total {total}");
        });

        runner.Add("finance — une écriture déséquilibrée est refusée", () =>
        {
            // La partie double n'est pas une élégance : c'est ce qui rend
            // l'identité comptable impossible à violer. Un oubli de contrepartie
            // doit échouer à l'instant de l'écriture, pas trois cents ticks plus
            // tard dans un invariant qu'il faudra remonter à la main.
            var book = new Ledger { OwnerId = "essai" };
            book.Open("cash", AccountKind.Asset);
            book.Open("equity", AccountKind.Equity);

            Check.Throws<InvalidOperationException>(
                () => book.Post("écriture bancale", new Leg("cash", 100m), new Leg("equity", -99m)),
                "une écriture dont les débits ne s'annulent pas doit être refusée");

            Check.Throws<InvalidOperationException>(
                () => book.Post("fraction de centime", new Leg("cash", 0.001m), new Leg("equity", -0.001m)),
                "un montant qui n'est pas un nombre entier de centimes doit être refusé");

            book.Post("apport", "cash", "equity", 250m);
            Check.True(book["cash"] == 250m && book["equity"] == 250m && book.Residual == 0m,
                $"apport mal enregistré : actif {book["cash"]}, capitaux {book["equity"]}");
        });

        runner.Add("finance — l'appel de marge liquide la position quand le cours cède", () =>
        {
            // L'achat à crédit doit être dangereux, sinon c'est un multiplicateur
            // gratuit. Ici la société fond — un amortissement qui dévore son actif —
            // donc son cours cède, et la position du magnat devient intenable.
            var sim = new Simulation(CollapsingCompany());
            sim.Run(60);

            var tycoon = sim.World.Finance.Magnate!;
            var player = sim.World.Finance.Player!;

            Check.True(tycoon.MarginCalls > 0,
                $"aucun appel de marge alors que le cours est passé de 5,00 à {player.SharePrice}");
            Check.True(tycoon.SharesSoldTotal > 0, "l'appel de marge n'a rien liquidé");
            Check.True(tycoon.Book.Residual == 0m,
                $"le bilan du magnat ne tient plus après liquidation : {tycoon.Book.Residual}");
            Check.True(tycoon.MarginLoan <= tycoon.PortfolioMarketValue(sim.World.Finance) + 0.01m,
                $"dette de marge {tycoon.MarginLoan} supérieure au portefeuille " +
                $"{tycoon.PortfolioMarketValue(sim.World.Finance)}");
        });

        runner.Add("finance — le module n'influence pas l'économie", () =>
        {
            // Le scénario de finance reprend l'économie de heartland au caractère
            // près : la trace des marchés doit donc être la même de part et d'autre,
            // et c'est ce qui protège les chiffres de docs/FINDINGS.md.
            //
            // Ce test peut légitimement casser un jour, et il faut savoir comment le
            // lire. Depuis que les prélèvements financiers sortent réellement de la
            // trésorerie, la pression financière peut atteindre le point où le
            // transporteur n'a plus de quoi acheter. Si cette égalité tombe, ce n'est
            // pas la finance qui a fuité : c'est le couplage qui est devenu mordant
            // sur ce scénario, et il faut alors soit relâcher le scénario de finance,
            // soit redériver ses chiffres de référence. Ceux de heartland, eux, ne
            // dépendent d'aucun réglage financier — il n'en a plus.
            string Fingerprint(string path)
            {
                var sim = new Simulation(ScenarioLoader.Load(path));
                var recorder = new CsvRecorder();
                recorder.Record(sim.World);
                for (int i = 0; i < 720; i++)
                {
                    sim.Step();
                    recorder.Record(sim.World);
                }
                return recorder.Fingerprint();
            }

            Check.Equal(Fingerprint(Fixtures.HeartlandPath()), Fingerprint(FinancePath()),
                "empreinte de la trace des marchés, sans finance puis avec");
        });

        runner.Add("finance — le scénario de régression de l'économie n'active pas la finance", () =>
        {
            // Décision de conception, et elle se protège par un test : heartland est
            // la trace de régression de l'économie, et tous les chiffres de
            // docs/FINDINGS.md la supposent exempte d'effets financiers. Activer la
            // finance dessus déplacerait la trésorerie disponible du transporteur,
            // donc ses achats, donc l'empreinte et le résultat de 240 374 que trois
            // documents citent. Un scénario éprouve une chose à la fois.
            var reference = ScenarioLoader.Load(Fixtures.HeartlandPath());
            Check.True(!reference.Finance.Enabled,
                "heartland.json doit garder finance.enabled = false");

            var dedicated = ScenarioLoader.Load(FinancePath());
            Check.True(dedicated.Finance.Enabled,
                "heartland-finance.json doit activer le module");

            // Et les deux doivent bien décrire la même économie, sinon la
            // comparaison d'empreintes ci-dessus ne prouverait rien.
            Check.True(reference.Cargos.Count == dedicated.Cargos.Count &&
                       reference.Cities.Count == dedicated.Cities.Count &&
                       reference.Recipes.Count == dedicated.Recipes.Count &&
                       reference.Trains.Count == dedicated.Trains.Count &&
                       reference.Seed == dedicated.Seed,
                "les deux scénarios doivent décrire la même économie");
        });

        runner.Add("finance — un scénario sans bloc finance tourne comme avant", () =>
        {
            var scenario = Fixtures.Bare("sans-finance");
            scenario.Cities.Add(Fixtures.City("ville", demand: 0.5, stock: 100));

            var sim = new Simulation(scenario);
            double initialStock = Invariants.InitialStockTotal(sim.World);
            sim.Run(60);

            Check.True(!sim.World.Finance.Enabled, "le module doit rester inactif par défaut");
            Check.True(sim.World.Finance.Player is null, "aucune société ne doit être ouverte");
            Check.True(Invariants.Check(sim.World, initialStock).Count == 0,
                "les invariants doivent tenir sans module finance");
        });

        runner.Add("finance — la trésorerie s'explique intégralement par ses quatre flux", () =>
        {
            // `bilan-tresorerie` a changé de formule, et c'est une correction, pas un
            // assouplissement. Tant que la finance tenait sa propre caisse, il ne
            // vérifiait qu'une moitié de la vérité : un dividende sortait du bilan de
            // la société sans sortir de la trésorerie du transporteur, qui pouvait
            // donc dépenser le même argent. Les deux bilans s'équilibraient au
            // centime et la fuite était pourtant réelle — la signature exacte du
            // lavage de fret. Le quatrième flux ferme cette porte.
            var sim = new Simulation(ScenarioLoader.Load(FinancePath()));
            var co = sim.World.Company;
            double worstGap = 0;

            for (int i = 0; i < 720; i++)
            {
                sim.Step();
                double expected = sim.World.Def.StartingCash + co.NetProfit + co.TotalFinanceFlow;
                double gap = Math.Abs(co.Cash - expected);
                if (gap > worstGap) worstGap = gap;
            }

            Check.True(worstGap <= 1e-4,
                $"la trésorerie s'écarte de {worstGap:0.########} de ses quatre flux");

            // Le test ne prouverait rien si le quatrième flux valait zéro : il faut
            // que la finance ait réellement prélevé de l'argent au transporteur.
            Check.Less(co.TotalFinanceFlow, -1000.0,
                $"les prélèvements financiers doivent être significatifs, or {co.TotalFinanceFlow:0.00}");

            // Et le résultat net doit rester une mesure du transport seul, sinon la
            // sentinelle de marge au kilomètre ne veut plus rien dire.
            double km = sim.World.Trains.Sum(t => t.TotalKmTravelled);
            Check.Less(co.NetProfit / km, 3.0,
                "le résultat net ne doit pas absorber les flux financiers");
        });

        runner.Add("finance — un prélèvement financier non reflété est détecté", () =>
        {
            // LA sentinelle de la faille corrigée. On la réintroduit à la main : un
            // dividende occulte, passé au bilan de la société — caisse et capitaux
            // propres en baisse du même montant, écriture parfaitement équilibrée —
            // mais jamais répercuté sur la trésorerie du transporteur.
            //
            // C'est exactement ce que faisait le module avant correction, et aucun
            // invariant ne s'en apercevait. `frontiere-tresorerie` doit l'attraper.
            var sim = new Simulation(ScenarioLoader.Load(FinancePath()));
            double initialStock = Invariants.InitialStockTotal(sim.World);
            sim.Run(120);

            Check.True(Invariants.Check(sim.World, initialStock).Count == 0,
                "les invariants doivent être sains avant l'injection");

            var player = sim.World.Finance.Player!;
            decimal stolen = 5_000m;
            player.Book.Post("dividende occulte",
                new Leg(Accounts.RetainedEarnings, stolen),
                new Leg(Accounts.Cash, -stolen));

            var violations = Invariants.Check(sim.World, initialStock);
            bool caught = violations.Any(v => v.Rule == "frontiere-tresorerie");
            Check.True(caught,
                "un prélèvement non reflété doit violer frontiere-tresorerie ; " +
                $"violations obtenues : {(violations.Count == 0 ? "aucune" : string.Join(", ", violations.Select(v => v.Rule)))}");

            // Et le bilan de la société, lui, reste parfaitement équilibré : c'est
            // bien là le piège. Une comptabilité juste ne dit pas que l'argent est
            // au bon endroit.
            Check.True(player.Book.Residual == 0m,
                "l'écriture injectée est équilibrée : c'est ce qui rend la fuite invisible au bilan");
            Check.True(!violations.Any(v => v.Rule == "bilan-actif-passif"),
                "aucune violation de bilan ne doit apparaître — la fuite n'est pas un déséquilibre");
        });

        runner.Add("finance — un dividende réduit ce que le transporteur peut engager", () =>
        {
            // Le sens de la correction, côté jeu : distribuer aux actionnaires, c'est
            // retirer de l'argent aux trains. Avant, le magnat encaissait et le
            // transporteur ne s'en apercevait pas.
            var sim = new Simulation(ScenarioLoader.Load(FinancePath()));
            var player = sim.World.Finance.Player!;
            var co = sim.World.Company;

            for (int i = 0; i < 720; i++)
            {
                double cashBefore = co.Cash;
                double flowBefore = co.TotalFinanceFlow;
                sim.Step();
                if (player.DividendThisTick <= 0m) continue;

                // Le tick contient aussi l'exploitation : on mesure donc sur le flux
                // financier, qui est la part que la finance revendique.
                double drawn = flowBefore - co.TotalFinanceFlow;
                Check.True(drawn > 0,
                    $"un dividende de {player.DividendThisTick} n'a rien prélevé " +
                    $"(trésorerie {cashBefore:0.00} → {co.Cash:0.00})");
                return;
            }

            Check.True(false, "aucune distribution en 720 ticks");
        });

        runner.Add("finance — une trésorerie négative devient une dette explicite au bilan", () =>
        {
            // Le défaut corrigé ici : la trésorerie pouvait plonger indéfiniment
            // dans le rouge sans que rien ne se passe. Le transporteur refusait
            // d'acheter du fret à découvert, mais les coûts kilométriques étaient
            // prélevés sans condition — plus la compagnie roulait, plus elle
            // creusait. Et bilan-tresorerie restait vérifié : c'est une identité
            // comptable, aussi vraie à −48 830 qu'à +340 000.
            var sim = new Simulation(SinkingCompany(rescued: false));
            double initialStock = Invariants.InitialStockTotal(sim.World);
            var player = sim.World.Finance.Player!;

            bool wentNegative = false;
            for (int i = 0; i < 360; i++)
            {
                sim.Step();
                if (sim.World.Company.Cash < 0) wentNegative = true;

                var violations = Invariants.Check(sim.World, initialStock);
                if (violations.Count > 0)
                    Check.True(false,
                        $"tick {sim.World.Tick.Index} : {violations[0].Rule} — {violations[0].Detail}");

                // Le découvert vaut, à chaque tick, exactement le déficit
                // d'exploitation. Pas « à peu près » : au centime.
                decimal shortfall = sim.World.Finance.ReflectedOperatingCash < 0m
                    ? -sim.World.Finance.ReflectedOperatingCash
                    : 0m;
                Check.True(player.OverdraftBalance == shortfall,
                    $"tick {sim.World.Tick.Index} : découvert {player.OverdraftBalance} " +
                    $"pour un déficit de {shortfall}");
            }

            Check.True(wentNegative, "le scénario devait mettre la compagnie en déficit");
            Check.True(player.OverdraftInterestTotal > 0m,
                "un découvert doit coûter des intérêts, sinon c'est un crédit gratuit");
            Check.True(player.InReceivership,
                "la compagnie devait finir sous administration judiciaire");
            Check.True(sim.World.Company.Grounded,
                "les trains d'une compagnie sous administration doivent cesser de rouler");
            Check.True(sim.World.Company.CreditLimit == 0.0,
                $"plus aucun découvert ne doit être accordé, or {sim.World.Company.CreditLimit}");

            // Le déficit doit avoir un terme : une fois les trains arrêtés, il ne
            // se creuse plus. C'est la différence entre une faillite et une spirale.
            decimal frozen = player.OverdraftBalance;
            sim.Run(60);
            Check.True(player.OverdraftBalance == frozen,
                $"le déficit continue de se creuser sous administration : " +
                $"{frozen} → {player.OverdraftBalance}");
        });

        runner.Add("finance — le magnat peut renflouer sa société, ou la laisser couler", () =>
        {
            // La décision qui fait le genre, et elle doit se jouer dans les deux
            // sens. Renflouer coûte sa fortune personnelle et rend du découvert
            // autorisé, donc du temps ; ne pas renflouer la laisse couler en
            // gardant son argent. Aucune des deux branches ne doit être gratuite.
            var abandoned = new Simulation(SinkingCompany(rescued: false));
            abandoned.Run(360);
            var lost = abandoned.World.Finance;

            var saved = new Simulation(SinkingCompany(rescued: true));
            saved.Run(360);
            var kept = saved.World.Finance;

            Check.True(kept.Player!.CapitalRaisedTotal > 0m,
                "le magnat devait souscrire à une augmentation de capital");
            Check.True(kept.Player.SharesIssuedInRescues > 0,
                "l'augmentation de capital doit émettre des actions nouvelles");

            // Le renflouement dilue : le magnat détient une part plus grande d'une
            // société plus fragile, et le flottant paie la différence.
            double keptStake = kept.Player.Register.HeldBy(Holders.Tycoon)
                / (double)kept.Player.Register.SharesIssued;
            double lostStake = lost.Player!.Register.HeldBy(Holders.Tycoon)
                / (double)lost.Player.Register.SharesIssued;
            Check.Less(lostStake, keptStake,
                "souscrire à l'augmentation de capital doit augmenter la part du magnat");

            // Il paie ce choix de sa poche : celui qui n'a pas renfloué est plus
            // riche personnellement. C'est exactement l'arbitrage à préserver.
            Check.Less(
                (double)kept.Magnate!.Cash,
                (double)lost.Magnate!.Cash,
                "renflouer doit coûter sa caisse personnelle");

            // Ce que l'apport achète : du temps. La société renflouée tombe sous
            // administration plus tard, ou pas du tout dans la fenêtre observée, et
            // ses trains ont roulé plus longtemps. Si ce n'était pas vrai, le
            // renflouement serait une écriture comptable sans conséquence de jeu.
            Check.True(lost.Player.ReceivershipTick >= 0,
                "la société abandonnée devait tomber sous administration");
            Check.True(
                kept.Player.ReceivershipTick < 0 ||
                kept.Player.ReceivershipTick > lost.Player.ReceivershipTick,
                $"la société renflouée est tombée au tick {kept.Player.ReceivershipTick}, " +
                $"l'abandonnée au tick {lost.Player.ReceivershipTick} : l'apport n'achète rien");

            double keptKm = saved.World.Trains.Sum(t => t.TotalKmTravelled);
            double lostKm = abandoned.World.Trains.Sum(t => t.TotalKmTravelled);
            Check.Less(lostKm, keptKm,
                "les trains de la société renflouée doivent avoir roulé plus longtemps");
        });

        runner.Add("finance — la frontière double/decimal ne dérive pas sur 720 ticks", () =>
        {
            // Le seul point de conversion reflète le CUMUL arrondi, pas la somme
            // des flux arrondis. L'écart avec le monde en double reste donc borné
            // par un demi-centime pour toujours, au lieu de croître d'un
            // demi-centime par tick — ce qui ferait 3,60 par an, invisible dans
            // une tolérance relative.
            var sim = new Simulation(ScenarioLoader.Load(FinancePath()));
            var finance = sim.World.Finance;
            double worstGap = 0;

            for (int i = 0; i < 720; i++)
            {
                sim.Step();
                double operating = sim.World.Def.StartingCash + sim.World.Company.NetProfit;
                double gap = Math.Abs((double)finance.ReflectedOperatingCash - operating);
                if (gap > worstGap) worstGap = gap;
            }

            Check.True(worstGap <= 0.005 + 1e-9,
                $"la frontière a dérivé de {worstGap:0.########} — au-delà d'un demi-centime, " +
                "l'arrondi s'accumule");
        });
    }

    private static IEnumerable<(string Who, Ledger Book)> Books(FinanceState finance)
    {
        foreach (var company in finance.Companies) yield return (company.Id, company.Book);
        if (finance.Magnate is not null) yield return ("magnat", finance.Magnate.Book);
    }

    /// <summary>
    /// Une compagnie qui perd de l'argent à rouler : un coût kilométrique tel
    /// qu'aucun trajet n'est rentable, donc des trains qui brûlent 960 par tick
    /// sans rien acheter. C'est le cas exact signalé par un autre module — une
    /// trésorerie à −48 830 sur 720 ticks, sans que rien ne se passe.
    /// </summary>
    /// <param name="rescued">Le magnat souscrit-il à une augmentation de capital, ou garde-t-il son argent ?</param>
    private static ScenarioDef SinkingCompany(bool rescued)
    {
        var scenario = Fixtures.Bare($"compagnie-qui-coule-{(rescued ? "renflouee" : "abandonnee")}");
        scenario.StartingCash = 20_000;
        scenario.Cities.Add(Fixtures.City("ferme", production: 2.0, stock: 200));
        scenario.Cities.Add(Fixtures.City("ville", demand: 0.4));
        scenario.Lines.Add(new LineDef
        {
            Id = "l1", Name = "l1",
            Stops =
            {
                new StopDef { City = "ferme", DistanceKm = 0 },
                new StopDef { City = "ville", DistanceKm = 100 },
            },
        });
        // 120 km par tick à 8 le kilomètre : 960 de perte par tick, et un coût
        // kilométrique tel que le transporteur ne trouve jamais un achat rentable.
        scenario.Trains.Add(new TrainDef
        {
            Id = "t1", Line = "l1", Capacity = 24, SpeedKmPerTick = 120, CostPerKm = 8.0,
        });

        scenario.Finance = new FinanceDef
        {
            Enabled = true,
            CompanyId = "player",
            CompanyName = "Compagnie qui coule",
            SharesIssued = 100_000,
            FixedAssetsAtStart = 150_000m,
            DepreciationAnnualPercent = 0m,
            BorrowWhenCashBelow = 0m,
            Valuation = new ValuationDef
            {
                // On mesure l'insolvabilité, pas le modèle de valorisation : le
                // cours suit l'actif net, sans multiple de résultat.
                EarningsMultiple = 0m,
                PriceSmoothing = 0.5m,
                EarningsSmoothing = 1m,
                MinSharePrice = 0.25m,
                MarketImpact = 0m,
            },
            Overdraft = new OverdraftDef
            {
                AnnualRatePercent = 14m,
                MaxFacility = 60_000m,
                PledgeRatio = 0.35m,
            },
            Dividends = new DividendPolicyDef { IntervalTicks = 0 },
            Tycoon = new TycoonPolicyDef
            {
                StartingCash = 60_000m,
                StartingShares = 20_000,
                TradeIntervalTicks = 30,
                // Il ne joue pas en bourse dans ce scénario : on isole la seule
                // décision qui nous intéresse, remettre ou non de l'argent.
                BuyBudgetFraction = 0m,
                SellAboveCostRatio = 99m,
                MarginInitialRatio = 0m,
                MarginMaintenanceRatio = 0.7m,
                MinLotShares = 100,
                RescuesCompany = rescued,
                RescueCashFraction = 0.5m,
                RescueDiscountPercent = 25m,
            },
        };
        return scenario;
    }

    /// <summary>
    /// Une société sans activité dont l'actif fond : un amortissement de 300 % par
    /// an vide le bilan en quatre mois, donc le cours s'effondre. C'est le décor
    /// minimal pour rendre un appel de marge inévitable, sans dépendre du détail
    /// de l'économie de fret.
    /// </summary>
    private static ScenarioDef CollapsingCompany()
    {
        var scenario = Fixtures.Bare("appel-de-marge");
        scenario.StartingCash = 100_000;
        scenario.Finance = new FinanceDef
        {
            Enabled = true,
            CompanyId = "player",
            CompanyName = "Compagnie fondante",
            SharesIssued = 100_000,
            FixedAssetsAtStart = 400_000m,
            DepreciationAnnualPercent = 300m,
            BorrowWhenCashBelow = 0m,
            Valuation = new ValuationDef
            {
                // Le cours suit l'actif net sans retard ni multiple : on veut
                // mesurer l'appel de marge, pas le modèle de valorisation.
                EarningsMultiple = 0m,
                PriceSmoothing = 1m,
                EarningsSmoothing = 1m,
                MinSharePrice = 0.01m,
                MarketImpact = 0m,
            },
            Dividends = new DividendPolicyDef { IntervalTicks = 0 },
            Tycoon = new TycoonPolicyDef
            {
                StartingCash = 100_000m,
                StartingShares = 20_000,
                TradeIntervalTicks = 10,
                BuyBudgetFraction = 0.9m,
                MarginInitialRatio = 0.9m,
                MarginMaintenanceRatio = 0.7m,
                MarginAnnualRatePercent = 9m,
                SellAboveCostRatio = 99m,
                SellFraction = 0.25m,
                MinLotShares = 100,
            },
        };
        return scenario;
    }
}
