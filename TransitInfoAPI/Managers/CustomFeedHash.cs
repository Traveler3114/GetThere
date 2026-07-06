using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TransitInfoAPI.Managers;

public static class CustomFeedHash
{
    public static string ComputeHash(IReadOnlyDictionary<string, List<Dictionary<string, object?>>> tableRecords)
    {
        var builder = new StringBuilder();

        foreach (var tableName in tableRecords.Keys.OrderBy(static k => k, StringComparer.Ordinal))
        {
            var records = tableRecords[tableName];
            builder.Append(tableName);
            builder.Append('|');

            var serializedRecords = new List<string>(records.Count);
            foreach (var record in records)
            {
                var sorted = new SortedDictionary<string, object?>(record, StringComparer.Ordinal);
                serializedRecords.Add(JsonSerializer.Serialize(sorted));
            }

            serializedRecords.Sort(StringComparer.Ordinal);

            foreach (var json in serializedRecords)
            {
                builder.Append(json);
                builder.Append('\n');
            }
        }

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        return Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
    }
}
