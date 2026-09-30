# Contrats entre modules

Ce document existe pour une raison précise : permettre de confier un module à
quelqu'un qui n'a pas participé aux discussions — un nouveau venu dans l'équipe,
ou un agent qui démarre sans mémoire du projet. Un module n'est délégable que si
son périmètre, son interface et son critère de réussite tiennent par écrit.

Tant qu'un contrat n'est pas écrit ici, le module n'est pas prêt à être délégué :
plusieurs personnes travaillant en parallèle sur des interfaces implicites
produisent du code qui ne s'assemble pas.

## Règles communes à tous les modules

1. Respecter les quatre règles de [ARCHITECTURE.md](ARCHITECTURE.md)
   (indépendance du moteur, pas fixe, ordre de parcours stable, aléa maîtrisé).
2. Ne créer ni détruire de marchandise autrement que par `Market.Produce` et
   `Market.Consume`. Les transferts passent par `Withdraw` / `Deposit`.
3. Ne pas modifier l'ordre des phases d'un tick.
4. Ne pas toucher à `Company` en dehors du module transport et du futur module
   finance. Le carnet de route (`Company.Freight`) est écrit par le seul transport ;
   le module `objectives` le lit, sans y écrire.
5. Toute valeur équilibrable vit dans `data/*.json`, jamais en dur dans le code.
6. `dotnet run --project tests/RailTycoon.Tests` doit rester vert, et chaque
   module ajoute ses propres invariants à la suite.
7. Un changement qui déplace une trace de référence (`ReferenceTraceTests`) met
   à jour l'empreinte **et** dit dans son message de commit pourquoi la trace a
   bougé. Une empreinte recopiée sans explication rend le test aussi creux que
   celui qu'il remplace.
8. Chaque scénario de `data/` déclare les blocs de tous les modules
   (`anticipating`, `network`, `finance`, `events`, `cycle`, `objectives`), ou écarte explicitement ceux dont il
   se passe avec une clé `"//<bloc>"` qui dit pourquoi. Un module qui ajoute un
   bloc l'inscrit dans `ScenarioLoader.ModuleBlocks`.

## Modules

### `economy` — solveur économique

**Interface** : `IEconomySolver` (`src/RailTycoon.Sim/Economy/EconomySolver.cs`)
**État actuel** : `ReferenceEconomySolver`, implémentation témoin volontairement simple.

**Périmètre** : production primaire, chaînes de transformation, consommation,
formation des prix.

**Ce qui manque** : anticipation des stocks par les acheteurs, saisonnalité,
contrats d'approvisionnement, croissance des villes en fonction de leur
desserte, concurrence entre plusieurs acheteurs sur un même marché.

**Critère de réussite** : sur les scénarios de régression existants, produire une
dispersion de prix entre villes *qui se déplace au cours de la partie* plutôt que
de converger vers un état stable. Une économie qui atteint un équilibre et y
reste n'a plus rien à offrir au joueur.

### `network` — réseau ferré

**Interface** : `IRailNetwork` (`src/RailTycoon.Sim/Network/RailNetwork.cs`), décrite
dans [ARCHITECTURE.md](ARCHITECTURE.md), section « Le réseau ferré ».
**État actuel** : tranche verticale en place — relief chargé depuis les données,
tracé sous contrainte de pente et de rayon, terrassement, ponts, tunnels, graphe de
voies et calcul d'itinéraire. Trois cartes d'essai avec devis de référence dans la
suite de tests. `heartland` reste en mode de compatibilité, sur ses distances
kilométriques.

**Périmètre** : graphe de voies sur relief, contraintes de pente et de rayon de
courbe, coût de déblai et de remblai calculé sur le terrain, ponts, tunnels,
aiguillages.

**Coût du réseau** : l'entretien des voies, 0,2 par kilomètre de voie et par tick
(`costs.upkeepPerTrackKmPerTick`), prélevé au transporteur en tête de la phase
transport, que les trains roulent ou non. Le devis de construction lui-même n'est
toujours débité nulle part.

**Ce que le relief coûte à l'exploitation** : le réseau ne publie qu'un scalaire,
`LegCostFactor` (1 + mètres gravis × `traction.climbEquivalentKm` ÷ longueur, par
tronçon et par sens) ; ce que le transport en fait dépend du modèle de coût choisi
dans `haulage.costModel` (`Transport/TrainCost.cs`) :

- `flat` (défaut, toutes les empreintes) : le facteur multiplie le coût kilométrique
  facturé, plein ou vide, mais la décision d'achat impute un coût moyen, à plat. Le
  relief est un impôt, pas une géographie.
- `mass` (opt-in, `data/sierra-marginal.json`) : le coût kilométrique est
  proportionnel à la masse remorquée — tare fixe, chargements variables, masses dans
  `haulage.massCost` —, le facteur multiplie les deux parts, et le transporteur
  décide sur le seul surcoût d'un chargement. **Facture et décision sortent de la
  même formule** : un test vérifie que dix chargements de plus coûtent à la facture
  dix fois le coût décidé, dans les deux sens du col. Le relief y fait une
  géographie au-delà d'une traction de 0,12 à 0,15.

Un module qui touche à `LegCostFactor`, à `MoveTrain` ou à `HaulCostPerUnitAhead`
doit garder cette égalité sous `mass`, et la neutralité au bit près sous `flat`.

**Ce qui manque** : les aiguillages comme objets (une bifurcation est pour l'instant
un nœud sans contrainte de géométrie), la recherche automatique d'un tracé — le
géomètre chiffre celui qu'on lui donne —, le terrassement à flanc de coteau, la
construction en cours de partie (tout est calculé au chargement), le passage de
`heartland` lui-même sur un relief — son économie tourne désormais sur relief dans
`data/sierra.json` —, et la décision de l'équipe sur le modèle de coût par défaut :
sous `flat`, le relief n'est que facturé ; sous `mass`, il pèse sur les décisions,
mais en changeant aussi le niveau de résultat, la tension du capital et la rotation
du fret (voir FINDINGS.md, « Le coût marginal réel », décisions 1 et 2).

**Critère de réussite** : le coût de construction d'un tracé donné doit être
reproductible et correspondre aux valeurs de référence sur les cartes de test.

### `dispatch` — circulation et signalisation

**Interface** : à créer, **au-dessus de `IRailNetwork`**. Le graphe de voies, le
grain de réservation (l'arête), la capacité de croisement d'un nœud et la forme d'un
itinéraire sont déjà définis par le module `network` : voir les garanties énumérées
dans [ARCHITECTURE.md](ARCHITECTURE.md), section « Le réseau ferré ». C'est la
raison pour laquelle ce module n'a pas été écrit en parallèle du réseau.
**État actuel** : inexistant — les trains se croisent sans se voir.

**Périmètre** : blocs, signaux, réservation d'itinéraire, évitement
d'interblocages.

**Critère de réussite** : 500 trains sur une carte dense pendant 10 000 ticks
sans interblocage ni régression de performance. C'est le module où il faut du
test aléatoire massif : les interblocages n'apparaissent pas sur les cas simples.

### `finance` — bourse et société

**Interface** : `IFinanceSolver` (`src/RailTycoon.Sim/Finance/FinanceSolver.cs`),
phase 6 du tick.
**État actuel** : `ReferenceFinanceSolver`, témoin piloté par les données.
Comptabilité en partie double, montants en `decimal` arrondis au centime (voir
[ARCHITECTURE.md](ARCHITECTURE.md) pour la frontière avec le monde en `double`).

**Ce qui est modélisé** : actions et registre des actionnaires, valeur
d'entreprise et cours avec impact des ordres, obligations à taux et échéance,
intérêts payés chaque tick, découvert bancaire explicite et administration
judiciaire, caisse personnelle du magnat distincte de celle de la société,
dividendes au prorata, achat sur marge avec appel de marge, augmentation de
capital, prise de participation dans un concurrent piloté par les données, OPA sur
le flottant et fusion-absorption.

**Scénarios** : `data/heartland-finance.json` est le scénario du module. Il reprend
l'économie de `heartland.json` au caractère près et n'ajoute que le bloc
`finance` ; `heartland.json` reste la trace de régression de l'**économie** et
n'active pas la finance. Cette séparation est une décision, protégée par un test :
tous les chiffres de [FINDINGS.md](FINDINGS.md) supposent `heartland` exempte
d'effets financiers, et un scénario doit éprouver une chose à la fois.

**Avec une conjoncture** (module `cycle`, voir plus bas), trois lectures, et trois
seulement : le taux d'une obligation est fixé **à l'émission** — taux facial, plus
l'ajustement de la phase, plus une prime de risque selon le levier et la rentabilité,
moins un bonus du dirigeant qui vaut 0 — et conservé avec elle (`Bond.Quote`) ; le taux
du découvert suit la phase au jour le jour ; le multiple de valorisation est multiplié
par le facteur de la phase pour un résultat positif, divisé pour une perte. Sans
conjoncture active, `FinanceState.Cycle` est nul et chaque calcul suit son chemin
d'avant le module, au centime — les empreintes de heartland-finance le prouvent.

**Ce qui manque** : un vrai concurrent (le module `ai` — les concurrents actuels
sont des bilans animés par des données, pas des réseaux), les dividendes des
concurrents, la prime de contrôle négociée plutôt que fixée, la faillite
liquidative (l'administration judiciaire gèle, elle ne liquide pas), et
l'amortissement du matériel réellement acheté plutôt qu'une valeur de départ.

**Dette connue, assumée, à traiter plus tard.** Deux points sont identifiés et
volontairement laissés en l'état :

1. **Le flottant est une contrepartie de profondeur infinie.** Il absorbe n'importe
   quel volume au cours affiché, et les plus-values que le magnat y réalise
   viennent de l'extérieur du modèle — sur le scénario de finance, plusieurs
   centaines de milliers. L'impact de marché (`valuation.marketImpact`) rend
   l'aller-retour coûteux mais ne referme pas la boucle. Même famille que le lavage
   de fret, en atténué : à traiter par une profondeur de carnet finie, sur le
   modèle du découpage en tranches d'une grosse livraison.
2. **Aucune statistique moyennée côté finance.** Le harnais affiche la fortune du
   magnat telle qu'elle est au dernier tick, or un tick sur trente est un tick
   d'ordre de bourse, et le cours y porte encore l'impact que l'ordre vient de
   produire. C'est précisément l'erreur d'instantané final que
   [FINDINGS.md](FINDINGS.md) documente pour les marchés. Il faut l'équivalent de
   `RunStatistics` pour les grandeurs financières avant de tirer une conclusion
   d'équilibrage de ces chiffres.

   *Aggravées par la conjoncture* (FINDINGS.md, « Le cycle économique ») : un cours qui
   oscille entre expansion et crise est une pompe pour qui trade contre un flottant
   infini. Raccourcir les phases de moitié fait passer la fortune moyenne du magnat
   témoin de 377 000 à 1 228 000. La profondeur de carnet finie est un prérequis de
   tout réglage fin de la bourse sous conjoncture.

**Critère de réussite** : tout bilan s'équilibre au centime à chaque tick, et
l'invariant `bilan-tresorerie` reste vérifié. En finance, une fuite d'un
millième par tick devient une fortune en deux ans de jeu.

**Invariants apportés** : `bilan-actif-passif`, `actions-emises`,
`dette-emprunts`, `tresorerie-non-negative`, `decouvert-explicite`,
`frontiere-tresorerie`. Tous se comparent à zéro exactement, sans tolérance : la
comptabilité est tenue en `decimal`, donc un écart d'un centime est un bug et non
un résidu de calcul.

### `events` — événements historiques et aléatoires

**Interface** : `IEventSolver` (`src/RailTycoon.Sim/Events/EventSolver.cs`), phase
0b du tick — voir le tableau des phases de [ARCHITECTURE.md](ARCHITECTURE.md) et
la décision qui l'y a insérée.
**État actuel** : `ReferenceEventSolver`, piloté par le bloc `events` du scénario.
Inactif par défaut, et neutre au bit près tant qu'il l'est.

**Périmètre** : des multiplicateurs sur le taux de **production primaire** et sur
la **demande des habitants**, rien d'autre. Deux sortes d'événements :

- *historiques* — datés (tick, ou année/mois/jour d'un calendrier de douze mois de
  trente jours), ciblés (une liste de villes, ou toutes celles où l'effet a prise),
  d'intensité et de durée fixées, avec une montée et une descente linéaires. Chacun
  se déclare `historical` — et doit alors renvoyer à une source de
  [SOURCES.md](SOURCES.md) — ou `inspired`. Le chargeur refuse un événement
  historique sans source.
- *aléatoires* — un catalogue de types : occurrences par an, mois où ils peuvent
  commencer, une ville tirée parmi les admissibles ou toutes à la fois, bornes
  d'intensité et de durée. Tirés sur la séquence propre du module ; quatre tirages
  par type et par jour, quoi qu'il arrive.

**Ce que le module publie** : `Market.EventProductionFactor` et
`Market.EventDemandFactor`, recomposés chaque jour — produit des événements actifs,
borné par `minFactor` et `maxFactor`. Et un journal public, `WorldState.Events` :
les événements *déclenchés* avec leurs dates de début et de fin, leurs cibles et
leurs multiplicateurs au plus fort, jamais ceux à venir. Le harnais l'affiche et
l'écrit dans `events.csv`. C'est la frontière d'information : un joueur la lit dans
la gazette, un concurrent IA a le droit d'en lire autant et pas davantage.

**Ce que le module garantit** — et que les tests vérifient :

1. Il ne touche ni stock, ni prix, ni argent. La matière passe toujours par
   `Market.Produce` et `Market.Consume`, dans le solveur économique.
2. Inactif, il n'écrit rien : bloc absent ou `enabled = false`, les traces sont
   celles d'avant le module au bit près, sous les deux solveurs.
3. Activé, il ne décale le flux aléatoire d'aucun autre module.
4. Un événement historique agit à sa date, sur sa cible seule, avec l'enveloppe
   annoncée, puis s'éteint ; intensités et durées aléatoires restent dans leurs
   bornes ; une même définition ne s'empile pas sur une même ville.

**Ce qu'il porte pour la conjoncture** : un attribut de données `cycle` sur un
historique ou un type aléatoire (`forcePhase` ou `pushTicks`, l'un ou l'autre), que
le module vérifie dans sa forme et reporte au journal (`EventRecord.Cycle`) sans le
lire. C'est le module `cycle` qui le lit, le jour où l'événement s'ouvre ; sans lui,
l'attribut est inerte, et l'empreinte de heartland-events n'a pas bougé en le
recevant. Le couplage reste donc une donnée : aucun identifiant d'événement n'est
écrit dans le code d'aucun des deux modules.

**Ce qu'il exige des solveurs économiques** : composer ces multiplicateurs dans
leurs taux du jour, et dimensionner l'entrepôt d'un site sur son débit nominal.
C'est une ligne par levier dans chacun des deux solveurs livrés.

**Scénario** : `data/heartland-events.json` est le scénario du module, sur le
modèle de heartland-finance : l'économie de `heartland.json` au caractère près, et
le seul bloc `events` en plus. `heartland.json` n'en déclare pas, décision protégée
par un test. Son catalogue aléatoire est **équilibré** — hausses et baisses se
compensent en espérance pour chaque marchandise et chaque levier —, ce que
`--balance` affiche et qu'un test exige : un catalogue à sens unique déplace
l'équilibre du scénario sans rien animer (voir [FINDINGS.md](FINDINGS.md)).

**Ce qui manque** : des événements sur les usines (un moulin en grève) et sur le
transport (une voie coupée, un pont emporté) ; un mécanisme à somme nulle — une
migration plutôt qu'un afflux — qui garantirait l'équilibre à chaque tick au lieu
d'en espérance ; des chaînes d'événements (une sécheresse qui rend un incendie plus
probable) ; des événements déclenchés par l'état du monde plutôt que par le
calendrier (une pénurie prolongée qui provoque une émeute) ; une déclaration par
`ironpeak`, dont la candidate — une grève de l'anthracite à l'automne 1900 — reste
à sourcer ; et la déclaration du bloc par le scénario relief + économie en cours
d'écriture dans un autre chantier.

**Critère de réussite** : sur son scénario d'épreuve, comparé à un témoin qui joue
le même calendrier d'aléas à intensité négligeable, faire changer la ville la plus
chère d'un mois sur l'autre plus souvent **sans dégrader le résultat médian du
transport**, et sans violer aucun invariant. Aujourd'hui : +2,6 points sous la
référence et +1,9 sous l'anticipant pour les aléatoires seuls, médiane neutre ; la
mobilité de l'amplitude, elle, ne bouge pas. Le critère du contrat `economy` n'est
donc atteint qu'en partie, et la mesure dit pourquoi : le transporteur de mesure,
une navette sans changement de parcours, ne peut pas exploiter un choc local.

### `cycle` — conjoncture

**Interface** : `ICycleSolver` (`src/RailTycoon.Sim/Cycle/CycleSolver.cs`), phase 0c
du tick — voir le tableau des phases de [ARCHITECTURE.md](ARCHITECTURE.md) et la
décision qui l'y a insérée, et pourquoi ce n'est pas un sous-bloc de `events`.
**État actuel** : `ReferenceCycleSolver`, piloté par le bloc `cycle` du scénario.
Inactif par défaut, et neutre au bit près tant qu'il l'est.

**Périmètre** : une suite de phases en boucle (dans l'ordre des données : expansion,
ralentissement, crise, reprise), chacune avec ses bornes de durée ; une durée tirée à
l'ouverture de chaque phase, un nombre par phase, sur la séquence propre du module.
Les phases sont désignées par leur identifiant ; seul `favorable` a un sens pour le
code. Le calendrier est **exogène** : aucune règle ne lit l'activité (le cycle mû par
l'économie est écarté par la vision).

**Ce que les événements y font** : par leur attribut `cycle` (contrat `events`),
`forcePhase` bascule la conjoncture le jour où l'événement s'ouvre, en tirant la durée
de la phase forcée (déjà dans cette phase : elle est prolongée si le tirage finit plus
tard) ; `pushTicks` allonge une phase favorable et abrège une défavorable (positif), ou
l'inverse (négatif). Le module lit le journal public des événements, et rien d'autre.

**Ce que le module publie** — les *conditions du jour*, en ligne droite sur
`transitionTicks` d'une phase à la suivante (`CycleState`) :

| condition | lue par | comment |
|---|---|---|
| `RateAdjustmentPercent` | finance | ajouté au taux facial d'une obligation **à son émission** (taux fixe ensuite), et au taux du découvert chaque jour |
| prime de risque (`CycleState.QuoteBond`) | finance | levier (dette + découvert + principal, sur capitaux propres) et rentabilité lissée ; plafond et plancher du taux dans `cycle.credit` |
| bonus du dirigeant | — | **point d'accroche** : terme de la formule du taux, vaut 0 tant que le score n'existe pas |
| `EarningsMultipleFactor` | finance | multiplie le multiple de valorisation d'un résultat positif, le divise pour une perte ; toute la cote |
| `DemandFactor` | économie | publié sur chaque marché, `Market.CycleDemandFactor`, composé par un produit avec `EventDemandFactor` dans les deux solveurs |
| `InvestorContributionFactor`, `InvestorPatienceFactor` | — | **points d'accroche** du futur module de fondation et du score de dirigeant ; lus par personne aujourd'hui |

Et un **journal public**, `CycleState.Journal` : changements de phase avec leur cause
(ouverture, échéance, bascule forcée par un événement, poussée qui fait échoir) et
chaque poussée avec son ampleur. Il ne publie **jamais** la date prévue de la fin d'une
phase : c'est un pari, pas une lecture. Le harnais l'affiche et l'écrit dans
`cycle.csv` ; un concurrent IA a le droit d'en lire autant, et pas davantage.

**Ce que le module garantit** — et que les tests vérifient (`CycleTests.cs`) :

1. Il ne touche ni stock, ni prix, ni argent : il publie des conditions, la matière
   passe par `Produce`/`Consume`, l'argent par le grand livre de la finance.
2. Inactif, il n'écrit rien : bloc absent ou `enabled = false`, les traces sont celles
   d'avant le module au bit près, sous les deux solveurs — y compris heartland-cycle
   désactivé contre la réunion de heartland-finance et heartland-events.
3. Activé, il ne décale le flux aléatoire d'aucun autre module : conjoncture neutre et
   conjoncture désactivée donnent la même partie au centime.
4. Chaque phase achevée a duré son tirage plus les poussées publiées, au jour près ; le
   tirage est dans ses bornes ; les phases se suivent dans l'ordre des données.
5. Un événement marqué déplace la conjoncture, un événement non marqué ne la touche
   pas ; la panique de 1873 met la conjoncture en crise à sa date, quel que soit le
   calendrier tiré.
6. Une obligation émise en crise coûte plus cher qu'en expansion, et son taux ne bouge
   plus après l'émission ; la bourse cote plus bas en crise, pertes comprises.
7. Sur heartland-cycle, demande moyenne et poussées sont équilibrées sur un cycle
   (`CycleBalance`, affiché par `--balance`).

**Scénario** : `data/heartland-cycle.json` : l'économie et la finance de
heartland-finance et les événements de heartland-events au caractère près, et le seul
bloc `cycle` en plus — le bloc `objectives`, observateur pur, mis à part ; mesuré sur
2 160 ticks (la panique de 1873 et au moins un cycle complet). Les autres scénarios
écartent le bloc par une clé `"//cycle"`.

**Ce qui manque** : les lecteurs des points d'accroche (fondation, score de
dirigeant) ; un taux de marge du magnat qui suive la conjoncture ; des types
d'événements propres à la conjoncture (faillite bancaire, ruée vers l'or) ; la
profondeur de carnet finie, sans laquelle la fortune du magnat n'est pas une mesure
d'équilibrage sous conjoncture ; une IA qui lise le journal.

**Critère de réussite** : sur son scénario d'épreuve, contre la même partie sans
conjoncture, un rythme lisible — cours et coût du crédit qui varient nettement d'une
phase à l'autre, cours moyen inchangé — **sans aucune mise sous administration** de la
configuration livrée, sous les deux solveurs, et sans violer aucun invariant.
Aujourd'hui : cours ÷2,2 en crise, obligation type à 5,5 % contre 11,0 %, 0/40 sous
administration.

### `objectives` — objectifs de scénario

**Interface** : `IObjectiveSolver` (`src/RailTycoon.Sim/Objectives/ObjectiveSolver.cs`),
phase 7 du tick, la dernière — voir le tableau des phases de
[ARCHITECTURE.md](ARCHITECTURE.md) et la décision qui l'y a ajoutée.
**État actuel** : `ReferenceObjectiveSolver`, piloté par le bloc `objectives` du
scénario. Inactif par défaut ; **observateur pur** quand il est actif.

**Périmètre** : les trois sortes d'objectifs que la vision décide (« Gagner »), et
elles seules. Un objectif a une mesure et un ou plusieurs **paliers** — une cible et
une échéance facultative, **incluse**, donnée en tick ou en date du calendrier de jeu.
Chaque soir, chaque palier en cours est conclu : **atteint** le jour où la mesure
franchit la cible, **manqué** le soir de l'échéance s'il ne l'est pas. Un palier conclu
l'est pour de bon, même si la mesure repasse de l'autre côté.

| sorte | mesure | ce qu'elle lit |
|---|---|---|
| `fortune` | fortune du magnat, au soir du tick (`averageTicks` = 1) ou moyenne des N derniers soirs ; illisible, donc jamais atteinte, avant N soirs | `Tycoon.NetWorth` |
| `deliveries` | chargements d'une marchandise **laissés par le rail** dans une ville — vendus moins rachetés, jamais négatif — ou la somme sur toutes les villes | `Company.Freight` |
| `connect` | 1 dès qu'un **même train, sur une même ligne**, s'est arrêté dans les deux villes, arrêts traversés compris | `Company.Freight` |

Les raisons de ces trois définitions, et celles qui ont été écartées, sont dans
[FINDINGS.md](FINDINGS.md), « Les objectifs ». L'essentiel :

- **Pas de fortune sans finance.** Sans magnat, un objectif de fortune est refusé au
  chargement — jamais remplacé en silence par la trésorerie de la compagnie, que la
  vision distingue de la fortune du joueur. La fenêtre de lecture est **obligatoire** :
  au jour le jour, un seul ordre de bourse contre le flottant infini (dette n° 1 de la
  finance) suffit à franchir un palier.
- **Livré n'est pas vendu.** La revente de ville en ville s'annule dans chaque ville
  intermédiaire ; livré au total vaut exactement ce que le rail a pris aux villes
  exportatrices, moins ce qui est à bord. Une ville qui produit la marchandise ne
  « reçoit » qu'au-delà de ce qu'elle exporte.
- **Relier exige un train.** Deux villes desservies par deux trains sans voie commune
  ne sont pas reliées ; le jour où la construction existera, il faudra poser la voie et
  y faire rouler un train.

**Le carnet de route** (`Transport/FreightLedger.cs`, `Company.Freight`) : chargements
vendus et achetés par marchandise et par ville, gares desservies par train et par
ligne. Écrit par le transporteur à l'instant de chaque échange et de chaque arrivée
(trois lignes dans `OpportunisticHaulageSolver`), dans tous les scénarios ; lu par le
seul module `objectives`. Un transporteur qui remplacerait le livré doit l'écrire aux
mêmes endroits : un test compare le carnet aux dépôts et retraits des marchés,
marchandise par ville.

**Ce que le module publie** : `WorldState.Objectives` — la mesure du jour de chaque
objectif et l'état de chaque palier (le tableau de bord), et un **journal public** des
paliers atteints ou manqués, avec la date, la mesure ce jour-là, la cible et, pour une
liaison, le train qui l'a faite. Le harnais l'affiche et l'écrit dans
`objectives.csv`. Un concurrent IA a le droit d'en lire autant : la fortune d'un
magnat et les livraisons d'une compagnie sont publiques.

**Ce que le module garantit** — et que les tests vérifient (`ObjectiveTests.cs`) :

1. Il ne touche ni stock, ni prix, ni argent, ni décision, et ne tire aucun aléa :
   heartland-cycle et sierra jouent la même partie, au bit près et sous les deux
   solveurs, avec et sans leur bloc. Aucune empreinte de `ReferenceTraceTests` n'a
   bougé à son arrivée.
2. Inactif, il n'écrit rien : ni progression, ni journal.
3. Chaque sorte conclut au bon jour, recalculé par un autre chemin que le module : la
   série des fortunes du magnat, les dépôts et retraits des marchés, la distance et la
   vitesse d'un train. L'échéance est incluse ; un palier est manqué le soir même,
   jamais avant.
4. La revente ne gonfle pas les livraisons ; deux réseaux disjoints ne relient rien.
5. Des objectifs combinés ne s'influencent pas : le journal de l'ensemble est la
   réunion des journaux de chacun joué seul.
6. Une donnée incohérente est refusée au chargement : sorte, marchandise ou ville
   inconnue, fortune sans finance ou sans fenêtre, cible absente ou nulle, cible sur
   une liaison, liaison qui n'a pas exactement deux villes distinctes, échéance à la
   fois en tick et en date, date sans calendrier ou qui n'existe pas, échéance avant
   le premier jour, deux calendriers différents (`objectives.startYear` et
   `events.startYear`), identifiants en double.

**Points d'accroche, que rien ne lit** : ce qu'un palier vaut (victoire, médaille,
jalon, défaite s'il est manqué), comment les objectifs se combinent en une issue de
partie, et ce qui fait perdre. La vision les laisse aux scénarios ; le module ne les
interprète pas.

**Scénarios** : `heartland-cycle` (fortune du magnat en moyenne sur 30 jours, à deux
paliers ; mille chargements de nourriture ; trois cents de charbon à Northgate) et
`sierra` (Pinecrest – Cedarton, de part et d'autre du col). Valeurs d'illustration,
non réglées. Les tests qui comparent ces scénarios à leurs voisins (heartland-finance
+ heartland-events, sierra-marginal) mettent le bloc à part ; les autres scénarios
l'écartent par une clé `"//objectives"`.

**Ce qui manque** : une issue de partie (victoire, défaite) et ce qui la décide ; un
objectif du **score de dirigeant** ou des **investisseurs** (VISION.md, « Le score de
dirigeant ») — même mécanique, autre mesure ; la construction en cours de partie, sans
laquelle « relier » n'est qu'un horaire ; la profondeur de carnet finie, sans laquelle
une fortune se fabrique par un ordre de bourse ; un objectif par compagnie le jour où
plusieurs compagnies rouleront (le carnet est déjà par compagnie, les objectifs lisent
celle du joueur) ; un minimum sur une fenêtre (« tenir le million trente jours »),
chiffré mais non implémenté.

**Critère de réussite** : sur ses scénarios d'épreuve, donner pour chaque palier une
date médiane, une dispersion et une part de parties manquées qui ne dépendent que de
la partie jouée — pas d'une revente, pas d'un soir d'ordre de bourse —, sans rien
déplacer de la partie. Aujourd'hui : livraisons nettes à 0 à 18 jours d'écart-type
pour la nourriture et 43 à 90 pour le charbon de Northgate, sur des horizons de 1 150
à 1 900 jours (en brut : de 200 à 925 jours pour le même charbon), fortune lisible en moyenne sur 30 jours (au jour le jour, le million se franchit
le soir d'un ordre dans 40 témoins sur 40), liaison fixée par l'horaire (tick 4 dans
40 parties sur 40), aucune empreinte déplacée.

### `content` — données historiques

**Périmètre** : locomotives et leurs caractéristiques par époque, marchandises,
chaînes de production, villes et leurs profils, cartes.

**Critère de réussite** : données sourcées, conformes au schéma de
`Economy/Definitions.cs`, et chargées sans erreur de validation.

### `ai` — concurrents

**État actuel** : inexistant. `OpportunisticHaulageSolver` n'est pas une IA de
concurrent : c'est un instrument de mesure, un trader glouton avec information
parfaite qui sert à détecter si l'économie présente des gradients exploitables.

**Critère de réussite** : construire un réseau rentable *et* jouer en bourse,
sans accès à une information que le joueur n'a pas.

## Ce qui ne se délègue pas

L'équilibrage et le sentiment de jeu. On peut déléguer « ce solveur respecte ses
invariants et bat la référence sur cette métrique » ; on ne peut pas déléguer
« est-ce que c'est amusant ». Cette question se tranche en jouant, et elle
appartient à l'équipe.
