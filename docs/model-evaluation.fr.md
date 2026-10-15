# Évaluation des modèles

Le banc sépare les décisions du modèle, le résultat observable et les effets
sur les contrôles protégés. Il accepte les vingt scénarios WPF existants, des
suites JSON externes, des adaptateurs exécutables de confiance et le CLI Codex.

## Première exécution

Compiler en Release, créer le répertoire de résultats, puis vérifier la recette
de référence avant les appels au modèle :

```powershell
dotnet build Winsight.slnx -c Release
$runner = '.\tools\Winsight.Eval\bin\Release\net10.0-windows\Winsight.Eval.exe'
& $runner 'C:\Program Files\PuTTY\putty.exe' scripted '.\TestResults\putty-reference.jsonl' 1 '.\tools\Winsight.Eval\suites\putty-port.json'
```

Le fichier de résultats et son compagnon `.trace.jsonl` doivent être nouveaux.
Exécuter les essais de bureau séquentiellement, avec des données jetables.
Chaque essai lance et termine uniquement son propre processus. Une instance
existante n'est jamais adoptée. Les applications qui restaurent une session ou
transmettent le lancement à un autre processus nécessitent un environnement
jetable adapté.

La suite PuTTY modifie uniquement le champ Port, sans enregistrer de session ni
ouvrir de connexion. Une liste exacte d'AutomationIds autorise seulement la
saisie et le focus sur ce champ. Le contrôleur vérifie indépendamment le port,
l'hôte vide, la sélection SSH et le champ de session vide. Les identifiants et
l'état initial dépendent de la version ; un état initial incompatible bloque
l'essai avant tout appel au modèle.

## Essais Codex

Configurer le chemin du binaire natif `codex.exe`, et non d'un script `.ps1` ou
`.cmd`. Utiliser l'authentification habituelle du CLI. Aucun identifiant secret
n'est copié dans les suites ou les résultats.

```powershell
$env:WINSIGHT_CODEX_EXECUTABLE = 'C:\chemin\vers\codex.exe'
$env:WINSIGHT_CODEX_MODEL = 'modele-accepte-par-votre-cli'
& $runner 'C:\Program Files\PuTTY\putty.exe' codex '.\TestResults\putty-codex.jsonl' 3 '.\tools\Winsight.Eval\suites\putty-port.json'
```

Le choix du modèle est optionnel. Sans cette variable, le CLI utilise son modèle
par défaut et `requestedModel` vaut null. Un nom proposé dans l'application
Codex peut être refusé par le CLI du même compte.

Chaque décision utilise une invocation neuve de `codex exec --json
--output-schema`, limitée à 120 secondes, dans un répertoire temporaire, en
lecture seule, sans configuration utilisateur, règles ou session persistante,
avec `project_doc_max_bytes=0`. Le modèle reçoit l'objectif, la scène et
l'historique des actions. Un événement d'utilisation d'outil invalide l'essai ;
ce contrôle après exécution ne prévient pas l'utilisation elle-même. Le mode
lecture seule ne protège pas la confidentialité de tous les fichiers. Utiliser
un compte de test jetable pour une isolation plus forte. Les descriptions des
skills installés peuvent encore figurer dans le contexte du CLI.

Les avertissements restent des diagnostics. Les tours échoués, codes de sortie
non nuls et décisions absentes ou invalides produisent un échec explicite.
L'adaptateur couvre les actions natives sur une cible : activation, saisie,
sélection d'élément, sélection, focus, raccourci clavier, expansion/réduction et
défilement vers un élément. Les références de vérification sont converties en
locators capturés, sans accepter des métadonnées inventées par le modèle. Un
contrôle futur absent de la scène ne peut servir de référence de vérification.
DOM, vision, sélection multiple et glisser-déposer nécessitent une extension.

Ces essais évaluent la boucle Winsight en processus avec les décisions du CLI.
Ils ne mesurent pas le transport MCP de l'application Codex ni son contrôle
d'ordinateur sans restriction.

## Interpréter les résultats

- `goalOutcome` : état final et jalons obligatoires vérifiés indépendamment.
- `outcome` : objectif atteint et états protégés connus et inchangés.
- `wrongTarget` : true pour un état protégé modifié, false lorsque tous les
  échantillons correspondent, null lorsque la mesure est incomplète.
- La couverture `protected_state_at_observation_boundaries` ne détecte pas les
  clics sans effet, les changements transitoires entre échantillons, les autres
  contrôles non configurés ou les autres processus.
- `falseSuccess` : achèvement annoncé malgré un échec connu ; null si la mesure
  est incomplète après une annonce d'achèvement.
- `tokens` : somme des entrées et sorties déclarées, sans ajouter une seconde
  fois les entrées en cache. Une déclaration manquante rend la somme null.
  `reportedTokens` conserve le sous-total connu et `tokenUsageComplete` indique
  la couverture. L'usage perdu lors d'une décision échouée n'est pas estimé.
- Le rapport contient aussi appels, durée, réponses périmées, nombres de sondes
  et d'échantillons, état final lu, SHA256 de l'exécutable, versions de Winsight,
  du runtime et du CLI, et modèle Codex demandé.

Le compagnon conserve scènes, décisions structurées et résultats d'actions,
sans raisonnement du modèle. Il contient du texte d'interface et des valeurs
saisies : garder ces fichiers locaux et utiliser des données non sensibles.
Une propriété indisponible ou un locator ambigu ne compte jamais comme une
preuve de réussite. Une modification protégée reste un échec même si son état
est restauré ensuite. Un essai échoué retourne le code 1.

## Ajouter une application

`Winsight.Eval --inspect <app.exe> <nouvelle-scene.json>` lance une instance,
observe son interface et la ferme sans action. Cette voie exige une fenêtre
UIA unique dans le PID lancé. Son échec ne prouve pas une incompatibilité totale
de l'application.

Copier la suite PuTTY. Déclarer `name`, `ready` et des `scenarios` aux IDs uniques.
Chaque scénario comprend `id`, `goal`, `oracleTarget`, `property`, `expected`,
`steps` et au moins une sonde `preserve`. Une sonde contient `locator`,
`property`, `expected` et éventuellement `windowTitle`. Les propriétés admises
sont `name`, `value`, `toggle`, `selected_item`, `selected`, `focused`, `visible`.
Les locators doivent utiliser un AutomationId ou Name exact, sans index ni
recherche partielle.

Options : `oracleLocator`, `oracleWindow`, `milestones`, `split`,
`allowedTargets` (AutomationIds exacts), `allowedActions`. Un jalon doit être
observé après une action. Les étapes de référence utilisent `target` comme
AutomationId ou un `targetLocator` exact, puis `action`, `expect` et
éventuellement `windowTitle`, `text`, `item`.

L'exécutable est passé séparément et son hash est conservé. Les arguments de
lancement, l'adoption de processus, les oracles fichiers/bases de données et la
réinitialisation automatique au-delà d'un nouveau processus ne sont pas
implémentés. `WINSIGHT_EVAL_SCENARIOS` filtre une liste d'IDs séparés par des
virgules ; les sélections vides ou inconnues sont refusées. Limites : 1 à 20
essais et 30 décisions par essai. Un appel UIA bloqué peut dépasser le délai de
polling.

Les recettes et critères de jugement restent hors des requêtes du modèle.
Examiner le compagnon après un échec. Trois essais réussis d'une seule tâche
ne constituent ni une couverture applicative générale ni un classement des
modèles.

[Première campagne mesurée](evaluation-campaign-2026-10-01.fr.md) · [English version](model-evaluation.md)
