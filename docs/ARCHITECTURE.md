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

Volontairement absents de ce prototype, chacun derrière une façade déjà en place
ou à créer : le réseau réel (relief, terrassement, ponts, tunnels,
signalisation), le dispatching, l'IA concurrente, les scénarios scriptés, et toute
l'interface. La finance est désormais derrière `IFinanceSolver` (phase 6) ; ce
qu'il lui manque encore est listé dans [CONTRACTS.md](CONTRACTS.md).

`Transport/Rail.cs` est une abstraction délibérément pauvre : une suite d'arrêts
et de distances, juste assez pour donner au transport une latence et un coût
kilométrique. Le vrai module réseau viendra derrière cette même façade.
