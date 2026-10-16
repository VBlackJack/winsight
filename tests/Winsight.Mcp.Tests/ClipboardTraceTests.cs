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

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Winsight.Core.Models;
using Winsight.Mcp.Serialization;
using Winsight.Mcp.Tools;
using Xunit;

namespace Winsight.Mcp.Tests;

/// <summary>
/// The clipboard may hold secrets: a read_clipboard_text result must reach the
/// caller, but its trace line must carry only the size.
/// </summary>
public sealed class ClipboardTraceTests
{
    [Fact]
    public void A_clipboard_read_is_traced_by_size_only()
    {
        const string secret = "hunter2-token-7f3a";
        string payload = JsonSerializer.Serialize(
            OperationResult.OkWith(new ClipboardTextResult(secret, secret.Length)), JsonOptions.Default);

        string line = JsonSerializer.Serialize(
            TraceRecorder.BuildToolEntry("read_clipboard_text", TimeSpan.FromMilliseconds(3), payload, isError: false));

        line.Should().NotContain(secret);
        line.Should().Contain($"\"payloadBytes\":{Encoding.UTF8.GetByteCount(payload)}");
    }
}
