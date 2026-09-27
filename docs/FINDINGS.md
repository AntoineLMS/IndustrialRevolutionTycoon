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
| 2 | 76 % | 188 661 | **1,09** |
| 3 | 78 % | **240 374** | 0,93 |
| 4 | 80 % | 231 972 | 0,67 |
| 6 | 81 % | 98 556 | 0,19 |

*Méthode : chaque ligne reprend `data/heartland.json` en ne changeant que le
nombre de trains. Jusqu'à trois, ce sont les trains déclarés par le scénario,
si bien que la ligne « 3 » **est** heartland et se reproduit avec
`--ticks 720` sans autre option. Au-delà, les trains supplémentaires démarrent
aux arrêts 7, 2 puis 6. La position de départ compte : voir plus bas.*

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

*Traité après la fusion des quatre modules.* Le test creux a disparu, ainsi que
son jumeau côté solveur anticipant. À leur place, `ReferenceTraceTests` fige
l'empreinte de chaque scénario livré sur 720 ticks : les trois traces CSV, plus
l'état financier de fin de partie au centime quand la finance est active. Une
empreinte qui bouge sans justification dans le message de commit est une
régression.

Une limite reste ouverte : les empreintes ont été posées sous Linux x64. La
consommation passe par `Math.Pow`, que .NET délègue à la bibliothèque
mathématique du système, et rien ne garantit le même dernier bit sous Windows ou
macOS. Si la suite échoue sur une autre plateforme sans changement de code, ce
n'est pas un faux positif : la même partie n'y est pas la même, ce qui compte
pour une sauvegarde partagée ou un multijoueur en lockstep.

## Une mesure qui ne mesurait pas la bonne chose

*Relevé par le module `network`, sur le tableau ci-dessus — donc sur mon propre
travail.*

La première version de ce tableau annonçait 248 807 de résultat net à trois
trains. Le scénario de référence en produit 240 374. L'écart n'était pas une
erreur de copie : le script qui a produit le balayage **fabriquait ses propres
scénarios** et attribuait les positions de départ des trains dans l'ordre 0, 9, 4,
là où `heartland.json` déclare 0, 4, 9. Vérifié en isolant la seule variable :

| positions de départ | résultat net |
|---|---|
| 0 / 9 / 4 (le balayage) | 248 807 |
| 0 / 4 / 9 (heartland) | 240 374 |

Trois trains sur la même carte, la même économie et la même durée : **3,5 %
d'écart pour la seule répartition des départs.** C'est un enseignement en soi sur
la sensibilité du modèle à l'ordonnancement, et cela mérite d'être exploré pour
lui-même.

Mais comme mesure, le tableau était fautif. Toutes ses lignes venaient du même
générateur, donc la conclusion qualitative tenait — l'optimum et l'effondrement
sont au même endroit après correction. Le défaut est ailleurs : la ligne « 3
trains » ne décrivait pas le scénario de référence, alors que tout lecteur la
comparerait au chiffre que produit `heartland`. Le tableau a été refait en ne
changeant qu'une variable à la fois.

Leçon, et c'est la même que pour le test de déterminisme creux : un banc de mesure
mérite la même méfiance que le code qu'il mesure. Celui-ci reconstruisait son
sujet d'étude au lieu de le prendre tel quel.

## Le prix du développement en parallèle : rien n'éprouve deux modules ensemble

*Constaté à l'intégration des quatre modules, en croisant les scénarios avec les
blocs de configuration qu'ils déclarent.*

| scénario | économie anticipante | réseau sur relief | finance |
|---|---|---|---|
| `heartland` | oui | non | oui |
| `ironpeak` | **non** | non | non |
| `terrain-plain` / `-valley` / `-pass` | **non** | oui | non |
| `sierra` *(ajouté depuis)* | oui | oui | non |

Chaque module est éprouvé par le scénario que son auteur a écrit, et par aucun
autre. C'est la contrepartie attendue du travail en parallèle, et elle se voit
dès qu'on la cherche.

Le cas le plus net : passer `ironpeak` au solveur anticipant ne change **rien**,
au bit près. Ce n'est pas un défaut du solveur — c'est exactement la propriété
qu'il revendique, « à configuration neutre, la trace de la référence à
l'identique » — mais cela signifie que **ses gains ne sont pas automatiques**.
Un nouveau scénario n'en bénéficie que si quelqu'un règle son bloc
`anticipating`. Les gains mesurés sur heartland (mobilité +50 %, la nourriture
qui passe de « trop uniforme » à « exploitable ») sont des gains *de ce
scénario-là*, pas du moteur.

Deux conséquences à traiter, dans cet ordre :

1. **Aucun scénario ne fait tourner relief et économie ensemble.** Or c'est
   précisément là que se joue la question ouverte du rayon économique : le module
   réseau expose un coût kilométrique qui dépend du relief, et personne n'a encore
   mesuré ce que cela fait aux écarts de prix. Poser `heartland` sur un relief
   déplacerait tous les chiffres de ce document — c'est une décision de conception,
   pas une tâche technique, et elle mérite un scénario neuf plutôt qu'une mutation
   de la référence.

   *Partiellement traité.* `data/sierra.json` pose l'économie de heartland, table
   pour table, sur une sierra de 950 m, et la mesure est faite — voir « Relief et
   économie ensemble » ci-dessous. Elle répond à la question posée, mais pas dans
   le sens attendu : le relief ne fait **rien** aux écarts de prix, parce qu'il
   n'entre dans aucune décision. Ce qui reste ouvert n'est plus technique : il faut
   décider si et comment le relief doit peser sur les décisions, et les options y
   sont chiffrées. `heartland.json` lui-même reste sur ses distances saisies à la
   main.
2. **Les scénarios de chaque module devraient déclarer les blocs des autres**, ou
   assumer explicitement de ne pas le faire. Un bloc absent n'est pas neutre : il
   est silencieusement inerte.

   *Traité.* Chaque scénario de `data/` déclare les blocs `anticipating`,
   `network` et `finance`, ou dit pourquoi il s'en passe par une clé
   `"//<bloc>"`. Un test l'exige, et le harnais signale au chargement tout bloc
   ni déclaré ni écarté. En posant cette règle, un cas s'est révélé :
   `heartland-finance.json`, censé reprendre l'économie de heartland « au
   caractère près », n'avait pas son bloc `anticipating`. Le test d'équivalence
   restait vert pour deux raisons : il tournait sous le solveur de référence, qui
   ignore ce bloc, et il ne comparait que le *nombre* de villes, de marchandises
   et de trains. Avec `--solver anticipating`, les deux scénarios jouaient deux
   économies différentes. Le bloc est rétabli, et le test compare désormais le
   contenu des deux scénarios, sous les deux solveurs.

Leçon, et c'est la troisième du même genre après le test de déterminisme creux et
le banc de mesure qui reconstruisait son sujet : ce qui échappe à la vérification
n'est pas ce que chaque module fait, c'est **l'espace entre eux**. Quatre modules
verts ne font pas un jeu vert.

## Événements historiques et aléatoires : ce qu'ils déplacent, et ce qu'ils ne déplacent pas

*Campagne du module `events` — 27 septembre 2026, scénario `heartland-events`,
720 ticks, sous les deux solveurs.*

### La question

Le module `events` fait varier, par des événements datés ou tirés au sort, le
taux de production primaire et la demande des habitants. Trois questions :

1. Les événements font-ils **déplacer** la dispersion des prix — le critère de
   réussite du contrat `economy` — ou seulement la secouer ?
2. Répondent-ils à « la nourriture est trop uniforme » ?
3. Est-ce du jeu, ou du bruit que le transporteur ne peut pas exploiter ?

### Méthode — et une découverte qui l'a imposée

`heartland-events.json` reprend l'économie de `heartland.json` au caractère près
(un test compare leur contenu sérialisé) et n'ajoute que le bloc `events` :
cinq événements historiques (voir [SOURCES.md](SOURCES.md)) et un catalogue de
dix types aléatoires. Les statistiques sont celles du harnais : 90 ticks de
chauffe exclus, acheteurs seulement. Deux mesures s'y ajoutent, calculées par le
banc de mesure :

- **tête/mois** — la ville la plus chère en prix *moyen* sur des blocs de 30
  ticks, et la fraction des mois où elle change. « tête change » compte le
  changement d'un tick à l'autre, qui pour la nourriture tourne autour de 60 % :
  c'est le bruit de dent de scie, pas une décision. Un train met quatre à huit
  jours à traverser la ligne ; le mois est l'échelle où le joueur rouvre sa carte.
- **réponse** — ce que le transporteur livre à un marché pendant un choc de
  demande à la hausse, rapporté à ce qu'il y livrait sur la même fenêtre sans
  événements. Au-dessus de 1, il a vu l'occasion et l'a servie.

Chaque configuration aléatoire est jouée **40 fois**, `events.randomSequence`
de 11 à 50 : l'économie n'a aucun aléa, c'est la seule source de variation.

**Une partie unique ne se compare à rien.** Avant la première mesure, un contrôle :
un seul choc de 0,1 % — la demande de nourriture de Rivertown à ×1,001 pendant 38
jours — fait passer le résultat net de heartland de 240 374 à 249 090. Un choc de
même taille sur le charbon de Northgate : 246 430 ; sur le blé de Fairview :
250 368. Le modèle est sensible aux conditions initiales à ±4 % près, et comparer
heartland-events à heartland revient à comparer deux tirages. Toutes les
comparaisons ci-dessous se font donc contre un **témoin** : le même calendrier
d'aléas, à 0,1 % d'intensité. Il a les mêmes dates, les mêmes cibles, et aucun
effet économique notable.

Et le témoin dit autre chose, qui dépasse ce module : **sous le solveur de
référence, les 240 374 de heartland sont en dessous des 40 trajectoires
voisines**, qui s'étalent de 243 600 à 260 500 (médiane 253 000). Le chiffre que
trois documents citent est celui d'une trajectoire basse, pas celui du scénario.
Sous l'anticipant, heartland (232 465) tombe au milieu de ses voisines (189 000 à
256 000, 16 en dessous sur 40). La régression reste une régression — l'empreinte
fige une trajectoire, et c'est ce qu'on lui demande — mais une conclusion
d'équilibrage tirée d'une seule partie ne vaut que ce que vaut un tirage.

### Premier résultat : un choc à sens unique est un déséquilibre déguisé

Le premier catalogue ne frappait que dans un sens par marchandise : des afflux
d'ouvriers sans épidémies, des vagues de froid sans redoux, des éboulements sans
filons. Référence, 40 réalisations, résultat net en milliers ; chaque catalogue
est comparé à son propre témoin, qui joue son calendrier :

| catalogue | mobilité | tête/mois | résultat net | médiane | réponse |
|---|---|---|---|---|---|
| témoin (même calendrier, ×0,001) | 0,401 ± 0,042 | 35,4 % | 249 ± 5 | 250 | — |
| premier jet, à sens unique | 0,374 ± 0,047 | 32,9 % | 197 ± 37 | 200 | 0,88 |
| afflux d'ouvriers seuls (témoin 251) | 0,371 | 32,8 % | 207 ± 42 | 219 | 0,89 |
| épidémies seules (témoin 250) | 0,423 | 33,6 % | 278 ± 13 | 276 | — |

Les événements faisaient *moins* bouger la dispersion, et coûtaient un cinquième
du résultat. La raison se lit dans les deux dernières lignes : le résultat suit le
**surplus** de nourriture, pas sa dispersion. La carte ne produit que 10 % de
nourriture de plus que ses habitants n'en mangent ; les afflux seuls ajoutent 2 %
à la demande annuelle de la carte, soit le cinquième de ce surplus, et le
transporteur perd 44 000. Les épidémies seules le rendent, et il gagne 28 000.
C'est exactement ce que le solveur anticipant interdit à sa saison — « une
saisonnalité qui déplacerait la moyenne serait un déséquilibre structurel déguisé
en dynamique » — et le premier catalogue le faisait sans que rien ne le signale.

*Traité.* `--balance` affiche désormais le biais attendu du catalogue, marchandise
par marchandise et levier par levier (`EventCatalogBalance`), et un test exige
qu'il soit nul sur heartland-events. Le catalogue livré apparie chaque hausse à une
baisse : vague de froid et redoux, mauvaise récolte et récolte abondante, afflux et
épidémie, fièvre de construction et marasme, éboulement et nouveau filon. Leçon,
la même que celle de `--balance` : un déséquilibre structurel ne ressemble pas à
un bug.

### Ce que fait le catalogue livré

40 réalisations, résultat net en milliers. « historiques + témoin » joue les cinq
historiques avec le calendrier aléatoire à 0,1 % : c'est le témoin de la ligne
« livré ».

| solveur | configuration | mobilité | tête/mois | dominance | résultat net | médiane | usines | réponse |
|---|---|---|---|---|---|---|---|---|
| référence | heartland (une partie) | 0,411 | 34,2 % | 59,5 % | 240 | 240 | 78,3 % | — |
| référence | témoin | 0,395 ± 0,038 | 33,0 % | 62,4 % | 252 ± 4 | 253 | 78,0 % | — |
| référence | aléatoires seuls | 0,406 ± 0,045 | 35,6 % | 61,4 % | 250 ± 28 | 257 | 77,8 % | 1,00 |
| référence | historiques + témoin | 0,401 ± 0,048 | 32,9 % | 62,1 % | 251 ± 5 | 251 | 77,9 % | 0,97 |
| référence | **livré** (les deux) | 0,400 ± 0,039 | 33,4 % | 62,1 % | 250 ± 28 | 255 | 77,8 % | 1,00 |
| anticipant | heartland (une partie) | 0,615 | 35,0 % | 52,4 % | 232 | 232 | 76,4 % | — |
| anticipant | témoin | 0,600 ± 0,032 | 37,9 % | 52,8 % | 233 ± 14 | 236 | 77,3 % | — |
| anticipant | aléatoires seuls | 0,598 ± 0,040 | 39,8 % | 52,6 % | 228 ± 25 | 230 | 77,2 % | 0,98 |
| anticipant | historiques + témoin | 0,605 ± 0,043 | 35,4 % | 55,0 % | 235 ± 11 | 235 | 77,3 % | 1,08 |
| anticipant | **livré** (les deux) | 0,596 ± 0,042 | 38,6 % | 54,1 % | 229 ± 28 | 232 | 77,2 % | 1,01 |

*« usines » est le taux d'usines desservies du premier tableau de ce document :
l'utilisation moyenne des six usines. Tête/mois : écart-type entre réalisations de
4 à 5 points, soit une erreur type d'environ 0,7 point sur une moyenne de 40 ; une
différence de deux points entre deux lignes est à peu près à trois erreurs types.*

Santé du signal-prix des deux marchandises en question, moyenne des réalisations :

| solveur | configuration | nourriture : écart | mobilité | charbon : écart | mobilité | charbon au plafond |
|---|---|---|---|---|---|---|
| référence | heartland | ×1,35 | 0,142 | ×11,4 | 0,485 | 5 % |
| référence | témoin | ×1,39 | 0,280 | ×10,7 | 0,449 | 4 % |
| référence | historiques seuls | ×1,36 | 0,133 | ×9,6 | 0,471 | 8 % |
| référence | livré | ×1,38 | 0,216 | ×9,4 | 0,509 | 7 % |
| anticipant | heartland | ×2,24 | 1,286 | ×16,1 | 0,439 | 7 % |
| anticipant | témoin | ×2,15 | 1,161 | ×16,1 | 0,427 | 8 % |
| anticipant | livré | ×1,96 | 0,978 | ×14,0 | 0,563 | 12 % |

Effet local des historiques, chacun seul, sur sa propre fenêtre (rapport à la
partie sans événements, même fenêtre ; entre parenthèses, l'anticipant) :

| événement | fenêtre | prix de la cible | prix moyen chez les acheteurs | flux de la cible |
|---|---|---|---|---|
| grève de l'anthracite | ticks 369–530 | — (Coalburg n'achète pas de charbon) | ×1,32 (×1,39) | enlèvements ×0,78 (×0,68) |
| sécheresse (inspirée) | 510–629 | — | ×1,00 | enlèvements ×1,00 |
| grand incendie de Chicago | 637–720 | planches de Kingsport ×1,88 (×1,50) | ×1,09 | livraisons ×1,00 (×0,71) |
| incendie de Peshtigo | 637–720 | grumes de Pinegrove ×1,20 | ×1,04 | — |
| panique de 1873 | à partir du tick 1 337 | hors des 720 ticks mesurés | | |

Balayages, **une variable à la fois**, sur les aléatoires seuls (référence ; les
chiffres de l'anticipant suivent la même pente et sont dans les clés `"//…"` du
scénario) :

| intensité (écart à 1 × k) | tête/mois | dominance | résultat net | médiane |
|---|---|---|---|---|
| k = 0,001 (témoin) | 33,0 % | 62,4 % | 252 ± 4 | 253 |
| k = 0,5 | 34,9 % | 61,5 % | 250 ± 16 | 251 |
| **k = 1 (livré)** | 35,3 % | 61,4 % | 250 ± 28 | 257 |
| k = 1,5 | 36,9 % | 60,3 % | 243 ± 43 | 252 |
| k = 2 | 36,6 % | 59,7 % | 239 ± 58 | 250 |

| fréquence (× m) | événements en 2 ans | tête/mois | résultat net | médiane |
|---|---|---|---|---|
| m = 0,25 | 13 | 35,0 % | 253 ± 16 | 253 |
| m = 0,5 | 26 | 34,7 % | 252 ± 21 | 255 |
| **m = 1 (livré)** | 49 | 35,6 % | 250 ± 28 | 257 |
| m = 2 | 95 | 36,6 % | 247 ± 41 | 254 |
| m = 4 | 171 | 37,4 % | 236 ± 44 | 245 |

La mobilité de l'amplitude reste entre 0,39 et 0,42 sur toute l'étendue des deux
balayages : dans le bruit. La montée des aléatoires, balayée de 0 à 20 ticks, ne
change aucune statistique agrégée. Toucher toutes les villes à la fois
(`scope: "all"`) au lieu d'une : tête/mois 38,6 %, dominance 55,8 %, mobilité
0,462 — le seul réglage qui fasse vraiment respirer la dispersion —, mais résultat
193 ± 96 et une réponse du transporteur qui tombe à 0,64.

### Conclusions

**1. Les événements déplacent un peu le point chaud, pas l'amplitude.** Équilibré,
le catalogue fait changer la ville la plus chère d'un mois sur l'autre de deux à
quatre points de plus que le témoin selon l'intensité, et fait baisser la part de
la ville dominante d'autant. La mobilité de l'amplitude, elle, ne bouge dans aucun
réglage. Au regard du critère du contrat `economy`, c'est un gain réel mais
modeste, du même ordre que la saison (+6 % de mobilité) et bien en dessous de
l'anticipation (+50 %). Sous le solveur de référence, une fois les historiques
ajoutés, il ne se distingue plus de son témoin (33,4 % contre 32,9 %) ; sous
l'anticipant il tient (38,6 % contre 35,4 %).

**2. Ils ne répondent pas à « la nourriture est trop uniforme ».** Sous la
référence, l'écart moyen de la nourriture reste entre ×1,38 et ×1,41 dans toutes
les configurations équilibrées — ×1,46 au mieux à intensité double. Un choc local
fait monter le prix d'une ville ; le réseau le referme avant qu'il pèse sur la
moyenne. Ce que les chocs changent, c'est le *niveau* du surplus (voir le premier
résultat), pas le *gradient* entre villes. L'uniformité de la nourriture est
structurelle — deux boulangeries au milieu de la ligne, trois trains qui la
parcourent en entier — et c'est l'anticipation qui la levait déjà (×2,2).

**3. Le charbon non plus ne se distribue pas mieux, il se resserre.** Avec les
historiques, dont la grève de 1871 qui fait monter le charbon de 32 % chez tous
ses acheteurs, l'écart du charbon *baisse* de ×11,4 à ×9,6 : une pénurie pousse
tous les prix vers le plafond, les villes proches de la mine rattrapent les
lointaines, et le prix y informe moins.

**4. Pour ce transporteur, c'est du bruit.** Sa réponse aux chocs de demande vaut
1,00 : il ne livre ni plus ni moins aux villes touchées. Ce n'est pas qu'il ne voit
pas le prix — il est omniscient —, c'est qu'il ne peut rien en faire : trois trains
en navette sur une ligne unique, qui achètent ce qui paie le mieux *devant eux* et
ne changent jamais de parcours. Un choc de 30 jours à une ville ne change ni son
passage ni sa cargaison. Le résultat n'en souffre pas en médiane, mais son
écart-type passe de 4 000 à 28 000 : les événements ajoutent du risque sans
ajouter de prise. Ce qu'un joueur ferait du journal — réaffecter un train vers la
ville en pénurie, parce qu'il sait que la vague de froid finit dans douze jours —
ce banc ne sait pas le mesurer. C'est la limite de l'instrument, pas du module.

**5. Le blé est bridé par le transport, pas par la récolte.** La sécheresse de
1871 ne se voit pas, même poussée à ×0,35 sur Fairview : elle vide le carreau sans
changer ce que le train emporte (137 chargements sur la fenêtre avec ou sans elle,
129 à ×0,35). Un événement de production n'a de prise que sur un site que le
transport ne sature pas.

### Décisions laissées à l'équipe

**Que doivent faire les aléatoires ?** Quatre réglages chiffrés, référence, contre
le témoin (tête/mois 33,0 %, dominance 62,4 %, résultat 252 ± 4) :

| option | tête/mois | dominance | résultat net | pour | contre |
|---|---|---|---|---|---|
| a. catalogue livré (k = 1, m = 1, une ville) | +2,6 pts | −1,0 pt | 250 ± 28, médiane neutre | de la variété sans coût médian | un gain modeste, qui disparaît sous la référence une fois les historiques ajoutés |
| b. intensité ×1,5 | +3,9 pts | −2,1 pts | 243 ± 43 | la tête bouge davantage | −9 000 en moyenne, risque ×1,5 |
| c. toutes les villes à la fois | +5,6 pts | −6,6 pts | 193 ± 96 | le seul réglage qui fasse respirer l'amplitude (+0,07) | c'est une saison ; le transporteur perd un quart |
| d. historiques seuls, pas d'aléatoires | −0,1 pt | −0,3 pt | 251 ± 5 | aucune variance ajoutée, des épisodes lisibles | aucun gain de mobilité |

L'option (a) est livrée parce qu'elle est la seule qui ne coûte rien. Elle ne se
défend pas par la mesure de la dispersion ; elle se défendra, ou non, en jouant.

**Faut-il annoncer la fin d'un événement ?** Le journal publie la date de fin dès
le déclenchement : une décision de transport se prend sur un horizon, et c'est cet
horizon qu'on rend jouable. Une grève dont personne ne connaît la fin serait plus
réaliste. Le transporteur glouton n'utilisant pas l'information, la mesure ne peut
pas trancher ; il faudra un concurrent qui lise le journal (contrat `ai`).

**Équilibrer par les données ou par le mécanisme ?** Le catalogue est équilibré
*en espérance*, par ses fréquences : sur une partie donnée, le hasard peut tirer
trois afflux et aucune épidémie. L'alternative est un mécanisme à somme nulle —
un afflux à une ville pris sur la demande des autres, une migration plutôt qu'une
naissance — qui garantirait l'équilibre à chaque tick. Non implémenté : c'est la
croissance des villes sans sa rétroaction sur le prix, et la croissance a montré
qu'une redistribution peut comprimer la dispersion au lieu de la déplacer. À
mesurer avant de choisir.

**Que faire du chiffre de référence de heartland ?** Sous la référence, 240 374 est
en dessous de ses 40 voisines. Le tableau « trains / résultat net » du début de ce
document est construit sur des parties uniques ; sa conclusion qualitative —
l'optimum à trois trains, l'effondrement à six — mérite d'être revérifiée sur des
ensembles de trajectoires voisines avant qu'on s'en serve pour équilibrer. Coût :
une quarantaine de parties par point, soit quelques secondes.

**Ce que le module ne fait pas encore**, et qui changerait peut-être la réponse :
des événements sur les usines (un moulin en grève) ou sur le transport (une voie
coupée, qui toucherait enfin le transporteur plutôt que les marchés), et un
scénario où le transport ne sature pas la production, pour que les événements de
production aient prise.
## Relief et économie ensemble : le relief est un impôt, pas une géographie

*Deuxième campagne — 27 septembre 2026, scénario `sierra`, 720 ticks, 90 de
chauffe exclus, statistiques moyennées chez les acheteurs seulement, sous le
solveur de référence et sous l'anticipant.*

### Le scénario

`data/sierra.json` reprend l'économie de heartland **table pour table** —
marchandises, recettes, demandes, capacités, ordre des villes le long de la ligne
— et la pose sur un relief : 505 km de voie, une plaine à 150 m de chaque côté, le
plateau forestier de Pinecrest à 378 m, et la mine de Coalpass dans un col à
1 112 m. On y monte par 786 m de dénivelé cumulé depuis l'ouest et par 961 m
depuis l'est, à la rampe maximale de 2,5 %. Reprendre des tables calibrées isole la seule variable
neuve, la géographie : `--balance` donne chaque marchandise entre 1,10 et 1,14, et
`--survey` un devis de 1 524 566, à 99 % de pose de voie, sans pont ni tunnel. La
montagne pèse sur l'exploitation (la rampe est coûte 44 % de plus à la montée),
pas sur la construction.

### Méthode

Chaque ligne des tableaux est le scénario **tel qu'il est écrit**, avec une seule
valeur changée par un filtre `jq` : le nombre de trains (les *n* premiers trains
déclarés, puis des copies du premier aux arrêts 7, 2, 6, comme le tableau de
heartland), `network.traction.climbEquivalentKm`, l'amplitude du relief ou de sa
rugosité, `haulage.expectedLoadFactor`. La ligne de référence de chaque tableau
reproduit l'empreinte figée dans `ReferenceTraceTests` (`E60F3BB3F1F2B083` sous la
référence, `6CEA03DA2C0CDB89` sous l'anticipant) : le banc mesure le scénario, il
ne le reconstruit pas. Toutes les lignes se relisent dans la sortie du harnais,
par exemple `jq '.network.traction.climbEquivalentKm = 0' data/sierra.json >
out/s.json` puis `--scenario out/s.json` : l'« Empreinte » qu'il affiche est celle
de la seule trace des marchés, qui dit si un réglage a changé un prix quelque part.
Deux lignes y ont été ajoutées pour cette campagne : « dont relief » sous le coût
d'exploitation (le facturé moins les mêmes kilomètres à plat) et la rotation du
fret (chargements livrés ÷ produits, après chauffe). Le relevé de la mécanique
imputée du transporteur (« gain » contre « coût imputé » par couple de villes) et
la variante « relief décidé » viennent d'un banc hors dépôt ; la seconde se
reproduit avec la ligne de code donnée plus bas.

### Le nombre de trains : même forme que heartland

| trains | résultat (référence) | résultat (anticipant) | usines desservies | mobilité réf. / ant. | écart planches réf. / ant. | écart charbon réf. / ant. |
|---|---|---|---|---|---|---|
| 1 | 76 695 | 76 243 | 72 % | 0,432 / 0,404 | ×4,7 / ×8,0 | ×12,7 / ×18,4 |
| 2 | 178 139 | 160 108 | 75 % | 0,442 / 0,404 | ×4,9 / ×7,1 | ×10,6 / ×15,3 |
| 3 | **219 411** | **242 254** | 78 % | 0,384 / 0,548 | ×4,8 / ×6,0 | ×10,1 / ×14,3 |
| 4 | 200 890 | 228 096 | 81 % | 0,345 / 0,583 | ×3,8 / ×4,6 | ×8,6 / ×13,3 |
| 5 | 134 325 | 171 811 | 82 % | 0,281 / 0,477 | ×3,5 / ×4,2 | ×8,1 / ×12,5 |
| 6 | 47 819 | 103 973 | 82 % | 0,220 / 0,427 | ×3,4 / ×3,8 | ×8,9 / ×12,8 |

*Usines desservies : utilisation moyenne sous le solveur de référence ;
l'anticipant est à deux points près. La ligne « 3 » est le scénario sans option.*

L'optimum est à trois trains sous les deux solveurs, le service monte pendant que
le résultat s'effondre, la dispersion se referme quand on ajoute de la capacité :
tout ce que la première campagne avait établi sur heartland tient sur relief. La
scierie de Cedarton, à 355 km de sa forêt et de l'autre côté du col, reste à 0 %
à tous les nombres de trains, comme celle d'Ironhill.

### Le résultat central : la trace des marchés ne voit pas la montagne

Trois trains, solveur de référence, une variable à la fois :

| variante | surcoût du relief | exploitation | résultat net | trace des marchés |
|---|---|---|---|---|
| relief gratuit (`climbEquivalentKm` = 0) | 0 % | 207 360 | 237 647 | `A59A1898…` |
| **scénario tel qu'écrit (0,03)** | 8,8 % | 225 596 | 219 411 | `A59A1898…` |
| traction × 3 (0,09) | 26,4 % | 262 068 | 182 940 | `A59A1898…` |
| traction « physique » (0,2) | 58,6 % | 328 933 | 116 075 | `A59A1898…` |
| montagne seule (rugosité à 0) | 5,9 % | 219 642 | 225 365 | `A59A1898…` |
| rugosité seule (montagne à 0) | 4,3 % | 216 380 | 228 628 | `A59A1898…` |
| relief entièrement plat | 0 % | 207 360 | 234 085 | `EA95EF39…` |

**Les six premières lignes ont la même trace des marchés au bit près.** Mêmes prix
à chaque tick dans chaque ville, donc mêmes écarts, même mobilité, mêmes usines,
mêmes flux ; seul le résultat net change, du montant exact du surcoût. Sous
l'anticipant, même constat (`5CF06DE1…` pour les six, résultats 260 490 /
242 254 / 205 782 / 138 917 de 0 à 0,2). La dernière ligne diffère, mais pas à
cause de la montagne : sur un relief parfaitement plat, les tronçons mesurent
exactement 50, 55, 75 km au lieu de 50,0004, 55,0012, 75,0066, et un train qui
atteignait un arrêt au tout début d'un tick l'atteint à la fin du précédent. Dix-huit
mètres sur 505 km suffisent à déplacer le résultat de 1,5 % : c'est la sensibilité
à l'ordonnancement déjà relevée plus haut (3,5 % pour la seule position de départ
des trains), pas un effet du relief.

La raison est dans le code, et elle est double :

1. **Le relief est facturé mais jamais décidé.** Le coût kilométrique d'un tronçon
   est multiplié par son facteur de relief au moment où le train le parcourt
   (`MoveTrain`), mais le coût que le transporteur impute à un chargement pour
   décider de l'acheter (`HaulCostPerUnitAhead`) se calcule sur la distance plate.
   Le commentaire de `RailLine.LegCostFactor` affirmait que le facteur « rétrécit
   le rayon économique des marchandises à bas prix dans cette direction » : c'était
   faux, il est corrigé.
2. **Même décidé, il ne serait pas un coût marginal.** Les trains roulent de toute
   façon, 120 km par tick, pleins ou vides. Le kilomètre est payé d'avance ; le
   relief n'est donc pas le prix de *transporter* quelque chose, c'est le prix
   d'*avoir* un train sur la ligne. Refuser un chargement parce qu'il franchit le
   col n'économise rien.

La seule chose que la montagne change réellement est ce que coûte chaque train :
un impôt fixe d'environ 6 100 par train sur 720 ticks à 0,03, de 40 500 à 0,2. Il
aplatit l'optimum sans le déplacer : à 0,2, deux trains font 109 268 contre
116 075 pour trois (−6 %), là où l'écart est de 19 % à 0,03.

### Le rayon économique est un réglage de l'instrument, pas de l'économie

Puisque le kilomètre est payé d'avance, qu'est-ce qui borne le rayon de 250 km
cité dans les questions ouvertes ? Le coût que le transporteur *impute*, qui
répartit le coût kilométrique sur une charge escomptée (`expectedLoadFactor`).
Une variable à la fois, solveur de référence, trois trains :

| `expectedLoadFactor` | sierra : résultat | sierra : Cedarton | heartland : résultat | heartland : Ironhill |
|---|---|---|---|---|
| 0,3 | −185 356 | 0 % | −164 099 | 0 % |
| **0,6 (tel qu'écrit)** | **219 411** | 0 % | **240 374** | 0 % |
| 1,0 | 243 784 | 0 % | 260 829 | 0 % |
| 2,0 | 249 952 | 19 % | 277 436 | 20 % |
| 100 (coût imputé ≈ 0) | 171 136 | 20 % | 198 120 | 19 % |

*Les variantes de heartland ont été mesurées sur une copie hors du dépôt ;
`heartland.json` n'est pas modifié.*

Imputer moins de coût — ce qui est plus fidèle à un kilomètre déjà payé — rapporte
**14 à 15 % de plus** sur les deux cartes, et démarre la scierie lointaine à 20 %.
Ce n'est pas un optimum à adopter : à 100, le transporteur accepte des marges
minuscules, encombre ses trains et perd 18 à 22 % par rapport au réglage actuel ;
à 0,3 il n'achète presque plus rien, et la trésorerie de la sierra descend à
−85 356 faute de module finance pour arrêter les trains. Mais cela établit que
**la scierie d'Ironhill ne meurt pas de la distance, elle meurt d'une règle de
gestion du transporteur**. Même mécanisme à Cedarton : ses grumes y valent 12,4
en moyenne contre 3,0 à Pinecrest, un gain de 7,6 par chargement contre un coût
imputé de 19,7 à 355 km.

Les planches obéissent à une autre règle de l'instrument. À Pinecrest elles valent
1,7 ; à Cedarton et Farport, 40 à 42, soit un gain de 36 à 38 contre 21 à 24 de
coût imputé : rentable selon les propres critères du transporteur. Elles n'y
arrivent pourtant presque jamais (8 chargements en 630 ticks, contre 273 à
Fordham) : le transporteur vend au premier acheteur qui lui laisse 10 % de marge
(`SellHere`), et tout acheteur la lui laisse sur une marchandise payée au
plancher. Le rayon des planches est la distance au prochain acheteur, pas un
rapport entre prix et coût.

### Ce que ferait le relief s'il entrait dans les décisions

Mesuré en multipliant, dans `HaulCostPerUnitAhead` seulement, la distance par le
facteur de relief du trajet — une ligne de code, **non retenue** (voir plus bas) :

```csharp
double km = train.Line.DistanceBetween(train.StopIndex, bestIndex)
          * train.Line.LegCostFactor(train.StopIndex, bestIndex);
```

| traction | statu quo (référence) | relief décidé (référence) | statu quo (anticipant) | relief décidé (anticipant) |
|---|---|---|---|---|
| 0,03 | 219 411 | 207 810 (−5 %) | 242 254 | 236 380 (−2 %) |
| 0,09 | 182 940 | 167 917 (−8 %) | 205 782 | 166 412 (−19 %) |
| 0,2 | 116 075 | −50 486 | 138 917 | −25 278 |

Là, une géographie apparaît. À 0,2, l'écart des planches passe de ×4,8 à ×9,7 :
elles s'entassent à l'ouest (0,43 × la référence à Westbrook, 0,54 à Fordham,
contre 0,99 et 0,94) et l'est reste au plafond, parce que le transporteur refuse de
leur faire passer le col. Deux bassins économiques séparés par la montagne —
exactement ce qu'on attendait du relief. Mais **la compagnie y perd** dans tous les
cas, parce qu'elle refuse un fret que ses trains, qui passent le col de toute
façon, auraient porté sans surcoût. Brancher le relief sur la décision sans rendre
le coût marginal produit une géographie en détruisant de la valeur : c'est
exactement le genre de correctif qui aurait l'air juste et serait faux.

Il n'est pas retenu pour une seconde raison : il déplace les empreintes des trois
cartes `terrain-*` (`2FCED02A…`, `B1B62C35…`, `EE7A71F2…`). C'est une décision de
conception, pas un bug à corriger en passant.

### Deux mesures du relief qui ne mesurent pas ce qu'on croit

**La rugosité coûte presque autant que la montagne.** Le facteur de relief convertit
la somme des dénivelés positifs du profil, et le profil suit chaque bosse que la
rampe maximale n'oblige pas à raboter. Sur la sierra, 20 m de rugosité font
gravir 58 à 86 m à chaque tronçon de plaine de 45 à 60 km : la rugosité seule
coûte 4,3 %, la montagne seule 5,9 %. Sur les cartes d'essai, dont la rugosité est
plus forte (25 m, 4 km), même décomposition — une variable à la fois, facteurs
aller / retour :

| carte (60 km) | tel qu'écrit | rugosité seule | formes seules | devis |
|---|---|---|---|---|
| plaine | 1,118 / 1,123 | 1,108 / 1,114 | 1,060 / 1,059 | 180 093 |
| vallée | 1,159 / 1,172 | 1,089 / 1,103 | 1,100 / 1,100 | 219 402 |
| col | 1,165 / 1,162 | 1,107 / 1,103 | 1,132 / 1,132 | 1 219 880 |
| crête (tunnel de 19 km) | 1,212 / 1,222 | 1,078 / 1,088 | 1,185 / 1,185 | 2 689 446 |

La rugosité seule coûte 8 à 11 % sur chacune, autant que le mamelon de la
« plaine » et presque autant que la montagne du col (13 %). Et la crête percée
d'un tunnel de 19 km — 1,5 million de plus que le col — coûte *plus* cher à
exploiter que le col : le tunnel abaisse le point haut, mais les rampes d'accès
gravissent davantage. Rien, à l'exploitation, ne récompense le choix de percer.

**Le commentaire qui justifiait 0,03 justifie 0,2.** `TractionDef` le motivait par
« une rampe de 1 % triple la résistance au roulement ». À 0,03, un kilomètre à 1 %
(10 m gagnés) coûte 1,3 kilomètre de plat ; tripler la résistance correspondrait à
environ 0,2. Le commentaire est corrigé pour dire ce que vaut la valeur, sans la
changer : la choisir est une décision.

### Observation : pourquoi les cartes d'essai perdent 75 000 à 78 000

C'est la carte, pas le relief. À `climbEquivalentKm` = 0, les trois cartes perdent
exactement 67 066 chacune — même ligne de 60 km, même train, même économie :

| carte | résultat net | dont relief | à relief gratuit |
|---|---|---|---|
| plaine | −75 391 | −8 325 (12,0 %) | −67 066 |
| vallée | −78 511 | −11 445 (16,6 %) | −67 066 |
| col | −78 366 | −11 300 (16,3 %) | −67 066 |

Un train parcourt 86 400 km en 720 ticks, soit 69 120 de coût kilométrique à plat,
qu'il transporte quelque chose ou non. En face, la seule demande de la carte est
de 0,6 chargement de blé par tick : même vendue en permanence au plafond (30),
elle rapporterait au plus 12 960. Recettes réelles 2 803, achats 749. Aucun
réglage de prix ne peut rendre ces cartes rentables : elles ont été conçues pour
chiffrer un devis, et leur résultat n'a pas de sens économique. Le relief n'y
ajoute que 12 à 17 % au coût d'exploitation, dont 8 à 11 points viendraient de
la seule rugosité. Là encore, la trace des marchés est identique avec et
sans coût de relief.

### Le bloc `anticipating` ne se recopie pas

Copié de heartland (4 ticks, poids 1, lissage 0,15), il fait tomber le résultat de
la sierra de 219 411 à 100 693 : le charbon se met à tourner en rond, 29 558
chargements livrés pour 723 produits après chauffe, contre 5 389 sous la
référence. Une grille (horizon 0 à 8, poids 0,5 et 1, lissage 0,10 à 0,25) désigne
2 ticks, poids 1, lissage 0,15 : +10 % de résultat, mobilité 0,548 contre 0,384,
changement de tête 24,3 % contre 20,1 %, avec des voisins cohérents. Le paysage
est bruité — un cran sur un réglage voisin déplace le résultat de 4 à 10 % —, et
aucune saison ne tient sur plusieurs nombres de trains (le chauffage au charbon,
+15 % de mobilité à trois trains, coûte 3,5 à 6 % de résultat à 2, 4 et 5). Les
chiffres complets sont dans les commentaires du scénario. La leçon rejoint celle
du tableau des modules : les gains de l'anticipant sont ceux d'un scénario réglé,
et un réglage d'emprunt peut diviser le résultat par deux.

En passant, le banc a compté les livraisons : sous le solveur de référence, la
nourriture est livrée 70 fois pour une fois produite, le charbon 8 fois. Le
transporteur revend d'une ville à l'autre les excédents au-delà de la réserve de
chaque ville. Ce n'est pas le lavage de fret — chaque revente paie sa marge sur un
vrai écart de prix —, mais c'est ce qui maintient la nourriture « trop uniforme »,
et cela mérite d'être regardé pour lui-même.

### Décisions à trancher par l'équipe

Aucune n'est tranchée ici. Chacune est chiffrée sur la sierra, trois trains, sauf
mention contraire.

1. **Le relief doit-il peser sur les décisions ?**
   - *Statu quo* : un impôt fixe par train, aucune géographie ; +8,8 % de coût
     d'exploitation à 0,03. Rien à coder.
   - *Relief dans le coût imputé* (une ligne) : une géographie visible à forte
     traction (écart des planches ×4,8 → ×9,7 à 0,2), mais −2 à −5 % de résultat
     à 0,03 et une perte nette à 0,2 ; déplace les trois empreintes `terrain-*`.
   - *Coût marginal réel* : ne facturer le relief qu'en proportion de la masse
     remorquée (tare + chargement), et décider sur ce seul surcoût. C'est la seule
     option où refuser un chargement économise vraiment ce qu'il coûte. Non
     mesurée : elle demande un paramètre de tare dans les données et un
     changement de `MoveTrain`, donc de toutes les traces sur relief.
   - *Des trains qui ne roulent pas à vide* : relève du module `dispatch`. Tant que
     les trains font la navette sans condition, aucun coût kilométrique n'est
     marginal, relief ou pas.
2. **Quelle valeur pour `climbEquivalentKm` ?** À 0,03 (actuel), la sierra paie
   8,8 % de relief, 18 236 sur deux ans. À 0,2 (la lecture « une rampe de 1 %
   triple la résistance »), 58,6 % : le résultat tombe de 219 411 à 116 075, et
   deux trains font presque aussi bien que trois. Entre les deux, 0,09 : 26,4 % et
   182 940.
3. **La rugosité doit-elle compter comme une rampe ?** Aujourd'hui la rugosité seule
   coûte 8 à 11 % sur 60 km, autant qu'un relief réel, et un tunnel ne fait rien
   gagner à l'exploitation. Options :
   ne compter que les dénivelés au-delà d'un seuil, ou lisser le profil avant de
   sommer — deux façons de déplacer les empreintes `terrain-*`, à décider avant
   d'écrire d'autres cartes.
4. **Le devis doit-il coûter quelque chose ?** Il n'est débité nulle part : la
   sierra coûte 1 524 566, quinze fois la mise de départ et près de quatorze ans
   de son résultat, et ce chiffre n'entre dans aucun compte. Tant que c'est le cas,
   « contourner, franchir ou percer » est un choix sans conséquence dans la
   simulation. Options : débiter au premier tick (ce qui exige la finance pour
   l'emprunter), amortir par tick, ou assumer que le réseau est donné par le
   scénario.
5. **Le rayon économique est-il une propriété voulue ?** S'il l'est, il doit venir
   d'un coût réel (option « coût marginal » ci-dessus), pas de
   `expectedLoadFactor` : passer ce réglage de 0,6 à 2 fait démarrer Ironhill et
   Cedarton et rapporte 14 à 15 % de plus. Quel que soit ce choix, la règle de
   vente au premier acheteur continuera de borner les planches à la distance du
   prochain client.
6. **Chaque scénario doit-il régler son anticipant ?** Le bloc de heartland divise
   le résultat de la sierra par deux. C'est soit une contrainte à documenter pour
   les auteurs de cartes, soit le signe d'un solveur trop sensible pour servir
   sans réglage.

## Questions ouvertes pour l'équipe

**Le rayon économique.** À 0,8 par kilomètre, une marchandise à bas prix ne peut
pas traverser la carte : pour les planches (référence 14), le rayon rentable est
d'environ 250 km sur une ligne de 500. La scierie d'Ironhill ne démarre jamais,
et le nord de la ligne n'a aucune source de planches viable. Ce n'est pas un bug —
c'est de la géographie économique, et c'est probablement *souhaitable* : elle
pousse à implanter l'industrie près de la ressource. Mais il faut le décider,
puis concevoir les cartes en conséquence.

*Précisé par la campagne sur relief* (« Relief et économie ensemble »,
ci-dessus) : ce rayon n'est pas fixé par les 0,8 par kilomètre, que les trains
paient qu'ils transportent ou non, mais par le coût que le transporteur *impute* à
un chargement (`expectedLoadFactor`) et, pour les planches, par sa règle de vente
au premier acheteur. Passer `expectedLoadFactor` de 0,6 à 2 fait démarrer Ironhill
à 20 %. Et le relief, tel qu'il est câblé, ne le raccourcit pas du tout. La
décision à prendre est donc d'abord de savoir *d'où* doit venir le rayon.

**La nourriture est trop uniforme** (écart moyen ×1,4 sur dix acheteurs). Avec
trois trains et un transporteur omniscient, le réseau nourrit tout le monde au
prix de référence. Il faut vérifier si un joueur humain, ou un concurrent,
recrée de la dispersion — ou s'il faut rendre la demande plus volatile
(saisonnalité, croissance des villes).
*Mesuré avec les événements : non.* Des chocs de demande locaux, à toutes les
doses essayées, laissent l'écart entre ×1,38 et ×1,46 sous la référence ; ils
déplacent le surplus, pas le gradient. Voir la section sur les événements.

**Le charbon ne se distribue jamais** (écart ×9 à ×13 quel que soit le nombre de
trains). À creuser : marchandise à faible valeur, source unique, demande répartie
sur neuf villes. C'est peut-être le problème logistique le plus intéressant de la
carte, ou un défaut d'équilibrage.
*Mesuré avec les événements :* une grève à la mine fait monter le charbon partout
et *resserre* son écart (×11,4 → ×9,6) au lieu de le distribuer. Voir la section
sur les événements.

## Verdict

Le prototype est concluant. La boucle tient, l'arbitrage se referme quand on
investit, et il y a un arbitrage stratégique réel entre profit et service. Les
prochains chantiers — réseau sur relief, dispatching, finance — peuvent démarrer
sur cette base, chacun derrière son contrat dans [CONTRACTS.md](CONTRACTS.md).

Ce qui reste à trancher est de l'ordre du choix de conception, pas de la
faisabilité.
