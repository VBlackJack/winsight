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

public sealed class CdpResponseParserTests
{
    [Fact]
    public void TryReadId_ResponseFrame_ReturnsId()
    {
        using JsonDocument document = JsonDocument.Parse("""{ "id": 7, "result": {} }""");

        CdpResponseParser.TryReadId(document.RootElement).Should().Be(7);
    }

    [Fact]
    public void TryReadId_EventFrame_ReturnsNull()
    {
        using JsonDocument document = JsonDocument.Parse(
            """{ "method": "Runtime.consoleAPICalled", "params": {} }""");

        CdpResponseParser.TryReadId(document.RootElement).Should().BeNull();
    }

    [Fact]
    public void TryReadError_ErrorFrame_ReturnsMessage()
    {
        using JsonDocument document = JsonDocument.Parse(
            """{ "id": 1, "error": { "code": -32601, "message": "Method not found" } }""");

        CdpResponseParser.TryReadError(document.RootElement).Should().Be("Method not found");
    }

    [Fact]
    public void TryReadError_SuccessFrame_ReturnsNull()
    {
        using JsonDocument document = JsonDocument.Parse("""{ "id": 1, "result": {} }""");

        CdpResponseParser.TryReadError(document.RootElement).Should().BeNull();
    }

    [Fact]
    public void ParseEvaluation_StringValue_ReturnsSerializedValue()
    {
        using JsonDocument document = JsonDocument.Parse(
            """{ "result": { "type": "string", "value": "hello" } }""");

        CdpEvaluationResult evaluation = CdpResponseParser.ParseEvaluation(document.RootElement);

        evaluation.Success.Should().BeTrue();
        evaluation.ValueType.Should().Be("string");
        evaluation.ValueJson.Should().Be("\"hello\"");
    }

    [Fact]
    public void ParseEvaluation_BooleanValue_ReturnsRawJson()
    {
        using JsonDocument document = JsonDocument.Parse(
            """{ "result": { "type": "boolean", "value": true } }""");

        CdpEvaluationResult evaluation = CdpResponseParser.ParseEvaluation(document.RootElement);

        evaluation.ValueJson.Should().Be("true");
    }

    [Fact]
    public void ParseEvaluation_ScriptException_ReturnsFailureWithDescription()
    {
        using JsonDocument document = JsonDocument.Parse("""
        {
          "result": { "type": "object", "subtype": "error" },
          "exceptionDetails": {
            "text": "Uncaught",
            "exception": { "description": "ReferenceError: nope is not defined" }
          }
        }
        """);

        CdpEvaluationResult evaluation = CdpResponseParser.ParseEvaluation(document.RootElement);

        evaluation.Success.Should().BeFalse();
        evaluation.ExceptionMessage.Should().Contain("ReferenceError");
    }

    [Fact]
    public void ParseEvaluation_UndefinedResult_ReturnsNullValue()
    {
        using JsonDocument document = JsonDocument.Parse(
            """{ "result": { "type": "undefined" } }""");

        CdpEvaluationResult evaluation = CdpResponseParser.ParseEvaluation(document.RootElement);

        evaluation.Success.Should().BeTrue();
        evaluation.ValueJson.Should().BeNull();
        evaluation.ValueType.Should().Be("undefined");
    }
}
