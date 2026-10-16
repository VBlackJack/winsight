# Validation du contrôle vérifié

*[English version](../verified-control-validation.md)*

Validation locale sous Windows, 2026-09-05. Ces résultats couvrent la fixture
native et les contrats déterministes; ils ne représentent pas des scores de
comparaison de modèles.

| Vérification | Preuve |
| --- | --- |
| Build Release | La solution compile avec zéro avertissement et zéro erreur |
| Core | 189 réussis, 2 ignorés (fixtures de vision externes) |
| MCP | 111 réussis |
| E2E complet | 35 réussis, 1 ignoré (fixture de vision externe) |
| E2E supplémentaire, booléen managé | 1 réussi; valeur transmise en minuscules, vérifiée sur une instance managée unique |
| E2E final des scènes vérifiées | 5 réussis, dont deux scénarios supplémentaires profil/capture |
| MCP stdio | Initialize et tools/list réussissent; 48 outils, dont les 7 nouveaux |
| Évaluation scriptée | 20/20 scénarios réussis après correction de la duplication des dialogues possédés |

Les cinq scénarios de scène finaux couvrent: un incrément vérifié; un incrément
volontairement non confirmé; le refus de répéter une observation consommée; une
simulation; un état de bascule modifié; la sortie PNG annotée; le refus
d'une action de vision après changement de pixels, avec un grounder factice;
l'aperçu, l'exécution et l'invalidation d'un profil par le hash de
l'exécutable. Les assertions regroupent plusieurs vérifications par scénario;
elles ne sont pas comptées comme des tests distincts.

Le test de bail interprocessus démarre un processus PowerShell distinct qui
possède le mutex nommé, vérifie le refus dans le processus de test, termine
uniquement cet enfant, puis vérifie la récupération de la propriété abandonnée.

Une première exécution groupée a rencontré un échec dans le test existant
`VisualDifferTests.Dimension_mismatch_fails_explicitly_and_writes_diff_map_when_requested`:
`System.Drawing.Image.Save` a signalé un encodeur null. Une exécution Core
complète ultérieure a réussi. Cet échec transitoire n'a été ni diagnostiqué ni
masqué par une logique de nouvelle tentative; le conserver comme point de suivi
s'il se reproduit en CI.

## Non mesuré

- Décisions réelles de Codex, Claude ou Gemini, coûts en tokens ou taux de réussite propres à chaque client.
- Envoi et vérification DOM WebView2 réels. Le refus fondé sur la propriété du point de terminaison est testé.
- Qualité réelle de la vision cloud/ONNX; les trois fixtures externes requises restent ignorées.
- OCR indépendant, couverture iframe/shadow DOM ou fonctionnement dans des bureaux protégés.
- Compatibilité étendue du glisser-déposer, de la sélection multiple et de la virtualisation avec les applications tierces.
- Taux d'actions sur une mauvaise cible: l'évaluation émet null, pas un zéro fabriqué.

Le banc scripté est une référence de régression. Son oracle final est
indépendant de la source des décisions, mais il connaît les vingt scénarios et
n'établit pas de généralisation. La CI exécute la même référence et conserve les
résultats JSONL avec les artefacts de test.
