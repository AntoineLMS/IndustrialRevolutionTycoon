# RailTycoon — prototype économique

Prototype d'un jeu de gestion ferroviaire inspiré de *Railroad Tycoon 3*. Ce
dépôt contient pour l'instant **uniquement la simulation économique**, sans
moteur, sans interface et sans image : c'est le pari de conception du projet.

## Pourquoi commencer par là

Dans ce genre de jeu, ce qui fait la profondeur n'est ni les graphismes ni le
nombre de locomotives : c'est une économie où chaque marché a son propre prix,
où livrer une marchandise fait baisser le prix qu'on venait exploiter, et où il
faut donc continuellement trouver la prochaine asymétrie. Si cette boucle n'est
pas intéressante quand elle se réduit à des chiffres dans un terminal, aucun
habillage ne la sauvera. Le prototype existe pour répondre à cette question
avant d'engager des années de production.

## Exécuter

```bash
dotnet run --project src/RailTycoon.Harness -- --ticks 720
```

Options : `--scenario <fichier>`, `--ticks <n>`, `--out <dossier>`, `--every <n>`.

Pour le volet financier — société, emprunts, bourse, OPA :

```bash
dotnet run --project src/RailTycoon.Harness -- --scenario data/heartland-finance.json --ticks 720
```

Même économie, au caractère près. `heartland.json` reste la trace de régression de
l'économie et n'active pas la finance : un scénario éprouve une chose à la fois.

Pour les événements — grève de l'anthracite de 1871, grand incendie de Chicago,
panique de 1873, et un catalogue d'aléas (vagues de froid, mauvaises récoltes,
afflux d'ouvriers…) :

```bash
dotnet run --project src/RailTycoon.Harness -- --scenario data/heartland-events.json --ticks 720
```

Même économie encore, au caractère près ; seul le bloc `events` s'ajoute. Le
harnais affiche le journal public des événements déclenchés, avec leurs dates de
jeu. Les dates, les sources et ce qui n'est qu'« inspiré de » sont dans
[docs/SOURCES.md](docs/SOURCES.md).

Pour la conjoncture — expansion, ralentissement, crise, reprise, qui déplacent les
taux d'emprunt, les cours et, un peu, la demande ; la panique de 1873 y force la
crise :

```bash
dotnet run --project src/RailTycoon.Harness -- --scenario data/heartland-cycle.json --ticks 2160
```

L'économie et la finance de `heartland-finance`, les événements de
`heartland-events`, au caractère près ; seul le bloc `cycle` s'ajoute (et un bloc
`objectives`, qui ne fait qu'observer). Six ans de jeu plutôt que deux, pour contenir
la panique et au moins un cycle complet. Le harnais affiche le journal de la conjoncture — la phase, sa date, sa cause, jamais la date où
elle finira — et, dans le volet financier, le taux de chaque obligation décomposé.

Le même scénario porte aussi des **objectifs** — une fortune à amasser, des
cargaisons à livrer — et la sierra une liaison à faire de part et d'autre du col : le
harnais affiche, pour chaque palier, sa cible, son échéance et s'il est atteint (et
quand), manqué ou en cours. Le module ne fait qu'observer : la partie est la même avec
et sans. Ce que « fortune », « livré » et « relier » veulent dire, et pourquoi, est
dans [docs/FINDINGS.md](docs/FINDINGS.md), « Les objectifs ».

Le harnais écrit six CSV dans `out/` (`markets.csv`, `company.csv`,
`industries.csv`, `events.csv`, `cycle.csv`, `objectives.csv`), affiche la matrice des
prix finaux, la dispersion des prix par marchandise, l'utilisation des usines, le
compte d'exploitation et, si le scénario en déclare, les journaux des événements, de
la conjoncture et des objectifs.

## Tester

```bash
dotnet run --project tests/RailTycoon.Tests
```

La suite vérifie les invariants — conservation de la matière, positivité des
stocks, bornes de prix, équilibre de la trésorerie, et côté finance l'équilibre du
bilan au centime à chaque tick. Elle compare aussi chaque scénario livré à une
**trace de référence** figée : tout changement de comportement de la simulation
la fait échouer, et doit être justifié dans le commit qui met l'empreinte à jour.
Elle n'utilise aucun paquet NuGet et fonctionne hors ligne. La variable
d'environnement `RAILTYCOON_TESTS` restreint la suite aux tests dont le nom la
contient (par exemple `RAILTYCOON_TESTS=véhicules`) : c'est l'outil de la
vérification par mutation, pas un substitut à la suite complète.

Avant toute simulation, vérifier que le scénario est réalisable :

```bash
dotnet run --project src/RailTycoon.Harness -- --balance
```

Ce bilan statique ne simule rien et détecte les pénuries structurelles — le cas
où tout tourne correctement et où les marchés concernés restent pourtant collés au
plafond de prix à jamais. Sur un scénario à événements, il affiche aussi le biais
attendu du catalogue aléatoire : un catalogue qui ne frappe que dans un sens est
un déséquilibre structurel du même genre. Sur un scénario à conjoncture, la demande
et le multiple moyens sur un cycle, et la poussée nette des événements.

Pour la sierra — l'économie de heartland posée sur un relief, `data/sierra.json` —
sous le modèle de coût « mass », où le coût kilométrique suit la masse remorquée et
où le transporteur décide sur ce qu'un chargement ajoute vraiment à la facture,
relief compris :

```bash
dotnet run --project src/RailTycoon.Harness -- --scenario data/sierra-marginal.json
```

Même économie que `sierra.json`, au caractère près ; seul le modèle de coût change.
Ce qu'il fait au relief et au résultat est dans [docs/FINDINGS.md](docs/FINDINGS.md),
« Le coût marginal réel ».

Pour les véhicules — des locomotives du catalogue achetées par la compagnie, un
carburant payé à chaque arrêt au prix local, un entretien par tick, une vitesse que
la puissance, l'adhérence et la masse du train ralentissent en rampe :

```bash
dotnet run --project src/RailTycoon.Harness -- --scenario data/sierra-vehicules.json
```

`sierra-marginal` au caractère près, plus le bloc `vehicles` et trois 4-4-0 de 1870.
Le harnais affiche le parc — prix, kilomètres par tick, part des tronçons montés en
plusieurs passes faute d'adhérence, carburant, entretien — et où les trains ont fait
le plein, à quel prix. Changer de machine est une ligne :
`jq '.trains[].locomotive = "lv_consolidation"' data/sierra-vehicules.json` ; le
catalogue se lit relativement au scénario (`vehicles.catalogPath`), donc une copie
écrite ailleurs que dans `data/` doit y pointer, par exemple
`| .vehicles.catalogPath = "../data/locomotives.json"` pour une copie dans `out/`. Ce que le choix de la machine
change, et pourquoi il dépend de la carte, est dans FINDINGS.md, « Les véhicules ».

Pour un scénario posé sur un relief, le devis de construction se lit de la même
façon, sans rien simuler :

```bash
dotnet run --project src/RailTycoon.Harness -- --scenario data/terrain-pass.json --survey
```

Il ventile chaque tronçon entre pose de voie, terrassement, ponts et tunnels. C'est
là que se décide s'il faut contourner, franchir ou percer : sur la carte du col, le
même relief coûte 220 000 en le contournant par la vallée, 1 220 000 en le
franchissant au col, et 2 690 000 en l'attaquant de front.

## Ce qu'il faut regarder dans la sortie

| Symptôme | Diagnostic |
|---|---|
| Un rapport offre/demande loin de 1 | Pénurie ou surabondance structurelle : rien d'autre ne mérite d'être analysé avant correction |
| Écart de prix proche de ×1 partout | Rien à transporter : pas de jeu |
| Une marchandise au plafond > 50 % du temps | Son prix n'est plus une information, c'est une constante |
| Une usine bloquée à 0 % | Mal approvisionnée, ou non rentable par construction |
| Une marge kilométrique anormalement élevée | **Suspecter une faille d'arbitrage**, pas un succès |
| Une rotation du fret très au-dessus de 1 | Le transporteur revend de ville en ville ce qu'il vient de livrer : légitime si chaque revente paie un vrai écart, suspect sinon |
| Trésorerie en croissance monotone et lisse | L'arbitrage ne se referme pas : trop facile |
| Un écart de bilan non nul, même d'un centime | **Fuite comptable**, jamais un résidu de calcul : la finance est tenue en `decimal` |
| Une compagnie sous administration | Le découvert a dépassé ce que ses capitaux propres gagent ; les trains sont à l'arrêt |
| Une part élevée de tronçons « coupés » dans le parc | La machine n'a pas l'adhérence de ses trains sur les rampes de la ligne : elle les monte en plusieurs passes, et perd ses trajets |
| Un remplissage à 0 % avec des achats de matériel | La flotte a coûté plus que la caisse ; sans finance, le transporteur n'achète plus rien et les trains roulent à vide |
| Une fortune de magnat qui bondit quand on raccourcit les phases | La pompe du flottant infini sur un cours qui oscille, pas un gain de jeu (voir FINDINGS, « Le cycle économique ») |
| Un palier de fortune atteint un soir d'ordre de bourse, perdu le lendemain | La fortune lue au jour le jour sur un flottant infini : un achat sur marge réévalue toute la position (voir FINDINGS, « Les objectifs ») |
| Un objectif de livraisons atteint en quelques semaines | Vérifier qu'il compte le livré net (vendu moins racheté) : compter les ventes compte la revente, des dizaines de fois pour la nourriture |
| Un écart de quelques pourcents entre deux parties | **Rien**, tant qu'il n'est pas mesuré sur un ensemble : un choc de 0,1 % déplace le résultat de heartland de ±4 % (voir FINDINGS) |

Les statistiques ignorent une période de chauffe (`--warmup`, 90 ticks par défaut)
et ne mesurent que les marchés ayant de vrais acheteurs. Les deux précautions sont
nécessaires : voir [docs/FINDINGS.md](docs/FINDINGS.md), où les premières
conclusions tirées de l'instantané final se sont toutes révélées fausses.

## Structure

```
src/RailTycoon.Sim/        bibliothèque de simulation, sans dépendance moteur
  Core/                    déterminisme, horloge, maths
  Economy/                 marchandises, marchés, prix, production
  Network/                 relief, tracé, terrassement, graphe de voies
  Transport/               lignes, trains, échanges
  Finance/                 société, emprunts, bourse, OPA (comptabilité en decimal)
  Events/                  événements historiques et aléatoires, journal public
  Cycle/                   conjoncture : phases, taux, bourse, demande, journal public
  Objectives/              objectifs de scénario : fortune, livraisons, liaisons (observateur pur)
  Telemetry/               traces CSV et invariants
src/RailTycoon.Harness/    exécutable en ligne de commande
tests/RailTycoon.Tests/    invariants, sans dépendance externe
data/                      scénarios et cartes (données de conception, modifiables sans recompiler)
  heartland.json           référence de l'économie, sans finance ni relief
  heartland-finance.json   même économie, volet financier activé
  heartland-events.json    même économie, événements historiques (1871-1873) et aléatoires
  heartland-cycle.json     finance et événements réunis, et la conjoncture (1870-1875) ; objectifs de fortune et de livraisons
  ironpeak.json            chaîne minerai → fonte → acier
  terrain-*.json           cartes d'essai du réseau : plaine, vallée, col
  sierra.json              économie de heartland sur une sierra : relief et économie ensemble ; une liaison à faire
  sierra-marginal.json     même sierra, coût d'exploitation proportionnel à la masse (coût marginal réel)
  sierra-vehicules.json    même sierra, trains tirés par des locomotives du catalogue achetées
  locomotives.json         catalogue historique (masse, puissance, carburant, prix), sources dans docs/SOURCES.md
docs/                      architecture et contrats entre modules
```

Voir [docs/VISION.md](docs/VISION.md) pour ce qu'est le jeu et ce que le joueur
y décide, [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) pour les règles non
négociables, [docs/CONTRACTS.md](docs/CONTRACTS.md) pour la répartition des
modules, et [docs/FINDINGS.md](docs/FINDINGS.md) pour les résultats de la première
campagne de mesure — y compris les quatre bugs qui laissaient tous la simulation
tourner sans erreur.

## État

Le prototype est concluant : la boucle économique tient, et investir en capacité
referme l'arbitrage dont on vivait. Le profit culmine à trois trains et s'effondre
à six, avec un arbitrage réel entre rentabilité et qualité de service. Les
chiffres sont dans [docs/FINDINGS.md](docs/FINDINGS.md).

## Cap technique

C# / .NET 8, sans dépendance à un moteur. La simulation pourra être intégrée
telle quelle dans Godot 4 ou Unity le jour où il faudra une interface, sans
réécriture — c'est la seule raison du choix de C# plutôt qu'un langage de
prototypage plus rapide.
