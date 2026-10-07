using System.Runtime.CompilerServices;

// Lets GeoInsight.Tests exercise CensusGeocodingService.ParseResult directly
// against a captured real-response fixture, without a live network call in
// the test suite and without making the parser a public API surface.
[assembly: InternalsVisibleTo("GeoInsight.Tests")]
