# Référence des outils

*[English version](../tools-reference.md)*

Ce document décrit les outils MCP exposés par `Winsight.Mcp` aux agents d'IA.
Pour le flux vérifié recommandé, voir [contrôle vérifié](verified-control.md).
`act_verified` renvoie des champs explicites `status`, `dispatched` et
`verified`; l'exécution de profil indique aussi son état vérifié/arrêté. La
convention OperationResult ci-dessous décrit les outils d'origine et les appels
d'observation et de configuration.
Chaque outil renvoie une chaîne JSON. Les erreurs, qu'elles proviennent d'un mode
de défaillance connu ou d'une exception inattendue dans le corps de l'outil, sont
uniformément remontées dans une forme `OperationResult`
(`{ "success": false, "error": "..." }`) plutôt que sous forme d'exceptions MCP,
afin qu'un agent puisse les lire et s'en remettre.
Une garde universelle `ToolSafety` à la frontière de l'outil intercepte tout ce
que l'implémentation lève et le convertit dans cette forme: une seule exception
incontrôlée ne peut donc ni abattre le processus hôte ni tuer le reste de la
session de l'agent.

## Cible de session (`set_target` / `get_target` / `clear_target`)

Chaque outil limité à un processus prend `processId`, mais vous n'avez pas à le
répéter à chaque appel. `set_target` enregistre un **processus par défaut de la
session**; ensuite, tout outil qui omet son propre `processId` l'utilise. Un
`processId` explicite sur un appel reste prioritaire sur la valeur par défaut.
C'est le pendant ad hoc du `processId` de niveau lot de [`run_steps`](#run_steps).

| Outil          | Rôle                                                                       |
| -------------- | -------------------------------------------------------------------------- |
| `set_target`   | Définit le processus par défaut de la session. Paramètre `processId` (int). Renvoie `{ processId, windowCount }`: `windowCount` est le nombre de fenêtres de premier niveau que le processus expose actuellement (0 est accepté; il peut être encore en cours de démarrage). Appelez-le juste après `launch_app` ou `list_windows`. |
| `get_target`   | Renvoie `{ processId }`, ou `{ processId: null }` si aucun n'est défini.    |
| `clear_target` | Efface la valeur par défaut; les appels suivants doivent de nouveau passer `processId`. |

Quand le `processId` d'un outil est omis et qu'aucune cible de session n'est
définie, l'appel échoue avec un message clair qui vous demande de passer
`processId` ou d'appeler `set_target`. Dans toute cette référence, `processId`
sur un outil limité à un processus est donc **facultatif** avec ce repli, même
là où les tables de paramètres le marquent comme requis pour l'appel au moteur
sous-jacent.

## Flux de travail orienté agent

Les outils recommandés sont `observe_scene`, `act_verified`, `run_verified_steps`,
`wait_any`, `capture_scene`, `watch_ui_events`, `audit_ui_accessibility`,
`select_file_dialog`, `switch_target`, `observe_dom_scene`, `locate_scene_target`,
`list_app_profiles`, `save_app_profile`, `save_verified_history_as_profile`,
`run_app_profile` et `delete_app_profile`.
Leurs [contrats et exemples](verified-control.md) distinguent
l'envoi d'une action des résultats vérifiés. Les flux existants listés
ci-dessous restent compatibles.

Au démarrage, `Winsight.Mcp` envoie des `ServerInstructions` MCP qui résument
cette boucle, déclare les annotations MCP (`ReadOnly`, `Destructive`, `Idempotent`,
`OpenWorld`) sur chaque outil, et expose les ressources MCP en lecture seule
(`winsight://guide`, `winsight://state`, `winsight://windows`,
`winsight://scene/latest`) ainsi que les prompts MCP (`explore_app`,
`verify_flow`, `diagnose_ui_bug`). Définir `WINSIGHT_TOOL_PROFILE=compact` pour
exposer un jeu réduit de 33 outils autonomes au lieu des 70 outils complets.

Ces outils de commodité d'origine restent disponibles. Les outils de plus bas
niveau restent disponibles, mais les agents devraient commencer ici, car les
réponses sont orientées vers une action réparable.

| Outil | Rôle |
| ---- | ------- |
| `observe` | Perception en un appel: fenêtres, éléments actionnables, localisateurs recommandés, actions probables, mémoire de session, chemin de capture d'écran facultatif |
| `plan_steps` | Ébauche un plan `run_steps` prudent à partir d'un objectif en langage naturel; simulez-le avant de l'exécuter |
| `repair_locator` | Classe des localisateurs de remplacement à partir de l'UI actuelle et de la mémoire des localisateurs observés |
| `act_and_observe` | Exécute un `BatchStep` et renvoie le journal d'action ainsi que le diff structurel avant/après |
| `click_and_wait` | Clique sur un localisateur et attend une condition après le clic, en un seul appel |
| `type_and_assert` | Saisit du texte et vérifie immédiatement un état observable |
| `get_agent_state` | Renvoie un contexte compact: cible, garde-fous, mémoire, dernier échec et recettes |
| `get_session_memory` / `clear_session_memory` | Inspecte ou réinitialise les localisateurs observés et les échecs réparables récents |
| `get_winsight_guide` | Renvoie le guide pratique intégré destiné aux agents |

### `observe`

Utilisez `observe` avant d'agir sur une UI inconnue. Il combine `list_windows`,
`describe_ui`, la mémoire de session, les prochaines actions probables et une
capture d'écran facultative.

| Nom | Type | Défaut | Description |
| ---- | ---- | ------- | ----------- |
| `processId` | int? | cible de session | Processus cible |
| `windowTitle` | string? | null | Limite à une sous-chaîne du titre de fenêtre |
| `includeScreenshot` | bool | false | Inclut un chemin de capture d'écran écrit sous `WINSIGHT_OUTPUT_DIR` |
| `maxResults` | int | 100 | Nombre maximal d'éléments actionnables |

La réponse est un `OperationResult` dont `data` contient `windows`, `elements`,
`nextActions`, `screenshot` (facultatif) et `memory`. Chaque élément actionnable
inclut un `affordanceId` stable ainsi que son `recommendedLocator`; chaque
entrée de `nextActions` nomme l'outil recommandé, des arguments prêts à remplir,
les entrées requises, le risque, le retour attendu et `locatorArgs`.

### Planification et réparation

`plan_steps(goal, text?, expectedValue?)` classe les contrôles visibles et
renvoie une charge utile brouillon `{ dryRun: true, processId, steps }` pour
`run_steps`. `repair_locator(query)` renvoie des candidats `currentUi` classés,
avec `locatorArgs`, et les correspondances en mémoire. En cas de cible manquante
ou d'arguments invalides, ces outils de haut niveau renvoient des données
d'échec structurées:

```json
{
  "success": false,
  "error": "No processId given ...",
  "data": {
    "code": "missing_target",
    "message": "No processId given ...",
    "cause": "target_resolution",
    "suggestedTool": "set_target",
    "suggestedArgs": { "processId": "<target process id>" },
    "retryable": true
  }
}
```

### État de l'agent et recettes intégrées

`get_agent_state` est l'appel d'orientation compact pour les agents qui ont
perdu le contexte. Il renvoie:

- `target`: l'id du processus de la session et l'existence ou non d'une cible.
- `guardrails`: l'état de la simulation, de la liste d'autorisation et de
  la trace.
- `memory`: les localisateurs récemment observés et les échecs réparables
  récents.
- `lastFailure`: l'échec le plus récent, s'il existe.
- `recommendedNextStep`: la prochaine étape de reprise ou d'action, sous forme
  de chaîne courte.
- `recipes`: des flux intégrés tels que `form_fill`, `dialog_flow`,
  `visual_click`, `webview_cdp` et `locator_repair`.

### Outils macro

`act_and_observe` est la macro générique en une étape. Passez un seul
`BatchStep`; elle renvoie le résultat de l'action, le `change` structurel, les
`elements` actuels et un chemin de capture d'écran facultatif. `click_and_wait`
et `type_and_assert` sont des macros plus étroites pour les boucles courantes.
Les outils macro renvoient `success=false` au niveau supérieur quand une action,
une attente ou une assertion interne échoue; l'objet `data` porte toujours le
journal détaillé des résultats internes pour la réparation.

Tous les outils de haut niveau qui modifient l'état respectent
`WINSIGHT_DRY_RUN`. Pour `act_and_observe`, la simulation globale se
comporte comme l'argument explicite `dryRun=true`: Winsight capture toujours
les observations avant/après et journalise l'étape qui aurait été exécutée, mais
ne clique pas, ne saisit pas, ne sélectionne pas et ne survole pas.

#### `act_and_observe`

| Nom | Type | Défaut | Description |
| ---- | ---- | ------- | ----------- |
| `step` | `BatchStep` | requis | Une étape à exécuter ou à simuler |
| `windowTitle` | string? | null | Limite l'observation à une sous-chaîne du titre de fenêtre |
| `includeScreenshot` | bool | false | Inclut le chemin d'une capture d'écran après l'action |
| `dryRun` | bool | false | Journalise les actions modifiantes sans les exécuter |
| `processId` | int? | cible de session ou processus de l'étape | Processus cible |

#### `click_and_wait`

| Nom | Type | Défaut | Description |
| ---- | ---- | ------- | ----------- |
| `automationId` / `name` / `controlType` / `nameContains` / `index` | champs de localisateur | null | Cible du clic |
| `condition` | string | `stable_for` | Condition d'attente après le clic |
| `expectedValue` | string? | null | Requis pour `value_equals` / `value_contains` |
| `waitAutomationId` / `waitName` / `waitControlType` | champs de localisateur | localisateur du clic | Cible d'attente distincte, facultative |
| `timeoutMs` | int? | délai d'attente par défaut | Budget d'attente |
| `stableForMs` | int? | `350` pour `stable_for`, plafonné par `timeoutMs` | Durée requise d'état inchangé |
| `windowTitle` | string? | null | Limite à une sous-chaîne du titre de fenêtre |
| `processId` | int? | cible de session | Processus cible |

#### `type_and_assert`

| Nom | Type | Défaut | Description |
| ---- | ---- | ------- | ----------- |
| `text` | string | requis | Texte à saisir |
| `operator` | string | requis | Opérateur d'assertion tel que `equals`, `contains`, `enabled`, `checked` |
| `expected` | string? | null | Opérande attendu de l'assertion |
| `automationId` / `name` / `controlType` / `nameContains` | champs de localisateur | null | Cible de la saisie |
| `assertAutomationId` / `assertName` | champs de localisateur | localisateur de saisie | Cible d'assertion distincte, facultative |
| `windowTitle` | string? | null | Limite à une sous-chaîne du titre de fenêtre |
| `processId` | int? | cible de session | Processus cible |

## `get_user_activity`

Indique si une personne utilise ce bureau. Lecture seule: n'envoie aucune
entrée.

**Paramètres**

| Nom         | Type | Obligatoire | Description |
| ----------- | ---- | ------ | ----------- |
| `processId` | int? | non    | Processus dont le titre de la fenêtre au premier plan peut être renvoyé. Se replie sur la cible de session. |

**Renvoie**: `OperationResult` avec:

| Champ | Signification |
| ----- | ------------- |
| `humanIdleMs` | Millisecondes depuis la dernière entrée non attribuée à Winsight (et hors `other_injected` avec `WINSIGHT_INPUT_HOOK=1`); null si aucune n'a été vue |
| `lastInputAgeMs` / `lastInputSource` | Âge de l'entrée la plus récente, de tout type, et source: `human`, `winsight` ou `other_injected` (avec `WINSIGHT_INPUT_HOOK=1`) |
| `foreground` | `handle`, `processId`, `processName`; `title` seulement quand la fenêtre appartient au processus cible (`titleWithheld` l'indique) |
| `sessionLocked` | true, false, ou null quand Windows ne le signale pas (lit `SessionFlags` dans `WTSINFOEXW` à l'offset 16 avec repli `OpenInputDesktop`) |
| `yieldToUserMs` / `userActive` | Le seuil `WINSIGHT_YIELD_TO_USER_MS`, et si une entrée humaine est plus récente que lui |
| `yieldOverridden` | true uniquement quand l'utilisateur a activé `WINSIGHT_YIELD_OVERRIDE=1` dans l'environnement serveur (journalisé à chaque contournement, jamais paramétrable par outil) |
| `diagnostics` | `inputKind` (`mouse`, `keyboard`, `other`, `unknown`), `cursorMoved`, `cursorPosition`, `inputHookActive`, `lastInputInjectedByOther`, et jusqu'à 8 `recentInputs` avec `ageMs` et `deltaFromPreviousMs` |

Par défaut, Winsight attribue les entrées selon le temps sans installer de hook
global de bas niveau (que les produits de sécurité des postes peuvent signaler).
Chaque processus Winsight de la session Windows enregistre la fenêtre temporelle
de chaque injection dans un registre partagé; une entrée située dans cette
fenêtre (plus 50 ms) appartient à Winsight, tout le reste est humain. Si des
entrées fantômes réinitialisent `humanIdleMs`, consultez `diagnostics`
(`cursorMoved`, `recentInputs`, `inputKind`), ou activez `WINSIGHT_INPUT_HOOK=1`
dans l'environnement serveur pour activer le hook optionnel `WH_MOUSE_LL` /
`WH_KEYBOARD_LL` (vérifie `LLMHF_INJECTED` / `LLKHF_INJECTED` sans enregistrer
les touches ni le texte) afin de classer les injections externes en
`other_injected` et de les ignorer dans le garde-fou de courtoisie.

## `wait_for_user_idle`

Attend que personne n'ait utilisé la souris ou le clavier pendant `idleMs`, en
excluant les entrées de Winsight lui-même, ou jusqu'à expiration complète de
`timeoutMs`. Lecture seule.

| Nom         | Type | Obligatoire | Description |
| ----------- | ---- | ------ | ----------- |
| `idleMs`    | int  | oui    | Période de calme requise, de 1 à 600000 |
| `timeoutMs` | int  | oui    | Attente maximale, de 0 à 600000; 0 vérifie une seule fois |
| `processId` | int? | non    | Comme pour `get_user_activity` |

**Renvoie**: `{ idle, waitedMs, idleMs, activity }`, où `activity` est un
instantané de `get_user_activity` et `waitedMs` la durée totale d'attente. En
cas d'expiration, l'appel échoue avec `timed_out` et les mêmes données. Après
un refus `user_active`, appelez `wait_for_user_idle` avec un vrai délai (par
exemple `idleMs=3000, timeoutMs=30000`), puis `observe_scene`, puis agissez.

## `launch_app`

Démarre une application et attend que sa fenêtre soit réellement présente avant
de rendre la main, afin qu'un agent n'entre jamais en concurrence avec une UI
encore en cours d'initialisation. La fenêtre renvoyée est déjà classée (voir
[`list_windows`](#list_windows)), prête à être pilotée sans `list_windows` ni
interrogation supplémentaire.

**Paramètres**

| Nom                  | Type    | Obligatoire | Description                                                           |
| -------------------- | ------- | ------ | --------------------------------------------------------------------- |
| `path`               | string  | oui    | Chemin absolu de l'exécutable à lancer                                |
| `arguments`          | string? | non    | Arguments de ligne de commande                                        |
| `workingDirectory`   | string? | non    | Répertoire de travail; par défaut, le répertoire de l'exécutable     |
| `waitForWindowTitle` | string? | non    | Sous-chaîne, insensible à la casse, du titre de fenêtre à attendre. Si omis, attend la fenêtre principale du processus |
| `timeoutMs`          | int     | non    | Attente maximale de la fenêtre. Défaut 15000; maximum 120000         |

**Renvoie**: `OperationResult` qui enveloppe un `LaunchResult`:

```json
{
  "success": true,
  "data": {
    "processId": 14820,
    "window": {
      "processId": 14820,
      "windowHandle": 1247868,
      "title": "Winsight Sample - Counter",
      "processName": "SampleWpfApp",
      "isForeground": true,
      "x": 750, "y": 400, "width": 420, "height": 280,
      "kind": "main", "className": "HwndWrapper[...]", "ownerHandle": 0, "isModal": false
    }
  }
}
```

L'appel échoue explicitement quand l'exécutable est introuvable, quand le
processus se termine pendant le démarrage (le code de sortie est indiqué), ou
quand aucune fenêtre correspondante n'apparaît dans le délai `timeoutMs`.

## `close_app`

Ferme un processus par son id. Par défaut, cela demande un arrêt propre
(`CloseMainWindow`) et attend jusqu'à cinq secondes. Si l'application reste
ouverte, l'appel renvoie un échec avec `stillRunning: true` et `closeRequested`.
Terminez toute confirmation d'enregistrement avant de réessayer. Seul
`force=true` tue l'arborescence du processus; une demande d'arrêt propre ne se
transforme jamais en arrêt forcé.

**Paramètres**

| Nom         | Type | Obligatoire | Description                                            |
| ----------- | ---- | ------ | ------------------------------------------------------ |
| `processId` | int  | oui    | Id du processus à fermer (issu de `launch_app` ou de `list_windows`) |
| `force`     | bool | non    | Tue immédiatement l'arborescence du processus          |

**Renvoie**: `OperationResult` dont `data` indique `processId` et si le
processus a été tué de force; un processus déjà terminé est signalé comme un
succès (`alreadyExited: true`).

## `list_windows`

Énumère les fenêtres de premier niveau, dans l'ordre z (la plus en avant
d'abord), via Win32: les surfaces possédées que l'énumération du bureau UIA
masque (boîtes de dialogue modales, MessageBox, menus déroulants, flyouts,
info-bulles) sont ainsi découvrables, et pas seulement atteignables.

**Paramètres**

| Nom         | Type | Défaut | Description                                                                 |
| ----------- | ---- | ------ | --------------------------------------------------------------------------- |
| `processId` | int? | null   | Si défini, renvoie de façon exhaustive la pile **complète** des fenêtres de ce processus (y compris les boîtes de dialogue sans titre, les popups et les menus). Si omis, renvoie toutes les fenêtres de premier niveau **nommées** du bureau (découverte; évite le bruit des fenêtres auxiliaires). |

**Renvoie**: tableau JSON de `WindowDescriptor`:

```json
[
  {
    "processId": 14820,
    "windowHandle": 1247868,
    "title": "Winsight Sample - Counter",
    "processName": "SampleWpfApp",
    "isForeground": true,
    "x": 750, "y": 400, "width": 420, "height": 280,
    "kind": "main",
    "className": "HwndWrapper[SampleWpfApp;;...]",
    "ownerHandle": 0,
    "isModal": false
  }
]
```

`kind` vaut `main`, `dialog` (fenêtre possédée avec barre de titre, ou Win32
`#32770`, MessageBox comprise), `popup` (surcouche sans barre de titre: menu
déroulant, flyout, menu contextuel), `menu` (Win32 `#32768`), `tooltip` ou
`other`. `ownerHandle` est le handle de la fenêtre propriétaire (`0` s'il n'y en
a pas), et `isModal` est true quand la fenêtre possède un parent désactivé (le
signal de boîte de dialogue modale de la plateforme). La classification repose
sur le style Win32 et la relation de possession, et elle est déterministe.

**Flux type**: appelez `list_windows` avec le `processId` cible après une
action susceptible d'ouvrir une boîte de dialogue ou un menu; choisissez
l'entrée dont le `kind` est `dialog` (ou `popup`/`menu`), puis limitez-y
`click_element`, `type_text`, `wait_for_element`, `capture_screenshot`, etc.
avec `windowTitle`. La surcouche la plus en avant est la première entrée
correspondante, puisque la liste est ordonnée de l'avant vers l'arrière.

## Sémantique des localisateurs (partagée par les outils UIA)

`click_element`, `type_text`, `select_item`, `get_element_state`,
`wait_for_element` et `hover_element` partagent le même contrat de
localisateur:

| Critère        | Correspondance                                            |
| -------------- | --------------------------------------------------------- |
| `automationId` | Exacte, sensible à la casse                               |
| `name`         | Exacte, sensible à la casse                               |
| `className`    | Exacte, sensible à la casse                               |
| `controlType`  | Exacte, insensible à la casse (`Button`, `Edit`, `CheckBox`, ...) |
| `nameContains` | Sous-chaîne, insensible à la casse                        |
| `descendantText` | Sous-chaîne, insensible à la casse, dans le Name ou la Value d'un descendant à profondeur bornée |
| `index`        | Choix à partir de zéro quand plusieurs éléments correspondent au reste |

Tous les critères fournis se combinent par un ET. Au moins un critère autre que
`index` doit être défini.

- **Multi-fenêtre**: la recherche couvre toutes les fenêtres de premier niveau
  visibles du processus (y compris les boîtes de dialogue modales possédées, que
  UIA masque à l'énumération du bureau; Winsight les atteint par les handles
  Win32). Utilisez `windowTitle` (sous-chaîne, insensible à la casse, du titre
  de fenêtre) pour limiter la recherche à une seule fenêtre, par exemple une
  boîte de dialogue modale.
- **Mode strict**: quand plusieurs éléments correspondent et qu'aucun `index`
  n'est donné, l'appel échoue et liste les candidats (`index`, `automationId`,
  `name`, `controlType`, `windowTitle`, `score`, `reason`) à la fois dans le
  message d'erreur et dans `data`, afin qu'un agent puisse affiner son
  localisateur en un seul aller-retour.
- **Quasi-correspondances**: quand rien ne correspond, les éléments dont l'id
  ou le nom recoupe partiellement les termes demandés sont suggérés dans
  l'erreur, avec des scores plus faibles et une `reason` telle que
  `partialText`.
- **Texte descendant**: `descendantText` examine les descendants jusqu'à la
  profondeur 5. Il cible les contrôles WPF dont le parent expose un `Name` non
  lisible par un humain (par exemple un nom de type de view-model) alors que le
  libellé utile est porté par un `TextBlock` enfant ou par un descendant porteur
  de valeur.
- **Les éléments hors écran** sont amenés dans la zone visible (motif
  ScrollItem) avant toute action physique de la souris.

## `inspect_ui_tree`

Parcourt l'arbre UI Automation d'une fenêtre de processus.

**Paramètres**

| Nom            | Type    | Défaut | Description                                                 |
| -------------- | ------- | ------ | ----------------------------------------------------------- |
| `processId`    | int     | -      | Id de processus obtenu depuis list_windows                  |
| `maxDepth`     | int     | 5      | Profondeur de l'arbre, plafonnée à 20                       |
| `windowTitle`  | string? | null   | Inspecte la première fenêtre dont le titre contient ce texte |
| `controlType`  | string? | null   | Ne garde que les noeuds de ce type de contrôle (et leurs ancêtres) |
| `nameContains` | string? | null   | Ne garde que les noeuds dont le Name contient ce texte (et leurs ancêtres) |
| `mainWindow`   | bool    | false  | Inspecte la fenêtre principale du processus même quand une boîte de dialogue ou une surcouche la recouvre; ignoré avec `windowTitle` |

Sans `windowTitle`, la fenêtre inspectée est la plus en avant qui n'est pas une
surcouche transitoire. Une info-bulle, un menu, une liste déroulante ou un
flyout ouvert (un popup possédé sans barre de titre, une fenêtre
`WS_EX_NOACTIVATE` ou une fenêtre d'outil, ou une classe native de menu ou
d'info-bulle) se trouve en haut de l'ordre z mais est ignoré: le survol d'un
élément ne fait donc plus renvoyer le popup de l'info-bulle par
`inspect_ui_tree`. Une boîte de dialogue modale n'est pas une surcouche et reste
choisie quand elle est au premier plan.

**Renvoie**: `UiElementNode` JSON (récursif). `value` porte le contenu actuel
du motif Value (zones de texte, listes déroulantes) et vaut null quand l'élément
ne prend pas en charge ce motif; l'arbre sert donc aussi d'oracle en lecture
seule:

```json
{
  "automationId": "MainWindow",
  "name": "Winsight Sample - Counter",
  "controlType": "Window",
  "className": "Window",
  "isEnabled": true,
  "isVisible": true,
  "x": 750, "y": 400, "width": 420, "height": 280,
  "children": [
    {
      "automationId": "IncrementButton",
      "name": "Increment",
      "controlType": "Button",
      "...": "..."
    }
  ]
}
```

## `describe_ui`

Répond à "que puis-je faire ici?": renvoie les éléments **actionnables** de la
fenêtre sous forme de liste compacte, ordonnée par position (de haut en bas, de
gauche à droite), au lieu d'un arbre UIA complet. Utilisez-le pour vous orienter
dans une fenêtre inconnue avant d'agir; ne recourez à
[`inspect_ui_tree`](#inspect_ui_tree) que si vous avez besoin de la structure
complète.

**Paramètres**

| Nom           | Type    | Défaut | Description                                                        |
| ------------- | ------- | ------ | ------------------------------------------------------------------ |
| `windowTitle` | string? | null   | Inspecte la première fenêtre dont le titre contient ce texte (boîtes de dialogue, popups) |
| `maxResults`  | int     | 200    | Nombre maximal d'éléments renvoyés                                 |
| `processId`   | int?    | null   | Id du processus cible; se replie sur la cible de session (voir [Cible de session](#cible-de-session-set_target--get_target--clear_target)) |

Types interactifs conservés: boutons, champs de saisie, cases à cocher, boutons
radio, listes déroulantes, listes et éléments de liste, onglets et éléments
d'onglet, éléments de menu, liens hypertexte, éléments d'arborescence,
curseurs. Le texte statique et les conteneurs de mise en page sont écartés.

**Renvoie**: `OperationResult` dont `data` est un tableau de `UiInteractable`,
chacun avec un `recommendedLocator` (le plus petit critère qui le cible:
`automationId` s'il est unique, sinon un `name` unique, sinon `controlType` +
`index`) prêt à être passé à `click_element` / `type_text` / `select_item`:

```json
{
  "success": true,
  "data": [
    {
      "controlType": "Button",
      "automationId": "IncrementButton",
      "name": "Increment",
      "value": null,
      "toggleState": null,
      "isSelected": null,
      "isEnabled": true,
      "isVisible": true,
      "x": 766, "y": 470, "width": 102, "height": 40,
      "windowTitle": "Winsight Sample - Counter",
      "recommendedLocator": "automationId=\"IncrementButton\""
    }
  ]
}
```

Le parcours se fait en largeur sur les fenêtres du processus: une
recommandation `controlType=...
index=N` utilise donc le même index que les outils de localisation.

## `snapshot_ui` / `diff_ui`

Le sens du retour: voir ce qu'une action a changé sans tout réinspecter.
`snapshot_ui` capture une référence structurelle du processus (ses fenêtres et
ses éléments actionnables); `diff_ui` capture l'état actuel, indique l'écart
par rapport à la référence, puis redéfinit la référence afin que les appels
successifs s'enchaînent.

Flux type: `snapshot_ui` → action → `diff_ui`. Sans instantané préalable,
`diff_ui` capture une référence et renvoie `verdict: "baseline_captured"`.

**Paramètres** (les deux outils)

| Nom           | Type    | Défaut | Description                                                  |
| ------------- | ------- | ------ | ------------------------------------------------------------ |
| `windowTitle` | string? | null   | Limite la capture des éléments à une fenêtre dont le titre contient ce texte |
| `processId`   | int?    | null   | Id du processus cible; se replie sur la cible de session    |

**`snapshot_ui` renvoie** - `OperationResult` avec `{ processId, windowCount, interactableCount }`.

**`diff_ui` renvoie** - `OperationResult` dont `data` est un `UiChange`:

```json
{
  "success": true,
  "data": {
    "verdict": "changed",
    "windowsOpened": [ { "title": "Winsight Sample - Note", "kind": "dialog", "isModal": true, "...": "..." } ],
    "windowsClosed": [],
    "elementsAdded": [ { "controlType": "Edit", "automationId": "NoteInput", "recommendedLocator": "automationId=\"NoteInput\"", "...": "..." } ],
    "elementsRemoved": [],
    "elementsChanged": [
      {
        "controlType": "CheckBox",
        "automationId": "FeatureToggle",
        "name": "Enable feature",
        "windowTitle": "Winsight Sample - Counter",
        "recommendedLocator": "automationId=\"FeatureToggle\"",
        "changes": [ { "field": "toggleState", "before": "Off", "after": "On" } ]
      }
    ]
  }
}
```

`verdict` vaut `no_change` quand rien ne diffère. Les fenêtres sont appariées
par handle; les éléments par une clé stable (fenêtre + AutomationId unique,
sinon type de contrôle + nom, les occurrences en double étant appariées dans
l'ordre); les éléments persistants sont comparés sur le nom, la valeur,
l'état de bascule, la sélection, l'état activé et la visibilité.

## `click_element`

Localise un élément (voir
[Sémantique des localisateurs](#sémantique-des-localisateurs-partagée-par-les-outils-uia))
et clique dessus.

**Paramètres**

| Nom            | Type    | Obligatoire | Description                                        |
| -------------- | ------- | ------------ | -------------------------------------------------- |
| `processId`    | int     | oui          | Id du processus cible                              |
| `automationId` | string? | localisateur | AutomationId UIA                                   |
| `name`         | string? | localisateur | Name UIA                                           |
| `className`    | string? | localisateur | ClassName UIA                                      |
| `controlType`  | string? | localisateur | Type de contrôle UIA                               |
| `nameContains` | string? | localisateur | Sous-chaîne du Name                                |
| `descendantText` | string? | localisateur | Sous-chaîne du Name/Value d'un descendant        |
| `index`        | int?    | non          | Choisit parmi plusieurs correspondances            |
| `windowTitle`  | string? | non          | Limite aux fenêtres dont le titre contient ce texte |
| `clickStyle`   | string? | non          | `single` (défaut), `double` ou `right`             |

**Ordre de résolution** (`single`): motif Invoke → motif Toggle → clic de
souris au centre. `double` et `right` sont toujours des actions physiques de la
souris.

**Renvoie**: `OperationResult`:

```json
{ "success": true }
```

ou, en cas d'ambiguïté:

```json
{
  "success": false,
  "error": "Locator ... matched 3 elements ... Candidates: [0] automationId=\"IncrementButton\" ...",
  "data": [ { "index": 0, "automationId": "IncrementButton", "name": "Increment", "controlType": "Button", "className": "Button", "windowTitle": "Winsight Sample - Counter" } ]
}
```

## `click_point`

Clique à une coordonnée sans localiser d'élément via UI Automation. C'est le
partenaire d'action de [`find_element_visually`](#find_element_visually) et
l'issue de secours pour les surfaces que UIA ne peut pas adresser (Canvas WPF
personnalisé, SkiaSharp, DirectX). La fenêtre cible doit être visible et non
masquée pour que le clic l'atteigne.

**Paramètres**

| Nom               | Type    | Obligatoire | Description                                                        |
| ----------------- | ------- | ------ | ------------------------------------------------------------------ |
| `processId`       | int     | oui    | Id du processus cible                                              |
| `x`               | int     | oui    | Coordonnée X du clic (relative à la fenêtre par défaut)            |
| `y`               | int     | oui    | Coordonnée Y du clic (relative à la fenêtre par défaut)            |
| `clickStyle`      | string? | non    | `single` (défaut), `double` ou `right`                             |
| `windowTitle`     | string? | non    | Restreint la fenêtre cible à celles dont le titre contient ce texte |
| `coordinateSpace` | string? | non    | `window` (défaut) ou `screen`                                      |
| `allowOutOfWindow` | bool   | non    | Avec `coordinateSpace=screen`, autorise des points hors des limites de la fenêtre cible. Défaut false. |
| `focusWindow`     | bool    | non    | Amène la fenêtre cible au premier plan et vérifie qu'elle détient le premier plan avant la validation du point. Défaut false. |
| `settleMs`        | int?    | non    | Millisecondes pendant lesquelles le pointeur reste sur le point avant l'appui, de 0 à 2000. Défaut 150. |

`coordinateSpace=window` interprète `x,y` dans l'espace de coordonnées de la
fenêtre cible, avec la **même origine qu'une capture d'écran de la fenêtre
entière**: les `x,y` renvoyés par `find_element_visually` peuvent donc être
transmis tels quels. L'outil résout l'origine de la fenêtre (la fenêtre visible
la plus en avant, ou celle désignée par `windowTitle`), vérifie que le point se
trouve dans les limites de la fenêtre, et le convertit en coordonnées d'écran.
`coordinateSpace=screen` traite `x,y` comme des coordonnées absolues du bureau,
mais vérifie quand même que le point se trouve dans une fenêtre visible du
processus cible (après tout filtre `windowTitle`), sauf si `allowOutOfWindow=true`
est défini intentionnellement.

Juste avant l'envoi, Winsight vérifie aussi l'identité et la géométrie de la
fenêtre, ainsi que le processus et la fenêtre situés sous le point. Une fenêtre
qui recouvre le point provoque un échec. `allowOutOfWindow` n'autorise jamais à
cliquer dans un autre processus.
Une fenêtre enfant d'un autre processus, rattachée à la fenêtre cible, compte
comme la cible: une surface WebView2 s'affiche dans un enfant
`Chrome_WidgetWin_1` appartenant à `msedgewebview2.exe`, et un point au-dessus
est accepté quand son ancêtre racine (`GetAncestor(GA_ROOT)`) est la fenêtre
cible. Une autre fenêtre de premier niveau, ou un enfant hébergé dans la fenêtre
d'un autre processus, placé au-dessus du point est toujours refusé. Les appels
d'entrée au sein d'un même serveur sont sérialisés; d'autres utilisateurs du
bureau ou d'autres processus peuvent toutefois modifier le bureau entre la
vérification native et l'envoi de l'entrée.

Contrairement à `click_element`, il n'y a pas de résolution par motif UIA:
chaque style est une action physique de la souris au point calculé.

**Stabilisation du pointeur**: le clic se déroule en trois temps: le pointeur
se déplace jusqu'au point, y reste pendant `settleMs`, sa position est relue,
puis le bouton est enfoncé sans nouveau déplacement. Les UI en mode immédiat
(iced sur winit, egui) résolvent le survol à partir du dernier `WM_MOUSEMOVE`
une fois par image et agissent sur ce survol quand le bouton s'enfonce; un
déplacement et un appui envoyés ensemble agissent sur la position précédente. Si
le pointeur n'est plus sur le point après la pause (à plus d'un pixel), l'appui
est refusé avec `cursor_moved`: quelque chose d'autre l'a déplacé, souvent une
personne. Le destinataire du point est vérifié de nouveau après la pause, car le
survol peut ouvrir un popup par-dessus. Les clics doubles et droits suivent le
même chemin, de même que les clics physiques de `click_element`,
`find_element_visually` et `act_verified`.

**Fenêtre recouverte** - sans `focusWindow`, un point recouvert par une autre
fenêtre, y compris une autre fenêtre du même processus, est refusé. Avec
`focusWindow=true`, la fenêtre est d'abord amenée au premier plan (la même mise
au premier plan vérifiée que pour `send_key_chord` et `drag`), son cadre est
résolu de nouveau, et l'appel échoue si la fenêtre a changé pendant cette
opération.

**Renvoie**: `OperationResult` dont `data` indique le point d'écran absolu qui
a été cliqué:

```json
{ "success": true, "data": { "screenX": 846, "screenY": 432 } }
```

Un point hors des limites de la fenêtre résolue est un échec explicite qui
indique la taille de la fenêtre, afin que l'agent puisse corriger la coordonnée.

> **Cibler une fenêtre précise** - sans `windowTitle`, l'origine de la fenêtre
> est la fenêtre visible **la plus en avant** du processus, qui peut être une
> info-bulle ou un popup transitoire plutôt que celle sur laquelle vous avez
> mesuré. Pour cliquer sur un point relatif à une surface précise, passez
> `windowTitle`; cela restreint aussi les vérifications de limites de
> `coordinateSpace=screen` à la famille de fenêtres visée.

## `scroll_point` / `hover_point`

Tourne la molette de la souris, ou laisse le pointeur au repos, à une
coordonnée, pour les listes, les canevas et les effets de survol que UI
Automation ne peut pas atteindre. Les deux prennent les paramètres de
`click_point` sauf `clickStyle` (`x`, `y`, `windowTitle`, `coordinateSpace`,
`allowOutOfWindow`, `focusWindow`, `settleMs`, `processId`) et effectuent les
mêmes vérifications: le point doit atteindre une fenêtre cible visible, et le
pointeur s'y stabilise et sa position est relue avant toute autre opération.

| Nom | Outil | Type | Description |
| ---- | ---- | ---- | ----------- |
| `delta` | `scroll_point` | int | Crans de molette, de -50 à 50, différent de 0. Une valeur positive fait défiler vers le haut, ou vers la droite avec `horizontal` |
| `horizontal` | `scroll_point` | bool | Actionne la molette horizontale. Défaut false |
| `holdMs` | `hover_point` | int? | Durée pendant laquelle le pointeur reste après la stabilisation, de 0 à 10000; sa position est vérifiée de nouveau ensuite |

Windows transmet la molette à la fenêtre située sous le pointeur, c'est pourquoi
le pointeur s'y déplace d'abord. `hover_point` n'appuie sur rien; faites une
capture ou appelez `read_text_visually` ensuite pour voir ce qui est apparu. Les
deux renvoient les coordonnées absolues `screenX`/`screenY`, et échouent avec
`cursor_moved` si une autre entrée a déplacé le pointeur.

## `type_text`

Localise un élément et y écrit du texte. Mêmes paramètres de localisateur que
`click_element` (sans `clickStyle`), plus:

| Nom    | Type   | Obligatoire | Description      |
| ------ | ------ | ------ | ---------------- |
| `text` | string | oui    | Texte à envoyer  |
| `focusWindow` | bool | non | Amène la fenêtre de premier niveau correspondante au premier plan avant d'écrire; échoue si `GetForegroundWindow` ne correspond pas |
| `allowWebView2Input` | bool | non | Écrit même quand l'élément se trouve dans une surface WebView2 (défaut false) |

**Ordre de résolution**: motif Value (s'il est modifiable) → focus +
keyboard.Type.

Un élément situé dans une surface WebView2 est refusé avant toute écriture:
une entrée clavier synthétique dans WebView2 a déjà fait planter `ctfmon`, l'hôte
de saisie de texte. Utilisez `type_into_webview` (CDP) pour le contenu web;
voir `get_focused_element` pour les signaux de détection.

**Renvoie**: `OperationResult` (même forme que `click_element`).

## `send_key_chord`

Met le focus sur une fenêtre de premier niveau cible, vérifie qu'elle est
devenue la fenêtre au premier plan, puis envoie un raccourci clavier via
`SendInput`. Cet outil n'utilise pas `SendKeys`.

| Nom           | Type    | Obligatoire | Description                                      |
| ------------- | ------- | ------ | ------------------------------------------------ |
| `processId`   | int     | oui    | Id du processus cible                            |
| `chord`       | string  | oui    | Raccourci tel que `Ctrl+Shift+N`, `Alt+F4`, `Ctrl+Enter` |
| `windowTitle` | string? | non    | Limite le focus aux fenêtres dont le titre contient ce texte |
| `allowWebView2Input` | bool | non  | Envoie même quand le focus clavier se trouve dans une surface WebView2 (défaut false) |

L'étape de focus restaure la fenêtre sélectionnée (une info-bulle ou un menu
ouvert est ignoré, voir `inspect_ui_tree`), appelle `SetForegroundWindow`, puis
interroge `GetForegroundWindow` pendant 1000 ms au plus. Si le handle au premier
plan ne devient pas celui de la fenêtre sélectionnée, l'outil renvoie
`success: false` avec les handles de fenêtre attendu et réel. Si le focus clavier
se trouve ensuite dans une surface WebView2, rien n'est envoyé et l'échec porte
l'élément qui a le focus.

**Envoi.** Chaque transition porte son code de balayage matériel, et les touches
du bloc étendu (PageUp, PageDown, Home, End, Insert, Delete, les flèches, Ctrl
et Alt de droite, les touches Windows) portent `KEYEVENTF_EXTENDEDKEY`. Les
modificateurs s'enfoncent d'abord dans l'ordre du raccourci, les autres touches
suivent, et tout est relâché dans l'ordre inverse, avec 15 ms entre les
transitions afin qu'un hook `WH_KEYBOARD_LL` qui lit `GetAsyncKeyState` voie
chaque modificateur stabilisé. Sans l'indicateur étendu ni le code de balayage,
Windows lit PageUp comme le 9 du pavé numérique et, avec NumLock actif et Shift
maintenu, injecte un faux relâchement de Shift autour de la touche:
l'application reçoit Ctrl+PageUp au lieu de Ctrl+Shift+PageUp. L'exemple de
laboratoire WPF (`samples/WpfLabApp`) journalise la vue du hook, celle de
`WM_KEYDOWN` et celle de WPF pour chaque frappe.

**Renvoie** (`focus` est l'élément qui a reçu le raccourci, de même forme que
`get_focused_element`)

```json
{
  "success": true,
  "data": {
    "chord": "Ctrl+Shift+N",
    "windowTitle": "Winsight Sample - Counter",
    "windowHandle": 1247868,
    "focus": { "controlType": "Edit", "automationId": "NoteInput", "isWebView2": false, "...": "..." }
  }
}
```

## `get_focused_element`

Indique où une entrée clavier arriverait en ce moment. Lecture seule; ne prend
que le `processId` facultatif.

**Renvoie** (`FocusedElementInfo`)

```json
{
  "success": true,
  "data": {
    "controlType": "Edit",
    "automationId": "",
    "name": "Terminal input",
    "className": "xterm-helper-textarea",
    "frameworkId": "Chrome",
    "processId": 154408,
    "processName": "msedgewebview2",
    "belongsToTarget": true,
    "isWebView2": true,
    "webView2Evidence": ["frameworkId=Chrome", "process=msedgewebview2",
                         "win32Focus=Chrome_WidgetWin_1", "uiaClass=Chrome_RenderWidgetHostHWND"],
    "win32FocusClassName": "Chrome_WidgetWin_1",
    "topLevelWindowHandle": 25827204,
    "topLevelWindowTitle": "Heimdall - Remote Access Control Center",
    "x": 1984, "y": 497, "width": 7, "height": 17
  }
}
```

`isWebView2` est true quand un signal quelconque place le focus dans WebView2:
un `frameworkId` Chromium, un élément appartenant à `msedgewebview2`, une classe
de fenêtre Chromium qui détient le focus Win32, ou une classe Chromium ou
`WebView2` parmi les ancêtres UIA. `belongsToTarget` est true quand l'élément
appartient au processus cible ou à une fenêtre qu'il héberge (WebView2 s'affiche
dans son propre processus, rattaché à la fenêtre hôte). `type_text` et
`send_key_chord` refusent d'envoyer dans WebView2 sauf si
`allowWebView2Input=true`.

## `hover_element`

Localise un élément, déplace la souris en son centre et capture une info-bulle
qui apparaît à proximité. Les info-bulles déjà présentes avant le survol sont
écartées, et les candidates sont filtrées selon leur proximité avec le point de
survol.

Utilise les paramètres de localisateur partagés, plus:

| Nom                   | Type    | Obligatoire | Description                                      |
| --------------------- | ------- | ------ | ------------------------------------------------ |
| `expectedTooltipText` | string? | non    | Assertion de sous-chaîne, insensible à la casse, sur le texte de l'info-bulle |
| `timeoutMs`           | int?    | non    | Délai d'attente de l'info-bulle; défaut 1500, maximum 10000 |

Les info-bulles sont lues dans les fenêtres transitoires du processus cible (un
`Popup` WPF qui héberge un `ToolTip`, ou une fenêtre `tooltips_class32`), et le
popup est attendu pendant tout le délai. Le `AutomationProperties.HelpText` de
l'élément ne sert que de repli quand aucun popup ne s'est ouvert.
`tooltip.source` indique d'où provient le texte: `popup` (les limites et
`windowHandle` sont ceux du popup) ou `helpText` (les limites sont celles de
l'élément, `windowHandle` vaut 0).

Quand `expectedTooltipText` est fourni, l'absence d'info-bulle correspondante
est un échec explicite. Sans texte attendu, le survol réussit et renvoie
l'info-bulle capturée si elle apparaît, ou `tooltip: null`.

**Renvoie**

```json
{
  "success": true,
  "data": {
    "x": 960,
    "y": 540,
    "tooltip": {
      "text": "Increment counter by one",
      "x": 912,
      "y": 566,
      "width": 176,
      "height": 32,
      "source": "popup",
      "windowHandle": 3409212
    }
  }
}
```

## `select_item`

Sélectionne un élément dans un conteneur localisé. Le localisateur du conteneur
utilise les paramètres de localisateur partagés. Désignez l'élément avec
exactement l'un des paramètres suivants:

| Nom                | Type    | Description                                          |
| ------------------ | ------- | ---------------------------------------------------- |
| `itemName`         | string? | Nom accessible exact                                 |
| `itemNameContains` | string? | Sous-chaîne du nom, insensible à la casse            |
| `itemNamePattern`  | string? | Expression régulière .NET à laquelle le nom doit correspondre (évaluation plafonnée à 250 ms) |

La sélection par sous-chaîne et par motif résiste aux noms qui portent des
données vivantes, comme une ligne d'arborescence nommée
`lab-c, protocol SFTP, state Reachable (1 ms) · 13:03:33` dont l'horloge change
chaque seconde: `itemNameContains: "lab-c,"` continue de correspondre. Ces
modes sont stricts: quand plusieurs éléments correspondent, l'appel échoue et
les liste. Un `itemName` exact conserve le comportement historique de première
correspondance.

**Gestion des conteneurs**

| Type de contrôle du conteneur | Stratégie                                       |
| ----------------------------- | ----------------------------------------------- |
| `ComboBox`                    | Sélection FlaUI ComboBox + repli de la liste    |
| `List`                        | Sélection FlaUI ListBox                         |
| `Tab`                         | Sélection de l'élément d'onglet                 |
| tout autre type               | Recherche d'un descendant SelectionItem         |

Si l'élément est introuvable, l'erreur liste les noms des éléments disponibles.

**Renvoie**: un `OperationResult` dont `data.selectedName` est le nom complet
de l'élément sélectionné.

## `drag`

Fait glisser avec le bouton gauche de la souris d'une source vers une cible, à
un rythme adapté au glisser-déposer OLE (`DragDrop.DoDragDrop` dans WPF):
appuyer sur la source, maintenir, sortir du rectangle de glissement système
(`SM_CXDRAG`), se déplacer par étapes, dépasser la cible d'un pixel puis revenir
afin que la cible du dépôt reçoive un dernier `DragOver`, attendre, relâcher.
Le bouton est toujours relâché, même quand un déplacement échoue.

Chaque extrémité est soit un élément, soit un point:

| Nom | Type | Description |
| ---- | ---- | ----------- |
| `sourceAutomationId`, `sourceName`, `sourceNameContains`, `sourceControlType`, `sourceIndex` | | Élément source (localisateur strict, centre) |
| `sourceX`, `sourceY` | int? | Point source à la place d'un élément |
| `targetAutomationId`, `targetName`, `targetNameContains`, `targetControlType`, `targetIndex` | | Élément cible |
| `targetX`, `targetY` | int? | Point cible à la place d'un élément |
| `targetOffsetX`, `targetOffsetY` | int | Décalage par rapport au centre de l'élément cible (par exemple sa moitié gauche) |
| `coordinateSpace` | string | `window` (défaut) ou `screen`, pour les points |
| `holdMs` | int | Maintien avant le premier déplacement; défaut 150, maximum 5000 |
| `steps` | int | Déplacements intermédiaires; défaut 12, plage 1-100 |
| `stepDelayMs` | int | Pause entre les déplacements; défaut 25, maximum 1000 |
| `dropDelayMs` | int | Survol de la cible avant le relâchement; défaut 250, maximum 5000 |
| `allowOutOfWindow` | bool | Permet à la cible de se trouver hors des fenêtres du processus cible (dépôt dans une autre application) |
| `windowTitle` | string? | Limite la recherche d'éléments et les points relatifs à la fenêtre |

La fenêtre cible est d'abord amenée au premier plan (une info-bulle ou un menu
ouvert est ignoré), car une fenêtre laissée au-dessus d'elle avalerait l'appui.
Les deux points doivent ensuite atteindre une fenêtre du processus cible
(vérifié avec `WindowFromPoint`), sauf si `allowOutOfWindow` lève la
vérification de la cible. Comme pour `click_point`, un enfant inter-processus
hébergé par cette fenêtre (une surface WebView2) lui appartient.
Vérifiez le résultat avec `wait_for_element` ou `get_element_state`: c'est
l'application qui décide de ce que signifie un dépôt.

**Renvoie**

```json
{ "success": true, "data": { "fromX": 2162, "fromY": 410, "toX": 1926, "toY": 410, "moves": 15 } }
```

## `manage_window`

Déplace, redimensionne, agrandit, réduit ou restaure une fenêtre de premier
niveau du processus cible: la fenêtre principale par défaut, ou la première
fenêtre dont le titre contient `windowTitle`.

| Nom | Type | Description |
| ---- | ---- | ----------- |
| `action` | string | `move`, `resize`, `set_bounds`, `maximize`, `minimize` ou `restore` |
| `x`, `y` | int? | Position du cadre extérieur en pixels d'écran, pour `move` et `set_bounds` |
| `width`, `height` | int? | Taille du cadre extérieur en pixels, pour `resize` et `set_bounds` |
| `windowTitle` | string? | Agit sur cette fenêtre au lieu de la fenêtre principale |

Déplacer ou redimensionner une fenêtre agrandie ou réduite la restaure d'abord.
`restore` revient toujours aux limites normales, même depuis une réduction qui a
suivi un agrandissement. Les demandes sont postées de façon asynchrone
(`ShowWindowAsync`, `SWP_ASYNCWINDOWPOS`) afin qu'un thread d'UI occupé ne
puisse pas bloquer l'appel; le résultat est lu une fois que la fenêtre a
atteint l'état demandé et que ses limites ont cessé de changer (jusqu'à 2 s).

**Renvoie** (`matchesRequest` vaut false quand l'application a contraint la
demande, par exemple avec une taille minimale)

```json
{
  "success": true,
  "data": {
    "windowHandle": 25827204, "title": "Heimdall - Remote Access Control Center",
    "action": "Resize", "state": "normal",
    "x": 1573, "y": 308, "width": 900, "height": 650, "matchesRequest": true
  }
}
```

## `get_element_state`

Lit l'état observable d'un élément localisé: c'est l'oracle léger, au niveau
de l'interface, pour les assertions. Préférez-le à `read_managed_state` quand
l'état affiché à l'écran suffit. Utilise les paramètres de localisation
partagés.

**Retour**: un `OperationResult` qui enveloppe un `ElementState`:

```json
{
  "success": true,
  "data": {
    "automationId": "FeatureToggle",
    "name": "Enable feature",
    "controlType": "CheckBox",
    "className": "CheckBox",
    "windowTitle": "Winsight Sample - Counter",
    "isEnabled": true,
    "isVisible": true,
    "hasKeyboardFocus": false,
    "value": null,
    "toggleState": "On",
    "isSelected": null,
    "selectedItem": null,
    "x": 766, "y": 612, "width": 102, "height": 15
  }
}
```

Les champs adossés à un pattern valent null quand l'élément ne prend pas en
charge le pattern correspondant: `value` (pattern Value), `toggleState`
(Toggle), `isSelected` (SelectionItem, par exemple un élément de liste),
`selectedItem` (Selection: le nom du premier enfant sélectionné d'un
conteneur tel qu'une ComboBox).

## `wait_for_element`

Interroge en boucle jusqu'à ce qu'une condition soit vraie pour un
localisateur, ou échoue après un délai. C'est la primitive de
synchronisation: attendez avant de cliquer (l'élément apparaît ou devient
actif) et après avoir agi (le dialogue se ferme). Utilise les paramètres de
localisation partagés, plus:

| Nom              | Type    | Défaut  | Description                                                  |
| ---------------- | ------- | ------- | ------------------------------------------------------------ |
| `condition`      | string  | -       | `exists`, `not_exists`, `enabled`, `visible`, `value_equals`, `value_contains`, `stable_for`, `name_equals`, `name_contains`, `name_matches` ou `child_count` |
| `expectedValue`  | string? | null    | Obligatoire pour `value_*` et `name_*`; une expression régulière pour `name_matches` |
| `expectedCount`  | int?    | null    | Obligatoire pour `child_count`                                |
| `childControlType` | string? | null  | Pour `child_count`, ne compte que les enfants directs de ce type de contrôle |
| `timeoutMs`      | int?    | 10000   | Attente maximale; modifiable globalement via `WINSIGHT_UIA_WAIT_TIMEOUT_MS`; plafonnée à 120000 |
| `pollIntervalMs` | int?    | 250     | Délai entre deux interrogations; minimum 50                  |
| `stableForMs`    | int?    | null    | Durée pendant laquelle l'état doit rester inchangé pour `stable_for` |

`exists` est satisfaite par une correspondance ou plus; `not_exists` par
zéro. `enabled`, `visible`, `value_equals`, `value_contains` et `stable_for`
exigent une correspondance unique (utilisez `index` pour lever l'ambiguïté)
et échouent immédiatement, avec la liste des candidats, quand plusieurs
éléments correspondent. `value_equals` compare exactement le contenu du
pattern Value; `value_contains` vérifie une sous-chaîne sans tenir compte de
la casse. `stable_for` ne réussit qu'une fois que l'instantané `ElementState`
complet est resté inchangé pendant `stableForMs`, ce qui sert aux assertions
anti-rebond après des mises à jour d'interface filtrées côté serveur.

`name_equals` (ordinale), `name_contains` (insensible à la casse) et
`name_matches` (expression régulière) testent le Name de l'élément, pour les
libellés et les lignes dont l'application met à jour le texte. `child_count`
réussit quand l'élément a exactement `expectedCount` enfants directs,
éventuellement limités à ceux de type `childControlType`; le résultat
rapporte le dernier `childCount`. Par exemple, attendez que le Tab nommé
Sessions contienne trois onglets au lieu de faire une pause:

```json
{ "name": "Sessions", "controlType": "Tab", "condition": "child_count",
  "expectedCount": 3, "childControlType": "TabItem", "timeoutMs": 5000 }
```

**Retour**: un `OperationResult` qui enveloppe un `WaitOutcome` (également
joint aux échecs par expiration du délai):

```json
{
  "success": true,
  "data": {
    "condition": "Exists",
    "conditionMet": true,
    "elapsedMs": 312,
    "matchCount": 1,
    "state": { "automationId": "NoteInput", "...": "..." }
  }
}
```

## `assert_element`

Vérifie une condition sur un élément localisé et obtient un verdict clair,
réussite ou échec: c'est une vérification immédiate (pas d'interrogation en
boucle, contrairement à `wait_for_element`). Les mêmes opérateurs existent
sous la forme d'une étape `assert` dans [`run_steps`](#run_steps).

**Paramètres**: les paramètres de localisation partagés, plus:

| Nom        | Type    | Obligatoire | Description                                           |
| ---------- | ------- | ----------- | ----------------------------------------------------- |
| `operator` | string  | oui         | L'opérateur d'assertion (ci-dessous)                  |
| `expected` | string? | pour les opérateurs texte/numériques | L'opérande attendu           |

**Opérateurs**: les opérateurs texte comparent la Value de l'élément, avec
repli sur son Name (les champs de saisie comme les libellés fonctionnent
donc):

| Opérateur | Vrai quand |
| --------- | ---------- |
| `equals` / `not_equals` | le texte est égal / n'est pas égal à `expected` |
| `contains` / `not_contains` | le texte contient / ne contient pas `expected` (insensible à la casse) |
| `greater_than` / `less_than` | le texte s'interprète comme un nombre supérieur / inférieur à `expected` |
| `enabled` / `disabled` | l'élément est actif / inactif |
| `visible` / `hidden` | l'élément est à l'écran / hors écran |
| `checked` / `unchecked` | l'état de bascule vaut On / Off |
| `selected` | l'élément est sélectionné |
| `exists` | l'élément a été localisé (n'importe quelle correspondance) |

**Retour**: un `OperationResult`: `success: true` avec `data.assertion` (le
détail) quand la condition est vraie; `success: false` avec une `error`
explicative (attendu contre obtenu) quand elle échoue ou que l'élément est
introuvable.

```json
{ "success": false, "error": "Assertion failed: expected value to equal \"Count: 999\" but was \"Count: 1\"" }
```

## `run_steps`

Exécute une séquence d'étapes d'interface en un seul appel, au lieu d'un
aller-retour par action. Un scénario entier (piloter, synchroniser, vérifier,
capturer) devient un seul appel d'outil qui renvoie un journal par étape.
Chaque étape est confiée à la même primitive que l'outil à action unique
correspondant, la sémantique est donc identique.

**Paramètres**

| Nom           | Type        | Obligatoire | Description                                                   |
| ------------- | ----------- | ----------- | ------------------------------------------------------------- |
| `steps`       | `Step[]`    | oui         | Étapes à exécuter, dans l'ordre                               |
| `processId`   | int?        | non         | Identifiant de processus par défaut pour les étapes qui n'en précisent pas |
| `stopOnError` | bool        | non         | S'arrête à la première étape en échec (true par défaut)       |
| `captureExceptions` | bool  | non         | Joint les exceptions levées par le processus cible pendant le lot (oracle causal). Exige le `processId` au niveau du lot. |
| `maxDurationMs` | int?      | non         | Budget total du lot. Quand le temps écoulé l'atteint avant une étape, l'exécution s'arrête et `timedOut` vaut true. |
| `dryRun`      | bool        | non         | Valide et journalise les étapes qui modifient l'interface sans cliquer, saisir, sélectionner, survoler ni écrire de captures. Également activé globalement par `WINSIGHT_DRY_RUN`. |
| `traceRun`    | bool        | non         | Écrit un artefact JSON de rejeu même quand le traçage global est désactivé. |
| `stopOnForegroundChange` | bool | non      | S'arrête avant une étape qui modifie l'écran quand le premier plan est passé à une fenêtre extérieure au lot (true par défaut). |

**Forme d'une étape**: un objet plat avec un discriminant `action` et les
champs propres à cette action. Les champs de localisation partagés
(`automationId`, `name`, `className`, `controlType`, `nameContains`, `index`,
`descendantText`, `windowTitle`) fonctionnent exactement comme pour
[`click_element`](#click_element). Chaque étape peut fixer son propre
`processId` (sinon la valeur par défaut du lot s'applique) et un `label`
facultatif, renvoyé tel quel dans le journal.

| `action`      | Correspond à         | Champs propres à l'étape                                       |
| ------------- | -------------------- | -------------------------------------------------------------- |
| `click`       | `click_element`      | `clickStyle`                                                   |
| `click_point` | `click_point`        | `x`, `y`, `clickStyle`, `coordinateSpace`, `allowOutOfWindow`, `focusWindow`, `settleMs` |
| `type`        | `type_text`          | `text`, `focusWindow`                                          |
| `scroll_point` | `scroll_point`     | `x`, `y`, `delta`, `horizontal`, et les champs de `click_point` sauf `clickStyle` |
| `hover_point` | `hover_point`        | `x`, `y`, `holdMs`, et les champs de `click_point` sauf `clickStyle` |
| `type_keys`   | `type_keys`          | `text`, `mode` (`keys` ou `paste`), `restoreDelayMs`, `windowTitle` |
| `key_chord`   | `send_key_chord`     | `chord`                                                        |
| `select`      | `select_item`        | `itemName`                                                    |
| `hover`       | `hover_element`      | `expectedTooltipText`, `timeoutMs`                             |
| `wait`        | `wait_for_element`   | `condition`, `expectedValue`, `timeoutMs`, `pollIntervalMs`, `stableForMs` |
| `wait_visual_change` | `wait_for_visual_change` | `region`, `threshold`, `localThreshold`, `timeoutMs`, `pollIntervalMs` |
| `wait_visual_stable` | `wait_for_visual_stable` | `region`, `threshold`, `localThreshold`, `stableComparisons`, `timeoutMs`, `pollIntervalMs` |
| `assert`      | `assert_element`     | `operator`, `expectedValue`: verdict immédiat; une assertion en échec fait échouer l'étape. L'opérateur `text_visible` lit `expectedValue` par OCR dans `region` |
| `get_state`   | `get_element_state`  | (localisateur uniquement)                                      |
| `screenshot`  | `capture_screenshot` | `outputPath` (un chemin horodaté par défaut est utilisé s'il est omis) |

Une étape `wait_visual_change` qui suit une étape modifiant l'écran compare
avec une capture que l'exécuteur prend juste avant cette étape: un
changement plus rapide qu'un aller-retour n'est donc pas manqué. `region` vaut
`{ "x", "y", "width", "height" }` en coordonnées de fenêtre.

`launch_app`/`close_app` ne sont volontairement pas des actions d'étape;
appelez-les autour du lot (l'identifiant de processus sert à délimiter les
étapes). Une étape `screenshot` consigne le chemin de la capture dans le
`data` de son journal (le journal du lot est en JSON: contrairement à
l'outil autonome `capture_screenshot`, il n'intègre pas l'image).

**Retour**: un échec de type `OperationResult` en cas d'entrée invalide,
sinon un `BatchResult`:

```json
{
  "success": true,
  "completed": 6,
  "total": 6,
  "failedAtIndex": null,
  "steps": [
    { "index": 0, "action": "click", "label": "increment-1", "success": true, "error": null, "data": null },
    { "index": 5, "action": "wait", "label": "assert-count", "success": true, "error": null,
      "data": { "condition": "Exists", "conditionMet": true, "elapsedMs": 41, "matchCount": 1, "state": { "...": "..." } } }
  ]
}
```

Avec `stopOnError: true` (par défaut), l'exécution s'interrompt à la première
étape en échec; `failedAtIndex` est l'index de cette étape, `success` vaut
false et les étapes suivantes sont absentes de `steps`. Avec
`stopOnError: false`, toutes les étapes s'exécutent et `failedAtIndex`
signale le premier échec. `completed` compte les étapes réussies; `total`
est le nombre d'étapes soumises. Quand `maxDurationMs` est défini et que le
budget est atteint avant la fin des étapes, le résultat porte
`timedOut: true` et `success: false`. Quand `dryRun` vaut true, les actions
qui modifient l'interface rapportent `data.dryRun=true` et ne touchent ni
l'interface cible ni les captures. Les étapes en lecture seule comme `wait`,
`assert` et `get_state` s'exécutent quand même.

**Bureau partagé**: avant chaque étape qui envoie une entrée (`click`,
`click_point`, `type`, `key_chord`, `select`, `hover`), l'exécuteur compare la
fenêtre au premier plan avec celle laissée par l'étape de ce type précédente.
Si le premier plan est passé à une fenêtre dont le processus n'est pas ciblé
par le lot, l'exécution s'arrête là, quelle que soit la valeur de
`stopOnError`: l'étape est journalisée avec une erreur `foreground_changed`
qui nomme les deux fenêtres (handles et noms de processus, sans titres), et
le résultat porte `foregroundChanged: true`. Sont tolérés: un dialogue
ouvert par la cible, l'absence de premier plan pendant une transition, et la
fermeture de la fenêtre de référence par la cible. Passez
`stopOnForegroundChange: false` pour désactiver ce contrôle. Avec
`WINSIGHT_YIELD_TO_USER_MS` défini, les mêmes étapes échouent avec
`user_active` tant qu'une personne a utilisé le bureau plus récemment que ce
délai.

**Artefacts de rejeu**: définissez `traceRun: true`, `WINSIGHT_TRACE=1` ou
`WINSIGHT_TRACE_DIR` pour écrire un fichier JSON de rejeu.
`BatchResult.tracePath` pointe vers lui. Utilisez `replay_steps` pour
l'inspecter ou le rejouer.

**Oracle causal des exceptions**: avec `captureExceptions: true`, une
session EventPipe s'exécute autour du lot entier et le résultat gagne un
tableau `exceptions` qui liste les exceptions levées par le processus cible
*pendant* les étapes (y compris celles qui sont interceptées: le runtime les
émet aussi). Un oracle borné dans le temps devient ainsi un oracle causal.
Chaque entrée porte `ExceptionType` et `ExceptionMessage`. Un tableau vide
signifie qu'aucune n'a été observée; le champ est absent quand la capture
n'a pas été demandée. Cette option exige le `processId` au niveau du lot (la
session cible un seul processus managé) et ajoute un court délai de
stabilisation et de vidage.

```json
{
  "success": true, "completed": 1, "total": 1, "failedAtIndex": null,
  "steps": [ { "index": 0, "action": "click", "success": true } ],
  "exceptions": [
    {
      "providerName": "Microsoft-Windows-DotNETRuntime",
      "eventName": "ExceptionThrown_V1",
      "payload": { "ExceptionType": "System.InvalidOperationException", "ExceptionMessage": "..." }
    }
  ]
}
```

## `replay_steps`

Rejoue un artefact de `run_steps`. C'est une simulation par défaut:
un agent peut ainsi inspecter les actions enregistrées sans répéter les clics
ni les frappes.

| Nom | Type | Défaut | Description |
| --- | ---- | ------ | ----------- |
| `tracePath` | string | - | Chemin renvoyé par `run_steps.tracePath` |
| `processId` | int? | null | Remplacement facultatif de l'identifiant de processus; sinon la valeur par défaut de la trace, puis la cible de session |
| `dryRun` | bool | true | Laissez true pour l'inspection; passez false pour exécuter les étapes enregistrées qui modifient l'interface |

Le retour a la même forme `BatchResult` que `run_steps`, et un nouvel artefact
de rejeu est écrit pour la tentative de rejeu.

## `read_managed_state`

S'attache à un processus managé et lit un champ de la première instance du
type demandé présente sur le tas.

**Paramètres**

| Nom         | Type   | Description                                                    |
| ----------- | ------ | -------------------------------------------------------------- |
| `processId` | int    | Identifiant du processus cible                                 |
| `typeName`  | string | Nom de type .NET complet (espace de noms + classe)             |
| `fieldName` | string | Nom du champ sur le type, y compris le nommage des champs de stockage |

**Retour**: un `OperationResult` dont `data` porte la valeur du champ:

```json
{ "success": true, "data": 42 }
```

Pour les champs de type référence, `data` est une chaîne de la forme
`"Type.Name @ 0xADDRESS"` (un pointeur hexadécimal). Les types valeur
primitifs reviennent sous forme de nombres, booléens ou chaînes JSON natifs.

**Contraintes importantes**

- Le processus du serveur MCP **doit** avoir la même architecture que la
  cible (x64 ici).
- L'attachement ne suspend pas la cible. Elle continue de s'exécuter pendant
  l'inspection: la valeur du champ est lue à un instant donné et peut avoir
  été modifiée au moment où l'agent voit le résultat.
- ClrMD n'évalue ni les accesseurs ni les propriétés. Pour lire
  `MyVm.Count` quand `Count` vaut `get => _internalCount;`, interrogez
  directement le champ de stockage `_internalCount`.

## `capture_screenshot`

Capture en PNG la fenêtre visible la plus haute du processus cible qui n'est
pas une surcouche transitoire (une info-bulle ou un menu ouvert est ignoré,
voir `inspect_ui_tree`), sa fenêtre principale quand `mainWindow=true`, ou la
première fenêtre visible dont le titre contient `windowTitle`, à l'aide de
`PrintWindow` avec `PW_RENDERFULLCONTENT`. Ce drapeau permet aux fenêtres WPF
composées par DirectX et aux autres surfaces accélérées de s'afficher
correctement dans le bitmap; sans lui, le résultat est souvent un rectangle
noir.

**Paramètres**

| Nom           | Type   | Obligatoire | Description                                         |
| ------------- | ------ | ----------- | --------------------------------------------------- |
| `processId`   | int    | oui         | Identifiant du processus cible                      |
| `outputPath`  | string | non         | Chemin de fichier absolu. Par défaut: un `screenshot-{utc-timestamp}.png` horodaté sous `WINSIGHT_OUTPUT_DIR` (par défaut `%TEMP%\winsight`) |
| `windowTitle` | string | non         | Sous-chaîne du titre de la fenêtre cible, insensible à la casse. Utilisez-la pour capturer des dialogues modaux, des fenêtres contextuelles ou des fenêtres secondaires. |
| `includeImage` | bool  | non         | Joint le PNG capturé en image intégrée (true par défaut). Passez false pour ne renvoyer que le chemin JSON. |
| `mainWindow`  | bool   | non         | Capture la fenêtre principale du processus même quand un dialogue ou une surcouche la recouvre; ignoré avec `windowTitle` |

> **Répertoire de sortie**: quand `outputPath` est omis, la capture est
> écrite dans le répertoire désigné par la variable d'environnement
> `WINSIGHT_OUTPUT_DIR`, avec repli sur `%TEMP%\winsight`. Définissez-la pour
> rediriger les captures vers un dossier lisible par un agent superviseur
> (par exemple un montage partagé). Le répertoire est créé à la première
> écriture.

**Retour**: un contenu MCP en deux blocs: un bloc **texte** qui porte le
JSON `OperationResult` enveloppant un `ScreenshotResult` et (sauf si
`includeImage=false`) un bloc **image** avec le PNG capturé intégré, pour
qu'un agent superviseur voie directement l'interface sans lire le fichier. Le
bloc texte:

```json
{
  "success": true,
  "data": {
    "path": "C:\\Users\\me\\AppData\\Local\\Temp\\winsight\\screenshot-20260509-143012-457.png",
    "width": 420,
    "height": 280,
    "capturedAtUtc": "2026-05-09T14:30:12.4571234Z"
  }
}
```

En cas d'échec (fenêtre réduite, `PrintWindow` a renvoyé false, chemin de
sortie non accessible en écriture, cible exigeant les droits administrateur
que l'hôte n'a pas, ...):

```json
{
  "success": false,
  "error": "PrintWindow returned false (Win32 error 0x00000005=5). Window may be minimized, occluded, or use a composition surface that does not support PrintWindow (e.g. WinUI3 with DesktopChildSiteBridge)."
}
```

**Remarques**

- Le rendu est synchrone. La latence dépend surtout du coût de redessin de la
  fenêtre (quelques ms pour des fenêtres WPF typiques).
- La capture couvre le rectangle englobant de la fenêtre, zone non cliente
  comprise (barre de titre, bordures).
- La sélection de fenêtre porte sur les fenêtres Win32 de premier niveau
  visibles, dans l'ordre z. Quand `windowTitle` est omis, un dialogue modal ou
  une fenêtre contextuelle visible appartenant au processus est généralement
  capturé avant la fenêtre principale. Passez `windowTitle` pour une capture
  déterministe d'un dialogue.
- Les fenêtres réduites, les fenêtres masquées et les surfaces de composition
  WinUI3 modernes (Bloc-notes de Win11, volets récents des Paramètres)
  peuvent faire échouer `PrintWindow`. Le message d'erreur inclut le code
  d'erreur Win32 pour que l'agent décide s'il faut réessayer, restaurer la
  fenêtre ou se replier sur une autre stratégie de capture.

## `type_keys`

Saisit du texte dans le contrôle qui a le focus dans la fenêtre de la cible
quand aucun élément UI Automation ne permet la saisie (iced, winit, egui).
Utilisez `type_text` quand il en existe un.

| Nom | Type | Obligatoire | Description |
| --- | ---- | ----------- | ----------- |
| `text` | string | oui | Jusqu'à 4096 caractères |
| `mode` | string? | non | `keys` (par défaut) ou `paste` |
| `windowTitle` | string? | non | Restreint la fenêtre par son titre |
| `restoreDelayMs` | int? | non | Mode collage: durée pendant laquelle le texte reste dans le presse-papiers avant la restauration, de 0 à 5000, 300 par défaut |
| `allowWebView2Input` | bool | non | Envoie même quand le focus est dans WebView2. false par défaut |
| `processId` | int? | non | Processus cible |

La fenêtre est mise au premier plan et son focus vérifié comme pour
`send_key_chord`. En mode `keys`, chaque caractère est une frappe
`KEYEVENTF_UNICODE` (l'application reçoit `WM_CHAR`, quelle que soit la
disposition du clavier), les sauts de ligne deviennent Entrée et les
tabulations Tab. Avant chaque frappe, la fenêtre doit toujours détenir le
premier plan; sinon la saisie s'arrête avec `foreground_changed`, pour que
la suite n'atterrisse jamais dans une fenêtre sur laquelle quelqu'un d'autre
vient de cliquer. Le mode `paste` exige `WINSIGHT_ALLOW_CLIPBOARD`: il
sauvegarde chaque format du presse-papiers stocké dans un bloc mémoire, place
le texte dans le presse-papiers (hors historique et hors presse-papiers
cloud), envoie Ctrl+V, attend `restoreDelayMs`, puis remet en place les
formats sauvegardés. `restored` vaut `full`, `partial` (les formats bitmap,
métafichier ou à handle privé n'ont pas pu être sauvegardés) ou `failed`.

**Retour**: `{ mode, length, windowHandle, windowTitle, focusedElement, restored, skippedFormats }`.

## `set_clipboard_text` / `read_clipboard_text`

Écrit ou lit le texte du presse-papiers: préparer un collage, ou vérifier ce
qu'un bouton Copier a copié. Les deux refusent avec `clipboard_disabled` sauf
si `WINSIGHT_ALLOW_CLIPBOARD` est défini, car le presse-papiers contient
souvent des mots de passe et des jetons. Le texte écrit par Winsight porte les
formats qui le tiennent à l'écart de l'historique du presse-papiers, du
presse-papiers cloud et des moniteurs de presse-papiers. Les traces
enregistrent la taille du résultat, jamais le texte. `read_clipboard_text`
renvoie `{ text, length }`, avec `text` à null quand le presse-papiers ne
contient pas de texte; `set_clipboard_text` prend `text` et renvoie
`{ length }`.

## `wait_for_visual_change` / `wait_for_visual_stable`

Se synchronise sur ce qu'affiche une fenêtre au lieu de faire une pause, pour
les interfaces sans arbre UI Automation sur lequel attendre.

| Nom | Type | Obligatoire | Description |
| --- | ---- | ----------- | ----------- |
| `region` | object? | non | `{ x, y, width, height }` en coordonnées de fenêtre. Par défaut: la fenêtre entière |
| `timeoutMs` | int | non | De 0 à 60000, 5000 par défaut |
| `pollMs` | int | non | Délai entre deux captures, de 20 à 5000, 100 par défaut |
| `threshold` / `localThreshold` | double? | non | Limites de SSIM et de SSIM du pire bloc; par défaut les réglages `WINSIGHT_VISUAL_*` |
| `stableComparisons` | int | non | `wait_for_visual_stable` uniquement: comparaisons inchangées consécutives, de 1 à 50, 3 par défaut |
| `baselinePath` | string? | non | `wait_for_visual_change` uniquement: un PNG de comparaison à utiliser au lieu d'une capture prise maintenant |
| `windowTitle`, `processId` | | non | Sélection de la fenêtre, comme pour `capture_screenshot` |

Deux captures diffèrent quand le SSIM global, le SSIM du pire bloc 16x16 ou le
ratio de pixels franchit les seuils de `compare_visual_baseline`: un seul mot
d'état modifié suffit donc. Les captures restent en mémoire et la fenêtre
n'est jamais touchée. Pour attendre l'effet d'une action, placez une étape
`wait_visual_change` de `run_steps` juste après elle: l'exécuteur capture la
référence avant l'action.

**Retour**: `{ condition, met, elapsedMs, frames, ssim, localSsimMin, pixelDiffRatio, region }`;
en cas d'expiration du délai, l'appel échoue avec `timed_out` et les mêmes
données.

## `read_text_visually`

Lit le texte qu'une fenêtre dessine, avec le moteur OCR intégré à Windows.
Hors ligne: rien ne quitte la machine et rien n'est à installer en dehors
d'une langue OCR (Paramètres, Heure et langue, Langue et région).

| Nom | Type | Obligatoire | Description |
| --- | ---- | ----------- | ----------- |
| `region` | object? | non | `{ x, y, width, height }` en coordonnées de fenêtre |
| `language` | string? | non | Balise BCP-47 comme `en-US`; par défaut les langues du profil utilisateur |
| `scale` | int | non | Agrandissement avant reconnaissance, de 1 à 4, 2 par défaut |
| `windowTitle`, `processId` | | non | Sélection de la fenêtre |

**Retour**: `{ language, text, lines: [{ text, bounds, words: [{ text, bounds }] }], region, scale }`.
Chaque `bounds` est en coordonnées de fenêtre, le même espace que
`click_point` relatif à la fenêtre. Sans langue OCR correspondante, l'appel
échoue avec `ocr_unavailable` et liste les langues installées. Dans un lot,
vérifiez avec
`{ "action": "assert", "operator": "text_visible", "expectedValue": "...", "region": {...} }`:
la comparaison ignore la casse et réduit les espacements.

## `compare_visual_baseline`

Capture la **zone cliente** de la fenêtre du processus cible (sans le cadre,
pour le déterminisme) et la compare à une référence PNG approuvée. La
comparaison est fondée sur la référence et déterministe: SSIM (gaussien
11x11, sigma 1.5, niveaux de gris) en moyenne globale et en minimum local du
pire bloc, plus un ratio de différence de pixels YIQ façon pixelmatch avec
détection des bords anticrénelés. Aucun modèle de ML, aucun GPU ni aucun
runtime Python n'est utilisé.

Avec `WINSIGHT_DRY_RUN=1`, cet outil renvoie un aperçu sans capturer, sans
élaguer d'artefacts, sans créer de référence ni remplacer une référence
existante, y compris quand `updateBaseline=true`.

**Paramètres**

| Nom                   | Type     | Obligatoire | Description                                      |
| --------------------- | -------- | ----------- | ------------------------------------------------ |
| `processId`           | int      | oui         | Identifiant du processus cible                   |
| `baselineKey`         | string   | oui         | Clé logique relative sous le répertoire des références, par exemple `counter-initial.png`; `.png` est ajouté s'il est omis |
| `updateBaseline`      | bool     | non         | Remplace la référence par la capture courante et renvoie `verdict: "baseline_updated"` |
| `ssimThreshold`       | double?  | non         | Remplace `WINSIGHT_VISUAL_SSIM_THRESHOLD`        |
| `localSsimThreshold`  | double?  | non         | Remplace `WINSIGHT_VISUAL_LOCAL_SSIM_THRESHOLD` (0..1) |
| `pixelDiffThreshold`  | double?  | non         | Remplace `WINSIGHT_VISUAL_PIXEL_THRESHOLD`       |
| `colorThreshold`      | double?  | non         | Remplace `WINSIGHT_VISUAL_COLOR_THRESHOLD` (0..1) |
| `ignoreRegions`       | object[] | non         | Rectangles `{ "x": 0, "y": 0, "width": 100, "height": 20 }` exclus des deux métriques |
| `includeImage`        | bool     | non         | Joint la carte de chaleur des différences en image PNG intégrée quand une comparaison a lieu (true par défaut) |

**Configuration**

| Variable                          | Défaut                                       | Rôle                    |
| --------------------------------- | -------------------------------------------- | ----------------------- |
| `WINSIGHT_VISUAL_BASELINE_DIR`    | `%LOCALAPPDATA%\winsight\visual-baselines`   | Stockage des références approuvées |
| `WINSIGHT_VISUAL_SSIM_THRESHOLD`  | `0.98`                                       | SSIM global minimal pour réussir |
| `WINSIGHT_VISUAL_LOCAL_SSIM_THRESHOLD` | `0.85`                                  | SSIM du pire bloc minimal pour réussir |
| `WINSIGHT_VISUAL_PIXEL_THRESHOLD` | `0.005`                                      | Ratio de différence maximal pour réussir |
| `WINSIGHT_VISUAL_COLOR_THRESHOLD` | `0.1`                                        | Sensibilité couleur de pixelmatch (0..1) |
| `WINSIGHT_OUTPUT_DIR`             | `%TEMP%\winsight`                            | Captures candidates et cartes de chaleur |
| `WINSIGHT_TRACE`                  | non définie                                  | `1`/`true` active les traces JSONL des outils et les artefacts de rejeu |
| `WINSIGHT_TRACE_DIR`              | `%TEMP%\winsight\traces` quand le traçage est actif | Répertoire des traces et des artefacts de rejeu |

Par défaut, les références sont stockées hors du dépôt. `baselineKey` doit
être un chemin `.png` relatif et ne peut pas sortir du répertoire de
références configuré.

**Retour**: un contenu MCP: un bloc **texte** avec le JSON `OperationResult`
qui enveloppe un `VisualDiffResult`, plus (quand une comparaison a eu lieu et
a produit une carte des différences, sauf si `includeImage=false`) un bloc
**image** avec la carte de chaleur intégrée, pour voir où l'interface a changé
sans ouvrir le fichier. Le bloc texte:

```json
{
  "success": true,
  "data": {
    "passed": false,
    "ssim": 0.972134,
    "pixelDiffRatio": 0.01125,
    "diffMapPath": "C:\\Users\\me\\AppData\\Local\\Temp\\winsight\\visual-diff-20260614-181233-118.png",
    "reasoning": "SSIM 0.972134 < 0.980000; pixel diff ratio 0.011250 > 0.005000.",
    "verdict": "fail",
    "baselinePath": "C:\\Users\\me\\AppData\\Local\\winsight\\visual-baselines\\counter-initial.png",
    "candidatePath": "C:\\Users\\me\\AppData\\Local\\Temp\\winsight\\visual-candidate-20260614-181233-063.png"
  }
}
```

Si la référence n'existe pas, l'outil capture la fenêtre, écrit cette capture
comme nouvelle référence et renvoie un verdict de création au lieu d'une
comparaison réussite/échec:

```json
{
  "success": true,
  "data": {
    "passed": true,
    "ssim": 1,
    "pixelDiffRatio": 0,
    "reasoning": "Visual baseline created at C:\\Users\\me\\AppData\\Local\\winsight\\visual-baselines\\counter-initial.png.",
    "verdict": "baseline_created",
    "baselinePath": "C:\\Users\\me\\AppData\\Local\\winsight\\visual-baselines\\counter-initial.png",
    "candidatePath": "C:\\Users\\me\\AppData\\Local\\Temp\\winsight\\visual-candidate-20260614-181233-063.png"
  }
}
```

**Remarques**

- Les images doivent avoir des dimensions identiques. Des dimensions
  différentes constituent un échec explicite et produisent une carte de
  chaleur entièrement rouge quand un chemin de carte des différences est
  demandé.
- Les zones ignorées servent aux interfaces dynamiques comme les horloges,
  les curseurs, le clignotement du point d'insertion ou les animations. Les
  pixels ignorés sont exclus à la fois du SSIM et de la différence de pixels.
- Le verdict n'est réussi que si `ssim >= threshold` et
  `pixelDiffRatio <= threshold`.

## `detect_webview2_endpoints`

Parcourt l'arborescence des processus descendants du pid hôte, trouve chaque
`msedgewebview2.exe` et indique si chacun a été lancé avec le drapeau
`--remote-debugging-port` (Chrome DevTools Protocol). Quand un port est
exposé et à l'écoute, un agent piloté par MCP peut alors attacher Playwright
ou Puppeteer à `http://localhost:{port}` pour piloter directement le contenu
web intégré.

**Paramètres**

| Nom             | Type | Obligatoire | Description                          |
| --------------- | ---- | ----------- | ------------------------------------ |
| `hostProcessId` | int  | oui         | Le pid de l'hôte WPF/Win32 (obtenu avec list_windows) |

**Retour**: un objet JSON:

```json
{
  "endpoints": [
    {
      "hostProcessId": 14820,
      "webView2ProcessId": 17240,
      "remoteDebuggingPort": 9222,
      "isPortListening": true,
      "commandLine": "\"...\\msedgewebview2.exe\" --type=... --remote-debugging-port=9222 ..."
    }
  ],
  "hint": null
}
```

Quand aucun point de terminaison utilisable n'est trouvé, `hint` porte un
message exploitable:

| Situation                           | Indication                                                           |
| ----------------------------------- | -------------------------------------------------------------------- |
| Aucun descendant `msedgewebview2.exe` | L'application n'intègre peut-être pas encore WebView2, ou il est encore en cours d'initialisation. |
| Trouvé mais sans `--remote-debugging-port` | L'application hôte doit définir `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222` (ou `CoreWebView2EnvironmentOptions.AdditionalBrowserArguments`) avant `EnsureCoreWebView2Async`. |
| Drapeau présent mais port pas à l'écoute | Le moteur de rendu a peut-être planté ou n'a pas fini de démarrer. |

**Remarques**

- La détection utilise une seule requête WMI (`Win32_Process`) et un
  instantané `IPGlobalProperties.GetActiveTcpListeners()`: c'est rapide
  (~50 ms) et sûr (aucun trafic de sondage sur les ports cibles).
- La détection de l'écoute porte sur toute interface locale, pas seulement
  la boucle locale: cela correspond au comportement de liaison de WebView2.

## `list_cdp_targets`

Liste les cibles déboguables d'un point de terminaison Chrome DevTools
Protocol (`GET /json/list`). Utilisez le port rapporté par
`detect_webview2_endpoints`.

**Paramètres**

| Nom             | Type | Défaut  | Description                                  |
| --------------- | ---- | ------- | -------------------------------------------- |
| `port`          | int  | -       | Port de débogage CDP (`remoteDebuggingPort`). |
| `hostProcessId` | int? | null    | Processus hôte propriétaire du point de terminaison WebView2. Repli sur `set_target`. |

Le port doit correspondre à un `remoteDebuggingPort` à l'écoute rapporté par
`detect_webview2_endpoints` pour le processus hôte autorisé.

**Retour**

```json
{
  "targets": [
    {
      "id": "AB12",
      "title": "Terminal",
      "url": "https://appassets.example/index.html",
      "type": "page",
      "webSocketDebuggerUrl": "ws://127.0.0.1:9222/devtools/page/AB12"
    }
  ],
  "hint": null
}
```

`hint` est renseigné quand le point de terminaison répond mais n'expose
encore aucune cible `page` (contenu encore en cours de navigation: réessayez
une fois le chargement de l'application terminé). Passez l'`id` d'une cible
`page` à `evaluate_javascript` ou à `type_into_webview`.

## `evaluate_javascript`

Évalue une expression JavaScript dans une page WebView2 via CDP
(`Runtime.evaluate` avec `returnByValue` + `awaitPromise`). Utilisez-le pour
lire l'état du DOM, extraire le texte visible (`document.body.innerText`) ou
effectuer des actions ponctuelles hors de portée d'UIA.

**Paramètres**

| Nom          | Type    | Défaut  | Description                                              |
| ------------ | ------- | ------- | -------------------------------------------------------- |
| `port`       | int     | -       | Port de débogage CDP.                                     |
| `targetId`   | string  | -       | Identifiant de la cible de page, obtenu avec `list_cdp_targets`. |
| `expression` | string  | -       | Expression JavaScript évaluée dans le contexte de la page. |
| `timeoutMs`  | int?    | null    | Délai par commande; par défaut `WINSIGHT_CDP_TIMEOUT_MS` ou 10000. |
| `hostProcessId` | int? | null    | Processus hôte propriétaire du point de terminaison WebView2. Repli sur `set_target`. |

Le port doit correspondre à un point de terminaison WebView2 à l'écoute
appartenant au processus hôte autorisé; les ports CDP bruts en boucle locale
sont refusés.

**Retour**: un `CdpEvaluationResult`:

```json
{
  "success": true,
  "valueJson": "\"$ ls\\nREADME.md\"",
  "valueType": "string"
}
```

Les exceptions du script reviennent sous la forme `{ "success": false,
"exceptionMessage": "ReferenceError: ..." }` au lieu d'une erreur d'outil,
pour que l'agent puisse les lire et se rétablir.

## `type_into_webview`

Saisit du texte dans une page WebView2 sous forme de vrais événements clavier
(paires keyDown/keyUp `Input.dispatchKeyEvent`). Contrairement à `type_text`,
fondé sur UIA, cet outil atteint les surfaces pilotées par keydown comme les
terminaux xterm.js hébergés dans un `HwndHost`: c'est le cas d'échec
documenté de la saisie UIA sur du contenu WebView2 intégré.

**Paramètres**

| Nom             | Type    | Défaut  | Description                                                       |
| --------------- | ------- | ------- | ----------------------------------------------------------------- |
| `port`          | int     | -       | Port de débogage CDP.                                              |
| `targetId`      | string  | -       | Identifiant de la cible de page, obtenu avec `list_cdp_targets`.   |
| `text`          | string  | -       | Texte à saisir. `\n` / `\r\n` deviennent Entrée, `\t` devient Tab. |
| `focusSelector` | string? | null    | Sélecteur CSS qui reçoit le focus avant la saisie (par exemple `.xterm-helper-textarea`). Échoue explicitement quand rien ne correspond. |
| `timeoutMs`     | int?    | null    | Délai par commande; par défaut `WINSIGHT_CDP_TIMEOUT_MS` ou 10000. |
| `hostProcessId` | int?    | null    | Processus hôte propriétaire du point de terminaison WebView2. Repli sur `set_target`. |

Le port doit correspondre à un point de terminaison WebView2 à l'écoute
appartenant au processus hôte autorisé; les ports CDP bruts en boucle locale
sont refusés.

**Retour**

```json
{ "success": true, "data": { "dispatchedEvents": 6 } }
```

**Remarques**

- Entrée est envoyée avec `windowsVirtualKeyCode` 13 et le texte `\r`, ce
  qu'attendent les émulateurs de terminal d'un appui sur une touche physique.
- Un délai de 10 ms sépare les événements clavier successifs pour que la page
  ne fusionne pas une saisie rapide.

## `observe_counters`

S'abonne aux `EventCounters` .NET d'un processus managé via EventPipe (l'IPC
de diagnostic officiel de Microsoft, le même canal que celui de
`dotnet-counters` et `dotnet-trace`). Utilisez-le comme **oracle non
intrusif**: lisez les valeurs des compteurs avant et après une action pour
vérifier un comportement sans toucher à la mémoire ni au code du processus
cible.

**Pourquoi le préférer à `read_managed_state` en environnement d'entreprise**:
l'IPC EventPipe est invisible pour les produits EDR (CrowdStrike,
SentinelOne, Microsoft Defender for Endpoint). L'attachement CLRMD utilise des
schémas `CreateRemoteThread`/`WriteProcessMemory` qui correspondent aux
heuristiques de détection des logiciels malveillants.

**Paramètres**

| Nom            | Type   | Défaut             | Description                              |
| -------------- | ------ | ------------------ | ---------------------------------------- |
| `processId`    | int    | -                  | Identifiant du processus managé cible    |
| `providerName` | string | `System.Runtime`   | Fournisseur EventCounter                 |
| `durationMs`   | int    | 2000               | Fenêtre d'observation. Min 500, max 30000 |

**Retour**: un `CounterSnapshot` JSON:

```json
{
  "processId": 14820,
  "providerName": "System.Runtime",
  "durationMs": 2000,
  "samplesObserved": 38,
  "counters": {
    "cpu-usage": 1.4,
    "working-set": 134217728,
    "gc-heap-size": 8388608,
    "gen-0-gc-count": 0,
    "exception-count": 0,
    "threadpool-thread-count": 12,
    "monitor-lock-contention-count": 0,
    "active-timer-count": 3,
    "alloc-rate": 152384
  }
}
```

**Fournisseurs courants**

| Fournisseur                         | Compteurs intéressants                                    |
| ----------------------------------- | --------------------------------------------------------- |
| `System.Runtime` (par défaut)       | cpu-usage, gc-heap-size, alloc-rate, exception-count, threadpool-thread-count |
| `Microsoft.AspNetCore.Hosting`      | requests-per-second, current-requests, failed-requests    |
| `System.Net.Http`                   | requests-started-rate, requests-failed-rate, http3.0-connections-current-total |
| `Microsoft-AspNetCore-Server-Kestrel` | connections-per-second, total-connections                |
| tout `EventSource` personnalisé     | compteurs déclarés via `EventCounter` / `IncrementingEventCounter` |

**Contraintes**

- La cible doit être en .NET Core 3.0+ / .NET 5+ / .NET 6+. Non pris en
  charge sur .NET Framework 4.x.
- Même compte utilisateur que la cible (ou administrateur).
- La première seconde environ de la fenêtre peut renvoyer des compteurs
  vides, car le runtime a besoin d'un intervalle pour les émettre. La fenêtre
  par défaut de 2000 ms est le minimum qui fournit de façon fiable un jeu
  complet.
- La table `Counters` enregistre la **dernière** valeur observée de chaque
  compteur pendant la fenêtre. `samplesObserved` indique combien
  d'événements de compteur sont arrivés au total, ce qui permet de détecter
  un processus mort en cours de collecte.

## `capture_events`

Capture les événements EventPipe d'un ou plusieurs fournisseurs dans un
processus managé. Utile pour observer chaque exception levée, chaque
événement `EventSource` personnalisé ou tout autre flux de diagnostic, sans
injecter de code dans la cible.

**Paramètres**

| Nom               | Type     | Défaut  | Description                                              |
| ----------------- | -------- | ------- | -------------------------------------------------------- |
| `processId`       | int      | -       | Identifiant du processus managé cible                    |
| `providers`       | string[] | -       | Noms des fournisseurs EventSource                        |
| `durationMs`      | int      | 3000    | Fenêtre de capture. Min 500, max 30000                   |
| `maxEvents`       | int      | 500     | Plafond strict d'événements conservés (1..10000). Positionne `truncated` s'il est atteint |
| `eventNameFilter` | string?  | null    | Filtre sur le nom d'événement, correspondance exacte (sensible à la casse) |

**Retour**: un `EventCaptureResult` JSON:

```json
{
  "processId": 14820,
  "providers": ["Microsoft-Windows-DotNETRuntime"],
  "durationMs": 3000,
  "totalCaptured": 2,
  "truncated": false,
  "events": [
    {
      "timestampUtc": "2026-05-09T14:30:12.4571234Z",
      "providerName": "Microsoft-Windows-DotNETRuntime",
      "eventName": "ExceptionThrown_V1",
      "eventId": 80,
      "level": "Warning",
      "payload": {
        "ExceptionType": "System.InvalidOperationException",
        "ExceptionMessage": "Sequence contains no elements",
        "ExceptionEIP": "0x7ffabc123456",
        "ExceptionHRESULT": -2147467261,
        "ExceptionFlags": 16,
        "ClrInstanceID": 9
      }
    }
  ]
}
```

**Usages courants**

| Objectif | Fournisseurs | eventNameFilter |
| -------- | ------------ | --------------- |
| Chaque exception levée | `["Microsoft-Windows-DotNETRuntime"]` | `"ExceptionThrown_V1"` |
| Événements du cycle de vie des tâches | `["System.Threading.Tasks.TplEventSource"]` | (aucun) |
| Requêtes du client HTTP | `["System.Net.Http"]` | (aucun) |
| Télémétrie applicative personnalisée | `["MyCompany.MyApp"]` | (nom d'événement précis) |

**Remarques**

- Les charges utiles sont normalisées vers des types compatibles JSON: les
  primitifs passent tels quels, `DateTime` est converti en UTC, `Guid` et les
  énumérations deviennent des chaînes, les tableaux d'octets deviennent de
  l'hexadécimal, et les dictionnaires/énumérables sont parcourus
  récursivement avec une profondeur maximale de 4.
- Les fournisseurs à haut débit (`Microsoft-Windows-DotNETRuntime` avec tous
  les mots-clés) peuvent saturer rapidement le plafond. Utilisez
  `eventNameFilter` pour restreindre le flux.
- Mêmes contraintes .NET Core 3.0+ et d'architecture de processus que
  `observe_counters`.

## `find_element_visually`

Localise un élément d'interface à partir d'une description en langage
naturel. L'outil capture la fenêtre cible, soumet la capture avec la requête
à un modèle de vision et renvoie le rectangle englobant. Utilisez-le quand
`inspect_ui_tree` ne renvoie aucun AutomationId utile (Canvas personnalisé,
SkiaSharp, surfaces DirectX ou tout contenu invisible pour UIA).

**Paramètres**

| Nom           | Type    | Obligatoire | Description                                      |
| ------------- | ------- | ----------- | ------------------------------------------------ |
| `processId`   | int     | oui         | Identifiant du processus cible (obtenu avec list_windows) |
| `query`       | string  | oui         | Description de la cible en langage naturel       |
| `windowTitle` | string? | non         | Sous-chaîne du titre de la fenêtre cible, insensible à la casse. Utilisez-la pour capturer des dialogues modaux, des fenêtres contextuelles ou des fenêtres secondaires avant l'ancrage visuel. |
| `act`         | string? | non         | `single`, `double` ou `right` pour cliquer sur le point ancré dans le même appel (via `click_point`). Omis par défaut: l'outil se contente de localiser. |

**Retour**: un `OperationResult` dont `data` est le `GroundedElement`:

```json
{
  "success": true,
  "data": {
    "x": 245,
    "y": 178,
    "width": 96,
    "height": 32,
    "confidence": 0.94,
    "reasoning": "Button labeled 'Increment' centered in the lower stack panel.",
    "source": "claude-sonnet-4-6",
    "screenX": 995,
    "screenY": 578
  }
}
```

`x` et `y` sont le **centre** du rectangle englobant, dans l'**espace en
pixels de la capture** (origine en haut à gauche de la fenêtre capturée).
`screenX` et `screenY` sont le même point déjà converti en **coordonnées
d'écran absolues** (l'origine de la fenêtre est ajoutée côté serveur): ils
peuvent donc être passés directement à `click_point` avec
`coordinateSpace=screen`, ou bien `x,y` relatifs à la fenêtre à
`click_point` avec le `coordinateSpace=window` par défaut. `screenX` /
`screenY` valent null quand l'origine de la fenêtre n'a pas pu être
déterminée.

**Agir en un seul appel**: quand `act` est défini, le point ancré est cliqué
par le même chemin que `click_point` (relatif à la fenêtre) et le résultat
enveloppe à la fois l'ancrage et le résultat du clic. Le clic est refusé
quand la confiance renvoyée est inférieure à `WINSIGHT_VISION_MIN_CONFIDENCE`
(`0.5` par défaut); un appel de localisation seule renvoie quand même
l'ancrage à faible confiance pour inspection.

```json
{
  "success": true,
  "data": {
    "grounded": { "x": 245, "y": 178, "...": "...", "screenX": 995, "screenY": 578 },
    "click": { "success": true, "data": { "screenX": 995, "screenY": 578 } }
  }
}
```

Si l'appel échoue (clé d'API manquante, erreur réseau ou sortie du modèle
illisible), l'outil renvoie à la place un objet d'échec `OperationResult`:

```json
{ "success": false, "error": "ANTHROPIC_API_KEY environment variable is not set. ..." }
```

**Configuration** (variables d'environnement)

| Variable                    | Défaut                                   | Rôle                             |
| --------------------------- | ---------------------------------------- | -------------------------------- |
| `WINSIGHT_VISION_BACKEND`   | `anthropic`                              | `anthropic` ou `onnx`            |
| `ANTHROPIC_API_KEY`         | -                                        | Obligatoire pour `anthropic`     |
| `WINSIGHT_VISION_MODEL`     | `claude-sonnet-4-6`                      | Remplacement du modèle Anthropic |
| `WINSIGHT_VISION_API_URL`   | `https://api.anthropic.com/v1/messages`  | Remplacement du point de terminaison Anthropic |
| `WINSIGHT_VISION_TIMEOUT_MS`| `60000`                                  | Délai par requête pour le backend Anthropic (borné; les 429/5xx transitoires sont réessayés) |
| `WINSIGHT_VISION_MIN_CONFIDENCE` | `0.5`                              | Confiance minimale du modèle exigée avant que `act` clique sur le point ancré |
| `WINSIGHT_VISION_ONNX_PATH` | -                                        | Répertoire local du modèle ONNX Qwen2.5-VL |
| `WINSIGHT_VISION_EP`        | `cpu`                                    | `cpu` uniquement dans cette version |
| `WINSIGHT_OUTPUT_DIR`       | `%TEMP%\winsight`                        | Répertoire de la capture intermédiaire `vision-{timestamp}.png` |

**Confidentialité**: les captures sont transmises à l'API configurée avec
`WINSIGHT_VISION_BACKEND=anthropic`. `WINSIGHT_VISION_BACKEND=onnx` exécute
un modèle Qwen2.5-VL local entièrement sur l'hôte et ne se replie jamais sur
Anthropic; voir [docs/local-onnx-grounder.md](local-onnx-grounder.md) pour
le provisionnement.
