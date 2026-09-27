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
Le harnais écrit trois CSV dans `out/` (`markets.csv`, `company.csv`,
`industries.csv`), affiche la matrice des prix finaux, la dispersion des prix par
marchandise, l'utilisation des usines et le compte d'exploitation.

## Tester

```bash
dotnet run --project tests/RailTycoon.Tests
```

La suite vérifie les invariants — conservation de la matière, positivité des
stocks, bornes de prix, équilibre de la trésorerie, reproductibilité de la trace,
et côté finance l'équilibre du bilan au centime à chaque tick.
Elle n'utilise aucun paquet NuGet et fonctionne hors ligne.

Avant toute simulation, vérifier que le scénario est réalisable :

```bash
dotnet run --project src/RailTycoon.Harness -- --balance
```

Ce bilan statique ne simule rien et détecte les pénuries structurelles — le cas
où tout tourne correctement et où les marchés concernés restent pourtant collés au
plafond de prix à jamais.

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
| Trésorerie en croissance monotone et lisse | L'arbitrage ne se referme pas : trop facile |
| Un écart de bilan non nul, même d'un centime | **Fuite comptable**, jamais un résidu de calcul : la finance est tenue en `decimal` |
| Une compagnie sous administration | Le découvert a dépassé ce que ses capitaux propres gagent ; les trains sont à l'arrêt |

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
  Telemetry/               traces CSV et invariants
src/RailTycoon.Harness/    exécutable en ligne de commande
tests/RailTycoon.Tests/    invariants, sans dépendance externe
data/                      scénarios et cartes (données de conception, modifiables sans recompiler)
docs/                      architecture et contrats entre modules
```

Voir [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) pour les règles non
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
