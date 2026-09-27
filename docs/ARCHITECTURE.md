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

## Ce qui n'est pas encore modélisé

Volontairement absents de ce prototype, chacun derrière une façade déjà en place
ou à créer : le réseau réel (relief, terrassement, ponts, tunnels,
signalisation), le dispatching, la finance (bourse, obligations, OPA), l'IA
concurrente, les scénarios scriptés, et toute l'interface.

`Transport/Rail.cs` est une abstraction délibérément pauvre : une suite d'arrêts
et de distances, juste assez pour donner au transport une latence et un coût
kilométrique. Le vrai module réseau viendra derrière cette même façade.
