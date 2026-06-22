using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOperatorTypeAndRealignRouteTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Update RouteType string values in CanonicalRoutes
            migrationBuilder.Sql("UPDATE CanonicalRoutes SET RouteType = 'Train' WHERE RouteType = 'Rail'");
            migrationBuilder.Sql("UPDATE CanonicalRoutes SET RouteType = 'Subway' WHERE RouteType = 'Metro'");
            migrationBuilder.Sql("UPDATE CanonicalRoutes SET RouteType = 'Bus' WHERE RouteType = 'Coach'");
            migrationBuilder.Sql("UPDATE CanonicalRoutes SET RouteType = 'Bicycle' WHERE RouteType = 'BikeShare'");
            migrationBuilder.Sql("UPDATE CanonicalRoutes SET RouteType = 'Scooter' WHERE RouteType = 'ScooterShare'");
            migrationBuilder.Sql("UPDATE CanonicalRoutes SET RouteType = 'Airplane' WHERE RouteType = 'Flight'");

            // Update PrimaryRouteType string values in CanonicalStations
            migrationBuilder.Sql("UPDATE CanonicalStations SET PrimaryRouteType = 'Train' WHERE PrimaryRouteType = 'Rail'");
            migrationBuilder.Sql("UPDATE CanonicalStations SET PrimaryRouteType = 'Subway' WHERE PrimaryRouteType = 'Metro'");
            migrationBuilder.Sql("UPDATE CanonicalStations SET PrimaryRouteType = 'Bus' WHERE PrimaryRouteType = 'Coach'");
            migrationBuilder.Sql("UPDATE CanonicalStations SET PrimaryRouteType = 'Bicycle' WHERE PrimaryRouteType = 'BikeShare'");
            migrationBuilder.Sql("UPDATE CanonicalStations SET PrimaryRouteType = 'Scooter' WHERE PrimaryRouteType = 'ScooterShare'");
            migrationBuilder.Sql("UPDATE CanonicalStations SET PrimaryRouteType = 'Airplane' WHERE PrimaryRouteType = 'Flight'");

            // Update RouteType string values in RawStops
            migrationBuilder.Sql("UPDATE RawStops SET RouteType = 'Train' WHERE RouteType = 'Rail'");
            migrationBuilder.Sql("UPDATE RawStops SET RouteType = 'Subway' WHERE RouteType = 'Metro'");
            migrationBuilder.Sql("UPDATE RawStops SET RouteType = 'Bus' WHERE RouteType = 'Coach'");
            migrationBuilder.Sql("UPDATE RawStops SET RouteType = 'Bicycle' WHERE RouteType = 'BikeShare'");
            migrationBuilder.Sql("UPDATE RawStops SET RouteType = 'Scooter' WHERE RouteType = 'ScooterShare'");
            migrationBuilder.Sql("UPDATE RawStops SET RouteType = 'Airplane' WHERE RouteType = 'Flight'");

            // Update RawRouteType string values in ReconciliationCandidates
            migrationBuilder.Sql("UPDATE ReconciliationCandidates SET RawRouteType = 'Train' WHERE RawRouteType = 'Rail'");
            migrationBuilder.Sql("UPDATE ReconciliationCandidates SET RawRouteType = 'Subway' WHERE RawRouteType = 'Metro'");
            migrationBuilder.Sql("UPDATE ReconciliationCandidates SET RawRouteType = 'Bus' WHERE RawRouteType = 'Coach'");
            migrationBuilder.Sql("UPDATE ReconciliationCandidates SET RawRouteType = 'Bicycle' WHERE RawRouteType = 'BikeShare'");
            migrationBuilder.Sql("UPDATE ReconciliationCandidates SET RawRouteType = 'Scooter' WHERE RawRouteType = 'ScooterShare'");
            migrationBuilder.Sql("UPDATE ReconciliationCandidates SET RawRouteType = 'Airplane' WHERE RawRouteType = 'Flight'");

            // Update CanonicalRouteType string values in ReconciliationCandidates
            migrationBuilder.Sql("UPDATE ReconciliationCandidates SET CanonicalRouteType = 'Train' WHERE CanonicalRouteType = 'Rail'");
            migrationBuilder.Sql("UPDATE ReconciliationCandidates SET CanonicalRouteType = 'Subway' WHERE CanonicalRouteType = 'Metro'");
            migrationBuilder.Sql("UPDATE ReconciliationCandidates SET CanonicalRouteType = 'Bus' WHERE CanonicalRouteType = 'Coach'");
            migrationBuilder.Sql("UPDATE ReconciliationCandidates SET CanonicalRouteType = 'Bicycle' WHERE CanonicalRouteType = 'BikeShare'");
            migrationBuilder.Sql("UPDATE ReconciliationCandidates SET CanonicalRouteType = 'Scooter' WHERE CanonicalRouteType = 'ScooterShare'");
            migrationBuilder.Sql("UPDATE ReconciliationCandidates SET CanonicalRouteType = 'Airplane' WHERE CanonicalRouteType = 'Flight'");

            migrationBuilder.DropColumn(
                name: "OperatorType",
                table: "Operators");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OperatorType",
                table: "Operators",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            // Reverse RouteType string values in CanonicalRoutes
            migrationBuilder.Sql("UPDATE CanonicalRoutes SET RouteType = 'Rail' WHERE RouteType = 'Train'");
            migrationBuilder.Sql("UPDATE CanonicalRoutes SET RouteType = 'Metro' WHERE RouteType = 'Subway'");
            migrationBuilder.Sql("UPDATE CanonicalRoutes SET RouteType = 'Coach' WHERE RouteType = 'Bus'");
            migrationBuilder.Sql("UPDATE CanonicalRoutes SET RouteType = 'BikeShare' WHERE RouteType = 'Bicycle'");
            migrationBuilder.Sql("UPDATE CanonicalRoutes SET RouteType = 'ScooterShare' WHERE RouteType = 'Scooter'");
            migrationBuilder.Sql("UPDATE CanonicalRoutes SET RouteType = 'Flight' WHERE RouteType = 'Airplane'");

            // Reverse PrimaryRouteType string values in CanonicalStations
            migrationBuilder.Sql("UPDATE CanonicalStations SET PrimaryRouteType = 'Rail' WHERE PrimaryRouteType = 'Train'");
            migrationBuilder.Sql("UPDATE CanonicalStations SET PrimaryRouteType = 'Metro' WHERE PrimaryRouteType = 'Subway'");
            migrationBuilder.Sql("UPDATE CanonicalStations SET PrimaryRouteType = 'Coach' WHERE PrimaryRouteType = 'Bus'");
            migrationBuilder.Sql("UPDATE CanonicalStations SET PrimaryRouteType = 'BikeShare' WHERE PrimaryRouteType = 'Bicycle'");
            migrationBuilder.Sql("UPDATE CanonicalStations SET PrimaryRouteType = 'ScooterShare' WHERE PrimaryRouteType = 'Scooter'");
            migrationBuilder.Sql("UPDATE CanonicalStations SET PrimaryRouteType = 'Flight' WHERE PrimaryRouteType = 'Airplane'");

            // Reverse RouteType string values in RawStops
            migrationBuilder.Sql("UPDATE RawStops SET RouteType = 'Rail' WHERE RouteType = 'Train'");
            migrationBuilder.Sql("UPDATE RawStops SET RouteType = 'Metro' WHERE RouteType = 'Subway'");
            migrationBuilder.Sql("UPDATE RawStops SET RouteType = 'Coach' WHERE RouteType = 'Bus'");
            migrationBuilder.Sql("UPDATE RawStops SET RouteType = 'BikeShare' WHERE RouteType = 'Bicycle'");
            migrationBuilder.Sql("UPDATE RawStops SET RouteType = 'ScooterShare' WHERE RouteType = 'Scooter'");
            migrationBuilder.Sql("UPDATE RawStops SET RouteType = 'Flight' WHERE RouteType = 'Airplane'");

            // Reverse RawRouteType string values in ReconciliationCandidates
            migrationBuilder.Sql("UPDATE ReconciliationCandidates SET RawRouteType = 'Rail' WHERE RawRouteType = 'Train'");
            migrationBuilder.Sql("UPDATE ReconciliationCandidates SET RawRouteType = 'Metro' WHERE RawRouteType = 'Subway'");
            migrationBuilder.Sql("UPDATE ReconciliationCandidates SET RawRouteType = 'Coach' WHERE RawRouteType = 'Bus'");
            migrationBuilder.Sql("UPDATE ReconciliationCandidates SET RawRouteType = 'BikeShare' WHERE RawRouteType = 'Bicycle'");
            migrationBuilder.Sql("UPDATE ReconciliationCandidates SET RawRouteType = 'ScooterShare' WHERE RawRouteType = 'Scooter'");
            migrationBuilder.Sql("UPDATE ReconciliationCandidates SET RawRouteType = 'Flight' WHERE RawRouteType = 'Airplane'");

            // Reverse CanonicalRouteType string values in ReconciliationCandidates
            migrationBuilder.Sql("UPDATE ReconciliationCandidates SET CanonicalRouteType = 'Rail' WHERE CanonicalRouteType = 'Train'");
            migrationBuilder.Sql("UPDATE ReconciliationCandidates SET CanonicalRouteType = 'Metro' WHERE CanonicalRouteType = 'Subway'");
            migrationBuilder.Sql("UPDATE ReconciliationCandidates SET CanonicalRouteType = 'Coach' WHERE CanonicalRouteType = 'Bus'");
            migrationBuilder.Sql("UPDATE ReconciliationCandidates SET CanonicalRouteType = 'BikeShare' WHERE CanonicalRouteType = 'Bicycle'");
            migrationBuilder.Sql("UPDATE ReconciliationCandidates SET CanonicalRouteType = 'ScooterShare' WHERE CanonicalRouteType = 'Scooter'");
            migrationBuilder.Sql("UPDATE ReconciliationCandidates SET CanonicalRouteType = 'Flight' WHERE CanonicalRouteType = 'Airplane'");
        }
    }
}
