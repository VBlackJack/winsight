// Copyright 2026 Julien Bombled
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace Winsight.Core.Modules.Vision;

/// <summary>Which <see cref="IElementGrounder"/> implementation to use.</summary>
public enum VisionBackendKind
{
    /// <summary>Cloud Anthropic vision API (default). Transmits the screenshot off-host.</summary>
    Anthropic,

    /// <summary>Local ONNX model, fully on-host. Required for regulated/sensitive content.</summary>
    Onnx,
}

/// <summary>ONNX Runtime execution provider for the local backend.</summary>
public enum OnnxExecutionProvider
{
    /// <summary>CPU — always available, slowest.</summary>
    Cpu,

    /// <summary>DirectML — any DX12 GPU on Windows.</summary>
    DirectML,

    /// <summary>CUDA — NVIDIA GPUs (requires a matching CUDA-enabled ONNX Runtime).</summary>
    Cuda,
}

/// <summary>
/// Resolves vision-backend configuration from environment variables (zero
/// hardcoding). The default is <see cref="VisionBackendKind.Anthropic"/> so
/// existing deployments are unaffected; the local backend is opt-in.
/// </summary>
public static class VisionBackend
{
    /// <summary>Selects the backend: <c>anthropic</c> (default) or <c>onnx</c>.</summary>
    public const string BackendVariable = "WINSIGHT_VISION_BACKEND";

    /// <summary>Filesystem directory holding the local ONNX model package.</summary>
    public const string OnnxModelPathVariable = "WINSIGHT_VISION_ONNX_PATH";

    /// <summary>Execution provider for the local backend: <c>cpu</c> (default), <c>dml</c>, or <c>cuda</c>.</summary>
    public const string ExecutionProviderVariable = "WINSIGHT_VISION_EP";

    /// <summary>Reads <see cref="BackendVariable"/>; unknown or unset resolves to Anthropic.</summary>
    public static VisionBackendKind ResolveBackend(string? raw)
    {
        return string.Equals(raw?.Trim(), "onnx", StringComparison.OrdinalIgnoreCase)
            ? VisionBackendKind.Onnx
            : VisionBackendKind.Anthropic;
    }

    /// <summary>Reads <see cref="ExecutionProviderVariable"/>; unknown or unset resolves to CPU.</summary>
    public static OnnxExecutionProvider ResolveExecutionProvider(string? raw)
    {
        return (raw?.Trim().ToLowerInvariant()) switch
        {
            "dml" or "directml" => OnnxExecutionProvider.DirectML,
            "cuda" => OnnxExecutionProvider.Cuda,
            _ => OnnxExecutionProvider.Cpu,
        };
    }
}
