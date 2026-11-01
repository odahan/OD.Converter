# Gestionnaire de modules facultatifs — lot 3

Le gestionnaire dispose de deux recettes d'installation réelles et indépendantes :
**Demucs 4.0.1 avec PyTorch CPU et htdemucs**, et **whisper.cpp 1.8.2 CPU avec
large-v3-turbo-q5_0 multilingue et Silero VAD v5.1.2**. `install` et `repair` téléchargent uniquement les
artefacts aux versions, tailles et SHA-256 fixés dans l'application. Chaque module
est activé après vérification des fichiers et une courte inférence réelle hors
ligne. Les commandes de traitement `separate` et `transcribe` sont raccordées aux
lots 4 et 5. La recette Whisper r2 ajoute le modèle VAD ; une génération r1 doit
être mise à niveau par une installation/réparation explicitement demandée.
Les [distributions et licences](distributions-modules.md) détaillent les choix.

## Commandes disponibles

```powershell
odconv modules list
odconv modules status separation
odconv modules verify transcription
odconv modules install separation
odconv modules repair transcription
odconv modules uninstall separation
```

`list` et `status` retournent 0 si l'inspection s'est déroulée correctement,
quel que soit l'état affiché. `verify` retourne 0 seulement pour `Ready`, sinon 2.
Les arguments incorrects retournent 1, les échecs d'opération 3 et l'annulation
130. Une recette non disponible retourne 2 sans modifier l'installation existante.
`uninstall` est idempotent et fonctionne sans recette d'installation.

Avant un téléchargement explicite, la commande affiche le module, sa version,
son emplacement, le profil CPU et le volume exact des transferts. Le programme
n'ajoute pas de confirmation interactive à ces commandes CLI.

Une inspection d'un module absent ne crée aucun dossier. Aucun de ces services
n'est construit dans le parcours de conversion ou d'extraction. Aucune découverte
de Python, de Demucs ou de Whisper sur le PATH n'est effectuée.

## Stockage et activation

```text
%LOCALAPPDATA%/OD.Converter/
  modules/
    separation.lock
    separation/
      active.json
      attempt.json                 # tentative interrompue ou échouée, le cas échéant
      versions/<generation>/
        runtime/
        models/
        licenses/
        module.json
      cache/                       # cache propre à ce module
    transcription.lock
    transcription/
      ...
```

Le root injectable est réservé aux tests et à l'intégration du service ; il n'y a
pas de nouvelle option CLI portable. L'emplacement par défaut est dans le compte
utilisateur, indépendamment du dossier de l'EXE. Aucun installateur système,
registre ou PATH persistant n'est modifié.

Chaque module possède un verrou exclusif de fichier interprocessus. Le fichier
vide reste présent après libération ; son existence ne signifie pas `Installing`.
L'OS libère le verrou si le processus se termine brutalement. Les opérations sur
l'autre module restent possibles.

L'installation prépare une nouvelle génération dans son **chemin définitif** :
un runtime contenant des chemins absolus ne sera donc pas déplacé après contrôle.
Les transferts HTTPS sont bornés par leur taille attendue et vérifiés avec un
SHA-256 fixé dans le code de la recette. Le gestionnaire recontrôle ces valeurs
avant extraction, même si le téléchargeur a été remplacé par un autre adaptateur.
La progression indique les octets reçus, puis les étapes sans pourcentage fictif.

Les ZIP ne peuvent sortir du dossier de génération, contenir de lien symbolique,
écraser une entrée précédente ni dépasser 100 000 entrées / 12 Gio décompressés.
Les chemins absolus, traversées, flux NTFS et points de réanalyse sont rejetés.
Avant suppression récursive, le chemin absolu et les descendants sont contrôlés,
sans suivre les jonctions vers un autre logiciel.

Après configuration, les fichiers obligatoires sont contrôlés puis l'adaptateur
exécute son test fonctionnel hors ligne. Un manifeste JSON versionné contient
l'identifiant du module, la version de la recette, le profil CPU et l'inventaire
SHA-256 des fichiers immuables. Le paquet ne peut fournir son propre `module.json`.
L'index d'intégrité traite au plus quatre fichiers à la fois avec des lectures
asynchrones bornées ; le contrôle des descendants ne revisite pas leurs ancêtres
à chaque fichier. L'annulation est vérifiée pendant le parcours et le hachage.
L'activation remplace seulement `active.json`, par déplacement dans le même dossier,
après réussite de tous les contrôles. L'ancienne génération n'est supprimée
qu'après activation réussie. Ce mécanisme vise les interruptions de processus ;
aucune garantie de résistance à une panne électrique du système de fichiers n'est
revendiquée.

Un échec conserve l'ancienne génération active et un marqueur de tentative.
La nouvelle génération et les téléchargements partiels sont nettoyés ; les
échecs de nettoyage sont signalés avec le chemin. Après arrêt brutal, la
réparation nettoie les générations abandonnées et recommence avec les artefacts
fixés, sans faire confiance aux anciens fichiers. Une activation JSON corrompue
est également récupérable par réparation. La désinstallation supprime uniquement
le dossier du module, y compris son cache ; le verrou reste pour éviter une course
entre processus.

## États et vérification

| État affiché | Critère |
|---|---|
| `Not installed` | Aucun dossier de module |
| `Installing` | Une opération possède le verrou exclusif du module |
| `Ready` | Recette reconnue, manifeste compatible, fichiers requis présents, inventaire complet vérifié et test fonctionnel réussi |
| `Needs repair` | Installation incomplète, manifeste invalide, recette inconnue, fichier manquant/modifié/ajouté ou échec fonctionnel |

Une tentative de réparation ratée ne rend pas indisponible une génération encore
valide : elle reste `Ready` avec un avertissement. Les manifestes locaux détectent
la corruption ; ils ne sont pas une signature contre un utilisateur capable de
modifier à la fois les fichiers et leurs empreintes. Les téléchargements, eux,
sont comparés aux empreintes de la recette compilée dans l'application.

## Contrat des prochains adaptateurs

`IModuleRecipe` fixe l'identifiant, la version, tous les téléchargements et leurs
tailles/SHA-256, les fichiers requis et deux opérations :

1. `ProvisionAsync` configure uniquement le payload privé. Tous les composants
   doivent provenir des artefacts fixés ; aucun `pip install` avec résolution
   réseau ouverte ou téléchargement implicite de modèle n'est acceptable.
2. `CheckAsync` vérifie runtime, imports/dépendances, chargement de modèle et courte
   exécution hors ligne. Un simple `--help` ne suffit pas. Les runtime, modèle et
   licences doivent figurer dans les fichiers requis.

`ModuleProcessRunner` reçoit un exécutable relatif au payload, résout son chemin
absolu et transmet chaque argument séparément, sans shell. Il vide l'environnement
hérité, conserve les variables système Windows nécessaires et configure les
caches, répertoires utilisateur et temporaires dans le cache du module.
`PYTHONPATH` et `PYTHONHOME` externes sont exclus, le site utilisateur Python est
désactivé et les bytecodes ne sont pas écrits dans le payload. Les appels Python
utilisent explicitement `-I -B` : `-B` reste nécessaire, car `-I` ignore les
variables `PYTHON*` de l'environnement. Les sorties sont
drainées sans accumulation illimitée ; le diagnostic conserve au plus 8 192
caractères. Un délai maximal et l'annulation arrêtent l'arbre des processus.

Le profil initial est **CPU uniquement** ; il masque CUDA dans l'environnement.
L'adaptateur doit aussi demander explicitement le CPU à son moteur. Une variante
GPU nécessitera une recette distincte testée, avec dépendances, empreintes,
volume et compatibilité déclarés ; aucun téléchargement GPU automatique n'est prévu.

## Validation et suite

Les dépendances CPU sont figées, les licences et attributions sont conservées,
et les bibliothèques Visual C++/OpenMP sont déployées localement depuis l'archive
officielle Microsoft. Le runtime d'OD.VideoDub n'est ni appelé ni modifié.
Whisper ne dépend pas de Python. Les recettes ne requièrent aucun compilateur
natif, runtime IA global ou GPU NVIDIA.

Les tests unitaires couvrent le cycle de vie avec fixtures et les contrats des
recettes, sans effectuer de téléchargement. Un outil distinct réalise les essais
avec les moteurs et poids réels dans un emplacement explicite : voir
[la procédure reproductible](distributions-modules.md#reproduire-la-validation-des-installations).
Les preuves et limites de chaque exécution figurent dans le
[suivi du lot 3](suivi-audio-video.md#lot-3--gestionnaire-de-modules-facultatifs).

Les traitements sur des médias réels, leur alignement et la qualité des sorties
appartiennent aux lots 4 et 5. La validation sur Windows propre et l'audit de
maintenance des distributions restent des contrôles de livraison du lot 7.
