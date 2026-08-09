using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Services;

namespace GetThere.Tests.Gtfs;

/// <summary>
/// The parsing, pagination and mapping rules a custom source depends on. These are the parts an
/// admin configures blind through a form, so a wrong answer here shows up as a silently malformed
/// feed rather than an error.
/// </summary>
public class CustomSourceEngineTests
{
    // ── Data path traversal ───────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseJsonRows_reads_a_root_level_array()
    {
        var result = new ExtractionResult();
        var rows = CustomSourceEngine.ParseJsonRows("""[{"id":"A"},{"id":"B"}]""", "", result);

        Assert.Equal(2, rows.Count);
        Assert.Equal("A", rows[0]["id"]);
    }

    [Fact]
    public void ParseJsonRows_walks_a_dotted_data_path()
    {
        var result = new ExtractionResult();
        var rows = CustomSourceEngine.ParseJsonRows(
            """{"data":{"stops":[{"id":"A"},{"id":"B"},{"id":"C"}]}}""", "data.stops", result);

        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void ParseJsonRows_flattens_nested_objects_to_dotted_keys()
    {
        var result = new ExtractionResult();
        var rows = CustomSourceEngine.ParseJsonRows(
            """[{"id":"A","position":{"lat":45.8,"lon":15.9}}]""", "", result);

        Assert.Equal(45.8, rows[0]["position.lat"]);
        Assert.Equal(15.9, rows[0]["position.lon"]);
    }

    [Fact]
    public void ParseJsonRows_warns_rather_than_throws_on_a_missing_path()
    {
        var result = new ExtractionResult();
        var rows = CustomSourceEngine.ParseJsonRows("""{"data":{}}""", "data.stops", result);

        Assert.Empty(rows);
        Assert.Contains(result.Warnings, w => w.Contains("stops"));
    }

    [Fact]
    public void ParseJsonRows_warns_rather_than_throws_on_invalid_json()
    {
        var result = new ExtractionResult();
        var rows = CustomSourceEngine.ParseJsonRows("not json at all", "", result);

        Assert.Empty(rows);
        Assert.Contains(result.Warnings, w => w.Contains("not valid JSON"));
    }

    [Fact]
    public void ParseJsonRows_treats_an_object_map_as_rows_keyed_by_id()
    {
        var result = new ExtractionResult();
        var rows = CustomSourceEngine.ParseJsonRows(
            """{"stop1":{"name":"First"},"stop2":{"name":"Second"}}""", "", result);

        Assert.Equal(2, rows.Count);
        Assert.Equal("stop1", rows[0]["_key"]);
        Assert.Equal("First", rows[0]["name"]);
    }

    [Fact]
    public void ParseCsvRows_reads_a_header_and_rows()
    {
        var result = new ExtractionResult();
        var rows = CustomSourceEngine.ParseCsvRows("stop_id,stop_name\nA,First\nB,Second\n", result);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Second", rows[1]["stop_name"]);
    }

    // ── Pagination ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Page_pagination_increments_the_configured_parameter()
    {
        var config = CustomSourceEngine.ParsePagination("""{"type":"page","parameter":"p","startAt":1}""");

        Assert.Equal(PaginationKind.Page, config.Kind);
        Assert.Equal("https://x.test/a?p=1", CustomSourceEngine.ApplyPagination("https://x.test/a", config, 0, 0, null));
        Assert.Equal("https://x.test/a?p=3", CustomSourceEngine.ApplyPagination("https://x.test/a", config, 2, 0, null));
    }

    [Fact]
    public void Offset_pagination_uses_the_running_row_count()
    {
        var config = CustomSourceEngine.ParsePagination("""{"type":"offset","parameter":"skip"}""");

        Assert.Equal("https://x.test/a?skip=250", CustomSourceEngine.ApplyPagination("https://x.test/a", config, 1, 250, null));
    }

    [Fact]
    public void Cursor_pagination_omits_the_parameter_until_a_cursor_exists()
    {
        var config = CustomSourceEngine.ParsePagination(
            """{"type":"cursor","cursorParameter":"after","cursorPath":"meta.next"}""");

        Assert.Equal("https://x.test/a", CustomSourceEngine.ApplyPagination("https://x.test/a", config, 0, 0, null));
        Assert.Equal("https://x.test/a?after=abc", CustomSourceEngine.ApplyPagination("https://x.test/a", config, 1, 0, "abc"));
    }

    [Fact]
    public void Pagination_appends_with_an_ampersand_when_the_url_already_has_a_query()
    {
        var config = CustomSourceEngine.ParsePagination("""{"type":"page","parameter":"page"}""");

        Assert.Equal("https://x.test/a?k=1&page=0", CustomSourceEngine.ApplyPagination("https://x.test/a?k=1", config, 0, 0, null));
    }

    [Fact]
    public void Malformed_pagination_config_degrades_to_a_single_request()
    {
        Assert.Equal(PaginationKind.None, CustomSourceEngine.ParsePagination("{oops").Kind);
        Assert.Equal(PaginationKind.None, CustomSourceEngine.ParsePagination(null).Kind);
    }

    // ── Mapping kinds ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Direct_mapping_copies_the_source_value()
    {
        var mapped = Map(
            new ExtractedRow { ["code"] = "ZAG-1" },
            new CustomSourceMapping { SourceExpression = "code", TargetField = "StopId", Kind = MappingKind.Direct });

        Assert.Equal("ZAG-1", mapped["StopId"]);
    }

    [Fact]
    public void Direct_mapping_resolves_a_nested_field_by_its_leaf_name()
    {
        var mapped = Map(
            new ExtractedRow { ["position.lat"] = 45.8 },
            new CustomSourceMapping { SourceExpression = "lat", TargetField = "StopLat", Kind = MappingKind.Direct });

        Assert.Equal(45.8, mapped["StopLat"]);
    }

    [Fact]
    public void Static_mapping_ignores_the_row_entirely()
    {
        var mapped = Map(
            new ExtractedRow { ["anything"] = "ignored" },
            new CustomSourceMapping { SourceExpression = "3", TargetField = "RouteType", Kind = MappingKind.Static });

        Assert.Equal("3", mapped["RouteType"]);
    }

    [Fact]
    public void Expression_mapping_substitutes_placeholders()
    {
        var mapped = Map(
            new ExtractedRow { ["from"] = "Črnomerec", ["to"] = "Dubec" },
            new CustomSourceMapping { SourceExpression = "{from} - {to}", TargetField = "RouteLongName", Kind = MappingKind.Expression });

        Assert.Equal("Črnomerec - Dubec", mapped["RouteLongName"]);
    }

    [Theory]
    [InlineData("departs at 7:05", "07:05:00")]
    [InlineData("2026-08-08T15:29:34Z", "15:29:34")]
    [InlineData("25:10:00", "25:10:00")]   // past midnight, and GTFS says keep it
    [InlineData("no time here", null)]
    public void TimeExtract_normalizes_to_gtfs_time(string input, string? expected)
        => Assert.Equal(expected, CustomSourceEngine.ExtractTime(input));

    [Theory]
    [InlineData("2026-08-08", "20260808")]
    [InlineData("valid from 2026-01-31 onwards", "20260131")]
    [InlineData("20260808", "20260808")]
    [InlineData("nothing", null)]
    public void DateExtract_normalizes_to_gtfs_date(string input, string? expected)
        => Assert.Equal(expected, CustomSourceEngine.ExtractDate(input));

    [Fact]
    public void ApplyMappings_produces_one_target_row_per_source_row()
    {
        List<ExtractedRow> rows =
        [
            new() { ["id"] = "A", ["name"] = "First" },
            new() { ["id"] = "B", ["name"] = "Second" }
        ];

        var mapped = CustomSourceEngine.ApplyMappings(rows,
        [
            new CustomSourceMapping { SortOrder = 0, SourceExpression = "id", TargetField = "StopId", Kind = MappingKind.Direct },
            new CustomSourceMapping { SortOrder = 1, SourceExpression = "name", TargetField = "StopName", Kind = MappingKind.Direct }
        ]);

        Assert.Equal(2, mapped.Count);
        Assert.Equal("B", mapped[1]["StopId"]);
        Assert.Equal("Second", mapped[1]["StopName"]);
    }

    [Fact]
    public void Unmapped_target_fields_come_back_null_rather_than_missing()
    {
        var mapped = Map(
            new ExtractedRow { ["id"] = "A" },
            new CustomSourceMapping { SourceExpression = "absent", TargetField = "StopName", Kind = MappingKind.Direct });

        Assert.True(mapped.ContainsKey("StopName"));
        Assert.Null(mapped["StopName"]);
    }

    // ── Deduplication ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Deduplicate_keys_on_the_target_field_name()
    {
        // Regression: DistinctBy used to run on source rows, where a target name like "StopId"
        // matched nothing. Every key resolved empty, so an eight-stop feed imported as one stop
        // with no error anywhere.
        List<ExtractedRow> mapped =
        [
            new() { ["StopId"] = "AT-001", ["StopName"] = "First" },
            new() { ["StopId"] = "AT-002", ["StopName"] = "Second" },
            new() { ["StopId"] = "AT-001", ["StopName"] = "First again" }
        ];

        var result = CustomSourceEngine.Deduplicate(mapped, "StopId", out var dropped);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, dropped);
        Assert.Equal("First", result[0]["StopName"]);
    }

    [Fact]
    public void Deduplicate_is_a_no_op_without_a_key()
    {
        List<ExtractedRow> rows = [new() { ["StopId"] = "A" }, new() { ["StopId"] = "A" }];

        Assert.Equal(2, CustomSourceEngine.Deduplicate(rows, null, out var dropped).Count);
        Assert.Equal(0, dropped);
    }

    [Fact]
    public void Deduplicate_keeps_rows_that_have_no_value_for_the_key()
    {
        // Folding every blank together would delete real rows over a mapping mistake.
        List<ExtractedRow> rows =
        [
            new() { ["StopName"] = "No id here" },
            new() { ["StopName"] = "Also no id" },
            new() { ["StopId"] = "A" }
        ];

        Assert.Equal(3, CustomSourceEngine.Deduplicate(rows, "StopId", out var dropped).Count);
        Assert.Equal(0, dropped);
    }

    private static ExtractedRow Map(ExtractedRow row, CustomSourceMapping mapping)
        => CustomSourceEngine.ApplyMappings([row], [mapping])[0];
}
