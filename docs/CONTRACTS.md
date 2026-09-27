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
   finance.
5. Toute valeur équilibrable vit dans `data/*.json`, jamais en dur dans le code.
6. `dotnet run --project tests/RailTycoon.Tests` doit rester vert, et chaque
   module ajoute ses propres invariants à la suite.
7. Un changement qui déplace une trace de référence (`ReferenceTraceTests`) met
   à jour l'empreinte **et** dit dans son message de commit pourquoi la trace a
   bougé. Une empreinte recopiée sans explication rend le test aussi creux que
   celui qu'il remplace.
8. Chaque scénario de `data/` déclare les blocs de tous les modules
   (`anticipating`, `network`, `finance`, `events`), ou écarte explicitement ceux dont il
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

**Ce qui manque** : les aiguillages comme objets (une bifurcation est pour l'instant
un nœud sans contrainte de géométrie), la recherche automatique d'un tracé — le
géomètre chiffre celui qu'on lui donne —, le terrassement à flanc de coteau, la
construction en cours de partie (tout est calculé au chargement), et le passage de
`heartland` sur un relief.

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
