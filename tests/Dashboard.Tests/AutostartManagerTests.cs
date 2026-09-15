using System.Xml.Linq;

namespace Dashboard.Tests;

public sealed class AutostartManagerTests
{
    [Fact]
    public void BuildTaskXmlIncludesSilentElevatedLogonContract()
    {
        const string executablePath = @"C:\Program Files\Dashboard & Tools\Dashboard.exe";
        const string workingDirectory = @"C:\Program Files\Dashboard & Tools";
        const string userSid = "S-1-5-21-1000";

        var xml = AutostartManager.BuildTaskXml(executablePath, workingDirectory, userSid);
        var document = XDocument.Parse(xml);
        var root = Assert.IsType<XElement>(document.Root);
        XNamespace ns = root.Name.Namespace;

        var trigger = root.Element(ns + "Triggers")?.Element(ns + "LogonTrigger");
        var principal = root.Element(ns + "Principals")?.Element(ns + "Principal");
        var settings = root.Element(ns + "Settings");
        var action = root.Element(ns + "Actions")?.Element(ns + "Exec");

        Assert.Equal("PT5S", trigger?.Element(ns + "Delay")?.Value);
        Assert.Equal(userSid, trigger?.Element(ns + "UserId")?.Value);
        Assert.Equal("HighestAvailable", principal?.Element(ns + "RunLevel")?.Value);
        Assert.Equal("InteractiveToken", principal?.Element(ns + "LogonType")?.Value);
        Assert.Equal("IgnoreNew", settings?.Element(ns + "MultipleInstancesPolicy")?.Value);
        Assert.Equal("true", settings?.Element(ns + "StartWhenAvailable")?.Value);
        Assert.Equal("false", settings?.Element(ns + "DisallowStartIfOnBatteries")?.Value);
        Assert.Equal("false", settings?.Element(ns + "StopIfGoingOnBatteries")?.Value);
        Assert.Equal("PT0S", settings?.Element(ns + "ExecutionTimeLimit")?.Value);
        Assert.Equal(executablePath, action?.Element(ns + "Command")?.Value);
        Assert.Equal(AutostartManager.ScheduledArguments, action?.Element(ns + "Arguments")?.Value);
        Assert.Equal(workingDirectory, action?.Element(ns + "WorkingDirectory")?.Value);
    }

    [Fact]
    public void VerifyTaskXmlRejectsMovedExecutable()
    {
        const string originalPath = @"C:\Dashboard\Dashboard.exe";
        const string movedPath = @"D:\Apps\Dashboard\Dashboard.exe";
        const string workingDirectory = @"C:\Dashboard";
        const string userSid = "S-1-5-21-1000";
        var xml = AutostartManager.BuildTaskXml(originalPath, workingDirectory, userSid);

        var status = AutostartManager.VerifyTaskXml(
            xml,
            movedPath,
            @"D:\Apps\Dashboard",
            userSid);

        Assert.True(status.Exists);
        Assert.False(status.IsValid);
        Assert.Contains("Executable path", status.Message);
    }

    [Fact]
    public void VerifyTaskXmlAcceptsExpectedTask()
    {
        const string executablePath = @"C:\Dashboard\Dashboard.exe";
        const string workingDirectory = @"C:\Dashboard";
        const string userSid = "S-1-5-21-1000";
        var xml = AutostartManager.BuildTaskXml(executablePath, workingDirectory, userSid);

        var status = AutostartManager.VerifyTaskXml(
            xml,
            executablePath,
            workingDirectory,
            userSid);

        Assert.True(status.Exists);
        Assert.True(status.IsValid, status.Message);
    }

    [Fact]
    public void VerifyTaskXmlAcceptsSchedulerNormalizedDefaultsAndAccountName()
    {
        const string executablePath = @"C:\Dashboard\Dashboard.exe";
        const string workingDirectory = @"C:\Dashboard";
        const string userSid = "S-1-5-21-1000";
        const string userAccount = @"DESKTOP\user";
        var document = XDocument.Parse(AutostartManager.BuildTaskXml(executablePath, workingDirectory, userSid));
        var root = Assert.IsType<XElement>(document.Root);
        XNamespace ns = root.Name.Namespace;
        var trigger = Assert.IsType<XElement>(root.Element(ns + "Triggers")?.Element(ns + "LogonTrigger"));

        trigger.Element(ns + "UserId")!.Value = userAccount;
        trigger.Element(ns + "Enabled")?.Remove();
        root.Element(ns + "Settings")?.Element(ns + "Enabled")?.Remove();

        var status = AutostartManager.VerifyTaskXml(
            document.ToString(SaveOptions.DisableFormatting),
            executablePath,
            workingDirectory,
            userSid,
            userAccount);

        Assert.True(status.Exists);
        Assert.True(status.IsValid, status.Message);
    }

    [Fact]
    public void VerifyTaskXmlRejectsExplicitlyDisabledTask()
    {
        const string executablePath = @"C:\Dashboard\Dashboard.exe";
        const string workingDirectory = @"C:\Dashboard";
        const string userSid = "S-1-5-21-1000";
        var document = XDocument.Parse(AutostartManager.BuildTaskXml(executablePath, workingDirectory, userSid));
        var root = Assert.IsType<XElement>(document.Root);
        XNamespace ns = root.Name.Namespace;

        root.Element(ns + "Settings")?.Element(ns + "Enabled")?.SetValue("false");

        var status = AutostartManager.VerifyTaskXml(
            document.ToString(SaveOptions.DisableFormatting),
            executablePath,
            workingDirectory,
            userSid);

        Assert.False(status.IsValid);
        Assert.Contains("disabled", status.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FinalizeOperationKeepsLegacyEntryUntilTaskIsVerified()
    {
        var legacyRemoved = false;

        var result = AutostartManager.FinalizeOperation(
            enabled: true,
            AutostartOperationResult.Ok("installed"),
            new AutostartTaskStatus(true, false, "wrong executable"),
            taskExistsAfterRemoval: false,
            () => legacyRemoved = true);

        Assert.False(result.Success);
        Assert.False(legacyRemoved);
    }

    [Fact]
    public void FinalizeOperationRemovesLegacyEntryAfterVerification()
    {
        var legacyRemoved = false;

        var result = AutostartManager.FinalizeOperation(
            enabled: true,
            AutostartOperationResult.Ok("installed"),
            new AutostartTaskStatus(true, true, "valid"),
            taskExistsAfterRemoval: false,
            () => legacyRemoved = true);

        Assert.True(result.Success);
        Assert.True(legacyRemoved);
    }

    [Fact]
    public void FinalizeRemovalKeepsLegacyEntryWhenTaskStillExists()
    {
        var legacyRemoved = false;

        var result = AutostartManager.FinalizeOperation(
            enabled: false,
            AutostartOperationResult.Ok("removed"),
            verification: null,
            taskExistsAfterRemoval: true,
            () => legacyRemoved = true);

        Assert.False(result.Success);
        Assert.False(legacyRemoved);
    }

    [Fact]
    public void InvalidManagementOperationReturnsFailureExitCode()
    {
        Assert.Equal(2, AutostartManager.RunManagementCommand("invalid"));
    }
}
