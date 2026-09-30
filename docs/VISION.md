# Vision du jeu

*Deuxième jet, 30 septembre 2026. Ce document fixe le cap : ce qu'est le jeu, qui y
joue, et ce que le joueur décide. [ARCHITECTURE.md](ARCHITECTURE.md) dit comment
la simulation tient debout, [CONTRACTS.md](CONTRACTS.md) comment elle se découpe,
[FINDINGS.md](FINDINGS.md) ce qu'elle fait quand on la mesure. Celui-ci dit
pourquoi elle existe.*

*Chaque section distingue ce qui est **décidé** de ce qui reste **à décider**. Une
décision ne se prend pas au détour d'un correctif : elle se prend ici, puis le code
suit. Quand une décision a demandé une interprétation, elle est signalée
« lecture retenue » : c'est à confirmer, pas à coder les yeux fermés.*

## Le pitch

Amérique du Nord, ère industrielle. Tu n'es pas une compagnie de chemin de fer :
tu es **un homme d'affaires**. Tu commences avec une fortune personnelle et une
réputation, et tu convaincs des investisseurs de te suivre pour fonder une
compagnie dont tu deviens le PDG.

Ta compagnie pose des voies, achète des locomotives et fonde des industries qui
transforment les ressources de la carte en produits de plus en plus élaborés. Ses
trains **achètent les marchandises au départ et les revendent à destination** :
chaque livraison referme l'écart de prix qui la rendait rentable, et il faut sans
cesse trouver la prochaine asymétrie. Tu ne choisis pas chaque chargement ; tu
donnes des ordres à tes trains, et le moteur fait le reste.

Si ta compagnie ne rapporte pas, les investisseurs peuvent te démettre. Ce qui
compte à la fin dépend du scénario : une fortune à amasser, des cargaisons à livrer,
deux villes à relier avant une date. L'Histoire s'invite en chemin : grève de
l'anthracite, incendie de Chicago, Panique de 1873.

## Le joueur est l'homme d'affaires

**Décidé.** Le joueur incarne une personne, pas une entreprise. Sa fortune
personnelle est distincte de celle de la compagnie qu'il dirige.

- Le joueur agit **à travers** sa compagnie. Il ne possède ni train ni industrie en
  propre : il possède des actions, et c'est la compagnie qui possède les trains et
  les industries.
- L'argent ne circule pas librement entre sa poche et la compagnie. Il entre par
  l'apport au capital ; il sort par les dividendes et la vente d'actions.
- Il a un **score de gestionnaire**, sa réputation auprès des investisseurs (voir
  plus bas).

*Déjà en place* : le module finance distingue la caisse personnelle du magnat de
celle de sa société, et le magnat renfloue ou laisse couler, achète à crédit, et
prend des participations.

## Devenir PDG : fonder une compagnie

**Décidé.** Posséder une compagnie, c'est **en être nommé PDG**. On le devient en
fondant une compagnie :

1. Le joueur met de l'argent personnel sur la table.
2. Des investisseurs y ajoutent le leur. **Plus le joueur apporte, plus les
   investisseurs apportent**, et son **score de gestionnaire** augmente encore leur
   mise.
3. **Les actions sont réparties au prorata de l'argent apporté.** Un joueur qui
   apporte 30 % du capital détient 30 % des actions.
4. **Si la compagnie n'est pas rentable, les investisseurs peuvent évincer le PDG.**

Ce qui en découle : fonder est un pari de levier. Apporter peu donne peu de capital
et peu de poids ; apporter beaucoup donne une grosse compagnie, mais expose une
grosse part de sa fortune. Et la réputation devient un actif : un gestionnaire qui
a réussi lève plus d'argent que sa mise ne le justifie.

**À décider** :

- **La formule de l'apport des investisseurs** : un multiple de l'apport du joueur,
  modulé par le score ? Plafonné ?
- **Le score de gestionnaire** : d'où vient-il (rentabilité passée, évolution du
  cours, dividendes versés, objectifs de scénario atteints) ? Monte-t-il et
  descend-il en cours de partie ? Survit-il à une éviction ?
- **L'éviction** : sur quel critère (pertes sur combien de temps, cours en baisse,
  découvert) ? Par un vote pondéré par les actions ? Un joueur **majoritaire**
  peut-il être évincé ?
- **Après l'éviction** : le joueur garde ses actions (donc sa fortune), mais perd
  la direction. Peut-il fonder une autre compagnie ? Est-ce la fin de la partie ?
- **Racheter plutôt que fonder** : le premier jet prévoyait de pouvoir racheter une
  compagnie existante. Comment en devient-on PDG : en achetant la majorité ?

## Une seule compagnie, qui transporte et produit

**Décidé, pour commencer simple.** Le joueur ne dirige **qu'une compagnie à la
fois**, et elle fait les deux métiers : le transport et l'industrie.

Deux règles d'accès en découlent :

1. **Il faut une compagnie pour acheter des véhicules.** Un homme d'affaires seul
   ne fait pas rouler de train.
2. **Il faut une compagnie pour fonder des industries.** L'industrie appartient à
   la compagnie, pas au joueur.

**À décider** : diriger une compagnie empêche-t-il de détenir des actions d'autres
compagnies, en simple investisseur ?

## Ressources de base et produits finis

**Décidé.**

- Les **produits de base** sont ceux que **la carte produit d'elle-même** : ils
  apparaissent sur le scénario (blé, grumes, charbon, minerai…). **Personne ne peut
  augmenter volontairement leur approvisionnement** : on ne fonde ni ferme, ni
  mine, ni forêt.
- Les **produits finis** sont **tous les autres** : farine, fonte, planches, acier,
  nourriture… Une compagnie peut fonder toute industrie qui en fabrique.
- Un produit fini peut servir d'intrant à une autre industrie. Le jeu est donc une
  **chaîne d'approvisionnement** : fonder un moulin crée une demande de blé et une
  offre de farine, que d'autres industries pourront transformer à leur tour.

Ce qui en découle : la carte fixe la quantité de matière qui entre dans l'économie.
Le joueur ne gagne pas en produisant plus de ressources, mais en choisissant
**où** les transformer et **par où** les faire passer.

## Les marchandises et le marché

**Décidé.**

- **Dans les villes, les marchandises n'appartiennent à personne.** Elles sont sur
  le marché de la ville, au prix local.
- Elles sont **produites par des industries qui, elles, appartiennent à des
  compagnies.** Une industrie vend sa production sur le marché local et encaisse
  pour la compagnie qui la possède.
- **La compagnie ne transporte que ce qu'elle achète.** Pas de fret pour le compte
  d'un tiers : tout passe par le marché.
- *Piste pour plus tard* : donner à la compagnie qui produit une ressource la
  **primauté du transport** : ses trains se servent en premier dans la production
  de ses propres industries.

**À décider** :

- Les ressources de base apparaissent sans industrie : **à qui appartiennent** les
  fermes, mines et forêts de la carte ? À personne, et la production va
  directement au marché ?
- Au début d'un scénario, les industries de produits finis existantes appartiennent
  à qui : à des compagnies concurrentes, ou à personne ?
- La primauté du transport : priorité d'achat au prix du marché, ou production
  réservée ?

## La compagnie transporte : acheter au départ, revendre à destination

**Décidé.** La compagnie **achète la marchandise au départ du trajet, au prix
local, et la revend à destination, au prix local.** Elle porte la marchandise, et
le risque, pendant le voyage. Son revenu est l'écart entre les deux prix, moins ce
que coûte le voyage.

C'est la différence de fond avec *Railroad Tycoon 3*, où le chemin de fer est
rémunéré pour livrer sans jamais posséder la cargaison. Pourquoi ce modèle est le
bon pour ce jeu :

- **C'est lui qui fait la boucle de jeu.** Livrer une marchandise fait baisser son
  prix à destination et monter son prix au départ : l'occasion se referme d'elle-même.
  La première campagne de mesure l'a établi : le profit culmine à trois trains et
  s'effondre à six.
- **Il donne un prix à chaque décision.** Un chargement se refuse s'il ne rapporte
  pas ce qu'il coûte à porter. C'est la condition pour que le relief, la distance et
  la locomotive pèsent sur le jeu (voir le chantier du coût marginal dans
  [FINDINGS.md](FINDINGS.md)).

Ses risques connus, que tout travail sur le transport doit garder en tête :

- **Le lavage de fret.** Une compagnie qui achète et revend peut faire tourner la
  même marchandise en rond et encaisser une marge fictive. La simulation garde une
  réserve dans chaque ville et une sentinelle de marge au kilomètre pour ça.
- **La revente de ville en ville.** Sur heartland, la nourriture est livrée 70 fois
  pour une fois produite. Ce n'est pas du lavage, chaque revente paie un vrai
  écart, mais c'est ce qui rend son prix trop uniforme.

## Les ordres de train et le chargement

**Décidé.**

- **Le joueur ne choisit pas le chargement.** Le moteur de jeu le choisit, sauf si
  les **ordres du train** le précisent.
- Le moteur charge **les marchandises dont le bénéfice futur sera le plus grand.**
- **Un train a une capacité de chargement limitée** : charger une marchandise, c'est
  renoncer à une autre.

Ce qui en découle : le joueur décide **où** passent ses trains et **ce qu'il leur
interdit ou leur impose** ; le moteur optimise dans ce cadre. C'est ce qui permet
de tenir à l'échelle de dizaines de trains.

*Déjà en place* : `OpportunisticHaulageSolver` fait déjà ce travail d'optimisation,
mais il joue sans ordres, et avec une information parfaite sur tous les prix.

**À décider** :

- **Ce que contiennent les ordres** : un itinéraire (les gares desservies, dans
  l'ordre), des marchandises imposées ou interdites par gare, une marge minimale ?
- **Ce que le moteur sait** pour estimer le « bénéfice futur » : les prix de toutes
  les villes, en temps réel, comme aujourd'hui ? Ou seulement ce que le joueur
  pourrait voir ? La règle posée pour les concurrents (aucune information que le
  joueur n'a pas) devrait valoir aussi pour le moteur qui joue pour le joueur.

## Les véhicules

**Décidé.** Seule une compagnie achète des véhicules. Ils sont un actif de la
compagnie : ils figurent à son bilan et s'amortissent.

Ce qui distingue deux locomotives :

| caractéristique | ce qu'elle change en jeu |
|---|---|
| **Puissance** | la charge qu'elle remorque, et sa tenue en rampe |
| **Vitesse** | le nombre de trajets par an, donc la vitesse à laquelle elle exploite un écart avant qu'il se referme |
| **Prix** | l'investissement de départ |
| **Carburant** | ce qu'elle brûle : bois, charbon, fioul… |
| **Coût du carburant** | le coût d'exploitation au kilomètre |
| **Coût de maintenance** | le coût fixe de la posséder, qu'elle roule ou non |

*Déjà en place* : un catalogue de 15 locomotives historiques, de 1829 à 1945, sourcé
dans [SOURCES.md](SOURCES.md), avec leur effort de traction. Il n'est pas encore
branché : la vitesse et le coût d'un train sont saisis à la main.

**À décider** : **le carburant vient-il du marché ?** Une locomotive à charbon qui
achète son charbon en ville crée une demande de charbon réelle, et relie le coût du
transport à l'économie qu'il dessert. C'est plus riche, et plus difficile à
équilibrer, qu'un coût fixe au kilomètre.

## Ce que le joueur décide

| niveau | décisions |
|---|---|
| **Homme d'affaires** | fonder une compagnie et y apporter son argent ; acheter ou vendre des actions ; renflouer sa compagnie ou la laisser couler |
| **PDG** | emprunter ; verser un dividende ; émettre des actions ; fonder une industrie ; acheter des véhicules ; poser des voies |
| **Trains** | l'itinéraire de chaque train ; les marchandises imposées ou interdites |

Tout le reste est le travail du moteur : quoi charger, où le revendre, à quel prix.

## Gagner : des objectifs par scénario

**Décidé.** Les conditions de victoire se définissent **par scénario**. Types
d'objectifs :

- **amasser une fortune personnelle** ;
- **transporter un certain nombre de cargaisons** d'une marchandise donnée, au
  total ou **vers une ville précise** ;
- **relier deux villes avant une date donnée.**

Un scénario peut en combiner plusieurs.

**À décider** : les paliers (une réussite simple, ou plusieurs niveaux comme les
médailles de *Railroad Tycoon 3*) ; ce qui fait perdre (faillite, éviction, date
dépassée) ; la période et la géographie de la première campagne.

## La bourse doit être solide

**Décidé.** **Le prix d'une compagnie est son cours multiplié par son nombre
d'actions.** C'est à ce prix que se rachètent des parts, et c'est lui qui fait la
fortune du joueur, faite en grande partie de ses actions.

*Lecture retenue.* Cette réponse portait sur la question « à quel prix une
compagnie du joueur vend-elle à une autre de ses compagnies ? ». Avec une seule
compagnie par joueur, cette question disparaît : la compagnie vend la production de
ses industries sur le marché local et y rachète ce que ses trains emportent, au prix
du marché. La réponse a été retenue comme **la valeur d'une compagnie**.

Ce qui en découle : **tout ce qui fausse le cours fausse le jeu.** Deux dettes
connues du module finance ([CONTRACTS.md](CONTRACTS.md)) deviennent donc
prioritaires dès que le joueur fonde et rachète :

1. **Le flottant est une contrepartie de profondeur infinie.** Il absorbe n'importe
   quel volume au cours affiché : on peut acheter ou vendre sans limite, et les
   plus-values viennent de l'extérieur du modèle.
2. **Aucune statistique moyennée côté finance.** Le cours se lit au dernier tick,
   alors qu'un tick d'ordre de bourse porte encore l'impact de l'ordre.

## Les concurrents

Des hommes d'affaires pilotés par l'ordinateur, qui jouent **avec les mêmes règles
et la même information que le joueur** : ils fondent des compagnies, posent des
voies, fondent des industries et jouent en bourse.

*Déjà en place* : des concurrents qui ne sont que des bilans animés par des données,
assez pour éprouver la bourse et les OPA, mais sans réseau.

## Ce qui fait la profondeur

1. **L'occasion qui se referme.** Chaque train rentable détruit une partie de ce qui
   le rend rentable. Le joueur ne gagne pas en empilant, il gagne en cherchant.
2. **Deux fortunes qui ne sont pas la même.** Ce qui est bon pour la compagnie n'est
   pas forcément bon pour son PDG. Un dividende enrichit le joueur et affaiblit sa
   compagnie, et une compagnie affaiblie peut le faire évincer.
3. **La chaîne d'approvisionnement.** La matière première est fixée par la carte ;
   fonder une industrie, c'est choisir où la transformer, et créer une demande que
   les trains devront servir.
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
la compagnie), la bourse, le relief, le catalogue historique et les objectifs par
scénario. Il s'en écarte délibérément sur quatre points :

- **La compagnie achète et revend**, au lieu d'être payée pour livrer.
- **On devient PDG en convainquant des investisseurs**, et on peut être évincé.
- **La compagnie fonde des industries**, et la carte fixe la matière première.
- **Les marchandises seulement**, sans voyageurs ni courrier.

## Écart avec la simulation d'aujourd'hui

Ce que la vision demande et que le code n'a pas encore, par ordre de dépendance :

1. **Fonder une compagnie.** La compagnie existe dès le premier tick, avec une mise
   de départ et un magnat fondateur à 30 % fixés par le scénario. Il manque l'apport
   des investisseurs, le score de gestionnaire et la répartition au prorata.
2. **Des industries qui appartiennent à une compagnie.** Aujourd'hui une industrie
   fait partie de sa ville : elle n'a ni propriétaire, ni caisse, ni coût de
   construction. Il faut qu'elle vende sa production au marché pour le compte de sa
   compagnie, et qu'une compagnie puisse en fonder une.
3. **Des véhicules achetés**, avec les caractéristiques du tableau ci-dessus. Un
   train est aujourd'hui déclaré par le scénario et ne coûte rien à acquérir.
4. **Des ordres de train.** Le transporteur automatique décide seul, sur des lignes
   fixes, avec une information parfaite.
5. **Une bourse solide** : une profondeur de carnet finie, et des grandeurs
   financières moyennées.
6. **L'éviction du PDG**, et les objectifs de scénario.
7. **Un relief qui pèse** : c'est le chantier en cours du coût marginal.

## Questions ouvertes, en un coup d'œil

1. La formule de l'apport des investisseurs, et le calcul du score de gestionnaire.
2. Le critère d'éviction, le cas du PDG majoritaire, et ce qui suit une éviction.
3. Racheter une compagnie existante : comment en devient-on PDG ?
4. Diriger une compagnie empêche-t-il d'investir dans d'autres ?
5. À qui appartiennent les ressources de base et les industries existantes au
   départ ?
6. La primauté du transport : priorité d'achat, ou production réservée ?
7. Ce que contiennent les ordres de train, et ce que le moteur a le droit de savoir.
8. Le carburant s'achète-t-il sur le marché ?
9. Les paliers de victoire, ce qui fait perdre, la période et la géographie de la
   première campagne.
