using System.Net;
using MediaRenamer.Model;
using MediaRenamer.Plugin.Abstractions;
using MediaRenamer.Providers.Tmdb;

namespace MediaRenamer.Providers.Tests;

public class TmdbMovieModeTests
{
    private const string MovieJson = """
    {
      "id": 238, "title": "Il Padrino", "original_title": "The Godfather", "original_language": "en",
      "overview": "Trama", "poster_path": "/p.jpg", "release_date": "1972-03-14", "runtime": 175,
      "vote_average": 8.7, "vote_count": 20000, "imdb_id": "tt0068646",
      "genres": [{"id": 18, "name": "Drama"}, {"id": 80, "name": "Crime"}],
      "belongs_to_collection": {"name": "Il Padrino - Collezione"},
      "production_countries": [{"iso_3166_1": "US"}],
      "translations": {"translations": [
        {"iso_639_1": "it", "iso_3166_1": "IT", "data": {"title": "Il Padrino"}},
        {"iso_639_1": "en", "iso_3166_1": "US", "data": {"title": "The Godfather"}},
        {"iso_639_1": "de", "iso_3166_1": "DE", "data": {"title": ""}}
      ]},
      "alternative_titles": {"titles": [{"iso_3166_1": "US", "title": "Godfather"}]},
      "credits": {"crew": [{"job": "Director", "name": "Francis Ford Coppola"}, {"job": "Producer", "name": "Albert S. Ruddy"}], "cast": [{"name": "Marlon Brando"}]},
      "release_dates": {"results": [
        {"iso_3166_1": "US", "release_dates": [{"certification": "R"}]},
        {"iso_3166_1": "IT", "release_dates": [{"certification": "T"}]}
      ]}
    }
    """;

    private static IMovieProvider Provider(Func<HttpRequestMessage, HttpResponseMessage> respond, out FakeHandler handler)
    {
        var (ctx, h) = TestHost.Create(respond);
        handler = h;
        return new TmdbPlugin().CreateProviders(ctx).OfType<IMovieProvider>().Single();
    }

    [Fact]
    public async Task Search_sends_bearer_token_and_maps_results()
    {
        var p = Provider(_ => FakeHandler.Json("""
            {"results": [{"id": 238, "title": "Il Padrino", "original_title": "The Godfather", "original_language": "en",
                          "release_date": "1972-03-14", "poster_path": "/p.jpg"}]}
            """), out var h);

        var results = await p.SearchMovieAsync(new SearchQuery("godfather", 1972, "it-IT"), TestHost.TmdbConfig(), default);

        var r = Assert.Single(results);
        Assert.Equal(new ProviderRef("tmdb", ProviderMode.Movie, "238"), r.Ref);
        Assert.Equal(1972, r.Year);
        Assert.Equal("Movie", r.Kind);
        Assert.Contains("The Godfather", r.Titles.AllForMatching());
        Assert.NotNull(r.PosterUri);

        var req = Assert.Single(h.Requests);
        Assert.Equal("Bearer test-token", req.Auth);
        Assert.Contains("query=godfather", req.Uri.Query);
        Assert.Contains("year=1972", req.Uri.Query);
        Assert.Contains("language=it-IT", req.Uri.Query);
    }

    [Fact]
    public async Task GetMovie_keeps_every_language_variant_and_resolves_output_language_separately()
    {
        var p = Provider(_ => FakeHandler.Json(MovieJson), out _);
        var m = await p.GetMovieAsync(new ProviderRef("tmdb", ProviderMode.Movie, "238"), "it-IT", TestHost.TmdbConfig(), default);

        Assert.Equal("Il Padrino", m.Titles.Resolve("it-IT"));
        Assert.Equal("Il Padrino", m.Titles.Resolve("it"));
        Assert.Equal("The Godfather", m.Titles.Resolve("en"));
        var matching = m.Titles.AllForMatching().ToList();
        Assert.Contains("Godfather", matching);
        Assert.Contains("The Godfather", matching);
        Assert.Contains("Il Padrino", matching);

        Assert.Equal(1972, m.Year);
        Assert.Equal("en", m.OriginalLanguage);
        Assert.Equal(new[] { "Francis Ford Coppola" }, m.Directors);
        Assert.Equal(new[] { "Drama", "Crime" }, m.Genres);
        Assert.Equal("T", m.Certification);
        Assert.Equal(175, m.RuntimeMinutes);
        Assert.Equal("238", m.Ids.Get("tmdb"));
        Assert.Equal("tt0068646", m.Ids.Get("imdb"));
    }

    [Fact]
    public async Task Missing_translation_is_not_mislabeled_as_the_requested_language()
    {
        var p = Provider(_ => FakeHandler.Json(MovieJson), out _);
        var m = await p.GetMovieAsync(new ProviderRef("tmdb", ProviderMode.Movie, "238"), "fr-FR", TestHost.TmdbConfig(), default);

        Assert.Equal("The Godfather", m.Titles.Resolve("fr-FR", MissingTranslationBehavior.UseOriginal));
        Assert.Equal("The Godfather", m.Titles.Resolve("fr-FR", MissingTranslationBehavior.UseEnglish));
        Assert.DoesNotContain(m.Titles.All, t => t.Language == "fr-FR");
    }

    [Fact]
    public async Task Responses_are_cached()
    {
        var p = Provider(_ => FakeHandler.Json(MovieJson), out var h);
        var id = new ProviderRef("tmdb", ProviderMode.Movie, "238");

        await p.GetMovieAsync(id, "it-IT", TestHost.TmdbConfig(), default);
        await p.GetMovieAsync(id, "it-IT", TestHost.TmdbConfig(), default);
        await p.GetMovieAsync(id, "en-US", TestHost.TmdbConfig(), default);

        Assert.Equal(2, h.Requests.Count);
    }

    [Fact]
    public async Task Missing_token_is_a_configuration_error_and_makes_no_request()
    {
        var p = Provider(_ => FakeHandler.Json("{}"), out var h);
        var ex = await Assert.ThrowsAsync<ProviderException>(() =>
            p.SearchMovieAsync(new SearchQuery("x"), TestHost.Config(), default));
        Assert.Equal(ProviderErrorKind.Configuration, ex.Kind);
        Assert.Empty(h.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ProviderErrorKind.Authentication)]
    [InlineData(HttpStatusCode.NotFound, ProviderErrorKind.NotFound)]
    public async Task Http_errors_are_translated(HttpStatusCode code, ProviderErrorKind kind)
    {
        var p = Provider(_ => FakeHandler.Json("{}", code), out _);
        var ex = await Assert.ThrowsAsync<ProviderException>(() =>
            p.GetMovieAsync(new ProviderRef("tmdb", ProviderMode.Movie, "1"), "en", TestHost.TmdbConfig(), default));
        Assert.Equal(kind, ex.Kind);
    }

    [Fact]
    public async Task Persistent_429_is_reported_as_rate_limited_after_retries()
    {
        var p = Provider(_ => new HttpResponseMessage((HttpStatusCode)429)
        {
            Headers = { RetryAfter = new(TimeSpan.Zero) },
            Content = new StringContent("{}"),
        }, out var h);

        var ex = await Assert.ThrowsAsync<ProviderException>(() =>
            p.SearchMovieAsync(new SearchQuery("x"), TestHost.TmdbConfig(), default));

        Assert.Equal(ProviderErrorKind.RateLimited, ex.Kind);
        Assert.Equal(4, h.Requests.Count); // 1 + 3 retries
    }

    [Fact]
    public async Task TestConnection_reports_success_and_failure_without_throwing()
    {
        var ok = Provider(_ => FakeHandler.Json("{\"images\":{}}"), out _);
        Assert.True((await ok.TestConnectionAsync(TestHost.TmdbConfig(), default)).Success);

        var bad = Provider(_ => FakeHandler.Json("{}", HttpStatusCode.Unauthorized), out _);
        var r = await bad.TestConnectionAsync(TestHost.TmdbConfig(), default);
        Assert.False(r.Success);
        Assert.Contains("401", r.Message);
    }

    [Fact]
    public void Movie_and_episode_modes_are_distinct_providers_of_the_same_plugin()
    {
        var (ctx, _) = TestHost.Create(_ => FakeHandler.Json("{}"));
        var all = new TmdbPlugin().CreateProviders(ctx).ToList();

        Assert.Equal(new[] { "tmdb.movie", "tmdb.episode" }, all.Select(p => p.Key()));
        Assert.Equal("TMDB – Movie Mode", all[0].Label());
        Assert.Equal("TMDB – Episode Mode", all[1].Label());
        Assert.IsAssignableFrom<IMovieProvider>(all[0]);
        Assert.False(all[0] is IEpisodeProvider);
        Assert.IsAssignableFrom<IEpisodeProvider>(all[1]);
        Assert.False(all[1] is IMovieProvider);
    }
}

public class TmdbEpisodeModeTests
{
    private static IEpisodeProvider Provider(Func<HttpRequestMessage, HttpResponseMessage> respond, out FakeHandler handler)
    {
        var (ctx, h) = TestHost.Create(respond);
        handler = h;
        return new TmdbPlugin().CreateProviders(ctx).OfType<IEpisodeProvider>().Single();
    }

    private static HttpResponseMessage Route(HttpRequestMessage r)
    {
        var path = r.RequestUri!.AbsolutePath;
        var query = r.RequestUri.Query;
        if (path.EndsWith("/search/tv"))
            return FakeHandler.Json("""{"results":[{"id":1,"name":"Dark Angel","original_name":"Dark Angel","original_language":"en","first_air_date":"2000-10-03"}]}""");
        if (path.EndsWith("/tv/1"))
            return FakeHandler.Json("""
            {"id":1,"name":"Dark Angel","original_name":"Dark Angel","original_language":"en","first_air_date":"2000-10-03",
             "origin_country":["US"],"genres":[{"name":"Drama"}],
             "seasons":[{"season_number":0,"name":"Specials"},{"season_number":1,"name":"Season 1","air_date":"2000-10-03"}],
             "external_ids":{"imdb_id":"tt0230","tvdb_id":78874},
             "translations":{"translations":[{"iso_639_1":"it","iso_3166_1":"IT","data":{"name":"Dark Angel"}}]},
             "alternative_titles":{"results":[{"title":"Dark Angel (2000)"}]},
             "content_ratings":{"results":[{"iso_3166_1":"US","rating":"TV-14"}]}}
            """);
        if (path.EndsWith("/season/0"))
            return FakeHandler.Json("""{"episodes":[]}""");
        if (path.EndsWith("/season/1"))
            return query.Contains("language=it")
                ? FakeHandler.Json("""{"episodes":[{"id":10,"name":"Labirinto","season_number":1,"episode_number":1,"air_date":"2000-10-03","overview":"Trama"},{"id":11,"name":"Rifugio","season_number":1,"episode_number":2,"air_date":"2000-10-10"}]}""")
                : FakeHandler.Json("""{"episodes":[{"id":10,"name":"Labyrinth","season_number":1,"episode_number":1,"air_date":"2000-10-03","overview":"Plot"},{"id":11,"name":"Haven","season_number":1,"episode_number":2,"air_date":"2000-10-10"}]}""");
        return FakeHandler.Json("{}", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SearchSeries_maps_results_as_series()
    {
        var p = Provider(Route, out _);
        var r = Assert.Single(await p.SearchSeriesAsync(new SearchQuery("dark angel"), TestHost.TmdbConfig(), default));
        Assert.Equal(new ProviderRef("tmdb", ProviderMode.Episode, "1"), r.Ref);
        Assert.Equal("Series", r.Kind);
        Assert.Equal(2000, r.Year);
    }

    [Fact]
    public async Task GetSeries_maps_seasons_ids_and_titles()
    {
        var p = Provider(Route, out _);
        var s = await p.GetSeriesAsync(new ProviderRef("tmdb", ProviderMode.Episode, "1"), "it-IT", TestHost.TmdbConfig(), default);

        Assert.Equal(SeriesType.Tv, s.Type);
        Assert.Equal(2, s.SeasonCount);
        Assert.Equal(new[] { 0, 1 }, s.Seasons.Select(x => x.Number));
        Assert.Equal(2000, s.Year);
        Assert.Equal("78874", s.Ids.Get("tvdb"));
        Assert.Equal("tt0230", s.Ids.Get("imdb"));
        Assert.Equal("TV-14", s.Certification);
        Assert.Contains("Dark Angel (2000)", s.Titles.AllForMatching());
    }

    [Fact]
    public async Task GetEpisodes_merges_all_requested_languages_and_keeps_provider_order()
    {
        var p = Provider(Route, out var h);
        var eps = await p.GetEpisodesAsync(new ProviderRef("tmdb", ProviderMode.Episode, "1"),
            new[] { "it-IT", "en-US" }, TestHost.TmdbConfig(), default);

        Assert.Equal(new int?[] { 1, 2 }, eps.Select(e => e.Number.Episode));
        Assert.All(eps, e => Assert.Equal(1, e.Number.Season));
        Assert.Equal("Labirinto", eps[0].Titles.Resolve("it"));
        Assert.Equal("Labyrinth", eps[0].Titles.Resolve("en"));
        Assert.Contains("Labyrinth", eps[0].Titles.AllForMatching());
        Assert.Equal("Trama", eps[0].Overview);
        Assert.Equal(new DateOnly(2000, 10, 3), eps[0].AirDate);
        Assert.Equal(new ProviderRef("tmdb", ProviderMode.Episode, "1"), eps[0].SeriesRef);

        // series + (season 0 + season 1) x 2 languages
        Assert.Equal(1 + 2 * 2, h.Requests.Count);
    }

    [Fact]
    public async Task Anime_is_detected_from_animation_genre_and_japanese_origin()
    {
        var p = Provider(_ => FakeHandler.Json("""
            {"id":2,"name":"Frieren","original_name":"葬送のフリーレン","original_language":"ja","origin_country":["JP"],
             "genres":[{"name":"Animation"}],"seasons":[]}
            """), out _);
        var s = await p.GetSeriesAsync(new ProviderRef("tmdb", ProviderMode.Episode, "2"), "en", TestHost.TmdbConfig(), default);
        Assert.Equal(SeriesType.Anime, s.Type);
        Assert.Contains("葬送のフリーレン", s.Titles.AllForMatching());
    }
}

public class TmdbIntegrationTests
{
    private const string TokenVariable = "MEDIARENAMER_TMDB_TOKEN";

    [SkippableFact]
    public async Task Real_api_search_and_details()
    {
        var token = Environment.GetEnvironmentVariable(TokenVariable);
        Skip.If(string.IsNullOrEmpty(token), $"Set {TokenVariable} to run the real TMDB tests.");

        var ctx = new MediaRenamer.Infrastructure.PluginContext(
            new MediaRenamer.Infrastructure.Caching.MemoryResponseCache(), TimeSpan.FromMinutes(5), TestHost.TempDir());
        var providers = new TmdbPlugin().CreateProviders(ctx).ToList();
        var movie = providers.OfType<IMovieProvider>().Single();
        var tv = providers.OfType<IEpisodeProvider>().Single();
        var config = TestHost.TmdbConfig(token!, "it-IT");

        Assert.True((await movie.TestConnectionAsync(config, default)).Success);

        var found = await movie.SearchMovieAsync(new SearchQuery("The Godfather", 1972), config, default);
        Assert.NotEmpty(found);
        var m = await movie.GetMovieAsync(found[0].Ref, "it-IT", config, default);
        Assert.Contains("The Godfather", m.Titles.AllForMatching());
        Assert.Equal(1972, m.Year);

        var series = await tv.SearchSeriesAsync(new SearchQuery("Dark Angel"), config, default);
        Assert.NotEmpty(series);
        var eps = await tv.GetEpisodesAsync(series[0].Ref, new[] { "it-IT", "en-US" }, config, default);
        Assert.NotEmpty(eps);
    }
}
