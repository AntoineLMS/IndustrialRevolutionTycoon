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
     option où refuser un chargement économise vraiment ce qu'il coûte.
     *Mesurée le 30 septembre 2026 et livrée en opt-in* (`haulage.costModel =
     "mass"`, `data/sierra-marginal.json`), sans déplacer aucune empreinte : voir
     « Le coût marginal réel » plus bas. En bref, sur 40 réalisations et
     entretien compris, elle fait mieux que le statu quo à toute traction et sous
     les deux solveurs (178 919 ± 1 552 contre 148 590 ± 5 283 à 0,03, référence) ;
     elle sépare deux bassins au-delà de 0,12 à 0,15 — la scierie de Cedarton
     s'arrête, les planches valent 2,00 à l'est contre 1,13 à l'ouest à 0,2 — mais
     presque rien à 0,03 ; et l'essentiel de son gain vient de ce qu'elle décide
     au coût marginal plutôt qu'au coût moyen, pas du relief. Changer le défaut
     reste une décision ouverte, chiffrée dans cette section.
   - *Des trains qui ne roulent pas à vide* : relève du module `dispatch`. Tant que
     les trains font la navette sans condition, aucun coût kilométrique n'est
     marginal, relief ou pas.
2. **Quelle valeur pour `climbEquivalentKm` ?** À 0,03 (actuel), la sierra paie
   8,8 % de relief, 18 236 sur deux ans. À 0,2 (la lecture « une rampe de 1 %
   triple la résistance »), 58,6 % : le résultat tombe de 219 411 à 116 075, et
   deux trains font presque aussi bien que trois. Entre les deux, 0,09 : 26,4 % et
   182 940. *Sous le modèle de coût marginal, la question change de nature : c'est
   elle qui décide s'il y a une géographie — aucune à 0,03, deux bassins à partir
   de 0,15 ; les options sont chiffrées dans « Le coût marginal réel ».*
3. **La rugosité doit-elle compter comme une rampe ?** Aujourd'hui la rugosité seule
   coûte 8 à 11 % sur 60 km, autant qu'un relief réel, et un tunnel ne fait rien
   gagner à l'exploitation. Options :
   ne compter que les dénivelés au-delà d'un seuil, ou lisser le profil avant de
   sommer — deux façons de déplacer les empreintes `terrain-*`, à décider avant
   d'écrire d'autres cartes.
4. *Tranchée le 30 septembre 2026 : entretien de 0,2 par kilomètre de voie et
   par tick, voir la section suivante.* **Le devis doit-il coûter quelque chose ?**
   Il n'est débité nulle part : la
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
   prochain client. *Le coût marginal réel donne ce rayon d'un coût physique : il
   est fixé par le rapport tare / charge des wagons et, au-delà d'un seuil de
   traction, par le col (« Le coût marginal réel »).*
6. **Chaque scénario doit-il régler son anticipant ?** Le bloc de heartland divise
   le résultat de la sierra par deux. C'est soit une contrainte à documenter pour
   les auteurs de cartes, soit le signe d'un solveur trop sensible pour servir
   sans réglage.

## Le réseau coûte : entretien de 0,2 par kilomètre de voie et par tick

*Décision de l'équipe, 30 septembre 2026, en réponse à la décision 4 ci-dessus.*

Chaque kilomètre de voie posée coûte 0,2 par tick au transporteur, que ses trains
roulent ou non — même sous administration judiciaire, qui arrête les trains, pas
le réseau. Une double voie compte double. Le tarif vit dans les données
(`network.costs.upkeepPerTrackKmPerTick`, 0,2 par défaut), et le prélèvement se
fait une fois par tick en tête de la phase transport. Le harnais l'affiche sous
l'exploitation (« dont entretien ») et dans le devis (`--survey`).

| scénario | voie | entretien / tick | sur 720 ticks | résultat avant | résultat après |
|---|---|---|---|---|---|
| sierra, référence | 505 km | 101 | 72 723 | 219 411 | 146 689 |
| sierra, anticipant | 505 km | 101 | 72 723 | 242 254 | 169 531 |
| terrain-plain | 60 km | 12 | 8 640 | −75 391 | −84 031 |
| terrain-valley | 60 km | 12 | 8 641 | −78 511 | −87 152 |
| terrain-pass | 120 km | 24 | 17 282 | −78 366 | −95 648 |

La carte du col compte deux itinéraires construits (par le col et par la crête
percée) : on paie la voie qu'on a posée, qu'on y roule ou non.

**La trace des marchés est identique au caractère près** avec et sans entretien,
sur les quatre cartes : c'est une charge fixe, elle ne change ni un prix ni un
chargement. Conséquence pratique : tous les résultats de la section « Relief et
économie ensemble », mesurés avant cette décision, restent justes à une
soustraction près — 72 723 pour chaque ligne de la sierra à trois trains (le
tableau par nombre de trains aussi, puisque l'entretien ne dépend pas des
trains), 8 640 ou 17 282 pour les cartes d'essai. `heartland`, `ironpeak` et
leurs variantes n'ont pas de réseau déclaré et ne changent pas.

*Corrigé le 30 septembre 2026, par la campagne du coût marginal.* La
soustraction ne vaut que tant que la trésorerie reste positive. Sans module
finance, une trésorerie négative interdit tout achat de fret, mais les trains
continuent de rouler et de payer : l'entretien fait alors passer sous zéro des
parties qui ne l'étaient pas, et elles s'effondrent. Six trains sur la sierra,
référence : −452 503 au lieu des 47 819 − 72 723 = −24 904 annoncés, usines à
22 %. Le relief imputé à 0,2 : −192 418 au lieu de −123 209. Sous l'anticipant,
six trains s'effondrent dans une partie des réalisations voisines (11 596 ± 98 226
de moyenne, médiane 32 952). Les lignes à trois trains et moins ne sont pas
touchées, ni les quatre et cinq trains à 0,03 (128 168 et 61 602, la soustraction
exacte) ; à 0,2, quatre trains le sont (−417 457). Voir « Le coût marginal réel »,
observations, et sa décision 6.

Ce que l'entretien ne fait pas encore : peser sur un choix. Un tracé plus long
coûte plus cher à entretenir, mais aucun acteur de la simulation ne choisit de
tracé — le réseau est donné par le scénario. Il pèsera sur les décisions le jour
où la construction se fera en cours de partie (module `network`, « ce qui
manque »), et c'est ce qui le distingue du relief : lui ne pèsera sur rien tant
que les trains roulent à vide (voir la décision 1 et le chantier du coût
marginal) — sauf sous le modèle `mass`, où il pèse sur chaque chargement (section
suivante).

## Le coût marginal réel : le relief fait une géographie, et la valeur vient d'ailleurs

*Troisième campagne sur relief — 30 septembre 2026, scénarios `sierra` et
`heartland`, 720 ticks, 90 de chauffe exclus, sous les deux solveurs, sur des
ensembles de 40 trajectoires voisines.*

### La question

La décision 1 de « Relief et économie ensemble » laissait une option non mesurée :
rendre une part du coût d'exploitation **réellement marginale** — proportionnelle à
la masse remorquée, de sorte que le relief et la distance coûtent davantage à un
train chargé qu'à un train vide — et faire décider le transporteur sur ce seul
surcoût. C'est la seule option où refuser un chargement économise ce qu'il coûte.
La question posée : **crée-t-elle une géographie économique — des bassins, un
rayon qui dépend du relief, une industrie viable près de sa ressource — sans
détruire de valeur ?**

### Le modèle

`haulage.costModel` choisit le modèle de coût, `flat` par défaut. Sous `mass`
(`Transport/TrainCost.cs`), le coût kilométrique d'un train se décompose en une part
fixe — locomotive, tender et wagons vides, dus plein ou vide — et une part par
chargement, **les deux multipliées par le facteur de relief** du tronçon et du sens,
parce qu'une rampe se gravit avec toute la masse du train :

```
tare         = locomotiveTonnes + capacité × wagonTareTonnes           (264 t)
masse_calib  = tare + calibrationLoadFactor × capacité × tonnesPerLoad (456 t)
FixedPerKm   = costPerKm × tare / masse_calib                           (0,463)
PerLoadKm    = costPerKm × tonnesPerLoad / masse_calib                  (0,0158)
facture      = Σ pas  km × relief × (FixedPerKm + PerLoadKm × charge)
décision     = PerLoadKm × Σ tronçons jusqu'à la destination  km × relief
```

La décision est la **dérivée exacte** de la facture par rapport à la charge, et les
deux sortent des mêmes tarifs et du même `LegCostFactor`. Un test le vérifie sur la
simulation elle-même — un train, un tick, aucune transaction possible : dix
chargements de plus coûtent à la facture dix fois ce que le transporteur leur a
imputé, dans les deux sens du col, à 1e-9 près. `expectedLoadFactor` n'est pas lu :
aucune part fixe n'est répartie sur une charge escomptée.

Une réserve, qui tient à l'instrument et non au modèle : le transporteur chiffre le
trajet jusqu'au **meilleur** acheteur en aval, mais vend au premier qui lui laisse
10 % de marge (`SellHere`). Quand il vend plus tôt, le chargement lui a coûté moins
que ce qu'il lui a imputé ; l'erreur est toujours dans le sens de la prudence.

À titre d'ordre de grandeur, un chargement de grumes de Pinecrest à Cedarton
(355 km, de l'autre côté du col) : le statu quo lui impute 19,7 quelle que soit la
montagne ; le modèle `mass` 5,6 à relief nul, 6,1 à 0,03, 7,1 à 0,09 et 9,0 à 0,2.

### Masses et calibration

Masses d'un train de marchandises américain des années 1870, sources dans
[SOURCES.md](SOURCES.md), « Masses du train » : locomotive et tender **48 t**
(Pennsylvania Railroad classe D5, 1870-1873, 48,1 t documentées), wagon couvert de
10 short tons de charge, **9 t**, pour **9 t** de tare [estimé]. Un train de 24
wagons pèse 264 t à vide, 480 t plein. Le catalogue de locomotives ne porte pas de
masse ; ces valeurs sont des paramètres de `haulage.massCost`, toutes dans les
données.

La calibration fixe le prix de la tonne-kilomètre : à `calibrationLoadFactor` de
sa capacité, un train coûte exactement son `costPerKm`. Deux choix possibles, tous
deux mesurés sur heartland, où il n'y a pas de relief pour brouiller le compte :

| calibration | ce qu'elle mesure | remplissage obtenu | coût facturé aux trains | écart au statu quo |
|---|---|---|---|---|
| statu quo | — | 0,716 | 207 360 | — |
| 0,72 | la charge moyenne du statu quo | 0,884 | 224 821 | +8,4 % |
| **0,89** (retenue) | la charge moyenne que le modèle fait porter lui-même | 0,890 | 207 362 | **+0,001 %** |

La première comparerait deux économies : le modèle fait remplir les trains davantage
(de 72 % à 89 %), si bien que calibrer sur l'ancienne charge renchérit
l'exploitation de 8 % et fait perdre 19 000 à la sierra sans rien dire des
décisions. La seconde est le point fixe : à 0,89, le coût total facturé sur
heartland est celui d'aujourd'hui à 2 près sur 207 360 (206 335 sous l'anticipant,
−0,5 %), et sur la sierra à 0,1 % près (225 370 contre 225 596 à 0,03). On compare
alors deux façons de **décider** sur une même économie.

### Méthode

**Des ensembles, jamais une partie.** Chaque configuration est jouée 41 fois : telle
qu'écrite, puis 40 fois avec une seule demande d'une seule ville multipliée par
1,001 ou 0,999 (dans l'ordre des villes et des marchandises du scénario), toute la
partie durant — le choc du contrôle de la section « Événements ». La perturbation
est la même pour toutes les variantes, si bien que les différences se lisent
**appariées**, perturbation par perturbation : « ± » y est une erreur type (écart-type
des 40 différences ÷ √40). Dans les tableaux de résultats, « ± » est l'écart-type
entre réalisations, et la médiane suit entre parenthèses. Le contrôle tient : sur
heartland sous la référence, l'ensemble donne 251 011 ± 5 521, comme le témoin des
événements (252 ± 4 milliers).

**Le banc mesure les scénarios, il ne les reconstruit pas.** Chaque variante est le
scénario tel qu'écrit, une valeur changée en mémoire. Les parties non perturbées
reproduisent les empreintes figées : `44876808B521C9E4` et `B80E7C2F7DFBD1A5` pour
la sierra, `B966B86D3F0AF83C` et `29EE085518F1B2B0` pour heartland,
`3A328BE0FBB30F59` et `64BC50252805D975` pour `sierra-marginal`. La variante
« relief imputé » (la ligne de code de « Ce que ferait le relief… ») tourne sur une
copie du transporteur hors dépôt, qui sans la ligne reproduit les empreintes au bit
près. Toutes les autres se relisent dans le harnais par un filtre `jq` sur le
scénario — par exemple `jq '.haulage.costModel = "mass" |
.network.traction.climbEquivalentKm = 0.2' data/sierra.json`, ou
`.cities[4].demand.food *= 1.001` pour une réalisation de l'ensemble.

**Un témoin de plus : décider au même coût, sans voir le relief.** Le modèle `mass`
change deux choses à la fois : il impute beaucoup moins (0,0158 par chargement et
par kilomètre, contre 0,0556 pour le statu quo) et il fait entrer le relief. Pour
les séparer, le **témoin 2,11** est le statu quo avec `expectedLoadFactor` = 2,11,
qui impute le même 0,0158 sur le plat (à 0,1 % près) — et rien pour le relief. Il
facture à la manière du statu quo : un train chargé y coûte ce que coûte un train
vide. L'écart entre `mass` et le témoin mêle donc deux choses, le relief dans la
décision et la facture à la masse ; la seconde se lit seule à relief nul.

Les résultats incluent l'entretien des voies (72 723 sur la sierra). C'est une
charge fixe qui ne change aucune décision tant que la trésorerie reste positive ; ce
n'est plus vrai en dessous (voir l'observation plus bas).

### Premier résultat : aucune destruction de valeur

Sierra, trois trains, résultat net sur 40 réalisations :

| traction | statu quo | relief imputé | coût marginal (`mass`) | `mass` − statu quo |
|---|---|---|---|---|
| *référence* | | | | |
| 0 | 166 826 ± 5 283 (167 741) | = statu quo | 196 264 ± 2 279 (196 858) | +29 438 ± 965 |
| **0,03** | 148 590 ± 5 283 (149 505) | 142 913 ± 5 061 (142 964) | **178 919 ± 1 552** (179 123) | +30 329 ± 832 |
| 0,09 | 112 118 ± 5 283 (113 033) | 77 983 ± 22 286 (86 151) | 143 093 ± 1 749 (142 940) | +30 975 ± 843 |
| 0,2 | 45 253 ± 5 283 (46 168) | −141 876 ± 66 465 (−126 919) | 79 573 ± 2 071 (79 931) | +34 320 ± 937 |
| *anticipant* | | | | |
| 0 | 178 923 ± 9 402 (181 633) | = statu quo | 195 238 ± 4 882 (196 504) | +16 315 ± 1 631 |
| **0,03** | 160 687 ± 9 402 (163 398) | 159 958 ± 9 955 (161 798) | **175 232 ± 6 295** (174 717) | +14 545 ± 2 038 |
| 0,09 | 124 215 ± 9 402 (126 926) | 98 206 ± 11 434 (97 063) | 141 126 ± 5 124 (141 926) | +16 911 ± 1 572 |
| 0,2 | 57 350 ± 9 402 (60 061) | −59 414 ± 94 035 (−21 358) | 72 625 ± 7 717 (72 400) | +15 275 ± 2 297 |

*Parties telles qu'écrites, à 0,03 : 146 689 (statu quo) et 175 856
(`sierra-marginal`) sous la référence, 169 531 et 169 883 sous l'anticipant. Sous
la référence, la partie de `sierra-marginal` n'a que 2 voisines sur 40 en dessous
d'elle : c'est un tirage bas, pas le scénario — la leçon des 240 374 de heartland.*

Le modèle `mass` fait mieux que le statu quo à toutes les tractions et sous les
deux solveurs, de 9 à 76 %, avec une dispersion **deux à trois fois plus faible**
sous la référence, une fois et demie sous l'anticipant : un transporteur qui décide
sur un coût vrai est moins à la merci d'une perturbation de 0,1 %. Le relief
imputé, lui, confirme la mesure précédente — une perte qui se creuse avec la
traction —, en pire qu'annoncé à 0,2 pour une raison indépendante du relief (voir
l'observation sur l'entretien).

Sur heartland, sans relief, même sens : 276 564 ± 1 766 contre 251 011 ± 5 521
sous la référence (+25 553 ± 904), 258 391 ± 5 864 contre 237 523 ± 10 190 sous
l'anticipant (+20 868 ± 1 699). La scierie d'Ironhill démarre (21 % et 20 %).

### Deuxième résultat : la valeur vient de la décision marginale, pas du relief

| traction | témoin 2,11 (réf.) | `mass` − témoin (réf.) | témoin 2,11 (ant.) | `mass` − témoin (ant.) |
|---|---|---|---|---|
| 0 | 196 404 ± 2 318 | −140 ± 327 | 197 674 ± 5 165 | −2 437 ± 1 059 |
| 0,03 | 178 168 ± 2 318 | +751 ± 436 | 179 439 ± 5 165 | −4 207 ± 1 529 |
| 0,09 | 141 696 ± 2 318 | +1 397 ± 418 | 142 967 ± 5 165 | −1 841 ± 1 131 |
| 0,2 | 74 832 ± 2 318 | **+4 742 ± 521** | 76 102 ± 5 165 | −3 477 ± 1 582 |

Sur heartland : témoin 276 472 ± 2 168 contre `mass` 276 564 ± 1 766 (référence),
257 008 ± 5 030 contre 258 391 ± 5 864 (anticipant) — indiscernables, ce qui est
attendu sans relief.

Le témoin retrouve **toute** la hausse : +29 578 sur la sierra, +25 461 sur
heartland sous la référence. C'est ce que « Le rayon économique est un réglage de
l'instrument » avait établi avec `expectedLoadFactor` = 2 : imputer moins rapporte,
et démarre les scieries lointaines. Ce que le modèle `mass` ajoute n'est pas ce
gain, c'est sa **justification** : 0,0158 n'est plus un réglage choisi parce qu'il
rapporte, c'est ce qu'un chargement coûte vraiment à porter, et `expectedLoadFactor`
ne sert plus.

Ce que le relief ajoute se lit dans la colonne des différences. Sous la référence,
décider sur le relief rapporte d'autant plus que le relief coûte : presque rien à
0,03 (+751 ± 436, à moins de deux erreurs types de zéro), +1 % à 0,09, **+6 % à
0,2**. Sous l'anticipant, l'écart est négatif, mais il l'est déjà à relief nul
(−2 437 ± 1 059) et ne se creuse pas avec la traction (−1 841 à −4 207, à une ou
deux erreurs types) : il ne vient pas du relief mais de la facture à la masse,
seule différence à relief nul, que l'anticipant, plus bruité, paie un peu.
**Le relief dans la décision ne détruit de valeur sous aucun des deux solveurs ;
sous la référence, il en crée à forte traction.**

### Troisième résultat : une géographie, au-delà d'un seuil de traction

Référence, trois trains ; le témoin et le statu quo ont la même trace des marchés à
toutes les tractions, puisque le relief n'entre pas dans leurs décisions :

| modèle, traction | Cedarton (min–max sur 40) | planches ouest / est | écart planches | mobilité planches | charbon ouest / est |
|---|---|---|---|---|---|
| statu quo, toute traction | 0 % | 0,97 / 1,95 | ×4,74 | 0,737 | 0,46 / 1,10 |
| témoin 2,11, toute traction | 22 % | 1,21 / 1,79 | ×3,32 | 0,591 | 0,47 / 1,15 |
| `mass`, 0 | 22 % (13–33) | 1,21 / 1,78 | ×3,30 | 0,575 | 0,47 / 1,15 |
| `mass`, **0,03** | 24 % (15–32) | 1,17 / 1,75 | ×3,43 | 0,593 | 0,49 / 1,15 |
| `mass`, 0,09 | 28 % (19–41) | 1,11 / 1,69 | ×3,41 | 0,594 | 0,48 / 1,13 |
| `mass`, 0,12 | 14 % (1–31) | 1,15 / 1,83 | ×3,49 | 0,542 | 0,49 / 1,12 |
| `mass`, 0,15 | **0 %** (0–0) | 1,12 / 1,99 | ×3,76 | 0,448 | 0,51 / 1,10 |
| `mass`, 0,2 | **0 %** (0–0) | 1,13 / **2,00** | ×3,73 | 0,458 | 0,50 / 1,09 |
| relief imputé, 0,2 | 0 % | 0,79 / 2,14 | ×8,58 | 0,562 | 0,44 / 1,17 |

*Prix moyens chez les acheteurs, × le prix de référence ; ouest = Westbrook,
Fordham, Pinecrest ; est = d'Eastgate à Farport ; Coalpass, au col, n'est dans
aucun des deux. Sous l'anticipant, même forme : Cedarton à 18, 19, 21, 13, 0 et 0 %
de 0 à 0,2, planches à l'est de 1,84 à 2,01.*

Là, le relief fait ce qu'on attendait de lui. **À 0,15 et au-delà, le col sépare
deux bassins** : les grumes de Pinecrest ne le passent plus (à 0,2, leur prix à
Cedarton revient à 1,55, celui d'un marché que personne ne livre), la scierie de
Cedarton s'arrête dans les 40 réalisations sous les deux solveurs, les planches
valent 2,00 × la référence à l'est contre 1,13 à l'ouest. L'industrie viable est
celle qui est du bon côté de la montagne, près de sa ressource. En dessous, le
rayon des grumes dépasse le col : à 0,09 la scierie tourne même un peu plus qu'à
plat (28 % contre 22 %, dans des fourchettes qui se recouvrent), vraisemblablement
parce que les planches de Pinecrest passent moins facilement vers l'est. Entre les
deux, à 0,12, elle bascule d'une réalisation à l'autre (1 à 31 %) : c'est le
seuil.

Et la géographie n'est pas celle du relief imputé. Celui-ci fabriquait des bassins
en refusant du fret que les trains portaient de toute façon : l'écart des planches
doublait (×8,6), l'est payait 2,14, et la compagnie perdait 187 000. Le modèle
`mass` les fabrique en refusant ce qui coûte vraiment : l'écart reste modéré
(×3,7), et la compagnie gagne.

Deux asymétries, toutes deux physiques. **Le charbon ne voit pas la montagne** : la
mine est au col, tout son charbon descend, et une descente ne coûte presque rien
(facteur 1,00 à 1,03) ; ses prix bougent de trois centièmes sur toute la gamme.
Et **à 0,03, la géographie est à peine là** : Cedarton à 24 % contre 22 % à relief
nul, les planches de l'est à 1,75 contre 1,78. La valeur actuelle de
`climbEquivalentKm` rend le relief presque invisible aux décisions, marginales ou
non.

### Ce que le modèle change au reste

**Le nombre de trains.** Mêmes ensembles, en ne changeant que le nombre de trains
(au-delà de trois, copies du premier aux arrêts 7, 2, 6) :

| trains | sierra 0,03, statu quo | sierra 0,03, `mass` | sierra 0,2, statu quo | sierra 0,2, `mass` | heartland, statu quo | heartland, `mass` |
|---|---|---|---|---|---|---|
| 1 | 10 522 ± 3 733 | 15 875 ± 4 161 | −23 943 ± 3 733 | −9 276 ± 3 633 | 89 038 ± 3 656 | 92 135 ± 3 579 |
| 2 | 101 095 ± 5 758 | 122 856 ± 3 520 | 32 225 ± 5 758 | 56 189 ± 4 562 | 183 512 ± 6 704 | 211 673 ± 3 278 |
| 3 | **148 590** ± 5 283 | **178 919** ± 1 552 | **45 253** ± 5 283 | **79 573** ± 2 071 | **251 011** ± 5 521 | 276 564 ± 1 766 |
| 4 | 126 497 ± 5 528 | 176 952 ± 1 886 | −419 581 ± 4 931 | 43 245 ± 2 084 | 229 069 ± 4 891 | **280 215** ± 1 986 |
| 5 | 55 338 ± 6 897 | 138 887 ± 2 588 | −577 979 ± 2 347 | −42 984 ± 70 481 | 161 723 ± 6 658 | 245 618 ± 2 561 |
| 6 | −449 479 ± 5 729 | 98 627 ± 2 137 | −704 823 ± 1 466 | −411 534 ± 1 188 | 101 099 ± 6 465 | 210 176 ± 2 250 |

*Référence. Usines desservies de 1 à 6 trains, heartland : 73 → 82 % (statu quo),
75 → 92 % (`mass`) ; scierie lointaine sous `mass` : 6, 13, 21, 33, 44, 56 %.
Sous l'anticipant, heartland `mass` culmine aussi à quatre trains (269 573 contre
258 391 à trois). Les effondrements du statu quo de la sierra à 4-6 trains ne sont
pas ceux de la première campagne : voir l'observation sur l'entretien.*

Le rendement du capital s'inverse toujours — c'est la propriété qui fait le jeu —,
mais **plus tard et plus doucement** : l'optimum passe de trois à trois-quatre
trains, et six trains gardent 75 % du résultat de l'optimum sur heartland, contre
40 % sous le statu quo. Le transporteur remplit mieux ses trains (89 % contre 72 %),
donc la capacité ajoutée trouve plus longtemps à s'employer. Ce qui adoucit
l'arbitrage entre profit et service : les usines desservies montent jusqu'à 92 %,
le résultat avec elles jusqu'à quatre trains.

**La rotation du fret.** Le charbon est livré 24 fois par chargement produit, contre
8,9 sous le statu quo (la nourriture 73 fois contre 70). Le témoin 2,11 fait de
même : c'est l'effet d'un coût décidé plus bas, pas du relief. Chaque revente paie
sa marge — 10 % et 1,5 par chargement au moins — sur un vrai écart de prix ; ce
n'est pas le lavage de fret, mais c'est la ligne « rotation très au-dessus de 1 »
du tableau de diagnostic du README, à surveiller.

**La mobilité.** Moyenne sur les marchandises, référence : 0,400 → 0,445 ; celle des
planches baisse (0,737 → 0,593), celle du charbon monte (0,439 → 0,526). Sous
l'anticipant, la moyenne baisse (0,560 → 0,465). La dispersion se referme là où le
transporteur transporte davantage. Au regard du critère du contrat `economy` — une
dispersion qui se déplace —, le modèle n'apporte donc rien de net : la mobilité
moyenne monte sous la référence et baisse sous l'anticipant.

### Sensibilité aux masses

Sierra, référence, 40 réalisations, une variable à la fois, calibration laissée à
0,89 :

| variante | 0,03 : résultat | Cedarton | 0,2 : résultat | Cedarton |
|---|---|---|---|---|
| **telle qu'écrite** (48 t, wagon 9 t, charge 9 t, 0,89) | 178 919 ± 1 552 | 24 % | 79 573 ± 2 071 | 0 % |
| calibration 0,72 | 159 727 ± 2 017 | 27 % | 51 512 ± 2 026 | 0 % |
| calibration 1,0 | 189 655 ± 2 366 | 24 % | 94 523 ± 1 545 | 0 % |
| locomotive 24 t | 179 373 ± 1 850 | 25 % | 80 377 ± 2 581 | 0 % |
| locomotive 96 t | 178 520 ± 1 884 | 22 % | 78 464 ± 1 744 | 0 % |
| wagon 4,5 t | 180 980 ± 1 568 | **0 %** | 83 198 ± 3 665 | 0 % |
| wagon 18 t | 177 600 ± 2 365 | 22 % | 75 700 ± 1 638 | **26 %** |

La calibration déplace le résultat — c'est le prix de la tonne, donc le niveau de
toute la facture — et presque pas les décisions : c'est pourquoi elle se mesure, au
point fixe, au lieu de se régler. La locomotive ne compte guère : 48 t sur 264. **La
tare des wagons, elle, est le levier de la géographie** : plus un wagon vide pèse
lourd, plus la part fixe domine, moins un chargement de plus coûte, et plus loin il
voyage. À 4,5 t, Cedarton meurt dès 0,03 ; à 18 t, elle survit au col à 0,2. Le
rapport tare / charge fait le rayon économique, et c'est le paramètre le moins bien
sourcé des trois.

### Deux observations en passant

**La vente au premier acheteur porte l'instrument.** Pour voir son rôle, une
variante de banc hors dépôt ne vend qu'à la meilleure destination en aval : −200 336
± 8 668 sous le statu quo, −200 901 ± 2 241 sous `mass`, à 0,03, usines à 48 et
35 %. Le fret s'entasse sur des destinations dont le prix s'effondre dès la première
livraison. La règle n'est pas changée ; elle est, avec le coût décidé, ce qui fait
tenir le transporteur de mesure, et le modèle `mass` n'y touche pas.

**Sans finance, l'entretien n'est une charge fixe que tant que la trésorerie reste
positive.** En dessous de zéro, le transporteur n'achète plus rien — pas de fret à
crédit sans module finance — mais ses trains continuent de rouler et de payer. Six
trains sur la sierra, statu quo, 0,03 : −452 503 avec l'entretien, contre −24 904
annoncés « à une soustraction près » de 47 819 ; usines à 22 %. Même mécanisme pour
le relief imputé à 0,2 : −192 418 contre −123 209. Aucune des mesures de trois
trains du statu quo n'est touchée (vérifié : 116 075 sans entretien à 0,2, comme
avant la décision) ; la section « Le réseau coûte » est corrigée en conséquence.

### Conclusion

**Oui : le coût marginal réel crée une géographie sans détruire de valeur — mais à
deux conditions, et en changeant autre chose en passant.**

1. *Sans destruction de valeur* : à toutes les tractions, sous les deux solveurs, il
   fait mieux que le statu quo (+9 à +76 %) et que le relief imputé, avec une
   dispersion moindre. Face au témoin qui décide au même coût sans
   voir le relief, le relief rapporte sous la référence (+6 % à 0,2) et ne coûte rien
   de discernable sous l'anticipant.
2. *La géographie demande de la traction* : à 0,03, elle est à peine visible ; au-delà
   de 0,12 à 0,15, le col sépare deux bassins, et la scierie de l'autre côté de la
   montagne s'arrête. Le seuil lui-même dépend du rapport tare / charge des wagons.
3. *La valeur vient de la décision marginale, pas du relief* : c'est l'abandon du coût
   moyen imputé qui rapporte 20 % sur la sierra, 10 % sur heartland, et démarre
   Ironhill et Cedarton. Le modèle `mass` rend ce gain légitime — un coût physique au
   lieu d'un réglage de l'instrument — mais il n'en est pas la source.
4. *Ce qu'il change en passant* : la tension du capital, adoucie (optimum à trois ou
   quatre trains, six trains à 75 % de l'optimum sur heartland au lieu de 40 %), et
   la rotation du charbon, multipliée par 2,7.

### Décisions laissées à l'équipe

Aucune n'est tranchée ici ; le modèle est livré en opt-in, sur la seule
`data/sierra-marginal.json`.

1. **Le modèle `mass` doit-il devenir le défaut ?**
   - *a. Non (livré)* : opt-in par les données. Aucune empreinte ne bouge, tous les
     chiffres de ce document restent valables.
   - *b. Partout* : les onze empreintes existantes bougent — le défaut réintroduit
     à la main (les masses lues sous `flat`) les fait toutes échouer —, et chaque
     chiffre de ce document est à remesurer. Heartland +10 % (251 → 277 milliers en
     moyenne), Ironhill à 21 %, optimum à quatre trains, six trains à 210 000 au lieu
     de 101 000, charbon livré 24 fois par chargement produit au lieu de 8,7.
     `heartland.json` cesserait d'être la référence de l'économie décrite plus haut.
   - *c. Sur les scénarios à relief seulement* (sierra, `terrain-*`) : heartland
     garde son rôle de référence ; la sierra passe de 148,6 à 178,9 milliers (les
     cartes `terrain-*`, qui perdent de l'argent par construction, n'ont pas été
     mesurées). Deux modèles de coût coexistent alors entre scénarios, ce qui rend
     leurs chiffres incomparables.
2. **Quelle valeur de `climbEquivalentKm` sous ce modèle ?** (sierra, référence)
   - *0,03 (actuel)* : 178 919, relief à 8,8 % du coût, **pas de géographie**.
   - *0,09* : 143 093, pas encore de bassins : Cedarton tourne davantage (28 %) et
     les planches baissent des deux côtés du col (1,11 / 1,69 contre 1,21 / 1,78 à
     plat).
   - *0,12* : 125 152, **le seuil** : Cedarton bascule d'une partie à l'autre (1 à
     31 %) — à éviter pour un scénario de régression.
   - *0,15* : 108 312, deux bassins, Cedarton à l'arrêt partout.
   - *0,2 (« une rampe de 1 % triple la résistance »)* : 79 573, deux bassins, le
     relief coûte 118 000 sur deux ans ; l'optimum reste à trois trains.
3. **Le rapport tare / charge des wagons est-il un paramètre physique ou un levier de
   conception ?** Il décide du rayon : à 4,5 t de tare, pas de scierie lointaine
   même à 0,03 ; à 18 t, pas de bassins même à 0,2. La valeur de 9 t est estimée, pas
   documentée (voir SOURCES.md). S'il doit être physique, il faut une meilleure
   source ; s'il est un levier, il faut le dire, et le régler par carte.
4. **La calibration se remesure à chaque changement de défaut.** 0,89 est un point
   fixe mesuré sur heartland sous la référence. Si le modèle devient le défaut, ou si
   `costPerKm` ou les masses changent, il faut la remesurer — sinon on compare deux
   économies (+8 % de coût à 0,72, −19 000 sur la sierra).
5. **La rotation du charbon à ×24** mérite d'être regardée pour elle-même avant de
   généraliser le modèle : légitime selon la règle actuelle, mais c'est la
   signature que le tableau de diagnostic désigne comme suspecte.
6. **Le plancher de trésorerie sans finance** (hors de ce chantier) : un scénario
   dont la trésorerie passe sous zéro cesse d'acheter mais continue de payer ses
   trains. Avec l'entretien, cela arrive à la sierra dès quatre trains à 0,2 et six
   à 0,03. Soit on l'accepte comme la faillite d'un scénario sans finance, soit les
   trains d'une compagnie sans trésorerie doivent s'arrêter comme sous
   administration.

## Le cycle économique : un rythme financier, pas une économie qui respire

*Campagne du module `cycle` — 30 septembre 2026, scénario `heartland-cycle`, 2 160
ticks (six années de jeu, 1870-1875), 90 de chauffe exclus, sous les deux solveurs,
sur des ensembles de 40 trajectoires voisines.*

### La question

La vision (« Le cycle économique ») veut une conjoncture — expansion, ralentissement,
crise, reprise — aux durées tirées au sort, que les événements font bouger, et qui
touche les taux d'emprunt, la bourse, les investisseurs et, modestement, la demande
des villes. Trois questions :

1. Le cycle crée-t-il un **rythme financier exploitable** — emprunter en expansion,
   racheter en crise — sans mener mécaniquement les compagnies à la faillite ?
2. Quel effet de la demande, à ±5 % contre ±10 % ?
3. Que change le **couplage avec les événements** par rapport à un cycle seul ?

### Le modèle

Le contrat est dans [CONTRACTS.md](CONTRACTS.md), la place dans le tick dans
[ARCHITECTURE.md](ARCHITECTURE.md) (phase 0c). En bref :

- **Des phases en boucle**, dans l'ordre des données, chacune avec ses bornes de durée ;
  une durée est tirée à l'ouverture de la phase, **un nombre par phase**, sur la
  séquence propre du module (13). Le calendrier est **exogène** : aucune règle ne lit
  l'activité — le cycle endogène a été écarté.
- **Les événements la déplacent par un attribut de données**, `cycle`, porté par un
  historique ou un type aléatoire : `forcePhase` bascule la conjoncture le jour où
  l'événement s'ouvre (la panique de 1873 force la crise ; déjà en crise, elle la
  prolonge) ; `pushTicks` est une bonne (positive) ou une mauvaise (négative) nouvelle,
  qui allonge une phase favorable et abrège une défavorable, ou l'inverse. Aucun
  identifiant d'événement n'apparaît dans le code. Les poussées sont **équilibrées en
  espérance** — 61,4 jours de bonnes nouvelles par an contre 61,0 de mauvaises —,
  comme le catalogue lui-même.
- **Quatre effets, tous dans les données**, en ligne droite sur 30 jours d'une phase à
  la suivante : un ajustement du taux, **fixé à l'émission** pour une obligation
  (variable au jour le jour pour le découvert), plus une prime de risque selon le
  levier et la rentabilité de la compagnie ; un facteur du multiple de valorisation,
  pour toute la cote, qui **multiplie** le multiple d'un résultat positif et le
  **divise** pour une perte (appliqué tel quel à une perte, il l'allégerait en crise) ;
  un facteur de demande des habitants, **publié sur chaque marché à côté de celui des
  événements** et composé par un produit dans les deux solveurs ; des facteurs des
  investisseurs, points d'accroche que personne ne lit encore. Le bonus selon le score
  du dirigeant a sa place dans la formule du taux, et vaut 0.
- **Le journal public** dit la phase, sa date, sa cause et chaque poussée ; il ne dit
  jamais quand la phase finira. C'est la différence délibérée avec les événements,
  qui annoncent leur fin : « garder de la trésorerie avant la crise » doit rester un
  pari.

Réglages livrés : expansion 360–900 jours, ralentissement 90–210, crise 270–630,
reprise 120–240, soit un cycle moyen de 1 410 jours (47 mois) calé sur les cycles
américains de 1854-1919 datés par le NBER ([SOURCES.md](SOURCES.md)) ; taux −1 / +0,5 /
+3 / +1 point ; multiple ×1,3 / ×1 / ×0,6 / ×0,85 ; demande ×1,04 / ×1 / ×0,95 / ×0,98.
Demande et multiple sont équilibrés sur un cycle moyen (−0,06 % et ×0,987, affichés
par `--balance`). La partie s'ouvre en crise : le NBER date un creux en décembre 1870.

### Méthode

**Le scénario d'épreuve** reprend au caractère près l'économie et la finance de
`heartland-finance` et les événements de `heartland-events` (un test compare le contenu
sérialisé à la réunion des deux), et n'ajoute que le bloc `cycle`. La finance est la
première touchée, d'où ce socle plutôt que heartland seul.

**2 160 ticks, pas 720.** Il faut contenir la panique (tick 1 337), au moins un cycle
complet — la crise d'ouverture finit entre 270 et 630, la suivante arrive au plus tard
à la panique — et la crise que la panique ouvre, jusqu'à la reprise (630 jours au
plus). À 720 ticks, on ne mesurerait que la première crise et sa reprise.

**Des ensembles, et des moyennes dans le temps.** Réalisation *i* (0 à 39) :
`events.randomSequence` = 11 + *i*, `cycle.randomSequence` = 13 + *i* ; la réalisation 0
est le scénario tel qu'écrit, qui reproduit les empreintes figées (`E8210450987FCE4E`,
`3CA30C95E38BE915`). Le **témoin** est le même scénario, conjoncture désactivée : un
test vérifie qu'elle ne tire alors rien et ne décale aucun autre flux, donc les deux
jouent le même calendrier d'événements, et les différences se lisent **appariées**,
réalisation par réalisation (« ± » y est une erreur type ; dans les tableaux de
niveaux, un écart-type entre réalisations, la médiane entre parenthèses). Fortune du
magnat, cours et dette sont des **moyennes sur les ticks après chauffe** ; la valeur au
dernier tick n'est donnée qu'à côté — c'est la dette connue n° 2 de la finance. Chaque
variante change **une seule valeur** en mémoire, sur le scénario tel qu'écrit.

**Une stratégie qui ne lit que le journal.** Pour savoir si le rythme est exploitable
par quelqu'un qui n'a que l'information publique, une variante du banc réécrit chaque
jour la politique du magnat selon la phase affichée : il n'achète qu'en crise ou en
reprise (« contracyclique »), le reste de sa politique inchangé.

### Premier résultat : un rythme lisible, et aucune faillite

| | référence, sans cycle | référence, livré | anticipant, sans cycle | anticipant, livré |
|---|---|---|---|---|
| mises sous administration | 0/40 | 0/40 | 0/40 | 0/40 |
| cours moyen | 17,06 ± 0,66 | 17,08 ± 0,98 | 14,64 ± 1,04 | 14,54 ± 1,33 |
| cours en expansion / en crise | — | 23,27 / 10,77 | — | 19,50 / 9,66 |
| cours en ralentissement / en reprise | — | 21,14 / 15,81 | — | 18,14 / 13,35 |
| taux d'une obligation type, expansion / crise | 6,00 | 5,45 / 11,01 | 6,00 | 5,40 / 11,01 |
| taux des trois séries (réalisation 0) | 6 / 7,5 / 9 | 9,92 / 11,96 / 15,12 | 6 / 7,5 / 9 | 9,84 / 11,73 / 14,87 |
| intérêts payés sur six ans | 66 000 | 93 000 (+27 000) | 64 000 | 88 000 (+24 000 ± 1 000) |
| dette moyenne | 135 000 | 122 000 | 131 000 | 117 000 |
| résultat du transport (milliers) | 1 074 ± 34 (1 081) | 1 125 ± 30 (1 129) | 943 ± 53 (950) | 986 ± 66 (997) |
| mobilité de la dispersion | 0,424 | 0,454 (+0,029 ± 0,006) | 0,631 | 0,625 (−0,006 ± 0,003) |
| tête du mois qui change | 38,0 % | 38,6 % | 41,3 % | 41,1 % |

*« Obligation type » : ce que coûterait, chaque jour, 100 000 à 6 % faciaux, prime de
risque de la compagnie du jour comprise, moyenné sur les jours de la phase. La prime
moyenne vaut 0,45 point en expansion, 2,0 en crise.*

**Le rythme est là, et il est lisible.** Le cours de la compagnie est divisé par 2,2
entre l'expansion et la crise sous les deux solveurs, et remonte en reprise ; le
crédit coûte deux fois plus cher en crise. Le cours **moyen**, lui, ne bouge pas
(17,06 contre 17,08) : le multiple oscille autour de celui du scénario, comme prévu.
**Aucune compagnie n'est menée à la faillite** : 0/40 sous administration sous les
deux solveurs. Ouverte en crise, la compagnie emprunte ses trois séries 3,9 à 6,1
points plus cher que le taux facial et paie 27 000 d'intérêts de plus sur six ans ;
elle le supporte.

**La demande fait tout le résultat.** Les 51 000 de résultat en plus (référence, ±
5 000 en différence appariée) disparaissent exactement quand on neutralise la seule
demande ; taux et bourse n'y touchent pas, le transporteur ne manquant jamais de
trésorerie. Et ils ne viennent pas d'une demande plus forte, mais **plus faible** :
sur ces six ans, la crise occupe 39 % du temps au lieu de 32 % en régime établi —
ouverture en crise, panique —, la demande réalisée vaut −0,74 %, et le surplus de
nourriture grossit d'autant, ce que le transporteur encaisse. C'est le mécanisme des
épidémies de la section « Événements », à petite dose : un calendrier qui penche d'un
côté déplace le surplus, et le résultat le suit.

### Deuxième résultat : le magnat témoin est la victime désignée — et la dette de la finance fausse cette mesure

Fortune du magnat, en milliers, moyenne dans le temps (médiane finale entre crochets) ;
« < 0 » compte les réalisations où il finit avec une fortune négative :

| configuration (référence sauf mention) | sans cycle | avec cycle | Δ | < 0 | appels de marge |
|---|---|---|---|---|---|
| magnat témoin (achats tous les 30 jours, 65 % sur marge) | 721 ± 161 [1 097] | 377 ± 218 [−24] | −344 ± 34 | 2 → 23 /40 | 40 → 448 |
| le même, sans marge | 538 ± 66 [771] | 457 ± 78 [713] | −81 | 0 → 0 | 0 |
| aucun ordre de bourse (il garde ses 30 %) | 569 ± 21 [840] | 586 ± 28 [974] | +17 | 0 → 0 | 0 |
| **contracyclique** : n'achète qu'en crise ou en reprise | = témoin | 563 ± 123 [961] | +186 ± 25 contre le témoin avec cycle | 1 /40 | 22 |
| contracyclique, sans marge | = sans marge | 475 ± 71 [786] | +18 contre « sans marge » avec cycle | 0 | 0 |
| anticipant, magnat témoin | 391 ± 159 [375] | 436 ± 141 [433] | +45 ± 27 | 5 → 5 /40 | 65 → 118 |
| anticipant, contracyclique | = témoin | 513 ± 130 [707] | +77 contre le témoin avec cycle | 6 /40 | 103 |

**Le magnat témoin achète en haut et vend en bas.** Il achète à intervalles fixes, à
65 % sur marge, quel que soit le cours : en expansion il achète cher, la crise divise
le cours par deux, l'appel de marge le fait vendre au plus bas. Sous la référence, il
perd la moitié de sa fortune moyenne et finit ruiné dans 23 réalisations sur 40. Sans
marge, la perte tombe à 81 000 ; sans ordres, le cycle ne lui coûte rien (+17 000). Ce
n'est donc pas le cycle qui ruine, c'est **le levier sur une règle aveugle au cycle**.
Neutraliser la seule bourse (multiple ×1 partout) rend tout (+5 000 ± 45 000 contre la
partie sans cycle) ; les taux seuls coûtent 31 000 ± 16 000. Sous l'anticipant, dont les
trajectoires de cours sont autres, le même magnat ne perd rien (+45 000 ± 27 000) : la
ruine n'est pas une propriété du cycle, c'est la rencontre d'un calendrier et d'un
levier.

**Le rythme est exploitable avec la seule information publique** : la règle qui
n'achète qu'en crise ou en reprise regagne 186 000 ± 25 000 sur le témoin, et les
appels de marge tombent de 448 à 22. Elle ne fait pas mieux que la partie sans cycle
en moyenne dans le temps (563 contre 721), parce que la fortune se mesure au cours du
jour et qu'un tiers du temps est de la crise ; à la fin, elle s'en approche (961 contre
1 097 en médiane).

**Mais cette mesure est faussée par la dette connue n° 1, et le cycle l'aggrave.** Le
flottant est une contrepartie de profondeur infinie : le magnat y achète et y vend
n'importe quel volume au cours affiché, et un cours qui oscille est une pompe pour qui
le suit. Le signe le plus net : **raccourcir les phases de moitié fait passer la
fortune moyenne du témoin de 377 000 à 1 228 000** (+506 000 ± 88 000 contre la partie
sans cycle ; +593 000 ± 65 000 sous l'anticipant), alors que les ramener aux trois
quarts la fait tomber à 278 000. Une grandeur qui répond de façon non monotone et à
±500 000 près à la durée des phases n'est pas une mesure d'équilibrage : c'est l'effet
d'un marché qui paie toujours au cours affiché. Tant qu'il n'y a pas de profondeur de
carnet finie, **aucune conclusion sur la fortune du magnat ne vaut réglage du cycle** ;
les conclusions qualitatives — le levier aveugle est puni, la lecture du journal paie
— sont celles qu'on peut garder.

### Troisième résultat : emprunter en expansion et racheter en crise ne vont pas ensemble

La compagnie témoin emprunte ses trois séries et rachète ses deux concurrents dans les
110 premiers jours. La phase d'ouverture décide donc des deux à la fois :

| ouverture | taux des trois séries | intérêts | 1re fusion (médiane) | fusions | résultat du transport (k) | admin. |
|---|---|---|---|---|---|---|
| **crise (livré)** | 9,92 / 11,96 / 15,12 | 93 000 | tick 76 | 2,00 | 1 125 ± 30 | 0/40 |
| reprise | 7,98 / 10,11 / 13,27 | 91 000 | tick 76 | 2,00 | 1 074 ± 43 | 0/40 |
| expansion | 6,04 / 9,00 / 12,18 | 65 000 | **tick 1 702** | 1,38 | 987 ± 56 | 0/40 |
| expansion, anticipant | 5,88 / 8,50 / 11,67 | 59 000 | jamais, pour plus de la moitié | 0,70 | 792 ± 191 | **2/40** |

Ouverte en expansion, la compagnie emprunte 28 000 moins cher — mais le cours de Great
Plains, porté par le multiple ×1,3, rend l'OPA trop chère pour sa trésorerie : la
montée au capital se poursuit tranche par tranche, l'OPA attend, et elle n'aboutit
qu'avec la panique de 1873, quand le cours de la cible s'effondre (tick 1 702 en
médiane). Entre les deux, la trésorerie immobilisée prive le transporteur de fret :
−138 000 ± 8 000 de résultat contre l'ouverture en crise sous la référence, et deux
mises sous administration sur 40 sous l'anticipant. **Le crédit bon marché ne compense
pas une cible chère** ; racheter en crise rapporte bien davantage qu'emprunter en
expansion n'économise. Pour la compagnie témoin, dont la politique d'acquisition est
une règle à seuils, c'est un effet de seuil ; pour un joueur, c'est exactement le
choix que la vision annonce.

### La demande : ±5 % contre ±10 %

Une variable : l'écart de chaque phase à 1, multiplié par *k* (*k* = 1 : +4 % / −5 %,
livré ; *k* = 2 : ±10 % ; *k* = 3 : ±15 %). Résultat en différence appariée contre la
partie sans cycle, en milliers ; écart-type du résultat entre réalisations :

| demande | résultat (réf.) | écart-type | mobilité (réf.) | tête/mois (réf.) | résultat (ant.) | mobilité (ant.) | tête/mois (ant.) |
|---|---|---|---|---|---|---|---|
| ×1 partout (*k* = 0) | 0 ± 0 | 34 | 0,424 | 38,0 % | 0 ± 0 | 0,631 | 41,3 % |
| **±5 % (livré)** | +51 ± 5 | 30 | 0,454 | 38,6 % | +43 ± 11 | 0,625 | 41,1 % |
| ±10 % | +42 ± 10 | 67 | 0,463 | 36,5 % | +31 ± 13 | 0,623 | 40,3 % |
| ±15 % | −37 ± 20 | 127 | 0,493 | 35,9 % | −74 ± 20 | 0,630 | 38,6 % |

**±5 % : un effet de niveau, pas de géographie.** Sous la référence, la mobilité de
l'amplitude monte un peu (+0,03), la tête du mois ne bouge pas (38,0 → 38,6 %) ; sous
l'anticipant, rien ne bouge. **±10 % n'apporte rien de plus au jeu et double le
risque** : la mobilité gagne encore un centième, mais la ville la plus chère change
*moins* souvent (36,5 %), l'écart-type du résultat double, et le magnat témoin finit
ruiné dans 36 réalisations sur 40. C'est la conclusion de la section « Événements »
sur la portée « all », retrouvée par un autre chemin : un choc qui frappe toutes les
villes à la fois fait respirer l'amplitude, il ne déplace pas le meilleur débouché —
et plus il est fort, moins la tête bouge. À ±15 %, le résultat baisse franchement.
L'effet modeste que la vision demande est confirmé, et ±5 % en est la bonne dose.

### Le couplage avec les événements

Une variable à la fois, contre le livré ; les événements jouent dans toutes les
lignes, seuls leurs attributs `cycle` changent (référence ; le calendrier de la
conjoncture ne dépend pas de l'économie, il est le même sous l'anticipant) :

| couplage | en crise au tick 1 337 | 2e crise (tick, médiane) | 1re reprise | Δ résultat (k) | Δ fortune moy. (k) |
|---|---|---|---|---|---|
| **livré** (panique + poussées) | 40/40 | 1 299 ± 69 (1 337) | 427 ± 104 | — | — |
| sans couplage (cycle seul) | 14/40 | 1 441 ± 181 (1 473) | 430 ± 90 | +1 ± 3 | −1 ± 22 |
| panique seule, sans poussées | 40/40 | 1 299 ± 70 (1 337) | 430 ± 90 | +3 ± 3 | −43 ± 24 |
| poussées seules, sans panique | 14/40 | 1 424 ± 180 (1 444) | 427 ± 104 | −0 ± 2 | +20 ± 7 |
| poussées ×2 | 40/40 | 1 280 ± 91 (1 337) | 427 ± 129 | −3 ± 3 | −21 ± 22 |
| poussées ×4 | 40/40 | 1 237 ± 142 (1 337) | 445 ± 222 | +6 ± 4 | +64 ± 47 |

**La panique fait un rendez-vous ; les poussées font un peu de brouillard.** Sans
couplage, le tirage seul met la conjoncture en crise au 18 septembre 1873 dans 14
réalisations sur 40, et la deuxième crise arrive à ±181 jours près autour du tick
1 441 ; avec la panique, l'Histoire a lieu à sa date dans toutes, et la deuxième crise
se resserre autour d'elle (écart-type de 181 à 69 jours). Les poussées livrées
dispersent la première reprise de ±104 jours au lieu de ±90 : deux semaines de
dispersion en plus sur une phase de six mois, de quoi montrer au journal que les
récoltes et le bâtiment comptent, pas assez pour rendre la conjoncture illisible. À
×4, la reprise se disperse de ±222 jours : les événements deviennent le cycle.

**Sur les agrégats, le couplage ne se voit pas** : résultat, cours moyen, mobilité
restent dans l'erreur type (Δ résultat +1 ± 3). Sur la fortune du magnat, les écarts
(−43 à +64) ne dépassent pas deux erreurs types, et la section précédente dit ce que
vaut cette grandeur. Le couplage est un **choix de récit** — l'Histoire à sa date, des
aléas qui ont des conséquences —, pas un réglage d'équilibre, et c'est pour cela qu'il
est livré équilibré : un couplage à sens unique déplacerait la part du temps passée en
crise, donc la demande moyenne, donc le résultat (un test l'interdit, et `--balance`
l'affiche).

Et **le cycle seul, sans aucun événement**, fait monter la mobilité de 0,366 à 0,425
sous la référence (+0,059 ± 0,004, autant que le catalogue d'événements seul), mais
n'ajoute que +0,029 une fois les événements présents : les deux sources de variation
ne s'additionnent pas.

### Conclusions

1. **Oui, le cycle crée un rythme financier, lisible et sans faillite.** Cours divisé
   par 2,2 en crise, crédit deux fois plus cher, cours moyen inchangé, aucune compagnie
   sous administration dans la configuration livrée, sous les deux solveurs.
2. **Il est exploitable avec l'information publique** — une règle qui n'achète qu'en
   crise ou en reprise regagne 186 000 sur la règle aveugle —, **mais la mesure de
   l'exploitation passe par la fortune du magnat, que la dette connue n° 1 fausse**, et
   le cycle l'aggrave : un cours qui oscille est une pompe sur un flottant infini. La
   profondeur de carnet finie devient un prérequis de tout réglage fin de la bourse.
3. **Le levier aveugle est puni** : le magnat témoin, à 65 % sur marge, finit ruiné une
   fois sur deux sous la référence. C'est dans l'esprit de la vision (l'achat à crédit
   doit être dangereux), et c'est un avertissement pour le futur concurrent IA : une IA
   qui ne lit pas le journal de la conjoncture perdra contre un joueur qui le lit.
4. **Racheter en crise l'emporte sur emprunter en expansion** pour la compagnie témoin :
   l'ouverture en expansion économise 28 000 d'intérêts et coûte 138 000 de résultat,
   parce que la cible devient trop chère.
5. **±5 % de demande est la bonne dose** ; ±10 % double le risque sans rien déplacer.
6. **Le couplage avec les événements est un récit, pas un réglage** : la panique ancre
   la crise en 1873, les poussées ajoutent deux semaines de dispersion, aucun agrégat
   ne bouge.

### Décisions laissées à l'équipe

Aucune n'est tranchée ici ; les chiffres sont des différences appariées contre la
partie sans cycle, sous la référence, sauf mention.

1. **Durée des phases.**
   - *a. NBER (livré)* : cycle moyen de 47 mois, 5,6 changements de phase en six ans.
     Historiquement défendable ; deux crises en six ans, celle de l'ouverture et 1873.
   - *b. ×0,75* (35 mois) : 7,1 changements ; rien d'autre ne bouge que la fortune du
     magnat (−443 000).
   - *c. ×0,5* (24 mois) : 11,7 changements, mobilité +0,015 seulement, et la fortune du
     magnat qui explose (+506 000) — la pompe du flottant. Un rythme plus rapide rend le
     jeu de bourse plus riche, et la dette n° 1 plus grave.
2. **Taux par phase** (ajustement en expansion / en crise, en points).
   - *−1 / +3 (livré)* : obligation type à 5,5 % contre 11,0 % ; intérêts +27 000.
   - *0 / 0* : les obligations à leur taux facial, intérêts −7 000 ; plus aucune raison
     d'emprunter à un moment plutôt qu'à un autre.
   - *−2 / +6* : 4,6 % contre 14,0 % ; intérêts +48 000 ; toujours 0/40 sous
     administration.
   - Et la **prime de risque** (2 points par unité de levier au-delà de 0,5, 10 par unité
     de perte, plafond 5) : sans elle, intérêts +12 000 au lieu de +27 000. Le taux du
     découvert suit la conjoncture au jour le jour ; celui de la **marge du magnat** ne la
     suit pas — les taux de l'argent au jour le jour ont flambé en 1873 : à décider.
3. **Force de la bourse** (multiple en expansion / en crise).
   - *×1,3 / ×0,6 (livré)* : cours ÷2,2 ; magnat témoin −344 000, ruiné 23 fois sur 40.
   - *×1,15 / ×0,8* : cours ÷1,6 ; −209 000, ruiné 23 fois sur 40 ; +95 000 sous
     l'anticipant.
   - *×1,45 / ×0,4* : cours ÷2,9 ; −393 000, ruiné 8 fois — la cible absorbée à
     l'ouverture coûte moins, la dette moyenne baisse de 11 000.
   - *×1 partout* : pas de rythme boursier ; +5 000.
   Tant que le flottant est infini, ces chiffres disent surtout ce que le levier du
   magnat témoin supporte ; la décision se prend avec la profondeur de carnet finie.
4. **Force de l'effet sur la demande.** *±5 % (livré)* : +51 000, écart-type inchangé ;
   *±10 %* : +42 000, écart-type doublé, tête du mois −2 points ; *0* : la conjoncture
   ne touche plus que la finance, ce qui se défend — la demande n'apporte ni géographie
   ni mobilité sous l'anticipant.
5. **Couplage avec les événements.** *Panique + poussées équilibrées (livré)* ; *panique
   seule* (les aléas ne touchent plus la conjoncture, aucun agrégat ne bouge) ;
   *poussées ×2* (deuxième crise à ±91 jours) ; *aucun couplage* (la crise de 1873 n'a
   lieu à sa date qu'une fois sur trois). Et : quels types doivent pousser ? Les
   récoltes et le bâtiment sont livrés ; une faillite bancaire ou une ruée vers l'or
   seraient de nouveaux types, donc un nouveau calendrier d'aléas pour heartland-events,
   dont l'empreinte bougerait.
6. **Phase d'ouverture.** *Crise (livré, le creux NBER de décembre 1870)* : crédit cher,
   cibles bon marché. *Expansion* : crédit bon marché, OPA bloquée jusqu'à la panique,
   −138 000. Pour une campagne, c'est un levier de difficulté.
7. **Annoncer la fin d'une phase ?** Livré : non, à l'inverse du choix fait pour les
   événements. L'annoncer rendrait la stratégie contracyclique triviale.
8. **Les points d'accroche** — apport et patience des investisseurs, bonus du score de
   dirigeant — attendent leurs modules ; leurs valeurs par phase (×1,2 / ×1 / ×0,5 /
   ×0,8 pour l'apport) ne sont pas mesurées, puisque personne ne les lit.

## Les objectifs : ce qu'on compte, et quand ça tombe

*Campagne du module `objectives` — 30 septembre 2026, scénarios `heartland-cycle`
(2 160 ticks, 1870-1875) et `sierra` (720 ticks), sous les deux solveurs, sur des
ensembles de 40 parties.*

### La question

La vision décide trois sortes d'objectifs — une fortune personnelle, des cargaisons
livrées (au total ou vers une ville), deux villes reliées avant une date — et laisse
aux scénarios les paliers, ce qui fait perdre et la géographie. Avant de régler un
seul chiffre, trois questions de définition, dont chacune peut rendre un objectif
creux :

1. **Quelle fortune, lue quand ?** Celle du magnat, pas la caisse de sa compagnie ;
   mais la dette connue n° 1 de la finance (un flottant de profondeur infinie) la rend
   manipulable. Au tick courant ou en moyenne glissante ?
2. **Qu'est-ce qu'une cargaison livrée**, quand la nourriture est vendue des dizaines
   de fois pour une fois produite ?
3. **Que veut dire « relier »** sur un réseau figé au chargement, et encore le jour où
   la construction existera ?

Et une question de mesure : à quelle date chaque objectif tombe-t-il, avec quelle
dispersion, et quelle part des parties le manque ? C'est ce qui permettra de régler
la difficulté d'un scénario.

### Le module

Le contrat est dans [CONTRACTS.md](CONTRACTS.md), la place dans le tick dans
[ARCHITECTURE.md](ARCHITECTURE.md) (phase 7, après la finance). En bref : un bloc
`objectives` déclare des objectifs, chacun avec un ou plusieurs **paliers** (une cible
et une échéance facultative, incluse). Chaque soir, le module lit la mesure de chaque
objectif et conclut chaque palier : **atteint** le jour où la mesure franchit la
cible, **manqué** le soir de l'échéance s'il ne l'est pas, **en cours** sinon. Un
palier conclu l'est pour de bon — une fortune qui fond après l'avoir atteinte ne le
défait pas. Journal public, affiché par le harnais, écrit dans `objectives.csv`.

**Un observateur pur.** Le module ne touche à rien et ne tire rien. La preuve est
double : aucune empreinte de référence n'a bougé, y compris celles de heartland-cycle
et de sierra qui portent désormais un bloc actif ; et un test rejoue ces deux
scénarios avec et sans leur bloc, sous les deux solveurs, et compare marchés,
compagnie, usines, état financier au centime et journaux. C'est aussi ce qui permet
la méthode ci-dessous : on accroche à une partie autant de variantes de lecture qu'on
veut sans la déplacer.

### Trois définitions

**La fortune est celle du magnat, et elle n'a pas de repli.** `Tycoon.NetWorth` :
caisse personnelle, plus le portefeuille au cours du jour, moins la dette de marge.
Sans module finance, il n'y a pas d'homme d'affaires, seulement une trésorerie de
compagnie ; mesurer celle-ci sous le nom de fortune serait précisément la confusion
que la vision interdit (« sa fortune personnelle est distincte de celle de la
compagnie qu'il dirige »). Un objectif de fortune sans finance est donc **refusé au
chargement**, avec un message qui dit pourquoi. Et la fenêtre de lecture —
`averageTicks`, 1 pour la fortune du soir, N pour la moyenne des N derniers soirs —
est **obligatoire** : la mesure qui suit montre que ce n'est pas un détail.

**Livré, c'est ce que le rail a laissé dans une ville : vendu moins racheté.** Par
ville et par marchandise, jamais négatif ; au total, la somme sur les villes. Le
carnet de route de la compagnie (`Company.Freight`), écrit par le transporteur à
l'instant de chaque échange, tient les deux cumuls. Trois définitions ont été écartées :

- *toutes les ventes* : sur heartland-cycle, la nourriture est vendue **88 fois** pour
  une fois produite (242 600 chargements vendus pour 2 760 produits en six ans) ;
  « mille chargements livrés » tomberait en **63 jours** ;
- *la première vente après la production* : il faudrait suivre la provenance de
  chaque chargement, or un marché est fongible — le blé de Fairview et celui de Weston
  se mélangent dans le même stock. Il faudrait marquer les stocks, donc toucher aux
  marchés, pour un résultat que la définition retenue donne sans cela ;
- *les imports cumulés d'un marché* : exacts aujourd'hui (seuls les trains déposent),
  mais un marché ne sait pas qui y vend ; le jour où un concurrent roulera, ils
  mélangeraient les compagnies. Un objectif appartient à un joueur.

Ce que la définition retenue garantit : une marchandise revendue de ville en ville
s'annule dans chaque ville intermédiaire (reçue puis rachetée) et ne compte qu'une
fois, **là où elle est restée**. Livré au total vaut exactement ce que le rail a pris
aux villes exportatrices, moins ce qui est à bord — un test le vérifie sur une
revente construite à la main (le même blé vendu deux fois, compté une) et sur
heartland-cycle. Revers assumé : une ville qui produit la marchandise n'en reçoit
« livraison » qu'au-delà de ce qu'elle exporte. Livrer de la nourriture à Kingsport,
qui a sa boulangerie, ne compte pas ; la vision parle de livrer à une ville, pas d'y
faire transiter.

**Relier, c'est qu'un même train, sur une même ligne, se soit arrêté dans les deux
villes** — arrêts traversés en cours de tick compris, puisque le carnet note chaque
arrivée en gare. Sur une ligne, un train qui a desservi A puis B a nécessairement fait
le trajet de l'un à l'autre (il ne rebrousse chemin qu'aux terminus). Deux villes
desservies par deux trains sur deux réseaux disjoints ne sont **pas** reliées — un
test le vérifie. Pourquoi pas « les deux gares sont sur une même ligne exploitée » :
sur un réseau figé, ce serait vrai au tick 0 ou jamais. Pourquoi pas « la voie
existe » : la vision parle de relier des villes, et une voie où ne roule aucun train
ne relie rien. Le jour où la construction existera, la définition restera juste — il
faudra poser la voie **et** y faire rouler un train —, et la ligne fait partie de la
clé, pour qu'un train réaffecté ne prouve rien de sa nouvelle ligne par l'ancienne.

### Méthode

Deux ensembles de 40 parties par scénario et par solveur, sur le scénario tel qu'écrit :

- **témoin** : un seul choc de demande de 0,1 % pendant 38 jours, sur un couple
  (ville, marchandise) et à une date qui changent avec la partie — la méthode du
  témoin de la section « Événements ». C'est la sensibilité aux conditions initiales :
  ce que deviennent les dates quand rien d'économique ne change ;
- **calendrier** (heartland-cycle seulement) : `events.randomSequence` = 11 + *i*,
  `cycle.randomSequence` = 13 + *i* — la méthode de la campagne du cycle. C'est ce que
  font d'autres aléas et une autre conjoncture.

À chaque partie s'ajoutent, sans la déplacer, des variantes de lecture : la fortune
au jour le jour, sur 30 et sur 90 jours ; les livraisons sans échéance à plusieurs
seuils ; et, relevées à part, les livraisons comptées en brut (toutes les ventes).
Dates en ticks ; « médiane [p25 – p75] » ; « atteint » compte les parties sur 40. Les
objectifs livrés sont des **valeurs d'illustration, non réglées** : un demi-million
avant 1873 et un million avant 1876 (moyenne sur 30 jours) ; mille chargements de
nourriture avant 1874 ; trois cents de charbon à Northgate avant le 1er juillet 1875
(tick 1 979) ; Pinecrest – Cedarton avant le 1er février 1875 sur la sierra.

### Premier résultat : au jour le jour, la fortune s'achète

Le soir du 1er avril 1872 (tick 810), le magnat témoin de heartland-cycle passe un
ordre de bourse : 38 000 actions de sa propre compagnie, à 65 % sur marge. L'impact de
l'ordre porte le cours de 23,7 à 37,3, et **tout** son portefeuille est réévalué à ce
cours : sa fortune passe de 536 000 à 1 059 000. Le lendemain elle vaut 1 018 000,
cinq jours plus tard 886 000. Au jour le jour, le palier du million tombe ce soir-là.

| référence | au jour le jour | moyenne 30 jours | moyenne 90 jours |
|---|---|---|---|
| demi-million, témoin | 40/40, 354 [350 – 354] | 40/40, 372 [370 – 372] | 40/40, 410 [408 – 410] |
| million, témoin | **40/40, tick 810 dans les 40** | **0/40** | 0/40 |
| demi-million, calendrier | 40/40, 347 [323 – 387] | 40/40, 363 [340 – 408] | 40/40, 400 [370 – 442] |
| million, calendrier | 38/40, 810 [810 – 840] | 15/40, 914 [852 – 1 034] | 11/40, 1 000 [939 – 1 075] |

| anticipant | au jour le jour | moyenne 30 jours | moyenne 90 jours |
|---|---|---|---|
| demi-million, témoin | 40/40, 342 [342 – 342] | 40/40, 356 [356 – 356] | 40/40, 390 [390 – 390] |
| million, témoin | 12/40, 840 [818 – 1 028] | 7/40, 1 036 [969 – 1 152] | 5/40, 989 [988 – 1 068] |
| demi-million, calendrier | 40/40, 398 [345 – 668] | 40/40, 415 [362 – 695] | 40/40, 448 [393 – 728] |
| million, calendrier | 26/40, 870 [810 – 1 658] | 13/40, 1 180 [977 – 2 091] | 10/40, 1 045 [1 008 – 1 867] |

Trois lectures :

1. **Au jour le jour, un palier de fortune mesure un ordre de bourse, pas une
   fortune.** Sous la référence, les 40 témoins atteignent le million le même soir,
   celui de l'ordre, et repassent sous la cible dès le lendemain pour les 1 349 soirs
   qui restent. En moyenne sur 30 jours, aucun ne l'atteint. Ce n'est pas un défaut
   du module, c'est la dette n° 1 de la finance vue d'un autre côté : acheter ses
   propres actions contre un flottant infini, avec un impact qui réévalue toute la
   position, **fabrique** de la fortune affichée. Un joueur qui l'a compris gagne un
   objectif de fortune en un ordre.
2. **La moyenne ne coûte presque rien quand la fortune monte vraiment** : 14 à 18
   jours de retard sur le demi-million à 30 jours (la moitié de la fenêtre, comme
   attendu d'une série qui croît), 48 à 56 à 90 jours. Elle ne supprime pas la
   manipulation, elle la rend chère : il faut tenir le cours gonflé pendant la
   fenêtre, donc racheter et porter la marge, là où un soir suffisait.
3. **Le million dépend du calendrier plus que du magnat témoin** : 0/40 sur les
   témoins de la référence, 15/40 quand la conjoncture et les aléas changent. La
   fortune est la plus dispersée des trois mesures ; un palier de fortune est un pari
   sur la bourse autant qu'un objectif de gestion.

### Deuxième résultat : les livraisons sont une horloge

| | référence, témoin | référence, calendrier | anticipant, témoin | anticipant, calendrier |
|---|---|---|---|---|
| nourriture : 1 000 avant 1874 | 40/40, 1 175 [1 175 – 1 175] | 40/40, 1 155 [1 140 – 1 166] | 40/40, 1 169 [1 169 – 1 169] | 40/40, 1 147 [1 135 – 1 158] |
| écart-type (jours) | 0 | 18 | 6 | 17 |
| la même, comptée en brut | 63 | 63 [62 – 64] | 78 | 76 [72 – 77] |
| charbon à Northgate : 300 avant le tick 1 979 | 37/40, 1 892 [1 834 – 1 917] | 30/40, 1 842 [1 759 – 1 900] | 40/40, 1 857 [1 822 – 1 875] | 38/40, 1 773 [1 730 – 1 832] |
| écart-type (jours) | 48 | 90 | 43 | 62 |
| la même, comptée en brut | 211 (de 200 à 925) | 646 [341 – 777] | 159 | 165 [146 – 178] |

Sans échéance, la nourriture franchit 250, 500, 750, 1 000 et 1 500 chargements aux
ticks 311, 624, 904, 1 175 et 1 767 sous la référence (témoins, écart-type ≤ 1 jour) :
**environ 0,85 chargement par jour, d'un bout à l'autre de la partie**, crises et
panique comprises. Le charbon de Northgate passe 100, 200 et 300 aux ticks 692, 1 213
et 1 901, écart-type de 24 à 57 jours.

Trois lectures :

1. **Livré net, c'est un débit, pas un exploit.** La nourriture laissée par le rail
   suit la consommation des villes qui n'en produisent pas, et le transporteur
   automatique la sert au jour près : deux jours d'écart entre les 40 témoins. Un
   objectif de livraisons se règle donc par un rapport cible / durée, et sa difficulté
   viendra de ce que le joueur fait de ses trains, pas de l'économie. Le charbon d'une
   ville en bout de ligne est plus dispersé (2 à 5 % de la durée), parce qu'il dépend
   de ce que la ligne choisit de porter, en concurrence avec les autres marchandises.
2. **Compté en brut, le même objectif tombe 18 fois plus vite et ne mesure plus rien
   de stable** : mille chargements de nourriture en 63 jours au lieu de 1 175 ; le
   charbon de Northgate entre 200 et 925 jours selon un choc de 0,1 %, entre 178 et
   1 134 selon le calendrier. Ce qui varie, c'est la revente — la rotation que le
   tableau de diagnostic du README signale déjà —, pas ce qui arrive à destination.
3. **Le calendrier compte plus pour le charbon que pour la nourriture** : 10 parties
   sur 40 manquent les trois cents chargements de Northgate quand la conjoncture
   change, 3 sur 40 parmi les témoins. Un palier réglé sur la trajectoire de référence
   serait manqué une fois sur quatre par un joueur qui ferait exactement la même chose
   sous un autre tirage.

### Troisième résultat : sur un réseau figé, relier, c'est l'horaire

Sur la sierra, Pinecrest et Cedarton sont reliées au **tick 4**, par t1 parti de
Westbrook, dans les 40 parties et sous les deux solveurs — écart-type nul. Aucune
décision n'y entre : les trains roulent sur la ligne du scénario, pleins ou vides,
tant que la compagnie n'est pas sous administration (et la sierra n'a pas de
finance). L'objectif éprouve la définition, pas un joueur. Il ne deviendra un choix
qu'avec la construction en cours de partie, qui n'existe pas (contrat `network`,
« ce qui manque ») ; l'échéance et le palier en sont le point d'accroche.

### Conclusions

1. **Les trois définitions tiennent** : la fortune est celle du magnat et rien
   d'autre ; livré ne compte qu'une fois ce que la revente fait tourner ; relier exige
   un même train sur une même ligne. Chacune est vérifiée par un test qui échoue quand
   on réintroduit la définition naïve.
2. **Un objectif de fortune lu au jour le jour se gagne par un seul ordre de bourse**,
   tant que le flottant est infini. La moyenne glissante le rend coûteux sans le rendre
   impossible ; seule la profondeur de carnet finie (dette n° 1) le referme. C'est une
   raison de plus, après celles du cycle, d'en faire un prérequis.
3. **Les livraisons nettes sont une horloge**, les brutes un bruit : c'est la bonne
   mesure pour régler un palier, et la difficulté se lira sur un rapport cible / durée.
4. **Relier ne mesure rien tant que le réseau est donné** : l'objectif existe, il
   attend la construction.
5. **Le module n'a rien déplacé** : aucune empreinte, avec ou sans bloc.

### Décisions laissées à l'équipe

Aucune n'est tranchée ici.

1. **Lire la fortune au jour le jour ou en moyenne glissante ?** Le module exige que
   chaque scénario le dise (`averageTicks`), et heartland-cycle a choisi 30 jours.
   - *Au jour le jour* : lisible, immédiat — et gagnable par un ordre de bourse tant
     que le flottant est infini (40/40 témoins atteignent le million le soir de
     l'ordre, 0/40 en moyenne sur 30 jours).
   - *Moyenne sur 30 jours* : 14 à 18 jours de retard sur une fortune qui monte
     vraiment, et la manipulation coûte un mois de cours tenu.
   - *Moyenne sur 90 jours* : 48 à 56 jours de retard, et un objectif qui ne se lit
     plus d'un coup d'œil.
   - *Autre* : un minimum sur la fenêtre (« tenir le million trente jours ») plutôt
     qu'une moyenne — non implémenté ; le plus robuste contre un pic, le plus sévère
     pour une fortune qui oscille avec la conjoncture.
   Le choix se refait quand la profondeur de carnet existera : la dette n° 1 réglée,
   l'écart entre ces lectures devrait se refermer, et il faudra le remesurer.
2. **Ce qu'un palier vaut.** Victoire, médaille, simple jalon, défaite s'il est
   manqué ? Le module ne l'interprète pas. Les mesures disent où tombent les paliers
   livrés : le demi-million et la nourriture toujours ; les trois cents chargements de
   charbon de 30 à 40 fois sur 40 ; le million de 0 à 15 fois sur 40 selon le solveur
   et le calendrier.
3. **Comment les objectifs se combinent.** Tous ? Un nombre ? Un score ? Le journal
   dit l'état de chacun ; rien ne les agrège.
4. **Livrer à une ville qui produit.** La définition nette ne compte rien à Kingsport
   pour la nourriture, qu'elle exporte. Si un scénario veut « livrer du grain au
   moulin » d'une ville qui en produit aussi, il faudra une autre sorte d'objectif ; la
   vision ne la demande pas aujourd'hui.
5. **Relier sur un réseau donné.** Garder la sorte telle quelle en attendant la
   construction, ou exiger d'ici là un chargement porté de l'une à l'autre (ce qui
   demanderait la provenance des chargements à bord) ?
6. **Fortune sans finance.** Refusée. L'alternative serait une quatrième sorte,
   explicitement nommée « trésorerie de la compagnie » ; la vision ne la demande pas.

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
*Une réponse est mesurée* (« Le coût marginal réel ») : sous le modèle `mass`, le
rayon vient d'un coût physique — ce qu'un chargement ajoute à la facture —, Ironhill
démarre à 21 %, et c'est le rapport tare / charge des wagons qui fixe la distance ;
le relief ne le raccourcit qu'au-delà d'une traction de 0,12 à 0,15.

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
