# Vision du jeu

*Cinquième jet, 30 septembre 2026. Ce document fixe le cap : ce qu'est le jeu, qui y
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
   mise, **jusqu'à dix fois l'apport du joueur**.
3. **Les actions sont réparties au prorata de l'argent apporté.** Un joueur qui
   apporte 30 % du capital détient 30 % des actions.

Au maximum, le joueur détient donc environ 9 % de sa compagnie (1 part pour 10) :
il démarre avec beaucoup plus d'argent, mais beaucoup moins de contrôle.

Ce qui en découle : fonder est un pari de levier. Apporter peu donne peu de capital
et peu de poids ; apporter beaucoup donne une grosse compagnie, mais expose une
grosse part de sa fortune. Et la réputation devient un actif : un dirigeant qui a
réussi lève plus d'argent que sa mise ne le justifie.

### Le score de dirigeant

**Décidé.**

- Il suit **les résultats de la compagnie** : une compagnie très rentable fait
  monter le score de son PDG.
- Un bon score **fait tenir plus longtemps** quand la rentabilité se dégrade : il
  y a davantage de points à perdre avant la sanction.
- **Les investisseurs fixent des objectifs.** Un objectif non atteint fait perdre
  des points.
- **Une perte se sanctionne à proportion de ce qu'elle pèse.** Perdre 2 000 sur
  l'année quand la compagnie vaut 2 millions ne coûte presque rien ; une perte
  annuelle qui représente un gros morceau de la valeur de la compagnie coûte une
  lourde sanction. La mesure est donc la perte **rapportée à la valeur de la
  compagnie** (cours × nombre d'actions), pas la perte en valeur absolue.
- **Se faire évincer ruine la réputation.** Le score en sort durablement abîmé, et
  la prochaine levée de fonds s'en ressent.

### L'éviction

**Décidé.**

- **Il faut réunir 50 % des voix plus une.** Une action, une voix.
- **Un PDG qui détient plus de la moitié des actions ne peut pas être évincé** :
  il vote contre, et il gagne.
- **Un score à zéro déclenche le vote.**

*Lecture retenue, à confirmer.* Un score à zéro ne vaut pas éviction à lui seul :
il fait voter contre le PDG tous les actionnaires autres que lui. L'éviction a lieu
s'ils détiennent plus de la moitié des actions, ce qui garde intacte la protection
du PDG majoritaire. Et comme l'apport des investisseurs peut aller jusqu'à dix fois
celui du joueur, un joueur qui a levé au maximum ne détient qu'environ 9 % : un
score à zéro le renvoie à coup sûr.

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
bancaire explicite. Avec une conjoncture (module `cycle`), le taux d'une obligation
est fixé à son émission : taux facial, plus l'ajustement de la phase, plus une prime
de risque selon le levier et la rentabilité ; le bonus du dirigeant a sa place dans
la formule et vaut 0 tant que le score n'existe pas. Sans conjoncture, les taux du
scénario.

### Le cycle économique

**Décidé.** La partie a une **conjoncture** : une suite de phases (expansion,
ralentissement, crise, reprise) aux durées tirées au sort dans des bornes fixées
par les données, sur un flux aléatoire qui lui est propre. **Les événements la
font bouger** : une panique historique déclenche une crise à sa date, et les
événements aléatoires peuvent précipiter ou retarder un changement de phase.

Ce qu'elle touche :

- **les taux d'emprunt** : un taux de base selon la phase, plus une prime de risque
  selon la compagnie, moins un bonus selon le score du dirigeant ;
- **la bourse** : le multiple de valorisation monte en expansion et s'effondre en
  crise ;
- **les investisseurs** : ils apportent moins et patientent moins en crise ;
- **la demande des villes**, modestement.

Pourquoi un effet modeste sur la demande : un choc qui touche toutes les villes à la
fois fait bouger tous les prix ensemble et ne crée aucun écart à exploiter
(FINDINGS.md, section sur les événements). Le cycle donne surtout **un rythme
financier** : emprunter pas cher en expansion, racheter un concurrent dont le cours
s'est effondré, garder de la trésorerie avant la crise.

**Écarté** : un cycle engendré par l'économie elle-même (l'activité nourrit la
confiance, qui nourrit la demande). Plus réaliste, mais une telle boucle diverge
ou oscille facilement, et elle est très difficile à équilibrer.

*Déjà en place* : le module `cycle` et son scénario `heartland-cycle` (taux, bourse et
demande ; les investisseurs attendent le module de fondation, leur appétit est déjà
publié). Les événements le font bouger par un attribut de données : la panique de
1873 force la crise, les récoltes et le bâtiment poussent la conjoncture. Le journal
public dit la phase, jamais la date où elle finira. Mesures dans FINDINGS.md.

**À décider** :

- **La courbe entre score et apport** : à quel score les investisseurs apportent-ils
  dix fois la mise du joueur, et combien à un score faible ?
- **Les objectifs des investisseurs** : résultat annuel, dividende, cours de
  l'action ? Qui les fixe, et à quelle fréquence ?
- **Le barème** : combien de points gagne une année rentable, combien coûte un
  objectif manqué, et selon quelle courbe une perte rapportée à la valeur de la
  compagnie devient-elle une lourde sanction ?
- **Après l'éviction** : le joueur garde ses actions, donc sa fortune, mais perd la
  direction. Peut-il fonder une autre compagnie ? Est-ce la fin de la partie ?
- **La formule du taux d'intérêt** : *la situation économique de la partie est un
  cycle à part entière, que les événements font bouger* (décidé plus haut) ; la
  formule livrée — facial + phase + prime de risque − bonus du dirigeant — et ses
  options sont chiffrées dans FINDINGS.md. Reste à fixer le barème du bonus, avec le
  score.

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

**Décidé : le prix offert est plafonné à deux fois le cours.** Si le joueur détient
des actions de la cible, sa compagnie peut les lui racheter au prix qu'il a
lui-même fixé, et donc faire passer de l'argent de sa compagnie dans sa poche aux
dépens des autres actionnaires. **C'est voulu** : c'est une manœuvre d'homme
d'affaires, et le plafond de deux fois le cours en borne l'ampleur.

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
- **Une compagnie peut racheter une industrie** qui n'appartient à personne.
- **Primauté du transport, sous forme de priorité d'achat** (piste pour plus tard) :
  la production d'une industrie va sur le marché comme toute autre ; les trains de
  la compagnie qui la possède se servent en premier et paient le prix du marché,
  donc se paient eux-mêmes. Les concurrents se servent dans ce qui reste. La
  production n'est jamais réservée.

**À décider** : **le prix d'une industrie** sans propriétaire. Sa valeur de
construction, un multiple de ce qu'elle rapporte, ou un prix fixé par le scénario ?

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

**Décidé.**

- **Le rayon d'action dépend de la taille de la gare.** Une petite gare ne dessert
  qu'une industrie à proximité ; une grande gare peut englober toute une grande
  ville.
- **Rien ne se charge à perte**, même un chargement forcé : forcer un type de
  marchandise restreint le choix du moteur, sans l'obliger à perdre de l'argent.
- **Le moteur utilise le prix du moment** au prochain arrêt.

**À décider** : les tailles de gare, leur rayon et leur prix de construction.

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

**Décidé.**

- **Le carburant s'achète en gare, au prix du marché local**, celui de la ville de
  la gare. Le coût du transport suit donc les prix de l'économie qu'il dessert.
- **Le carburant n'a pas besoin d'être disponible là où le train fait le plein.**
  Pour l'instant, c'est un coût indexé sur le prix du marché, pas un prélèvement
  physique : il ne vide aucun stock, et une ville sans charbon ne bloque pas une
  ligne à vapeur.
- *Plus tard* : une mécanique pour influencer le cours du carburant.

*Déjà en place* : un catalogue de 15 locomotives historiques, de 1829 à 1945, sourcé
dans [SOURCES.md](SOURCES.md), avec leur effort de traction. Il n'est pas encore
branché : la vitesse et le coût d'un train sont saisis à la main. Le modèle de coût
« mass » (FINDINGS.md, « Le coût marginal réel ») connaît déjà la masse d'une
locomotive et de ses wagons.

**Décidé, en connaissance de cause** : le prix local vaut même quand la ville n'a
pas de carburant en stock. Une ville sans charbon affiche le prix plafond (trois
fois la référence) : y faire le plein coûte donc cher, et c'est voulu. Où l'on fait
le plein devient un choix d'itinéraire : une ligne à vapeur a intérêt à passer par
une ville où le charbon abonde.

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
   financières moyennées. Puis le vote d'éviction et le rachat par offre sur les
   actions. (Des taux d'emprunt qui dépendent de la conjoncture et de la compagnie
   existent désormais ; il leur manque le score du dirigeant. La conjoncture rend la
   profondeur de carnet plus urgente : un cours qui oscille est une pompe sur un
   flottant infini.)
7. **Des objectifs de scénario.**
8. **Un relief qui pèse.** Le coût marginal réel existe, en option
   (`haulage.costModel = "mass"`) : il crée deux bassins de part et d'autre du col
   sans détruire de valeur, mais seulement au-delà d'une traction de 0,12 à 0,15.
   En faire le défaut est une décision ouverte (FINDINGS.md, « Le coût marginal
   réel »).

## Questions ouvertes, en un coup d'œil

1. La courbe entre score et apport des investisseurs, les objectifs des
   investisseurs, et le barème des points.
2. Ce qui suit une éviction : fonder une autre compagnie, ou fin de partie ?
3. Les formules du cycle économique : durées des phases, taux par phase, force de
   la bourse et de la demande, force de l'effet des événements. *Mesuré, options
   chiffrées* dans FINDINGS.md, « Le cycle économique » : le livré (phases calées sur
   le NBER, −1 / +3 points de taux, multiple ×1,3 / ×0,6, demande ±5 %, panique et
   poussées équilibrées) donne un rythme lisible sans aucune faillite de compagnie ;
   ce qui reste à trancher dépend de la profondeur de carnet finie, sans laquelle la
   fortune du magnat n'est pas une mesure fiable.
4. Le prix d'une industrie sans propriétaire.
5. Les tailles de gare, leur rayon d'action et leur prix.
