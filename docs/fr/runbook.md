# Runbook UIA de Winsight

*[English version](../runbook.md)*

## Boucle d'agent recommandée

Utilisez d'abord les outils d'agent de haut niveau:

1. `launch_app` ou `list_windows`, puis `set_target`.
2. `observe` pour obtenir les fenêtres, les éléments actionnables, les
   localisateurs recommandés, les actions probables, la mémoire récente et, en
   option, le chemin d'une capture d'écran.
3. `plan_steps` avec l'objectif de l'utilisateur.
4. `run_steps` avec `dryRun=true`.
5. `act_and_observe` ou `run_steps` avec `dryRun=false`.
6. `repair_locator` quand un élément est introuvable ou qu'un localisateur est
   ambigu.

`get_winsight_guide` renvoie ce mode opératoire via MCP. Utilisez-le quand un
agent doit se rétablir sans documentation externe dans son contexte.

`plan_steps` est volontairement prudent pour les contrôles de sélection. Si
l'objectif ressemble à "sélectionner" ou si le meilleur candidat est un
combo/liste/onglet, l'ébauche clique sur le candidat et définit
`requiresReview=true`; observez les options ouvertes, puis utilisez
`select_item` ou une étape `select` de `run_steps` avec un `itemName` explicite.

## Bureau partagé

L'écran peut être utilisé par une personne ou par une autre session d'agent.

1. `get_user_activity` avant de prendre la souris ou le clavier; `humanIdleMs`
   est le temps écoulé depuis la dernière saisie humaine, hors saisies de
   Winsight, et `diagnostics` fournit `inputKind`, `cursorMoved` et
   `recentInputs`.
2. `wait_for_user_idle` avec un vrai délai (par exemple `idleMs=3000`,
   `timeoutMs=30000`) avant un lot ou après `user_active`, puis `observe_scene`
   avant d'agir.
3. Conservez `run_steps stopOnForegroundChange=true` (la valeur par défaut): un
   lot s'arrête avec `foreground_changed` avant de toucher à nouveau l'écran
   quand quelqu'un prend le premier plan.
4. Définissez `WINSIGHT_YIELD_TO_USER_MS` (par exemple `5000`) dans
   l'environnement du serveur pour que tout outil qui modifie l'écran échoue
   avec `user_active` tant qu'une personne est active.
5. Si des entrées fantômes réinitialisent `humanIdleMs`, activez
   `WINSIGHT_INPUT_HOOK=1` dans l'environnement serveur pour séparer les
   entrées matérielles des injections tierces (`other_injected`) via
   `LLMHF_INJECTED` / `LLKHF_INJECTED`. Pour un contournement explicite et
   journalisé sur la session, seul l'utilisateur peut activer
   `WINSIGHT_YIELD_OVERRIDE=1` dans la configuration hôte (jamais via un
   argument d'outil).

Pour les interfaces dessinées par le GPU sans arbre d'automatisation (iced,
winit, egui), cliquez avec `click_point focusWindow=true`. Le pointeur se pose
sur le point avant l'appui (`settleMs`, 150 ms par défaut); augmentez cette
valeur si l'application s'affiche plus lentement. Saisissez avec `type_keys`,
synchronisez avec des étapes `wait_visual_change` et `wait_visual_stable` au
lieu de pauses fixes, et vérifiez le texte avec `read_text_visually` ou une
étape `assert` avec l'opérateur `text_visible`.

Après une recompilation de Winsight, redémarrez la session client. Une session
en cours conserve le processus serveur avec lequel elle a démarré, et sa liste
d'outils, jusqu'à sa reconnexion. `get_winsight_guide` le montre:
`server.registeredTools` est ce que cette session peut appeler, et
`server.newerBuildAvailable` vaut true quand `tools\run-mcp.ps1` l'a démarrée
depuis une sortie recompilée depuis. Un outil cité dans la documentation mais
absent de `registeredTools` signifie la même chose.

## Piloter UIA via MCP

Ne scriptez pas UIA en imbriquant du PowerShell ad hoc dans une session
d'agent. Utilisez plutôt le serveur MCP Winsight. Les scripts UIA en PowerShell
imbriqué masquent les échecs derrière des frontières de processus, entrent en
concurrence pour le focus du bureau et contournent la frontière des outils qui
convertit les échecs COM/UIA en JSON `OperationResult` récupérable.

Pour le débogage bas niveau, utilisez `inspect_ui_tree` pour découvrir les
localisateurs, puis pilotez la cible avec les outils MCP:

```json
{
  "tool": "send_key_chord",
  "arguments": {
    "processId": 14820,
    "chord": "Ctrl+Shift+N",
    "windowTitle": "Winsight Sample"
  }
}
```

```json
{
  "tool": "type_text",
  "arguments": {
    "processId": 14820,
    "automationId": "NoteInput",
    "text": "hello from UIA",
    "windowTitle": "Note",
    "focusWindow": true
  }
}
```

```json
{
  "tool": "wait_for_element",
  "arguments": {
    "processId": 14820,
    "automationId": "NoteInput",
    "condition": "value_equals",
    "expectedValue": "hello from UIA",
    "windowTitle": "Note"
  }
}
```

```json
{
  "tool": "wait_for_element",
  "arguments": {
    "processId": 14820,
    "automationId": "CountLabel",
    "condition": "stable_for",
    "stableForMs": 350,
    "pollIntervalMs": 50
  }
}
```

```json
{
  "tool": "hover_element",
  "arguments": {
    "processId": 14820,
    "automationId": "IncrementButton",
    "expectedTooltipText": "Increment counter by one"
  }
}
```

Pour les contrôles WPF dont le `Name` parent est un nom de type de view-model
et dont le libellé visible se trouve dans un élément enfant, utilisez
`descendantText` avec un critère de restriction habituel:

```json
{
  "tool": "get_element_state",
  "arguments": {
    "processId": 14820,
    "controlType": "Button",
    "descendantText": "Semantic Child Label"
  }
}
```

## Régression de l'audit de sécurité et portes de validation de la vision

Pour l'installation par l'utilisateur final, utilisez
`Winsight-Setup-0.1.0-win-x64.exe` et le guide anglais/français
`docs/installation.html`. L'installation s'effectue par utilisateur. Pour
l'empaquetage, installez Inno Setup 6 et lancez `tools/build-setup.ps1` depuis
Windows PowerShell 5.1 ou ultérieur. Utilisez un `-OutputDirectory` neuf pour
chaque build. La sortie comprend l'EXE d'installation, le ZIP portable,
`BUILD-COMMIT.txt` et `SHA256SUMS.txt`.

Emplacements de configuration des clients:

| Client | Configuration utilisateur |
|---|---|
| Codex | `%USERPROFILE%\.codex\config.toml` (ou `CODEX_HOME`) |
| Claude Desktop | `%APPDATA%\Claude\claude_desktop_config.json` |
| Claude Code | `%USERPROFILE%\.claude.json` (ou `CLAUDE_CONFIG_DIR`) |
| Gemini CLI | `%USERPROFILE%\.gemini\settings.json` (ou `%GEMINI_CLI_HOME%\.gemini\settings.json`) |

L'installation préserve les autres entrées de serveur et les paramètres des
clients, mais peut normaliser la mise en forme. Fermez les clients avant toute
modification. Elle valide tous les fichiers sélectionnés avant d'écrire,
enregistre des sauvegardes à côté des fichiers et consigne les chemins traités
avec succès pour la désinstallation. Un échec de déclaration est signalé dans
l'assistant et renvoie le code de sortie 3 en mode silencieux; les journaux se
trouvent sous `%LOCALAPPDATA%\Winsight\logs`. Une déclaration Winsight en
conflit doit être supprimée dans le client avant de réessayer. La
désinstallation agit après confirmation, refuse les erreurs de configuration et
préserve les entrées redirigées ailleurs.

Les formats de configuration ont été vérifiés par rapport à la documentation
officielle:
[documentation Codex](https://developers.openai.com/codex/mcp),
[documentation Claude Code](https://code.claude.com/docs/en/mcp),
[configuration Claude Desktop](https://modelcontextprotocol.io/docs/develop/connect-local-servers)
et [documentation Gemini CLI](https://geminicli.com/docs/tools/mcp-server/).

Lancez `dotnet test Winsight.slnx -c Release` sur un bureau Windows interactif.
`AuditSafetyRegressionTests` et `AuditSafetyScenarioTests` couvrent la politique
de processus, la fermeture non forcée, les références en simulation, les échecs
de trace, l'unicité des artefacts, l'occlusion et les captures visuelles
périmées. L'option `--refuse-close` de l'exemple est une fixture de test qui
annule la fermeture; le nettoyage ne termine explicitement que les processus
appartenant aux tests.

Quand des hôtes MCP actifs verrouillent les sorties habituelles, compilez et
testez avec le même répertoire `--artifacts-path`. Définissez
`WINSIGHT_SAMPLE_APP_PATH` sur le `SampleWpfApp.exe` fraîchement compilé dans ce
répertoire pour que les tests E2E exercent l'exemple correspondant.

Les vrais tests d'intégration de la vision portent `Category=Vision`. Sans leurs
fixtures, ils sont signalés **Skipped**, jamais **Passed**. Pour valider le
modèle, provisionnez:

- `WINSIGHT_VISION_ONNX_PATH`: répertoire du modèle local exporté.
- `WINSIGHT_VISION_TEST_IMAGE`: capture d'écran de référence.
- `WINSIGHT_VISION_REF_DIR`: sortie du processeur de référence, y compris
  `pixel_values.f32`.

Lancez ensuite `dotnet test Winsight.slnx -c Release --filter Category=Vision --logger trx`.
N'acceptez cette porte de validation du modèle que lorsque les trois tests
sélectionnés réussissent sans aucun test ignoré. L'exécution CI ordinaire
conserve les artefacts TRX même au vert, afin que l'absence de fixtures reste
visible. Les tests de sécurité avec un grounder factice n'établissent pas la
précision du modèle.

`find_element_visually` conserve le HWND capturé, le PID propriétaire et le
rectangle. Une fenêtre déplacée, redimensionnée, désactivée, masquée ou détruite
exige une nouvelle capture. Un clic refusé renvoie `success: false` avec les
détails de l'ancrage et du clic. Utilisez `windowTitle` pour sélectionner
explicitement la fenêtre principale ou la fenêtre modale voulue.
`act_and_observe` rejette les identifiants de processus contradictoires entre
l'appel externe et l'étape.

Les écritures de traces et de relectures se font au mieux: un échec de stockage
préserve le résultat d'origine de l'outil, et une relecture indisponible n'a pas
de `tracePath`. Les noms de fichiers des captures d'écran incluent un suffixe
unique pour que des captures rapides en lot ne s'écrasent pas mutuellement.

## Hôtes connus qui rejettent les entrées synthétiques

Certaines applications hôtes Windows amènent Windows à rejeter les déplacements
de pointeur Win32 synthétiques (`SendInput` et `SetCursorPos`, où
`SetCursorPos` renvoie `FALSE` avec `GetLastError() == 0`) lorsqu'une VM
allumée est affichée dans l'onglet actif de la fenêtre au premier plan, quelle
que soit la façon dont elle a pris le premier plan et que le curseur soit dans
la fenêtre ou en dehors, même à niveau d'intégrité Medium identique.

La liste des hôtes est configurable via
`WINSIGHT_SYNTHETIC_INPUT_REJECTING_HOSTS` (noms de processus séparés par des
virgules, sans `.exe`, valeur par défaut `vmware,vmware-vmx,vmplayer`; une
valeur vide désactive la liste par défaut).

| Processus | Comportement observé | Contournements recommandés |
|---|---|---|
| `vmware`, `vmware-vmx`, `vmplayer` (VMware Workstation / Player) | Lorsqu'une VM allumée est affichée dans l'onglet actif de la fenêtre au premier plan, tous les déplacements synthétiques de pointeur échouent (`SetCursorPos` renvoie `FALSE`, `err=0`) et les outils pointeur renvoient `foreground_rejects_synthetic_input`. L'interface hôte VMware sans VM en cours d'exécution dans l'onglet actif accepte les entrées synthétiques. Aucune préférence connue de VMware ne corrige le cas d'une VM allumée. Les outils clavier (`send_key_chord`, `type_keys`, repli clavier de `type_text`) s'exécutent normalement et joignent un avertissement `foreground_may_reject_synthetic_input`. | Basculer vers un autre onglet (sans VM allumée) ou mettre une autre fenêtre au premier plan. Utiliser les patrons UI Automation (`click_element`, `select_item`, `act_verified` avec actions UIA) pour l'interface hôte de VMware, et `vmsight` pour les entrées dans la VM invitée. |

Utilisez `diagnose_pointer` pour comparer les quatre méthodes de déplacement du
pointeur face à la fenêtre de premier plan courante, `ClipCursor`,
`GetCursorInfo` et le contexte d'entrée du processus.

