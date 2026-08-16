namespace Dashboard;

public sealed record CoreUpgradeResult(
    string Version,
    string AssetName,
    string BackupPath,
    bool IsAlreadyLatest,
    string Warning = "");
