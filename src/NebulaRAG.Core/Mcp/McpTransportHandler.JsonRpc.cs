using System.Text.Json.Nodes;

namespace NebulaRAG.Core.Mcp;

public sealed partial class McpTransportHandler
{
    /// <summary>
    /// Builds a JSON-RPC success envelope.
    /// </summary>
    /// <param name="id">Request ID.</param>
    /// <param name="result">Result payload.</param>
    /// <returns>JSON-RPC success object.</returns>
    private static JsonObject BuildResult(JsonNode? id, JsonObject result)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        };
    }

    /// <summary>
    /// Builds a JSON-RPC error envelope.
    /// </summary>
    /// <param name="id">Request ID.</param>
    /// <param name="code">Error code.</param>
    /// <param name="message">Error message.</param>
    /// <returns>JSON-RPC error object.</returns>
    private static JsonObject BuildError(JsonNode? id, int code, string message)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
    }
}
