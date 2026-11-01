# Extension des traitements audio et vidéo

Date de référence : 1er novembre 2026. Statut : périmètre validé, implémentation à réaliser.

Le découpage, les dépendances et l'avancement figurent dans [le suivi des lots](suivi-audio-video.md).

## 1. Objectif

Étendre OD.Converter afin de convertir, séparer et transcrire l'audio d'un fichier audio ou vidéo, avec un parcours commun. Les sorties doivent pouvoir être réutilisées dans un montage en conservant leur position temporelle par rapport à la source.

L'application de base reste un EXE Windows x64 auto-contenu, avec FFmpeg et FFprobe embarqués. La conversion audio et l'extraction audio d'une vidéo fonctionnent sans installation supplémentaire, sans Python et sans téléchargement de modèle.

Deux modules locaux facultatifs complètent ce socle :

- **Separation** : Demucs pour produire une piste voix et une piste de fond.
- **Transcription** : whisper.cpp et un modèle Whisper multilingue pour produire une transcription globale et un SRT.

Les modules sont indépendants. Une transcription ne nécessite pas de séparation préalable. Les fonctionnalités s'appliquent aux sources audio comme aux sources vidéo.

## 2. Périmètre et limites

### Inclus

- Entrées audio actuelles et entrées vidéo courantes, notamment MP4, MKV, MOV, AVI et WebM, sous réserve des codecs effectivement décodables par le FFmpeg embarqué.
- Analyse des pistes audio, sélection d'une piste et erreur explicite si aucune piste audio n'est disponible.
- Conversion ou extraction avec les paramètres audio existants.
- Séparation voix/fond, avec export possible de la bande-son complète en complément.
- Transcription de la piste sélectionnée ou des voix isolées, avec TXT et SRT globaux dans la langue source.
- Préservation des silences et de la référence temporelle de toutes les sorties.
- Gestion des modules depuis le menu principal et accès équivalent en ligne de commande.
- Exécution locale après installation ; aucun service distant requis pour traiter les médias.

### Exclus de cette étape

- Détection, identification ou numérotation des interlocuteurs ; aucune étiquette « Speaker 1 ».
- Séparation d'une piste par interlocuteur.
- Traduction, synthèse vocale, clonage de voix et doublage automatique.
- Remontage automatique de la vidéo avec une nouvelle voix.
- Décomposition supplémentaire du fond en musique, bruitages et ambiances.
- Garantie de retrouver les pistes originales du studio ou d'éliminer toute trace de voix.

Les paroles de plusieurs personnes peuvent apparaître dans une même transcription globale. Les chevauchements de parole et la qualité de la source peuvent affecter le résultat.

## 3. Parcours utilisateur commun

1. Choisir un fichier audio ou vidéo.
2. Analyser la source et afficher ses pistes audio : index, codec, langue, titre, canaux et durée lorsque disponibles.
3. Sélectionner la piste ; pour une seule piste, la sélection est automatique. En CLI, sans sélection explicite, utiliser la première piste audio et l'indiquer.
4. Choisir le traitement :
   - `Convert / extract audio` ;
   - `Separate voice and background` ;
   - `Generate transcript and subtitles` ;
   - `Separate and transcribe`.
5. Choisir les fichiers à produire, leur destination et les paramètres applicables.
6. Afficher un récapitulatif puis exécuter le traitement avec progression et annulation.
7. Afficher les chemins des résultats et les éventuels avertissements.

L'interface, les aides et les messages du programme restent en anglais. Le menu principal comprend `Optional modules` pour installer, vérifier, réparer et désinstaller chaque module.

### Disponibilité des actions

| Action | Aucun module | Demucs seul | Whisper seul | Les deux |
|---|---|---|---|---|
| Convertir / extraire | Oui | Oui | Oui | Oui |
| Séparer voix / fond | Non | Oui | Non | Oui |
| Transcrire + SRT | Non | Non | Oui | Oui |
| Séparer puis transcrire | Non | Non | Non | Oui |

Les actions indisponibles sont visibles en gris avec le module requis et ne sont pas exécutables. L'absence de module ne provoque aucune installation automatique au démarrage ni pendant une conversion simple. En CLI, une demande nécessitant un module absent retourne une erreur explicite avec la commande d'installation.

Les invitations existantes pour convertir un fichier passé directement à l'EXE doivent rester utilisables. Le menu principal ne doit pas imposer un détour à cet usage.

## 4. Chaîne de traitement

La source fournit une piste audio sélectionnée à une chaîne commune. FFmpeg prépare les formats de travail nécessaires, que l'entrée soit audio ou vidéo. Aucune étape intermédiaire ne doit utiliser de compression avec perte.

- **Conversion / extraction** : piste sélectionnée → FFmpeg → format audio demandé.
- **Séparation** : piste sélectionnée → PCM de travail → Demucs → voix et fond → alignement → export.
- **Transcription directe** : piste sélectionnée → PCM adapté à Whisper → whisper.cpp → TXT et SRT.
- **Séparation puis transcription** : voix isolées alignées → PCM adapté à Whisper → TXT et SRT.

La préparation pour Whisper peut changer la fréquence d'échantillonnage et le nombre de canaux, sans changer la vitesse ni supprimer des intervalles. Les formats de travail sont internes et ne remplacent pas les choix d'export.

### Sorties

Exemple avec le préfixe `clip` :

| Fichier | Contenu |
|---|---|
| `clip-original.wav` | Bande-son complète de la piste sélectionnée |
| `clip-vocals.wav` | Voix estimées par Demucs, toutes personnes confondues |
| `clip-background.wav` | Fond estimé : musique, ambiances et bruitages |
| `clip-transcript.txt` | Texte global UTF-8, sans identification des interlocuteurs |
| `clip-subtitles.srt` | Sous-titres globaux horodatés dans la langue source |

Ces noms sont les conventions proposées pour les nouvelles actions. La conversion simple conserve ses conventions actuelles. L'utilisateur choisit la destination et les exports utiles. Les réglages de format sont réutilisés pour les exports audio ; ils ne s'appliquent pas aux TXT/SRT.

Le WAV PCM est le format recommandé pour le montage et pour la vérification précise des longueurs. Les formats compressés restent accessibles avec une information sur leurs limites temporelles éventuelles.

## 5. Exigence transversale : alignement pour le montage

### Référence temporelle

- Pour un fichier audio, l'origine est le début de la présentation de ce fichier.
- Pour une vidéo, l'origine est le début de sa présentation, même si la piste audio sélectionnée démarre plus tard.
- Les timestamps de conteneur non nuls ou négatifs doivent être interprétés relativement à cette origine ; ne pas utiliser aveuglément les valeurs brutes.
- Une sélection de piste ne doit pas modifier cette référence.

La durée cible correspond à la ligne temporelle de présentation de la source : vidéo pour une entrée vidéo, audio pour une entrée audio. Les cas de durée inconnue ou de conteneur incohérent doivent être détectés ; ne pas annoncer un alignement validé sans mesure fiable.

### Règles obligatoires

- Ne jamais couper automatiquement les silences initiaux, intermédiaires ou finaux.
- Conserver les offsets audio/image ; ajouter du silence initial si nécessaire et supprimer seulement les échantillons situés avant l'origine de présentation.
- Si la piste audio se termine avant la vidéo, compléter les exports audio de montage par du silence jusqu'à la durée cible.
- Gérer les discontinuités temporelles de la source sans concaténer les intervalles en supprimant les espaces.
- Compenser tout délai mesurable introduit par les moteurs, le découpage ou le rééchantillonnage.
- Pour les exports PCM à une même fréquence, viser exactement `arrondi(durée cible × fréquence)` échantillons pour chaque piste. Les écarts d'arrondi sont limités à la résolution d'un échantillon.
- Toute correction de longueur se fait après conservation de l'alignement. Ajouter du silence ou tronquer une fin excédentaire ne corrige pas un décalage interne.
- Aucun étirement temporel pour faire correspondre arbitrairement les longueurs.
- Les timestamps SRT utilisent cette même origine, y compris lors d'une transcription de voix séparées. Appliquer une compensation une seule fois.
- Une éventuelle détection d'activité vocale ne doit pas compacter la chronologie ; rétablir les positions d'origine si elle travaille par segments.

La longueur exacte du fichier audio et la précision de reconnaissance des limites de phrases sont deux critères distincts. Les timestamps estimés par Whisper peuvent nécessiter une relecture ; ne pas garantir une synchronisation labiale ou une précision à l'échantillon pour les sous-titres.

Pour les exports compressés, vérifier les timestamps de lecture et le traitement du délai d'encodage. Ne pas promettre une identité du nombre d'échantillons décodés dans tous les logiciels de montage. L'export PCM sert de référence.

### Mesures attendues

La validation mesure la longueur, mais aussi le début et plusieurs repères internes et finaux. Un fichier de bonne longueur avec un contenu décalé échoue à la validation. Pour les moteurs IA, comparer des repères sur les zones conservées et ne pas attendre une identité des formes d'onde séparées.

## 6. Modules facultatifs et installation isolée

### Stockage

Emplacement par défaut proposé :

```text
%LOCALAPPDATA%\OD.Converter\
    modules\
        separation\
            runtime\
            models\
            module.json
        transcription\
            runtime\
            models\
            module.json
    cache\
```

Ce dossier appartient exclusivement à OD.Converter. L'exécutable peut rester dans un emplacement non accessible en écriture. Aucun droit administrateur ne doit être requis pour l'installation standard.

- Demucs dispose d'un Python privé et de ses dépendances ; ne pas se baser sur un Python global ou un environnement appartenant à OD.VideoDub.
- whisper.cpp utilise son propre exécutable natif, ses bibliothèques et ses modèles ; aucun Python supplémentaire n'est requis pour ce module.
- Appeler les exécutables par leur chemin explicite. Ne pas modifier le PATH persistant ni les installations d'autres logiciels.
- Diriger aussi les téléchargements de modèles et caches des bibliothèques dans l'espace OD.Converter, via l'environnement du processus si nécessaire.
- Un mode portable à côté de l'EXE est une évolution possible, hors obligation de cette première version.

### Installation depuis le programme

L'utilisateur lance explicitement l'installation. Elle est silencieuse vis-à-vis des installateurs externes : aucun assistant ni question technique supplémentaire, avec progression visible dans OD.Converter.

L'application présente auparavant le module, son emplacement et le volume de téléchargement connu ou estimé. L'installation prépare le moteur, ses dépendances et le modèle, puis exécute un contrôle fonctionnel court.

- Utiliser un ensemble de versions compatibles fixé et documenté, pas les dernières versions découvertes sans validation.
- Choisir une distribution compatible CPU ; proposer ou détecter une accélération matérielle compatible sans exiger un GPU NVIDIA.
- Vérifier les téléchargements à l'aide des empreintes fixées pour la distribution retenue.
- Préparer l'installation dans une zone temporaire et n'activer le module qu'après validation. Une mise à jour ratée ne doit pas invalider une installation fonctionnelle.
- Gérer annulation, téléchargement interrompu, manque d'espace, absence de réseau et installation simultanée.
- Une réparation et une désinstallation ciblent seulement le module concerné. Les médias et exports utilisateur sont conservés.
- Conserver les licences et attributions des moteurs, distributions et poids retenus. Vérifier leur compatibilité de redistribution avant de figer les paquets.

### États exposés

`Not installed`, `Installing`, `Ready`, `Needs repair`.

La présence d'un dossier ne suffit pas à déclarer `Ready`. La vérification contrôle le runtime, les dépendances, le modèle et la capacité d'exécution. Un module endommagé désactive ses actions sans bloquer la conversion de base.

## 7. Réutilisation d'OD.VideoDub

Références consultées dans le projet voisin, à adapter sans introduire de dépendance d'exécution vers lui :

- `SourceSeparator.cs` : appel de Demucs avec `--two-stems vocals`, modèle configuré `htdemucs`, résultats `vocals.wav` et `no_vocals.wav`.
- `AudioExtractor.cs` : préparation audio par FFmpeg.
- `WhisperTranscriber.cs` : appel natif de whisper.cpp, SRT et texte global.
- `SetupService.cs` : téléchargement et préparation des outils.

Le modèle de transcription actuellement configuré dans OD.VideoDub est `large-v3-turbo`. C'est un candidat pour OD.Converter, à confirmer avec les contraintes CPU, mémoire, téléchargement et licence. Une version plus légère ou quantifiée peut être retenue après évaluation.

Ne pas reprendre telles quelles la dépendance CUDA, la recherche globale sur PATH, les hypothèses de langue française ni la logique de reprise basée seulement sur l'existence des fichiers. Les fichiers de travail d'une autre source ou d'autres paramètres ne doivent pas être réutilisés par erreur.

## 8. Architecture et compatibilité

Conserver la structure CLI existante et séparer les responsabilités : analyse des médias, préparation temporelle, orchestration, installation des modules, exécution des moteurs et export. Les choix et états de disponibilité doivent être partagés entre menu et CLI.

Des abstractions autour des processus, téléchargements, chemins et moteurs doivent permettre les tests sans télécharger les modèles. Les arguments de processus sont transmis séparément, sans construction d'une commande shell à partir de chemins utilisateur.

Le projet actuel est une application console. Il n'est pas nécessaire d'introduire une interface graphique ou une refonte MVVM pour cette extension. Le code et ses commentaires de documentation XML restent en anglais.

La syntaxe exacte des nouvelles commandes et options sera figée au lot 1 et documentée. Elle doit couvrir : piste choisie, action, exports, langue, destination, écrasement et gestion des modules. Les commandes `convert`, `probe`, `formats`, les options existantes et le lancement par chemin de fichier restent compatibles.

L'absence de piste audio, d'un module ou d'un modèle, un échec moteur et une annulation ont des messages distincts. Conserver les conventions de sortie existantes : 0 succès, 1 arguments/fichier, 2 outil requis indisponible, 3 traitement échoué, 130 annulation. Les nouveaux cas sont explicitement rattachés à ces conventions.

## 9. Fichiers, annulation et progression

- Ne jamais remplacer la source. Appliquer la politique d'écrasement existante à chaque résultat demandé.
- Vérifier toutes les collisions de noms avant un traitement coûteux et éviter les collisions entre sources de même nom.
- Utiliser un dossier de travail unique par opération ; isoler les exécutions concurrentes.
- Écrire les résultats dans des fichiers temporaires puis finaliser les fichiers complets. Ne pas présenter une sortie partielle comme réussie.
- Si une étape tardive échoue, indiquer les résultats déjà finalisés et ceux qui manquent ; retourner un code d'échec pour la demande globale.
- Arrêter les sous-processus lors d'une annulation et nettoyer les fichiers de travail selon une politique documentée.
- Afficher l'étape active et une progression lorsque le moteur la fournit ; ne pas inventer de pourcentage.
- Les traitements courants ne téléchargent aucun composant implicitement.

## 10. Validation et définition de terminé

Les critères détaillés et les preuves sont suivis par lot. La livraison complète exige :

- Conversion/extraction utilisables hors ligne avec le seul EXE publié sur une machine sans modules.
- Matrice audio/vidéo et modules absents/présents/endommagés validée.
- WAV de montage de longueur attendue et sans dérive sur plusieurs repères.
- TXT/SRT globaux en UTF-8, sans locuteurs, dans la langue source choisie ou détectée.
- Installation, réparation, annulation et désinstallation isolées testées.
- Régression des commandes existantes contrôlée et documentation utilisateur mise à jour.
- Distinction explicite entre tests simulés, contrôles avec de vrais modèles et validations sur une machine propre. Un contrôle non exécuté reste à faire.

Pour toute commande `dotnet build` ou `dotnet test`, ajouter explicitement `-m:1`.
