# Résultats du prototype économique

*Première campagne de mesure — 27 septembre 2026, scénario `heartland`, 720 ticks (deux années de jeu).*

## La question posée

Le prototype existe pour répondre à une seule question, avant d'engager des
années de production : **la boucle économique de Railroad Tycoon est-elle
intéressante quand elle se réduit à des chiffres dans un terminal ?**

Concrètement : est-ce qu'investir dans son réseau referme l'occasion dont on
vivait, forçant le joueur à chercher la suivante ? Ou est-ce qu'une ligne
rentable le reste pour l'éternité ?

## Réponse : oui, la tension existe

Le profit, à économie constante, en fonction du nombre de trains :

| trains | usines desservies | résultat net | marge / km |
|---|---|---|---|
| 1 | 73 % | 89 356 | 1,03 |
| 2 | 76 % | 206 533 | **1,20** |
| 3 | 77 % | **248 807** | 0,96 |
| 4 | 80 % | 238 136 | 0,69 |
| 6 | 81 % | 93 052 | 0,18 |

Deux propriétés en sortent, et ce sont les bonnes :

1. **Le rendement du capital s'inverse.** La marge kilométrique culmine à deux
   trains, le résultat total à trois, et six trains rapportent moins qu'un seul.
   Ajouter de la capacité comprime les écarts de prix dont cette capacité se
   nourrit. Le joueur ne peut pas gagner en empilant des trains : il doit trouver
   où les mettre.
2. **Servir l'économie et maximiser le profit ne sont pas la même chose.** Le
   taux d'usines desservies monte régulièrement de 73 % à 81 % pendant que le
   résultat s'effondre. Il y a là un arbitrage de magnat, pas un optimum unique —
   exactement le genre de décision qui fait le sel du genre.

Ces chiffres viennent d'un transporteur automatique glouton doté d'une
information parfaite sur les prix courants. Un joueur humain fera moins bien, ce
qui laisse d'autant plus de marge de progression.

## Quatre bugs, et ce qu'ils enseignent

Les quatre ont été trouvés pendant cette campagne. Chacun laissait la simulation
tourner et tous les invariants techniques satisfaits. C'est la leçon centrale :
**un invariant dit que rien n'est cassé, pas que le jeu fonctionne.**

### 1. Le lavage de fret

Le transporteur déchargeait ses vingt-quatre chargements dans une ville, le prix
local s'effondrait, et il les rachetait aussitôt au même arrêt puisque la ville
suivante était encore en pénurie. Marge encaissée à chaque tronçon, fret tournant
en rond. La compagnie a gagné **deux millions pendant que les six usines étaient
à l'arrêt et tous les marchés au plafond**.

*Correction* : une ville conserve une couverture de ses propres besoins avant de
céder quoi que ce soit (`EconomyDef.RetainedCoverage`). Un site sans consommation
locale — ferme, mine, moulin vis-à-vis de sa farine — cède tout.

*Leçon* : une rentabilité anormalement élevée est un symptôme, jamais une bonne
nouvelle. D'où la sentinelle de marge kilométrique dans les tests.

### 2. Le terminus aveugle

Le sens de marche d'un train n'était retourné qu'*après* son échange à l'arrêt.
Arrivé à un terminus, le train regardait donc les destinations qu'il venait de
quitter, n'y voyait rien, et repartait à vide. Fairview, la ferme à blé, est le
terminus de la ligne : ses moulins sont restés à 1 % pendant toute la première
série de mesures.

*Leçon* : un bug d'ordonnancement d'une ligne se déguise parfaitement en problème
d'équilibrage. Sans le tableau d'utilisation des usines, il aurait été « corrigé »
en ajoutant des trains.

### 3. Le prix d'une marchandise que personne ne veut

Le modèle initial appliquait un plancher de demande commun, qui donnait à tout
marché vide un prix de pénurie de 3 × la référence — y compris pour des
marchandises que la ville ne consomme pas. Le transporteur y voyait des
destinations lucratives et acheminait blé et farine vers Northgate, qui n'en
consomme pas un gramme.

*Correction* : un marché sans acheteur vaut le prix plancher, explicitement.

*Effet de bord aussitôt révélé* : un moulin ne consomme pas sa propre farine. En
valorisant sa production au prix local, le test de rentabilité concluait que
moudre n'est jamais rentable, et **toutes** les usines du scénario se sont
arrêtées. Une usine paie ses intrants au prix local, mais valorise sa production
au prix de référence : elle produit pour expédier.

### 4. Une chaîne de valeur jamais calibrée

Le moulin transformait 20 de blé en 22 de farine — 10 % de marge, en dessous des
15 % qu'il exige pour démarrer. Il était **structurellement incapable de
tourner**. La scierie, elle, avait 350 % de marge et tournait quoi qu'il arrive,
insensible au prix de ses grumes.

*Correction* : chaque maillon ajoute environ 70 % de valeur. Cette marge est la
réserve dans laquelle l'usine absorbe un intrant devenu cher avant de s'arrêter.
Trop mince, elle ne démarre jamais ; trop large, le prix des intrants cesse de
compter et le joueur n'a plus de prise.

*Leçon* : les prix de référence et les recettes ne sont pas des données
indépendantes. C'est un système, et `--balance` doit le vérifier.

## Un seuil qu'il fallait calculer, pas deviner

Un moulin ne tourne que si son blé reste sous 1,478 × le prix de référence, ce
qui exige une couverture de 0,515. Avec un horizon de 30 jours et une demande de
2 chargements par tick, cela représente **31 chargements pour une capacité de
train de 24** : aucune livraison ne pouvait démarrer l'usine.

L'horizon de couverture est passé à 15 jours. Il en faut désormais 16, et un seul
train suffit à débloquer un site — ce qui est la décision que le joueur doit
pouvoir prendre. Cette interaction entre l'horizon de prix, la capacité d'un
train et le seuil de rentabilité d'une usine ne se voit dans aucun des trois
paramètres pris séparément.

## Les outils qui ont permis tout ça

Ils valent plus que le code de simulation lui-même, et méritent d'être maintenus
en priorité.

**`--balance`** — bilan offre/demande statique, sans simuler. Les trois pénuries
structurelles du premier jet se lisaient en six lignes. Un déséquilibre
structurel ne ressemble pas à un bug : tout tourne, et les marchés concernés
restent collés au plafond à jamais.

**Statistiques moyennées, jamais l'instantané final.** Un marché desservi oscille
en dents de scie entre plein et vide. Lire le dernier tick, c'est tomber au
hasard sur une crête ou un creux. Les premières conclusions tirées de
l'instantané final étaient toutes fausses.

**Mesurer chez les acheteurs seulement.** Une ville qui ne consomme pas une
marchandise est au prix plancher par construction. En comptant ces marchés
fictifs, les mesures annonçaient « blé au plancher 71 % du temps » alors que le
blé s'écoulait normalement.

## Un test qui ne prouvait pas ce qu'il annonçait

*Trouvé par le module `economy`, dans le code de base — c'est-à-dire dans le mien.*

Le test « déterminisme — deux exécutions donnent la même trace » est **trivialement
satisfait** : `DeterministicRandom` est construit dans `Simulation` et stocké sur
`WorldState`, mais il n'est **jamais tiré**. Zéro appel à `NextDouble`,
`NextUInt` ou `NextRange` dans tout le dépôt hors du fichier qui les définit. Il
n'y a aucun aléa dans la simulation, donc deux exécutions identiques ne peuvent
pas différer.

L'infrastructure est correcte et la règle « aucun aléa hors de
`DeterministicRandom` » reste la bonne. Mais le test ne teste rien aujourd'hui, et
il donne l'illusion inverse : il *ressemble* à une garantie de reproductibilité.

Le vrai risque qu'il était censé couvrir est l'instabilité de l'ordre de parcours
des dictionnaires, qui ne se manifeste pas forcément entre deux exécutions du même
processus. Ce qu'il faut à la place est une **trace de référence** : figer
l'empreinte attendue et la comparer. Tout changement de comportement de la
simulation devient alors visible et doit être justifié, au lieu de passer
inaperçu. À faire une fois les quatre modules fusionnés — poser une empreinte de
référence maintenant la ferait échouer à chaque fusion légitime.

Leçon générale, qui prolonge celle des quatre bugs : un test vert ne dit pas qu'il
couvre quelque chose. Celui-ci était vert depuis le premier jour.

## Questions ouvertes pour l'équipe

**Le rayon économique.** À 0,8 par kilomètre, une marchandise à bas prix ne peut
pas traverser la carte : pour les planches (référence 14), le rayon rentable est
d'environ 250 km sur une ligne de 500. La scierie d'Ironhill ne démarre jamais,
et le nord de la ligne n'a aucune source de planches viable. Ce n'est pas un bug —
c'est de la géographie économique, et c'est probablement *souhaitable* : elle
pousse à implanter l'industrie près de la ressource. Mais il faut le décider,
puis concevoir les cartes en conséquence.

**La nourriture est trop uniforme** (écart moyen ×1,4 sur dix acheteurs). Avec
trois trains et un transporteur omniscient, le réseau nourrit tout le monde au
prix de référence. Il faut vérifier si un joueur humain, ou un concurrent,
recrée de la dispersion — ou s'il faut rendre la demande plus volatile
(saisonnalité, croissance des villes).

**Le charbon ne se distribue jamais** (écart ×9 à ×13 quel que soit le nombre de
trains). À creuser : marchandise à faible valeur, source unique, demande répartie
sur neuf villes. C'est peut-être le problème logistique le plus intéressant de la
carte, ou un défaut d'équilibrage.

## Verdict

Le prototype est concluant. La boucle tient, l'arbitrage se referme quand on
investit, et il y a un arbitrage stratégique réel entre profit et service. Les
prochains chantiers — réseau sur relief, dispatching, finance — peuvent démarrer
sur cette base, chacun derrière son contrat dans [CONTRACTS.md](CONTRACTS.md).

Ce qui reste à trancher est de l'ordre du choix de conception, pas de la
faisabilité.
