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

using FluentAssertions;
using Winsight.Core.Modules.Vision;
using Xunit;

namespace Winsight.Core.Tests;

public class AnthropicResponseParserTests
{
    private const string SourceModel = "claude-sonnet-4-6";

    [Fact]
    public void Parses_clean_inline_json_response()
    {
        const string ModelText =
            "{\"x\":150,\"y\":80,\"width\":120,\"height\":40," +
            "\"confidence\":0.95,\"reasoning\":\"button labeled Increment.\"}";

        var grounded = AnthropicResponseParser.ParseGrounded(ModelText, SourceModel);

        grounded.X.Should().Be(150);
        grounded.Y.Should().Be(80);
        grounded.Width.Should().Be(120);
        grounded.Height.Should().Be(40);
        grounded.Confidence.Should().BeApproximately(0.95, 0.0001);
        grounded.Reasoning.Should().Be("button labeled Increment.");
        grounded.Source.Should().Be(SourceModel);
    }

    [Fact]
    public void Tolerates_leading_prose_before_json()
    {
        const string ModelText =
            "Sure, here is the location:\n" +
            "{\"x\":10,\"y\":20,\"width\":30,\"height\":40,\"confidence\":1,\"reasoning\":\"ok\"}";

        var grounded = AnthropicResponseParser.ParseGrounded(ModelText, SourceModel);

        grounded.X.Should().Be(10);
        grounded.Confidence.Should().Be(1);
    }

    [Fact]
    public void Tolerates_markdown_code_fence()
    {
        const string ModelText =
            "```json\n" +
            "{\"x\":50,\"y\":60,\"width\":70,\"height\":80,\"confidence\":0.5,\"reasoning\":\"r\"}\n" +
            "```";

        var grounded = AnthropicResponseParser.ParseGrounded(ModelText, SourceModel);

        grounded.X.Should().Be(50);
        grounded.Width.Should().Be(70);
    }

    [Fact]
    public void Handles_strings_inside_reasoning_with_braces()
    {
        const string ModelText =
            "{\"x\":1,\"y\":2,\"width\":3,\"height\":4,\"confidence\":0.1," +
            "\"reasoning\":\"contains a {brace} character.\"}";

        var grounded = AnthropicResponseParser.ParseGrounded(ModelText, SourceModel);

        grounded.Reasoning.Should().Be("contains a {brace} character.");
    }

    [Fact]
    public void Tolerates_numeric_strings()
    {
        const string ModelText =
            "{\"x\":\"100\",\"y\":\"200\",\"width\":\"50\",\"height\":\"25\"," +
            "\"confidence\":\"0.7\",\"reasoning\":\"r\"}";

        var grounded = AnthropicResponseParser.ParseGrounded(ModelText, SourceModel);

        grounded.X.Should().Be(100);
        grounded.Y.Should().Be(200);
        grounded.Confidence.Should().BeApproximately(0.7, 0.0001);
    }

    [Fact]
    public void Throws_when_no_json_object_present()
    {
        var act = () => AnthropicResponseParser.ParseGrounded(
            "I cannot find the element you described.",
            SourceModel);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Throws_when_required_property_missing()
    {
        const string ModelText =
            "{\"x\":1,\"y\":2,\"width\":3,\"confidence\":0.5,\"reasoning\":\"r\"}";

        var act = () => AnthropicResponseParser.ParseGrounded(ModelText, SourceModel);
        act.Should().Throw<FormatException>().WithMessage("*height*");
    }

    [Fact]
    public void ParseEnvelope_extracts_first_text_block()
    {
        const string ResponseJson = """
        {
          "id": "msg_01",
          "type": "message",
          "role": "assistant",
          "model": "claude-sonnet-4-6",
          "content": [
            {
              "type": "text",
              "text": "{\"x\":11,\"y\":22,\"width\":33,\"height\":44,\"confidence\":0.9,\"reasoning\":\"ok\"}"
            }
          ],
          "stop_reason": "end_turn",
          "usage": { "input_tokens": 100, "output_tokens": 50 }
        }
        """;

        var grounded = AnthropicResponseParser.ParseEnvelope(ResponseJson, SourceModel);

        grounded.X.Should().Be(11);
        grounded.Source.Should().Be(SourceModel);
    }

    [Fact]
    public void ParseEnvelope_throws_when_no_text_content()
    {
        const string ResponseJson = """
        {
          "content": [
            { "type": "tool_use", "id": "abc", "name": "x", "input": {} }
          ]
        }
        """;

        var act = () => AnthropicResponseParser.ParseEnvelope(ResponseJson, SourceModel);
        act.Should().Throw<FormatException>().WithMessage("*no text content*");
    }
}
