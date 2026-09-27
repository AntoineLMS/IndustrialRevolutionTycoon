# Architecture

## Les quatre règles non négociables

Ces règles existent parce qu'elles sont chacune la condition d'une chose qu'on
veut pouvoir faire plus tard. Les enfreindre ne casse rien immédiatement — c'est
précisément ce qui les rend dangereuses.

### 1. La simulation ne dépend d'aucun moteur

`RailTycoon.Sim` ne référence ni Godot, ni Unity, ni aucun paquet NuGet. Une
partie complète doit pouvoir tourner dans un terminal.

*Pourquoi* : c'est ce qui permet d'exécuter 720 ticks en quelques secondes dans
une CI, de comparer deux modèles économiques sur la même trace, et de changer de
moteur sans réécrire les règles du jeu.

### 2. Pas fixe, et rien qui dépende du temps réel

Un tick = un jour de jeu. Aucune règle ne consulte l'horloge système ni un delta
de frame.

*Pourquoi* : rejouabilité d'une partie sauvegardée, traces de régression
comparables, et possibilité d'un multijoueur en lockstep sans réécriture.

### 3. Tout parcours de collection est ordonné de façon stable

Les listes le sont par construction. **Les dictionnaires ne le sont pas** — leur
ordre d'énumération n'est pas garanti par le contrat de .NET. On passe donc
toujours par `WorldState.CargoOrder` et `WorldState.MarketsOf(city)`.

*Pourquoi* : l'ordre des ventes influe sur les prix, donc sur le résultat. Un
ordre instable produit des parties non reproductibles et des tests qui échouent
un jour sur dix — le pire mode de défaillance possible.

### 4. Aucun aléa hors de `DeterministicRandom`

`System.Random` est interdit : son implémentation a déjà changé entre versions de
.NET, et changerait à nouveau.

## Ordre des phases d'un tick

**Cet ordre fait partie du contrat.** Le modifier change le résultat de toutes
les parties et invalide les traces de régression. Un module qui aurait besoin
d'un ordre différent doit faire l'objet d'une décision documentée ici, pas d'un
changement au détour d'un correctif.

| Phase | Effet | Détenteur |
|---|---|---|
| 0 | Remise à zéro de la télémétrie du tick | `Simulation` |
| 0b | Événements : ouverture et extinction, tirage des aléatoires, multiplicateurs du jour publiés sur les marchés | `IEventSolver` |
| 1 | Production primaire (fermes, mines, forêts) | `IEconomySolver` |
| 2 | Usines : consommation des intrants, production | `IEconomySolver` |
| 3 | Consommation des habitants, modulée par le prix | `IEconomySolver` |
| 4 | Recalcul des prix | `IEconomySolver` |
| 5 | Déplacement des trains, achats et ventes | `IHaulageSolver` |
| 6 | Finance : exploitation constatée, découvert, intérêts, cours, dividendes, bourse, OPA | `IFinanceSolver` |

La phase 6 a été **ajoutée à la fin**, jamais insérée. C'est la décision
documentée qu'exige la règle ci-dessus, et sa raison est simple : la finance
*constate* ce que l'exploitation a produit. La placer avant le transport ferait
décider d'un dividende sur le résultat de la veille, et changerait le résultat de
toutes les parties existantes. Ajoutée en queue, elle ne déplace aucune phase et
n'a aucun effet sur les traces de régression de l'économie — c'est vérifié par un
test qui compare l'empreinte de la trace des marchés avec et sans le module.

La phase 0b a été **insérée** entre 0 et 1, et c'est la seconde décision de ce
genre. Elle ne change l'ordre d'aucune phase existante, et sa place découle de ce
qu'elle fait : elle décide quels événements agissent aujourd'hui et publie, sur
chaque marché, deux nombres — `EventProductionFactor` et `EventDemandFactor` —
que le solveur économique compose dans ses taux du jour avant la phase 1. Elle ne
touche ni stock, ni prix, ni argent ; la marchandise continue de naître et de
disparaître par `Market.Produce` et `Market.Consume`, en phases 1 à 3. Trois
raisons à cette place plutôt qu'une autre :

- **Avant la phase 1**, parce qu'un événement daté du tick *t* doit agir sur la
  production du tick *t*. Le placer en fin de tick, après la finance, pour le
  tick suivant, reviendrait au même calcul avec un décalage d'un jour dans le
  journal — et une date qui glisse d'un jour est une date fausse.
- **Hors du solveur économique**, parce qu'il y en a deux, et qu'un troisième
  viendra. Toute la logique — dates, enveloppes, tirages, cibles, composition,
  bornes — vit dans le module events ; un solveur ne lit qu'un nombre par marché
  et par levier. La référence recompose ses taux du jour à partir du nominal
  (`Market.NominalDemandRate`, fixé à la construction), l'anticipant les compose
  avec sa saison et la taille de ses villes. Les deux dimensionnent l'entrepôt
  d'un site sur son débit **nominal** : une grève ne rétrécit pas le carreau, et
  un frein dimensionné sur le débit du jour ferait subir l'événement deux fois.
- **Sans effet quand le module est inactif**, au bit près : les multiplicateurs
  restent à 1, et `nominal × 1` vaut `nominal` jusqu'au dernier bit. Aucune
  empreinte de `ReferenceTraceTests` n'a bougé à l'arrivée du module, ce qui est
  la preuve demandée par la règle ci-dessus ; un test la renouvelle en jouant
  heartland-events avec son bloc plein mais désactivé.

L'aléa des événements est tiré sur **sa propre séquence** de
`DeterministicRandom` (`events.randomSequence`, 11 par défaut), comme la finance
sur la sienne (7) : activer les événements ne décale aucun autre tirage. Et chaque
type aléatoire tire un nombre fixe de valeurs par jour, qu'il se déclenche ou non,
si bien que le calendrier d'un type ne dépend ni de l'intensité des autres ni de
leur fréquence.

Conséquence voulue de l'ordre 4 puis 5 : les trains voient les prix
d'après-production. Le joueur arrive sur un marché tel qu'il est au matin, pas
tel qu'il était la veille.

## Le modèle de prix

Le prix se forme à partir de la **couverture** : le nombre de ticks de
consommation que le stock local peut assurer.

```
couverture     = stock / (demande_effective × horizon)
multiplicateur = (forme + 1) / (forme + couverture)     borné à [min, max]
prix           = prix_de_référence × multiplicateur
```

À couverture 1, le multiplicateur vaut exactement 1. Stock vide, le prix monte
vers `(forme+1)/forme` ; surabondance, il tombe au plancher.

`demande_effective` additionne la consommation des habitants et celle des usines
locales. **Une demande nulle est traitée explicitement** : un marché sans acheteur
vaut le prix plancher, et non un prix de pénurie. Une première version noyait ce
cas dans un « plancher de demande » commun, ce qui donnait à toute ville vide un
prix de pénurie même pour des marchandises qu'elle ne consomme pas ; le
transporteur y acheminait du blé qui n'y trouvait jamais preneur.

**La propriété qui fait le jeu** : livrer sur un marché augmente son stock, donc
sa couverture, donc fait baisser le prix qu'on exploitait. L'arbitrage se referme
tout seul. C'est ce qui empêche une ligne rentable de l'être pour l'éternité, et
qui oblige le joueur à chercher la prochaine asymétrie. La campagne de mesure de
[FINDINGS.md](FINDINGS.md) montre que cette propriété tient à l'échelle de la
carte : le profit total culmine à trois trains et s'effondre à six.

L'horizon de couverture n'est pas un réglage cosmétique : il fixe l'échelle du
stock qui compte, donc ce qu'une seule livraison peut changer. Voir
[FINDINGS.md](FINDINGS.md) pour le calcul qui l'a fait passer de 30 à 15 jours.

## Trois régulateurs qu'il ne faut pas confondre

Chacun freine une chose différente, et les mélanger a produit des bugs réels.

| Mécanisme | Ce qu'il régule | Clé |
|---|---|---|
| Couverture | le **prix** d'un marché | stock ÷ demande locale |
| Encombrement | le **débit** d'un site de production | stock ÷ capacité de l'entrepôt |
| Réserve | ce qu'une ville accepte de **céder** | stock − besoins conservés |

La production primaire se bride sur l'**encombrement de son entrepôt**, jamais sur
la couverture locale. Ce qui arrête une mine n'est pas le prix du charbon sur
place, c'est un carreau encombré faute de wagons. Brider sur la couverture punit
précisément les sites que personne ne dessert encore — ceux que le joueur doit
avoir envie d'aller chercher.

La **réserve** est ce qui empêche une ville de revendre ce qu'on vient de lui
livrer. Sans elle, le transporteur déchargeait puis rachetait au même arrêt et
encaissait une marge à chaque tronçon sans nourrir personne.

## Valorisation d'une usine

Une usine paie ses intrants au **prix local** — c'est ce qu'elle décaisse
réellement — mais valorise sa production au **prix de référence**, pas au prix
local.

*Pourquoi* : un moulin ne consomme pas sa propre farine, qui vaut donc le prix
plancher chez lui. Valoriser sa production au prix local revient à conclure que
moudre n'est jamais rentable. Une usine produit pour expédier : sa production vaut
ce qu'elle vaut dans l'économie, pas ce qu'en donnerait un voisin qui n'en veut
pas.

Corollaire de conception : le prix de référence des marchandises et les recettes
forment un **système**, pas des données indépendantes. Chaque maillon doit ajouter
une marge suffisante pour absorber un intrant devenu cher avant de s'arrêter —
trop mince, l'usine ne démarre jamais ; trop large, le prix de ses intrants cesse
de compter. `--balance` vérifie la cohérence de l'ensemble.

## Impact d'un gros échange sur le prix

Un train qui décharge vingt chargements ne les vend pas tous au prix affiché : la
transaction est découpée en tranches, le prix étant recalculé entre chaque. Les
derniers chargements se vendent moins cher que les premiers.

*Pourquoi* : sans ce mécanisme, saturer une même ville serait indéfiniment
rentable, et la stratégie optimale se réduirait à une seule route.

## Le réseau ferré

Le module `network` (`src/RailTycoon.Sim/Network/`) remplace la suite d'arrêts et de
distances par un **graphe de voies posé sur un relief**. Sa façade est
`IRailNetwork`, et elle ne répond qu'à trois questions :

| Question | Méthode | Qui la pose |
|---|---|---|
| Combien coûterait *ce* tracé ? | `Survey(points, voies)` | le constructeur, l'interface |
| Par où passe-t-on de A à B ? | `TryConnect`, `Routes` | le transport, la circulation |
| Que coûte le relief à l'exploitation ? | `CostFactor(arête, sens)` | le transport |

Tout le reste — carte de hauteurs, enveloppe de profils, terrassement, ponts,
tunnels — est derrière. Le transport ne connaît que `RailLine`, qui reste une suite
plate d'arrêts et de distances : il demande `DistanceBetween` et reçoit la longueur
réelle de la voie, il demande `LegCostFactor` et reçoit un nombre. **Aucun type du
relief ne traverse cette frontière.**

### Comment un tracé est chiffré

1. **En plan** — les points de passage sont reliés par des alignements droits
   raccordés par des arcs de cercle au rayon minimal. Un virage qui exigerait plus
   serré est *refusé au chargement*, pas corrigé en silence.
2. **En long** — une plateforme dont la pente ne dépasse jamais `pente_max` est
   exactement une fonction lipschitzienne de la distance. Cet ensemble est encadré
   par le profil tout en déblai et le profil tout en remblai ; toute combinaison
   convexe des deux est admissible. Le géomètre chiffre quelques stratégies le long
   de cette famille et **garde la moins chère**.
3. **Ouvrages** — au-delà d'un seuil de hauteur de remblai on construit un pont, au
   delà d'un seuil de profondeur de déblai on perce un tunnel, à condition que
   l'ouvrage soit assez long pour en être un.

*Pourquoi cette forme* : sur un terrain plus doux que la pente maximale, les deux
bornes se confondent avec le sol, la voie le suit et ne coûte que sa pose. Là où le
sol est plus raide, l'écart s'ouvre, et c'est là — et seulement là — qu'on paie. Le
coût n'est donc pas une fonction de la distance : c'est une fonction du relief
traversé. Et parce que le volume de terrassement croît comme le *carré* de la
hauteur, un grand remblai finit toujours par donner raison au pont, puis au tunnel.

Sur les trois cartes d'essai, à soixante kilomètres exactement chacune, et avec un
relief rigoureusement identique entre la deuxième et la troisième :

| Carte | Devis | dont | × plaine |
|---|---|---|---|
| plaine | 180 093 | 180 009 de voie | 1,0 |
| vallée | 219 402 | 39 000 de terrassement | 1,2 |
| col | 1 219 880 | 326 000 de terrassement, 713 000 de viaducs | 6,8 |
| crête | 2 689 446 | un tunnel de 19 km | 14,9 |

Les deux dernières lignes traversent la *même* chaîne, à trente-six kilomètres
l'une de l'autre. C'est tout l'arbitrage du module : contourner par la vallée,
franchir au col, ou percer la crête.

### Ce que la façade garantit au module `dispatch`

Le module réseau définit le type graphe de voies, et `dispatch` sera écrit contre
lui. Ces garanties sont donc des engagements, pas des détails d'implémentation :

- **Des indices entiers contigus.** `TrackGraph.Nodes` et `TrackGraph.Edges` sont
  indexés de 0 à n-1 dans l'ordre de déclaration du scénario. `dispatch` peut
  allouer ses tableaux parallèles — occupation, réservations, cantons — sans
  dictionnaire ni indirection.
- **Un ordre de parcours stable.** `TrackNode.EdgeIndices` est dans l'ordre de
  déclaration des arêtes. Aucun parcours du graphe ne passe par l'énumération d'un
  dictionnaire, et le calcul d'itinéraire départage les égalités de longueur par
  l'indice d'arête. Deux exécutions donnent le même itinéraire.
- **Le grain de réservation est l'arête.** `TrackEdge.TrackCount` dit combien de
  trains peuvent l'occuper à la fois : 1 est une voie unique, donc un conflit
  possible. `TrackNode.PassingTracks` dit combien peuvent s'y croiser ou y
  stationner. Ces deux nombres sont la matière première d'un interblocage.
- **Un itinéraire est une suite de franchissements orientés.** `TrackRoute.Legs` est
  une liste de `RouteLeg`, chacun désignant une arête, un sens de parcours et ses
  deux nœuds ; `TrackRoute.Nodes` compte toujours un élément de plus. Rien d'autre
  n'est nécessaire pour poser des cantons dessus.
- **La correspondance arrêt → réseau est explicite.** `RailStop.NodeIndex` donne le
  nœud d'un arrêt, et `RailLine.Segments[i].Legs` les tronçons exacts qu'un train
  franchit entre deux arrêts consécutifs. `dispatch` n'a pas à redécouvrir
  l'itinéraire d'une ligne.
- **Le profil est déjà réduit.** `TrackProfile.RulingGradePercent(sens)` donne la
  rampe déterminante dans un sens de marche — celle qui borne le tonnage
  remorquable — et les sections portent leur rayon de courbe, qui bornera la
  vitesse. Les sections sont fusionnées : un profil se lit, il ne s'échantillonne
  pas.
- **Rien ne bouge en cours de partie.** Le relief et les devis sont calculés une
  fois, au chargement. Un profil, une longueur, un coût sont des constantes pour la
  durée d'une partie.

Ce que la façade **ne** promet **pas** : aucune notion de temps, d'occupation, de
signal ni de réservation. Le réseau décrit une géographie ; qui a le droit de rouler
où appartient entièrement à `dispatch`.

### Trois limites connues

**Le géomètre chiffre le tracé qu'on lui donne, il ne le cherche pas.** Il ne sait
pas allonger une ligne pour adoucir une rampe, ni inventer le lacet ou le
développement en boucle qu'un ingénieur de 1870 aurait tracés. D'où le tunnel de
dix-neuf kilomètres du franchissement de la crête : c'est le prix honnête d'un
mauvais tracé, pas la meilleure façon de passer là.

**Le terrassement suppose une plateforme en terrain horizontal.** La section est un
trapèze symétrique, ce qui surestime le déblai à flanc de coteau — précisément la
technique qui rendrait praticable le franchissement de la crête. Corriger cela
demande la pente transversale du terrain, donc un échantillonnage latéral du relief.

**Le relief est facturé, pas décidé.** `LegCostFactor` multiplie le coût
kilométrique que paie un train, mais le coût que le transporteur impute à un
chargement pour décider de l'acheter reste calculé sur la distance plate — et les
trains roulent de toute façon, pleins ou vides. Mesuré sur `data/sierra.json` : la
trace des marchés est identique au bit près que la montagne coûte 0, 0,03 ou 0,2
kilomètre par mètre. Le relief est un impôt sur le kilomètre-train, pas une
géographie économique, et le devis n'est débité nulle part. Les options sont
chiffrées dans [FINDINGS.md](FINDINGS.md), « Relief et économie ensemble ».

## La monnaie : `decimal` en finance, `double` partout ailleurs

C'est la seule entorse au typage uniforme de la simulation, et elle est
délibérée.

Un `double` ne peut pas représenter 0,01. Chaque opération laisse un résidu, et un
bilan n'est alors vérifiable qu'« à une tolérance près ». Cette tolérance est
exactement ce qu'il ne faut pas accorder à une comptabilité : un intérêt de 0,005
perdu par tick fait 3,60 par an et par emprunt, et aucune tolérance relative de
1e-9 ne le verra jamais. Le bug du lavage de fret (voir
[FINDINGS.md](FINDINGS.md)) est le précédent : deux millions gagnés sans qu'un
seul invariant ne bronche, parce qu'aucun test ne demandait l'égalité exacte.

La règle est donc :

| Grandeur | Type | Pourquoi |
|---|---|---|
| Stocks, prix unitaires, kilomètres, taux d'utilisation | `double` | grandeurs continues, issues d'un modèle continu, que personne n'additionne sur 720 ticks |
| Soldes : caisse, dette, capitaux propres, cours d'une action | `decimal` arrondi au centime | doivent s'équilibrer exactement, et le résidu doit valoir zéro et non « peu » |
| Nombres d'actions | `long` | il n'y a aucune raison d'introduire un arrondi là où le monde réel n'en a pas |

**La frontière est unique** : `FinanceState.PostOperatingResult`. Elle convertit
la trésorerie d'exploitation du module transport en centimes, une fois par tick.
Le détail qui compte : elle convertit le **cumul** et écrit la *différence* avec ce
qui a déjà été reflété. La somme de tout ce qui est passé en écriture vaut donc
toujours exactement `Round(cumul, 2)`, quel que soit le nombre de ticks.
Convertir le flux de chaque tick laisserait au contraire un demi-centime d'erreur
par tick. Un invariant surveille cet écart et exige qu'il reste sous le
demi-centime pour toujours.

Côté finance, la comptabilité est tenue en **partie double** : chaque écriture est
un ensemble de mouvements dont la somme est nulle, refusée à l'instant où elle est
passée si elle ne l'est pas. L'identité actif = passif + capitaux propres n'est
donc pas une propriété qu'on espère et qu'on vérifie après coup, c'est une
conséquence du type — et l'invariant se compare à **zéro exactement**, sans
tolérance.

## Une seule caisse, et un quatrième flux qui l'explique

Le module finance a d'abord tenu **sa propre caisse**, distincte de la trésorerie
d'exploitation qu'il se contentait de refléter. Chaque bilan s'équilibrait au
centime, et la fuite était pourtant réelle : un dividende de 88 000 ou une OPA de
75 000 sortaient du bilan de la société sans jamais sortir de la caisse du
transporteur, qui pouvait donc dépenser le même argent. Mesuré en réintroduisant
la faille : **23 520 dépensés deux fois dès le tick 15**.

C'est exactement la famille du lavage de fret. Non pas une erreur de calcul, mais
deux invariants dont chacun vérifie une moitié de la vérité — et qui, pris
ensemble, ne couvrent pas l'intervalle entre les deux.

La correction a demandé de changer la formule de `bilan-tresorerie`, ce qui n'est
pas un assouplissement :

```
trésorerie = mise de départ
           + recettes du transport − achats de fret − coûts d'exploitation
           + flux financiers nets            ← le quatrième terme
```

`Company.Cash` est désormais **la seule vérité** sur l'argent disponible à
l'exploitation. `NetProfit` reste volontairement hors flux financiers — un
emprunt n'est pas une recette, un dividende n'est pas une charge d'exploitation —
ce qui garde la marge au kilomètre interprétable et la sentinelle
anti-lavage-de-fret utilisable.

Deux détails de conception valent d'être connus :

- **La position est déduite, pas reportée.** Dix sites d'écriture touchent la
  caisse de la société ; en oublier un suffirait à recréer la fuite. Une seule
  fonction, en fin de tick, lit la position nette du grand livre — caisse moins
  découvert — et en déduit ce que la trésorerie doit valoir. La partie double sert
  de source de vérité unique : ce qui n'est pas au bilan n'existe pas.
- **Le transporteur voit une décision financière au tick suivant**, puisque la
  finance clôture après lui. C'est le pendant de « les trains voient les prix
  d'après-production » : la comptabilité arrête ses comptes le soir, le service du
  trafic en prend connaissance le matin.

L'invariant `frontiere-tresorerie` est la sentinelle de cette faille : la position
nette du bilan doit valoir la trésorerie du transporteur au demi-centime près. Un
prélèvement passé au bilan mais non répercuté fait apparaître son montant
immédiatement. Un test le vérifie en **réintroduisant la faille à la main**.

## Une trésorerie négative est une dette, pas un actif négatif

La trésorerie pouvait plonger indéfiniment dans le rouge sans que rien ne se
passe. Le transporteur refusait d'acheter du fret à découvert, mais les coûts
kilométriques étaient prélevés sans condition : **plus la compagnie roulait, plus
elle creusait**, sans terme. Et l'invariant `bilan-tresorerie` restait vérifié —
c'est une identité comptable, tout aussi vraie à −48 830 qu'à +340 000. Le mode de
défaillance est le même que celui des quatre bugs de FINDINGS : tout tourne, rien
ne signale rien.

Trois règles, toutes réglables dans les données :

1. **Le découvert est explicite.** La part négative de la trésorerie
   d'exploitation figure au passif du bilan, au centime, et porte intérêt à un
   taux délibérément plus élevé que celui des obligations — sinon le joueur
   n'aurait aucune raison de préférer un emprunt choisi à un découvert subi.
2. **Le découvert est borné.** La banque le gage sur les capitaux propres. Au-delà,
   la compagnie est mise sous administration : les trains cessent de rouler. C'est
   ce qui donne un *terme* au déficit — un réseau qu'on ne peut plus financer ne
   creuse pas son trou en continuant à brûler du charbon.
3. **Le capital achète du temps, pas l'absolution.** Le magnat peut souscrire à une
   augmentation de capital : son argent personnel renforce les capitaux propres,
   donc le découvert autorisé, donc la capacité des trains à rouler encore. Il ne
   *rembourse pas* le trou d'exploitation, qui ne se comble qu'en exploitant
   mieux. Et il peut choisir de ne pas renflouer, en gardant son argent — les deux
   branches sont exercées par les tests, parce que c'est précisément la décision
   qui fait le genre.

## Ce qui n'est pas encore modélisé

Volontairement absents de ce prototype, chacun derrière une façade à créer : la
signalisation et le dispatching, l'IA concurrente, les scénarios scriptés au-delà
des événements datés, et toute l'interface. Le réseau sur relief est derrière
`IRailNetwork`, la finance derrière `IFinanceSolver` (phase 6) ; ce qu'il leur
manque encore est listé dans [CONTRACTS.md](CONTRACTS.md).

Les événements historiques et aléatoires sont derrière `IEventSolver` (phase 0b) ;
ils ne touchent que la production primaire et la demande des habitants, et ce
qu'il leur manque est listé dans [CONTRACTS.md](CONTRACTS.md).

`Transport/Rail.cs` reste une abstraction pauvre — une suite d'arrêts et de
distances — et c'est désormais un choix et non une dette : c'est la projection du
graphe de voies que le transport consomme, et la seule chose qu'il ait besoin de
savoir. Le scénario de référence `heartland` y pose encore ses distances à la main,
en mode de compatibilité ; les cartes à relief sont `data/terrain-*.json`.
