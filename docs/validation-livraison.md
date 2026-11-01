# Validation de livraison audio/vidéo

**Décision du 1er novembre 2026 : lot 7 clôturé sur le périmètre validé.**
L'utilisateur reporte sans échéance les validations sur Windows vierge, plusieurs
interlocuteurs, changements de langue et autres médias représentatifs. Les
procédures ci-dessous restent disponibles pour une reprise ultérieure ; leurs
exigences de validation complète ne bloquent plus la clôture actuelle. Aucun
contrôle reporté n'est considéré réussi. Les écoutes de l'épisode 17 sont
acceptées ; voir le [suivi et les reports](suivi-audio-video.md).

Cette recette complète les [preuves par lot](suivi-audio-video.md). Elle sépare
les tests automatisés, les mesures sur l'EXE publié, les moteurs CPU réels et
la recette sur Windows vierge. Aucun de ces contrôles ne vaut les trois autres.

## Construire et publier

Depuis la racine du dépôt, avec le SDK .NET du projet :

```powershell
dotnet build OD.Converter.slnx -c Release -m:1
dotnet test OD.Converter.slnx -c Release -m:1 --no-build --no-restore --logger "trx;LogFileName=release-tests.trx" --results-directory out/release-tests
./build.ps1 -Configuration Release -Output publish
Get-FileHash publish/odconv.exe -Algorithm SHA256
```

Le script de publication contrôle les codes de sortie de `--help` et `formats`.
Le résultat local est `publish/odconv.exe`. Une publication locale ne crée pas
de release GitHub. Conserver l'empreinte avec le rapport pour identifier l'EXE
effectivement testé. Les avertissements NU1900 signifient que l'audit NuGet n'a
pas pu joindre son service ; ils ne constituent pas un audit réussi.

## Mesurer l'EXE autonome

```powershell
dotnet build tools/OD.Converter.ReleaseValidation -c Release -m:1
& tools/OD.Converter.ReleaseValidation/bin/Release/net10.0/win-x64/OD.Converter.ReleaseValidation.exe publish/odconv.exe src/OD.Converter/tools/ffmpeg.exe out/release-validation
```

Le dernier dossier doit être nouveau ou vide. Prévoir environ 3 Gio libres.
Le validateur copie uniquement l'EXE dans un chemin contenant espaces, accent,
esperluette et crochets. Ses processus utilisent un PATH limité au répertoire
système Windows, sans override FFmpeg, Python global ou racine .NET explicite.
Le répertoire d'extraction du bundle et les temporaires sont privés à la recette.
Les installations de la machine hôte restent présentes : ce contrôle ne simule
pas une désinstallation de Python, du runtime Visual C++ ou des modules.

FFmpeg fourni au validateur génère et décode les fixtures par chemin absolu.
Il n'est pas ajouté au PATH de l'application. Le contrôle `--dry-run` exige que
l'application choisisse effectivement son FFmpeg extrait du bundle, plutôt
qu'une copie globale. Aucun téléchargement de module n'est lancé.

Contrôles :

- Aides racine, `process`, `modules`, version et formats.
- MP4/MPEG-4/ALAC, MKV/FFV1/PCM, MOV/MPEG-4/PCM, AVI/MPEG-4/PCM et WebM/VP8/Opus.
- Repères à 0,75/2/3,25 s après retard de 500 ms, audio plus court que l'image,
  sortie mono de 240 000 frames à 48 kHz. L'AVI est testé sans offset : le muxeur
  de cette fixture ne conserve pas le décalage `-itsoffset`.
- Tolérance des repères : MP4/MOV/AVI 1 échantillon, MKV 48 (timestamp milliseconde),
  WebM/Opus 480 (10 ms pour ce codec avec perte). La longueur PCM reste exacte.
- Source PCM de deux heures à 8 kHz rééchantillonnée à 22,05 kHz : exactement
  158 760 000 frames ; cinq repères à 0,25/1 800/3 600/5 400/7 199,75 s,
  tolérance d'un échantillon. Les fenêtres de mesure sont lues en flux.
- Conversion classique WAV→MP3 et `probe` ; refus d'une collision (code 1),
  empreinte de l'export existant inchangée.

`results.json` consigne l'empreinte de l'EXE, l'hôte et chaque mesure ou échec.
`commands.log` conserve arguments, sorties et codes des processus. Le validateur
retourne un échec dès qu'un contrôle échoue. Les fichiers restent disponibles
dans le dossier de recette pour inspection ; aucun nettoyage récursif automatique.
Les fixtures synthétiques ne prouvent pas la prise en charge de tous les codecs,
des fichiers endommagés, ni l'alignement des moteurs IA sur deux heures.

## Moteurs réels et qualité

### Recette IA de l'EXE publié sur un média représentatif

`tools/OD.Converter.PublishedAiValidation` lance réellement une copie de l'EXE
publié, via sa CLI. Le validateur .NET s'exécute sur l'hôte de développement ;
ses mesures ne constituent pas une recette Windows vierge.

```powershell
dotnet build tools/OD.Converter.PublishedAiValidation -c Release -m:1
& tools/OD.Converter.PublishedAiValidation/bin/Release/net10.0/win-x64/OD.Converter.PublishedAiValidation.exe publish/odconv.exe '<module separation existant>' '<module transcription existant>' '<video locale>' out/published-ai
```

Lancer depuis la racine du dépôt, avec une vidéo de plus de 90 s. Les chemins
de modules désignent les dossiers contenant `active.json`, pas leurs racines
parentes. Le dossier de résultats doit être nouveau ou vide. Les modules du
compte doivent être absents : l'outil refuse de remplacer des modules personnels.
Il copie uniquement la génération active existante de chaque module dans
`%LOCALAPPDATA%/OD.Converter/modules`, puis exige que l'EXE publié la vérifie
(inventaire, intégrité et contrôle fonctionnel). Aucun téléchargement ni
installation depuis les distributions n'est exercé par cette recette.

Trois extraits vidéo de 30 s sont générés avec le FFmpeg explicite du dépôt :
début, milieu et fin. Ils conservent le contenu audio sélectionné en ALAC,
48 kHz stéréo ; la vidéo est réduite pour l'inspection. Pour chaque extrait,
l'EXE transcrit directement, puis sépare et transcrit les voix, en français,
avec tous les exports. Le PATH des processus est limité au système Windows,
les overrides FFmpeg/Python/.NET sont retirés et bundle/temporaires sont privés.

Les contrôles portent sur codes de sortie, cohérence TXT/SRT, timestamps,
**1 440 000 frames stéréo à 48 kHz** pour chaque export audio, valeurs finies,
et corrélation original / somme voix + fond dans trois fenêtres actives.
Les fenêtres sont choisies parmi le début, le milieu et la fin de chaque extrait ;
elles ne sont pas des repères synthétiques ni une preuve de qualité de chaque
stem. Leur décalage doit rester dans un échantillon.

Les copies sont désinstallées par la CLI dans le bloc de nettoyage, y compris
après échec. La première désinstallation vérifie que l'autre module fonctionne
encore. Les médias sources et les exports finalisés sont protégés par empreinte.
Des fichiers de verrou inactifs peuvent rester dans le dossier privé ; aucun
dossier parent n'est supprimé. Un échec de nettoyage reste une erreur consignée.
`results.json` et `commands.log` identifient EXE, source, générations copiées,
extraits, commandes, mesures, sous-titres et nettoyage. L'écoute et la relecture
restent obligatoires, et les indicateurs de validation Windows vierge et humaine
restent faux.

Après une recette réussie, préparer une fiche locale avec les transcriptions
directes/après séparation et les liens vers vidéo, voix et fond :

```powershell
./tools/OD.Converter.PublishedAiValidation/New-Review.ps1 -ResultsDirectory out/published-ai
```

Le script crée `Review.md` et refuse d'écraser une fiche existante. Il laisse
les observations et la décision humaine à compléter. Les timestamps des SRT
concernent chaque extrait, avec son décalage source indiqué dans la fiche ; ils
ne constituent pas une transcription complète de la vidéo d'origine.

Pour tester la transcription directe du **fichier complet**, sans séparation
ni génération d'extraits, ajouter `--full-transcription` :

```powershell
& tools/OD.Converter.PublishedAiValidation/bin/Release/net10.0/win-x64/OD.Converter.PublishedAiValidation.exe publish/odconv.exe - '<module transcription existant>' '<video locale>' out/published-full-transcription --full-transcription
./tools/OD.Converter.PublishedAiValidation/New-Review.ps1 -ResultsDirectory out/published-full-transcription
```

Le tiret remplace le chemin Demucs, ignoré dans ce mode. Seule la génération
Whisper est copiée, vérifiée et désinstallée. L'index absolu de la première piste
audio et la durée complète sont lus par FFprobe ; les TXT/SRT utilisent la
chronologie du fichier d'origine. `Mode: FullTranscription` est consigné dans le
rapport. Les contrôles de structure, de cohérence TXT/SRT, d'empreintes et de
nettoyage restent identiques. La fiche de relecture pointe vers la vidéo source
complète et n'annonce pas de stems absents. Une sortie non vide et structurelle
ne prouve toujours pas que toutes les paroles ont été reconnues.

Pour le mode complet, `Review.md` indique le nombre de sous-titres, la première
et la dernière borne, et les intervalles sans sous-titre d'au moins deux
secondes, y compris les silences de début/fin. Il s'agit de points d'écoute :
une pause peut être normale et l'absence de grande plage vide ne prouve pas
l'exhaustivité de la reconnaissance. Consigner séparément les erreurs lexicales
repérées lors de la lecture et leur vérification à l'écoute.

### Préparer les écoutes restantes après une relecture ciblée

Quand les deux recettes portent sur la même source et le même EXE, préparer un
kit d'écoute depuis les rapports existants et `human-review.json` :

```powershell
./tools/OD.Converter.PublishedAiValidation/New-ListeningKit.ps1 -FullResultsDirectory out/lot7-validation/episode17-full-transcription-01 -ExcerptResultsDirectory out/lot7-validation/episode17-published-ai-02 -OutputDirectory out/lot7-validation/episode17-listening
```

Le script fonctionne sous Windows PowerShell 5.1 et exige un dossier nouveau.
Il contrôle les empreintes de la source, des exports bruts/révisés et des trois
vidéos mesurées avant copie. Il conserve les verdicts antérieurs et prépare les
comparaisons vidéo/original/voix/fond du début, milieu et fin, avec TXT/SRT directs
et après séparation. Il extrait aussi un WAV PCM stéréo 48 kHz de la fin sans
sous-titre, précédée de huit secondes de contexte au maximum, depuis la piste
explicitement sélectionnée dans la recette complète. Aucun moteur IA ne tourne.

`Review.md` donne les liens et repères ; `listening-review.json` contient les
empreintes des 32 fichiers du kit pour cette recette, la commande FFmpeg et cinq
verdicts en attente : fin sans sous-titre, transcription globale et trois écoutes
de séparation. Consigner auteur, date, observations et décision pour chaque
contrôle après écoute. La vidéo complète reste liée à son chemin local : la
copier séparément en cas de transfert du kit. Les rapports et verdicts d'origine
restent inchangés. `HumanReviewCompleted` et `IsCleanWindows` restent faux ; ni
les autres médias représentatifs ni Windows vierge ne sont validés par ce kit.

Reprendre les recettes documentées de [cycle des modules](distributions-modules.md#reproduire-la-validation-des-installations),
de [séparation](separation-audio.md#recette-reproductible) et de
[transcription](transcription-audio.md). Les mesures CPU des lots 3–5 restent
leurs preuves propres ; le validateur de livraison n'exécute pas d'inférence IA.
La maintenance des versions, URL, empreintes et conditions de redistribution
reste à revoir avant une diffusion publique ; le catalogue fixé n'est pas une
affirmation sur les dernières versions disponibles.

Écouter original/voix/fond sur musique, interview et ambiance représentatives,
en particulier attaques, voix faibles et résidus. Relire TXT/SRT en regard de
la source, avec vérification du début, milieu et fin, omissions, hallucinations
et limites de phrases. Consigner média, langue, durée, réglages et anomalies.
La fixture bilingue en mode `auto` a omis une voix française : la qualité sur
changements de langue n'est pas validée. Le VAD réduit les hallucinations sur
bruit mais peut omettre de la parole. Les lots 4–5 restent à vérifier.

## Recette Windows vierge à exécuter

### Préparer le kit transférable

Les scripts de [`tools/clean-windows`](../tools/clean-windows/Review.md) préparent
et exécutent cette recette sans SDK .NET ni outil de mesure dans la VM.
Sur l'hôte, après publication :

```powershell
./tools/clean-windows/New-ValidationKit.ps1 -OutputDirectory out/clean-windows-kit -SpeechSource 'E:\Medias\interview-musique.wav'
```

`SpeechSource` est facultatif pour les seuls contrôles hors ligne, obligatoire
pour la phase des moteurs. Choisir un contenu représentatif dont l'utilisation
est autorisée ; il sera copié dans le kit. Le dossier de sortie doit être nouveau
ou vide. Le kit contient uniquement l'EXE publié, deux fixtures (WAV avec trois
repères et MP4 avec retard de 500 ms), l'éventuel média représentatif, le script
invité, une fiche de relecture et `kit.json` (tailles et SHA-256). Aucun FFmpeg,
Python, runtime, SDK ou cache externe n'y est ajouté. Le FFmpeg de l'hôte sert
uniquement à générer les fixtures.

Copier le kit dans une VM Windows x64 vierge sous compte standard. Les scripts
sont compatibles Windows PowerShell 5.1. Depuis le dossier du kit, après avoir
déconnecté le réseau de la VM dans l'hyperviseur :

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Invoke-Validation.ps1 -Phase Offline -OutputDirectory 'C:\Validation results\offline' -OfflineNetworkConfirmed
```

La phase hors ligne contrôle aides, formats, analyse, extraction, conversion,
collision avec empreinte conservée, absence des modules et code 2 avec commande
d'installation adaptée. Aucun téléchargement n'est demandé. Le script ne coupe
pas le réseau : l'option consigne la confirmation de l'opérateur.

Après reconnexion du réseau, avec un média représentatif dans le kit :

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Invoke-Validation.ps1 -Phase Modules -OutputDirectory 'C:\Validation results\modules' -Language fr
```

Cette phase installe, vérifie, exécute et désinstalle chaque module séparément,
contrôle l'absence de l'autre, puis installe les deux, exécute le traitement
combiné, répare et vérifie les deux. La désinstallation ciblée conserve l'autre
module, les sources et les exports (empreintes). Les deux modules sont absents
à la fin du parcours réussi. En cas d'échec, les preuves et l'état des modules
sont conservés pour diagnostic ; aucun nettoyage destructif automatique.
Prévoir plusieurs Gio et le temps d'inférence CPU. Refaire ensuite la phase
`Offline`, réseau déconnecté, avec un nouveau dossier de résultats.

Rapporter les dossiers de résultats sur l'hôte, puis mesurer chaque dossier :

```powershell
./tools/clean-windows/Test-ValidationResults.ps1 -KitDirectory out/clean-windows-kit -ResultsDirectory out/guest-offline
```

Le vérificateur lie le rapport au kit et à l'EXE, vérifie les empreintes des
exports transférés, le PCM mono 32 bits à 48 kHz, **240 000 frames** et les repères
à **0,75/2/3,25 s**, tolérance d'un échantillon. `measurements.json` conserve les
mesures ou l'échec ; les fichiers décodés temporaires sont supprimés. Les journaux
invités sont `commands.jsonl` et `results.json`, avec codes de sortie, identité
de l'hôte, jeton, outils globaux visibles, empreintes et résultats.

Le script refuse un jeton administrateur, les outils globaux visibles dans le
PATH et des modules préexistants. Ces contrôles ne prouvent pas la provenance
d'une VM ni l'absence de runtimes hors PATH. Le rapport laisse donc
`CleanWindowsVerified` et `HumanReviewCompleted` à `false` : la provenance,
l'isolation réseau, les licences, les annulations et la relecture restent à
consigner par l'opérateur dans la fiche `Review.md` et le suivi. La réparation
automatique porte sur une génération saine ; elle ne simule pas une corruption
ou une interruption. `-DevelopmentHost` autorise seulement un essai local de la
phase `Offline`, explicitement identifié comme tel, sans valider Windows vierge
ou une coupure réseau. Aucun script ne modifie les fonctionnalités Windows ou
ne provisionne une VM.

### Contrôles de l'environnement et recette humaine

Utiliser une VM Windows x64 neuve ou Windows Sandbox avec utilisateur standard,
sans Python, FFmpeg, .NET ni modules installés. Consigner la version de Windows,
la mémoire, le CPU, le jeton non administrateur et l'empreinte de l'EXE. Copier
uniquement `odconv.exe`, les fixtures et cette liste de contrôle ; aucun runtime
ou DLL de développement. Ne pas réutiliser les modules du compte de développement.

1. Couper le réseau. Exécuter `--help`, `formats`, `modules list`, `probe` et une
   conversion WAV→MP3. Les deux modules doivent être `Not installed`.
2. Exécuter `process source.mp4 --action extract --audio-track 1 --bit-depth 32`.
   Vérifier les exports avec les mêmes mesures de frames et repères ; conserver
   les fichiers pour mesure sur l'hôte si aucun outil de mesure n'est dans la VM.
3. Vérifier que `separate` et `transcribe` retournent 2, annoncent la bonne
   commande d'installation et ne téléchargent rien.
4. Rétablir le réseau. Installer explicitement `separation`, vérifier son état,
   exécuter une séparation puis le désinstaller. Extraction et transcription
   absente doivent garder leurs comportements respectifs.
5. Installer explicitement `transcription` seul, vérifier, transcrire, relire le
   TXT/SRT et désinstaller. Aucun Python global ne doit être nécessaire.
6. Installer les deux, exécuter `separate-transcribe`, puis tester réparation,
   annulation et désinstallation ciblée avec conservation des médias et exports.
7. Refaire extraction et conversion hors ligne après désinstallation. Vérifier
   les licences conservées dans les payloads et la licence FFmpeg du bundle.

Archiver les sorties, mesures et observations avant de fermer l'environnement
jetable. Cette recette reste nécessaire pour revendiquer une validation sur
Windows vierge ; elle est reportée et ne bloque plus la clôture du lot 7 décidée
par l'utilisateur le 1er novembre 2026.
