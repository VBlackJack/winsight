# Campagne d'évaluation du 2026-10-01

*[English version](../evaluation-campaign-2026-10-01.md)*

Le banc accepte maintenant les décisions du CLI Codex, des suites applicatives
externes, des états protégés, des listes d'actions/cibles autorisées et des
traces structurées. La première tâche sur une application réelle a réussi trois
fois sur PuTTY. Cette mesure limitée ne constitue pas un score général de
compatibilité.

| Vérification | Résultat |
| --- | --- |
| Build Release isolé | 0 avertissement, 0 erreur |
| Tests MCP, exécution finale | 128 réussis |
| Nouveaux tests E2E, exécution ciblée finale | 2 réussis |
| Suite E2E complète | 36 réussis, 2 échecs, 1 ignoré |
| Régression WPF scriptée finale, séquentielle | 20/20 |
| Recette PuTTY de référence | 1/1 |
| CLI Codex par défaut, compteur WPF | 1/1 ; identité effective du modèle non enregistrée |
| CLI Codex, modèle demandé `gpt-5.5`, port PuTTY | 3/3 |

Les nouveaux tests prouvent la détection d'un état protégé modifié puis restauré,
et le refus d'un succès annoncé lorsque l'objectif est atteint mais un état
protégé a changé. Les tests unitaires couvrent lectures indisponibles, suites
malformées, restrictions des actions et cibles secondaires, traduction des
références, tokens et distinction entre avertissement, outil et échec du CLI.

La suite E2E complète **n'est pas verte**. Deux tests existants ont échoué :
`PerceiveChangesScenarioTests.Diff_reports_a_toggled_checkbox_then_an_opened_dialog`
(`no_change` au lieu de `changed`) et
`ClickPointScenarioTests.Coordinate_click_point_increments_the_counter`
(clic refusé pour occultation ou fenêtre/processus différent). Les causes ne
sont pas établies. Ces tests n'ont pas été modifiés et leur résultat complet
reste conservé. Le test vision ignoré manque de fixture externe. Les tests Core
n'ont pas été relancés ; aucun fichier Core n'a changé.

Une première régression WPF a donné 19/20 pendant qu'un autre essai WPF était
actif. Le cas compteur à un n'a pas satisfait l'oracle final malgré une action
déclarée vérifiée. L'exécution finale séquentielle donne 20/20. L'échec initial
reste conservé ; la concurrence est un facteur de confusion, pas une cause
prouvée.

## Essais réels PuTTY

Version : `Release 0.85-1`. SHA256 :

```text
12D452AB3DB256776D463A053AC7258A848DE9CC28D13EA3215A87216F6C823C
```

Tâche : saisir `2222` dans Port, conserver l'hôte vide, SSH sélectionné et le
champ de session vide. Seules la saisie et le focus sur Port sont autorisés.
Aucune connexion ni sauvegarde n'est autorisée. Chaque essai utilise un nouveau
processus, fermé après contrôle. CLI : `codex-cli 0.153.0`.

| Essai | Résultat / objectif | État protégé modifié | Appels | Durée | Tokens déclarés |
| --- | --- | --- | --- | --- | --- |
| 1 | réussi / réussi | false | 3 | 14 070 ms | 51 444 |
| 2 | réussi / réussi | false | 3 | 13 986 ms | 51 453 |
| 3 | réussi / réussi | false | 3 | 15 453 ms | 51 608 |

Chaque essai comprend une action vérifiée, une décision d'achèvement et deux
observations. Trois états protégés sont contrôlés à trois frontières. Aucun
faux achèvement n'a été mesuré. `wrongTarget=false` couvre uniquement ces
échantillons ; ce n'est pas un taux universel de mauvais clics nul.

Les tokens incluent le contexte du CLI, qui conserve les descriptions des skills
malgré l'absence de configuration utilisateur et de documents projet. Le coût
monétaire et la version effective du modèle au-delà du nom demandé ne sont pas
mesurés.

## Limites et suite

Le nom desktop `gpt-6.1-sol` a été refusé par le CLI de ce compte avec HTTP 400.
Cet échec n'est pas présenté comme une équivalence avec le modèle réussi. Les
premières versions de l'adaptateur confondaient aussi les avertissements avec
un événement d'outil ; le parseur final et ses tests corrigent cette distinction.

La Calculatrice `11.2607.0.0` a échoué au contrôle de readiness du PID lancé.
Aucune action de modèle n'a été tentée. Ce résultat indique une limite de cette
voie de lancement/observation, pas une incompatibilité totale de la Calculatrice.

Priorités suivantes : diagnostiquer les deux échecs E2E, mesurer un contexte CLI
plus petit, ajouter des tâches indépendantes sur d'autres applications.
WebView2, vision réelle, oracles de sauvegarde fichiers, surveillance des
mauvaises cibles entre processus et comparaison étendue des modèles restent
non mesurés.

Les preuves locales sont dans `.validation/eval-20261001/` : TRX dans
`tests/Mcp-verified/`, `tests/E2E/`, `tests/E2E-new/` ; résultats et compagnons
`scripted-sequential.jsonl`, `putty-scripted.jsonl`, `putty-codex.jsonl` ; essai
par défaut `codex-default-count1.jsonl`, refus du nom desktop
`codex-count1-v4.jsonl`, régression initiale `scripted.jsonl`. Ces fichiers sont
ignorés par Git. Les changements restent locaux, sans commit, push ni release.

[Utiliser le banc](model-evaluation.md)
