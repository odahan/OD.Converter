# Suivi de l'extension audio et vidéo

Référence : [spécifications fonctionnelles et techniques](specifications-audio-video.md).

Dernière mise à jour : 1er novembre 2026.

## Point de départ

- Périmètre fonctionnel validé dans la discussion, formalisé dans les spécifications.
- État initial au cadrage : aucune implémentation de cette extension déclarée terminée. Les lots 1 à 3 sont désormais réalisés, avec recettes réelles et cycle de vie des modules validés. Les lots 4 et 5 sont implémentés et mesurés avec les vrais modèles CPU ; les écoutes/relectures de l'épisode 17 sont acceptées, les validations sur d'autres médias sont reportées. Le lot 6 est réalisé et vérifié automatiquement. Le lot 7 est **clôturé sur décision utilisateur le 1er novembre 2026**, avec EXE autonome publié et mesuré localement ; les validations Windows vierge et autres cas représentatifs sont explicitement reportées, sans échéance. Détails, preuves et reports ci-dessous.
- Existant observé : FFmpeg/FFprobe embarqués ; conversion avec vidéo désactivée en sortie ; analyse de la première piste audio ; pas de gestionnaire de modules ni de menu principal de traitements.
- Deux fichiers comportent déjà des modifications dans l'espace de travail : `ConversionOptionsValidator.cs` et `FfmpegConverter.cs`. Les préserver et examiner leur contenu avant toute modification ; ce suivi ne les attribue pas à l'extension.

## Tableau de pilotage

États : **À faire**, **En cours**, **À vérifier**, **Terminé**, **Bloqué**. Cocher un élément seulement après réalisation et vérification. Un lot est terminé quand ses critères d'acceptation sont couverts et ses preuves consignées.

Exception de clôture décidée par l'utilisateur le 1er novembre 2026 : le lot 7
est terminé sur le périmètre validé, avec les contrôles restants explicitement
reportés dans la section dédiée. Ces reports ne constituent pas des tests réussis.

| Lot | Livrable | Dépendances | État |
|---|---|---|---|
| 1 | Contrats communs, commandes et analyse des sources | Aucune | Terminé |
| 2 | Préparation audio et contrat temporel | 1 | Terminé |
| 3 | Gestionnaire de modules isolés | 1 | Terminé |
| 4 | Installation Demucs et séparation | 2, 3 | À vérifier — validations complémentaires reportées |
| 5 | Installation Whisper et transcription globale | 2, 3 | À vérifier — validations complémentaires reportées |
| 6 | Menu commun, disponibilité et orchestration complète | 1 à 5 | Terminé |
| 7 | Validation de bout en bout, documentation et publication | 1 à 6 | Terminé — clôture avec validations reportées sur décision utilisateur |

Le lot 2 et le lot 3 sont indépendants après le lot 1. Les lots 4 et 5 sont indépendants une fois leurs prérequis satisfaits. Ces dépendances n'imposent aucune délégation ni exécution parallèle.

## Lot 1 — Contrats et analyse des sources

### Travail

- [x] Définir les demandes de traitement et les résultats communs audio/vidéo.
- [x] Étendre l'analyse à toutes les pistes audio avec leurs index, langues, titres et caractéristiques.
- [x] Définir l'origine de présentation, les offsets, la durée cible et leur fiabilité.
- [x] Figer la syntaxe CLI des actions et de la gestion des modules, y compris aide et codes de sortie.
- [x] Définir la sélection de piste et les conventions de noms des nouveaux exports.
- [x] Préserver les commandes et le lancement direct d'un fichier existants.

### Acceptation

- [x] Une source audio et une source vidéo alimentent les mêmes contrats de traitement.
- [x] Plusieurs pistes sont identifiées sans confusion entre index absolu et index audio.
- [x] Une vidéo muette est rejetée clairement avant traitement.
- [x] Durée inconnue et timestamps non nuls/négatifs sont représentés sans hypothèse silencieuse.

Preuves / résultats (2026-11-01) :

- Contrats ajoutés : `MediaSource`, `AudioTrackInfo`, `PresentationTimeline`, `ProcessingRequest`, `ProcessingPlan`, `ProcessingOutput` et `ProcessingResult` ; conventions d'exports partagées dans `ProcessingConventions`.
- Analyse FFprobe de toutes les pistes ; index absolu et ordinal audio distincts, langue/titre et caractéristiques, exclusion des images de couverture de la chronologie vidéo. `probe --json` conserve le JSON FFprobe brut.
- Origine, durée et offset nullables ; timestamps signés conservés ; fiabilité conteneur/pistes/inconnue/incohérente et avertissements. Une origine vidéo inconnue n'est pas remplacée par le début d'une piste audio retardée. Une durée déduite exige tous les endpoints de référence. Les métadonnées ne constituent pas une validation de l'alignement.
- Syntaxe figée et documentée dans [le contrat CLI](cli-audio-video.md), avec aide exécutable `process --help` et `modules --help`. Les commandes réservent l'interface des futurs lots : `process` valide et analyse mais retourne 2 tant que l'exécution n'est pas implémentée ; `modules` retourne 2 sans installation ni modification. Ce lot ne prétend pas livrer les traitements ni le gestionnaire de modules.
- `dotnet build OD.Converter.slnx -c Release -m:1` : réussi, 0 avertissement, 0 erreur.
- `dotnet test OD.Converter.slnx -c Release -m:1` : 58 tests réussis, 0 échec, 0 ignoré. Cas simulés : audio/vidéo, pistes multiples, origine négative/non nulle/inconnue, durée inconnue/incohérente, piste retardée, vidéo muette, sélection, noms et arguments, annulation. L'exécution VSTest dans le bac à sable rencontrait une erreur socket 10013 ; relance hors de cette restriction réussie.
- Essais réels avec les FFmpeg/FFprobe embarqués et le binaire Release : MP4 synthétique avec vidéo index 0 et pistes audio index 1/2 (48 kHz/44,1 kHz, langues fra/eng), sélection explicite de l'index 2, rejet de l'index vidéo 0 (code 1), rejet d'une vidéo muette avant traitement (code 1), lecture JSON d'un WAV, conversion WAV→MP3 et aides CLI réussies. Médias de contrôle locaux dans `out/lot1-validation` (ignoré par Git).
- Les commandes historiques restent disponibles ; le branchement de lancement direct vers le parcours interactif est conservé. Aucun essai manuel complet du menu interactif n'est revendiqué. Les modifications préexistantes de `ConversionOptionsValidator.cs` et `FfmpegConverter.cs` sont inchangées.
- Prochaine étape séquentielle : lot 2, préparation PCM et validation mesurée de l'alignement ; le lot 3 est également débloqué. Aucune mesure d'alignement PCM, séparation réelle, transcription réelle ou installation de module n'a été effectuée à ce stade.

## Lot 2 — Préparation audio et alignement

### Travail

- [x] Extraire la piste explicitement sélectionnée avec FFmpeg.
- [x] Préparer un PCM sans perte pour séparation et un PCM adapté à Whisper.
- [x] Préserver les silences, offsets et discontinuités par rapport à l'origine de présentation.
- [x] Centraliser la mesure et la normalisation des longueurs PCM après traitement.
- [x] Prévoir la conversion des horodatages des moteurs vers la chronologie source.
- [x] Gérer les dossiers temporaires, annulations et sorties partielles.

### Acceptation

- [x] Extraction d'un MP4 sans Demucs ni Whisper et sans réseau.
- [x] Cas audio WAV et MP3, stéréo et mono, avec fréquences différentes.
- [x] Audio retardé dans une vidéo, audio plus court que l'image et silences de début/fin conservés.
- [x] Repères au début, au milieu et en fin sans dérive ; longueur PCM conforme à la durée cible à l'arrondi d'un échantillon près.
- [x] Conversion simple inchangée lorsque aucune fonction avancée n'est demandée.

Preuves / résultats (2026-11-01) :

- `AudioPreparationService` extrait par index absolu, conserve les timestamps avec `-copyts`, soustrait l'origine de présentation puis remplit/coupe les discontinuités avec `aresample` sans compensation de vitesse. La préparation conserve les canaux et la fréquence source en PCM float32, sans codec intermédiaire avec perte. Ce choix de précision flottante n'est pas une promesse d'identité binaire avec tous les formats sources.
- Préparations WAV float32 pour séparation et WAV PCM signé 16 bits, mono 16 kHz pour Whisper ; aucun moteur IA installé ou exécuté. `PcmLength` mesure les frames à partir des octets, rejette les sorties vides/incomplètes et normalise uniquement la fin à `arrondi(durée × fréquence, AwayFromZero)`. Les changements de fréquence d'export sont aussi mesurés. `EngineTimeline` fournit le décalage de segment et la compensation unique d'un délai moteur mesuré ; les vrais délais moteurs restent à vérifier aux lots 4 et 5.
- `process --action extract` exécutable hors ligne, options d'encodage et noms du lot 1 conservés. Précontrôle de collision et protection du chemin source ; export temporaire dans le dossier de destination, finalisé seulement après succès. Un échec/une annulation conserve l'ancienne destination et nettoie les fichiers partiels. Dossiers de travail uniques sous `%TEMP%/OD.Converter`, supprimés dans tous les cas ; avertissement avec chemin si le nettoyage échoue.
- Politique explicite : origine/durée inconnue ou incohérente rejetée (code 3), sans substituer la durée audio à celle d'une vidéo. Exception annoncée pour le WAV PCM sans timestamps : l'échantillon zéro définit son origine. Pas de reconstruction automatique des endpoints vidéo inconnus par décodage intégral. Voir [la politique de préparation](preparation-audio.md).
- `dotnet build OD.Converter.slnx -c Release -m:1` : réussi, 0 avertissement, 0 erreur. `dotnet test OD.Converter.slnx -c Release -m:1 --logger "trx;LogFileName=lot2-tests.trx" --results-directory out/lot2-validation/test-results` : **76 tests réussis**, 0 échec, 0 ignoré. Premier essai VSTest restreint abandonné sur délai de connexion au testhost ; les exécutions hors de cette restriction ont réussi. Rapport local ignoré par Git : `out/lot2-validation/test-results/lot2-tests.trx`.
- Tests FFmpeg réels automatisés : WAV/MP3 mono et stéréo à 22,05/44,1/48 kHz, préparations des deux moteurs, MP4/MKV multipistes avec sélection de l'index absolu 2, retard de 500 ms, piste plus courte que l'image, discontinuité interne MKV et silences conservés ; MPEG-TS avec origine 3 s ; paquets négatifs MKV avec référence de présentation explicitement fournie par le test. Ce dernier contrôle la préparation/coupe avant origine, pas l'inférence automatique de la chronologie du conteneur négatif.
- Repères PCM MP4 et WAV/rééchantillonnage à un échantillon près ; WAV d'une minute rééchantillonné de 44,1 vers 48 kHz, repères à 0,25/30/59,75 s et exactement 2 880 000 frames. Les fixtures MKV ont une résolution de timestamp d'une milliseconde : tolérance de position de 48 échantillons à 48 kHz ; MPEG-TS/MP2 : 2 échantillons ; MP3 : 10 ms pour la réponse impulsionnelle avec perte. Toutes les longueurs PCM sont exactes ; ces tolérances de repères ne modifient pas ce critère.
- Essais du binaire Release : MP4 ALAC retardé, WAV 24 bits de 240 000 frames, repères mesurés exactement à 36 000/96 000/156 000 ; seconde piste du MP4 du lot 1 extraite à 44,1 kHz ; WAV PCM sans timestamps traité avec annonce de sa référence ; régression de `convert` WAV→MP3 et aide `process --help` vérifiées. Médias de contrôle dans `out/lot2-validation` (ignoré par Git). Tests d'échec d'encodage et d'annulation simulés : ancienne sortie conservée, fichiers temporaires supprimés.
- Les fichiers préexistants `ConversionOptionsValidator.cs` et `FfmpegConverter.cs` n'ont pas été modifiés par ce lot. Aucun essai Demucs/Whisper, délai IA, média de plusieurs heures, menu complet ou publication sur machine propre n'est revendiqué. Prochaine étape séquentielle : **lot 3, gestionnaire de modules isolés**.

## Lot 3 — Gestionnaire de modules facultatifs

### Travail

- [x] Créer l'espace privé OD.Converter et les manifestes de version.
- [x] Implémenter les états absent, installation, prêt et à réparer.
- [x] Ajouter téléchargement avec progression, validation d'intégrité et activation après contrôle fonctionnel.
- [x] Ajouter verrouillage d'installation, annulation, réparation et désinstallation ciblée.
- [x] Choisir et documenter les distributions et versions compatibles ainsi que leurs licences.
- [x] Prévoir une configuration CPU et une stratégie d'accélération compatible.
- [x] Éviter toute modification du PATH global ou utilisation d'un runtime externe implicite.

### Acceptation

- [x] Une installation dans un compte standard fonctionne sans droits administrateur.
- [x] Une interruption ou une panne réseau laisse un état réparable.
- [x] Un module endommagé n'affecte ni l'autre module ni la conversion classique.
- [x] Aucun téléchargement au démarrage ou pendant une conversion/extraction simple.
- [x] Désinstaller un module conserve les sources, les exports et l'autre module.
- [x] Aucun fichier de runtime, poids ou cache créé dans l'installation d'un autre logiciel.

Preuves / résultats (2026-11-01) :

- Socle partagé `ModuleManager`, `ModulePaths`, `ModuleDownloader`, `ModuleProcessRunner` et contrat `IModuleRecipe`. Stockage sous `%LOCALAPPDATA%/OD.Converter/modules/<module>` ; générations immuables et activation par remplacement d'un pointeur JSON après validation. Les tests créent uniquement des espaces temporaires dédiés.
- Téléchargements HTTPS bornés par taille et SHA-256 fixés, progression en octets, seconde vérification par le gestionnaire, ZIP contrôlés, refus des traversées/liens/jonctions. Inventaire complet versionné et contrôle fonctionnel obligatoires pour `Ready`. Un manifeste local est un contrôle de corruption, pas une signature anti-altération par le propriétaire du compte.
- Verrou exclusif interprocessus par module ; réparation depuis les artefacts fixés, récupération après interruption ou pointeur corrompu, préservation d'une génération active valide si la nouvelle tentative échoue. Nettoyage des fichiers partiels ; désinstallation idempotente limitée au module et à son cache, refus des jonctions vers un autre dossier.
- Processus appelés par chemin privé avec arguments séparés, environnement enfant reconstruit, caches et temporaires privés, sites Python externes exclus, profil CPU et CUDA masqué. Accélération future uniquement par variante explicitement validée, sans téléchargement automatique. Délai maximal, annulation et arrêt de l'arbre des processus prévus.
- CLI branchée : `list`/`status` lisent les états (code 0), `verify` exige `Ready` (sinon 2), `uninstall` cible le module. `install`/`repair` utilisent maintenant les **deux recettes réelles enregistrées** : Demucs 4.0.1 / CPython privé 3.11.9 / PyTorch et torchaudio 2.6.0+cpu / htdemucs, et whisper.cpp 1.8.2 CPU / large-v3-turbo-q5_0 multilingue, sans Python pour Whisper. Catalogue HTTPS embarqué avec tailles, empreintes, dépendances complètes et licences ; environ 921 Mo pour les deux modules. DLL Visual C++ et OpenMP déployées localement depuis une archive Microsoft vérifiée, sans installateur système.
- `dotnet build OD.Converter.slnx -c Release -m:1 --no-restore` : réussi, 0 avertissement, 0 erreur, outil de validation inclus dans la solution. `dotnet test OD.Converter.slnx -c Release -m:1 --no-restore --logger "trx;LogFileName=lot3-recipes-tests.trx" --results-directory out/lot3-validation/test-results` : **106 tests réussis**, 0 échec, 0 ignoré. Exécution VSTest hors restriction du bac à sable. Rapports locaux ignorés par Git : `out/lot3-validation/test-results/lot3-recipes-tests.trx` (suite actuelle) et `lot3-tests.trx` (100 tests de la première livraison du socle).
- Tests simulés : runtimes/modèles/licences factices, HTTP en mémoire, corruption, inventaire modifié, modèle manquant, contrôle fonctionnel échoué, panne réseau et annulation, réparation ratée, activation corrompue, verrou concurrent et indépendance des modules, sources/exports conservés. Tests Windows réels : processus privé de contrôle, code d'échec, délai/annulation, jonction externe refusée à la désinstallation. Les fixtures ne prouvent pas la compatibilité des moteurs IA.
- Essais du binaire Release dans le bac à sable : `modules list` et `status separation` (0, absents), `verify transcription` (2), aide (0) et identifiant invalide (1). Résultats actuels dans `out/lot3-validation/cli-recipes-smoke.json`. L'ancien `cli-smoke.json` correspond au socle antérieur, où les recettes n'étaient pas disponibles. Aucune installation dans les modules personnels du compte n'a été lancée par ces essais CLI.
- Deux inférences réelles de contrôle : Demucs charge le checkpoint vérifié et calcule quatre pistes finies d'une seconde stéréo 44,1 kHz ; Whisper charge le modèle quantifié et génère un TXT sur une seconde de WAV mono 16 kHz. Les contrôles ne valident pas la qualité audio ou textuelle : l'essai direct Whisper sur silence peut produire « Thank you. », ce qui confirme la nécessité du traitement des cas sans parole au lot 5.
- Correction issue des essais : le binaire MSVC Whisper échouait avec `0xC0000409` sur le chemin de modèle accentué. Les arguments modèle/WAV/sortie sont désormais relatifs ASCII depuis le dossier de contrôle privé, en conservant le stockage réel avec espaces et accents. Voir [le diagnostic amont](https://github.com/ggml-org/whisper.cpp/issues/4018). Python utilise `-I -B`, pip installe uniquement les fichiers locaux sans résolution réseau, et le hachage traite au plus quatre fichiers à la fois.
- **Cycle réel complet réussi**, rapport `out/lot3-validation/real-modules-04/results.json` : les deux modules atteignent `Ready`, une corruption du poids de chacun est détectée (`Needs repair`), la réparation restaure `Ready`, puis la désinstallation rend chacun `Not installed`. Les sources/exports sentinelles et l'autre module sont contrôlés et préservés. Exécution par `tools/OD.Converter.ModuleValidation`, jeton administrateur **False**, dans un chemin privé contenant espaces et `é`, sans usage du runtime d'OD.VideoDub. Les acquisitions initiales ont utilisé le téléchargeur HTTPS réel ; les réparations du validateur ont réutilisé les artefacts contrôlés de `real-modules-02/downloads`, avec réseau interdit. La reprise `--resume` du dossier dédié a aussi été exercée après l'échec natif Whisper et sa correction. Les pannes réseau, annulations et manque d'espace restent des tests simulés, distincts de ce cycle réel.
- **Lot 3 terminé** sur ce périmètre, sans revendiquer de Windows propre, de séparation d'un média utilisateur, de mesure du délai IA ni de qualité de transcription. La validation d'une publication sur machine propre et la maintenance de sécurité des versions figées restent au lot 7. Voir [le contrat du gestionnaire](modules-optionnels.md) et [les distributions/licences et la procédure reproductible](distributions-modules.md). Prochaine étape séquentielle : **lot 4, séparation Demucs et alignement des exports** ; le lot 5 est également débloqué.
- Les modifications préexistantes de `ConversionOptionsValidator.cs` et `FfmpegConverter.cs` sont préservées.

## Lot 4 — Demucs et séparation voix/fond

### Travail

- [x] Adapter l'appel éprouvé dans OD.VideoDub sans dépendre de son installation.
- [x] Installer un Python privé, les dépendances Demucs et le modèle retenu (fourni et vérifié au lot 3).
- [x] Rediriger tous les caches et modèles vers l'espace OD.Converter (fourni au lot 3 ; à conserver dans l'adaptateur).
- [x] Produire voix, fond et bande-son complète selon les exports choisis.
- [x] Appliquer le contrat temporel aux sorties et les paramètres de conversion d'export.
- [x] Contrôler les erreurs, la progression et l'arrêt des processus.

### Acceptation

- [x] La même action fonctionne sur un fichier audio et une vidéo.
- [x] Les deux pistes séparées ont la même longueur et la même origine que la bande-son de référence PCM.
- [ ] Essais réels sur narration musicale, interview et ambiance/bruitages ; appréciation de la qualité consignée.
- [x] Absence de GPU NVIDIA supportée par la configuration CPU retenue.
- [x] Aucun résultat réutilisé seulement parce qu'un ancien fichier de même nom existe.

Preuves / résultats (2026-11-01) :

- `process --action separate` est implémenté par `AudioSeparationService`, `DemucsSeparator` et un adaptateur Python embarqué. Réutilisation de la logique deux stems d'OD.VideoDub, avec API Demucs locale plutôt que découverte de modèles par sa CLI : checkpoint vérifié du lot 3, CPU explicite, deux threads, `shifts=0`, segments avec recouvrement de 25 %, SoundFile float32 et aucun téléchargement. Les voix et la somme des autres sources sont exportées ; `original` utilise le PCM préparé avant séparation. Tous les exports reprennent par défaut la fréquence et les canaux source ; les paramètres explicites sont appliqués aux trois exports. Voir [la politique et la recette de séparation](separation-audio.md).
- Vérification d'intégrité et contrôle fonctionnel avant usage ; verrou de génération conservé pendant le traitement pour empêcher réparation/désinstallation concurrentes. Python et caches privés, environnement CPU du lot 3 conservé ; aucun Python global ni moteur d'OD.VideoDub utilisé. Les échantillons vocaux locaux du projet voisin servent uniquement de données de recette. Chaque opération crée ses propres intermédiaires, sans reprise par nom de fichier. Absence/corruption/concurrence du module : code 2 avec commande d'installation/réparation ; aucune installation implicite.
- Contrôle de toutes les collisions avant préparation/inférence, protection de la source, encodage partagé avec l'extraction et finalisation individuelle des exports complets. Échec tardif/annulation : liste des résultats déjà finalisés, fichiers manquants annoncés par la CLI, anciennes destinations inachevées préservées et partiels nettoyés. Progression native Demucs affichée, annulation de l'arbre de processus et limite de sécurité de sept jours. Les stems tronqués de plus d'un échantillon sont rejetés avant normalisation, sans les masquer par un remplissage de silence.
- `dotnet build OD.Converter.slnx -c Release -m:1 --no-restore` : réussi, **0 avertissement, 0 erreur**, après restauration NuGet complète réussie. Un essai intermédiaire avait rencontré NU1900 dans le bac à sable ; un autre avait échoué sur la DLL du validateur encore en cours d'exécution. La compilation finale a été relancée après sa fin. `dotnet test tests/OD.Converter.Tests/OD.Converter.Tests.csproj -c Release -m:1 --no-restore --logger "trx;LogFileName=lot4-tests.trx" --results-directory out/lot4-validation/test-results` : **122 tests réussis**, 0 échec, 0 ignoré. VSTest exécuté hors de la restriction locale de communication du bac à sable, comme aux lots précédents.
- Tests avec moteur simulé et FFmpeg réel : marqueurs à 0,25/1,5/2,75 s, sources mono 22,05 kHz et stéréo 48 kHz, export FLAC mono rééchantillonné à 48 kHz, longueur exacte et repères à un échantillon près. Contrôles de collision sur le dernier export, module absent, échec/annulation moteur, stem tronqué, échec/annulation du deuxième encodage avec premier export conservé, nouvelle inférence malgré des exports existants, verrouillage et codes de sortie CLI. Ces contrôles ne revendiquent pas de qualité IA.
- **Quatre traitements réels htdemucs CPU**, via `tools/OD.Converter.SeparationValidation`, dans `out/lot4-validation/real-01` (ignoré par Git). Trois WAV de 12 s à 44,1 kHz stéréo : voix française sur fond tonal synthétique, alternance de deux échantillons vocaux, bruit brun et bruitages synthétiques. Pour chacun, les trois exports ont **529 200 frames**. Une vidéo MKV de 14 s avec deux pistes audio utilise explicitement l'index absolu **2**, retardé de **500 ms**, avec silence final : les trois exports ont **672 000 frames à 48 kHz**. Aucun GPU utilisé ; stack PyTorch CPU avec CUDA masqué.
- Sur chaque cas réel, repères au début, au milieu et en fin : **décalages de reconstruction 0/0/0 échantillon** par corrélation de `vocals + background` avec `original`. Les repères WAV sont à 0,25/6/11,75 s ; ceux de la piste vidéo retardée sont à 0,75/6,5/12,25 s. Aucune compensation de délai ajoutée. Toutes les sorties sont finies et de longueur exacte. Le rapport local `out/lot4-validation/real-01/results.json` conserve paramètres, frames, temps de traitement, RMS et chemins. Ces mesures de reconstruction et le recadrage de l'API valident la recette temporelle de ces fixtures ; elles ne promettent pas l'identité de formes d'onde ou la conservation perceptive de chaque événement dans chaque stem.
- **Qualité à vérifier par écoute** : RMS voix/fond respectivement 0,05862/0,04005 pour la narration, 0,04687/0,00781 pour l'alternance de voix et 0,00056/0,01320 pour l'ambiance. La reconstruction n'est pas identique à l'original (RMS d'erreur 0,00253/0,00083/0,00222) ; les estimations ne doivent pas être présentées comme des pistes de studio. Ces chiffres sont des observations automatiques, pas une appréciation auditive. Les fixtures utilisent des voix réelles avec fonds/interview assemblés ; **aucune écoute humaine sur une vraie narration musicale, interview complète ou ambiance réelle n'est revendiquée**. Le critère d'acceptation correspondant reste décoché et le lot est **À vérifier**, pas Terminé. Pas de recette de plusieurs heures ni de Windows propre à ce stade.
- Aide `process --help` et retour CLI réel **2** sans module avec commande d'installation vérifiés. Les fichiers préexistants `ConversionOptionsValidator.cs` et `FfmpegConverter.cs` restent inchangés. Prochaine action : écoute des stems et recette sur médias représentatifs pour clôturer le lot 4 ; le **lot 5 Whisper** reste techniquement débloqué, sans implémentation de transcription dans ce lot.

## Lot 5 — Whisper, TXT et SRT globaux

### Travail

- [x] Adapter l'appel whisper.cpp d'OD.VideoDub avec chemins privés et langue configurable.
- [x] Installer le moteur natif, ses bibliothèques et le modèle multilingue retenu (fourni et vérifié au lot 3).
- [x] Produire un TXT UTF-8 et un SRT global sans identification des interlocuteurs.
- [x] Permettre la transcription directe ou sur les voix isolées, avec la même origine temporelle.
- [x] Préserver les intervalles silencieux et traiter le cas sans parole sans inventer de texte.
- [x] Vérifier la cohérence structurelle des sous-titres et signaler les résultats incomplets.

### Acceptation

- [x] Transcription directe disponible avec Whisper seul, pour audio comme vidéo.
- [x] Transcription des voix séparées disponible avec les deux modules.
- [x] Essais en français et en anglais : langue source conservée, sans traduction implicite.
- [ ] Plusieurs personnes transcrites globalement, sans étiquettes de locuteur.
- [x] SRT avec index, timestamps valides et origine correcte, y compris avec silence initial et piste audio retardée.
- [x] Relecture d'un échantillon pour précision du texte et placement des phrases ; limites consignées.

Preuves / résultats (2026-11-01) :

- `AudioTranscriptionService` branche les actions CLI `transcribe` et `separate-transcribe`. Préparation PCM alignée partagée ; voix Demucs alignées pour l'action combinée ; TXT UTF-8 sans BOM et SRT globaux construits depuis les mêmes segments validés. Pas de traduction demandée, pas de diarisation, aucune réutilisation de résultats par existence de fichiers. La transcription directe n'ouvre pas le module Demucs. Tous les exports demandés sont contrôlés avant les moteurs ; finalisation individuelle et résultats déjà terminés conservés si une étape tardive échoue.
- `WhisperTranscriber` adapte l'appel natif d'OD.VideoDub avec modèle et exécutable privés, CPU, langue `auto`/code choisi et chemins relatifs pour les accents. Le PCM mono 16 kHz couvre la présentation complète. Le moteur restitue les timestamps VAD dans cette chronologie ; aucun offset de piste n'est ajouté une seconde fois. Index SRT, ordre sans chevauchement, bornes et texte contrôlés avant export. Une sortie manquante, tronquée ou hors durée échoue avec code 3. Un SRT valide ne prouve pas que toutes les paroles sont reconnues.
- **Défaut réel détecté puis corrigé** : sans VAD, l'ambiance synthétique sans parole produisait six répétitions japonaises inventées malgré `-sns` ; l'essai combiné produisait aussi une fin SRT à 12,020 s pour 12 s et était correctement rejeté. Preuves exploratoires conservées dans `out/lot5-validation/real-03`. Ajout du VAD officiel Silero v5.1.2 (885 098 octets, MIT), modèle et licence à révisions/SHA-256 fixés dans le catalogue. Recette Whisper **r2**, contrôle fonctionnel avec VAD ; une ancienne génération nécessite une installation/réparation explicite. Aucun téléchargement pendant un traitement. Le VAD réduit les hallucinations, sans garantir leur absence sur toute source ni la détection de toute voix.
- `dotnet build OD.Converter.slnx -c Release -m:1 --no-restore` : réussi, **0 erreur**, un avertissement **NU1900** lié à l'accès indisponible au service NuGet d'audit des vulnérabilités. `dotnet test OD.Converter.slnx -c Release -m:1 --no-restore --logger "trx;LogFileName=lot5-final-vad-tests.trx" --results-directory out/lot5-validation/test-results` : **147 tests réussis**, 0 échec, 0 ignoré. Tentative VSTest restreinte abandonnée sur connexion au testhost ; recette finale hors de cette restriction. TRX final distinct des tentatives précédentes.
- Tests simulés des moteurs avec FFmpeg réel : transcription directe/voix séparées, langues fr/en/auto, UTF-8, silences et sélection d'une deuxième piste vidéo retardée de 500 ms (5 s, exactement 80 000 frames mono 16 kHz, silences initial/final contrôlés), SRT mal formés/incomplets/hors durée, silence numérique sans appel de reconnaissance, module absent, collision avant moteur, erreur/annulation, export tardif en échec avec TXT déjà finalisé, codes CLI et nettoyage. Ces tests ne constituent pas une mesure de précision linguistique.
- **Six parcours réels CPU avec recette r2**, rapports dans `out/lot5-validation/real-vad-01/results.json` : silence numérique (TXT/SRT vides), narration française sur fond tonal (3 segments), alternance de deux voix fr/en avec silence interne (1 segment, limite ci-dessous), vidéo MKV de 14 s sélectionnant l'index absolu **2** retardé de 500 ms (2 segments, premier timestamp **830 ms**, dernier **11 100 ms**), ambiance synthétique sans parole (TXT/SRT vides, hallucinations observées sans VAD éliminées sur cette fixture), et séparation puis transcription française (voix/fond de **529 200 frames à 44,1 kHz**, TXT/SRT valides). Tous ces parcours retournent **0**. Aucune longueur PCM ou compensation temporelle n'est déduite de la précision des phrases Whisper.
- **Anglais synthétique réel**, voix Windows SAPI Microsoft Zira Desktop, 9,202812 s : recette r2, code 0, deux segments entre 130 et 8 330 ms. Les mots correspondent exactement au texte de référence après neutralisation de casse/ponctuation. Rapport et comparaison dans `out/lot5-validation/real-en-vad-01`. Cela ne remplace pas un essai sur voix anglaise naturelle ni une écoute humaine.
- **Qualité restant à vérifier** : l'essai bilingue fr/en en `auto` avec VAD a retenu l'anglais et omis la première voix française. Aucun indicateur structurel ne permet de reconnaître automatiquement cette omission. L'application ne demande aucune traduction, mais la détection automatique d'une seule langue sur un fichier bilingue est insuffisante. Le critère « plusieurs personnes transcrites globalement » reste donc décoché en attente d'une recette représentative et d'un choix de stratégie linguistique. Les limites de phrases sont estimées (la narration peut commencer à 0 ms malgré son silence initial) ; aucune précision à l'échantillon ni synchronisation labiale n'est revendiquée. Écoute/relecture humaine, chevauchements et voix faibles restent à valider. Lot **À vérifier**, pas Terminé.
- Aide CLI et erreur réelle **2** sans module avec commande d'installation vérifiées. Documentation dans [la politique de transcription](transcription-audio.md), catalogue/licences et aide mis à jour ; validateur reproductible `tools/OD.Converter.TranscriptionValidation` ajouté. Les modifications préexistantes de `ConversionOptionsValidator.cs` et `FfmpegConverter.cs` sont préservées. Prochaine étape technique : **lot 6**, en conservant ouverts les critères de qualité des lots 4 et 5.

## Lot 6 — Parcours et orchestration

### Travail

- [x] Ajouter le menu principal de traitements communs audio/vidéo.
- [x] Afficher en gris les actions indisponibles avec le module requis, sans les rendre exécutables.
- [x] Ajouter `Optional modules` avec installation, vérification, réparation et désinstallation.
- [x] Partager disponibilité et logique de traitement entre menu et CLI.
- [x] Ajouter sélection des pistes, exports, langue, destination et paramètres applicables.
- [x] Afficher le récapitulatif, les étapes, les résultats et les échecs partiels.
- [x] Contrôler les collisions de tous les fichiers demandés avant le calcul.

### Acceptation

- [x] Parcours compréhensible sans distinction artificielle entre traitements audio et vidéo.
- [x] Matrice des quatre combinaisons de modules installés vérifiée.
- [x] Aucun module imposé pour conversion ou extraction et aucune installation implicite.
- [x] CLI utilisable sans interaction ; erreurs de prérequis actionnables.
- [x] Chemins avec espaces, accents et caractères spéciaux correctement transmis aux processus.
- [x] Annulation avec nettoyage et compte rendu fidèle des fichiers déjà finalisés.

Preuves / résultats (2026-11-01) :

- `InteractiveProcessingMenu` fournit le menu sans arguments : `Process audio / video`, `Convert audio (classic)`, `Optional modules`, `Exit`. Le parcours commun affiche toutes les pistes (index absolu, codec, langue, titre, fréquence, canaux, durée), sélectionne automatiquement la piste unique ou propose le choix, puis les quatre traitements selon leurs prérequis. Les actions indisponibles sont affichées en gris avec la commande d'installation/réparation et exclues des choix exécutables. Le lancement direct `odconv <file>` conserve le parcours classique sans détour.
- `ProcessingAvailability` partage les prérequis des actions entre le menu et l'exécution CLI. Les états proviennent des contrôles hors ligne du gestionnaire existant. La CLI ne contrôle que les modules requis par son action ; extraction et conversion restent indépendantes. `Optional modules` délègue les opérations au même `ModulesCommand`, annonce emplacement/version/profil CPU/volume avant confirmation explicite d'installation ou réparation et n'effectue aucun téléchargement implicite. Les générations restent verrouillées et revérifiées à l'ouverture par les services existants.
- Choix des exports, langue auto ou code source, format audio, bitrate ou qualité MP3/Ogg, compression FLAC, profondeur PCM, canaux, fréquence, destination et préfixe ; seuls les paramètres applicables sont proposés. Récapitulatif avant exécution et confirmation groupée des destinations déjà présentes. `ProcessCommand` vérifie désormais tous les chemins demandés avant tout appel aux moteurs, en complément des validations propres aux services. Les arguments sont transmis sous forme de listes, sans concaténation de commande shell.
- Les traitements sont exécutés par `ProcessCommand`, avec les mêmes étapes, avertissements, codes, fichiers finalisés et fichiers manquants après échec partiel. Les invites du nouveau menu observent le jeton d'annulation ; code 130 et compte rendu conservé après annulation du traitement. Les tests existants des services couvrent le nettoyage et la conservation des sorties déjà finalisées après erreur/annulation tardive.
- `dotnet build OD.Converter.slnx -c Release -m:1 --no-restore` : réussi, 0 erreur ; un avertissement **NU1900** sur le projet de validation de transcription, lié à l'accès indisponible au service NuGet d'audit. `dotnet test OD.Converter.slnx -c Release -m:1 --no-restore --logger "trx;LogFileName=lot6-final-tests.trx" --results-directory out/lot6-validation/test-results` : **162 tests réussis**, 0 échec, 0 ignoré. VSTest restreint abandonné sur connexion au testhost ; exécution finale hors de cette restriction. Rapport local ignoré par Git : `out/lot6-validation/test-results/lot6-final-tests.trx`.
- **15 nouveaux cas automatisés** : matrice des quatre combinaisons de modules (états simulés), états absents/à réparer/en cours et commandes actionnables, extraction sans inspection ni création de modules, collision du dernier export SRT avant tout moteur, navigation `Optional modules` et retour, refus d'installation/réparation/désinstallation sans création de fichiers, annulation pendant l'attente d'une touche. Parcours complet du vrai rendu Spectre.Console avec touches simulées et **FFprobe/FFmpeg réels**, produisant un WAV depuis un chemin avec espaces, accents, `&` et crochets. Ce dernier a détecté puis validé la correction de l'échappement des valeurs par défaut de chemin/préfixe.
- Essais du binaire Release : extraction CLI d'un WAV de 200 ms sous `out/lot6-validation/cli été & [test]`, exactement **8 820 frames à 44,1 kHz** ; seconde exécution rejetée avec code **1** sur collision, empreinte de l'export existant inchangée ; régression `convert` WAV→MP3, aide racine, `process --help` et `modules --help` réussies. Sources et résultats locaux ignorés par Git. Ces essais ne remplacent pas la publication autonome du lot 7.
- README, aide racine et contrat CLI mis à jour. Les modifications préexistantes de `ConversionOptionsValidator.cs` et `FfmpegConverter.cs` sont préservées. Aucune nouvelle installation réelle de modèle ni recette humaine d'écoute/ergonomie n'est revendiquée pour ce lot ; les cycles réels du lot 3 et traitements CPU des lots 4–5 restent leurs preuves propres. Les critères de qualité des lots 4 et 5 restent ouverts. Prochaine étape technique : **lot 7**, validation de livraison et environnement Windows propre.

## Lot 7 — Validation et livraison

**État actuel : clôturé le 1er novembre 2026 sur décision utilisateur**, sur le
périmètre réalisé et validé. Les cases non cochées ci-dessous sont des validations
reportées, sans échéance ; elles ne bloquent plus cette clôture et ne sont pas
présentées comme réussies. Voir « Clôture du lot 7 et validations reportées ».

### Travail

- [x] Ajouter les tests nécessaires pour analyse, timestamps, sélection, modules et orchestration.
- [x] Effectuer les essais réels avec FFmpeg, Demucs et Whisper ; distinguer les résultats simulés.
- [x] Vérifier les formats vidéo représentatifs avec le FFmpeg embarqué.
- [ ] **Reporté** : tester le programme publié sur un environnement Windows sans Python ni modules préinstallés.
- [x] Mettre à jour README, aide CLI, exemples et documentation des installations/licences.
- [x] Publier l'EXE auto-contenu selon la procédure du projet et consigner sa validation.

### Acceptation

- [x] Suite de tests pertinente réussie ; toute commande build/test comprend explicitement `-m:1`.
- [x] Régression des conversions audio existantes contrôlée.
- [ ] **Reporté** : extraction simple vérifiée hors ligne avec le seul EXE publié sur Windows vierge.
- [x] Alignement PCM validé sur médias longs et cas de décalage, pas seulement sur la durée totale.
- [x] Transcription/SRT relus et limites de qualité décrites.
- [ ] **Reporté** : installation et désinstallation des deux modules validées séparément sur un environnement propre.
- [x] Chaque lot possède des preuves ; les contrôles non réalisés sont explicitement reportés par décision utilisateur, sans réussite implicite.

Preuves / résultats (2026-11-01) :

- Ajout de `tools/OD.Converter.ReleaseValidation` à la solution : recette exécutable contre l'EXE publié, générateur/décodeur FFmpeg explicite, contrôles de codes de sortie, frames et repères, JSON avec SHA-256, journal des commandes et erreurs. L'EXE est copié seul sous un chemin avec espaces, accent, `&` et crochets ; PATH des processus limité au répertoire système Windows, overrides FFmpeg/Python/.NET retirés, bundle et temporaires privés. Un `convert --dry-run` contrôle que le FFmpeg sélectionné est celui du bundle extrait. Les installations de l'hôte restent présentes ; cette isolation locale n'est pas une recette Windows vierge ni une coupure réseau.
- `dotnet build OD.Converter.slnx -c Release -m:1` : réussi, 0 erreur, avertissements **NU1900** sur les projets de validation de transcription/livraison (service d'audit NuGet inaccessible). Reconstructions ciblées du validateur après corrections de sa fixture/rééchantillonnage : réussies, même avertissement. `dotnet test OD.Converter.slnx -c Release -m:1 --no-build --no-restore --logger "trx;LogFileName=lot7-final-tests.trx" --results-directory out/lot7-validation/test-results` : **162 tests réussis**, 0 échec, 0 ignoré. La tentative restreinte a échoué à joindre testhost ; relance hors de cette restriction réussie. Les couvertures analyse, sélection, modules et orchestration des lots précédents sont conservées ; les nouveaux contrôles sont dans la recette de livraison, sans moteur IA simulé présenté comme réel.
- Publication locale réussie par `dotnet publish src/OD.Converter -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -m:1 -o publish`. EXE testé : `publish/odconv.exe`, SHA-256 **CD24769F1F2CD311AF5ED3B1CF46C55BCC4DC79734DDB14A991DFDCC61670EDE**. Aucune release distante créée. `build.ps1` conserve désormais `-m:1` aussi à la publication et refuse de déclarer le succès si `--help` ou `formats` échoue.
- Recette finale `out/lot7-validation/release-03` : **code 0**, aides/version/formats, extraction MP4/MPEG-4/ALAC, MKV/FFV1/PCM, MOV/MPEG-4/PCM, AVI/MPEG-4/PCM et WebM/VP8/Opus. Chaque export mesure **240 000 frames à 48 kHz**. Repères début/milieu/fin : **0/0/0 échantillon** pour MP4/MKV/MOV/AVI ; **48/48/48 échantillons** pour WebM/Opus, dans la tolérance codec de 10 ms. Retard de 500 ms et audio plus court que l'image pour MP4/MKV/MOV/WebM. AVI sans offset : la première fixture a révélé que ce muxeur ne conservait pas `-itsoffset`, vérifié par FFprobe ; fixture corrigée, sans attribuer à l'application un décalage absent du fichier.
- WAV PCM de **deux heures**, source 8 kHz exportée à **22,05 kHz**, **158 760 000 frames exactes**. Cinq repères à 0,25/1 800/3 600/5 400/7 199,75 s : **0/0/0/0/−1 échantillon**, dans la tolérance d'un échantillon. Ce contrôle valide préparation/rééchantillonnage/export ; il ne valide pas une inférence Demucs/Whisper de deux heures. Une première demande 16 kHz a été rejetée conformément aux fréquences CLI autorisées ; la recette finale utilise 22,05 kHz.
- Régression publiée WAV→MP3 et `probe` réussie ; collision rejetée avec code **1**, empreinte de l'export inchangée. Les essais interrompus `release-01`/`release-02` et le rapport final sont conservés localement dans `out`, ignoré par Git. Les modules IA n'ont pas été réinstallés ni réexécutés par cette recette : les essais CPU réels restent ceux des lots 3–5.
- README clarifie le périmètre sans dépendance externe (conversion/extraction), et la [procédure de livraison](validation-livraison.md) documente commandes, formats, tolérances, limites, licences et recette Windows vierge, avec installations séparées des deux modules. Aide CLI existante vérifiée sur le binaire publié. Maintenance des distributions/licences avant diffusion publique encore à revoir ; aucun audit externe nouveau n'est revendiqué.
- **Restent obligatoires** : EXE hors ligne sur Windows vierge sans Python/.NET/modules globaux, installation/désinstallation séparées dans cet environnement, recette IA de livraison et écoute/relecture sur contenus représentatifs des lots 4–5. Aucune validation humaine ou de machine propre n'est cochée implicitement. Les modifications préexistantes de `ConversionOptionsValidator.cs` et `FfmpegConverter.cs` sont préservées. Prochaine étape : exécuter la recette Windows vierge et consigner les écoutes/relectures avant clôture du lot 7.
- Préparation de cette étape (2026-11-01) : kit transférable ajouté dans `tools/clean-windows` avec EXE seul, fixtures WAV/MP4 à repères, inventaire SHA-256, exécution invitée PowerShell 5.1 en deux phases (`Offline` et `Modules`), journaux JSON et vérificateur des exports rapatriés. La phase Modules prévoit les deux installations séparées, inférences et désinstallations, puis combinaison, réparation et indépendance des modules ; elle n'a **pas été exécutée** ici. Fiche de relecture fournie ; procédure détaillée dans [validation-livraison.md](validation-livraison.md#préparer-le-kit-transférable).
- Vérification locale du kit : EXE publié d'empreinte **CD24769F1F2CD311AF5ED3B1CF46C55BCC4DC79734DDB14A991DFDCC61670EDE**, phase `Offline -DevelopmentHost` sous **Windows PowerShell 5.1**, chemins avec espaces, accents, `&` et crochets, **18 commandes contrôlées**, code 0. Mesure rapatriée : **240 000 frames**, PCM mono signé 32 bits à 48 kHz, repères **0/0/0 échantillon**. Contre-épreuve : export de même longueur décalé de 100 ms et empreinte actualisée, rejeté par la mesure des repères. Preuves locales : `out/lot7-validation/clean kit été & [02]`, `clean smoke été & [02]` et `clean-negative-markers`. Premier essai conservé (`clean-host-smoke-01`) : contrôle du message d'installation corrigé pour supporter les retours à la ligne de la console. Aucun code applicatif ni les deux fichiers préexistants n'ont été modifiés par cette préparation.
- Limite de l'environnement : `WindowsSandbox.exe` absent et `Get-VM` échoue sur l'accès CIM. Aucun Windows vierge n'a été démarré, aucun réseau n'a été coupé, aucune nouvelle inférence IA ni écoute humaine n'est revendiquée. Les rapports laissent les validations Windows vierge et humaine explicitement à `false`. Le lot 7 reste **En cours**, les lots 4–5 **À vérifier**. Prochaine action : transférer le kit dans une VM vierge standard avec un média représentatif, exécuter les phases puis consigner mesures, licences, annulations et relectures.
- Suite avec média fourni par l'utilisateur (2026-11-01) : absence de VM confirmée ; lecture du fichier réseau `\\TANKER\VideosODYT\Les Jeudi de l'IA\EP 017\FILM\Episode 17 - Ia as a judge.mp4`, **324 026 628 octets**, durée **584,448 s**, vidéo H.264 et piste audio absolue **1**, AAC stéréo 48 kHz. Copie locale contrôlée contre l'original par SHA-256 **3596779A02DFBFF3CF2B9482AAE934A4CB844AB2DAD19DCDAF7AA6423289D98D**, provenance conservée sous `out/lot7-validation/episode17-source`. Aucun changement du média réseau.
- Recette reproductible ajoutée : `tools/OD.Converter.PublishedAiValidation`, qui lance l'EXE autonome par sa CLI sur trois extraits vidéo ALAC stéréo 48 kHz de **30 s**, aux offsets source **0 / 277,224 / 554,448 s**. Chaque extrait est transcrit directement puis après séparation Demucs, en français. Les générations existantes, issues des artefacts fixés des lots 3–5, sont copiées uniquement dans les modules personnels préalablement absents, puis intégralement vérifiées par l'EXE publié. PATH système seul, overrides retirés, EXE copié seul sur chemin spécial, bundle/temporaires privés. Cette copie n'est **pas** une installation depuis les distributions ni une recette Windows vierge. Les dossiers de générations d'origine restent intacts.
- Défaut réel reproduit dans `episode17-published-ai-01` : dernier sous-titre Whisper à **30,020 s** pour 30 s, traitement rejeté avec code **3** et aucun TXT/SRT finalisé. L'implémentation VAD amont complète son dernier bloc de 512 frames à 16 kHz et utilise des centisecondes. Correction bornée dans `WhisperTranscriber.cs` : calcul de la dernière fenêtre et de son arrondi natif ; seule une fin de dernier sous-titre dans cette marge est ramenée à la durée source, avec avertissement. Débuts, autres fins et PCM inchangés. Les chevauchements et dépassements plus grands restent rejetés ; le parseur général reste strict. Sept tests de régression ajoutés, dont 30,020 s accepté/coupé, 30,030 s rejeté, segment entièrement hors source, fin intermédiaire, chevauchement, et 12,020 s toujours rejeté pour 12 s sans bloc partiel. Voir [la politique VAD](transcription-audio.md).
- Compilation finale : `dotnet build OD.Converter.slnx -c Release -m:1 --no-restore`, **0 erreur**, trois avertissements **NU1900** (audit NuGet inaccessible). Reconstruction ciblée du nouveau validateur après ajout du contrôle d'empreinte des exports : réussie, même avertissement. Une tentative intermédiaire de reconstruction a rencontré un verrou sur l'apphost du validateur lancé trop tôt ; la reconstruction finale a été effectuée après arrêt du processus. `dotnet test OD.Converter.slnx -c Release -m:1 --no-build --no-restore --logger "trx;LogFileName=lot7-episode17-unrestricted-tests.trx" --results-directory out/lot7-validation/test-results` : **169 tests réussis**, 0 échec, 0 ignoré. Tentative restreinte abandonnée après délai de connexion testhost ; relance hors restriction réussie.
- EXE corrigé republié par `dotnet publish src/OD.Converter -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -m:1 --no-build --no-restore -o publish`, SHA-256 **F2E61DA592A40913E80EA9A3C88FC8266414DFD6C35CEFEDA4B1E128A43CED84**. Nouvelle recette `release-04` : **code 0**, cinq conteneurs et deux heures PCM contrôlés ; mesures identiques à `release-03` (vidéos 240 000 frames, repères 0/0/0 sauf WebM 48/48/48 ; deux heures 158 760 000 frames, 0/0/0/0/−1). Aucune release distante créée.
- Recette IA finale `out/lot7-validation/episode17-published-ai-02/results.json` : **code 0**, `Passed: true`, **35 commandes** aux codes attendus et **21 exports** contrôlés par empreinte. Les **six parcours** directs/combinés réussissent : début **8/8 segments**, milieu **4/4**, fin **7/7**. Quatre fins VAD sont ramenées de 30,020 à **30,000 s** avec avertissement ; l'extrait final finit à **26,530 / 26,370 s** selon le parcours. Chaque original/voix/fond comporte **1 440 000 frames**, stéréo 48 kHz, PCM signé 32 bits, valeurs décodées finies. Corrélation original / somme voix + fond : **0/0/0 échantillon sur chacun des trois extraits**, dans des fenêtres actives proches de leur début/milieu/fin. RMS d'erreur de reconstruction respectifs : **0,0006095 / 0,0005750 / 0,0006268** ; ces mesures ne valident pas la qualité perceptive d'un stem.
- Nettoyage réel de la recette IA : désinstallation des copies séparation/transcription par l'EXE, autre module vérifié `Ready` après la première désinstallation, puis chacun `Not installed` (vérification code **2**). Source locale, EXE et 21 exports inchangés par empreinte après nettoyage. Des fichiers de verrou inactifs restent dans le dossier privé, sans génération ni modèle actif. Jeton administrateur **False** ; les inférences sont bien celles des moteurs CPU réels, aucun moteur simulé dans cette recette.
- Fiche `episode17-published-ai-02/Review.md` générée et vérifiée sous PowerShell 5.1 par `New-Review.ps1` : liens vers les trois vidéos, originaux/voix/fonds, six TXT/SRT et transcriptions directes/après séparation regroupées par extrait. Lecture textuelle : ponctuation/segmentation différentes au début, transcription textuelle identique au milieu, et « promptes » à relire dans l'extrait final. Cela n'est pas une comparaison à une référence humaine ni une écoute. Les SRT utilisent la chronologie locale de chaque extrait ; aucune transcription complète des 9 min 44 s n'est revendiquée. Nouveau kit `episode17-clean-kit-02` préparé avec l'EXE corrigé et l'extrait médian comme média de parole.
- État après ces essais : case des essais FFmpeg/Demucs/Whisper réels cochée sur preuves ; lots **4–5 À vérifier**, lot **7 En cours**. Restent obligatoires : écoute/relecture en regard de la source et médias complémentaires (musique/interview/ambiance), provenance Windows vierge et réseau réellement coupé, installations depuis les distributions dans cet environnement, licences et annulations de recette. L'absence de VM ne vaut pas validation de cet environnement. Prochaine action disponible : ouvrir la fiche de relecture et consigner les observations humaines ; recette Windows vierge à organiser lorsqu'un environnement sera disponible. Les deux fichiers préexistants `ConversionOptionsValidator.cs` et `FfmpegConverter.cs` sont préservés.
- Validation supplémentaire sur le **fichier complet** (2026-11-01) : option `--full-transcription` ajoutée au validateur publié ; seule la génération Whisper est copiée/vérifiée, l'index audio **1** et la durée **584,448 s** sont lus sur le média original local, sans créer d'extraits ni lancer Demucs. Compilation ciblée `dotnet build tools/OD.Converter.PublishedAiValidation -c Release -m:1 --no-restore` réussie, **0 erreur**, avertissement **NU1900** d'audit inaccessible. Code applicatif et EXE inchangés (SHA-256 **F2E61DA592A40913E80EA9A3C88FC8266414DFD6C35CEFEDA4B1E128A43CED84**) ; les 169 tests applicatifs de la livraison précédente restent leur preuve propre, sans nouvelle exécution revendiquée pour cette extension de recette.
- Rapport `out/lot7-validation/episode17-full-transcription-01/results.json` : **code 0**, `Mode: FullTranscription`, `Passed: true`, **5 commandes** aux codes attendus, **2 exports** TXT/SRT. Traitement réel Whisper CPU de **324,600 s** pour tout le fichier ; **153 segments** validés, de **00:00:00,260 à 00:09:40,830**, sur la chronologie de l'épisode complet. Cohérence TXT/SRT, numérotation, durées positives, ordre, absence de chevauchements et limites source vérifiés. TXT : SHA-256 **42D88C768DF373FE5B51C2855F9847684EFD3B5D6D86241D88D117507C6FC6F3** ; SRT : **EFCF2DBCA08909083A14EC8FF45AC602C1D93765055C0ABBCCA5E601833A4F4D**. Whisper est désinstallé par l'EXE et sa vérification retourne **2**, source locale/EXE/exports inchangés par empreinte après nettoyage. Aucune installation/téléchargement ni nouvelle validation de Demucs n'est revendiqué ici.
- Fiche complète `episode17-full-transcription-01/Review.md` générée sous **PowerShell 5.1** : lien vers vidéo entière, TXT/SRT, transcription et plages sans sous-titre d'au moins deux secondes. **Aucune plage intérieure** n'atteint ce seuil ; seule la fin **580,830–584,448 s**, soit **3,618 s**, est signalée pour écoute. Ce résultat ne prouve ni l'exhaustivité ni la précision linguistique. Lecture textuelle ajoutée à la fiche : passages « de 1 à 1 à 5 » à **3:38,410**, « patern » à **5:47,670**, « reflux obligatoire » à **5:58,100**, « Au faiblesse » à **7:33,400**, « promptes » à **8:09,340** et **9:23,080**, à comparer avec l'audio avant correction. Les TXT/SRT conservent la sortie moteur.
- État après transcription complète : lots **4–5 À vérifier**, lot **7 En cours**, `HumanReviewCompleted` et `IsCleanWindows` toujours **false**. Prochaine étape : écoute ciblée des passages signalés, vérification des sous-titres et de la séparation, puis consignation d'une décision humaine. Windows vierge reste indisponible ; les autres réserves de livraison ci-dessus restent ouvertes. Aucun fichier source sur le réseau n'a été modifié.
- **Relecture et audition utilisateur consignées (2026-11-01)** : quatre corrections confirmées sur la transcription complète : cue **59**, **3:38,410**, « de 1 à 1 à 5 » → « de 1 à 5 » ; cue **99**, **5:58,100**, « reflux obligatoire » → « refus obligatoire » ; cues **129** et **150**, **8:09,340** et **9:23,080**, « promptes » → « prompts ». Cue **117**, **7:33,400**, « Au faiblesse, pas de faiblesse » : utilisateur **OK**, conservé. Aucun verdict explicite sur le cue **94**, « patern » : laissé en attente, sans correction déduite. Le retour ne constitue pas une acceptation globale de toutes les paroles, des limites de phrases, de la plage finale sans sous-titre ou de la qualité de séparation.
- Copies révisées créées dans `episode17-full-transcription-01` : `reviewed-transcript.txt`, `reviewed-subtitles.srt` et `human-review.json` (verdicts, corrections, SHA-256 et périmètre partiel). **153 cues**, numérotation et timestamps strictement identiques aux exports bruts ; cohérence TXT/SRT vérifiée. Les quatre corrections portent uniquement sur les textes confirmés. Empreintes des deux exports moteur bruts contrôlées et inchangées ; `results.json` reste le rapport automatique d'origine. `Review.md` lie les deux versions et consigne la relecture humaine ciblée. Aucune nouvelle inférence, modification applicative ou correction lexicale automatique du moteur n'a été ajoutée. Les lots **4–5 restent À vérifier**, le lot **7 En cours** ; poursuivre les vérifications humaines restantes et la recette Windows vierge lorsqu'un environnement sera disponible.

Mise à jour de la relecture ciblée (2026-11-01) : l'utilisateur confirme aussi que le passage « pattern » à **5:47,670** (cue **94**) est **OK**. Les **six passages signalés ont désormais un verdict humain** : quatre corrections et deux passages acceptés. `Review.md` et `human-review.json` sont mis à jour ; les TXT/SRT bruts et révisés restent inchangés par SHA-256. Cette validation ciblée laisse ouverts la fin sans sous-titre, l'acceptation globale de la transcription, la qualité de séparation et Windows vierge.

Préparation de la prochaine écoute (2026-11-01) : ajout du script reproductible
`tools/OD.Converter.PublishedAiValidation/New-ListeningKit.ps1` et génération du
kit local `out/lot7-validation/episode17-listening été & [01]`. Il rassemble les
trois vidéos début/milieu/fin, original/voix/fond, TXT/SRT directs et après
séparation, transcription complète brute/révisée et verdicts ciblés antérieurs.
Le WAV de fin couvre **572,830–584,448 s** ; la zone sans sous-titre commence à
**8 s dans l'extrait** et dure **3,618 s**. `Review.md` fournit le parcours et
`listening-review.json` les cinq décisions en attente et l'inventaire SHA-256.

Vérifications : génération réelle sous **Windows PowerShell 5.1**, dans un chemin
avec espaces, accent, esperluette et crochets ; **32 fichiers** contrôlés par
SHA-256 ; FFprobe mesure le WAV PCM signé 16 bits stéréo à **48 kHz**, exactement
**557 664 frames**, soit **11,618 s**. Une seconde génération dans le même dossier
est rejetée (code 1), préservant la fiche. Les empreintes source/exports sont
contrôlées par le script ; aucune nouvelle inférence ni modification applicative.
Pas de nouvelle compilation ni exécution de la suite .NET pour cet ajout de
script de recette. Les lots **4–5 restent À vérifier**, le lot **7 En cours**.
Prochaine action : écouter cette fin et les trois séparations, consigner les
verdicts puis l'acceptation globale ; Windows vierge et médias représentatifs
restent à couvrir. Aucun verdict humain n'est déduit automatiquement.

### Verdicts utilisateur sur les écoutes restantes (2026-11-01)

L'utilisateur confirme que la fin **580,830–584,448 s** contient uniquement la
musique de clôture, sans parole : l'absence de texte est **normale et acceptée**.
Il confirme également que le reste des écoutes et les séparations voix/fond sont
**OK**. Les **cinq contrôles du kit sont donc acceptés** pour l'épisode 17 : fin
sans sous-titre, transcription globale et placement, séparation début/milieu/fin.
Les quatre corrections lexicales précédentes restent conservées dans les copies
révisées ; aucune nouvelle correction n'est demandée.

`listening-review.json` et le `human-review.json` de la transcription complète
consignent auteur, date, périmètre et verdicts ; `HumanReviewCompleted` vaut
désormais **true pour cette revue de l'épisode 17**. Les fiches `Review.md` sont
complétées. La copie `prior-human-review.json` reste l'instantané antérieur ; les
rapports automatiques `results.json` ne sont pas réécrits. Les **32 empreintes du
kit**, ainsi que celles des TXT/SRT bruts et révisés d'origine, sont vérifiées et
préservées. Aucune inférence ni compilation supplémentaire pour cette mise à jour.

Les critères de relecture des lots 5 et 7 sont cochés. Les lots **4–5 restent À
vérifier** : cette acceptation porte sur l'épisode et ses extraits, sans prouver
les cas manquants interview/ambiance représentatives ni la transcription de
plusieurs personnes ou les changements de langue. Le lot **7 reste En cours**,
avec `IsCleanWindows` **false**. Prochaine étape : recette sur Windows vierge et
complément des médias représentatifs encore requis, sans nouvelle écoute exigée
sur les cinq contrôles désormais acceptés.

### Clôture du lot 7 et validations reportées (2026-11-01)

L'utilisateur demande de **clore l'étape 7** et de remettre à plus tard les tests
Windows vierge, les bandes à plusieurs interlocuteurs et les autres validations
complémentaires. Le lot 7 est donc **Terminé sur le périmètre validé**, avec les
reports ci-dessous acceptés, **sans date de reprise fixée**. Cette décision
remplace les mentions antérieures « lot 7 En cours » du journal et des preuves,
conservées comme historique. Aucune nouvelle exécution n'est revendiquée.

| Validation reportée | État / conditions de reprise |
|---|---|
| EXE autonome sur Windows vierge, sans Python ni modules préinstallés | Non exécutée ; reprendre le kit `tools/clean-windows` dans un environnement propre |
| Extraction/conversion hors ligne sur cet environnement propre | Non validée sur Windows vierge ; essais locaux existants conservés |
| Installation, désinstallation, réparation et annulation des modules sur cet environnement propre | Non validées sur Windows vierge ; cycles déjà exécutés sur l'hôte conservés |
| Transcription de plusieurs interlocuteurs et changements de langue | Validation représentative reportée ; omission observée sur la fixture bilingue en mode `auto` toujours documentée |
| Qualité de séparation sur d'autres médias : interview, ambiance/bruitages et autres sources représentatives | Validation complémentaire reportée ; acceptation de l'épisode 17 et de ses trois extraits conservée |

Les lots 4 et 5 restent **À vérifier pour ces compléments reportés** ; leur
implémentation et les résultats acceptés ne sont pas remis en cause. Aucun
verdict d'écoute supplémentaire n'est demandé pour l'épisode 17. Les rapports
automatiques et les médias sont inchangés, et `IsCleanWindows` reste **false**.
Leur reprise fera l'objet d'une demande ultérieure ; aucun test ou rappel n'est
programmé par cette clôture.

## Finition de l'interface et aide embarquée — version 1.3.0 (2026-11-01)

Demandée après la clôture du lot 7, cette évolution est réalisée sans reprendre
les validations complémentaires reportées des lots 4 et 5.

- Version applicative portée de **1.2.1 à 1.3.0**, commune aux métadonnées de
  l'assembly, aux menus et à la commande `--version`.
- Présentation Spectre.Console harmonisée : bannière ASCII et identité commune,
  couleurs, sélections contrastées avec descriptions, étapes numérotées,
  panneaux source/module, tableaux arrondis, états des modules lisibles,
  indicateur d'activité de conversion et rubriques d'aide CLI. L'historique de
  sortie est conservé au retour des parcours. `probe --json` reste du JSON brut.
- [Guide utilisateur EN/FR](user-guide.html) autonome en HTML/CSS, sans JavaScript
  ni ressource externe requise ; sommaire, parcours, modules, commandes,
  encodage, fichiers, limites et dépannage. Thèmes clair/sombre, affichage
  adaptatif et styles d'impression.
- Guide embarqué en ressource de l'EXE. L'entrée **User guide / Guide utilisateur**
  et `odconv guide` extraient `odconv-user-guide.html` **à côté de l'exécutable
  réellement lancé** puis invoquent le navigateur par défaut. `guide --extract`
  réalise seulement l'extraction. Une ancienne copie est remplacée atomiquement ;
  une copie identique reste intacte. En cas d'échec du navigateur, le HTML reste
  disponible pour une ouverture manuelle. Le dossier de l'EXE doit être inscriptible.

Vérifications :

- Compilation Release de la solution réussie ; les trois avertissements NU1900
  des outils de recette concernent l'indisponibilité de l'audit NuGet réseau.
- Suite finale : **176 tests réussis**, aucun échec ni test ignoré. Les sept
  nouveaux tests couvrent l'extraction bilingue, l'actualisation, la copie
  identique, les échecs disque/navigateur et la navigation aller-retour du menu
  d'aide. Rapport : `out/ui-validation/test-results/ui-guide-tests.trx`.
  Comme lors des recettes antérieures, la connexion VSTest/testhost échoue dans
  le bac à sable restreint ; l'exécution hors de cette restriction réussit.
- Contrôles Chrome sans interface : vues 1440 × 1080 et 390 × 844, thèmes
  sombre/clair, section française ; aucune ancre interne manquante ni débordement
  horizontal de page aux dimensions contrôlées. Captures et mesures sous
  `out/ui-validation`.
- EXE autonome republié dans `publish/odconv.exe`, puis contrôlé seul dans un
  dossier avec espaces, accent, esperluette et crochets : version, aide, formats,
  extraction de la ressource et comparaison SHA-256 au guide source. Extraction
  FFmpeg réelle, conversion WAV→MP3, JSON brut et aides process/modules réussis.
- Ouverture du guide par le navigateur par défaut effectivement invoquée depuis
  l'EXE publié, code 0. Aucun nouveau modèle IA installé ou exécuté ; les reports
  Windows vierge et médias représentatifs restent inchangés.

### Soutien Buy Me a Coffee ajouté au menu (2026-11-01)

La section **Buy me a coffee** reprend la page de soutien réellement utilisée
par HarmoTools : `https://buymeacoffee.com/knhsynths`, pour Olivier Dahan /
KNHSynths, également déjà référencée par la licence d'OD.Converter. Un panneau
Spectre.Console rappelle le caractère facultatif des contributions. Le choix
**Open Buy Me a Coffee** invoque le navigateur par défaut ; **Back** revient au
menu sans ouvrir de page. En cas d'échec du navigateur, l'adresse reste affichée
pour une ouverture manuelle et le menu reste utilisable.

README et guide HTML EN/FR mis à jour ; guide réembarqué et EXE **1.3.0** republié.
Vérification : **179 tests réussis**, aucun échec ni test ignoré, dont navigation
de soutien, URL attendue, retour sans navigateur et échec d'ouverture simulé.
Rapport : `out/ui-validation/test-results/coffee-menu-tests.trx`. Aucun paiement
ni envoi de données réalisé lors des vérifications.

## Décisions restant à prendre pendant l'implémentation

Ces choix techniques ne remettent pas en cause le périmètre validé.

| Décision | Lot | Résultat |
|---|---|---|
| Syntaxe CLI et contrat précis de sélection des pistes | 1 | `process --action … --audio-track <index absolu>` ; première piste par défaut annoncée ; commandes `modules` réservées ; voir [contrat CLI](cli-audio-video.md) |
| Gestion des durées inconnues et des conteneurs incohérents | 1–2 | Valeurs nullables, fiabilité et avertissements ; écart métadonnées > 100 ms signalé. Lot 2 : refus explicite si origine/durée inconnue ou incohérente ; WAV PCM référencé à l'échantillon zéro avec annonce ; mesure des frames décodées et normalisation de fin, sans reconstruction automatique des endpoints vidéo inconnus |
| Distributions privées Python et whisper.cpp, versions, empreintes et licences | 3 | CPython embarqué 3.11.9 x64, Demucs 4.0.1, PyTorch/torchaudio 2.6.0+cpu et dépendances fixées ; whisper.cpp 1.8.2 CPU natif ; CRT/OpenMP 14.44.35211 locaux. Catalogue embarqué avec URL, tailles/SHA-256 et licences conservées ; voir `distributions-modules.md`. Maintenance des versions et Windows propre à revoir au lot 7 |
| Stratégie CPU/GPU et téléchargement des variantes | 3 | Profil initial CPU, CUDA masqué et sélection CPU explicite attendue des adaptateurs ; GPU seulement via recette distincte testée, sans téléchargement ni détection imposant une installation |
| Modèle Demucs distribué | 3–4 | `htdemucs`, checkpoint `955717e8-8726e21a.th`, empreinte SHA-256 complète fixée et chargement/inférence CPU vérifiés ; qualité sur médias à mesurer au lot 4 |
| Modèle Whisper distribué et éventuelle quantification | 3–5 | `large-v3-turbo-q5_0` multilingue, révision Hugging Face et SHA-256 fixés ; chargement/inférence CPU vérifiés ; qualité et sous-titres à mesurer au lot 5 |
| Politique de conservation des fichiers de travail après échec | 2, 6 | Suppression des intermédiaires et exports partiels sur succès/échec/annulation ; avertissement avec chemin en cas d'échec de nettoyage ; anciennes destinations conservées jusqu'à finalisation. Lot 6 : compte rendu partagé menu/CLI des exports déjà finalisés et manquants après échec ou annulation tardive ; précontrôle de toutes les destinations avant moteur |

## Journal de suivi

Ajouter une ligne à chaque changement significatif, avec les vérifications exécutées et les blocages éventuels.

| Date | Lot | Point de suivi | Vérification / prochaine action |
|---|---|---|---|
| 2026-11-01 | Cadrage | Spécifications et découpage créés ; diarisation exclue ; alignement obligatoire ; modules indépendants pour audio et vidéo | Commencer par les contrats, la syntaxe CLI et l'analyse des sources du lot 1 |
| 2026-11-01 | 1 | Contrats communs, analyse multipiste et temporelle, sélection absolue, noms et syntaxe CLI figés ; aides disponibles, exécution des nouveaux traitements explicitement en attente | Build sans avertissement ; 58 tests réussis ; essais FFmpeg réels et régression WAV→MP3 ; commencer le lot 2 |
| 2026-11-01 | 2 | Extraction CLI hors ligne, préparations PCM et moteurs, mesure/normalisation, référence temporelle, dossiers isolés et finalisation des exports | Build sans avertissement ; 76 tests réussis dont mesures FFmpeg réelles, repères multipistes/offsets/discontinuités et minute rééchantillonnée ; limites documentées ; commencer le lot 3 |
| 2026-11-01 | 3 | Socle de gestion isolée, états, intégrité, générations, verrouillage, réparation et désinstallation ; CLI branchée, recettes réelles encore absentes | Build sans avertissement ; 100 tests réussis avec fixtures, processus Windows et jonction réels ; lot En cours, distributions/licences et installations réelles à valider avant clôture |
| 2026-11-01 | 3 | Recettes CPU Demucs/Whisper, versions et empreintes figées, licences et CRT/OpenMP locaux ; contrôles IA réels, correction des chemins accentués Whisper et vérification d'intégrité bornée | Build sans avertissement ; 106 tests réussis ; cycle réel des deux modules sous jeton non élevé : Ready → corruption détectée → réparation → désinstallation, autres fichiers/modules préservés ; lot terminé, commencer le lot 4 |
| 2026-11-01 | 4 | Séparation CLI Demucs CPU, exports voix/fond/original alignés, verrou de génération et compte rendu des exports partiels ; validateur reproductible ajouté | Build sans avertissement ; 122 tests réussis ; quatre traitements IA réels, longueurs exactes et repères reconstruits 0/0/0 échantillon, dont vidéo multipiste retardée ; lot À vérifier en attente d'écoute et de médias représentatifs ; lot 5 techniquement débloqué |
| 2026-11-01 | 5 | Transcription CLI directe et après séparation, TXT/SRT globaux, validation structurelle et finalisation ; recette Whisper r2 avec Silero VAD après hallucinations détectées sur bruit seul | Build réussi (NU1900 réseau), 147 tests réussis ; six parcours CPU r2 et anglais synthétique réel ; bruit/silence sans texte, vidéo retardée et combinaison validées ; lot À vérifier : omission d'une voix dans la fixture bilingue auto et relecture/écoute encore ouvertes ; lot 6 techniquement débloqué |
| 2026-11-01 | 6 | Menu commun audio/vidéo, disponibilité partagée, gestion explicite des modules, options et récapitulatif, précontrôle multi-exports et annulation des invites ; lancement direct classique conservé | Build réussi (NU1900 réseau), 162 tests réussis ; matrice simulée, navigation avec rendu Spectre réel et extraction FFmpeg réelle sur chemin spécial ; lot terminé, commencer le lot 7 en conservant ouverts les critères de qualité des lots 4–5 |
| 2026-11-01 | 7 | Validateur de l'EXE autonome, cinq conteneurs vidéo, repères PCM sur deux heures, publication locale et procédure Windows vierge ; script de publication contrôle les codes des essais | 162 tests réussis ; recette publiée code 0, longueurs exactes et repères longs 0/0/0/0/−1 échantillon ; lot En cours : Windows vierge, IA de livraison et qualité humaine restent à réaliser |
| 2026-11-01 | 7 | Kit Windows vierge transférable, scripts invités hors ligne/modules, inventaire et journaux SHA-256, mesure des exports rapatriés et fiche de relecture | Essai local PowerShell 5.1 : 18 commandes réussies, 240 000 frames et repères 0/0/0 ; export décalé rejeté. Windows vierge indisponible ici ; phase IA et écoutes non exécutées, lot toujours En cours |
| 2026-11-01 | 5, 7 | Média réel de l'épisode 17, validateur CLI de l'EXE publié, défaut de fin VAD 30,020 s corrigé avec borne calculée, fiche de relecture et nouveau kit | 169 tests réussis ; EXE republié, recette release-04 réussie ; six parcours IA sur trois extraits, 21 exports, 1 440 000 frames chacun et corrélation 0/0/0 ; copies des modules désinstallées. Écoute/relecture et Windows vierge restent ouverts |
| 2026-11-01 | 5, 7 | Mode de recette de transcription complète et fiche globale avec plages à écouter ; épisode 17 traité en entier avec l'EXE publié | Code 0 ; 153 segments sur 584,448 s, TXT/SRT cohérents, 324,600 s de traitement réel sur CPU, source/exports préservés et copie Whisper désinstallée ; six passages textuels signalés, relecture humaine et Windows vierge toujours ouverts |
| 2026-11-01 | 5, 7 | Audition/relecture utilisateur : quatre erreurs confirmées, un passage accepté ; copies TXT/SRT révisées et trace de revue partielle | 153 cues et timestamps inchangés, TXT/SRT cohérents, exports bruts préservés par SHA-256 ; verdict « patern », fin sans sous-titre et acceptation globale encore ouverts, Windows vierge non validé |
| 2026-11-01 | 5, 7 | Passage « pattern » accepté par l'utilisateur : les six points ciblés ont un verdict | Quatre corrections et deux acceptations consignées ; empreintes des exports inchangées. Acceptation globale, fin sans sous-titre, séparation et Windows vierge restent ouverts |
| 2026-11-01 | 4, 5, 7 | Kit d'écoute des contrôles restants : fin contextualisée, trois comparaisons de séparation et transcription globale avec verdicts en attente | PowerShell 5.1 réel, 32 empreintes vérifiées, WAV de 11,618 s / 557 664 frames, écrasement refusé ; écouter et consigner les décisions, puis poursuivre la recette Windows vierge |
| 2026-11-01 | 4, 5, 7 | Cinq verdicts utilisateur acceptés sur l'épisode 17 ; fin musicale sans parole confirmée, transcription et séparations OK | Revues JSON/Markdown mises à jour, 32 empreintes du kit et TXT/SRT d'origine préservés ; critères de relecture cochés, Windows vierge et autres cas représentatifs encore ouverts |
| 2026-11-01 | 7 | Lot clôturé sur décision utilisateur, sur le périmètre validé ; validations Windows vierge, plusieurs interlocuteurs et autres médias remises à plus tard | Tableau de pilotage et critères mis à jour ; reports explicites sans échéance, aucun test non exécuté marqué réussi ; reprise sur demande ultérieure |
| 2026-11-01 | Finition 1.3.0 | Présentation Spectre.Console harmonisée, guide HTML EN/FR autonome embarqué, menu d'aide et commande guide avec extraction à côté de l'EXE puis ouverture navigateur | 176 tests réussis, vues navigateur adaptatives contrôlées, EXE republié et vérifié isolément, ressource comparée par SHA-256, ouverture navigateur et régressions FFmpeg réussies ; validations différées inchangées |
| 2026-11-01 | Finition 1.3.0 | Section Buy me a coffee : soutien volontaire Olivier Dahan / KNHSynths, même URL que HarmoTools, ouverture navigateur explicite et retour au menu | 179 tests réussis, guide EN/FR et README actualisés, EXE republié avec aide réembarquée |
