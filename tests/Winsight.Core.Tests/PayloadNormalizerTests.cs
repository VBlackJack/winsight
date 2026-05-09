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
using Winsight.Core.Modules.Diagnostics;
using Xunit;

namespace Winsight.Core.Tests;

public class PayloadNormalizerTests
{
    [Fact]
    public void Null_returns_null()
    {
        PayloadNormalizer.Normalize(null).Should().BeNull();
    }

    [Theory]
    [InlineData("hello")]
    [InlineData(42)]
    [InlineData(true)]
    [InlineData(3.14)]
    public void Primitives_pass_through_unchanged(object value)
    {
        PayloadNormalizer.Normalize(value).Should().Be(value);
    }

    [Fact]
    public void DateTime_is_converted_to_utc()
    {
        var local = new DateTime(2026, 5, 9, 14, 0, 0, DateTimeKind.Local);
        var normalized = PayloadNormalizer.Normalize(local);
        normalized.Should().BeOfType<DateTime>().Which.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Guid_is_serialized_as_string()
    {
        var guid = Guid.Parse("00000000-0000-0000-0000-000000000001");
        PayloadNormalizer.Normalize(guid).Should().Be(guid.ToString());
    }

    [Fact]
    public void Enum_is_serialized_as_string()
    {
        PayloadNormalizer.Normalize(DayOfWeek.Friday).Should().Be("Friday");
    }

    [Fact]
    public void ByteArray_is_serialized_as_hex()
    {
        var bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        PayloadNormalizer.Normalize(bytes).Should().Be("DEADBEEF");
    }

    [Fact]
    public void Typed_dictionary_is_recursively_normalized()
    {
        var input = new Dictionary<string, object>
        {
            ["name"] = "counter",
            ["value"] = 42,
            ["enabled"] = true,
        };

        var normalized = PayloadNormalizer.Normalize(input);

        normalized.Should().BeAssignableTo<IDictionary<string, object?>>();
        var dict = (IDictionary<string, object?>)normalized!;
        dict["name"].Should().Be("counter");
        dict["value"].Should().Be(42);
        dict["enabled"].Should().Be(true);
    }

    [Fact]
    public void Nested_dictionary_is_recursively_normalized()
    {
        var input = new Dictionary<string, object>
        {
            ["payload"] = new Dictionary<string, object>
            {
                ["Name"] = "cpu-usage",
                ["Mean"] = 1.4,
            },
        };

        var normalized = PayloadNormalizer.Normalize(input);

        var outer = (IDictionary<string, object?>)normalized!;
        var inner = (IDictionary<string, object?>)outer["payload"]!;
        inner["Name"].Should().Be("cpu-usage");
        inner["Mean"].Should().Be(1.4);
    }

    [Fact]
    public void List_is_recursively_normalized()
    {
        var input = new object[] { "a", 1, true };
        var normalized = PayloadNormalizer.Normalize(input);
        normalized.Should().BeAssignableTo<IReadOnlyList<object?>>();
        var list = (IReadOnlyList<object?>)normalized!;
        list.Should().HaveCount(3);
        list[0].Should().Be("a");
        list[1].Should().Be(1);
        list[2].Should().Be(true);
    }

    [Fact]
    public void Excessive_nesting_is_capped_with_marker()
    {
        // Build 6 levels of nested dictionaries (deeper than MaxDepth = 4).
        IDictionary<string, object> current = new Dictionary<string, object> { ["leaf"] = "deep" };
        for (var i = 0; i < 6; i++)
        {
            current = new Dictionary<string, object> { ["next"] = current };
        }

        var normalized = PayloadNormalizer.Normalize(current);

        // Walk down: at MaxDepth we should hit the marker string.
        object? cursor = normalized;
        for (var depth = 0; depth < PayloadNormalizer.MaxDepth; depth++)
        {
            cursor.Should().BeAssignableTo<IDictionary<string, object?>>();
            cursor = ((IDictionary<string, object?>)cursor!)["next"];
        }
        cursor.Should().Be("<max depth>");
    }

    [Fact]
    public void Unknown_type_falls_back_to_ToString()
    {
        var custom = new Uri("https://example.com/");
        PayloadNormalizer.Normalize(custom).Should().Be("https://example.com/");
    }
}
