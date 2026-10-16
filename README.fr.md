# Winsight

*[English version](README.md)*

> Un framework de test natif Windows pour les agents IA.

Winsight est un serveur et un SDK [Model Context Protocol](https://modelcontextprotocol.io)
qui permet aux agents IA (Claude Code, Cursor, Windsurf, agents sur mesure)
d'inspecter, de piloter et de vérifier par assertions des applications de bureau
Windows en cours de développement, au-delà de ce que permet UI Automation seul.

## Démarrer en 60 secondes

**Recommandé:** téléchargez `Winsight-Setup-0.1.0-win-x64.exe` depuis
[v0.1.0](https://github.com/VBlackJack/winsight/releases/tag/v0.1.0), fermez vos
clients et suivez l'assistant. Sélectionnez **Codex, Claude Desktop, Claude Code
et/ou Gemini CLI**, puis redémarrez-les. Aucune commande en terminal ni aucune
installation de .NET n'est nécessaire. Les clients eux-mêmes doivent déjà être
installés. Consultez le [guide d'installation](docs/installation.html).

Pour la version portable Windows x64, téléchargez `winsight-0.1.0-win-x64.zip` et
`SHA256SUMS.txt` depuis [v0.1.0](https://github.com/VBlackJack/winsight/releases/tag/v0.1.0).
Vérifiez l'archive avec `Get-FileHash -Algorithm SHA256`, extrayez-la, puis
déclarez le chemin absolu de `winsight-mcp.exe` comme commande stdio de votre
client MCP. L'archive inclut le runtime .NET; aucune installation de SDK n'est
requise. Les poids des modèles de vision et les identifiants d'API se
configurent séparément. Consultez les
[notes de version](docs/release-notes-v0.1.0.md) pour la configuration et les
limites de validation.

Pour compiler depuis les sources:

```powershell
git clone https://github.com/VBlackJack/winsight.git
cd winsight
dotnet build -c Release
.\tools\smoke-mcp.ps1                  # confirms the server speaks MCP and lists 61 tools
```

Ensuite, dans Claude Code, le fichier `.mcp.json` à la racine du dépôt déclare
automatiquement le serveur à l'ouverture du projet. Lancez `/mcp` dans Claude
Code pour confirmer que `winsight` apparaît comme connecté, puis essayez une
consigne comme:

> Lancez `samples\SampleWpfApp\bin\Release\net10.0-windows\SampleWpfApp.exe`,
> cliquez cinq fois sur le bouton Increment, puis lisez `_internalCount` sur
> `Winsight.Samples.SimpleCounter.ViewModels.CounterViewModel` pour confirmer
> qu'il vaut 5.

L'agent doit enchaîner de lui-même `list_windows` → `click_element` →
`read_managed_state`.

## Contrôle vérifié

Utilisez `observe_scene` et `act_verified` pour des cibles à durée de vie limitée et une vérification explicite du résultat.
UIA natif, DOM WebView2 autorisé, repli sur la vision, profils d'application et un exécuteur d'évaluation à vingt scénarios sont documentés dans [contrôle vérifié](docs/fr/verified-control.md).

## Pourquoi

Les bots de jeu pilotent des personnages dans des mondes DirectX depuis deux
décennies. Les bots de test pour applications de bureau ont pris du retard,
bloqués au niveau de l'arbre UI Automation. Winsight comble cet écart en
combinant quatre capacités orthogonales derrière une surface d'outils unique
qu'un LLM peut composer:

| Couche      | Ce qu'elle apporte                                                            | Repose sur                                      |
| ----------- | ----------------------------------------------------------------------------- | ----------------------------------------------- |
| Cycle de vie | Lancer une application et attendre sa fenêtre, la fermer                     | `Process` + interrogation de disponibilité de la fenêtre |
| Orchestration | Exécuter un scénario complet (action, synchronisation, assertion, capture) en un seul appel groupé | `StepRunner` au-dessus des couches action/synchronisation/oracle |
| Découverte  | Énumération des fenêtres (classées: main/dialog/popup/menu/tooltip, propriétaire, modale), inspection de l'arbre UIA (filtrable, avec valeurs), résumé des affordances (éléments actionnables + localisateurs recommandés) | FlaUI (UIA3) + énumération des fenêtres Win32   |
| Action      | Localisation stricte des éléments, styles de clic, clics par coordonnées, saisie, combinaisons de touches, survol, sélection d'éléments | Patterns FlaUI + SendInput / repli Win32 |
| Synchronisation | Attentes conditionnelles (exists / not_exists / enabled / visible / value / stable) | Interrogation UIA avec résultats structurés |
| Oracle (UI) | Instantanés d'état d'élément (valeur, bascule, sélection, focus, géométrie), assertions de premier plan | Patterns FlaUI                    |
| Retour      | Différence de l'interface après une action (fenêtres ouvertes/fermées, éléments ajoutés/supprimés/modifiés) | Instantané + différence structurelle pure |
| Oracle      | Lire des champs privés et l'état managé sans `InternalsVisibleTo`             | Microsoft.Diagnostics.Runtime (CLRMD)           |
| Diagnostics | S'abonner aux EventCounters, capturer des événements EventPipe (exceptions, télémétrie), capturer les exceptions de façon causale pendant un lot | Microsoft.Diagnostics.NETCore.Client + TraceEvent |
| Capture     | Capture d'écran de fenêtre via `PrintWindow` + `PW_RENDERFULLCONTENT`, renvoyée en ligne comme image MCP | Win32 `PrintWindow`        |
| WebView2    | Localiser les instances WebView2 intégrées, exposer leur point de terminaison de débogage CDP | Arbre de processus WMI + recoupement des écoutes TCP |
| Perception  | Ancrage visuel à partir d'une description en langage naturel (cloud ou entièrement local) | Anthropic Messages API / Qwen2.5-VL ONNX local |

## État

Phase 1 + Phase 2 + durcissement de la frontière des outils + ergonomie des
tests UIA + régression visuelle v0 + clics par coordonnées + découvrabilité
multi-fenêtre + cycle de vie des applications + contrôle vérifié + surfaces sans
UI Automation (iced, winit, egui) + prise en compte du bureau partagé:
entièrement amorçable, validé de bout en bout sur `SampleWpfApp` (chemin
nominal), `WpfLabApp` (chemins de saisie du shell WPF et une sonde Win32 brute
qui se comporte comme iced sur winit) et une véritable application WPF
(`Win11Forge`). Couverture actuelle:
tests unitaires Core/MCP et scénarios E2E qui pilotent un bureau Windows
interactif réel. Consultez les [preuves d'audit et de remédiation](docs/fr/audit-2026-09-05.md)
pour les résultats mesurés. Les tests d'intégration de la vision sont
explicitement ignorés quand leurs fixtures de modèle, d'image ou de référence
sont absentes.

| Capacité                        | Phase 1 | Phase 2 | Phase 3 |
| ------------------------------- | ------- | ------- | ------- |
| Découverte des fenêtres         | oui     |         |         |
| Fenêtres classées (dialog/popup/menu/tooltip, propriétaire, modale) |   | oui     |         |
| Résumé des affordances (`describe_ui`: éléments actionnables + localisateurs) |   | oui     |         |
| Perception des changements (`snapshot_ui` / `diff_ui`: ce qu'une action a modifié) |   | oui     |         |
| Cycle de vie des applications (lancement + attente de la fenêtre, fermeture) |   | oui     |         |
| Cible de session (set_target: omettre processId à chaque appel) |   | oui     |         |
| Inspection de l'arbre UIA       | oui     |         |         |
| Clic / saisie via UIA           | oui     |         |         |
| Clics par coordonnées (espace fenêtre/écran) |   | oui     |         |
| Exécution d'étapes par lot (`run_steps`, un seul aller-retour) |   | oui     |         |
| Assertions de premier plan (`assert_element` + étape assert, réussite/échec clair) |   | oui     |         |
| Oracle causal des exceptions (`run_steps captureExceptions`) |   | oui     |         |
| Localisateurs stricts + boîtes de dialogue modales |   | oui     |         |
| Oracle d'état des éléments + attentes |   | oui     |         |
| Combinaisons de touches ciblées + survol des infobulles |      | oui     |         |
| Sélection d'éléments (combo/liste/onglet) |   | oui     |         |
| Lecture de champ managé via CLRMD | oui   |         |         |
| Capture d'écran de fenêtre (PrintWindow) |   | oui     |         |
| Découverte des points de terminaison CDP WebView2 |   | oui     |         |
| Pilotage CDP WebView2 (eval / saisie) |    | oui     |         |
| Observateur de compteurs EventPipe |      | oui     |         |
| Capture d'événements EventPipe arbitraires |   | oui     |         |
| Ancrage visuel (API Claude)     |         | oui     |         |
| Ancrage visuel (Qwen2.5-VL ONNX local) |  | oui     |         |
| Régression visuelle (SSIM + différence de pixels) |   | oui     |         |
| Clic ancré visuellement (recherche + action) |    | oui     |         |
| Contrôle vérifié (`observe_scene` + `act_verified`, scènes, profils) |  | oui |  |
| Stabilisation du pointeur pour les interfaces en mode immédiat, molette et survol par coordonnées |  | oui |  |
| Saisie de texte sans élément UIA (`type_keys`, presse-papiers) |  | oui |  |
| Attentes visuelles (changement, stabilité) et lecture de texte par OCR |  | oui |  |
| Prise en compte du bureau partagé (activité utilisateur, retrait, garde de premier plan) |  | oui |  |
| Agent exploratoire (Sonnet 4.6) |         |         | oui     |

## Organisation du dépôt

```
winsight/
├── src/
│   ├── Winsight.Core/      Moteur: UIA, CLRMD, capture, diagnostics, vision, OCR, activité utilisateur, modèles
│   ├── Winsight.Mcp/       Exécutable du serveur MCP (stdio) + garde de la frontière des outils
│   └── Winsight.Setup/     Assistant d'installation par utilisateur (déclaration des clients)
├── samples/
│   ├── SampleWpfApp/       Application compteur utilisée comme cible d'intégration
│   └── WpfLabApp/          Laboratoire du shell WPF; --pointer-probe ouvre une fenêtre Win32 brute qui se comporte comme iced
├── tests/
│   ├── Winsight.Core.Tests Tests unitaires xUnit de la couche moteur
│   ├── Winsight.Mcp.Tests  Tests unitaires xUnit de la frontière des outils MCP (ToolSafety, wrappers)
│   └── Winsight.E2E        Scénarios de bout en bout sur SampleWpfApp et WpfLabApp
├── tools/
│   ├── Winsight.Eval/      Exécuteur d'évaluation indépendant du modèle
│   ├── run-mcp.ps1         Lance une copie du serveur compilé, pour que la compilation reste déverrouillée
│   └── smoke-mcp.ps1       Test de fumée JSON-RPC du serveur stdio
└── docs/                   Documentation anglaise; docs/fr/ contient les versions françaises
    ├── architecture.md
    ├── local-onnx-grounder.md
    ├── runbook.md
    ├── tools-reference.md
    └── verified-control.md
```

## Compilation

Nécessite le SDK .NET 10. Le moteur, le serveur et les tests ciblent
`net10.0-windows10.0.19041.0` (Windows 10 version 2004 ou ultérieure) pour
l'API OCR de Windows; la restauration télécharge le pack de référence du SDK
Windows correspondant.

```powershell
dotnet restore
dotnet build
dotnet test tests\Winsight.Core.Tests -c Release         # engine unit tests
dotnet test tests\Winsight.Mcp.Tests -c Release          # MCP boundary unit tests
dotnet test tests\Winsight.E2E -c Release                # E2E (drives the sample app on the live desktop)
```

La suite E2E lance `SampleWpfApp.exe`, exerce le parcours orienté bureau et
vérifie les résultats. Elle nécessite un bureau interactif déverrouillé dont le
focus est disponible pour le processus de test; les restrictions de Windows sur
la fenêtre de premier plan peuvent faire échouer les cas dépendants du focus
dans les sessions en arrière-plan ou distantes.

## Exécuter le serveur MCP

Le serveur parle MCP sur stdio. Compilez-le une fois, puis déclarez le script
wrapper auprès de votre client:

```powershell
dotnet build src\Winsight.Mcp -c Release
```

### Configuration client recommandée

```json
{
  "mcpServers": {
    "winsight": {
      "command": "powershell",
      "args": [
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        "G:\\_Projects\\winsight\\tools\\run-mcp.ps1"
      ]
    }
  }
}
```

`tools\run-mcp.ps1` copie le serveur MCP compilé dans `%TEMP%\winsight-mcp-runtime`
avant de le lancer. Ainsi `src\Winsight.Mcp\bin` reste recompilable pendant qu'un
client est connecté; un lancement direct de `src\Winsight.Mcp\bin\...\winsight-mcp.dll`
verrouille le fichier et peut bloquer `dotnet build`.

### Où placer la configuration

| Client      | Fichier de configuration                                 |
| ----------- | -------------------------------------------------------- |
| Claude Code | `%USERPROFILE%\.claude.json` (par utilisateur) ou `.mcp.json` à la racine du dépôt |
| Cursor      | `~/.cursor/mcp.json`                                     |
| Windsurf    | `~/.codeium/windsurf/mcp_config.json`                    |

Après la déclaration, lancez `/mcp` dans Claude Code pour confirmer que le
serveur `winsight` est connecté et liste les 61 outils.
Après une recompilation de Winsight, redémarrez la session client: une session
conserve le processus serveur, et la liste d'outils, avec lesquels elle a
démarré. `get_winsight_guide` indique la version compilée qu'exécute une
session, sa liste exacte d'outils et, lorsqu'elle est lancée via
`tools\run-mcp.ps1`, si une version plus récente est en attente
(`newerBuildAvailable`).

### Vérification rapide depuis le shell

Pour confirmer que le serveur démarre proprement (indépendamment de tout
client):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\run-mcp.ps1
```

Le processus doit rester ouvert en attente d'entrées stdio. Appuyez sur
`Ctrl+C` pour quitter. Les journaux vont sur stderr; stdout est réservé au
trafic du protocole MCP.

### Facultatif: activer l'ancrage visuel

L'outil `find_element_visually` a besoin d'un backend de vision. Le backend par
défaut appelle l'API de vision Claude d'Anthropic.

| Variable                    | Obligatoire | Valeur par défaut                     |
| --------------------------- | ----------- | ------------------------------------- |
| `WINSIGHT_VISION_BACKEND`   | non         | `anthropic` (`anthropic` ou `onnx`)   |
| `ANTHROPIC_API_KEY`         | oui, pour `anthropic` | -                           |
| `WINSIGHT_VISION_MODEL`     | non         | `claude-sonnet-4-6`                   |
| `WINSIGHT_VISION_API_URL`   | non         | `https://api.anthropic.com/v1/messages` |
| `WINSIGHT_VISION_MIN_CONFIDENCE` | non    | `0.5` (confiance minimale requise avant que `find_element_visually` n'exécute `act`) |
| `WINSIGHT_VISION_ONNX_PATH` | oui, pour `onnx` | -                                |
| `WINSIGHT_VISION_EP`        | non, pour `onnx` | `cpu` (DirectML n'est pas activé dans cette version) |

> **Confidentialité:** avec le backend `anthropic`, la capture d'écran est
> envoyée à l'API configurée. `WINSIGHT_VISION_BACKEND=onnx` exécute un modèle
> Qwen2.5-VL local entièrement sur la machine (voir
> [docs/fr/local-onnx-grounder.md](docs/fr/local-onnx-grounder.md) pour
> l'approvisionnement) et ne se replie jamais sur le cloud. Si le backend
> sélectionné n'est pas configuré, tous les autres outils continuent de
> fonctionner; seul `find_element_visually` renvoie une erreur à l'appel.

### Facultatif: rediriger la sortie des captures

Par défaut, `capture_screenshot` ainsi que les captures intermédiaires et cartes
de différences produites par `find_element_visually` et `compare_visual_baseline`
écrivent des PNG sous `%TEMP%\winsight`. `capture_screenshot` (et la différence
de `compare_visual_baseline`) renvoient aussi le PNG en ligne comme image MCP, si
bien qu'un agent dans le même processus voit l'interface sans toucher au
disque; le chemin du fichier reste utile pour un superviseur hors processus.
Définissez la variable d'environnement `WINSIGHT_OUTPUT_DIR` pour rediriger les
captures vers un répertoire lisible par un agent superviseur, par exemple un
dossier partagé avec l'hôte de l'agent. Le répertoire est créé à la première
écriture; un argument `outputPath` explicite passé à `capture_screenshot` est
toujours prioritaire.

| Variable             | Obligatoire | Valeur par défaut  |
| -------------------- | ----------- | ------------------ |
| `WINSIGHT_OUTPUT_DIR`| non         | `%TEMP%\winsight`  |

## Boucle d'agent recommandée

Pour les agents IA, privilégiez d'abord les outils de haut niveau et ne revenez
aux primitives qu'en cas de besoin:

1. `launch_app` ou `list_windows`, puis `set_target`.
2. `observe` pour obtenir les fenêtres, les éléments actionnables, les
   localisateurs recommandés, les actions probables, la mémoire de session et,
   en option, une capture d'écran.
3. `plan_steps` avec l'objectif de l'utilisateur.
4. `run_steps` avec `dryRun=true`.
5. `act_and_observe` ou `run_steps` avec `dryRun=false`.
6. `repair_locator` si un localisateur échoue ou devient ambigu.

`get_winsight_guide` expose ce mode opératoire directement via MCP, afin qu'un
agent puisse se rétablir même quand la documentation externe n'est pas dans son
contexte.

### Facultatif: références de régression visuelle

`compare_visual_baseline` capture la fenêtre cible et la compare à un PNG de
référence par SSIM et différence de pixels. Les références sont stockées hors du
dépôt. Si une référence n'existe pas, ou si `updateBaseline=true`, la capture
courante est copiée comme référence et le verdict du résultat est
`baseline_created` ou `baseline_updated`.

| Variable                          | Obligatoire | Valeur par défaut                            |
| --------------------------------- | ----------- | -------------------------------------------- |
| `WINSIGHT_VISUAL_BASELINE_DIR`    | non         | `%LOCALAPPDATA%\winsight\visual-baselines`   |
| `WINSIGHT_VISUAL_SSIM_THRESHOLD`  | non         | `0.98`                                       |
| `WINSIGHT_VISUAL_LOCAL_SSIM_THRESHOLD` | non    | `0.85`                                       |
| `WINSIGHT_VISUAL_PIXEL_THRESHOLD` | non         | `0.005`                                      |
| `WINSIGHT_VISUAL_COLOR_THRESHOLD` | non         | `0.1`                                        |

## Outils exposés

Consultez [docs/fr/tools-reference.md](docs/fr/tools-reference.md) pour le
schéma complet.

Pour les essais de modèles mesurés et les suites propres à chaque application,
consultez [l'évaluation des modèles](docs/fr/model-evaluation.md)
([English](docs/model-evaluation.md)).

| Outil                         | Rôle                                                   |
| ----------------------------- | ------------------------------------------------------ |
| `set_target` / `get_target` / `clear_target` | Définir, lire ou effacer le processus par défaut de la session, pour que les outils liés à un processus puissent omettre `processId` |
| `observe`                     | Orientation en un appel: fenêtres, éléments actionnables, localisateurs recommandés, actions probables, mémoire, capture d'écran facultative |
| `plan_steps`                  | Ébaucher un plan `run_steps` prudent à partir d'un objectif en langage naturel |
| `repair_locator`              | Classer des localisateurs de remplacement à partir de l'interface courante et de la mémoire de session |
| `run_steps`                   | Exécuter une séquence d'étapes d'interface (action/synchronisation/assertion/capture) en un appel, avec un journal par étape, un mode simulation, la relecture de traces et une capture causale facultative des exceptions |
| `replay_steps`                | Rejouer un artefact de relecture `run_steps`, en simulation par défaut |
| `act_and_observe`             | Exécuter une étape et renvoyer la différence d'interface avant/après ainsi que les affordances courantes |
| `click_and_wait`              | Cliquer et synchroniser en un appel, avec le retour de l'action, de l'attente et de la différence |
| `type_and_assert`             | Saisir puis vérifier immédiatement un état observable |
| `get_agent_state`             | Contexte compact: cible, garde-fous, mémoire, dernier échec et recettes intégrées |
| `get_session_memory` / `clear_session_memory` | Inspecter ou réinitialiser les localisateurs observés et les échecs récents réparables |
| `get_winsight_guide`          | Mode opératoire intégré pour la boucle recommandée et la politique de réparation, plus la version compilée en cours et ses outils déclarés |
| `observe_scene`               | Observation à durée de vie limitée avec des références de cible `eN`, fenêtres, focus, états et actions prises en charge |
| `act_verified`                | Agir une fois sur une référence `observe_scene` et vérifier une postcondition explicite |
| `capture_scene`               | Capturer une fenêtre d'une observation, annotée avec ses références `eN` |
| `observe_dom_scene`           | Observer une page WebView2 autorisée avec le même contrat d'observation |
| `locate_scene_target`         | Repli sur la vision qui ajoute une cible localisée à une nouvelle observation |
| `save_app_profile` / `run_app_profile` | Enregistrer et exécuter une recette liée à la version et au SHA256 de l'exécutable |
| `launch_app`                  | Démarrer une application et attendre que sa fenêtre soit prête (renvoie un descripteur classé) |
| `close_app`                   | Demander une fermeture propre, ou forcer explicitement l'arrêt |
| `list_windows`                | Énumérer les fenêtres de premier niveau (classées; par processus, fait apparaître dialogues, popups et menus) |
| `inspect_ui_tree`             | Renvoyer l'arbre UIA d'un processus (filtrable, avec valeurs) |
| `describe_ui`                 | Lister les éléments actionnables d'une fenêtre avec des localisateurs prêts à l'emploi ("que puis-je faire ici?") |
| `snapshot_ui` / `diff_ui`     | Capturer une référence de l'interface et indiquer ce qu'une action a modifié (fenêtres, éléments, valeurs des champs) |
| `click_element`               | Cliquer sur un élément localisé (simple/double/droit, localisateurs stricts) |
| `click_point`                 | Cliquer sur une coordonnée relative à la fenêtre ou une coordonnée d'écran bornée (agit sur un point ancré visuellement); le pointeur se stabilise avant l'appui, et `focusWindow` met d'abord la cible au premier plan |
| `type_text`                   | Saisir dans un élément localisé (refuse les cibles WebView2) |
| `send_key_chord`              | Donner le focus à une fenêtre et envoyer une combinaison de touches avec codes de balayage et indicateurs de touches étendues |
| `get_focused_element`         | Indiquer l'élément qui détient le focus clavier, et s'il se trouve dans WebView2 |
| `get_user_activity`           | Indiquer si une personne utilise le bureau: temps écoulé depuis la dernière saisie humaine (hors saisies de Winsight), fenêtre de premier plan, session verrouillée |
| `wait_for_user_idle`          | Attendre que personne n'ait utilisé la souris ni le clavier pendant une durée donnée |
| `scroll_point` / `hover_point` | Tourner la molette de la souris, ou poser le pointeur, à une coordonnée, avec les contrôles de `click_point` |
| `type_keys`                   | Saisir du texte dans une fenêtre sans élément UIA cible (frappes Unicode, ou collage qui restaure le presse-papiers) |
| `set_clipboard_text` / `read_clipboard_text` | Écrire ou lire le texte du presse-papiers; désactivé sauf si `WINSIGHT_ALLOW_CLIPBOARD` est défini |
| `wait_for_visual_change` / `wait_for_visual_stable` | Attendre qu'une fenêtre ou une zone change d'aspect, ou cesse de changer, au lieu d'une pause fixe |
| `read_text_visually`          | Lire le texte affiché par une fenêtre avec le moteur OCR hors ligne de Windows, avec des rectangles en coordonnées de fenêtre |
| `hover_element`               | Survoler un élément et capturer/vérifier sa fenêtre d'infobulle (ou repli sur HelpText) |
| `select_item`                 | Sélectionner un élément par nom exact, sous-chaîne ou motif dans un ComboBox/ListBox/Tab/Tree |
| `drag`                        | Glisser entre des éléments ou des points, au rythme du glisser-déposer OLE |
| `manage_window`               | Déplacer, redimensionner, agrandir, réduire ou restaurer une fenêtre |
| `get_element_state`           | Lire l'état d'un élément (valeur, bascule, sélection, focus) |
| `wait_for_element`            | Attendre qu'un élément existe, disparaisse, soit activé ou visible, que sa valeur ou son nom corresponde, qu'un nombre d'enfants soit atteint, ou que son état soit stable |
| `assert_element`              | Vérifier une condition sur un élément (equals/contains/greater_than/checked/enabled/...) avec réussite/échec clair |
| `read_managed_state`          | Lire un champ managé par attachement CLRMD             |
| `capture_screenshot`          | Capture PNG d'une fenêtre au premier plan ou désignée par son titre, renvoyée en ligne comme image MCP (PrintWindow PW_RENDERFULLCONTENT) |
| `compare_visual_baseline`     | Comparer une fenêtre capturée à un PNG de référence (SSIM + différence de pixels) |
| `detect_webview2_endpoints`   | Trouver les enfants WebView2 et signaler tout point de terminaison CDP |
| `list_cdp_targets`            | Lister les cibles débogables exposées par un point de terminaison CDP WebView2 autorisé |
| `evaluate_javascript`         | Évaluer du JavaScript dans une page WebView2 autorisée via CDP |
| `type_into_webview`           | Saisir dans un contenu WebView2 autorisé à l'aide d'événements clavier CDP |
| `observe_counters`            | S'abonner aux EventCounters via EventPipe (oracle non intrusif) |
| `capture_events`              | Capturer des événements EventPipe arbitraires (exceptions, EventSource personnalisé) |
| `find_element_visually`       | Localiser un élément à partir d'une requête en langage naturel à l'aide d'un modèle de vision |

## Architecture

Consultez [docs/fr/architecture.md](docs/fr/architecture.md).

## Modèle de sécurité

Winsight est un **outil de développement pour la machine sur laquelle il
s'exécute**. Par conception, il accorde à l'agent connecté de larges capacités
sur cette session de bureau:

- `launch_app` démarre un exécutable arbitraire; `close_app` met fin à une arborescence de processus.
- `evaluate_javascript` exécute du JavaScript arbitraire dans une page WebView2 intégrée.
  Les outils CDP ne se connectent qu'aux ports signalés par `detect_webview2_endpoints`
  pour la cible de session autorisée ou un processus hôte explicite.
- `read_managed_state` s'attache à un processus managé et lit sa mémoire (CLRMD);
  `observe_counters` / `capture_events` y ouvrent des sessions EventPipe.
- `capture_screenshot` écrit dans tout `outputPath` que vous lui passez (il n'est
  **pas** confiné à un répertoire: l'agent dispose déjà d'un large accès aux
  fichiers, si bien que confiner ce seul outil relèverait de la mise en scène plutôt
  que d'une véritable frontière). `compare_visual_baseline` fait exception: sa
  `baselineKey` est confinée au répertoire des références.

N'exécutez le serveur que sur une machine de développement que vous contrôlez,
sous le même compte utilisateur que l'agent, contre des applications que vous
développez ou testez. Il s'exécute sous votre identité: il n'accorde aucun
privilège que vous n'ayez déjà, et il ne doit pas être exposé à un client non
fiable. Les oracles basés sur EventPipe (`observe_counters`, `capture_events`)
sont la voie compatible avec les EDR; l'attachement CLRMD de
`read_managed_state` utilise des techniques que certains produits de sécurité
des postes signalent.

Garde-fous facultatifs:

| Variable | Rôle |
| -------- | ---- |
| `WINSIGHT_ALLOWED_PROCESS_IDS` | Liste d'autorisation, séparée par virgules ou points-virgules, des identifiants de processus que tout outil lié à un processus doit respecter |
| `WINSIGHT_ALLOWED_PROCESS_NAMES` | Liste d'autorisation, séparée par virgules ou points-virgules, des noms de processus que tout outil lié à un processus doit respecter |
| `WINSIGHT_ALLOWED_EXECUTABLES` | Liste d'autorisation, séparée par virgules ou points-virgules, des chemins d'exécutables pour `launch_app` |
| `WINSIGHT_DRY_RUN` | `1`/`true`/`yes` bloque les outils qui modifient l'état et les écritures de captures de `run_steps`, et renvoie ce qui aurait été exécuté |
| `WINSIGHT_ALLOW_CLIPBOARD` | `1`/`true`/`yes` active les outils de presse-papiers et `type_keys mode=paste`; désactivé par défaut car le presse-papiers peut contenir des secrets |
| `WINSIGHT_YIELD_TO_USER_MS` | Millisecondes: toute action qui modifie l'écran échoue avec `user_active` tant qu'une saisie humaine est plus récente que cette durée. Non défini ou `0` le désactive |
| `WINSIGHT_TRACE` / `WINSIGHT_TRACE_DIR` | Écrire les traces JSONL des outils et les artefacts de relecture de `run_steps` |

## Licence

Apache License 2.0: consultez [LICENSE](LICENSE).
