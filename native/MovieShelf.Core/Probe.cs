using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MovieShelf.Core;

/// <summary>What ffprobe says about one video file.</summary>
public sealed class ProbeInfo
{
    public int? Duration;                // seconds
    public int? Width, Height;
    public string? VCodec;
    public long? Bitrate;
    public List<string> Audio = new(), Subs = new();   // language names, no repeats
    public int NAudio, NSubs;            // number of streams
}

/// <summary>Reads what is really inside a video file with ffprobe (part of ffmpeg): length, picture size, audio languages and
/// subtitles. Optional: without ffprobe the app works from the file names. Port of probe.py.</summary>
public static class Probe
{
    static readonly Dictionary<string, string> Lang = new()
    {
        ["eng"] = "English", ["fas"] = "Persian", ["per"] = "Persian", ["fra"] = "French", ["fre"] = "French", ["deu"] = "German", ["ger"] = "German",
        ["spa"] = "Spanish", ["ita"] = "Italian", ["rus"] = "Russian", ["ara"] = "Arabic", ["tur"] = "Turkish", ["jpn"] = "Japanese", ["kor"] = "Korean",
        ["zho"] = "Chinese", ["chi"] = "Chinese", ["hin"] = "Hindi", ["por"] = "Portuguese", ["nld"] = "Dutch", ["dut"] = "Dutch", ["swe"] = "Swedish",
        ["pol"] = "Polish", ["heb"] = "Hebrew", ["ell"] = "Greek", ["gre"] = "Greek", ["dan"] = "Danish", ["nor"] = "Norwegian", ["fin"] = "Finnish",
        ["ces"] = "Czech", ["cze"] = "Czech", ["hun"] = "Hungarian", ["ron"] = "Romanian", ["rum"] = "Romanian", ["ukr"] = "Ukrainian",
        ["fa"] = "Persian", ["en"] = "English",
    };

    static readonly string[] ExtraPlaces =
    {
        @"%LOCALAPPDATA%\Microsoft\WinGet\Packages\*FFmpeg*\**\bin\ffprobe.exe",
        @"%LOCALAPPDATA%\Microsoft\WinGet\Links\ffprobe.exe",
        @"C:\ffmpeg\bin\ffprobe.exe", @"C:\Program Files\ffmpeg\bin\ffprobe.exe",
    };

    /// <summary>The ffprobe to use: the path set in Settings, one on PATH, or a usual install place. Null if not found.</summary>
    public static string? FindFfprobe(Settings? settings = null)
    {
        string custom = settings?.FfprobePath ?? "";
        if (custom.Length > 0 && File.Exists(custom)) return custom;
        string bundled = Path.Combine(AppContext.BaseDirectory, "tools", "ffprobe.exe");   // where the installer puts it
        if (File.Exists(bundled)) return bundled;
        var found = FolderUtil.Which("ffprobe");
        if (found != null) return found;
        if (!OperatingSystem.IsWindows()) return null;
        foreach (var pattern in ExtraPlaces)
        {
            var hits = FolderUtil.Glob(Environment.ExpandEnvironmentVariables(pattern));
            if (hits.Count > 0) return hits[0];
        }
        return null;
    }

    /// <summary>Language name for an ISO code; null for unknown / undetermined; other codes upper-cased.</summary>
    public static string? LangName(string? code)
    {
        code = (code ?? "").Trim().ToLowerInvariant();
        if (code is "" or "und" or "zxx" or "mis" or "mul") return null;
        return Lang.TryGetValue(code, out var n) ? n : code.ToUpperInvariant();
    }

    // Python truthiness of a JSON value, which decides which of two fields is used
    static bool Truthy(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString()!.Length > 0,
        JsonValueKind.Number => e.GetDouble() != 0,
        JsonValueKind.True => true,
        JsonValueKind.Array => e.GetArrayLength() > 0,
        JsonValueKind.Object => e.EnumerateObject().Any(),
        _ => false,
    };

    static JsonElement Get(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) ? v : default;

    static int? IntOf(JsonElement e) => e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var d) ? (int)d : null;

    static string? StrOf(JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    public static ProbeInfo Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Parse(doc.RootElement);
    }

    /// <summary>ffprobe -print_format json output -> duration (seconds), picture size, codec, bitrate, audio and subtitle languages.</summary>
    public static ProbeInfo Parse(JsonElement js)
    {
        var streams = Get(js, "streams");
        var list = streams.ValueKind == JsonValueKind.Array ? streams.EnumerateArray().ToList() : new List<JsonElement>();
        JsonElement video = default;
        foreach (var s in list)
            if (StrOf(Get(s, "codec_type")) == "video" && !Truthy(Get(Get(s, "disposition"), "attached_pic"))) { video = s; break; }
        var fmt = Get(js, "format");

        List<string> Names(string kind)
        {
            var o = new List<string>();
            foreach (var s in list)
                if (StrOf(Get(s, "codec_type")) == kind)
                {
                    var n = LangName(StrOf(Get(Get(s, "tags"), "language")));
                    if (n != null && !o.Contains(n)) o.Add(n);
                }
            return o;
        }

        int? duration = null;
        try
        {
            var d = Truthy(Get(fmt, "duration")) ? Get(fmt, "duration") : Truthy(Get(video, "duration")) ? Get(video, "duration") : default;
            double secs = d.ValueKind switch
            {
                JsonValueKind.String => double.Parse(d.GetString()!.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture),
                JsonValueKind.Number => d.GetDouble(),
                _ => 0,
            };
            if (!double.IsFinite(secs)) throw new FormatException();
            duration = (int)secs is var s0 and not 0 ? s0 : null;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException) { duration = null; }

        long? bitrate = null;
        var br = Get(fmt, "bit_rate");
        if (Truthy(br))
        {
            if (br.ValueKind == JsonValueKind.String) bitrate = long.TryParse(br.GetString()!.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var b) && b != 0 ? b : null;
            else if (br.ValueKind == JsonValueKind.Number) bitrate = (long)br.GetDouble() is var b2 and not 0 ? b2 : null;
        }
        return new ProbeInfo
        {
            Duration = duration, Width = IntOf(Get(video, "width")), Height = IntOf(Get(video, "height")),
            VCodec = StrOf(Get(video, "codec_name")), Bitrate = bitrate, Audio = Names("audio"), Subs = Names("subtitle"),
            NAudio = list.Count(s => StrOf(Get(s, "codec_type")) == "audio"),
            NSubs = list.Count(s => StrOf(Get(s, "codec_type")) == "subtitle"),
        };
    }

    /// <summary>Probe one file. Null when ffprobe could not read it (or took longer than the timeout).</summary>
    public static ProbeInfo? ProbeFile(string ffprobe, string path, int timeoutSeconds = 45)
    {
        try
        {
            var psi = new ProcessStartInfo(ffprobe)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var a in new[] { "-v", "error", "-print_format", "json", "-show_format", "-show_streams", path }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p == null) return null;
            var stdout = p.StandardOutput.ReadToEndAsync();
            p.StandardError.ReadToEndAsync();   // drained so a chatty ffprobe cannot block
            if (!p.WaitForExit(timeoutSeconds * 1000))
            {
                try { p.Kill(true); } catch (InvalidOperationException) { }
                return null;
            }
            p.WaitForExit();
            if (p.ExitCode != 0) return null;
            return Parse(stdout.GetAwaiter().GetResult());
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or JsonException or InvalidOperationException) { return null; }
    }

    /// <summary>The resolution a picture size really is: '2160p' | '1080p' | '720p' | '576p' | 'SD' (null without a width). Width decides,
    /// because wide films are cropped (1920x800 is 1080p).</summary>
    public static string? RealHeightClass(int? width, int? height)
    {
        int w = width ?? 0;
        if (w >= 3000) return "2160p";
        if (w >= 1800) return "1080p";
        if (w >= 1200) return "720p";
        if (w >= 900) return "576p";
        return w != 0 ? "SD" : null;
    }
}
