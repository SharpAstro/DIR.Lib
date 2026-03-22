using Shouldly;
using System.Threading.Tasks;

namespace DIR.Lib.Tests;

public sealed class SignalBusTests
{
    [Fact]
    public void ProcessPending_DeliversSyncSignalToSubscriber()
    {
        var bus = new SignalBus();
        TextInputState? received = null;
        var state = new TextInputState();

        bus.Subscribe<ActivateTextInputSignal>(sig => received = sig.Input);
        bus.Post(new ActivateTextInputSignal(state));
        bus.ProcessPending().ShouldBeTrue();

        received.ShouldBe(state);
    }

    [Fact]
    public void ProcessPending_ReturnsFalseWhenEmpty()
    {
        new SignalBus().ProcessPending().ShouldBeFalse();
    }

    [Fact]
    public void UnsubscribedSignal_IsSilentlyDequeued()
    {
        var bus = new SignalBus();
        bus.Post(new RequestExitSignal());
        // Dequeued (returns true) but no handler fires — no crash
        bus.ProcessPending().ShouldBeTrue();
    }

    [Fact]
    public void MultipleSubscribers_AllReceiveSignal()
    {
        var bus = new SignalBus();
        var count = 0;

        bus.Subscribe<RequestRedrawSignal>(_ => count++);
        bus.Subscribe<RequestRedrawSignal>(_ => count++);

        bus.Post(new RequestRedrawSignal());
        bus.ProcessPending();

        count.ShouldBe(2);
    }

    [Fact]
    public void MultipleSignals_DeliveredInOrder()
    {
        var bus = new SignalBus();
        var order = new List<string>();

        bus.Subscribe<ActivateTextInputSignal>(_ => order.Add("activate"));
        bus.Subscribe<DeactivateTextInputSignal>(_ => order.Add("deactivate"));

        bus.Post(new ActivateTextInputSignal(new TextInputState()));
        bus.Post(new DeactivateTextInputSignal());
        bus.ProcessPending();

        order.ShouldBe(new[] { "activate", "deactivate" });
    }

    [Fact]
    public void DifferentSignalTypes_RoutedToCorrectHandlers()
    {
        var bus = new SignalBus();
        var exitReceived = false;
        var redrawReceived = false;

        bus.Subscribe<RequestExitSignal>(_ => exitReceived = true);
        bus.Subscribe<RequestRedrawSignal>(_ => redrawReceived = true);

        bus.Post(new RequestExitSignal());
        bus.ProcessPending();

        exitReceived.ShouldBeTrue();
        redrawReceived.ShouldBeFalse();
    }

    [Fact]
    public void AsyncHandler_SubmittedToTracker()
    {
        var bus = new SignalBus();
        var tracker = new BackgroundTaskTracker();
        var ran = false;

        bus.Subscribe<RequestExitSignal>(async _ =>
        {
            await Task.Yield();
            ran = true;
        });

        bus.Post(new RequestExitSignal());
        bus.ProcessPending(tracker);

        // Task was submitted to tracker — drain to complete
        tracker.DrainAsync().GetAwaiter().GetResult();
        ran.ShouldBeTrue();
    }

    [Fact]
    public void AsyncHandler_WithoutTracker_Throws()
    {
        var bus = new SignalBus();
        bus.Subscribe<RequestExitSignal>(async _ => await Task.Yield());
        bus.Post(new RequestExitSignal());

        Should.Throw<InvalidOperationException>(() => bus.ProcessPending());
    }

    [Fact]
    public void PostFromAnotherThread_DeliveredOnProcessThread()
    {
        var bus = new SignalBus();
        var deliveredOnThread = -1;
        var mainThread = Environment.CurrentManagedThreadId;

        bus.Subscribe<RequestRedrawSignal>(_ => deliveredOnThread = Environment.CurrentManagedThreadId);

        // Post from a different thread
        var postTask = Task.Run(() => bus.Post(new RequestRedrawSignal()));
        postTask.Wait();

        // Process on main thread
        bus.ProcessPending();

        deliveredOnThread.ShouldBe(mainThread);
    }

    [Fact]
    public void ProcessPending_ClearsQueue()
    {
        var bus = new SignalBus();
        var count = 0;

        bus.Subscribe<RequestRedrawSignal>(_ => count++);
        bus.Post(new RequestRedrawSignal());
        bus.ProcessPending();
        count.ShouldBe(1);

        // Second call — queue is empty
        bus.ProcessPending().ShouldBeFalse();
        count.ShouldBe(1);
    }

    [Fact]
    public void SignalPayload_PreservedThroughDelivery()
    {
        var bus = new SignalBus();
        var state = new TextInputState { Placeholder = "test-marker" };
        TextInputState? received = null;

        bus.Subscribe<ActivateTextInputSignal>(sig => received = sig.Input);
        bus.Post(new ActivateTextInputSignal(state));
        bus.ProcessPending();

        received.ShouldNotBeNull();
        received!.Placeholder.ShouldBe("test-marker");
    }
}
