# Transcription globale Whisper — lot 5

`process --action transcribe` transcrit directement la piste audio sélectionnée,
qu'elle provienne d'un fichier audio ou vidéo. Seul le module `transcription`
est requis. `process --action separate-transcribe` utilise d'abord Demucs puis
transcrit les voix estimées ; les deux modules sont requis. Aucune installation
ni aucun téléchargement n'a lieu pendant le traitement.

```powershell
odconv modules install transcription
odconv process "interview.wav" --action transcribe --language fr
odconv process "clip.mp4" --action transcribe --audio-track 2 --language auto
odconv process "clip.mp4" --action separate-transcribe --exports transcript,subtitles --language en
```

Le moteur est whisper.cpp CPU et le modèle multilingue quantifié
`large-v3-turbo-q5_0` du catalogue du lot 3. L'adaptateur reprend le principe de
l'appel natif d'OD.VideoDub, avec chemins privés, langue configurable, aucun
appel de traduction ni option de diarisation. Les arguments sont transmis
séparément au processus, sans shell. Les chemins de travail du moteur sont
relatifs pour contourner sa limitation de lecture des chemins accentués.

La préparation Whisper utilise un WAV PCM signé 16 bits, mono 16 kHz, créé
depuis le PCM déjà aligné. Le modèle de détection de parole Silero v5.1.2
(885 098 octets, MIT) est installé avec Whisper. Il évite de demander une
reconnaissance sur les intervalles sans parole détectée. whisper.cpp rétablit
les timestamps des segments dans la chronologie du WAV complet avec sa table
de correspondance VAD ; l'application ne rajoute pas le retard de la piste.
Les timestamps SRT se rapportent directement à l'origine de présentation de
la source. Aucune compensation de délai moteur non mesuré n'est inventée.

Références : [modèle VAD officiel figé](https://huggingface.co/ggml-org/whisper-vad/tree/9ffd54a1e1ee413ddf265af9913beaf518d1639b),
[intégration et correspondance temporelle de whisper.cpp 1.8.2](https://github.com/ggml-org/whisper.cpp/blob/v1.8.2/src/whisper.cpp).

Les segments SRT du moteur alimentent les deux exports : TXT UTF-8 sans BOM,
avec une ligne par segment, et SRT global numéroté. Les index, timestamps,
durées positives, ordre sans chevauchement, limites de la source et texte
non vide sont contrôlés avant export. Une sortie absente ou structurellement
invalide fait échouer le traitement avec le code 3. Une fin de fichier valide
ne permet pas de prouver que Whisper a reconnu toutes les paroles : ce contrôle
nécessite une relecture.

Le VAD de whisper.cpp 1.8.2 complète son dernier bloc de **512 échantillons à
16 kHz** avec des zéros et calcule sa durée à partir du nombre de blocs, puis
arrondit les timestamps au centième de seconde. Sur un extrait réel de 30 s,
cela a produit une dernière fin à **30,020 s**. L'adaptateur calcule la borne
exacte de cette fenêtre à partir du nombre de frames PCM fourni à Whisper.
Seule la fin du **dernier** sous-titre peut être ramenée à la durée source,
si elle reste dans cette borne et si le sous-titre commence avant la fin source.
Un avertissement l'annonce ; aucun début ni sous-titre intermédiaire n'est
déplacé. Les chevauchements, sous-titres entièrement hors source et dépassements
supérieurs à la fenêtre restent rejetés. Le parseur SRT général reste strict.
La même correction n'est pas appliquée à une entrée de 12 s dont les 192 000
frames à 16 kHz forment des blocs complets. Ce traitement de la marge technique
ne mesure pas la précision linguistique des limites de phrase. Voir le
[code amont figé du VAD et de ses conversions temporelles](https://github.com/ggml-org/whisper.cpp/blob/v1.8.2/src/whisper.cpp#L4663-L4770).

Le silence numérique (amplitude absolue au plus `0,00001` sur tout le PCM mono
préparé) produit des exports vides sans appel d'inférence. Une sortie vide du
moteur est également acceptée avec un avertissement. Les tokens non verbaux sont
supprimés par l'option native `-sns`. La détection de parole réduit les
hallucinations observées sur le bruit de la recette, mais peut manquer des voix
faibles ou atypiques. Musiques, bruits et voix difficiles peuvent encore causer
des omissions ou hallucinations. La fiabilité sur des ambiances représentatives
et les limites de phrases nécessitent une relecture.
La détection `auto` choisit une langue pour le fichier : dans la fixture alternant
français et anglais, la recette avec VAD a retenu l'anglais et omis la première
voix française. La transcription multilingue avec changements de langue dans un
même fichier n'est pas validée. Une sortie structurée et non vide ne prouve pas
que toutes les personnes ont été transcrites.

La recette Whisper passe de `r1` à `r2` pour inclure le modèle VAD et sa licence.
Une installation antérieure nécessite une opération explicite
`odconv modules repair transcription` ; aucun ajout implicite au runtime n'est fait.

Toutes les destinations demandées sont contrôlées avant les moteurs. Chaque
export est écrit dans un fichier temporaire dans le dossier de destination puis
finalisé par déplacement. Une erreur ou annulation préserve l'ancienne
destination de l'export concerné et supprime le fichier partiel. Le résultat
global conserve les chemins déjà finalisés et annonce les sorties manquantes.
Les générations des modules sont verrouillées pendant leur utilisation.
Les fichiers de travail sont supprimés en fin d'opération ; un échec de nettoyage
est signalé avec son chemin.

Le validateur `tools/OD.Converter.TranscriptionValidation` réutilise les fixtures
du lot 4. Il installe explicitement Whisper dans l'espace de modules fourni,
à partir d'artefacts locaux dont taille et SHA-256 sont vérifiés, sans réseau.
Il teste silence, narration française, alternance de voix, vidéo multipiste
retardée, ambiance synthétique et séparation puis transcription. Le JSON conserve
les segments, chemins, avertissements, durées et codes de sortie. Il ne valide
pas automatiquement la précision linguistique ni la qualité auditive.
Un fichier audio anglais peut remplacer le dossier de fixtures pour une recette
unitaire avec `--language en`.
