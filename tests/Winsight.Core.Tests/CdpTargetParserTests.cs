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

using System.Text.Json;
using FluentAssertions;
using Winsight.Core.Models;
using Winsight.Core.Modules.Cdp;
using Xunit;

namespace Winsight.Core.Tests;

public sealed class CdpTargetParserTests
{
    [Fact]
    public void Parse_TypicalWebView2Payload_ReturnsPageTarget()
    {
        const string Json = """
        [
          {
            "description": "",
            "devtoolsFrontendUrl": "/devtools/inspector.html?ws=127.0.0.1:9222/devtools/page/AB12",
            "id": "AB12",
            "title": "Terminal",
            "type": "page",
            "url": "https://appassets.example/index.html",
            "webSocketDebuggerUrl": "ws://127.0.0.1:9222/devtools/page/AB12"
          }
        ]
        """;

        IReadOnlyList<CdpTarget> targets = CdpTargetParser.Parse(Json);

        targets.Should().ContainSingle();
        targets[0].Id.Should().Be("AB12");
        targets[0].Title.Should().Be("Terminal");
        targets[0].Type.Should().Be("page");
        targets[0].Url.Should().Be("https://appassets.example/index.html");
        targets[0].WebSocketDebuggerUrl.Should().Be("ws://127.0.0.1:9222/devtools/page/AB12");
    }

    [Fact]
    public void Parse_EntryWithoutId_IsSkipped()
    {
        const string Json = """[{ "title": "orphan" }, { "id": "X1", "type": "page" }]""";

        IReadOnlyList<CdpTarget> targets = CdpTargetParser.Parse(Json);

        targets.Should().ContainSingle(t => t.Id == "X1");
    }

    [Fact]
    public void Parse_MissingOptionalFields_DegradeGracefully()
    {
        const string Json = """[{ "id": "X1" }]""";

        IReadOnlyList<CdpTarget> targets = CdpTargetParser.Parse(Json);

        targets[0].Title.Should().BeEmpty();
        targets[0].Url.Should().BeEmpty();
        targets[0].Type.Should().BeEmpty();
        targets[0].WebSocketDebuggerUrl.Should().BeNull();
    }

    [Fact]
    public void Parse_EmptyArray_ReturnsEmptyList()
    {
        CdpTargetParser.Parse("[]").Should().BeEmpty();
    }

    [Fact]
    public void Parse_NonArrayRoot_Throws()
    {
        Action act = () => CdpTargetParser.Parse("""{ "id": "X1" }""");

        act.Should().Throw<JsonException>();
    }
}
