# Distributions privées et licences des modules CPU

Le catalogue embarqué [artifacts.json](../src/OD.Converter/Services/Modules/Resources/artifacts.json)
fixe les URL HTTPS, tailles et SHA-256 de **tous** les téléchargements. Il est
compilé dans l'EXE : aucune recherche de « dernière version » ni lecture d'un
catalogue modifiable dans le dossier utilisateur n'est faite à l'installation.
Une distribution mise à jour nécessite une nouvelle recette et une validation.

## Profils retenus

| Module | Distribution | Modèle | Téléchargement total |
|---|---|---|---|
| Separation | Demucs 4.0.1, CPython embarqué 3.11.9 x64, PyTorch et torchaudio 2.6.0+cpu | `htdemucs`, checkpoint `955717e8-8726e21a.th` | 339 428 213 octets |
| Transcription | whisper.cpp 1.8.2, archive officielle Windows x64 CPU, recette r2 | `large-v3-turbo-q5_0`, multilingue, et VAD `silero-v5.1.2` | 582 025 776 octets |

Le choix quantifié Whisper réduit le téléchargement et la mémoire par rapport au
poids non quantifié. Il ne modifie pas la langue de sortie. La qualité du texte
et des sous-titres reste à mesurer au lot 5 ; le contrôle d'installation vérifie
l'exécution, pas la précision de reconnaissance. Les versions ci-dessus constituent
une base compatible testée, pas un engagement à distribuer la dernière version de
chaque composant. La maintenance de sécurité de ces versions doit être revue
avant publication au lot 7, notamment celle du Python embarqué.

Sources officielles : [CPython 3.11.9](https://www.python.org/downloads/release/python-3119/),
[Demucs 4.0.1](https://pypi.org/project/demucs/4.0.1/),
[PyTorch 2.6.0](https://pytorch.org/get-started/previous-versions/#v260),
[whisper.cpp 1.8.2](https://github.com/ggml-org/whisper.cpp/releases/tag/v1.8.2),
[modèles ggml](https://huggingface.co/ggerganov/whisper.cpp/tree/5359861c739e955e79d9a303bcbc70fb988958b1).

Le lot 5 ajoute le [VAD Silero v5.1.2 officiel](https://huggingface.co/ggml-org/whisper-vad/tree/9ffd54a1e1ee413ddf265af9913beaf518d1639b),
885 098 octets, SHA-256 `29940d98d42b91fbd05ce489f3ecf7c72f0a42f027e4875919a28fb4c04ea2cf`.
Sa [licence MIT](https://github.com/snakers4/silero-vad/blob/066409f3d9db45a30d21dfcbf9147826ffdd7052/LICENSE)
est conservée dans `licenses/silero-vad-LICENSE`. La recette r2 exige une
installation/réparation explicite des anciennes générations r1, sans modification
du module Demucs.

## Dépendances Demucs fixées

L'installation ne résout aucune dépendance. Elle télécharge les artefacts fixés,
prépare pip 25.1.1, setuptools 80.9.0, wheel 0.45.1 et packaging 25.0 dans le
Python privé, puis installe les fichiers locaux avec `--no-index --no-deps
--no-build-isolation --no-compile --no-cache-dir`. Demucs est construit depuis son
sdist officiel avec ces outils locaux. `pip check` contrôle le graphe obtenu.
Les compilateurs C/C++ ne sont pas nécessaires : les dépendances natives sont
des wheels Windows x64 CPython 3.11 fixées.

| Paquet | Version | Licence principale / notices |
|---|---|---|
| demucs | 4.0.1 | MIT |
| torch | 2.6.0+cpu | BSD-3-Clause et notices des bibliothèques incluses |
| torchaudio | 2.6.0+cpu | BSD-2-Clause |
| numpy | 1.26.4 | BSD-3-Clause et notices des bibliothèques incluses |
| soundfile | 0.13.1 | BSD-3-Clause ; libsndfile 1.2.2 inclus sous LGPL |
| sympy | 1.13.1 | BSD |
| mpmath | 1.3.0 | BSD |
| cffi | 2.1.1 | MIT-0 |
| julius | 0.2.8 | MIT |
| lameenc | 1.9.0 | LGPL-3.0-or-later |
| typing_extensions | 4.16.0 | PSF-2.0 |
| dora-search | 0.1.13 | MIT |
| einops | 0.8.2 | MIT |
| filelock | 4.2.6 | MIT |
| fsspec | 2026.9.0 | BSD-3-Clause |
| Jinja2 | 3.1.6 | BSD-3-Clause |
| MarkupSafe | 3.0.4 | BSD-3-Clause |
| networkx | 3.6.1 | BSD-3-Clause |
| omegaconf | 2.4.0 | BSD-3-Clause ; attribution ANTLR conservée |
| PyYAML | 6.0.3 | MIT |
| openunmix | 1.3.0 | MIT |
| pycparser | 3.12 | BSD-3-Clause |
| retrying | 1.4.2 | Apache-2.0 |
| submitit | 1.5.4 | MIT |
| cloudpickle | 3.1.2 | BSD-3-Clause |
| tqdm | 4.70.1 | MPL-2.0 et MIT |
| colorama | 0.4.6 | BSD-3-Clause |
| treetable | 0.2.6 | Unlicense |

Les licences originales et attributions des wheels sont conservées intégralement
dans leurs répertoires `.dist-info`, y compris les notices des bibliothèques
natives. `runtime/LICENSE.txt` conserve les termes Python et ses dépendances.
Le répertoire `licenses` ajoute les licences moteur/modèle, un inventaire des
paquets avec liens et un fichier `THIRD-PARTY-NOTICES.txt`.

Les composants LGPL restent des bibliothèques dynamiques séparées, sans copie de
leur code dans l'EXE .NET. Sources correspondantes :
[lameenc 1.9.0](https://pypi.org/project/lameenc/1.9.0/#files),
[LAME 3.100](https://sourceforge.net/projects/lame/files/lame/3.100/),
[libsndfile 1.2.2](https://github.com/libsndfile/libsndfile/releases/tag/1.2.2).
Une modification locale volontaire du runtime est détectée par le manifeste et
fait passer le module à `Needs repair` ; elle n'entraîne pas de réparation
automatique ni de remplacement des fichiers modifiés.

## Bibliothèques Visual C++ privées

Les deux recettes téléchargent directement l'archive Microsoft
`Microsoft.VC.14.44.17.14.CRT.Redist.X64.base.vsix`, version 14.44.35211, puis
copient seulement les DLL **release** CRT et OpenMP à côté de leur exécutable.
Aucun `vc_redist.exe`, MSI, élévation, registre ou runtime d'un autre logiciel
n'est utilisé. Les fichiers `debug_nonredist` de l'archive ne sont jamais extraits.

L'empreinte publiée dans le catalogue Visual Studio est conservée :
`4aaf54db0bfc9435f7c3660e1a00237a4b556042bfeea64bde44c2e0194e6ee5`.
La taille fixée, **3 224 191 octets**, est celle du téléchargement dont l'empreinte
est confirmée ; le catalogue Microsoft annonçait une taille différente.

Le texte officiel [Visual C++ Runtime 2015–2022](https://visualstudio.microsoft.com/license-terms/vs2022-cruntime/)
est conservé dans `licenses/visual-c-runtime.docx`. Le déploiement local et les
conditions de redistribution sont décrits par
[Microsoft](https://learn.microsoft.com/cpp/windows/redistributing-visual-cpp-files).
Le catalogue télécharge les composants depuis leurs éditeurs ; il n'incorpore
aucune DLL ni aucun poids IA dans le dépôt ou l'EXE de base. Une republication de
ces binaires reste soumise aux conditions de leurs licences, notamment à celles
de Visual Studio pour les fichiers redistribuables.

## Contrôles fonctionnels avant activation

Demucs vérifie les versions, le profil PyTorch sans CUDA, charge le checkpoint
local vérifié, puis calcule les quatre stems d'une seconde de silence stéréo
44,1 kHz. La forme et les valeurs finies du tenseur sont contrôlées. Le checkpoint
Demucs contenant des classes Python, son chargement utilise explicitement
`weights_only=False` uniquement pour ce fichier fixé et contrôlé ; ce mécanisme
ne reçoit jamais un fichier média ou un checkpoint arbitraire de l'utilisateur.

Whisper charge le modèle local et exécute `whisper-cli` sur une seconde de WAV
mono 16 kHz, avec GPU désactivé et le VAD activé (recette r2). Le code de sortie et la création du TXT de
contrôle sont vérifiés ; un texte vide sur le silence reste acceptable. Aucun
fichier de contrôle précédent n'est réutilisé. Les deux contrôles disposent d'un
délai de trois minutes et nettoient leur dossier temporaire propre au module.

Le binaire MSVC de Whisper peut terminer avec `0xC0000409` si un chemin de modèle
accentué lui parvient dans la page de codes ANSI, alors que son chargeur attend de
l'UTF-8 ; voir [le diagnostic amont](https://github.com/ggml-org/whisper.cpp/issues/4018).
Le contrôle travaille dans un sous-dossier privé du module et passe le modèle par
un chemin relatif ASCII ; le WAV et le préfixe de sortie sont également relatifs.
L'emplacement réel du module peut ainsi contenir des espaces et des accents,
sans chemin court 8.3, jonction ou copie du modèle à chaque contrôle. L'adaptateur
du lot 5 devra conserver cette convention pour ses fichiers de travail.

Ces tests ne remplacent pas les essais de séparation sur des médias réels du
lot 4 ni la relecture de transcriptions du lot 5. Une machine Windows propre
reste à vérifier au lot 7.

## Reproduire la validation des installations

L'outil de validation utilise un emplacement explicite, indépendant des modules
du compte utilisateur, et refuse un dossier de validation non vide :

```powershell
dotnet build tools/OD.Converter.ModuleValidation/OD.Converter.ModuleValidation.csproj -c Release -m:1
& tools/OD.Converter.ModuleValidation/bin/Release/net10.0/win-x64/OD.Converter.ModuleValidation.exe out/module-validation both
```

Les téléchargements représentent environ 921 Mo décimaux pour les deux modules ;
le runtime décompressé et la réparation demandent plusieurs Gio disponibles.
L'outil installe, vérifie, corrompt un poids, constate `Needs repair`, reconstruit,
puis désinstalle les modules en contrôlant les sources/exports et l'autre module.
La première acquisition utilise le téléchargeur HTTPS de production. Les
réparations réutilisent ensuite le cache d'artefacts du validateur, avec réseau
interdit et nouvelles vérifications SHA-256. Ce cache de test n'est pas un cache
de téléchargement implémenté dans le produit. Un troisième argument facultatif
permet de réutiliser un cache de validation existant ; son contenu est toujours
recontrôlé. Les rapports figurent dans `results.json` ou `failure.txt` du dossier
de validation.

Après une interruption, `--resume` permet de reprendre un dossier créé par ce
validateur et portant son marqueur `.module-validation`. Les modules déjà prêts
sont vérifiés avant de poursuivre ; les installations incomplètes sont reprises
par le gestionnaire. Le validateur ne permet pas d'adopter un dossier non vide
arbitraire sans ce marqueur.
