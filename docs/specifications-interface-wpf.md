# Spécification de l’interface WPF d’OD.Converter

Spécification proposée le 21 novembre 2026 à partir de `odconv` 1.3.2. Le contrat de référence est celui des commandes et des menus actuels. [Contrats CLI audio/vidéo](cli-audio-video.md) · [Documentation du dépôt](../README.md).

## 1. Objectif et périmètre

Ajouter une application de bureau Windows pour effectuer les mêmes opérations que `odconv.exe` depuis une interface graphique en anglais. Pour une même source et des paramètres équivalents, les deux applications doivent appliquer les mêmes règles de validation, choisir les mêmes moteurs et produire les mêmes types de fichiers, avec les mêmes noms, réglages d’encodage et repères temporels. La progression, les erreurs et les résultats sont présentés dans l’interface graphique.

La GUI est un projet annexe C# WPF sous .NET 11, avec MVVM Toolkit et ses générateurs par attributs. La première livraison cible Windows 11 x64 pour la GUI. La CLI conserve son point d’entrée, sa cible .NET 10, son mode interactif et ses contrats pour les scripts. Une même release GitHub fournit deux exécutables autonomes, `odconv.exe` et `odconv-gui.exe`.

## 2. Parcours et parité fonctionnelle

La fenêtre principale propose `Convert audio`, `Process audio / video`, `Inspect media` et `Optional modules`. Un menu `Help` donne accès à `Supported formats`, `User guide`, `About` et `Buy Me a Coffee`. Chaque parcours présente d’abord les réglages utiles dans le cas courant, puis les options avancées applicables. Les champs de chemin acceptent aussi la saisie directe. Au lancement avec un chemin de fichier en argument, la fenêtre ouvre `Convert audio` avec la source présélectionnée, comme le lancement direct de la CLI avec un fichier.

| Écran | Équivalent CLI | Exigences |
| --- | --- | --- |
| `Convert audio` | `convert`, menu classique, lancement direct | Choisir un fichier, plusieurs fichiers ou un dossier. En mode dossier, exposer le motif et la récursivité. Choisir le format, le fichier de sortie en mode fichier unique ou le dossier de sortie, et contrôler l’écrasement. `Preview command` affiche les commandes FFmpeg du `--dry-run` pour les entrées résolues, sans exécuter la conversion ni créer de dossier de sortie. Afficher le résultat par fichier pour un lot. |
| `Process audio / video` | `process`, menu guidé | Analyser une source audio ou vidéo, sélectionner une piste, choisir l’action, les exports et leur destination, puis vérifier le récapitulatif. Afficher clairement les actions indisponibles et les modules requis, avec un accès à `Optional modules`. |
| `Inspect media` | `probe`, `info`, `probe --json` | Afficher les métadonnées, les pistes, les durées, les décalages temporels, la fiabilité de ces informations et les avertissements. Permettre de consulter et copier le JSON brut fourni par FFprobe. Une source sans audio reste analysable et le traitement audio est désactivé. |
| `Optional modules` | `modules list/status/verify/install/repair/uninstall` | Afficher les états `Not installed`, `Installing`, `Ready` et `Needs repair` pour `Separation` et `Transcription`. Donner accès aux opérations `Refresh`, `Verify`, `Install`, `Repair` et `Uninstall`, selon l’état du module. |
| `Help` | `formats`, `guide`, `guide --extract`, `--help`, `--version` et menu de soutien | Afficher les formats issus de `FormatRegistry`, les descriptions des réglages et la version de l’application. Extraire `odconv-user-guide.html` à côté de l’EXE, afficher son chemin et permettre l’ouverture du guide EN/FR hors ligne dans le navigateur par défaut. Ouvrir le lien de soutien existant sur demande. |

### Conversion classique

- Les onze formats sont issus de `FormatRegistry`. Les alias, extensions, valeurs par défaut, arguments FFmpeg et contraintes proviennent du registre, du validateur et du générateur d’arguments existants. Exposer le débit, la qualité VBR, la fréquence d’échantillonnage, le nombre de canaux, le mono prioritaire, la profondeur PCM et la compression FLAC selon le format. Une valeur `Default` ou `Keep source` conserve l’effet d’une option omise lorsque le moteur le prévoit.
- Réutiliser l’expansion des dossiers, le motif par défaut `*` et les règles de résolution des sorties de `convert`, y compris le traitement du chemin relatif au répertoire de travail avec `--output-dir`. Afficher les chemins effectivement calculés avant le lancement. Conserver les conventions du menu classique pour la suggestion initiale d’un fichier unique, notamment le suffixe mono déjà présent et le mono proposé pour une source à un canal. Les différences existantes entre les deux points d’entrée doivent être couvertes par des tests de caractérisation.
- Tous les parcours concernés exposent un champ avancé pour désigner `ffmpeg.exe` ou son dossier. Utiliser `FfmpegLocator` pour l’ordre de découverte, la variable `OD_CONVERTER_FFMPEG` et le compagnon `ffprobe.exe`.

### Traitement audio et vidéo

1. Afficher les pistes dans l’ordre retourné par FFprobe. Présélectionner la première piste audio et afficher son index absolu, son codec, sa langue, son titre, ses canaux et sa durée lorsqu’ils sont connus. Transmettre l’index absolu FFprobe au moteur, distinct de l’ordre d’affichage.
2. Proposer les quatre actions et leurs exports. Les sélections autorisées et les valeurs initiales viennent de `ProcessingConventions`.

   | Action affichée | Modules requis | Exports proposés |
   | --- | --- | --- |
   | `Extract soundtrack` | Aucun | `original` par défaut |
   | `Separate vocals and background` | `Separation` | `vocals` et `background` par défaut, `original` en option |
   | `Transcribe` | `Transcription` | `transcript` en TXT et `subtitles` en SRT par défaut |
   | `Separate and transcribe` | Les deux | `vocals`, `background`, `transcript` et `subtitles` par défaut, `original` en option |

3. Afficher la langue source pour les actions de transcription, avec `Auto` par défaut et la possibilité de saisir un code de deux lettres accepté par la CLI. L’action combinée transcrit les voix isolées. Les sorties TXT/SRT restent globales, en UTF-8 et dans la langue source.
4. Préremplir le dossier et le préfixe à partir de la source, puis proposer les réglages d’encodage uniquement si un export audio est sélectionné. Le réglage audio par défaut est WAV. Le récapitulatif affiche la piste, les exports et leurs chemins calculés. La préparation, la mesure PCM, les avertissements de délai des codecs et les règles de rejet des chronologies sont ceux du moteur commun. [Politique de préparation audio](preparation-audio.md).

### Modules facultatifs

La CLI et la GUI partagent `%LOCALAPPDATA%\OD.Converter\modules`, les recettes épinglées, les verrous interprocessus et les générations validées. Un module installé ou réparé dans une interface est utilisable dans l’autre après actualisation. Les actions dépendantes recontrôlent la disponibilité au lancement. Si une autre instance détient un verrou, la GUI affiche l’état remonté par le gestionnaire et propose d’actualiser.

`Install` et `Repair` demandent une confirmation après affichage du module, de sa version, de son emplacement, de son profil CPU et du volume total des transferts. `Uninstall` demande confirmation pour le seul module visé. La consultation et la vérification sont hors ligne. Seules une installation ou une réparation demandées par l’utilisateur peuvent déclencher le téléchargement des modules. `Convert audio` et `Extract soundtrack` fonctionnent hors ligne avec les outils intégrés. [Cycle de vie des modules](modules-optionnels.md) · [Distributions et licences](distributions-modules.md).

## 3. Exécution, erreurs et annulation

- Avant un traitement, afficher les destinations et signaler ensemble les fichiers existants. L’utilisateur doit confirmer explicitement leur remplacement. Recalculer le plan après toute modification d’un paramètre et revérifier les destinations avant exécution. Un nouveau conflit déclenche une nouvelle décision ou remonte comme erreur selon le parcours, sans autorisation implicite d’écrasement. Les règles existantes de protection du fichier source restent applicables.
- La fenêtre reste réactive pendant l’analyse, les vérifications, les téléchargements et les traitements. L’interface affiche l’étape en cours, par exemple `Analyzing source`, `Extracting audio` ou `Transcribing`. Afficher une quantité mesurée lorsqu’elle existe, comme le nombre de fichiers traités ou le nombre d’octets de l’artefact en cours de téléchargement. Pour les autres étapes, utiliser un indicateur indéterminé.
- Une opération active à la fois par fenêtre empêche les doubles soumissions et fige les paramètres de la demande en cours. `Cancel` propage `CancellationToken` au moteur et à ses processus enfants. En cas de fermeture pendant une opération, demander confirmation et attendre la fin de l’arrêt et du nettoyage avant de fermer la fenêtre.
- Afficher les erreurs, avertissements et résultats par fichier avec le détail technique disponible. Pour `process`, distinguer les exports finalisés des exports demandés mais absents, y compris après échec ou annulation. Les fichiers déjà finalisés et les anciennes destinations dont le remplacement a échoué sont préservés selon les règles d’export existantes. Pour `convert`, reprendre les règles actuelles de poursuite du lot et afficher un succès uniquement pour une conversion achevée dont la sortie est présente.
- Les résultats permettent de copier le chemin, d’ouvrir le fichier ou d’afficher son dossier. Les cinq états présentés à l’utilisateur sont `Ready`, `Running`, `Completed`, `Failed` et `Cancelled`. Les avertissements et sorties partielles restent visibles avec l’état final correspondant.

## 4. Thème sombre et ergonomie Windows

- Appliquer le thème Fluent sombre au niveau de `App.xaml` (`ThemeMode="Dark"`). Les fenêtres principales et secondaires, les confirmations, les erreurs, les menus et les contrôles de l’application doivent être sombres dès leur affichage, y compris sous un thème Windows clair. Centraliser les couleurs complémentaires et états interactifs dans des dictionnaires de ressources WPF. [Thème Fluent WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net90).
- Le chrome des fenêtres de l’application fait partie de la recette. Vérifier le fond de la barre de titre, le texte, la bordure et la lisibilité des boutons système, en fenêtre active et inactive. Conserver le chrome natif et compléter le thème Fluent, si nécessaire, par une adaptation DWM centralisée après création du `HWND`. Sous Windows clair, utiliser au besoin les attributs de couleur de la barre de titre, du texte et de la bordure. [Thème de fenêtre WPF](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.thememode?view=windowsdesktop-11.0) · [Attributs DWM](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute).
- Conserver le déplacement, le redimensionnement, le menu système au clavier et l’ancrage Windows pour les fenêtres redimensionnables. Les confirmations de l’application utilisent des dialogues WPF sombres. Les sélecteurs de fichiers et de dossiers fournis par Windows et le navigateur utilisent leur propre thème.
- Vérifier l’ordre de tabulation, le focus visible, les noms accessibles, les contrastes et la lisibilité aux échelles 100 %, 150 % et 200 %. Les états associent texte et couleur. Respecter le mode Contraste élevé de Windows. Réutiliser l’icône OD.Converter pour l’exécutable et la fenêtre.

## 5. Architecture et réutilisation

| Projet | Cible | Responsabilité |
| --- | --- | --- |
| `src/OD.Converter.Core/OD.Converter.Core.csproj` | `net10.0` | Bibliothèque partagée pour les modèles, la validation, les formats, la planification, l’exécution audio/vidéo, FFmpeg/FFprobe, le gestionnaire de modules et la ressource du guide. |
| `src/OD.Converter/OD.Converter.csproj` | `net10.0`, `win-x64`, `Exe` | `odconv.exe`, analyse des arguments, menus Spectre.Console, rendu des résultats et codes de sortie existants. |
| `src/OD.Converter.Gui/OD.Converter.Gui.csproj` | `net11.0-windows`, `win-x64`, `WinExe`, `UseWPF=true` | `odconv-gui.exe`, vues XAML, ViewModels, dialogues de l’application et intégration au bureau Windows. |

La CLI et la GUI référencent `OD.Converter.Core`. Extraire progressivement le code existant en conservant ses espaces de noms et ses contrats. Déplacer d’abord `ConversionOptions`, `MediaSource`, `ProcessingRequest`, `ProcessingPlan`, `ProcessingResult`, `FormatRegistry`, les validateurs et les services. Extraire ensuite la planification et l’orchestration aujourd’hui présentes dans `ConvertCommand` et `ProcessCommand`, afin que les deux façades leur transmettent des demandes typées et reçoivent des résultats structurés. L’analyse des arguments et les invites restent dans la CLI. Le projet GUI s’exécute sans `odconv.exe` et utilise les services en processus. Les outils natifs continuent d’être lancés par les runners existants.

Les services partagés remontent les étapes, les diagnostics et les résultats via des valeurs typées et une progression injectée, en réutilisant `ProcessingResult` et `ModuleProgress`. Adapter les écritures `Console` encore présentes dans les services et la sortie des outils natifs à ce mécanisme. La CLI continue de rendre ses messages sur les flux attendus, notamment le JSON de `probe --json`. La GUI reçoit les événements de l’opération en cours et met à jour ses collections sur le Dispatcher WPF. Les processus natifs sont lancés sans fenêtre de console.

Les `ViewModels` sont des classes `partial` qui héritent d’`ObservableObject` ou d’`ObservableValidator`. Utiliser `[ObservableProperty]`, `[NotifyCanExecuteChangedFor]` et `[RelayCommand]` pour la liaison et les actions. Les commandes longues retournent `Task`, acceptent un `CancellationToken` et génèrent la commande d’annulation avec `IncludeCancelCommand = true`. Les ViewModels orchestrent les étapes de présentation et délèguent les règles métier au cœur partagé. `App.xaml.cs` compose les dépendances à l’entrée et le code-behind se limite au comportement des vues et de leurs fenêtres. Le code et les commentaires de documentation XML sont en anglais. [Générateurs MVVM Toolkit](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/overview) · [Commandes asynchrones et annulation](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/relaycommand).

## 6. Compilation et release

- Ajouter le cœur partagé à `OD.Converter.slnx`. Construire la GUI par son propre `.csproj` avec le SDK .NET 11. Le parcours CLI seul reste compilable avec le SDK .NET 10. Le poste de release est Windows x64, équipé du SDK .NET 11 et des composants nécessaires pour compiler, tester et publier les deux cibles. [SDK .NET 11](https://dotnet.microsoft.com/en-us/download/dotnet/11.0).
- Publier par défaut les deux EXE via `build.ps1` ou `publish.bat` dans le même dossier, sous les noms `publish/odconv.exe` et `publish/odconv-gui.exe`. L’option `-CliOnly` conserve la publication CLI seule. Une erreur de build, de publication ou de validation de l’une des deux variantes fait échouer la préparation de la release commune.
- Publier les deux applications en Release, `win-x64`, `SelfContained=true`, `PublishSingleFile=true` et `PublishTrimmed=false`. Chaque EXE doit fonctionner une fois copié seul dans son dossier. Les deux publications incluent les mêmes binaires FFmpeg/FFprobe et leur licence depuis `src/OD.Converter/tools`. Conserver le guide et les ressources des modules dans l’assembly qui les lit, avec des noms logiques stables. Vérifier sur les EXE publiés la résolution effective des outils embarqués et l’accès aux ressources. [Publication .NET en fichier unique](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/).
- Donner aux deux binaires la même version produit issue d’une source commune. Joindre les deux EXE Windows x64 à la même release GitHub, avec leurs SHA-256. Les notes indiquent quel fichier télécharger pour un usage terminal ou graphique et rappellent que les modules CPU sont facultatifs.

Commandes de contrôle prévues après l’intégration du projet annexe.

```powershell
dotnet build OD.Converter.slnx -c Release -m:1
dotnet test OD.Converter.slnx -c Release -m:1 --no-build
dotnet build src/OD.Converter.Gui/OD.Converter.Gui.csproj -c Release -r win-x64 -m:1
```

## 7. Recette et critères d’acceptation

| Vérification | Résultat attendu |
| --- | --- |
| Régression CLI | La suite existante passe. Les commandes et menus conservent leurs options, alias, sorties, codes de retour et comportements sur les scénarios déjà couverts. Le validateur de livraison CLI passe avec l’EXE publié. |
| Conversion | Rejouer via CLI et GUI une conversion simple, un lot de fichiers et un dossier récursif avec motif. Comparer entrées résolues, destinations, commandes du `dry-run`, caractéristiques produites et résultats par entrée, y compris si une destination existe ou si une conversion échoue en milieu de lot. |
| Analyse et traitement | Utiliser une vidéo à plusieurs pistes dont les index FFprobe diffèrent de leur ordre audio, une vidéo muette, un WAV PCM et un média dont la chronologie est inexploitable. Vérifier le JSON brut, les avertissements et le rejet de la chronologie invalide. Rejouer les quatre actions sur des médias de référence avec les modules requis. Comparer les exports sélectionnés, les métadonnées, l’alignement PCM et les horodatages SRT selon les critères des traitements existants. |
| Échec et annulation | Tester un refus d’écrasement, une destination invalide, une annulation pendant FFmpeg, un traitement IA et une installation de module. Vérifier la réactivité, l’arrêt des processus, le nettoyage prévu et l’exactitude du bilan des fichiers, y compris les exports réellement finalisés avant une interruption. |
| Modules partagés | Installer avec la CLI, traiter avec la GUI, puis vérifier le parcours inverse. Vérifier que les opérations concurrentes respectent les verrous et qu’une réparation ou une désinstallation ciblée laisse l’autre module utilisable. |
| Thème et accessibilité | Sous Windows clair puis sombre, contrôler le chrome et les dialogues propres à l’application. Tester les états actif et inactif, les boutons système, le clavier, le redimensionnement et les échelles d’affichage prévues. |
| Paquet de release | Tester les deux EXE exacts de la release sur une VM Windows 11 x64 propre sous compte standard, sans .NET, FFmpeg ni Python installés. Copier chaque EXE seul, lancer conversion, analyse, extraction et guide hors ligne, puis installer et utiliser explicitement les modules depuis la GUI. Consigner les SHA-256 et les résultats. [Procédure de validation existante](validation-livraison.md). |

## 8. Ordre de réalisation

1. Caractériser les contrats CLI, extraire le cœur partagé et faire repasser les tests existants. Vérifier la publication CLI dès le déplacement des ressources.
2. Valider un premier exécutable WPF publié avec le thème sombre, le chrome sombre sous Windows clair et une conversion hors ligne avec FFmpeg embarqué.
3. Raccorder les parcours de la matrice, la progression, l’annulation et la gestion des modules sur les services partagés. Exécuter la recette de parité sur des fichiers représentatifs.
4. Préparer la publication commune, valider les deux EXE sur Windows propre et documenter le choix de l’exécutable dans le README et les notes de release.
