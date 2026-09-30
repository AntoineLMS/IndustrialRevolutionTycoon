# Vision du jeu

*Premier jet, 30 septembre 2026. Ce document fixe le cap : ce qu'est le jeu, qui y
joue, et ce que le joueur décide. [ARCHITECTURE.md](ARCHITECTURE.md) dit comment
la simulation tient debout, [CONTRACTS.md](CONTRACTS.md) comment elle se découpe,
[FINDINGS.md](FINDINGS.md) ce qu'elle fait quand on la mesure. Celui-ci dit
pourquoi elle existe.*

*Chaque section distingue ce qui est **décidé** de ce qui reste **à décider**. Une
décision ne se prend pas au détour d'un correctif : elle se prend ici, puis le code
suit.*

## Le pitch

Amérique du Nord, ère industrielle. Tu n'es pas une compagnie de chemin de fer :
tu es **un homme d'affaires**. Tu commences avec une fortune personnelle, et tu la
fais fructifier en fondant ou en rachetant des compagnies.

Une compagnie de transport achète des locomotives et fait circuler des trains qui
**achètent les marchandises au départ et les revendent à destination**. Chaque
livraison referme l'écart de prix qui la rendait rentable : une ligne lucrative ne
le reste pas, et il faut sans cesse trouver la prochaine asymétrie. Une compagnie
peut aussi fonder des industries de produits finis, et créer ainsi la demande que
ses propres trains viendront servir.

Ce qui compte à la fin, c'est ta fortune, pas celle de tes compagnies. Tu peux
enrichir une compagnie, la saigner en dividendes, la laisser couler, ou racheter
celle d'un concurrent. L'Histoire s'invite en chemin : grève de l'anthracite,
incendie de Chicago, Panique de 1873.

## Le joueur est l'homme d'affaires

**Décidé.** Le joueur incarne une personne, pas une entreprise. Sa fortune
personnelle est distincte de celle des compagnies qu'il contrôle, et c'est elle
qui mesure sa réussite.

Ce que cela implique :

- Le joueur agit **à travers** ses compagnies. Il ne possède ni train ni industrie
  en propre : il possède des parts de compagnies, et ce sont elles qui possèdent
  les trains et les industries.
- Il peut contrôler **plusieurs compagnies à la fois**, de natures différentes.
- L'argent ne circule pas librement entre sa poche et ses compagnies. Il entre par
  les apports en capital et les achats d'actions ; il sort par les dividendes et
  les ventes d'actions. C'est ce qui rend « saigner une compagnie » ou « la
  renflouer » des décisions, et non des virements.

*Déjà en place* : le module finance distingue la caisse personnelle du magnat de
celle de sa société, et le magnat renfloue ou laisse couler, achète à crédit, et
prend des participations.

## Les compagnies : fonder ou racheter

**Décidé.** Le joueur peut **fonder** une compagnie de transport, ou en **racheter**
une qui existe.

- **Fonder** : le joueur apporte un capital de départ en échange d'actions. La
  compagnie démarre avec cette seule caisse ; tout le reste (emprunts, nouvelles
  émissions) se négocie ensuite.
- **Racheter** : prendre le contrôle d'une compagnie existante en achetant ses
  actions, jusqu'à l'OPA et la fusion. Le rachat récupère ce que la compagnie
  possède, dettes comprises.

**À décider** : ce que « posséder » veut dire. Détenir une action ? La majorité
(le module finance utilise aujourd'hui un seuil de contrôle de 50 %) ? Le seuil
compte, parce que les règles qui suivent (fonder une industrie, acheter un train)
en dépendent toutes.

## Ce qu'il faut posséder pour agir

**Décidé.** Deux règles d'accès structurent le jeu :

1. **Il faut posséder une compagnie de transport pour acheter des véhicules.** Un
   homme d'affaires seul ne fait pas rouler de train.
2. **Il faut posséder une compagnie pour fonder des industries de produits
   finis.** L'industrie appartient à la compagnie qui la fonde, pas au joueur.

Ces deux règles font de la compagnie le passage obligé de toute action économique.
Le joueur choisit **quelle** compagnie agit, et c'est elle qui paie, s'endette et
encaisse.

**À décider** :

- **Quelle compagnie peut fonder une industrie ?** La compagnie de transport
  elle-même, ou une compagnie industrielle distincte qu'il faudrait fonder à part ?
  La seconde option ouvre un vrai jeu d'intégration verticale (une compagnie
  industrielle et une compagnie de transport, deux bilans, deux cours de bourse) ;
  la première est plus simple à lire.
- **Où s'arrête « produit fini » ?** Nourriture, planches, acier le sont
  clairement. La farine et la fonte sont des intermédiaires : une compagnie
  peut-elle fonder un moulin ou un haut fourneau ?
- **Les industries primaires** (fermes, mines, forêts) : données par la carte et
  intouchables, ou rachetables ?

## La compagnie de transport : acheter au départ, revendre à destination

**Décidé.** La compagnie de transport **achète la marchandise au départ du trajet,
au prix local, et la revend à destination, au prix local.** Elle porte la
marchandise, et le risque, pendant le voyage. Son revenu est l'écart entre les deux
prix, moins ce que coûte le voyage.

*Lecture retenue, à confirmer.* La compagnie est un **transporteur** par son
métier : elle fait circuler des trains, elle ne produit rien. Mais elle est payée
comme un **négociant** : par l'écart de prix, et non par un tarif de fret versé par
un expéditeur. C'est la différence de fond avec *Railroad Tycoon 3*, où le chemin
de fer est rémunéré pour livrer sans jamais posséder la cargaison.

Pourquoi ce modèle est le bon pour ce jeu :

- **C'est lui qui fait la boucle de jeu.** Livrer une marchandise fait baisser son
  prix à destination et monter son prix au départ : l'occasion se referme d'elle-même.
  La première campagne de mesure l'a établi : le profit culmine à trois trains et
  s'effondre à six.
- **Il donne un prix à chaque décision.** Un chargement se refuse s'il ne rapporte
  pas ce qu'il coûte à porter. C'est la condition pour que le relief, la distance et
  le choix de locomotive pèsent sur le jeu (voir le chantier du coût marginal dans
  [FINDINGS.md](FINDINGS.md)).

Ses risques connus, que tout travail sur le transport doit garder en tête :

- **Le lavage de fret.** Une compagnie qui achète et revend peut faire tourner la
  même marchandise en rond et encaisser une marge fictive. La simulation garde une
  réserve dans chaque ville et une sentinelle de marge au kilomètre pour ça.
- **La revente de ville en ville.** Sur heartland, la nourriture est livrée 70 fois
  pour une fois produite. Ce n'est pas du lavage, chaque revente paie un vrai
  écart, mais c'est ce qui rend son prix trop uniforme.

**À décider** :

- **Les transactions entre compagnies du même joueur.** Quand la compagnie de
  transport achète l'acier d'une aciérie fondée par une autre compagnie du joueur,
  à quel prix ? Au prix du marché local (chaque compagnie défend ses propres
  comptes, et l'intégration verticale n'est pas gratuite), ou à un prix interne
  choisi par le joueur (ce qui permet de déplacer du profit d'une compagnie à
  l'autre, avec ce que cela implique pour les actionnaires minoritaires) ?
- **Le transport pour autrui.** La compagnie ne transporte-t-elle que ce qu'elle
  achète, ou peut-elle aussi porter la marchandise d'un tiers contre rémunération ?

## Les véhicules

**Décidé.** Seule une compagnie de transport achète des véhicules. Ils sont un actif
de la compagnie : ils figurent à son bilan, s'amortissent, et se revendent avec
elle en cas de rachat.

*Déjà en place* : un catalogue de 15 locomotives historiques, de 1829 à 1945, sourcé
dans [SOURCES.md](SOURCES.md), mais pas encore branché sur la simulation : la
vitesse et le coût d'un train sont saisis à la main.

**À décider** : ce que le joueur choisit sur un train. La locomotive seule, ou aussi
la composition du convoi ? L'itinéraire ? Et ce qui distingue deux locomotives en
jeu : la vitesse, la capacité, le coût au kilomètre, la tenue en rampe ?

## Ce que le joueur décide

C'est la section la plus importante, parce qu'aujourd'hui le joueur ne décide
**rien** : un transporteur automatique qui connaît tous les prix joue à sa place.
Il sert d'instrument de mesure, pas de joueur.

Les décisions que la vision implique, par niveau :

| niveau | décisions |
|---|---|
| **Homme d'affaires** | fonder ou racheter une compagnie ; acheter ou vendre des actions, à crédit ou non ; renflouer ou laisser couler ; lancer une OPA |
| **Compagnie** | emprunter ; verser un dividende ; émettre des actions ; fonder une industrie ; acheter des véhicules |
| **Transport** | où poser la voie ; quels trains sur quelles lignes ; quoi acheter, où, et où le revendre |

**À décider** : jusqu'où le joueur descend. Choisit-il chaque chargement, comme un
négociant, ou fixe-t-il des consignes (une ligne, des marchandises autorisées, une
marge minimale) qu'un agent automatique applique ? Le premier est plus riche, le
second tient à l'échelle de dizaines de trains.

## Les concurrents

Des hommes d'affaires pilotés par l'ordinateur, qui jouent **avec les mêmes règles
et la même information que le joueur** : ils fondent et rachètent des compagnies,
posent des voies et jouent en bourse. Un concurrent ne doit jamais voir un prix ou
un événement que le joueur ne voit pas.

*Déjà en place* : des concurrents qui ne sont que des bilans animés par des données,
assez pour éprouver la bourse et les OPA, mais sans réseau.

## Ce qui fait la profondeur

1. **L'occasion qui se referme.** Chaque train rentable détruit une partie de ce qui
   le rend rentable. Le joueur ne gagne pas en empilant, il gagne en cherchant.
2. **Deux fortunes qui ne sont pas la même.** Ce qui est bon pour une compagnie
   n'est pas forcément bon pour son propriétaire. Un dividende enrichit le joueur et
   affaiblit sa compagnie ; un achat à crédit multiplie les gains et les ruines.
3. **L'intégration verticale.** Posséder à la fois l'aciérie et les trains qui
   emportent son acier, c'est capter les deux marges, et prendre les deux risques.
4. **Le territoire.** Le relief, la distance et l'emplacement des industries
   dessinent des régions économiques. *Pas encore vrai dans la simulation* : le
   relief ne pèse aujourd'hui sur aucune décision.

## Hors du cap

Ce que le jeu ne cherche pas à être, pour l'instant :

- **Un simulateur de circulation.** La signalisation existe pour que les trains ne
  se traversent pas, pas pour être un jeu en soi.
- **Un jeu de voyageurs.** Pas de voyageurs ni de courrier : le jeu porte sur les
  marchandises.
- **Un jeu à interface d'abord.** La simulation doit être intéressante réduite à des
  chiffres dans un terminal avant qu'on la dessine.

## Parenté avec *Railroad Tycoon 3*

Le jeu en garde la figure du magnat (une fortune personnelle distincte de celle de
la compagnie), la bourse, les OPA, le relief et le catalogue historique. Il s'en
écarte sur trois points, délibérément :

- **La compagnie achète et revend**, au lieu d'être payée pour livrer.
- **Le joueur contrôle plusieurs compagnies de natures différentes**, et c'est par
  elles qu'il fonde des industries.
- **Les marchandises seulement**, sans voyageurs ni courrier.

## Écart avec la simulation d'aujourd'hui

Ce que la vision demande et que le code n'a pas encore, par ordre de dépendance :

1. **Plusieurs compagnies contrôlées par le joueur.** Le monde n'a aujourd'hui
   qu'une seule compagnie d'exploitation (`Company`) ; la finance tient les bilans
   des concurrents, mais ceux-ci n'exploitent rien.
2. **Des industries qui appartiennent à quelqu'un.** Aujourd'hui une industrie fait
   partie de sa ville : elle n'a ni propriétaire, ni caisse, ni bilan. Pour qu'une
   compagnie en fonde une, il faut un propriétaire, un coût de construction et un
   compte de résultat.
3. **Des véhicules achetés.** Un train est déclaré par le scénario et ne coûte rien
   à acquérir ; la finance lui prête une valeur de départ forfaitaire.
4. **Un joueur qui décide.** Le transporteur automatique doit devenir un agent qui
   applique des consignes, ou céder la place aux décisions du joueur.
5. **Un relief qui pèse.** C'est le chantier en cours du coût marginal.

## Questions ouvertes, en un coup d'œil

1. Que veut dire « posséder » une compagnie : une action, ou la majorité ?
2. Une compagnie industrielle distincte, ou la compagnie de transport qui fonde ses
   propres industries ?
3. Où s'arrête « produit fini » ? Les industries primaires sont-elles rachetables ?
4. À quel prix une compagnie du joueur vend-elle à une autre compagnie du joueur ?
5. La compagnie de transport porte-t-elle aussi la marchandise d'autrui ?
6. Jusqu'où le joueur descend-il : chaque chargement, ou des consignes ?
7. Qu'est-ce qui distingue deux locomotives en jeu ?
8. Comment gagne-t-on : une fortune à atteindre, une date, des objectifs par
   scénario ? Et sur quelle période, et quelle géographie ?
