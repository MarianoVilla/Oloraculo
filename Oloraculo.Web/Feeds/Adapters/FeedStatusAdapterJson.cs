using System.Text.Json;

namespace Oloraculo.Web.Feeds.Adapters
{
    internal static class FeedStatusAdapterJson
    {
        public static JsonElement ArrayAt(JsonElement root, params string[] names)
        {
            if (root.ValueKind == JsonValueKind.Array)
                return root;

            foreach (var name in names)
            {
                if (TryFindProperty(root, name, out var value) && value.ValueKind == JsonValueKind.Array)
                    return value;
            }

            return default;
        }

        public static int ArrayCount(JsonElement root, params string[] names)
        {
            var array = ArrayAt(root, names);
            return array.ValueKind == JsonValueKind.Array ? array.GetArrayLength() : 0;
        }

        public static bool HasPropertyName(JsonElement root, string name)
        {
            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in root.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (HasPropertyName(property.Value, name))
                        return true;
                }
            }

            if (root.ValueKind == JsonValueKind.Array)
                return root.EnumerateArray().Any(item => HasPropertyName(item, name));

            return false;
        }

        private static bool TryFindProperty(JsonElement root, string name, out JsonElement value)
        {
            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in root.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }

                    if (TryFindProperty(property.Value, name, out value))
                        return true;
                }
            }

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    if (TryFindProperty(item, name, out value))
                        return true;
                }
            }

            value = default;
            return false;
        }
    }
}
