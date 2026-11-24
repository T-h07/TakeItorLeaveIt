using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Core.Diagnostics;
using TakeItOrLeaveIt.Core.Multiplayer;
using TakeItOrLeaveIt.Core.Persistence;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using Xunit.Abstractions;
using static TakeItOrLeaveIt.Core.Tests.Catalog;
using static TakeItOrLeaveIt.Core.Tests.Sales;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Phase 08 hardening, suite S: logging privacy and untrusted input bounds.</summary>
public sealed class SecurityHardeningTests
{
    [Theory]
    [InlineData(@"Could not find file 'C:\Users\jane\AppData\LocalLow\TVGS\Schedule I\Saves\a.json'.", @"Could not find file '<path>\a.json'.")]
    [InlineData(@"Access to the path 'C:\Users\Jane Doe\Games\UserData\TakeItOrLeaveIt\s.json' is denied.", @"Access to the path '<path>\s.json' is denied.")]
    [InlineData(@"open D:\Steam\steamapps\common\Schedule I failed", @"open <path>\Schedule I failed")]
    [InlineData(@"\\fileserver\share\profiles\jane\x.json missing", @"<path>\x.json missing")]
    [InlineData("cannot read /home/jane/.local/share/x.json now", "cannot read <path>/x.json now")]
    [InlineData("C:/Users/jane/x.json.", "<path>/x.json.")]
    public void P08_S01_absolute_paths_are_reduced_to_the_file_name(string input, string expected)
        => Assert.Equal(expected, LogText.RedactPaths(input));

    [Theory]
    [InlineData("Order 3 has an invalid category or quantity; ignored.")]
    [InlineData("see https://github.com/T-h07/TakeItorLeaveIt for help")]
    [InlineData("ratio 3:2 and time 12:30")]
    [InlineData("")]
    public void P08_S02_text_without_paths_is_unchanged(string input)
        => Assert.Equal(input, LogText.RedactPaths(input));

    [Fact]
    public void P08_S03_no_user_name_survives_redaction_of_an_exception_with_a_stack_trace()
    {
        Exception thrown;
        try
        {
            throw new IOException(@"The process cannot access the file 'C:\Users\secretuser\AppData\x.json' because it is being used.");
        }
        catch (IOException ex)
        {
            thrown = ex;
        }

        var entry = LogText.Entry("Writing failed.", thrown);
        var described = LogText.Describe(thrown);

        Assert.DoesNotContain("secretuser", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("secretuser", described, StringComparison.Ordinal);
        Assert.StartsWith("IOException: ", described, StringComparison.Ordinal);
        Assert.Contains("x.json", described, StringComparison.Ordinal);
    }

    [Fact]
    public void P08_S04_untrusted_values_are_clipped_and_lose_control_characters()
    {
        var hostile = "line1\r\n[TakeItOrLeaveIt] Fake log line\t" + new string('A', 5_000);
        var clipped = LogText.Clip(hostile);

        Assert.True(clipped.Length <= LogText.MaxValueLength + 1);
        Assert.DoesNotContain('\n', clipped);
        Assert.DoesNotContain('\r', clipped);
        Assert.EndsWith("…", clipped, StringComparison.Ordinal);
        Assert.Equal(string.Empty, LogText.Clip(null));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogText.Clip("x", 0));
    }

    [Fact]
    public void P08_S05_one_log_entry_is_bounded()
    {
        var entry = LogText.Entry(new string('x', 100_000));
        Assert.True(entry.Length < LogText.MaxEntryLength + 20);
        Assert.EndsWith("(truncated)", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void P08_S06_a_duplicate_json_key_is_reported_clipped()
    {
        var key = new string('k', 10_000);
        var ex = Assert.ThrowsAny<FormatException>(() => MiniJson.Parse("{\"" + key + "\":1,\"" + key + "\":2}"));
        Assert.True(ex.Message.Length < 400, ex.Message.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void P08_S07_an_invalid_order_entry_id_is_reported_clipped()
    {
        var hostileId = "\n" + new string('z', 50_000);
        var json = MiniJson.Write(new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1d,
            ["orders"] = new List<object?> { new Dictionary<string, object?> { ["contract"] = hostileId } },
        });
        var issues = new List<string>();

        ActiveOrderStore.FromJson(json, issues);

        var issue = Assert.Single(issues);
        Assert.True(issue.Length < 200, issue.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.DoesNotContain('\n', issue);
    }
}
