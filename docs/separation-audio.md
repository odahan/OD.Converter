# Séparation Demucs — lot 4

`process --action separate` traite une piste sélectionnée d'une source audio ou
vidéo. Installer explicitement le module avec `odconv modules install separation`.
Aucune installation ni acquisition de modèle n'a lieu pendant le traitement.

```powershell
odconv process "clip.mp4" --action separate --audio-track 2 --exports original,vocals,background --to wav --bit-depth 24 --output-dir "D:\Exports"
```

Les exports par défaut sont `vocals,background`. Les paramètres de conversion
s'appliquent aux trois exports audio ; sans fréquence ou canaux explicites, tous
reprennent ceux de la piste source, y compris une source mono. Le modèle travaille
en interne à 44,1 kHz stéréo. Le WAV PCM est la référence de montage ; les formats
compressés peuvent ajouter un délai propre au codec ou au logiciel de lecture.
Une entrée multicanal est réduite en stéréo pour Demucs ; un export multicanal
réexpanse cette estimation et ne reconstitue pas les canaux séparés du mixage
surround. L'export `original` provient du PCM source et conserve ses canaux.

## Adaptation et chronologie

L'adaptateur reprend la logique deux stems d'OD.VideoDub : voix et somme des
autres sources Demucs. Il utilise directement l'API Demucs 4.0.1, `load_model`
et `apply_model`, plutôt que la découverte de modèles de sa CLI. Cela permet de
charger uniquement le checkpoint local vérifié, avec PyTorch 2.6, et de lire et
écrire avec SoundFile sans recherche d'un FFmpeg externe sur le PATH.

L'inférence utilise exclusivement le CPU, deux threads, les segments du modèle,
un recouvrement de 25 % et `shifts=0`. Ce dernier choix rend la recette temporelle
reproductible sans augmentation par décalage aléatoire. La qualité peut différer
de la configuration CLI Demucs avec augmentation. Les quatre estimations sont
calculées ; seules les voix et la somme des autres sources sont exposées. Aucun
réglage de gain indépendant n'est appliqué aux fichiers float intermédiaires.
Un signal constant est conservé dans le fond avec une voix nulle, afin d'éviter
une division par zéro. Les valeurs non finies sont rejetées.

La préparation du lot 2 place d'abord le PCM sur l'origine de présentation avec
ses silences et discontinuités. Demucs reçoit le fichier entier, sans VAD ni
concaténation de régions. Son découpage utilise le recouvrement et le recadrage
de `apply_model`. Chaque stem doit avoir le nombre de frames attendu à un
échantillon près ; un résultat tronqué est rejeté plutôt que rempli silencieusement.
La normalisation finale et le rééchantillonnage utilisent `PcmLength`. Aucune
compensation de délai arbitraire n'est appliquée ; les mesures de recette doivent
justifier toute compensation future.

Le module est vérifié et verrouillé jusqu'à la fin des exports, ce qui empêche sa
réparation ou désinstallation pendant une inférence. Python est lancé par chemin
absolu avec `-I -B`, un environnement privé et CUDA masqué. Les caches restent
dans l'espace du module. Les médias intermédiaires résident dans un dossier
temporaire unique, nettoyé à la fin. Aucun stem n'est réutilisé selon son nom.

Toutes les collisions sont contrôlées avant calcul. Chaque fichier complet est
finalisé par déplacement dans son dossier de destination. Un échec tardif garde
les exports déjà finalisés et les annonce avec un code global d'échec. Ctrl+C
arrête l'arbre des sous-processus ; les fichiers partiels sont supprimés. La
progression native de Demucs est affichée ; elle décrit ses segments, pas un
pourcentage global du traitement. La limite d'exécution de sécurité est de sept
jours ; l'utilisateur peut annuler avant cette limite.

## Recette reproductible

Les tests ordinaires utilisent un moteur simulé avec les vrais FFmpeg/FFprobe.
Ils ne téléchargent aucun modèle. Le validateur séparé utilise un module déjà
installé et les deux échantillons vocaux fournis explicitement :

```powershell
dotnet build tools/OD.Converter.SeparationValidation -c Release -m:1
dotnet tools/OD.Converter.SeparationValidation/bin/Release/net10.0/win-x64/OD.Converter.SeparationValidation.dll "<racine privée OD.Converter>" "<nouveau dossier de recette>" "<voix 1.wav>" "<voix 2.wav>"
```

Lancer depuis la racine du dépôt. Le dossier de recette doit être vide. Le
validateur génère douze secondes de narration sur fond tonal, une alternance de
deux voix et une ambiance synthétique avec repères ; il crée aussi une vidéo
de quatorze secondes avec une seconde piste audio retardée de 500 ms. Il vérifie
les longueurs de tous les exports et l'absence de valeurs non finies. Le rapport
`results.json` consigne les décalages par corrélation des repères sur la somme
voix + fond, au début, au milieu et en fin. Ce contrôle de reconstruction ne
mesure pas à lui seul la qualité perceptive de chaque stem. Les médias de recette,
exports et rapports restent locaux dans `out`, ignoré par Git.

Les échantillons vocaux du projet voisin ont servi uniquement de données de
recette ; le programme distribué n'a aucune dépendance vers OD.VideoDub. Une
écoute humaine sur de vrais programmes musicaux, interviews et ambiances reste
nécessaire avant de déclarer la qualité acceptable pour un usage donné. Le détail
des contrôles exécutés et des réserves figure dans le [suivi](suivi-audio-video.md).
