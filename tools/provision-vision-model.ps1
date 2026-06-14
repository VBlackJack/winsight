# Copyright 2026 Julien Bombled
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#     http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

<#
.SYNOPSIS
    Downloads the local Qwen2.5-VL-3B ONNX model used by OnnxLocalGrounder.
.DESCRIPTION
    Fetches the fp16 3-ONNX split + processor config from onnx-community, plus the
    BPE tokenizer files (vocab.json / merges.txt) from the base Qwen repo, into a
    destination directory. Weights are large (~6 GB) and are never committed.
    Point WINSIGHT_VISION_ONNX_PATH at the destination afterwards.
.PARAMETER Destination
    Target directory for the model package.
.EXAMPLE
    pwsh tools/provision-vision-model.ps1 -Destination C:\models\qwen2.5-vl-3b-onnx
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$Destination
)

$ErrorActionPreference = 'Stop'

$OnnxRepo = 'onnx-community/Qwen2.5-VL-3B-Instruct-ONNX'
$BaseRepo = 'Qwen/Qwen2.5-VL-3B-Instruct'

# relativePath -> source repo. The .onnx_data* parts are the external weights.
$Manifest = [ordered]@{
    'onnx/vision_encoder_fp16.onnx'              = $OnnxRepo
    'onnx/vision_encoder_fp16.onnx_data'         = $OnnxRepo
    'onnx/embed_tokens_fp16.onnx'                = $OnnxRepo
    'onnx/embed_tokens_fp16.onnx_data'           = $OnnxRepo
    'onnx/decoder_model_merged_fp16.onnx'        = $OnnxRepo
    'onnx/decoder_model_merged_fp16.onnx_data'   = $OnnxRepo
    'onnx/decoder_model_merged_fp16.onnx_data_1' = $OnnxRepo
    'onnx/decoder_model_merged_fp16.onnx_data_2' = $OnnxRepo
    'config.json'                                = $OnnxRepo
    'generation_config.json'                     = $OnnxRepo
    'preprocessor_config.json'                   = $OnnxRepo
    'processor_config.json'                      = $OnnxRepo
    'tokenizer.json'                             = $OnnxRepo
    'tokenizer_config.json'                      = $OnnxRepo
    'chat_template.json'                         = $OnnxRepo
    'vocab.json'                                 = $BaseRepo
    'merges.txt'                                 = $BaseRepo
}

function Get-HuggingFaceUrl {
    param([string]$Repo, [string]$RelativePath)
    return "https://huggingface.co/$Repo/resolve/main/$RelativePath"
}

Write-Host "Provisioning Qwen2.5-VL-3B ONNX into: $Destination"
Write-Host "Files: $($Manifest.Count) (several gigabytes total)."

if (-not $PSCmdlet.ShouldProcess($Destination, "Download $($Manifest.Count) model files (~6 GB)")) {
    Write-Host "Dry run: no files downloaded."
    return
}

New-Item -ItemType Directory -Force -Path $Destination | Out-Null

foreach ($entry in $Manifest.GetEnumerator()) {
    $relativePath = $entry.Key
    $repo = $entry.Value
    $targetPath = Join-Path $Destination $relativePath
    $targetDir = Split-Path -Parent $targetPath
    if (-not (Test-Path $targetDir)) {
        New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
    }

    if (Test-Path $targetPath) {
        Write-Host "  skip (exists): $relativePath"
        continue
    }

    $url = Get-HuggingFaceUrl -Repo $repo -RelativePath $relativePath
    Write-Host "  download: $relativePath"
    Invoke-WebRequest -Uri $url -OutFile $targetPath -UseBasicParsing
}

Write-Host "Done. Set WINSIGHT_VISION_ONNX_PATH=$Destination and WINSIGHT_VISION_BACKEND=onnx."
