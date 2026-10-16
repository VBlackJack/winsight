# Architecture

*[English version](../architecture.md)*

Winsight est organisé en couches pour que chaque capacité soit utilisable,
remplaçable et testable indépendamment. Le serveur MCP est un adaptateur mince;
la valeur réside dans le moteur.

## Couches

```
┌─────────────────────────────────────────────────────────┐
│  Agent IA (Claude Code / Cursor / sur mesure)           │
└─────────────────────────────────────────────────────────┘
                         │  MCP (stdio)
                         ▼
┌─────────────────────────────────────────────────────────┐
│  Winsight.Mcp                                           │
│   - définitions des outils (Tools/*.cs)                 │
│   - garde externe ToolSafety (Tools/ToolSafety.cs)      │
│   - sérialisation JSON (Serialization/JsonOptions.cs)   │
│   - hébergement (Program.cs)                            │
└─────────────────────────────────────────────────────────┘
                         │  dans le processus
                         ▼
┌─────────────────────────────────────────────────────────┐
│  Winsight.Core                                          │
│   • Models        - enregistrements de données (DTO)    │
│   • UiAutomation  - FlaUI UIA3, recherche/BFS/actions   │
│   • Lifecycle     - lancer et fermer des applications   │
│   • Orchestration - lots run_steps et gardes            │
│   • ManagedState  - lecture du tas CLRMD sans suspendre │
│   • Capture       - PrintWindow PW_RENDERFULLCONTENT    │
│   • WebView2      - arbre de processus WMI + CDP        │
│   • Diagnostics   - compteurs + événements EventPipe    │
│   • Vision        - IElementGrounder + backends visuels │
│   • VisualRegression - comparaisons, attentes visuelles │
│   • UserActivity  - origine des saisies, bureau partagé │
│   • Clipboard     - texte, sauve/restaure, privé        │
│   • Ocr           - lecture de texte Windows.Media.Ocr  │
└─────────────────────────────────────────────────────────┘
                         │  API Win32 / de diagnostic .NET
                         ▼
┌─────────────────────────────────────────────────────────┐
│  Application cible (toute app WPF / Win32 / WebView2)   │
└─────────────────────────────────────────────────────────┘
```

## Responsabilités des modules

### Winsight.Core/Models

Enregistrements de données purs. Aucun comportement, aucune E/S.

- `WindowDescriptor`: instantané d'une fenêtre de premier niveau.
- `UiElementNode`: un noeud unique d'un arbre UIA récursif.
- `ElementLocator`: critères d'entrée pour trouver un élément (id/name/
  className exacts, controlType et nameContains insensibles à la casse,
  sélection par index).
- `ElementCandidate`: descripteur compact d'élément renvoyé lors des
  recherches ambiguës ou en échec.
- `ElementState`: instantané d'état au niveau de l'interface renvoyé par
  `get_element_state` (valeur, bascule, sélection, focus, géométrie).
- `WaitCondition` + `WaitOutcome`: énumération de conditions et charge utile
  de résultat de `wait_for_element`; `ClickStyle`: variantes de clic pour
  `click_element`. Les deux énumérations fournissent des analyseurs du format
  d'échange (`exists`/`not_exists`/..., `single`/`double`/`right`).
- `OperationResult`: enveloppe uniforme `{success, error, data}` utilisée par
  le chemin d'échec de chaque outil (et aussi par le chemin de succès de
  plusieurs outils).
- `ScreenshotResult`: chemin / dimensions / horodatage de capture renvoyés par
  `capture_screenshot` (dont l'outil joint aussi le PNG en ligne comme image
  MCP).
- `WebView2Endpoint`: descripteur d'un processus WebView2 descendant avec le
  port CDP avec lequel il a été lancé (le cas échéant) et l'indication que ce
  port écoute réellement ou non.
- `CounterSnapshot`: sortie de `observe_counters`: fournisseur, durée, nombre
  d'échantillons, et une table `counters` qui associe chaque nom de compteur à
  sa dernière valeur observée.
- `DiagnosticEvent` + `EventCaptureResult`: sortie de `capture_events`:
  métadonnées par événement (horodatage, fournisseur, nom, niveau, charge utile
  normalisée) bornées par un plafond configurable avec un indicateur
  `truncated`.
- `GroundedElement`: boîte englobante, confiance et raisonnement renvoyés par
  `find_element_visually`.
- `UserActivitySnapshot` et `ForegroundWindowInfo`: sortie de
  `get_user_activity` (temps d'inactivité humaine, source de la dernière
  saisie, fenêtre au premier plan dont le titre est masqué hors de la cible,
  session verrouillée).
- `ClipboardTextResult` et `TypeKeysResult`: texte du presse-papiers et sa
  longueur, et ce que `type_keys` a envoyé (mode, fenêtre, élément ayant le
  focus, restauration du presse-papiers).
- `WindowRegion`: un rectangle en coordonnées de fenêtre, partagé par les
  attentes visuelles, l'OCR et le recadrage des captures.
- `VisualTextResult`, `VisualTextLine`, `VisualTextWord`: sortie OCR avec des
  rectangles en coordonnées de fenêtre.

### Winsight.Core/Modules/UiAutomation

Encapsule FlaUI (UIA3):

- `WindowDiscovery`: énumère les fenêtres de premier niveau via Win32
  `EnumWindows` (les boîtes de dialogue possédées, les popups, les menus et les
  info-bulles sont ainsi découvrables, et pas seulement atteignables), classe
  chacune via `WindowClassifier` et résout les métadonnées du processus. Une
  restriction optionnelle par processus renvoie toute la pile de fenêtres d'une
  application, surfaces sans titre comprises. Utilise `GetForegroundWindow`
  pour l'indicateur de premier plan.
- `WindowClassifier`: classification pure d'une fenêtre en un type
  (main/dialog/popup/menu/tooltip/other) plus sa modalité, à partir de son nom
  de classe Win32, de ses styles caption/popup et de sa propriété; testable
  unitairement sans fenêtre réelle.
- `WindowGeometry`: résout les limites en coordonnées écran d'une fenêtre de
  processus (la plus haute, ou restreinte par titre), en reproduisant la
  sélection de fenêtre de `WindowCapture`.
- `WindowPointMapper`: traduction pure entre coordonnées relatives à la fenêtre
  et coordonnées écran (↔) et contrôles de limites; sert de base à
  `click_point` et aux coordonnées écran ajoutées aux résultats de
  `find_element_visually`.
- `UiTreeInspector`: parcours BFS borné de l'arbre UIA pour une fenêtre de
  processus donnée (profondeur 5 par défaut, plafonnée à 20), avec restriction
  optionnelle par titre de fenêtre et élagage par controlType/nameContains;
  les noeuds portent le contenu du motif Value.
- `AffordanceScanner`: la couche de perception derrière `describe_ui`: parcourt
  les fenêtres du processus en largeur d'abord, conserve les types de contrôles
  interactifs, réutilise `ElementStateReader.BuildState` pour chacun, et les
  associe à `AffordanceRecommender` (une fonction pure qui choisit le plus petit
  localisateur unique) pour renvoyer une liste compacte d'affordances triée par
  position.
- `UiSnapshotter` / `UiDiffer`: le sens de rétroaction derrière `snapshot_ui` /
  `diff_ui`: `UiSnapshotter` capture un `UiSnapshot` (fenêtres + affordances) en
  composant `WindowDiscovery` et `AffordanceScanner`; `UiDiffer` est une
  fonction pure qui compare deux instantanés pour produire un `UiChange`
  (fenêtres appariées par handle, éléments par une clé stable). La couche MCP
  conserve la référence par processus dans un `UiSnapshotStore` à portée de
  session.
- `NativeWindowEnumerator` (interne): liste via Win32 `EnumWindows` des
  fenêtres de premier niveau visibles d'un processus. Nécessaire parce qu'UIA
  rattache les fenêtres possédées (boîtes de dialogue modales) à leur
  propriétaire et les omet à la fois des énumérations du bureau et des enfants
  du propriétaire; enraciner les recherches sur les handles Win32 rend chaque
  fenêtre adressable.
- `LocatorMatcher`: sémantique de correspondance pure entre un
  `ElementLocator` et un instantané d'élément (exacte, sous-chaîne, quasi
  correspondance), testable unitairement sans UIA réel.
- `ElementFinder` (interne): résolution stricte d'un élément unique sur toutes
  les fenêtres du processus: échoue avec la liste des candidats en cas
  d'ambiguïté (pas de première correspondance silencieuse), suggère des quasi
  correspondances quand rien ne correspond, et tolère les `COMException` des
  éléments détruits en cours de parcours.
- `ActionInvoker`: clics (Invoke / Toggle / repli souris; variantes double clic
  et clic droit), clics par coordonnées (`ClickPoint`, en espace fenêtre ou
  écran, le partenaire d'action de l'ancrage visuel), saisie (motif Value
  inscriptible / repli clavier), sélection d'éléments dans les conteneurs
  ComboBox/ListBox/Tab, et défilement des éléments hors écran pour les rendre
  visibles avant les actions physiques. Les actions par coordonnées
  (`ClickPoint`, `ScrollPoint`, `HoverPoint`) partagent un même chemin:
  résoudre la fenêtre, éventuellement la mettre au premier plan
  (`focusWindow`), valider le destinataire du point, puis confier le point à
  `PointerDispatch`. `TypeKeys` saisit dans la fenêtre au premier plan de la
  cible sans élément UIA, sous forme de frappes ou d'un collage qui restaure le
  presse-papiers.
- `PointerDispatch`: chaque action physique du pointeur en trois temps:
  déplacer, attendre `settleMs` (150 ms par défaut), relire le pointeur
  (`cursor_moved` quand une autre saisie l'a déplacé), revalider le
  destinataire, puis appuyer, tourner la molette ou maintenir sans bouger de
  nouveau. Les interfaces en mode immédiat (iced sur winit) résolvent le survol
  une fois par image à partir du dernier `WM_MOUSEMOVE`; un déplacement et un
  appui envoyés ensemble agissent sur la position précédente. `IPointerDriver`
  permet aux tests unitaires d'enregistrer la séquence.
- `TextTyper`: planifie le texte en frappes `KEYEVENTF_UNICODE` (Entrée et
  Tab comme touches, paires de substitution comme deux unités) et les envoie à
  un rythme régulier, en vérifiant avant chaque frappe que la cible détient
  toujours le premier plan.
- `DesktopInputLease`: mutex à l'échelle de la session qui sérialise les
  saisies physiques entre les processus Winsight. Le bail le plus externe
  applique aussi la politique de priorité à l'utilisateur et enregistre sa
  fenêtre d'injection dans le registre des saisies (voir UserActivity).
- `ElementStateReader`: construit des instantanés `ElementState` à partir des
  éléments localisés (l'oracle au niveau de l'interface derrière
  `get_element_state`).
- `AssertionEvaluator`: évaluation pure d'opérateurs (equals/contains/
  greater_than/checked/enabled/...) sur un `ElementState`, derrière l'outil
  `assert_element` et l'étape run_steps `assert`; les opérateurs textuels
  comparent Value, avec repli sur Name.
- `ElementWaiter`: interroge un localisateur jusqu'à ce qu'une `WaitCondition`
  soit vérifiée ou qu'un délai expire (`UiaDefaults` porte les valeurs par
  défaut et les plafonds).
- `UiaDefaults`: délais, intervalles d'interrogation et plafonds de candidats
  centralisés; le délai d'attente peut être remplacé via
  `WINSIGHT_UIA_WAIT_TIMEOUT_MS`.

### Winsight.Core/Modules/Lifecycle

- `AppLauncher`: démarre un exécutable et attend que sa fenêtre soit présente
  (fenêtre principale, ou une fenêtre correspondant à un fragment de titre)
  avant de renvoyer un `LaunchResult` classé, ce qui supprime la course au
  démarrage du lancement suivi d'interrogations; ferme aussi un processus
  proprement (`CloseMainWindow`), en préservant une application toujours en
  cours d'exécution, ou ne la termine que si l'arrêt forcé est explicitement
  demandé. Réutilise `WindowDiscovery` pour détecter la fenêtre prête.

### Winsight.Core/Modules/Orchestration

- `StepRunner`: exécute une séquence `BatchStep[]` en une passe, en confiant
  chaque étape aux primitives existantes d'action/synchronisation/oracle/capture
  (aucune logique dupliquée). Les étapes sont isolées des exceptions (un échec
  devient le résultat de cette étape, pas l'abandon du lot), stop-on-error
  s'arrête au premier échec, et l'exécution renvoie un journal `BatchResult`.
  Sert de base à l'outil `run_steps`. Avant chaque étape qui modifie l'écran, il
  applique la politique de priorité à l'utilisateur et, sauf si
  `stopOnForegroundChange` est désactivé, `ForegroundGuard`. Quand une étape
  `wait_visual_change` suit une étape qui modifie l'écran, l'exécuteur capture
  l'image de référence avant cette étape, pour ne pas manquer un changement
  rapide. L'étape `assert` avec l'opérateur `text_visible` lit la fenêtre par
  OCR.
- `ForegroundGuard`: règle pure qui arrête un lot quand le premier plan est
  passé, entre deux étapes, à une fenêtre extérieure aux processus du lot
  (quelqu'un a pris l'écran). Les boîtes de dialogue de la cible, les
  transitions sans premier plan et la perte de la fenêtre de référence sont
  tolérées. `IForegroundReader` permet aux tests de scénariser le premier plan.

### Winsight.Core/Modules/ManagedState

Encapsule Microsoft.Diagnostics.Runtime (CLRMD).

- `ManagedStateReader`: s'attache sans suspension à un processus managé,
  parcourt le tas, trouve la première instance du type demandé et lit un champ
  nommé. Prend en charge les types d'éléments primitifs et les résumés de
  références.

Le processus hôte **doit** correspondre à l'architecture de la cible (x64 par
défaut dans ce dépôt). L'inspection entre architectures différentes n'est pas
prise en charge.

### Winsight.Core/Modules/Capture

Capture d'écran de fenêtre via l'API Win32 `PrintWindow` avec l'indicateur
`PW_RENDERFULLCONTENT` (Windows 8.1+).

- `WindowCapture`: localise les fenêtres Win32 de premier niveau visibles par
  id de processus, filtre éventuellement par sous-chaîne du titre, alloue un
  `Bitmap` correspondant, appelle `PrintWindow` et enregistre un PNG au chemin
  demandé. Les captures de fenêtre complète ciblent par défaut la fenêtre
  visible la plus haute dans l'ordre z, pour que les boîtes de dialogue modales
  et les popups soient adressables; les captures de référence de la zone
  cliente privilégient la fenêtre principale du processus sauf si une
  restriction par titre est fournie. `CaptureFrame` renvoie la même capture de
  fenêtre complète en mémoire, éventuellement recadrée sur une `WindowRegion`,
  sous forme de `CapturedFrame` (bitmap, identité de la fenêtre, région) pour
  les attentes visuelles et l'OCR.

C'est l'indicateur `PW_RENDERFULLCONTENT` qui permet à l'appel de rendre
correctement les fenêtres WPF composées par DirectX. Sans lui, les surfaces
accélérées reviennent sous forme de rectangles noirs.

### Winsight.Core/Modules/WebView2

Découvre les instances WebView2 intégrées et leurs points de terminaison
Chrome DevTools Protocol, pour que les agents IA puissent attacher
Playwright/Puppeteer via CDP.

- `WebView2Detector`: parcours WMI de `Win32_Process` en une seule requête,
  traverse l'arbre des descendants à partir d'un pid hôte, et signale chaque
  enfant `msedgewebview2.exe` avec l'éventuel `--remote-debugging-port=N` avec
  lequel il a été lancé. Recoupe avec
  `IPGlobalProperties.GetActiveTcpListeners()` pour confirmer que le port
  écoute réellement.
- `WebView2CommandLineParser`: fonction pure utilitaire qui extrait le port de
  débogage d'une ligne de commande de processus. Facile à tester unitairement.

Ce module ne démarre ni ne modifie l'environnement WebView2. L'activation de
CDP doit se faire dans le code de l'application hôte (via
`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` ou
`CoreWebView2EnvironmentOptions.AdditionalBrowserArguments`); le détecteur
signale seulement ce qui existe déjà et émet une indication exploitable quand
rien d'utilisable n'est trouvé.

### Winsight.Core/Modules/Diagnostics

Observabilité non intrusive des processus managés (.NET 6+) via l'IPC
EventPipe officiel, le même canal qu'utilisent `dotnet-counters` et
`dotnet-trace`. Point essentiel, il ne s'agit **pas** d'ETW brut: aucun
privilège administrateur requis, aucune heuristique `CreateRemoteThread` que
les produits EDR signalent, et aucune modification du code source de la cible.

- `DiagnosticsObserver.ObserveCounters`: ouvre une session EventPipe pour un
  fournisseur avec `EventCounterIntervalSec=1`, écoute les événements
  `EventCounters` pendant une fenêtre bornée (de 500 ms à 30 s), agrège la
  dernière valeur `Mean` (compteurs moyennés) ou `Increment` (compteurs
  monotones) observée par nom de compteur, et renvoie l'instantané.
- `DiagnosticsObserver.CaptureEvents`: ouvre une session EventPipe couvrant
  un ou plusieurs fournisseurs, capture chaque événement (ou seulement ceux qui
  correspondent à un filtre de nom exact), normalise les charges utiles en
  représentations compatibles JSON, et renvoie une liste bornée avec un
  indicateur `Truncated` quand le plafond est atteint. Le plafond par défaut
  est de 500 événements; 10 000 au maximum.
- `DiagnosticsObserver.CaptureExceptionsDuring`: l'oracle causal derrière
  `run_steps captureExceptions`: démarre une session EventPipe d'exceptions
  CLR, laisse le système se stabiliser, exécute l'action fournie, vide les
  tampons et s'arrête, en renvoyant les exceptions levées pendant l'action.
  Utilise le `ClrTraceEventParser` fortement typé (`ExceptionStart`) parce que
  l'analyseur Dynamic ne sait pas décoder les charges utiles
  `Microsoft-Windows-DotNETRuntime` basées sur un manifeste via EventPipe.
- `PayloadNormalizer`: fonction pure utilitaire qui convertit des valeurs
  arbitraires de charge utile TraceEvent en une représentation compatible JSON
  (les primitives passent telles quelles, `DateTime` est converti en UTC,
  `Guid` et les énumérations deviennent des chaînes, les tableaux d'octets
  deviennent de l'hexadécimal, les dictionnaires et énumérables sont parcourus
  récursivement avec une profondeur plafonnée à 4). Facile à tester
  unitairement.

L'observateur est l'oracle recommandé, pour deux raisons qui le placent devant
CLRMD:

1. Sûr vis-à-vis des EDR en environnement d'entreprise. L'attachement CLRMD
   utilise des heuristiques `CreateRemoteThread`/`WriteProcessMemory` qui
   correspondent à des motifs de logiciels malveillants et déclenchent
   CrowdStrike Falcon, SentinelOne et Microsoft Defender for Endpoint. L'IPC
   EventPipe, non.
2. Sémantique de série temporelle. Les compteurs donnent le taux
   d'allocation, le taux d'exceptions, la fréquence des GC, la croissance du
   pool de threads: utile pour des oracles du type "ce scénario a-t-il fui?"
   auxquels une lecture ponctuelle d'un champ ne peut pas répondre.

CLRMD reste le bon outil pour lire un champ précis sur une instance précise,
en particulier pendant une étape de test unique et déterministe.

### Winsight.Core/Modules/Vision

Localise les éléments d'interface qu'UIA ne voit pas (Canvas personnalisé,
SkiaSharp, surfaces DirectX, contenu WebView2) en soumettant une capture de
fenêtre accompagnée d'une requête en langage naturel à un modèle de vision, et
en relisant une boîte englobante.

- `IElementGrounder`: le contrat: `FindAsync(screenshotPath, query, ct)`
  renvoie un `GroundedElement` (coordonnées du centre, boîte englobante,
  confiance, raisonnement, source).
- `AnthropicVisionGrounder`: implémentation par défaut. Lit
  `ANTHROPIC_API_KEY` dans l'environnement, appelle l'API Anthropic Messages
  avec la capture encodée en base64 et une invite très contrainte qui exige une
  sortie uniquement en JSON. Le modèle par défaut est `claude-sonnet-4-6`; le
  remplacer via `WINSIGHT_VISION_MODEL`. Le point de terminaison lui-même peut
  être remplacé via `WINSIGHT_VISION_API_URL` pour des proxys auto-hébergés ou
  un routage par une passerelle d'entreprise.
- `AnthropicResponseParser`: extrait l'objet JSON du modèle de l'enveloppe de
  réponse, de façon robuste à la prose qui l'entoure, aux blocs de code
  markdown, aux chaînes numériques et aux accolades dans le texte de
  raisonnement.
- `OnnxLocalGrounder`: sélectionnable avec `WINSIGHT_VISION_BACKEND=onnx`. Un
  backend entièrement local qui exécute Qwen2.5-VL-3B sous forme d'un découpage
  en 3 ONNX (encodeur de vision + plongement des tokens + décodeur) sur ONNX
  Runtime simple, avec une boucle de génération mRoPE écrite à la main. Il
  valide `WINSIGHT_VISION_ONNX_PATH` et exige `WINSIGHT_VISION_EP=cpu`, échoue
  explicitement en cas de mauvaise configuration, et ne se replie jamais sur le
  cloud. L'exécution est limitée au CPU dans cette version (l'opérateur fusionné
  MultiHeadAttention du décodeur n'est pas encore pris en charge par l'EP
  DirectML: un suivi est ouvert).
- `QwenImagePreprocessing` / `QwenVisionTokenizer`: réimplémentations en C# du
  smart-resize + patchify de Qwen2.5-VL et du tokeniseur BPE au niveau octet
  (via `CodeGenTokenizer` sur un vocabulaire complété des tokens spéciaux de
  Qwen).
- `GroundingCoordinateParser`: transforme la sortie textuelle du modèle, point
  `[x,y]` ou boîte `[x1,y1,x2,y2]` (pixels absolus dans l'espace de l'image
  redimensionnée), en un centre de clic unique; le grounder la dénormalise en
  pixels de l'image d'origine et la stocke dans `GroundedElement` sous forme de
  boîte de surface nulle.

#### Pourquoi une API de vision cloud d'abord

Le backend cloud est livré par défaut pour un ancrage visuel sans
configuration. Le backend ONNX local est optionnel et a un coût de mise en
place: un modèle de plusieurs gigaoctets (téléchargé hors du dépôt) et un
prétraitement/une tokenisation côté C#. Voir
[docs/local-onnx-grounder.md](local-onnx-grounder.md).

Passer par l'API Anthropic donne dès aujourd'hui l'ancrage visuel à l'agent,
avec pour contrepartie que les captures quittent l'hôte. L'interface
(`IElementGrounder`) est la garantie de compatibilité future: quand le chemin
ONNX local sera éprouvé et comptera plus que la commodité du cloud (contenu
réglementé, CI hors ligne, exécutions à fort volume), `OnnxLocalGrounder` pourra
évoluer derrière le même contrat sans toucher à aucune surface d'outil.

### Winsight.Core/Modules/VisualRegression

Compare une capture d'interface à une référence approuvée à l'aide de mesures
d'image déterministes. Ce module n'utilise volontairement pas
Anomalib/PatchCore: Winsight conduit l'application dans des états connus, il
dispose donc d'une référence et n'a pas besoin de détection de nouveauté non
supervisée.

- `VisualDiffer`: charge deux PNG de même taille, calcule le SSIM en niveaux
  de gris (gaussienne 11x11, sigma 1.5) à la fois comme moyenne globale et
  comme le pire bloc NxN (SSIM local, pour détecter les petits changements
  localisés), calcule un ratio de différence de pixels YIQ à la manière de
  pixelmatch avec détection des bords anticrénelés, et écrit une carte de
  chaleur rouge optionnelle.
- `VisualDiffOptions`: seuils SSIM global + local, seuil de différence de
  pixels, seuil de couleur pixelmatch, rectangles ignorés et chemin optionnel
  de la carte de différences. Les valeurs par défaut sont lues depuis
  `WINSIGHT_VISUAL_SSIM_THRESHOLD`, `WINSIGHT_VISUAL_LOCAL_SSIM_THRESHOLD`,
  `WINSIGHT_VISUAL_PIXEL_THRESHOLD` et `WINSIGHT_VISUAL_COLOR_THRESHOLD`.
- `VisualDiffResult`: verdict réussite/échec (ou référence créée/mise à jour),
  SSIM global, minimum du SSIM local, ratio de différence de pixels, chemin de
  la carte de différences et raisonnement renvoyés par
  `compare_visual_baseline`. Les captures utilisent la zone cliente de la
  fenêtre pour le déterminisme.
- `VisualIgnoreRegion`: DTO de rectangle compatible JSON utilisé par l'outil
  MCP pour masquer l'interface dynamique comme les horloges, les curseurs, le
  clignotement du caret ou les animations.

La comparaison échoue franchement sur les différences de taille d'image. Les
rectangles ignorés excluent des pixels à la fois du dénominateur SSIM et du
ratio de différence de pixels.

- `VisualDiffer.Compare(Bitmap, Bitmap, options)`: la même comparaison sur des
  images en mémoire, utilisée pour surveiller une fenêtre plutôt que vérifier
  un fichier de référence.
- `VisualWaiter`: sert de base à `wait_for_visual_change` (interroger jusqu'à
  ce qu'une capture diffère d'une référence) et à `wait_for_visual_stable`
  (interroger jusqu'à ce qu'un nombre de comparaisons consécutives ne montre
  aucun changement). Les boucles reçoivent une source d'images et un
  `IWaitClock` injectés, et sont donc testées sans bureau.

### Winsight.Core/Modules/UserActivity

Distingue les saisies d'une personne de celles de Winsight sur un bureau
partagé, sans hook bas niveau (que les produits de sécurité des postes
signalent).

- `InputLedger`: mémoire partagée nommée
  (`Local\Winsight.InputLedger.<session>`) qui contient la dernière fenêtre
  d'injection (début, fin, en cours) et la dernière saisie attribuée à un
  humain. Chaque processus Winsight de la session y écrit ses injections via
  `DesktopInputLease`.
- `InputAttributor`: attribution pure par le temps: la dernière saisie
  (`GetLastInputInfo`) appartient à Winsight quand elle tombe dans la fenêtre
  d'injection élargie de 50 ms, à un humain sinon. Gère le rebouclage du
  compteur de ticks 32 bits et ignore une injection laissée active par un
  processus planté au bout de 60 s.
- `UserActivityMonitor`: instantané pour `get_user_activity` (temps
  d'inactivité humaine, fenêtre et processus au premier plan, session
  verrouillée via `WTSQuerySessionInformation`) et interrogation derrière
  `wait_for_user_idle`.
- `UserActivityPolicy`: lit `WINSIGHT_YIELD_TO_USER_MS`; les outils qui
  modifient l'écran, les étapes `run_steps` et le bail de saisie le plus
  externe échouent avec `user_active` tant qu'une saisie humaine est plus
  récente que le seuil.

Limites connues: une saisie faite par une personne pendant une injection de
Winsight est masquée par celle-ci, et une saisie injectée par un autre outil
compte comme humaine.

### Winsight.Core/Modules/Clipboard

- `ClipboardThread`: un thread STA dédié qui possède une fenêtre réservée aux
  messages et pompe ses messages pendant toute la vie du processus. Le
  propriétaire du presse-papiers reçoit des messages quand une autre
  application prend le presse-papiers; un propriétaire qui ne pomperait pas le
  bloquerait.
- `ClipboardService`: lit et écrit du texte, et sauvegarde et restaure chaque
  format du presse-papiers contenu dans un bloc mémoire (les bitmaps, les
  métafichiers et les handles privés sont signalés comme ignorés). Le texte
  qu'écrit Winsight porte `ExcludeClipboardContentFromMonitorProcessing`,
  `CanIncludeInClipboardHistory=0` et `CanUploadToCloudClipboard=0`.
- `ClipboardPolicy`: chaque outil de presse-papiers et `type_keys mode=paste`
  refusent avec `clipboard_disabled` sauf si `WINSIGHT_ALLOW_CLIPBOARD` est
  défini.

### Winsight.Core/Modules/Ocr

- `VisualTextReader`: capture la fenêtre ou une région en mémoire, l'agrandit
  (x2 par défaut, borné par le maximum du moteur) et exécute
  `Windows.Media.Ocr`, hors ligne, dans la langue demandée ou dans les langues
  du profil utilisateur. Les lignes et les mots reviennent avec des rectangles
  projetés en coordonnées de fenêtre (`OcrGeometry`). C'est pourquoi Core cible
  `net10.0-windows10.0.19041.0`.
- `VisualTextMatcher`: la comparaison `text_visible`: insensible à la casse,
  espaces regroupés, ligne par ligne et à travers les lignes.

### Winsight.Mcp

Un hôte `Microsoft.Extensions.Hosting` qui enregistre les modules du moteur
comme singletons, puis les branche sur
`AddMcpServer().WithStdioServerTransport()`. Les outils sont découverts par
réflexion à partir de toute classe `[McpServerToolType]` de l'assembly.

Un singleton `SessionTarget` conserve le processus par défaut de la session:
les outils à portée de processus acceptent un `processId` optionnel et se
replient sur lui (`set_target` / `get_target` / `clear_target` le gèrent), si
bien qu'un agent qui pilote une seule application n'a pas besoin de répéter
l'id.

`ToolListAnnouncer` envoie `notifications/tools/list_changed` une fois le
client initialisé. `ServerBuildInfo` fournit à `get_winsight_guide` la version
compilée qu'exécute ce processus (version du produit avec le commit, date du
fichier, démarrage du processus), les outils qu'il a enregistrés, et indique si
la sortie source depuis laquelle `tools\run-mcp.ps1` l'a copié a été recompilée
depuis (`WINSIGHT_SOURCE_BUILD_DIR`). Une session cliente conserve le
processus serveur avec lequel elle a démarré, si bien qu'une liste d'outils
périmée y est visible.

La journalisation est forcée sur stderr: stdout est réservé au protocole MCP.

## Threads et durée de vie

- Chaque appel d'outil construit et libère sa propre instance
  `UIA3Automation`. Les outils restent ainsi sans état et on évite les
  surprises d'appartements COM entre requêtes MCP parallèles.
- Le `DataTarget` de CLRMD a de même une portée limitée à l'appel.
  L'attachement se fait sans suspension (`suspend: false`) pour éviter les
  problèmes d'interaction avec le GC sur .NET 9 / .NET 10.

## Sécurité à la frontière des outils

Chaque méthode d'outil de `Winsight.Mcp` est encapsulée par
`ToolSafety.SafeInvoke` (`SafeInvokeAsync` pour l'outil de vision,
`SafeInvokeContent` pour les outils de capture qui renvoient une image). La
garde:

- Intercepte toute exception non fatale levée dans le corps de l'outil et
  renvoie un JSON structuré `OperationResult { success: false, error }` que
  l'agent peut lire pour se rétablir. `SafeInvokeContent` renvoie le même JSON
  d'échec dans un bloc de texte d'un `CallToolResult` en erreur, si bien que la
  forme de l'erreur est identique qu'un outil renvoie une chaîne ou un contenu
  riche (texte plus image en ligne).
- Reproduit l'exception d'origine (avec la pile complète) sur **stderr** pour
  qu'une personne qui s'attache au processus hôte puisse voir ce qui s'est
  passé. Stdout reste réservé au trafic du protocole MCP.
- Enregistre un événement d'outil JSONL compact quand `WINSIGHT_TRACE` ou
  `WINSIGHT_TRACE_DIR` est défini: nom de l'outil, durée, succès/erreur et
  nombre d'octets de la charge utile.
- Laisse se propager les exceptions réellement fatales
  (`OutOfMemoryException`, `StackOverflowException`, `ThreadAbortException`):
  les masquer cacherait une défaillance de tout le processus.

Pourquoi cette garde existe plutôt que de s'appuyer sur la gestion d'erreurs
propre au framework MCP: on a observé qu'une seule exception non gérée dans
n'importe quel outil (une `COMException` issue d'un appel UIA, un échec
d'attachement CLRMD sur un processus inaccessible) **terminait le processus
hôte .NET**, ce qui ferme le transport stdio et rend tous les autres outils
définitivement inutilisables pour le reste de la session de l'agent. La garde
universelle cantonne l'échec au seul outil appelant.

## Politique et rejeu

`SessionTarget` est aussi le point de passage obligé de la politique de
processus. `TryResolve` et `TrySet` appliquent des listes d'autorisation
optionnelles avant l'exécution de tout outil à portée de processus:
`WINSIGHT_ALLOWED_PROCESS_IDS`, `WINSIGHT_ALLOWED_PROCESS_NAMES`, et
`WINSIGHT_ALLOWED_EXECUTABLES` pour `launch_app`.
`ToolAccessPolicy.GuardMutation` contrôle chaque outil qui modifie l'écran:
d'abord l'aperçu en simulation, puis le refus au titre de la priorité à
l'utilisateur. Les outils CDP exigent aussi que le port demandé soit une socket
en écoute appartenant à un processus WebView2 découvert sous le processus hôte
autorisé, si bien qu'un port de boucle locale brut ne suffit pas à contourner
la politique de processus. `WINSIGHT_DRY_RUN` bloque les outils MCP qui
modifient l'état (`click_*`, saisie, sélection, combinaisons de touches,
évaluation JS, lancement/fermeture, et écritures de captures de `run_steps`) et
renvoie à la place un aperçu structuré.

`run_steps` peut écrire un artefact de rejeu contenant le lot préparé et le
journal. `replay_steps` lit cet artefact et le réexécute, en simulation par
défaut, pour qu'un agent puisse déboguer un scénario avant d'en répéter les
effets de bord.

## Couche d'expérience agent

`AgentExperienceTool` est la surface de haut niveau destinée aux agents IA:

- `observe` fusionne la découverte des fenêtres, l'analyse des affordances,
  les prochaines actions probables, la mémoire de session et des captures
  optionnelles.
- Chaque affordance observée porte un `affordanceId` stable; `nextActions`
  inclut l'outil préféré, les arguments du localisateur, les valeurs par défaut
  de simulation, le risque et la rétroaction attendue, pour que les agents
  puissent agir sans redéduire la forme de l'appel.
- `plan_steps` transforme un objectif humain en un brouillon `BatchStep`
  prudent qu'il convient d'exécuter en simulation avant l'exécution réelle.
- `repair_locator` classe les candidats de l'interface courante et de la
  mémoire quand un localisateur échoue.
- `act_and_observe`, `click_and_wait` et `type_and_assert` condensent en un
  seul appel les boucles courantes action/synchronisation/assertion/diff.
- `get_agent_state` renvoie la cible courante, les garde-fous, la mémoire, le
  dernier échec, la prochaine étape recommandée et les recettes intégrées.
- `get_winsight_guide` expose directement la boucle préférée via MCP, si bien
  qu'un agent n'a pas besoin de documentation externe dans son contexte.

`SessionMemory` enregistre les localisateurs récemment observés par
processus/fenêtre et conserve une courte liste d'échecs réparables. Elle est
volontairement à portée de session, et non persistée sur disque. Les échecs
réparables de haut niveau partagent la même forme de données: `code`, `cause`,
`suggestedTool`, `suggestedArgs` optionnel, `candidates` optionnel, et
`retryable`.

## Modes de défaillance et leur signalement

| Situation                                 | Comportement                                          |
| ----------------------------------------- | ----------------------------------------------------- |
| La fenêtre n'a pas de nom                 | Ignorée par `list_windows` sur tout le bureau; incluse dans une liste par processus |
| Exception COM sur une seule fenêtre       | Interceptée localement, cette fenêtre est ignorée     |
| Élément introuvable                       | `OperationResult.Failure(...)` (success = false)      |
| Le processus n'a pas de runtime managé    | `OperationResult.Failure(...)`                        |
| Type/champ introuvable dans le tas        | `OperationResult.Failure(...)`                        |
| Incompatibilité d'architecture / d'élévation | L'attachement CLRMD lève une exception; ToolSafety l'encapsule en `OperationResult.Failure(message)` et l'hôte reste actif |
| Toute autre exception non gérée dans un outil | ToolSafety l'encapsule en `OperationResult.Failure(message)`, journalise la pile complète sur stderr, rend la main à l'appelant |
| Une personne a utilisé le bureau plus récemment que `WINSIGHT_YIELD_TO_USER_MS` | `user_active: ...`, rien n'est envoyé |
| Le premier plan a changé entre deux étapes de `run_steps` | Le lot s'arrête avec `foreground_changed` et `foregroundChanged: true` |
| Le pointeur a bougé pendant une stabilisation | `cursor_moved: ...`, l'appui, la molette ou le maintien n'est pas envoyé |
| Outil de presse-papiers sans `WINSIGHT_ALLOW_CLIPBOARD` | `clipboard_disabled: ...` |
| Aucun composant OCR pour la langue        | `ocr_unavailable: ...` avec la liste des langues installées |
| Une attente visuelle ou d'inactivité n'est jamais satisfaite | `timed_out: ...` avec les dernières mesures |

## Ce qui est volontairement absent à ce stade

- L'accélération GPU du grounder ONNX local. Le backend local Qwen2.5-VL est
  livré (CPU); l'exécution DirectML/CUDA est un suivi (l'opérateur fusionné
  MultiHeadAttention du décodeur n'est pas encore pris en charge par l'EP
  DirectML). Voir [local-onnx-grounder.md](local-onnx-grounder.md).
- La détection de nouveauté visuelle sans référence. Le chemin de régression
  visuelle livré repose sur une référence avec SSIM + différence de pixels; la
  détection d'anomalies non supervisée serait une fonctionnalité distincte si
  un besoin concret apparaît plus tard.
- L'instrumentation par injection dans le processus (injection de DLL,
  MinHook).
- Les hooks de saisie bas niveau (`WH_MOUSE_LL`, `WH_KEYBOARD_LL`) dans le
  serveur. L'activité de l'utilisateur est plutôt attribuée par le temps via le
  registre des saisies; le hook clavier du laboratoire WPF est une cible de
  test, pas une partie de Winsight.
- La projection de coordonnées compatible DPI pour les configurations
  multi-écrans.
- Le hook D3D11 `Present` pour capturer les surfaces qu'UIA ne voit pas.
- L'inspection UIA / CLRMD entre niveaux d'élévation différents. Un hôte
  winsight exécuté en utilisateur standard ne peut pas inspecter les processus
  qui déclarent `<requestedExecutionLevel level="requireAdministrator" />` ni
  s'y attacher. Les échecs d'outils remontent proprement via `ToolSafety`, mais
  les opérations elles-mêmes ne peuvent pas réussir. Pour piloter des
  applications élevées, exécuter l'hôte winsight au même niveau d'intégrité ou
  à un niveau supérieur.

Ces points sont suivis dans la matrice des capacités du README.
