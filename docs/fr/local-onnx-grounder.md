# Grounder de vision ONNX local

*[English version](../local-onnx-grounder.md)*

`WINSIGHT_VISION_BACKEND=onnx` sélectionne un `IElementGrounder` entièrement
local qui exécute **Qwen2.5-VL-3B** intégralement sur la machine: rien ne quitte
le poste, ce qui le rend utilisable sur des contenus réglementés ou sensibles.
Il ne se replie jamais sur le cloud: s'il est sélectionné mais mal configuré,
`find_element_visually` échoue explicitement.

## Fonctionnement

Le modèle est consommé sous la forme d'un découpage en 3 fichiers ONNX à la
manière de transformers.js (`vision_encoder` + `embed_tokens` +
`decoder_model_merged`, fp16), exécuté sur `Microsoft.ML.OnnxRuntime` standard
avec une boucle de génération mRoPE écrite à la main:

- `QwenImagePreprocessing`: redimensionnement intelligent (grille de 28px) +
  normalisation CLIP + découpage en patchs Qwen dans le tenseur `pixel_values`
  (`System.Drawing`).
- `QwenVisionTokenizer`: BPE Qwen au niveau octet via `CodeGenTokenizer` sur un
  vocabulaire complété par les tokens spéciaux Qwen; construit la consigne de
  chat.
- `OnnxLocalGrounder`: exécute vision → embed → decode, analyse la sortie
  `[x, y]` / `[x1, y1, x2, y2]` du modèle (`GroundingCoordinateParser`) et
  ramène le centre exprimé dans l'espace redimensionné en pixels de l'image
  d'origine.

## Configuration

| Variable                    | Valeur par défaut | Remarques                                |
| --------------------------- | ----------------- | ---------------------------------------- |
| `WINSIGHT_VISION_BACKEND`   | `anthropic`       | Définir à `onnx` pour le backend local.  |
| `WINSIGHT_VISION_ONNX_PATH` | -                 | Répertoire contenant le modèle provisionné (voir ci-dessous). |
| `WINSIGHT_VISION_EP`        | `cpu`             | `cpu` uniquement dans cette version.     |

## Provisionner le modèle

Les poids ne sont jamais versionnés. Lancez le script de provisionnement pour
télécharger le modèle (~6 GB) dans un répertoire cible, puis faites pointer
`WINSIGHT_VISION_ONNX_PATH` vers celui-ci:

```powershell
pwsh tools/provision-vision-model.ps1 -Destination C:\models\qwen2.5-vl-3b-onnx
$env:WINSIGHT_VISION_BACKEND = "onnx"
$env:WINSIGHT_VISION_ONNX_PATH = "C:\models\qwen2.5-vl-3b-onnx"
```

Le répertoire doit contenir `onnx\*_fp16.onnx` (+ les parties `.onnx_data*`),
ainsi que `vocab.json` et `merges.txt` (les fichiers du tokenizer BPE dont le
runtime a besoin).

## Limites connues (suites à donner)

- **Fournisseur d'exécution**: CPU uniquement. L'opérateur fusionné
  `MultiHeadAttention` du décodeur échoue actuellement sur l'EP DirectML, donc
  `WINSIGHT_VISION_EP=dml` est refusé tant que l'accélération GPU n'est pas
  implémentée. La latence sur CPU est de l'ordre de ~15 s par appel.
- **Fidélité du redimensionnement**: le rééchantillonneur bicubique de
  `System.Drawing` s'écarte de la référence (PIL) plus qu'idéalement, ce qui
  peut décaler le point ancré de quelques dizaines de pixels. Il tombe
  néanmoins sur l'élément cible sur les interfaces testées; un
  redimensionnement fidèle à PIL est une suite à donner pour les mises en page
  denses.
- **Qualité du modèle**: la v1 livre le décodeur Qwen2.5-VL-3B de base. Un
  fine-tuning dédié à l'ancrage d'interface (par exemple ZonUI-3B, dont la tour
  de vision est identique à celle de base) peut être substitué en remplaçant
  l'ONNX du décodeur.
