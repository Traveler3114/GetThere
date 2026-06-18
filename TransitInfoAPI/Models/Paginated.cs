namespace TransitInfoAPI.Models;

public record Paginated<T>(List<T> Data, int Total);
