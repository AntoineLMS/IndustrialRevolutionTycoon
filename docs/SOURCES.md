# Sources — module `content`

Ce document couvre les deux livrables du module `content` :
[`data/locomotives.json`](../data/locomotives.json) (catalogue historique) et
[`data/ironpeak.json`](../data/ironpeak.json) (second scénario, chaîne acier).

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
