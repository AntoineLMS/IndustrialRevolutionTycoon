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

**Interface** : à créer. `Company` est pour l'instant une simple trésorerie.
**État actuel** : recettes, achats de fret, coûts d'exploitation.

**Périmètre** : actions, obligations, dividendes, achat sur marge, OPA, fusions,
distinction entre caisse personnelle du magnat et caisse de la société.

**Critère de réussite** : tout bilan s'équilibre au centime à chaque tick, et
l'invariant `bilan-tresorerie` reste vérifié. En finance, une fuite d'un
millième par tick devient une fortune en deux ans de jeu.

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
