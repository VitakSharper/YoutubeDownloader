using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Tests;

public class OutputFileNamerTests
{
    private const string Folder = @"C:\Music\Mix";

    private static readonly Func<string, bool> NothingExists = _ => false;

    [Theory]
    [InlineData("Bon Jovi - You Give Love A Bad Name", "Bon Jovi", "Bon Jovi - You Give Love A Bad Name")]
    [InlineData("Halo", "BeyoncéVEVO", "Beyoncé - Halo")]
    [InlineData("Beyoncé - Halo", "BeyoncéVEVO", "Beyoncé - Halo")]
    [InlineData("Lose Yourself", "Eminem - Topic", "Eminem - Lose Yourself")]
    [InlineData("Etanercept", "Armando Hasudungan", "Armando Hasudungan - Etanercept")]
    [InlineData("Song", "", "Song")]
    [InlineData("Song", "VEVO", "VEVO - Song")]
    public void TrackName_FollowsTheArtistTitleRule(string title, string channel, string expected)
    {
        Assert.Equal(expected, OutputFileNamer.TrackName(title, channel));
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(50, 3)]
    [InlineData(999, 3)]
    [InlineData(1000, 4)]
    public void NumberWidth_IsAtLeastThree(int highestNumber, int expected)
    {
        Assert.Equal(expected, OutputFileNamer.NumberWidth(highestNumber));
    }

    [Fact]
    public void Plan_BuildsNumberedNamesInTheFolder()
    {
        var entries = new[] { new NamingEntry(7, "Halo", "BeyoncéVEVO"), new NamingEntry(12, "Toto - Africa", "TotoVEVO") };

        var paths = OutputFileNamer.Plan(entries, Folder, "mp3", NothingExists);

        Assert.Equal(new[] { @"C:\Music\Mix\007. Beyoncé - Halo.mp3", @"C:\Music\Mix\012. Toto - Africa.mp3" }, paths);
    }

    [Fact]
    public void Plan_ExistingFile_GetsSuffixTwo()
    {
        var paths = OutputFileNamer.Plan(new[] { new NamingEntry(1, "B", "A") }, Folder, "mp3", path => path == @"C:\Music\Mix\001. A - B.mp3");

        Assert.Equal(@"C:\Music\Mix\001. A - B (2).mp3", Assert.Single(paths));
    }

    [Fact]
    public void Plan_ExistingPartSibling_GetsSuffixTwo()
    {
        var paths = OutputFileNamer.Plan(new[] { new NamingEntry(1, "B", "A") }, Folder, "mp3", path => path == @"C:\Music\Mix\001. A - B.part.mp3");

        Assert.Equal(@"C:\Music\Mix\001. A - B (2).mp3", Assert.Single(paths));
    }

    [Fact]
    public void Plan_SuffixKeepsCountingPastTwo()
    {
        var existing = new HashSet<string> { @"C:\Music\Mix\001. A - B.mp3", @"C:\Music\Mix\001. A - B (2).mp3" };

        var paths = OutputFileNamer.Plan(new[] { new NamingEntry(1, "B", "A") }, Folder, "mp3", existing.Contains);

        Assert.Equal(@"C:\Music\Mix\001. A - B (3).mp3", Assert.Single(paths));
    }

    [Fact]
    public void Plan_SameNameTwiceInOneCall_SecondGetsSuffix()
    {
        var entries = new[] { new NamingEntry(1, "B", "A"), new NamingEntry(1, "B", "A") };

        var paths = OutputFileNamer.Plan(entries, Folder, "mp3", NothingExists);

        Assert.Equal(new[] { @"C:\Music\Mix\001. A - B.mp3", @"C:\Music\Mix\001. A - B (2).mp3" }, paths);
    }

    [Fact]
    public void Plan_LongTitle_IsCutToTheLimit()
    {
        var paths = OutputFileNamer.Plan(new[] { new NamingEntry(1, new string('x', 400), "A") }, Folder, "mp3", NothingExists);

        var fileName = Path.GetFileName(Assert.Single(paths));
        Assert.StartsWith("001. ", fileName);
        Assert.EndsWith(".mp3", fileName);
        var trackName = fileName["001. ".Length..^".mp3".Length];
        Assert.InRange(trackName.Length, 1, OutputFileNamer.MaxTrackNameLength);
    }

    [Fact]
    public void Plan_WidensNumbersPast999()
    {
        var entries = new[] { new NamingEntry(1, "B", "A"), new NamingEntry(1000, "D", "C") };

        var paths = OutputFileNamer.Plan(entries, Folder, "mp3", NothingExists);

        Assert.Equal(new[] { @"C:\Music\Mix\0001. A - B.mp3", @"C:\Music\Mix\1000. C - D.mp3" }, paths);
    }

    [Fact]
    public void AlreadySaved_FindsVideoSavedInThisFolderWithThisExtension()
    {
        var history = new[] { new HistoryEntry { Url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ", FilePath = @"C:\Music\Mix\001. A - B.mp3" } };

        var ids = OutputFileNamer.AlreadySavedVideoIds(history, Folder, "mp3", _ => true);

        Assert.Equal("dQw4w9WgXcQ", Assert.Single(ids));
    }

    [Fact]
    public void AlreadySaved_IgnoresOtherFolderOtherExtensionMissingFileOrNoVideoId()
    {
        const string missingFile = @"C:\Music\Mix\003. A - D.mp3";
        var history = new[]
        {
            new HistoryEntry { Url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ", FilePath = @"C:\Music\Other\001. A - B.mp3" },
            new HistoryEntry { Url = "https://www.youtube.com/watch?v=9bZkp7q19f0", FilePath = @"C:\Music\Mix\002. A - C.opus" },
            new HistoryEntry { Url = "https://www.youtube.com/watch?v=kJQP7kiw5Fk", FilePath = missingFile },
            new HistoryEntry { Url = "https://example.com/page", FilePath = @"C:\Music\Mix\004. A - E.mp3" },
        };

        var ids = OutputFileNamer.AlreadySavedVideoIds(history, Folder, "mp3", path => path != missingFile);

        Assert.Empty(ids);
    }

    [Fact]
    public void SubFolder_IsTheSanitizedTitleUnderTheParent()
    {
        Assert.Equal(@"C:\Music\'80s_ Sing-Alongs_", OutputFileNamer.SubFolder(@"C:\Music", "'80s: Sing-Alongs?"));
    }
}
