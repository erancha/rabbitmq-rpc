using System.Text.Json;
using Xunit;
using TodoApp.Shared.Messages;

namespace TodoApp.Tests;

/// <summary>
/// Pins the transport contract of the creation reply: the worker constructs CreatedResponse,
/// property-name matching is case-insensitive, and a success payload without the id must fail
/// deserialization rather than default to zero.
/// </summary>
public class CreatedResponseTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Reads_the_workers_creation_payload()
    {
        var rpc = JsonSerializer.Deserialize<RpcResponse<CreatedResponse>>(
            "{\"Data\":{\"CreatedId\":7},\"Success\":true}", WebOptions)!;

        Assert.True(rpc.Success);
        Assert.Equal(7, rpc.Data!.CreatedId);
    }

    [Fact]
    public void Reads_a_camel_cased_payload_case_insensitively()
    {
        var rpc = JsonSerializer.Deserialize<RpcResponse<CreatedResponse>>(
            "{\"Data\":{\"createdId\":7},\"Success\":true}", WebOptions)!;

        Assert.Equal(7, rpc.Data!.CreatedId);
    }

    [Fact]
    public void Missing_created_id_fails_deserialization()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<RpcResponse<CreatedResponse>>(
                "{\"Data\":{},\"Success\":true}", WebOptions));
    }

    [Fact]
    public void Error_reply_deserializes_without_a_payload()
    {
        var rpc = JsonSerializer.Deserialize<RpcResponse<CreatedResponse>>(
            "{\"Success\":false,\"Error\":{\"Message\":\"m\",\"Kind\":\"VALIDATION\"}}", WebOptions)!;

        Assert.False(rpc.Success);
        Assert.Null(rpc.Data);
    }
}
