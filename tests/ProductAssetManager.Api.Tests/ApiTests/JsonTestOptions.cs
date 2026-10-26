using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProductAssetManager.Api.Tests.ApiTests;

public static class JsonTestOptions
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
