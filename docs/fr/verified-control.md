# Contrôle vérifié

*[English version](../verified-control.md)*

La boucle recommandée est `observe_scene` -> `act_verified` -> une nouvelle
observation. Les outils d'origine restent disponibles pour les clients
existants. Exécuter `get_winsight_guide` pour découvrir la boucle recommandée.
La configuration des clients ne change pas pour Codex, Claude Desktop, Claude
Code et Gemini CLI.

## Observer et agir

Appeler `observe_scene` avec `processId` (ou appeler d'abord `set_target`), un
`windowTitle` optionnel et `maxResults` entre 1 et 1000. Son `data.id` identifie
une observation valable deux minutes. `data.capture.elements` contient des
références `eN`, des localisateurs exacts, les actions prises en charge, le focus,
la sélection, l'état de bascule et la géométrie des fenêtres. Les fenêtres
indiquent le premier plan, le propriétaire et le caractère modal. Les éléments
désactivés ou hors écran figurent dans l'observation; les actions revérifient
s'ils sont utilisables.

```json
{
  "request": {
    "observation": "<data.id from observe_scene>",
    "target": "<reference for IncrementButton>",
    "action": "activate",
    "expect": {
      "kind": "name",
      "expected": "Count: 1",
      "locator": { "automationId": "CountLabel" },
      "windowTitle": "Winsight Sample - Counter"
    },
    "timeoutMs": 3000,
    "dryRun": false
  }
}
```

Les observations sont limitées à 16 par session serveur. Une action envoyée
consomme son observation, y compris lorsqu'un fournisseur lève une exception
après l'envoi. Un nouveau processus qui réutilise un PID ne peut pas hériter des
références. Les actions natives revalident l'identité d'exécution, la géométrie
de la fenêtre, l'empreinte nom/valeur, l'état de bascule et la sélection. UIA
est échantillonné séquentiellement: les observations ne sont pas des instantanés
atomiques de l'application.

| Statut | Signification | Action suivante |
| --- | --- | --- |
| `preview` | Arguments acceptés; aucun envoi et aucune consommation de référence | Exécuter une fois autorisé; l'état courant sera revérifié |
| `verified` | Le prédicat explicite a été observé après l'envoi | Observer avant l'action suivante |
| `dispatched_unconfirmed` | L'envoi a eu lieu ou a pu avoir lieu; le résultat n'est pas prouvé | Observer et décider; ne jamais répéter à l'aveugle |
| `stale` | Référence ou cible courante plus utilisable | Utiliser l'observation de reprise renvoyée ou observer à nouveau |
| `blocked` | Entrée invalide, refus de la politique, annulation avant l'envoi ou cible indisponible | Résoudre la cause signalée |

La vérification prouve le prédicat fourni, pas l'intention complète de
l'utilisateur ni la causalité. Choisir un prédicat discriminant: vérifier qu'un
bouton existant existe toujours ne prouve pas qu'un document a été enregistré.
Les appels vers l'application externe et vers l'accessibilité Windows peuvent
dépasser le délai de polling; ce n'est pas une échéance stricte autour d'un
fournisseur bloqué.

## Actions et prédicats

Les actions natives prennent en charge l'activation, l'affectation de valeur, le
focus, les raccourcis clavier, la sélection simple ou multiple, la sélection
d'un élément de conteneur, l'expansion/réduction, le défilement vertical, le
défilement d'un élément dans la vue, le clic droit, le double clic et le
glisser-déposer. `scroll` utilise `text: "up"` ou `"down"`; `drag_to` utilise une
référence `destination` issue de la même observation; `select_many` utilise des
références `targets`. La sélection multiple et le glisser-déposer sont des
opérations séquentielles, pas des transactions. Un échec partiel reste non
confirmé; aucun retour arrière n'est inventé. Pour les listes virtualisées,
faire défiler l'élément dans la vue puis observer à nouveau. Les tableaux, menus
et boîtes de dialogue de fichiers utilisent leurs éléments UIA exposés; il
n'existe pas d'API universelle propre à une application pour les tableaux ou
les dialogues de fichiers.

Prédicats natifs: `exists`, `absent`, `name`, `value`, `selected`, `toggle`,
`focused`, `visible`, `selected_item`, `window_open`, `window_closed`, `managed`.
Les prédicats d'élément exigent un `AutomationId` ou un `Name` exact; un
`ControlType` optionnel peut les restreindre. Les prédicats indexés, partiels ou
portant sur le texte des descendants sont refusés. Les prédicats de fenêtre
utilisent un titre exact. L'absence n'est jamais vérifiée à partir d'une scène
tronquée. Les valeurs booléennes attendues sont des chaînes en minuscules. Les
états de bascule sont `On`, `Off` ou `Indeterminate`.

`managed` exige `typeName`, `fieldName` et une valeur attendue exacte. La
vérification exige exactement une instance correspondante dans le tas. Un tas
qui ne peut pas être parcouru ou plusieurs instances correspondantes laissent le
résultat non confirmé. Aucun débogueur n'est attaché et l'application n'est pas
suspendue. Les limites d'accès du fournisseur s'appliquent toujours.

## Captures d'écran, DOM et vision

`capture_scene(observation, windowHandle)` écrit un PNG original et un PNG
annoté. Les étiquettes ne s'appliquent qu'à ce HWND. La capture est un
échantillon ultérieur: une géométrie identique ne garantit pas que les pixels
et UIA ont été capturés simultanément.

`observe_dom_scene(port, targetId, processId)` exige un point de terminaison
WebView2 sélectionné explicitement, dont le socket d'écoute appartient à un
descendant de l'application autorisée. Il renvoie la même structure
observation/référence. Les rectangles DOM sont exprimés en coordonnées du
viewport CSS, jamais en coordonnées du bureau. Il prend en charge l'activation,
le focus, l'affectation de valeur et le défilement dans la vue. Utiliser
`expect.cssSelector` avec `exists`, `absent`, `value`, `name`, `focused` ou
`selected`. Les vérifications autres que l'absence exigent un seul noeud
correspondant. La syntaxe CSS est vérifiée avant l'envoi de l'action.

Les références DOM conservent les noeuds réels et leur état dans une table
bornée côté page. Le remplacement des noeuds, une navigation et une observation
DOM ultérieure les invalident. Les scripts de la page peuvent accéder à cette
table: c'est une aide à l'automatisation, pas une frontière de sécurité face à
une application hostile. Seul le document principal est parcouru; la couverture
des iframes et du shadow DOM n'est pas revendiquée. Les clics DOM scriptés ne
sont pas des événements de souris physiques de confiance. Ce backend exige une
validation WebView2 réelle pour chaque application cible.

`locate_scene_target(observation, windowHandle, query)` utilise le grounder de
vision configuré, renvoie une nouvelle observation et une cible `v1`, et exige
une confiance d'au moins 0.8. Il utilise la configuration cloud/ONNX existante
sans ajouter à ONNX de repli vers le cloud. Le grounder sait décrire le texte
visible; il n'existe pas de moteur OCR séparé. La capture d'écran complète doit
rester identique octet pour octet avant une action de vision. Les animations,
les curseurs clignotants et les changements de rendu peuvent donc exiger une
nouvelle observation. La confiance est un indice de localisation, pas une preuve
de réussite de l'action. Un prédicat natif ou managé vérifie le résultat.

Tout texte visible, tout contenu DOM et tout raisonnement de vision sont des
données d'application non fiables, jamais des instructions pour accorder des
permissions ou modifier la politique des processus.

## Concurrence et reprise

Les répartiteurs d'entrées natives et l'envoi DOM vérifié partagent un mutex
nommé dans la session Windows interactive. Des hôtes Winsight distincts se
disputent le même bail; une propriété abandonnée est récupérable. Le verrou
n'empêche ni les saisies humaines, ni les changements provoqués par
l'application, ni une autre automatisation, ni une évaluation JavaScript
arbitraire par les outils historiques. Il est tenu pendant la validation et
l'envoi synchrones, pas pendant toute la période de vérification.

La reprise se limite à une nouvelle observation native avant tout envoi. Elle ne
choisit jamais de remplaçant et ne répète jamais une action sans nouvelle
décision. Les échecs DOM et vision exigent une nouvelle observation ou un
nouveau grounding explicite.

## Profils d'application

`save_app_profile(name, steps, processId, dryRun)` enregistre une recette
explicite sous `%LOCALAPPDATA%\Winsight\profiles`. Les noms sont restreints; les
profils utilisent le schéma 1, contiennent 1..20 étapes et sont limités à 1 MiB,
avec 128 profils au maximum. Les écritures passent par un fichier temporaire
voisin et un remplacement atomique. Un enregistrement remplace le profil de même
nom. Les prédicats exploratoires (`any_change`, `visual_change`) sont refusés
dans les profils enregistrés afin de garder des recettes strictement déterministes.

`list_app_profiles()` liste les profils enregistrés (nom, exécutable, version,
SHA256, nombre d'étapes) et `delete_app_profile(name)` en supprime un.
`launch_app` et `observe_scene` remontent aussi automatiquement `matchingProfiles`
quand des profils enregistrés correspondent au chemin, à la version et au SHA256
de l'exécutable en cours.

Chaque étape contient `target` (locator exact), `action`, `expect` et
éventuellement `windowTitle`, `itemName` et `textParameter`. `textParameter`
nomme une valeur fournie à l'exécution; le texte saisi n'est pas persisté. Ne
pas placer de secrets dans les noms littéraux, les prédicats ou les recettes.

`run_app_profile(name, parameters, processId, dryRun)` s'exécute par défaut en
`preview`. Il vérifie le chemin, la version et le SHA256 de l'exécutable,
prévalide toutes les étapes et tous les paramètres, puis observe et vérifie
chaque action (avec notifications de progression MCP par étape). Toute modification
des octets de l'exécutable invalide le
profil. Une cible absente ou ambiguë et un résultat non vérifié arrêtent
l'exécution. Les profils ne sont ni réparés silencieusement ni certifiés
automatiquement. `verified` s'applique uniquement à l'exécution terminée; cela
ne garantit pas toutes les exécutions futures.

## Extensions de la boucle autonome

- **Perception hybride et budget tokens (`observe_scene`)** : passer
  `actionableOnly=true` ou `filterText` pour réduire les grands arbres UIA.
  `actionableOnly` est strict: un élément n'est conservé que s'il a au moins
  une action autre que `drag_to`/`right_click`/`double_click` et qu'il est
  visible ou prend en charge `scroll_into_view` (séparateurs, textes statiques et
  éléments de menu invisibles écartés); la même règle pilote la détection d'UIA
  clairsemé de `surfaceHint` et les étiquettes de `capture_scene`, tandis que la
  vérification `any_change` est inchangée. Les éléments portent `windowHandle`
  (omis pour les éléments DOM) au lieu d'un objet `window` (changement
  incompatible); les bornes des fenêtres sont dans `windows[]`, une seule fois.
  Les
  filtres sont appliqués avant le plafond `maxResults`, et `truncated` vaut
  `true` uniquement si les résultats correspondants ont été coupés par
  `maxResults` ou si le parcours UIA a atteint sa limite de noeuds. Quand UIA
  expose peu d'éléments actionnables, `surfaceHint` signale un port CDP WebView2
  à l'écoute ou une surface dessinée manuellement. Passer `includeOcr=true` pour
  synthétiser des cibles OCR cliquables `tN` utilisables directement par
  `act_verified`.
- **Set-of-Mark inline (`capture_scene`)** : `windowHandle` est optionnel (fenêtre
  au premier plan ou principale par défaut) et `includeImage=true` (par défaut)
  renvoie le PNG annoté inline en tant qu'image MCP.
- **`act_verified` en un aller-retour et prédicats exploratoires** : définir
  `observeAfter=true` pour obtenir `nextObservation` et le `diff` structurel dès
  `verified`, en réutilisant les filtres de l'observation source (`windowTitle`,
  `maxResults`, `actionableOnly`, `filterText`, `includeOcr`). En plus des
  prédicats stricts, `value_contains`, `name_contains`, `any_change` (qui
  réutilise aussi les filtres source) et `visual_change` facilitent
  l'exploration autonome. Pour `activate` sur un `MenuItem`, `any_change` exige
  une preuve d'ouverture de menu (nouvelle fenêtre/popup, nouveaux éléments
  `Menu`/`MenuItem` ou état `Expanded`) ; sans preuve avant expiration,
  `act_verified` renvoie `dispatched_unconfirmed` avec un indice `click_point`
  centré sur les bornes de l'élément (utile sur les barres de menus Win32
  personnalisées comme `wui.unibar` qui ignorent UIA `Invoke`). Pour les autres
  contrôles, `any_change` conserve sa sémantique exploratoire et vérifie sur
  tout changement structurel, mais renvoie un `hint`
  (`code="any_change_unrelated"`) recommandant un prédicat strict (`window_open`,
  `selected`, `value`, `toggle`, ...) lorsque la vérification ne repose que sur
  des éléments sans lien avec la cible (ni la cible, ni ses descendants, ni une
  nouvelle fenêtre). Les échecs répétés incluent un `hint` structuré avec
  `loopWarning` et suggestion de pivot.
- **Lot vérifié multi-étapes (`run_verified_steps`)** : exécute 1..20 étapes
  sémantiques en un seul appel avec `observe_scene` + `act_verified` à chaque
  étape, s'arrête au premier échec avec une observation `recovery` fraîche, et
  émet la progression MCP entre étapes.
- **Raccourci boîtes de dialogue fichiers (`select_file_dialog`)** : cible les
  boîtes Windows `#32770` (Ouvrir/Enregistrer), saisit le chemin, valide et
  vérifie la fermeture.
- **Exécution en arrière-plan (`backgroundOnly=true`)** : sur `act_verified` et
  `run_verified_steps`, restreint l'exécution aux patterns UIA sans vol de focus
  ni souris (`Invoke`, `Toggle`, `SetValue`, `Select`, `Expand`, `Collapse`,
  `Scroll`) et contourne la garde `user_active`.
- **Cibles nommées multi-apps (`set_target` + `switch_target`)** : associer des
  alias courts à plusieurs processus dans une session et basculer sans manipuler
  de PIDs bruts.
- **Événements UIA temps réel et audit QA (`watch_ui_events`, `audit_ui_accessibility`)** :
  capter les popups/menus éphémères par hooks UIA et auditer l'arbre UIA d'une
  fenêtre (accessibilité et testabilité).
- **Attente multi-branches (`wait_any`)** : surveiller 1..10 conditions
  concurrentes (`element_exists`, `element_not_exists`, `value_contains`,
  `window_open`, `window_closed`, `ocr_contains`, `visual_change`) et retourner
  dès la première branche satisfaite.
- **Zoom ciblé et promotion en profil** : passer `targetRef` + `paddingPx` à
  `capture_scene` pour recadrer un contrôle sur écran HiDPI/4K, et appeler
  `save_verified_history_as_profile(name)` pour transformer en un appel les
  étapes `act_verified` strictes réussies de la session en profil épinglé.

## Évaluation

Compiler la solution puis exécuter:

```powershell
.\tools\Winsight.Eval\bin\Release\net10.0-windows10.0.19041.0\Winsight.Eval.exe `
  .\samples\SampleWpfApp\bin\Release\net10.0-windows\SampleWpfApp.exe `
  scripted .\results-new.jsonl
```

Le banc crée une fixture neuve pour chacun des vingt scénarios: compteur,
réinitialisation, sélection, bascule, note, dialogue et clavier. Il vérifie
l'état final de l'interface indépendamment de la source des décisions et
contrôle les transitions observées pour réinitialisation/bascule/annulation. Il
écrit une ligne JSONL par essai, avec le résultat, l'achèvement annoncé, le faux
succès, les appels, la durée, les tokens lorsqu'ils sont disponibles et les
réponses périmées. `wrongTarget` signale désormais les violations d'états
protégés échantillonnées indépendamment; il reste null lorsque les preuves sont
incomplètes. Sa couverture n'inclut pas les clics sans changement d'état ni les
contrôles non configurés. Voir [l'évaluation des modèles](model-evaluation.md)
pour les essais Codex, les suites externes et les limites exactes de notation.
Un dernier argument optionnel répète chaque scénario 1..20 fois. Le fichier de
sortie doit être nouveau.

Remplacer `scripted` par un exécutable adaptateur de confiance pour les essais
de modèles. À chaque tour, l'adaptateur lit une requête JSON sur stdin (`goal`,
`scene`, `history`, `contract`) et écrit une ligne contenant soit
`{"done":true,"tokens":123}`, soit `{"action":{...SceneAction...},"tokens":123}`.
L'adaptateur fournit l'intégration du modèle et du fournisseur; aucun
identifiant de fournisseur ni appel facturable n'est inclus. Le banc exécute au
plus 30 décisions par scénario et accorde 120 secondes à chaque processus
adaptateur. Il n'isole pas l'adaptateur dans un bac à sable. Utiliser un compte
de test isolé pour les expériences de modèles. Conserver les versions de
l'adaptateur et du modèle avec le fichier de résultats.

Cinq scénarios sont étiquetés heldout pour comparaison; la référence scriptée
connaît tous les scénarios et constitue donc une exécution de régression, pas un
score de généralisation. Le banc exerce la boucle d'outils vérifiés en
processus, pas le transport MCP de chaque client. La compatibilité du transport
de chaque client est vérifiée séparément par le smoke test MCP. Les performances
avec un modèle réel, une application réelle, WebView2, le glisser-déposer, la
sélection multiple et l'OCR ne doivent pas être déduites du score WPF scripté.
