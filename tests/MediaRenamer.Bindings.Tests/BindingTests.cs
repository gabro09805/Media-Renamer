using MediaRenamer.Bindings.Catalog;
using MediaRenamer.Bindings.Expressions;
using MediaRenamer.Bindings.Formats;
using MediaRenamer.Model;

namespace MediaRenamer.Bindings.Tests;

public class BindingTests
{
    private static readonly FormatRenderer Renderer = new();

    private static string R(string format, MediaBindingSource? source = null)
    {
        var result = Renderer.Render(format, source ?? TestMedia.EpisodeSource());
        Assert.True(result.Success, result.Error?.Message);
        return result.RelativePath!;
    }

    private static ExpressionException Fails(string format, MediaBindingSource? source = null)
    {
        var result = Renderer.Render(format, source ?? TestMedia.EpisodeSource());
        Assert.False(result.Success);
        return result.Error!;
    }

    [Fact]
    public void Upper_function_works_as_a_method_call()
    {
        Assert.Equal("BREAKING BAD", R("{n.upper()}"));
        Assert.Equal("BREAKING BAD", R("{n.toUpperCase()}"));
    }

    [Fact]
    public void Drive_binding_keeps_its_colon_when_it_is_the_whole_first_segment()
    {
        Assert.Matches("^[A-Z]:$", R("{drive}"));
        Assert.DoesNotContain(":", R("{n} {drive}"));
    }

    [Theory]
    [InlineData("{n}", "Breaking Bad")]
    [InlineData("{t}", "Cat's in the Bag")]
    [InlineData("{y}", "2008")]
    [InlineData("{ny}", "Breaking Bad (2008)")]
    [InlineData("{s}", "1")]
    [InlineData("{e}", "2")]
    [InlineData("{s00}", "01")]
    [InlineData("{e00}", "02")]
    [InlineData("{s00e00}", "S01E02")]
    [InlineData("{sxe}", "1x02")]
    [InlineData("{absolute}", "2")]
    [InlineData("{original}", "Breaking.Bad.S01E02.720p.HDTV-GRP")]
    [InlineData("{ext}", "mkv")]
    [InlineData("{vf}", "720p")]
    [InlineData("{vc}", "x264")]
    [InlineData("{ac}", "AAC")]
    [InlineData("{group}", "GRP")]
    [InlineData("{lang}", "en")]
    [InlineData("{folder}", "Downloads")]
    [InlineData("{d}", "2008-01-27")]
    [InlineData("{tmdbid}", "1396")]
    [InlineData("{imdbid}", "tt0903747")]
    [InlineData("{genre}", "Drama")]
    [InlineData("{genres}", "Drama, Crime")]
    [InlineData("{sn}", "Season 1")]
    [InlineData("{provider}", "tmdb")]
    public void Base_bindings(string format, string expected) => Assert.Equal(expected, R(format));

    [Fact]
    public void Movie_bindings()
    {
        var m = TestMedia.MovieSource();
        Assert.Equal("Inception (2010)/Inception (2010)", R("{n} ({y})/{n} ({y})", m));
        Assert.Equal("Christopher Nolan", R("{director}", m));
        Assert.Equal("148", R("{runtime}", m));
        Assert.Equal("8.4", R("{rating}", m));
    }

    [Fact]
    public void Bindings_are_identical_whatever_the_provider()
    {
        var tmdb = TestMedia.EpisodeSource();
        var anidb = TestMedia.EpisodeSource(series: TestMedia.BreakingBad("anidb", "4563"), episode: TestMedia.Episode("anidb"));

        foreach (var format in new[] { "{n}", "{t}", "{s00e00}", "{n}/Season {s00}/{n} - {s00e00} - {t}", "{y}", "{absolute}" })
            Assert.Equal(R(format, tmdb), R(format, anidb));
    }

    [Fact]
    public void Output_language_selects_the_title_but_never_changes_numbers()
    {
        var it = TestMedia.EpisodeSource(new MatchPreferences("it"));
        Assert.Equal("Il gatto è nel sacco", R("{t}", it));
        Assert.Equal("S01E02", R("{s00e00}", it));

        var ja = TestMedia.EpisodeSource(new MatchPreferences("ja"));
        Assert.Equal("ブレイキング・バッド", R("{n}", ja));

        var romanized = TestMedia.EpisodeSource(new MatchPreferences("ja-Latn"));
        Assert.Equal("Burei Kingu Baddo", R("{n}", romanized));
    }

    [Fact]
    public void Missing_translation_falls_back_to_original_or_primary_title()
    {
        var de = TestMedia.EpisodeSource(new MatchPreferences("de"));
        Assert.Equal("Breaking Bad", R("{n}", de));
    }

    [Fact]
    public void Romanized_Japanese_does_not_fall_back_to_kanji_titles()
    {
        var series = TestMedia.BreakingBad() with { Titles = new TitleSet(new[] { new LocalizedText("Breaking Bad", "en", TitleKind.Primary), new LocalizedText("ブレイキング・バッド", "ja") }) };
        var source = TestMedia.EpisodeSource(new MatchPreferences("ja-Latn"), series);
        Assert.Equal("Breaking Bad", R("{n}", source));
    }

    [Fact]
    public void Localize_reads_a_specific_language()
    {
        Assert.Equal("Il gatto è nel sacco", R("{localize.it.t}"));
        Assert.Equal("Burei Kingu Baddo", R("{localize['ja-Latn'].n}"));
    }

    [Fact]
    public void Episode_order_from_match_preferences_picks_the_numbering()
    {
        var dvd = TestMedia.EpisodeSource(new MatchPreferences("en", MatchMode.Opportunistic, EpisodeOrder.Dvd));
        Assert.Equal("S01E03", R("{s00e00}", dvd));

        var absolute = TestMedia.EpisodeSource(new MatchPreferences("en", MatchMode.Opportunistic, EpisodeOrder.Absolute));
        Assert.Equal("12", R("{absolute}", absolute));
        Assert.Equal("S01E12", R("{s00e00}", absolute));

        var production = TestMedia.EpisodeSource(new MatchPreferences("en", MatchMode.Opportunistic, EpisodeOrder.Production));
        Assert.Equal("S01E02", R("{s00e00}", production));
    }

    [Theory]
    [InlineData("{1 + 2 * 3}", "7")]
    [InlineData("{(1 + 2) * 3}", "9")]
    [InlineData("{10 / 2}", "5")]
    [InlineData("{7 / 2}", "3.5")]
    [InlineData("{7 % 4}", "3")]
    [InlineData("{-e}", "-2")]
    [InlineData("{e + 1}", "3")]
    [InlineData("{e.pad(3)}", "002")]
    [InlineData("{e.pad(4, 'x')}", "xxx2")]
    [InlineData("{'a' + 'b'}", "ab")]
    [InlineData("{'n=' + e}", "n=2")]
    [InlineData("{min(3, 1, 2)}", "1")]
    [InlineData("{max(e, 5)}", "5")]
    [InlineData("{abs(-4)}", "4")]
    [InlineData("{(10 / 2).pad(3)}", "005")]
    [InlineData("{(-e).abs()}", "2")]
    public void Arithmetic_and_precedence(string format, string expected) => Assert.Equal(expected, R(format));

    [Theory]
    [InlineData("{y ? 'has year' : 'no year'}", "has year")]
    [InlineData("{group ? \"[$group]\" : ''}", "[GRP]")]
    [InlineData("{special ?: 'regular'}", "regular")]
    [InlineData("{e == 2 ? 'two' : 'other'}", "two")]
    [InlineData("{e != 2 ? 'x' : 'y'}", "y")]
    [InlineData("{e > 1 && e < 3 ? 'in' : 'out'}", "in")]
    [InlineData("{e < 1 || e >= 2 ? 'in' : 'out'}", "in")]
    [InlineData("{!special ? 'not special' : 'special'}", "not special")]
    [InlineData("{s == 1 ? (e == 2 ? 'a' : 'b') : 'c'}", "a")]
    public void Conditionals_and_operators(string format, string expected) => Assert.Equal(expected, R(format));

    [Theory]
    [InlineData("{n.upper()}", "BREAKING BAD")]
    [InlineData("{n.lower()}", "breaking bad")]
    [InlineData("{n.space('.')}", "Breaking.Bad")]
    [InlineData("{n.replace('Bad', 'Good')}", "Breaking Good")]
    [InlineData("{n.replaceAll(/[aeiou]/, '_')}", "Br__k_ng B_d")]
    [InlineData("{n.replaceAll(/(\\w+) (\\w+)/, '$2 $1')}", "Bad Breaking")]
    [InlineData("{n.replaceFirst(/a/, 'A')}", "BreAking Bad")]
    [InlineData("{n.removeAll(/\\s/)}", "BreakingBad")]
    [InlineData("{n.take(5)}", "Break")]
    [InlineData("{n.drop(9)}", "Bad")]
    [InlineData("{n.substring(0, 8)}", "Breaking")]
    [InlineData("{n.before(' ')}", "Breaking")]
    [InlineData("{n.after(' ')}", "Bad")]
    [InlineData("{n.initials()}", "BB")]
    [InlineData("{n.length()}", "12")]
    [InlineData("{n.length}", "12")]
    [InlineData("{n.startsWith('Break') ? 'y' : 'n'}", "y")]
    [InlineData("{n.contains('xyz') ? 'y' : 'n'}", "n")]
    [InlineData("{'the simpsons'.upperInitial()}", "The Simpsons")]
    [InlineData("{'THE SIMPSONS'.lowerTrail()}", "The Simpsons")]
    [InlineData("{'The Simpsons'.sortName()}", "Simpsons, The")]
    [InlineData("{'Amélie Çà'.ascii()}", "Amelie Ca")]
    [InlineData("{'  x  '.trim()}", "x")]
    [InlineData("{n[0]}", "B")]
    [InlineData("{n[-1]}", "d")]
    public void String_functions(string format, string expected) => Assert.Equal(expected, R(format));

    [Theory]
    [InlineData("{original =~ /S(\\d+)E(\\d+)/ ? 'match' : 'none'}", "match")]
    [InlineData("{(original =~ /S(\\d+)E(\\d+)/)[1]}", "01")]
    [InlineData("{(original =~ /S(?<season>\\d+)E/)['season']}", "01")]
    [InlineData("{original.match(/S(\\d+)E/)}", "01")]
    [InlineData("{original.match(/(\\d+)p/)}", "720")]
    [InlineData("{original.match(/NOPE/) ?: 'none'}", "none")]
    [InlineData("{original ==~ /.*GRP/ ? 'full' : 'partial'}", "full")]
    [InlineData("{original ==~ /GRP/ ? 'full' : 'partial'}", "partial")]
    [InlineData("{original.matches(/Breaking.*/) ? 'y' : 'n'}", "y")]
    [InlineData("{original.split(/\\./)[0]}", "Breaking")]
    [InlineData("{n.replaceAll(/\\p{Upper}/, '#')}", "#reaking #ad")]
    [InlineData("{n.replaceAll(/a++/, 'A')}", "BreAking BAd")]
    public void Regex_functions(string format, string expected) => Assert.Equal(expected, R(format));

    [Theory]
    [InlineData("{d.format('yyyy')}", "2008")]
    [InlineData("{d.format('dd-MM-yyyy')}", "27-01-2008")]
    [InlineData("{d.year}", "2008")]
    [InlineData("{d.month}", "1")]
    [InlineData("{d.day}", "27")]
    [InlineData("{d.format('EEEE')}", "Sunday")]
    [InlineData("{now.format('yyyy')}", "2026")]
    [InlineData("{today}", "2026-10-30")]
    [InlineData("{d.year >= 2008 ? 'recent' : 'old'}", "recent")]
    public void Dates(string format, string expected) => Assert.Equal(expected, R(format));

    [Theory]
    [InlineData("{genres.join(' & ')}", "Drama & Crime")]
    [InlineData("{genres.size()}", "2")]
    [InlineData("{genres.first()}", "Drama")]
    [InlineData("{genres.last()}", "Crime")]
    [InlineData("{genres[1]}", "Crime")]
    [InlineData("{genres.sort().join(',')}", "Crime,Drama")]
    [InlineData("{genres.reverse().join(',')}", "Crime,Drama")]
    [InlineData("{[1, 2, 3].join('+')}", "1+2+3")]
    [InlineData("{genres.contains('Drama') ? 'y' : 'n'}", "y")]
    public void Lists(string format, string expected) => Assert.Equal(expected, R(format));

    [Fact]
    public void Variables_and_multiple_statements()
    {
        Assert.Equal("BREAKING BAD!", R("{ def x = n.upper(); x + '!' }"));
        Assert.Equal("6", R("{ a = 2; b = 3; a * b }"));
    }

    [Fact]
    public void Nested_bindings_inside_strings()
    {
        Assert.Equal("Breaking Bad - S01E02", R("{ \"$n - ${s00e00}\" }"));
        Assert.Equal("Breaking Bad (2008)", R("{ \"${n} (${y})\" }"));
        Assert.Equal("price $5", R("{ 'price $' + 5 }"));
        Assert.Equal("1 + 2 = 3", R("{ \"1 + 2 = ${1 + 2}\" }"));
        Assert.Equal("Break", R("{ \"${n.take(5)}\" }"));
        Assert.Equal("X Breaking Bad X", R("{ \"X ${ \"$n\" } X\" }"));
    }

    [Fact]
    public void Info_object_exposes_metadata()
    {
        Assert.Equal("tt0903747", R("{info.Ids.imdb}"));
        Assert.Equal("Drama, Crime", R("{info.Genres}"));
        Assert.Equal("tmdb", R("{info.Provider}"));
    }

    [Fact]
    public void Null_safe_navigation_and_missing_values()
    {
        Assert.Equal("none", R("{special?.format('x') ?: 'none'}"));
        Assert.Equal("none", R("{special.format('x') ?: 'none'}"));
    }

    [Fact]
    public void Value_without_data_renders_empty_with_a_warning_and_trims_dangling_dash()
    {
        var movie = TestMedia.MovieSource();
        var result = Renderer.Render("{n} - {s00e00}", movie);
        Assert.True(result.Success);
        Assert.Equal("Inception", result.RelativePath);
        Assert.Single(result.Warnings);
        Assert.Contains("s00e00", result.Warnings[0]);
    }

    [Fact]
    public void Series_format_builds_folders()
    {
        var result = Renderer.Render("{n}/Season {s00}/{n} - {s00e00} - {t}", TestMedia.EpisodeSource());
        Assert.Equal(new[] { "Breaking Bad", "Season 01", "Breaking Bad - S01E02 - Cat's in the Bag" }, result.Segments);
        Assert.Equal("Breaking Bad/Season 01", result.FolderPath);
        Assert.Equal("Breaking Bad - S01E02 - Cat's in the Bag", result.FileName);
    }

    [Fact]
    public void Backslash_is_also_a_folder_separator_and_empty_folders_are_dropped()
    {
        Assert.Equal("Breaking Bad/File", R("{n}\\\\File"));
        Assert.Equal("Breaking Bad/File", R("/{n}//File/"));
    }

    [Fact]
    public void Values_cannot_inject_folders_or_forbidden_characters()
    {
        var series = TestMedia.BreakingBad() with { Titles = new TitleSet(new[] { new LocalizedText("AC/DC: Live? <Tour> \"2008\"", "en", TitleKind.Primary) }) };
        var result = Renderer.Render("{n}", TestMedia.EpisodeSource(series: series));
        Assert.Single(result.Segments);
        Assert.Equal("AC-DC - Live Tour '2008'", result.Segments[0]);
    }

    [Fact]
    public void Trailing_dots_are_removed_from_each_name_as_windows_requires()
    {
        var series = TestMedia.BreakingBad();
        var episode = TestMedia.Episode() with { Titles = new TitleSet(new[] { new LocalizedText("Wait...", "en", TitleKind.Primary) }) };
        Assert.Equal("Breaking Bad - Wait", R("{n} - {t}", TestMedia.EpisodeSource(series: series, episode: episode)));
    }

    [Fact]
    public void Parent_directory_segments_are_dropped()
    {
        Assert.Equal("x", R("../{'..'}/x"));
    }

    [Fact]
    public void Same_format_re_renders_with_the_new_language()
    {
        var format = "{n} - {t}";
        Assert.Equal("Breaking Bad - Cat's in the Bag", R(format, TestMedia.EpisodeSource(new MatchPreferences("en"))));
        Assert.Equal("Breaking Bad - Il gatto è nel sacco", R(format, TestMedia.EpisodeSource(new MatchPreferences("it"))));
    }

    [Fact]
    public void Subtitle_bindings()
    {
        var sub = new SubtitleFile { Path = @"C:\x\a.it.forced.srt", Language = "it", Forced = true, Sdh = false };
        var source = new MediaBindingSource(sub, null, TestMedia.BreakingBad(), TestMedia.Episode(), new MatchPreferences("en"));
        Assert.Equal("it", R("{lang}", source));
        Assert.Equal("true", R("{forced}", source));
        Assert.Equal("Breaking Bad.it.forced", R("{n}.{lang}{forced ? '.forced' : ''}", source));
    }

    [Fact]
    public void Preset_formats_from_the_spec_render()
    {
        Assert.Equal("Inception (2010)/Inception (2010)", R(BuiltInFormats.Default(FormatKind.Movie).Expression, TestMedia.MovieSource()));
        Assert.Equal("Breaking Bad/Season 01/Breaking Bad - S01E02 - Cat's in the Bag", R(BuiltInFormats.Default(FormatKind.Series).Expression));
        Assert.Equal("Breaking Bad/Season 01/Breaking Bad - 2 - Cat's in the Bag", R(BuiltInFormats.All.First(f => f.Id == "builtin.anime.absolute").Expression));
    }

    [Fact]
    public void Every_catalog_binding_resolves_without_throwing_on_movies_and_episodes()
    {
        foreach (var source in new[] { TestMedia.MovieSource(), TestMedia.EpisodeSource(), new MediaBindingSource(null, null, null, null, new MatchPreferences()) })
            foreach (var definition in BindingCatalog.All)
                _ = definition.Resolve(source);
    }

    [Fact]
    public void Catalog_names_are_unique_and_have_help()
    {
        Assert.Equal(BindingCatalog.All.Count, BindingCatalog.All.Select(d => d.Name).Distinct().Count());
        Assert.All(BindingCatalog.All, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Description));
            Assert.False(string.IsNullOrWhiteSpace(d.Category));
        });
    }

    // ---- errors -------------------------------------------------------------------------------------------------

    [Fact]
    public void Unknown_binding_suggests_a_correction_and_points_at_it()
    {
        var error = Fails("{n} - {s00e0}");
        Assert.Contains("Unknown binding 's00e0'", error.Message);
        Assert.Contains("s00e00", error.Message);
        Assert.Equal("{n} - {s00e0}".IndexOf("s00e0", StringComparison.Ordinal), error.Start);
    }

    [Fact]
    public void Unknown_function_is_reported()
    {
        var error = Fails("{n.uper()}");
        Assert.Contains("Unknown function 'uper'", error.Message);
        Assert.Contains("upper", error.Message);
    }

    [Fact]
    public void Function_on_wrong_type_is_reported()
    {
        Assert.Contains("cannot be used", Fails("{e.upper()}").Message);
        Assert.Contains("number", Fails("{n - 1}").Message);
    }

    [Theory]
    [InlineData("{n", "Missing closing '}'")]
    [InlineData("n}", "Unexpected '}'")]
    [InlineData("{}", "empty")]
    [InlineData("{n.}", "after '.'")]
    [InlineData("{n +}", "Expected a value")]
    [InlineData("{(n}", "Expected ')'")]
    [InlineData("{'abc}", "Unterminated string")]
    [InlineData("{\"abc}", "Unterminated string")]
    [InlineData("{n.replaceAll(/abc, 'x')}", "Unterminated regular expression")]
    [InlineData("{n @ 1}", "Unexpected character '@'")]
    [InlineData("{ { n } }", "Unexpected '{'")]
    [InlineData("{n n}", "Expected an operator")]
    [InlineData("{n ? 'a'}", "Expected ':'")]
    public void Syntax_errors_have_clear_messages(string format, string fragment)
    {
        var validation = Renderer.Validate(format, TestMedia.EpisodeSource());
        Assert.False(validation.IsValid);
        Assert.Contains(fragment, validation.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(validation.Errors[0].Start, 0, format.Length);
    }

    [Fact]
    public void Invalid_regex_is_reported_without_rendering()
    {
        var validation = Renderer.Validate("{n.replaceAll(/(unclosed/, '')}", TestMedia.EpisodeSource());
        Assert.False(validation.IsValid);
        Assert.Contains("regular expression", validation.Errors[0].Message);
    }

    [Fact]
    public void Validation_reports_every_unknown_name_and_accepts_defined_variables()
    {
        var validation = Renderer.Validate("{foo} {bar.upper()} {ny} { def x = n; x }", TestMedia.EpisodeSource());
        Assert.Equal(2, validation.Errors.Count);
        Assert.True(Renderer.Validate("{n}/Season {s00}/{n} - {s00e00} - {t}", TestMedia.EpisodeSource()).IsValid);
    }

    [Fact]
    public void Division_by_zero_and_empty_result_are_errors()
    {
        Assert.Contains("Division by zero", Fails("{1 / 0}").Message);
        Assert.Contains("empty", Fails("{''}").Message);
    }

    [Fact]
    public void Error_inside_a_function_points_at_the_call()
    {
        var format = "{n.replaceAll('(', 'x')}";
        var error = Fails(format);
        Assert.Contains("regular expression", error.Message);
        Assert.InRange(error.Start, 0, format.Length);
    }

    // ---- highlighter --------------------------------------------------------------------------------------------

    [Fact]
    public void Highlighter_classifies_tokens()
    {
        var text = "{n}/S{s00} {y ? 'a' : upper(1)} {x.pad(2)} {/re/}";
        var spans = SyntaxHighlighter.Highlight(text);
        SyntaxKind KindAt(int index) => spans.First(s => index >= s.Start && index < s.Start + s.Length).Kind;

        Assert.Equal(SyntaxKind.Brace, KindAt(0));
        Assert.Equal(SyntaxKind.Binding, KindAt(1));
        Assert.Equal(SyntaxKind.Brace, KindAt(2));
        Assert.Equal(SyntaxKind.Separator, KindAt(3));
        Assert.Equal(SyntaxKind.Text, KindAt(4));
        Assert.Equal(SyntaxKind.Binding, KindAt(text.IndexOf("s00", StringComparison.Ordinal)));
        Assert.Equal(SyntaxKind.Operator, KindAt(text.IndexOf('?')));
        Assert.Equal(SyntaxKind.String, KindAt(text.IndexOf("'a'", StringComparison.Ordinal)));
        Assert.Equal(SyntaxKind.Function, KindAt(text.IndexOf("upper", StringComparison.Ordinal)));
        Assert.Equal(SyntaxKind.Number, KindAt(text.IndexOf("(1)", StringComparison.Ordinal) + 1));
        Assert.Equal(SyntaxKind.Function, KindAt(text.IndexOf("pad", StringComparison.Ordinal)));
        Assert.Equal(SyntaxKind.Regex, KindAt(text.IndexOf("/re/", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("{n.replaceAll(/abc")]
    [InlineData("{'unterminated")]
    [InlineData("}{")]
    [InlineData("{@}")]
    [InlineData("")]
    public void Highlighter_never_throws_and_covers_only_valid_ranges(string text)
    {
        var spans = SyntaxHighlighter.Highlight(text);
        Assert.All(spans, s =>
        {
            Assert.True(s.Start >= 0 && s.Length > 0 && s.Start + s.Length <= text.Length);
        });
        for (var i = 1; i < spans.Count; i++) Assert.True(spans[i].Start >= spans[i - 1].Start + spans[i - 1].Length);
    }

    [Fact]
    public void Highlighter_marks_the_broken_part_of_a_block_as_error()
    {
        var spans = SyntaxHighlighter.Highlight("{n @ 1}");
        Assert.Contains(spans, s => s.Kind == SyntaxKind.Error);
    }
}

