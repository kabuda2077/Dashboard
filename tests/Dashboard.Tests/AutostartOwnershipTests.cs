using System.Xml.Linq;

namespace Dashboard.Tests;

public sealed class AutostartOwnershipTests
{
    private const string Exe = @"C:\DashboardTest\Dashboard.exe";
    private const string Directory = @"C:\DashboardTest";
    private const string Sid = "S-1-5-21-1000";
    private static string Xml() => AutostartManager.BuildTaskXml(Exe, Directory, Sid);

    [Theory]
    [InlineData("sid", true)] [InlineData("sid", false)]
    [InlineData("exe", true)] [InlineData("exe", false)]
    [InlineData("directory", true)] [InlineData("directory", false)]
    [InlineData("malformed", true)] [InlineData("malformed", false)]
    [InlineData("extraAction", true)] [InlineData("extraAction", false)]
    [InlineData("queryFailure", true)] [InlineData("queryFailure", false)]
    public void ForeignOrUnverifiableTasksNeverReachAMutatingCommand(string reason, bool enabled)
    {
        var xml = Xml();
        if (reason == "sid") xml = AutostartManager.BuildTaskXml(Exe, Directory, "S-1-5-21-2000");
        if (reason == "exe") xml = AutostartManager.BuildTaskXml(@"D:\Other\Dashboard.exe", Directory, Sid);
        if (reason == "directory") xml = AutostartManager.BuildTaskXml(Exe, @"D:\Other", Sid);
        if (reason == "malformed") xml = "broken";
        if (reason == "extraAction")
        {
            var document = XDocument.Parse(xml); var ns = document.Root!.Name.Namespace;
            var actions = document.Root.Element(ns + "Actions")!;
            actions.Add(new XElement(actions.Elements().Single())); xml = document.ToString();
        }
        var mutations = 0;
        var result = AutostartManager.ChangeOwnedTask(enabled,
            () => reason == "queryFailure" ? new(false, Error: "access denied") : new(true, xml),
            _ => { mutations++; return AutostartOperationResult.Ok("must not run"); }, Exe, Directory, Sid);
        Assert.False(result.Success);
        Assert.Equal(0, mutations);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void OwnedTaskChangesAreVerifiedAfterMutation(bool enabled)
    {
        var queries = 0; var mutations = 0;
        var result = AutostartManager.ChangeOwnedTask(enabled,
            () => ++queries == 1 ? new(true, Xml()) : new(enabled, enabled ? Xml() : null),
            replace => { Assert.True(replace); mutations++; return AutostartOperationResult.Ok("changed"); }, Exe, Directory, Sid);
        Assert.True(result.Success);
        Assert.Equal(2, queries); Assert.Equal(1, mutations);
    }

    [Fact]
    public void AbsentTaskCreationDoesNotAuthorizeForceAndRemovalIsANoop()
    {
        var queries = 0;
        var create = AutostartManager.ChangeOwnedTask(true,
            () => ++queries == 1 ? new(false) : new(true, Xml()),
            replace => { Assert.False(replace); return AutostartOperationResult.Ok("created"); }, Exe, Directory, Sid);
        Assert.True(create.Success);
        var remove = AutostartManager.ChangeOwnedTask(false, () => new(false), _ => throw new Exception("must not delete"), Exe, Directory, Sid);
        Assert.True(remove.Success);
    }

    [Fact]
    public void FailedPostMutationQueryDoesNotReportVerifiedSuccess()
    {
        var queries = 0;
        var result = AutostartManager.ChangeOwnedTask(true,
            () => ++queries == 1 ? new(false) : new(false, Error: "scheduler unavailable"),
            _ => AutostartOperationResult.Ok("created"), Exe, Directory, Sid);
        Assert.False(result.Success);
        Assert.Contains("verification failed", result.Message);
    }

    [Fact]
    [Trait("Category", "WebViewIntegration")]
    public void ReadOnlySchedulerQueryDistinguishesMissingTaskFromQueryFailure()
    {
        // Read a unique nonexistent path only; never create/delete or inspect a user's task.
        var result = AutostartManager.ReadTaskDefinition(@"\Dashboard.ReadOnlyTest." + Guid.NewGuid().ToString("N") + @"\Missing");
        Assert.Null(result.Error);
        Assert.False(result.Exists);
    }

    [Fact]
    public void DifferentElevationAccountIsRejectedBeforeTaskAccess() =>
        Assert.Equal(2, AutostartManager.RunManagementCommand("install", "not-the-current-user"));
}
