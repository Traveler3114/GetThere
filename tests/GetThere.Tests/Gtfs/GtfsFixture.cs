using System.IO.Compression;
using System.Text;

namespace GetThere.Tests.Gtfs;

/// <summary>
/// Builds GTFS archives in memory.
/// <para>
/// The import pipeline is the largest untested surface in the solution, and the reason it stayed
/// that way is that exercising it looked like it needed a committed binary fixture. It does not: a
/// GTFS feed is a zip of CSV files, so the archive can be assembled per test and the contents stay
/// readable in the test that asserts on them.
/// </para>
/// </summary>
public sealed class GtfsFeedBuilder
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds or replaces a file. Pass the CSV exactly as it should appear in the archive.</summary>
    public GtfsFeedBuilder With(string fileName, string contents)
    {
        _files[fileName] = contents;
        return this;
    }

    /// <summary>Removes a file, so a test can assert what happens when a feed omits it.</summary>
    public GtfsFeedBuilder Without(string fileName)
    {
        _files.Remove(fileName);
        return this;
    }

    /// <summary>A minimal feed that satisfies the validator: agency, stops, routes, trips, stop_times.</summary>
    public static GtfsFeedBuilder Minimal()
    {
        return new GtfsFeedBuilder()
            .With("agency.txt",
                "agency_id,agency_name,agency_url,agency_timezone\n" +
                "ZET,Zagrebački električni tramvaj,https://www.zet.hr,Europe/Zagreb\n")
            .With("stops.txt",
                "stop_id,stop_name,stop_lat,stop_lon,location_type\n" +
                "S1,Britanski trg,45.8131,15.9619,0\n" +
                "S2,Trg bana Jelačića,45.8131,15.9775,0\n")
            .With("routes.txt",
                "route_id,agency_id,route_short_name,route_long_name,route_type\n" +
                "R1,ZET,6,Črnomerec - Sopot,0\n")
            .With("trips.txt",
                "route_id,service_id,trip_id,trip_headsign,shape_id\n" +
                "R1,WEEKDAY,T1,Sopot,SH1\n")
            .With("stop_times.txt",
                "trip_id,arrival_time,departure_time,stop_id,stop_sequence\n" +
                "T1,08:00:00,08:00:00,S1,1\n" +
                "T1,08:05:00,08:05:00,S2,2\n")
            .With("calendar.txt",
                "service_id,monday,tuesday,wednesday,thursday,friday,saturday,sunday,start_date,end_date\n" +
                "WEEKDAY,1,1,1,1,1,0,0,20260101,20261231\n");
    }

    /// <summary>Opens the assembled archive. The caller disposes it.</summary>
    public ZipArchive Build()
    {
        var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, contents) in _files)
            {
                var entry = archive.CreateEntry(name);
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(contents);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        buffer.Position = 0;
        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }
}
