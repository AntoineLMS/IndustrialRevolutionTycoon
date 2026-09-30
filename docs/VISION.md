# Vision du jeu

*Troisième jet, 30 septembre 2026. Ce document fixe le cap : ce qu'est le jeu, qui y
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
- Il a un **score de dirigeant**, sa réputation auprès des investisseurs (voir
  plus bas).

*Déjà en place* : le module finance distingue la caisse personnelle du magnat de
celle de sa société, et le magnat renfloue ou laisse couler, achète à crédit, et
prend des participations.

## Devenir PDG : fonder une compagnie

**Décidé.** Posséder une compagnie, c'est **en être nommé PDG**. On le devient en
fondant une compagnie :

1. Le joueur met de l'argent personnel sur la table.
2. Des investisseurs y ajoutent le leur. **Plus le joueur apporte, plus les
   investisseurs apportent**, et son **score de dirigeant** augmente encore leur
   mise.
3. **Les actions sont réparties au prorata de l'argent apporté.** Un joueur qui
   apporte 30 % du capital détient 30 % des actions.

Ce qui en découle : fonder est un pari de levier. Apporter peu donne peu de capital
et peu de poids ; apporter beaucoup donne une grosse compagnie, mais expose une
grosse part de sa fortune. Et la réputation devient un actif : un dirigeant qui a
réussi lève plus d'argent que sa mise ne le justifie.

### Le score de dirigeant

**Décidé.**

- Il suit **les résultats de la compagnie** : une compagnie très rentable fait
  monter le score de son PDG.
- Un bon score **fait tenir plus longtemps** quand la rentabilité se dégrade : les
  investisseurs patientent davantage avant de voter contre un dirigeant qui a fait
  ses preuves.
- **Se faire évincer ruine la réputation.** Le score en sort durablement abîmé, et
  la prochaine levée de fonds s'en ressent.

### L'éviction

**Décidé.**

- Si la compagnie n'est pas rentable, les actionnaires peuvent démettre le PDG.
- **Il faut réunir 50 % des voix plus une.** Une action, une voix.
- **Un PDG qui détient plus de la moitié des actions ne peut pas être évincé** :
  il vote contre, et il gagne.

Ce qui en découle : la majorité absolue est une assurance. Un joueur prudent la
garde ; un joueur ambitieux la dilue pour lever davantage, et accepte de pouvoir
être renvoyé.

### Emprunter

**Décidé.** La compagnie peut emprunter. **Le taux d'intérêt dépend de trois
choses** :

- **la situation économique de la partie** : un crédit cher en crise, bon marché
  en période faste ;
- **la situation de la compagnie** : sa rentabilité, son endettement ;
- **le score du dirigeant.**

*Déjà en place* : le module finance émet des obligations et tient un découvert
bancaire explicite, mais à des taux fixés par le scénario.

**À décider** :

- **La formule de l'apport des investisseurs** : un multiple de l'apport du joueur,
  modulé par le score ? Plafonné ?
- **Le calcul du score** : sur quelle période se mesure la rentabilité, et de
  combien une éviction le fait-elle chuter ?
- **Le critère qui fait voter les investisseurs** : pertes sur combien de temps,
  cours en baisse, découvert ? Et comment le score en décale le seuil ?
- **Après l'éviction** : le joueur garde ses actions, donc sa fortune, mais perd la
  direction. Peut-il fonder une autre compagnie ? Est-ce la fin de la partie ?
- **La formule du taux d'intérêt**, et ce que veut dire « la situation économique
  de la partie » : un cycle économique à part entière, ou les événements du module
  `events` ?

## Une seule compagnie, qui transporte et produit

**Décidé, pour commencer simple.** Le joueur ne **dirige qu'une compagnie à la
fois**, et elle fait les deux métiers : le transport et l'industrie. En revanche,
il **peut investir dans d'autres compagnies**, en simple actionnaire.

Deux règles d'accès en découlent :

1. **Il faut une compagnie pour acheter des véhicules.** Un homme d'affaires seul
   ne fait pas rouler de train.
2. **Il faut une compagnie pour fonder des industries.** L'industrie appartient à
   la compagnie, pas au joueur.

### Racheter une autre compagnie

**Décidé.** La compagnie que dirige le joueur peut **proposer un prix par action**
pour racheter les actions d'une autre compagnie. **Si elle en réunit plus de la
moitié**, auprès des investisseurs ou du joueur lui-même, **elle absorbe l'autre
compagnie** : ses trains, ses industries et ses dettes lui reviennent.

*Déjà en place* : le module finance sait prendre des participations, lancer une
OPA avec une prime sur le cours et fusionner une compagnie absorbée.

**À décider** : **le joueur qui vend ses propres actions à sa propre compagnie.**
Si le joueur détient des actions de la cible, sa compagnie les lui rachète au prix
qu'il a lui-même fixé. Rien ne l'empêche alors de proposer un prix très élevé pour
faire passer l'argent de sa compagnie dans sa poche, aux dépens des autres
actionnaires. Faut-il l'interdire, plafonner le prix par rapport au cours, ou en
faire un risque (une chute du score de dirigeant, une révolte des actionnaires) ?

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

- **Au début d'un scénario, les industries n'appartiennent à personne** : ni les
  usines, ni les mines, fermes, forêts ou puits de pétrole. Elles produisent quand
  même.
- **Dans les villes, les marchandises n'appartiennent à personne.** Elles sont sur
  le marché, au prix local.
- Une industrie fondée par une compagnie vend sa production sur le marché local et
  encaisse pour cette compagnie.
- **Une compagnie n'achète de marchandise que pour la transporter** sur ses trains.
  Elle ne stocke pas, ne spécule pas, et ne transporte pas pour le compte d'un
  tiers. (Le carburant, acheté pour être brûlé, est la seule exception : voir
  « Les véhicules ».)
- *Piste pour plus tard* : donner à la compagnie qui produit une ressource la
  **primauté du transport** sur la production de ses propres industries.

**À décider** :

- Une compagnie peut-elle **racheter une industrie** qui n'appartient à personne ?
- La primauté du transport, le jour où on l'introduit : **priorité d'achat** (la
  production va sur le marché, les trains du propriétaire se servent en premier et
  paient le prix du marché, donc se paient eux-mêmes) ou **production réservée**
  (elle ne passe pas par le marché, et les concurrents n'y ont pas accès) ?

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

- **Les ordres d'un train contiennent au moins deux gares** : une de départ et une
  d'arrivée. Ils peuvent en contenir davantage.
- **Le joueur ne choisit pas le chargement.** Par défaut, le moteur de jeu le
  choisit **parmi les marchandises disponibles dans le rayon d'action de la gare de
  départ**, en fonction du **bénéfice réalisé au prochain arrêt**, lors de la
  revente.
- **Le joueur peut forcer un type de chargement** en créant les ordres du train.
- **Un train a une capacité de chargement limitée** : charger une marchandise, c'est
  renoncer à une autre.

Ce qui en découle : le joueur décide **où** passent ses trains et, s'il le veut,
**ce qu'ils emportent** ; le moteur optimise le reste, arrêt par arrêt. C'est ce
qui permet de tenir à l'échelle de dizaines de trains.

Deux écarts avec le moteur actuel (`OpportunisticHaulageSolver`) :

- **Le rayon d'action d'une gare est une notion nouvelle.** Aujourd'hui, un train
  échange avec le marché de la ville où il s'arrête, et une industrie fait partie
  de sa ville. Avec un rayon d'action, une gare se sert dans toutes les industries
  à sa portée, et une industrie mal placée peut n'être desservie par aucune gare.
- **Le prochain arrêt seulement.** Le moteur actuel estime la revente sur tous les
  arrêts à venir. La règle retenue est plus simple, plus lisible pour le joueur,
  et plus myope : un train ne charge pas pour un arrêt lointain s'il ne gagne rien
  au suivant.

**À décider** :

- **Le rayon d'action** : une distance fixe, ou qui dépend de la taille de la
  gare ?
- **Un chargement forcé** se charge-t-il même à perte ?
- **Le prix au prochain arrêt** que le moteur utilise : le prix du moment, ou le
  dernier prix connu du joueur ?

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

**Décidé.** **Le carburant s'achète au prix du marché.** Une locomotive à charbon
crée donc une vraie demande de charbon, et le coût du transport dépend de
l'économie qu'il dessert. *Plus tard* : une mécanique pour influencer le cours du
carburant.

*Déjà en place* : un catalogue de 15 locomotives historiques, de 1829 à 1945, sourcé
dans [SOURCES.md](SOURCES.md), avec leur effort de traction. Il n'est pas encore
branché : la vitesse et le coût d'un train sont saisis à la main. Le modèle de coût
« mass » (FINDINGS.md, « Le coût marginal réel ») connaît déjà la masse d'une
locomotive et de ses wagons.

**À décider** : **où** la locomotive fait le plein. À chaque arrêt, au prix local ?
Seulement dans certaines gares ? Une ville sans charbon peut-elle arrêter une ligne
à vapeur ?

## Ce que le joueur décide

| niveau | décisions |
|---|---|
| **Homme d'affaires** | fonder une compagnie et y apporter son argent ; investir dans d'autres compagnies ; acheter ou vendre des actions ; renflouer sa compagnie ou la laisser couler ; garder ou non la majorité |
| **PDG** | emprunter ; verser un dividende ; émettre des actions ; fonder une industrie ; acheter des véhicules ; poser des voies ; proposer un prix pour racheter une autre compagnie |
| **Trains** | les gares desservies, deux au moins ; un type de chargement forcé, si on le veut |

Tout le reste est le travail du moteur : quoi charger quand rien n'est forcé, et
où le revendre.

## Gagner : des objectifs par scénario

**Décidé.** Les conditions de victoire se définissent **par scénario**. Types
d'objectifs :

- **amasser une fortune personnelle** ;
- **transporter un certain nombre de cargaisons** d'une marchandise donnée, au
  total ou **vers une ville précise** ;
- **relier deux villes avant une date donnée.**

Un scénario peut en combiner plusieurs. **Le reste se décidera avec les scénarios** :
les paliers de réussite, ce qui fait perdre, la période et la géographie de la
première campagne.

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
   des investisseurs, le score de dirigeant et la répartition au prorata.
2. **Des industries qui appartiennent à quelqu'un.** Aujourd'hui une industrie fait
   partie de sa ville : elle n'a ni propriétaire, ni caisse, ni coût de
   construction. Les industries de départ peuvent rester sans propriétaire ; celles
   qu'une compagnie fonde doivent encaisser pour elle.
3. **Le rayon d'action des gares.** Les industries doivent avoir une position sur la
   carte, et les gares se servir dans celles qui sont à leur portée.
4. **Des véhicules achetés**, avec les caractéristiques du tableau ci-dessus, et un
   carburant acheté au marché. Un train est aujourd'hui déclaré par le scénario et
   ne coûte rien à acquérir.
5. **Des ordres de train** : une liste de gares, et des chargements forcés. Le
   transporteur automatique décide seul, sur des lignes fixes, en regardant tous les
   arrêts à venir, avec une information parfaite.
6. **Une bourse solide** : une profondeur de carnet finie, et des grandeurs
   financières moyennées. Puis le vote d'éviction, le rachat par offre sur les
   actions, et des taux d'emprunt qui dépendent de la situation.
7. **Des objectifs de scénario.**
8. **Un relief qui pèse.** Le coût marginal réel existe, en option
   (`haulage.costModel = "mass"`) : il crée deux bassins de part et d'autre du col
   sans détruire de valeur, mais seulement au-delà d'une traction de 0,12 à 0,15.
   En faire le défaut est une décision ouverte (FINDINGS.md, « Le coût marginal
   réel »).

## Questions ouvertes, en un coup d'œil

1. La formule de l'apport des investisseurs, et le calcul du score de dirigeant.
2. Le critère qui fait voter les investisseurs, et ce qui suit une éviction.
3. La formule du taux d'intérêt, et ce qu'est « la situation économique de la
   partie ».
4. Le joueur qui vend ses propres actions à sa propre compagnie : l'interdire, le
   plafonner, ou en faire un risque ?
5. Une compagnie peut-elle racheter une industrie qui n'appartient à personne ?
6. La primauté du transport : priorité d'achat, ou production réservée ?
7. Le rayon d'action d'une gare ; un chargement forcé se charge-t-il à perte ; quel
   prix le moteur connaît-il au prochain arrêt ?
8. Où une locomotive fait-elle le plein ?
