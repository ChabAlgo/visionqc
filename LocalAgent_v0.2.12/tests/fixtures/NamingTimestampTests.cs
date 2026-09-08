using System;
using System.Collections.Generic;
using VisionQC.LocalAgent.Domain;
using VisionQC.LocalAgent.Services;
internal static class NamingTimestampTests
{
    static int count;
    static readonly string Cell = "J1037G87P611903999";
    static NamingProfile Profile(string mode = "auto", int index = 3) {
        return new NamingProfile { dateTime = new NamingFieldRule { mode = mode, tokenIndex = index } };
    }
    static void Check(string name, NamingProfile profile, string file, string timestamp, string status = "success") {
        var result = NamingProfileParser.Parse(profile, file);
        if (result.captureTimestamp != timestamp || result.status != status || result.cellId != Cell.Substring(0, 16))
            throw new Exception(name + ": " + result.status + " / " + result.captureTimestamp + " / " + result.cellId);
        count++;
    }
    static int Main() {
        string file = "TAB_" + Cell + "_20260807074705_CRACK AN(TOP)_BLUTOL.jpg";
        Check("server filename", Profile(), file, "2026-08-07T07:47:05");
        Check("specified token", Profile("token"), file, "2026-08-07T07:47:05");
        Check("old default also accepts combined", new NamingProfile(), file, "2026-08-07T07:47:05");
        Check("split auto", Profile(), "20260807_074705_" + Cell + ".jpg", "2026-08-07T07:47:05");
        Check("split location", Profile("token", 1), "20260807_074705_" + Cell + ".jpg", "2026-08-07T07:47:05");
        Check("wrong explicit location", Profile("token", 2), file, null, "partial");
        Check("invalid calendar", Profile(), "TAB_" + Cell + "_20260230074705.jpg", null, "partial");
        Check("invalid time", Profile(), "TAB_" + Cell + "_20260807244705.jpg", null, "partial");
        Check("leap day", Profile(), "TAB_" + Cell + "_20240229000000.jpg", "2024-02-29T00:00:00");
        Check("two timestamps", Profile(), file + "_20260807074706.jpg", null, "ambiguous");
        Check("combined plus split is ambiguous", Profile(), "20260807_074705_" + Cell + "_20260807074705.jpg", null, "ambiguous");
        var old = new NamingProfile {
            date = new NamingFieldRule { mode = "token", tokenIndex = 1 },
            time = new NamingFieldRule { mode = "token", tokenIndex = 4 }
        };
        Check("saved non-adjacent legacy", old, "20260807_" + Cell + "_LABEL_074705.jpg", "2026-08-07T07:47:05");
        old.dateTime = new NamingFieldRule { mode = "legacy" };
        Check("migrated non-adjacent legacy", old, "20260807_" + Cell + "_LABEL_074705.jpg", "2026-08-07T07:47:05");
        old.dateTime.mode = "auto"; old.time.tokenIndex = 9;
        Check("automatic ignores saved legacy positions", old, "20260807_074705_" + Cell + ".jpg", "2026-08-07T07:47:05");
        var custom = Profile(); custom.delimiter = "-";
        Check("custom delimiter", custom, "TAB-" + Cell + "-20260807074705.jpg", "2026-08-07T07:47:05");
        var preview = NamingProfileParser.Preview(new NamingPreviewRequest { profile = Profile("token", 0), fileNames = new List<string> { file } });
        if (preview.ok) throw new Exception("Invalid token index accepted");
        count++;
        Console.WriteLine("PASS " + count + " naming timestamp cases"); return 0;
    }
}
