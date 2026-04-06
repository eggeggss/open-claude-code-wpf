using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Utils
{
    public static class JsonHelper
    {
        public static JObject ParseOrEmpty(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new JObject();
            try { return JObject.Parse(json); }
            catch { return new JObject(); }
        }

        public static T DeserializeOrDefault<T>(string json) where T : new()
        {
            if (string.IsNullOrWhiteSpace(json)) return new T();
            try { return JsonConvert.DeserializeObject<T>(json); }
            catch { return new T(); }
        }

        public static string SerializePretty(object obj)
        {
            return JsonConvert.SerializeObject(obj, Formatting.Indented);
        }

        public static string Serialize(object obj)
        {
            return JsonConvert.SerializeObject(obj);
        }

        public static string GetString(JObject obj, string key, string defaultValue = null)
        {
            return obj?[key]?.ToString() ?? defaultValue;
        }
    }
}
