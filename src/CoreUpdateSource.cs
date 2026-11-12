using System.Text.RegularExpressions;

namespace Dashboard;

// Version output identifies a known distribution, not the authenticity of a binary.
internal sealed record CoreUpdateSource(string Mode, string ReleaseUrl)
{
    internal static CoreUpdateSource Unknown { get; } = new("unknown", "");

    internal static string RepositoryUrlFor(string releaseUrl) => releaseUrl switch
    {
        "https://github.com/reF1nd/sing-box-releases/releases" => "https://github.com/reF1nd/sing-box",
        "" => "",
        _ => releaseUrl[..^"/releases".Length]
    };

    internal static CoreUpdateSource Resolve(string output, CoreKind kind)
    {
        var version = VersionField(output, kind);
        if (version.Length == 0) return Unknown;
        if (kind == CoreKind.SingBox)
        {
            if (Regex.IsMatch(version, @"(?:^|-)reF1nd(?:[.-]|$)", RegexOptions.IgnoreCase))
                return new("manual", "https://github.com/reF1nd/sing-box-releases/releases");
            return Regex.IsMatch(version, @"^v?\d+\.\d+\.\d+(?:-(?:alpha|beta|rc)\.\d+)?$", RegexOptions.IgnoreCase)
                ? new("manual", "https://github.com/SagerNet/sing-box/releases") : Unknown;
        }

        if (version.StartsWith("TanakaLun-", StringComparison.OrdinalIgnoreCase))
            return new("manual", "https://github.com/TanakaLun/mihomo/releases");
        if (version.StartsWith("alpha-smart", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(output, @"\A\s*Mihomo Smart\s+", RegexOptions.IgnoreCase))
            return new("manual", "https://github.com/vernesong/mihomo/releases");
        return Regex.IsMatch(version, @"^(?:v\d+\.\d+\.\d+|(?:alpha|beta|meta)-[0-9a-f]{6,40})$", RegexOptions.IgnoreCase)
            ? new("automatic", "https://github.com/MetaCubeX/mihomo/releases") : Unknown;
    }

    // Read only the core's version field (or a standalone version), never Go/build metadata.
    internal static string VersionField(string output, CoreKind kind)
    {
        var trimmed = output.Trim();
        var prefix = kind == CoreKind.SingBox ? @"sing-box(?: version)?" : @"(?:Mihomo(?: (?:Meta|Smart))?|Clash(?:\.Meta)?)";
        var match = Regex.Match(trimmed, @"\A" + prefix + @"[ \t]+(?<version>\S+)(?:\s|$)", RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups["version"].Value;
        return Regex.IsMatch(trimmed, @"\A\S+\z") ? trimmed : "";
    }
}
