using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>ApplyWithRestore: draft → update → activate, con restore y rethrow de la excepción ORIGINAL.</summary>
public class CloudFlowUpdaterApplyWithRestoreTests
{
    [Fact]
    public void ActiveFlow_HappyPath_RunsDraftUpdateActivateInOrder()
    {
        var calls = new List<string>();

        CloudFlowUpdater.ApplyWithRestore(true,
            () => calls.Add("draft"), () => calls.Add("update"), () => calls.Add("activate"), _ => { });

        calls.Should().Equal("draft", "update", "activate");
    }

    [Fact]
    public void InactiveFlow_OnlyUpdates_StateNotTouched()
    {
        var calls = new List<string>();

        CloudFlowUpdater.ApplyWithRestore(false,
            () => calls.Add("draft"), () => calls.Add("update"), () => calls.Add("activate"), _ => { });

        calls.Should().Equal("update");
    }

    [Fact]
    public void ActiveFlow_UpdateThrows_ReactivatesAndRethrowsOriginal()
    {
        var calls = new List<string>();
        var original = new InvalidOperationException("update boom");
        var logs = new List<string>();

        var act = () => CloudFlowUpdater.ApplyWithRestore(true,
            () => calls.Add("draft"), () => throw original, () => calls.Add("activate"), logs.Add);

        act.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(original);
        calls.Should().Equal("draft", "activate");
        logs.Should().ContainSingle().Which.Should().Contain("restaurado");
    }

    [Fact]
    public void ActiveFlow_UpdateThrowsAndReactivationThrows_OriginalStillRethrown()
    {
        var original = new InvalidOperationException("update boom");
        var logs = new List<string>();

        var act = () => CloudFlowUpdater.ApplyWithRestore(true,
            () => { }, () => throw original, () => throw new TimeoutException("activate boom"), logs.Add);

        act.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(original);
        logs.Should().ContainSingle().Which.Should().Contain("activate boom");
    }

    [Fact]
    public void ActiveFlow_DraftThrows_DoesNotReactivateAndRethrows()
    {
        var calls = new List<string>();
        var original = new InvalidOperationException("draft boom");

        var act = () => CloudFlowUpdater.ApplyWithRestore(true,
            () => throw original, () => calls.Add("update"), () => calls.Add("activate"), _ => { });

        act.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(original);
        calls.Should().BeEmpty();
    }

    [Fact]
    public void InactiveFlow_UpdateThrows_NoStateChangeAndRethrows()
    {
        var calls = new List<string>();
        var original = new InvalidOperationException("update boom");

        var act = () => CloudFlowUpdater.ApplyWithRestore(false,
            () => calls.Add("draft"), () => throw original, () => calls.Add("activate"), _ => { });

        act.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(original);
        calls.Should().BeEmpty();
    }
}
