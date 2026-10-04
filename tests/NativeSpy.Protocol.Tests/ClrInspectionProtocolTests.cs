using System.Text.Json;
using System.Text.Json.Nodes;
using NativeSpy.Protocol.Clr;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;
using NativeSpy.Protocol.Json;
using Xunit;

namespace NativeSpy.Protocol.Tests;

public sealed class ClrInspectionProtocolTests
{
    [Fact]
    public void Clr_values_round_trip_with_their_closed_union_branch()
    {
        var member = new MemberRefDto("session", "member", "boundary", "type");
        var response = new ReadFieldValuesResponseDto(new[]
        {
            new MemberReadResultDto(
                member,
                ClrReadOutcome.Available,
                ClrValueDto.Decimal(new[] { 1, 2, 3, 4 }, "12.5"))
        });

        var payload = ClrInspectionJsonCodec.SerializeReadFieldValuesResponse(response);
        var decoded = ClrInspectionJsonCodec.DeserializeReadFieldValuesResponse(payload);

        Assert.Equal("12.5", decoded.Results[0].Value!.DecimalDisplay);
        Assert.Equal(new[] { 1, 2, 3, 4 }, decoded.Results[0].Value!.DecimalBits);
    }

    [Fact]
    public void Unknown_and_cross_branch_properties_are_rejected()
    {
        var member = new MemberRefDto("session", "member", "boundary", "type");
        var response = new ReadFieldValuesResponseDto(new[]
        {
            new MemberReadResultDto(member, ClrReadOutcome.Available, ClrValueDto.Null())
        });
        var payload = ClrInspectionJsonCodec.SerializeReadFieldValuesResponse(response);
        var node = JsonNode.Parse(payload.GetRawText())!.AsObject();
        node["results"]![0]![@"value"]!["integerValue"] = "1";

        Assert.Throws<ProtocolJsonException>(() =>
            ClrInspectionJsonCodec.DeserializeReadFieldValuesResponse(
                JsonDocument.Parse(node.ToJsonString()).RootElement));
    }

    [Fact]
    public void Duplicate_nested_properties_are_rejected_before_deserialization()
    {
        const string payload = """
            {
              "results": [
                {
                  "member": {
                    "sessionId": "session",
                    "memberId": "member",
                    "boundaryId": "boundary",
                    "declaringTypeId": "type"
                  },
                  "outcome": "Available",
                  "value": { "kind": "Null", "kind": "Null" }
                }
              ]
            }
            """;

        using var document = JsonDocument.Parse(payload);
        Assert.Throws<ProtocolJsonException>(() =>
            ClrInspectionJsonCodec.DeserializeReadFieldValuesResponse(document.RootElement));
    }
}
