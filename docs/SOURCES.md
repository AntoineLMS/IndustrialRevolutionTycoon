# Sources — modules `content`, `events` et `cycle`

Ce document couvre les deux livrables du module `content` :
[`data/locomotives.json`](../data/locomotives.json) (catalogue historique) et
[`data/ironpeak.json`](../data/ironpeak.json) (second scénario, chaîne acier) ;
et les événements historiques du module `events`, déclarés dans
[`data/heartland-events.json`](../data/heartland-events.json) — voir la
[section dédiée](#evenements) ; et les masses du train du modèle de coût `mass`
([`data/sierra-marginal.json`](../data/sierra-marginal.json)) — voir
[« Masses du train »](#masses-du-train) ; et les dates et durées qui calent la
conjoncture du module `cycle` — voir [« Cycle économique »](#cycle-economique).

Principe suivi partout : une caractéristique **physique** (année, effort de
traction, vitesse, masse) est documentée quand une source le permet, et
marquée **[estimé]** avec sa méthode de calcul sinon. Une valeur
**économique** (coût d'achat, coût d'entretien, consommation en unités de
jeu) n'est *jamais* une donnée historique — convertir un coût en livres de
1855 ou en Reichsmark de 1935 vers une unité monétaire de jeu abstraite est
un choix de conception, pas une mesure. Ces valeurs sont donc systématiquement
estimées, par une formule explicite plutôt qu'au jugé, afin qu'elles restent
cohérentes entre elles (voir la note de méthode plus bas).

## Catalogue de locomotives — `data/locomotives.json`

Recherches effectuées via WebSearch le 27 septembre 2026 (Wikipédia et sites
spécialisés de patrimoine ferroviaire). Pour chaque machine : ce qui est
documenté, ce qui est estimé, et pourquoi.

### Rocket (Stephenson, 1829)
- **Documenté** : effort de traction 825 lbf (3,7 kN) ; vitesse max. 30 mph
  (48 km/h) ; masse 4,3 t.
- Source : [Wikipédia — Stephenson's Rocket](https://en.wikipedia.org/wiki/Stephenson%27s_Rocket) ;
  [steamlocomotives.org](https://www.steamlocomotives.org/locomotives/rocket).

### Stourbridge Lion (Foster, Rastrick & Co, 1829)
- **Documenté** : masse locomotive 6,4 t (14 000 lb) ; vitesse au retour du
  premier essai 15 mph (24 km/h) ; alésage/course des cylindres 8,5 × 36 in ;
  roues motrices 48 in.
- **Estimé** : effort de traction (10,2 kN) — non documenté directement ;
  calculé par la formule standard `TE = 0,85 × P × d² × s / D` avec une
  pression de chaudière supposée de 50 psi, comparable aux machines
  britanniques contemporaines (aucune pression n'est publiée pour cette
  machine précise).
- Source : [Wikipédia — Stourbridge Lion](https://en.wikipedia.org/wiki/Stourbridge_Lion) ;
  [Smithsonian — modèle du Stourbridge Lion](https://americanhistory.si.edu/collections/object/nmah_687356).

### Planet (Robert Stephenson and Company, 1830)
- **Documenté** : masse 4,32 t ; cylindres 11 × 16 in ; roues motrices 60 in ;
  trajet Liverpool–Manchester (≈50 km) couvert en 1 h, soit ≈48 km/h de
  moyenne — utilisé ici comme vitesse déclarée, en notant que c'est une
  moyenne de service et non un maximum instantané mesuré.
- **Estimé** : effort de traction (6,1 kN), même formule et même hypothèse de
  pression que le Stourbridge Lion (non documentée pour cette machine).
- Source : [Wikipédia — Planet (locomotive)](https://en.wikipedia.org/wiki/Planet_(locomotive)).

### John Bull (Robert Stephenson and Company / Camden & Amboy, 1831)
- **Documenté** : masse ≈9,1 t ; vitesse 25–30 mph (retenu : 48 km/h) ;
  cylindres 9 × 20 in ; roues motrices 54 in.
- **Estimé** : effort de traction (5,7 kN), même formule, pression supposée
  50 psi (non documentée).
- Source : [Wikipédia — John Bull (locomotive)](https://en.wikipedia.org/wiki/John_Bull_(locomotive)) ;
  [Smithsonian — John Bull](https://www.si.edu/object/steam-locomotive-john-bull:nmah_841968).

### Lafayette (Norris Locomotive Works / B&O n°13, 1837)
- **Documenté** : effort de traction 2 325 lbf (10,3 kN) ; masse « juste plus
  de 10 tonnes » ; roues motrices 48 in ; cylindres 10,5 × 20 in.
- **Estimé** : vitesse (40 km/h) — non trouvée pour cette machine précise ;
  retenue par comparaison avec les vitesses typiques des premiers chemins de
  fer américains de la même décennie (John Bull, Stourbridge Lion).
- Source : [Norris Locomotive Works — american-rails.com](https://www.american-rails.com/norris.html) ;
  [Locomotive Wiki — Lafayette](https://locomotive.fandom.com/wiki/Baltimore_%26_Ohio_No._13_%22Lafayette%22).

### Fire Fly (Great Western Railway, voie large, 1840)
- **Documenté** : masse 24,2 t ; capable de tracter 80 t à 60 mph (97 km/h,
  retenu comme vitesse max.) ; moyenne de 50 mph sur un trajet enregistré ;
  cylindres 15 × 18 in (puis 16 × 20 in) ; roues motrices 84 in (7 ft).
- **Estimé** : effort de traction (9,1 kN), même formule, pression supposée
  50 psi (non publiée pour cette classe).
- Source : [Wikipédia — GWR Firefly Class](https://en.wikipedia.org/wiki/GWR_Firefly_Class).

### Iron Duke (Great Western Railway, voie large, 1847)
- **Documenté** : effort de traction 8 100 lbf (36,0 kN) ; masse 35,5 tonnes
  longues (36,1 t) ; vitesse maximale enregistrée 78,2 mph (125,9 km/h,
  arrondi à 126) en service sur le *Flying Dutchman* — la classe était
  estimée capable de 80 mph, mais c'est la vitesse *mesurée* qui est retenue
  ici.
- Source : [Wikipédia — GWR Iron Duke class](https://en.wikipedia.org/wiki/GWR_Iron_Duke_class) ;
  [Locomotive Wiki — GWR Iron Duke Class](https://locomotive.fandom.com/wiki/GWR_Iron_Duke_Class).

### Crampton n°80 « Le Continent » (Cail & Cie, France, 1852)
- **Documenté** : vitesse de conception 120 km/h ; effort de traction 18 kN ;
  puissance 295 kW ; masse (avec tender) 50,5 t.
- Source : [Wikipédia — Crampton locomotive](https://en.wikipedia.org/wiki/Crampton_locomotive) ;
  [all-andorra.com — Crampton n°80 Le Continent](https://all-andorra.com/the-french-steam-locomotive-type-210-crampton-80-le-continent-from-1852/).

### General (Rogers, Ketchum & Grosvenor / Western & Atlantic, 1855)
- **Documenté** : masse 22,8 t (50 300 lb) ; masse adhérente 15 t ; pression
  de chaudière 140 psi ; cylindres de 15 in d'alésage ; roues motrices 60 in.
- **Estimé** :
  - effort de traction (31,8 kN) — la course des cylindres n'est publiée dans
    aucune des sources trouvées ; calculée avec la formule standard en
    supposant une course de 16 in, typique des 4-4-0 américains des années
    1850.
  - vitesse (64 km/h) — non documentée pour cette machine ; retenue par
    comparaison avec les vitesses typiques des « American » 4-4-0 de l'époque
    (≈40 mph).
- Source : [Wikipédia — The General (locomotive)](https://en.wikipedia.org/wiki/The_General_(locomotive)).

### Pennsylvania Railroad classe E6 Atlantic (1910)
- **Documenté** : effort de traction 32 000 lbf (142,3 kN) ; masse locomotive
  110,5 t (243 600 lb) ; vitesse « dépassant 100 mph » (retenue : 161 km/h).
- Source : [Wikipédia — Pennsylvania Railroad class E6](https://en.wikipedia.org/wiki/Pennsylvania_Railroad_class_E6).

### LNER classe A1 « Flying Scotsman » (1923)
- **Documenté** : effort de traction, telle que construite, 29 835 lbf
  (132,7 kN) ; masse 97,8 t ; le 30 novembre 1934, première locomotive à
  vapeur officiellement authentifiée à 100 mph (161 km/h).
- Source : [Wikipédia — LNER Class A3 4472 Flying Scotsman](https://en.wikipedia.org/wiki/LNER_Class_A3_4472_Flying_Scotsman) ;
  [Smithsonian Magazine — the speedometer hit 100](https://www.smithsonianmag.com/smart-news/flying-scotsman-made-train-history-when-speedometer-hit-100-180961257/).

### DRG classe 05 (1935)
- **Documenté** : record du monde vapeur 200,4 km/h (11 mai 1936, toujours
  invaincu) ; masse en service (unité 05 003) 124,0 t ; cylindres 3 ×
  (450 × 660 mm) ; roues motrices 2 300 mm ; pression de chaudière 20 bar.
- **Estimé** : effort de traction (148,3 kN) — non trouvé directement ;
  calculé par la formule standard à deux cylindres puis mise à l'échelle
  ×1,5 pour les trois cylindres de cette machine.
- Source : [Wikipédia — DRG Class 05](https://en.wikipedia.org/wiki/DRG_Class_05) ;
  [Wikipédia (de) — DR-Baureihe 05](https://de.wikipedia.org/wiki/DR-Baureihe_05).

### LNER classe A4 « Mallard » (1938)
- **Documenté** : effort de traction 35 455 lbf (157,7 kN) ; masse locomotive
  104,6 t ; record du monde vapeur 125,88 mph (202,58 km/h, arrondi à 203),
  toujours invaincu.
- Source : [Wikipédia — LNER Class A4 4468 Mallard](https://en.wikipedia.org/wiki/LNER_Class_A4_4468_Mallard) ;
  [Wikipédia — LNER Class A4](https://en.wikipedia.org/wiki/LNER_Class_A4).

### Union Pacific classe 4000 « Big Boy » (1941)
- **Documenté** : effort de traction visé 135 375 lbf (602,3 kN), effort
  mesuré au démarrage jusqu'à 138 200 lbf lors d'essais en 1943 ; masse
  locomotive seule 345,6 t (762 000 lb, tender exclu) ; vitesse maximale de
  conception 80 mph (129 km/h) — en service, ces machines roulaient
  généralement sous 60 mph.
- Source : [Wikipédia — Union Pacific Big Boy](https://en.wikipedia.org/wiki/Union_Pacific_Big_Boy).

### EMD F3 (1945, diesel-électrique)
- **Documenté** : puissance 1 500 ch ; effort de traction jusqu'à 55 000 lbf
  selon le rapport d'engrenage (244,6 kN, retenu) ; vitesse maximale selon
  rapport d'engrenage, de 50 mph (fret) à 102 mph (voyageurs) — 65 mph
  (104 km/h) retenu comme rapport généraliste représentatif, ni le plus
  rapide ni le plus tracteur.
- **Estimé** : consommation (7,0 kg/km) — aucune source directe trouvée ;
  dérivée d'un ordre de grandeur courant dans la littérature ferroviaire pour
  les locomotives diesel de 1 500 ch (≈3–4 gallons US/mile), converti en kg
  avec une densité gazole de 0,85 kg/L. Traitée comme une estimation, pas
  comme une donnée sourcée.
- Source : [Wikipédia — EMD F3](https://en.wikipedia.org/wiki/EMD_F3).

### Note de méthode — coût d'achat, entretien, consommation

Ces trois champs sont estimés pour **toutes** les locomotives, par les
formules suivantes, appliquées uniformément pour rester cohérentes entre
elles (une locomotive deux fois plus puissante coûte plus cher, pas au
hasard) :

```
consommation (kg/km)     = 0,15 × effort de traction (kN)      [locomotives à charbon]
coût d'achat              = 0,5  × effort de traction (kN) × vitesse (km/h),
                            plancher à 200
coût d'entretien (/km)    = 0,03 × effort de traction (kN)
```

La consommation du diesel (EMD F3) déroge à cette formule — voir sa fiche
ci-dessus — parce qu'un moteur diesel consomme beaucoup moins de carburant
par unité d'effort qu'une chaudière à charbon (rendement thermique très
supérieur) ; appliquer la même formule aurait donné un chiffre absurdement
élevé et **faux**, pas seulement approximatif.

Ces trois champs ne sont **jamais** présentés comme des données historiques
dans `data/locomotives.json` — voir `LocomotiveDef` dans
[`src/RailTycoon.Sim/Economy/LocomotiveDef.cs`](../src/RailTycoon.Sim/Economy/LocomotiveDef.cs),
dont la doc XML renvoie ici.

### Ce qui manque

- Aucune consommation de combustible par kilomètre n'a de source directe pour
  aucune des quinze machines : c'est la donnée la plus difficile à trouver
  publiée par locomotive précise (les sources documentent des *capacités* de
  soute, pas des débits). Toutes sont donc estimées.
- La pression de chaudière des cinq premières machines (Stourbridge Lion,
  Planet, John Bull, Fire Fly, General) n'est pas publiée dans les sources
  trouvées ; l'effort de traction qui en dépend est donc calculé sur une
  hypothèse (50 psi, ou 140 psi documentée pour le General), pas mesuré.
- La vitesse de Lafayette et du General n'a pas de source directe et est
  estimée par comparaison avec des machines contemporaines documentées.

<a id="masses-du-train"></a>
## Masses du train — modèle de coût `mass` (`haulage.massCost`)

Le modèle de coût `mass` (voir [FINDINGS.md](FINDINGS.md), « Le coût marginal
réel ») a besoin de trois masses : la locomotive avec son tender, la tare d'un
wagon, la charge d'un wagon — un chargement du jeu est un wagon. Le catalogue
`data/locomotives.json` ne porte pas de masse (les masses documentées plus haut
ne sont pas dans le fichier) ; les trois valeurs sont donc des paramètres du
scénario, et c'est ici qu'elles se justifient.

Recherches effectuées via WebSearch le 30 septembre 2026. Même limite que pour
les événements : les pages n'ont pas pu être ouvertes depuis l'environnement de
travail (search.library.wisc.edu est bloqué par le proxy sortant, les autres
n'ont été lues que par les extraits du moteur de recherche).

### Locomotive et tender — 48 t, documenté

- **Documenté** : Pennsylvania Railroad classe D5, 4-4-0 construites de 1870 à
  1873 — locomotive 65 200 lb (29,6 t), tender 40 800 lb (18,5 t), ensemble
  106 000 lb (**48,1 t**). Deux 4-4-0 contemporaines, de construction ou de
  dessin américains, encadrent la valeur : Victorian Railways classe H (1877),
  49,3 t avec tender ; classe D (1876, Rogers), 58,3 t.
- Le General (1855), au catalogue, pèse 22,8 t **sans** tender : l'ordre de
  grandeur de la locomotive seule d'une machine plus ancienne, cohérent.
- Sources : [Wikipédia — Pennsylvania Railroad class D5](https://en.wikipedia.org/wiki/Pennsylvania_Railroad_class_D5) ;
  [Wikipédia — Victorian Railways H class (1877)](https://en.wikipedia.org/wiki/Victorian_Railways_H_class_(1877)) ;
  [Wikipédia — Victorian Railways D class (1876)](https://en.wikipedia.org/wiki/Victorian_Railways_D_class_(1876)).

### Wagon — 9 t de charge, 9 t de tare, estimés

- **Estimé** : un wagon couvert américain des années 1870 porte 10 short tons
  (20 000 lb, **9,07 t**, retenu 9 t) et pèse à vide du même ordre (retenu
  **9 t**, rapport tare / charge de 1).
- Ce que les extraits donnent : une comparaison d'époque entre voie étroite et
  voie normale indique qu'un wagon couvert de voie normale « pesait environ vingt
  mille livres » (page non ouverte, extrait seulement) ; les wagons couverts de
  voie étroite du Denver & Rio Grande, 1878-1883, portaient 10 tonnes, et un
  relevé de marquage des années 1880 donne 20 000 lb de charge pour 8 350 lb de
  tare (rapport 0,42 — plus léger, en voie étroite, et de source secondaire).
  Aucune source trouvée ne documente directement la tare d'un wagon de voie
  normale de 1875.
- **C'est le paramètre qui compte** : le rapport tare / charge fixe la part
  marginale du coût, donc le rayon économique. À 4,5 t de tare, la scierie de
  Cedarton ne démarre pas même à relief faible ; à 18 t, elle survit au col à
  forte traction (FINDINGS, « Sensibilité aux masses »). Une meilleure source est
  la prochaine étape avant de s'y fier.
- Sources : [search.library.wisc.edu — comparaison voie étroite / voie normale](https://search.library.wisc.edu/digital/AJ4LAFP5BKIPLA8D/text/AFCTLVPW5EYVFQ8X) (extrait) ;
  [largescalecentral.com — wooden box car info](https://largescalecentral.com/t/wooden-box-car-info/73392) (D&RG, 1880s) ;
  J. H. White Jr., *The American Railroad Freight Car: From the Wood-Car Era to the
  Coming of Steel*, Johns Hopkins University Press, 1993 — la référence à consulter,
  non lue.

### Calibration — 0,89, mesurée, pas documentée

`calibrationLoadFactor` n'est pas une donnée historique : c'est la charge moyenne
que le modèle fait porter lui-même aux trains de heartland (89 % de la capacité,
solveur de référence, 40 réalisations). Elle fixe le prix de la tonne-kilomètre
pour que le coût facturé sur heartland reste celui du modèle `flat` ; le calcul est
dans FINDINGS.md.

## Second scénario — `data/ironpeak.json`

Aucune valeur de ce fichier n'est une donnée historique au sens strict : ce
sont des paramètres de conception, comme dans `heartland.json`. Deux points
méritent néanmoins d'être notés :

- Le rapport minerai → fonte (3 pour 1 en quantités de jeu) est *inspiré* du
  fait qu'il faut historiquement plus d'une tonne de minerai pour produire
  une tonne de fonte (la métallurgie du XIXe siècle tournait généralement
  autour de 1,6 à 2 tonnes de minerai par tonne de fonte, selon la teneur du
  minerai) — le chiffre 3 retenu ici est **calibré pour la marge de 70 %**
  exigée par la conception du jeu (voir docs/FINDINGS.md), pas mesuré sur un
  haut fourneau réel. Le présenter comme une reconstitution historique
  précise serait trompeur ; c'est un choix d'équilibrage inspiré par un ordre
  de grandeur réel.
- Les prix de référence (minerai 9, fonte 46, acier 78) sont calculés pour
  obtenir une marge par maillon comprise entre 1,69 et 1,70 (soit environ
  70 % de valeur ajoutée), exactement la règle appliquée dans
  `heartland.json` — voir le calcul détaillé dans le commentaire
  `//chaineacier` du fichier, et vérifié par le test « content — la chaîne
  acier ajoute environ 70 % de valeur à chaque maillon » dans
  `tests/RailTycoon.Tests/Program.cs`.
- La géographie (Ironpeak isolée à l'origine de la ligne) est une décision de
  conception documentée dans le commentaire `//geographie` du fichier, pas
  une carte réelle.

<a id="evenements"></a>
## Événements historiques — `data/heartland-events.json`

Recherches effectuées via WebSearch le 27 septembre 2026. **Limite à connaître** :
les pages elles-mêmes n'ont pas pu être ouvertes depuis l'environnement de
travail (Wikipédia, les revues de Penn State et les sites d'histoire locale sont
bloqués par le proxy sortant) ; ce qui est dit « documenté » ci-dessous l'est par
les extraits et résumés que le moteur de recherche renvoie de ces pages, avec
leurs adresses. C'est moins qu'une lecture complète, et une relecture humaine des
pages citées est la prochaine étape avant de s'y fier pour un texte de jeu.

Le principe est celui des locomotives, transposé. Ce qu'un événement a de
**factuel** — sa date, sa nature, son ampleur physique — est documenté quand une
source le permet. Ce qu'il a d'**économique dans le jeu** — son multiplicateur,
sa durée d'effet, sa montée — n'est *jamais* une donnée historique : convertir la
ruine d'une filière de 1871 en « ×0,4 sur la production de charbon » est un choix
de conception, exactement comme convertir un prix de 1855 en unité de jeu. Et sa
**cible** est une transposition : la carte de heartland est fictive, Kingsport
n'est pas Chicago, Coalburg n'est pas la vallée de Schuylkill.

D'où deux natures, que le chargeur fait respecter :

- `"basis": "historical"` exige une `source` qui renvoie ici ; la date et la nature
  de l'événement sont documentées.
- `"basis": "inspired"` dit qu'on s'inspire d'un fait réel sans pouvoir en
  documenter l'effet que le jeu lui prête. Le harnais l'affiche « inspiré de »,
  jamais « historique ».

Calendrier : l'année de jeu compte douze mois de trente jours, et le tick 0 est le
1er janvier de `events.startYear` (1870 pour heartland). Le 8 octobre 1871 est donc
le tick 360 + 9 × 30 + 7 = 637.

<a id="greve-anthracite-1871"></a>
### Grève de l'anthracite, janvier–juin 1871 — `historical`

- **Documenté** : la *Workingmen's Benevolent Association* (WBA), premier syndicat
  des mineurs d'anthracite de Pennsylvanie, a appelé à la grève en 1868, 1869 et
  1871. En 1871, les syndicats de la région de Lehigh sont sortis **début
  janvier** ; la grève a été tranchée par un arbitre, le juge William Elwell, dont
  la décision est tombée le **14 mai 1871** ; le dernier syndicat a repris le
  travail le **21 juin 1871**, avec une baisse de salaire de 10 %, chaque comté
  ayant été autorisé à négocier ses propres conditions de reprise. Deux grévistes
  ont été tués à Scranton par les gardes des compagnies.
- **Estimé / choisi** : le jour de début (10 janvier — « début janvier » est tout
  ce que les extraits donnent) ; la montée de 20 jours, qui traduit une grève qui
  s'étend de comté en comté et une reprise échelonnée ; le multiplicateur ×0,4 sur
  la production de charbon de Coalburg, choisi pour une grève réelle mais non
  totale — aucune source trouvée ne chiffre le tonnage perdu.
- Sources : Harold W. Aurand, « Early Mine Workers' Organizations in the
  Anthracite Region », *Pennsylvania History*
  ([journals.psu.edu](https://journals.psu.edu/phj/article/download/24915/24684)) ;
  H. W. Aurand, *From the Molly Maguires to the United Mine Workers*, chap. 8
  « The Collapse of the W.B.A. »
  ([Temple University Press](https://temple.manifoldapp.org/read/from-the-molly-maguires-to-the-united-mine-workers-the-social-ecology-of-an-industrial-union-1869-1897/section/9a01e5d1-b85c-44f2-a9cd-ae7597fe6c69)) ;
  [Wikipédia — Workingmen's Benevolent Association of Schuylkill County](https://en.wikipedia.org/wiki/Workingmen%27s_Benevolent_Association_of_Schuylkill_County).

<a id="secheresse-1871"></a>
### Sécheresse du Middle West, été 1871 — `inspired`

- **Documenté** : l'été 1871 a été particulièrement sec dans le nord du Middle
  West ; 1870 et 1871 ont toutes deux été des années très sèches, et de petits
  feux brûlaient dans la région depuis la fin août. C'est la toile de fond des
  incendies d'octobre (voir Peshtigo).
- **Non documenté, d'où la nature « inspiré de »** : l'effet de cette sécheresse
  sur les récoltes de blé. Aucune source trouvée ne chiffre une baisse de
  rendement ; les dates (1er juin – 30 septembre), la montée de 30 jours et le
  multiplicateur ×0,75 sur le blé de Fairview et de Weston sont des choix.
- Sources : [NWS Green Bay — The Peshtigo Fire](https://www.weather.gov/grb/peshtigofire) ;
  [Wikipédia — Peshtigo fire](https://en.wikipedia.org/wiki/Peshtigo_fire).

<a id="grand-incendie-1871"></a>
### Grand incendie de Chicago, 8–10 octobre 1871 — `historical`

- **Documenté** : parti le soir du 8 octobre 1871 près de la grange des O'Leary,
  l'incendie a tué environ 300 personnes, détruit quelque 17 000 bâtiments sur
  environ 3,3 miles carrés et laissé plus de 100 000 habitants sans abri. Le bois
  était partout — bâtiments, trottoirs, chaussées de pin. La reconstruction a
  commencé dès les décombres dégagés (la « Great Rebuilding ») ; les parcs à bois
  et les abattoirs, hors de la zone brûlée, ont continué de tourner.
- **Estimé / choisi** : la traduction en demande de **planches** à Kingsport — la
  plus grande ville de la carte, un port —, ×2,5 sur 300 jours avec 30 jours de
  montée pour une reconstruction qui démarre après le déblaiement. Aucune source
  trouvée ne chiffre la demande de bois de construction de la reconstruction.
- Sources : [National Geographic Education — The Chicago Fire of 1871 and the
  "Great Rebuilding"](https://education.nationalgeographic.org/resource/chicago-fire-1871-and-great-rebuilding/) ;
  [Chicago Architecture Center — The Great Chicago Fire of 1871](https://www.architecture.org/online-resources/architecture-encyclopedia/the-great-chicago-fire-of-1871) ;
  [Wikipédia — Great Chicago Fire](https://en.wikipedia.org/wiki/Great_Chicago_Fire).

<a id="incendie-peshtigo-1871"></a>
### Incendie de Peshtigo, 8 octobre 1871 — `historical`

- **Documenté** : le même jour que Chicago, un incendie de forêt a ravagé le
  nord-est du Wisconsin et une partie de la péninsule supérieure du Michigan :
  environ 1,2 million d'acres (490 000 ha ; certaines sources disent 1,5 million),
  entre 1 200 et 2 500 morts — l'incendie de forêt le plus meurtrier de l'histoire
  des États-Unis. Peshtigo était une ville-scierie appartenant à William Ogden,
  siège de l'une des plus grandes usines de produits du bois du pays.
- **Estimé / choisi** : l'effet sur les **grumes** de Pinegrove, la seule forêt de
  la carte, ×0,5 pendant un an, avec 5 jours de montée — un incendie est soudain.
- Sources : [Wikipédia — Peshtigo fire](https://en.wikipedia.org/wiki/Peshtigo_fire) ;
  [NWS Green Bay — The Peshtigo Fire](https://www.weather.gov/grb/peshtigofire) ;
  [University of Illinois LibGuides — The Peshtigo Fire of 1871](https://guides.library.illinois.edu/historical_wildfires/peshtigo).

<a id="panique-1873"></a>
### Panique de 1873, à partir du 18 septembre 1873 — `historical`

- **Documenté** : la panique commence le 18 septembre 1873 avec la suspension de
  la banque Jay Cooke & Co., agent du gouvernement pour le financement des
  chemins de fer et très exposée au Northern Pacific ; la Bourse de New York ferme
  le 20 septembre pour dix jours, une première. En deux ans, 89 des 364 compagnies
  ferroviaires font faillite et 18 000 entreprises disparaissent ; le chômage
  atteint 14 % en 1876. La crise ouvre une dépression d'environ cinq ans, la
  « Long Depression ».
- **Estimé / choisi** : la traduction en baisse de la demande de **planches**
  (×0,6 — le bâtiment et les chantiers ferroviaires s'arrêtent) et de **charbon**
  (×0,85 — l'industrie ralentit) dans toutes les villes, pendant cinq ans, avec
  60 jours de montée. Aucune source trouvée ne chiffre ces demandes.
- Hors des 720 ticks mesurés : elle tombe au tick 1 337. Elle agit dans une partie
  de plus de trois ans et demi.
- Sources : [Library of Congress — The Panic of 1873](https://guides.loc.gov/this-month-in-business-history/september/panic-of-1873) ;
  [PBS American Experience — The Panic of 1873](https://www.pbs.org/wgbh/americanexperience/features/grant-panic/) ;
  [Yale Program on Financial Stability — Crisis Chronicles: The Long Depression and the Panic of 1873](https://elischolar.library.yale.edu/cgi/viewcontent.cgi?article=13743&context=ypfs-documents) ;
  [Wikipédia — Panic of 1873](https://en.wikipedia.org/wiki/Panic_of_1873).

### Ce qui manque

- Aucun des multiplicateurs n'a de source : ni le tonnage perdu par la grève de
  1871, ni la demande de bois de la reconstruction de Chicago, ni la chute de la
  construction après 1873. Ce sont des choix de conception, au même titre que le
  coût d'achat d'une locomotive.
- Les pages citées n'ont été lues qu'à travers les extraits du moteur de recherche
  (voir la limite en tête de section).
- Le jour de début de la grève de 1871 n'est pas documenté au-delà de « début
  janvier ».
- `ironpeak.json` (1900) ne déclare pas d'événements. Un candidat pour un futur
  scénario d'épreuve est une grève de l'anthracite à l'automne 1900 ; elle **n'a
  pas été vérifiée** ici et ne doit pas être déclarée historique avant de l'être.

Les événements **aléatoires** du catalogue (vague de froid, redoux, mauvaise
récolte, récolte abondante, afflux d'ouvriers, épidémie, fièvre de construction,
marasme du bâtiment, éboulement, nouveau filon) ne prétendent à aucune source :
ce sont des types génériques, dont les fréquences et les intensités sont justifiées
par la mesure — voir [FINDINGS.md](FINDINGS.md) et les clés `"//…"` du scénario.

<a id="cycle-economique"></a>
## Cycle économique — `data/heartland-cycle.json`

Le module `cycle` ne déclare aucun événement : il tire ses phases au sort. Deux
choses seulement y prétendent à une source — les **dates** qui calent la partie de
1870, et l'**ordre de grandeur** des durées de phase.

- **Documenté** : le National Bureau of Economic Research date les cycles
  américains depuis 1854. Il place un pic en juin 1869, un creux en décembre 1870,
  un pic en octobre 1873 et un creux en mars 1879 — la contraction de 1873-1879,
  65 mois, est la plus longue de sa chronologie. Sur 1854-1919, ses moyennes sont
  d'environ 22 mois de contraction et 27 mois d'expansion, soit un cycle d'environ
  quatre ans.
- **Estimé / choisi** : l'ouverture en **crise** (lecture : la fin de la contraction
  de 1869-1870, dont le creux tombe au tick 330, dans les bornes de la crise) ; le
  découpage en quatre phases, où reprise + expansion valent l'expansion du NBER
  (810 jours en moyenne) et ralentissement + crise sa contraction (600 jours) ; les
  bornes de chaque phase ; tous les effets — taux, multiple, demande,
  investisseurs. Ce sont des paramètres de jeu, jamais des mesures, comme les
  multiplicateurs des événements. La contraction de 1873-1879 n'est pas reproduite :
  la crise que force la panique dure 270 à 630 jours, pas 65 mois.
- **Couplage** : la panique de 1873 est la seule bascule historique ; elle est
  sourcée plus haut ([Panique de 1873](#panique-1873)). Les poussées des récoltes et
  du bâtiment sont un choix : l'idée que les récoltes menaient le cycle américain du
  XIXe siècle est une lecture courante de l'histoire économique, pas une mesure
  chiffrée ici.
- Source : [NBER — US Business Cycle Expansions and Contractions](https://www.nber.org/research/data/us-business-cycle-expansions-and-contractions).
- **Limite** : les dates et les moyennes ci-dessus viennent de la connaissance de la
  chronologie du NBER ; la page n'a pas pu être relue pendant ce chantier (accès
  réseau bloqué). Les moyennes sont données à un mois près, et doivent être
  revérifiées avant d'être citées comme exactes.
