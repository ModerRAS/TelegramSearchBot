using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TelegramSearchBot.Model.Mcp {
    /// <summary>
    /// JSON-RPC 2.0 request message for MCP protocol.
    /// </summary>
    public class JsonRpcRequest {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonProperty("id")]
        public int? Id { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("params")]
        public object Params { get; set; }
    }

    /// <summary>
    /// JSON-RPC 2.0 response message for MCP protocol.
    /// </summary>
    public class JsonRpcResponse {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; }

        [JsonProperty("id")]
        public int? Id { get; set; }

        [JsonProperty("result")]
        public JToken Result { get; set; }

        [JsonProperty("error")]
        public JsonRpcError Error { get; set; }
    }

    public class JsonRpcError {
        [JsonProperty("code")]
        public int Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("data")]
        public object Data { get; set; }
    }

    /// <summary>
    /// MCP tool description as returned by tools/list.
    /// </summary>
    public class McpToolDescription {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("inputSchema")]
        public McpInputSchema InputSchema { get; set; }
    }

    public class McpInputSchema {
        [JsonProperty("type")]
        public string Type { get; set; } = "object";

        [JsonProperty("properties")]
        public Dictionary<string, McpPropertySchema> Properties { get; set; } = new();

        [JsonProperty("required")]
        public List<string> Required { get; set; } = new();
    }

    public class McpPropertySchema {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("default")]
        public object Default { get; set; }
    }

    /// <summary>
    /// MCP tool call result content.
    /// </summary>
    public class McpToolCallResult {
        [JsonProperty("content")]
        public List<McpContent> Content { get; set; } = new();

        [JsonProperty("isError")]
        public bool IsError { get; set; }
    }

    public class McpContent {
        [JsonProperty("type")]
        public string Type { get; set; } = "text";

        [JsonProperty("text")]
        public string Text { get; set; }
    }

    /// <summary>
    /// MCP initialize result.
    /// </summary>
    public class McpInitializeResult {
        [JsonProperty("protocolVersion")]
        public string ProtocolVersion { get; set; }

        [JsonProperty("capabilities")]
        public McpCapabilities Capabilities { get; set; }

        [JsonProperty("serverInfo")]
        public McpServerInfo ServerInfo { get; set; }
    }

    public class McpCapabilities {
        [JsonProperty("tools")]
        public object Tools { get; set; }
    }

    public class McpServerInfo {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }

    /// <summary>
    /// MCP tools/list result.
    /// </summary>
    public class McpToolsListResult {
        [JsonProperty("tools")]
        public List<McpToolDescription> Tools { get; set; } = new();
    }

    /// <summary>
    /// Configuration for an MCP server that can be connected to via stdio.
    /// </summary>
    public class McpServerConfig {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("command")]
        public string Command { get; set; }

        [JsonProperty("args")]
        public List<string> Args { get; set; } = new();

        [JsonProperty("env")]
        public Dictionary<string, string> Env { get; set; } = new();

        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;
    }
}
