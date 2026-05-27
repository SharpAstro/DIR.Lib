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
    public async Task AsyncHandler_SubmittedToTracker()
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
        await tracker.DrainAsync().WaitAsync(TestContext.Current.CancellationToken);
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
    public async Task PostFromAnotherThread_DeliveredOnProcessThread()
    {
        var bus = new SignalBus();
        var deliveredOnThread = -1;

        bus.Subscribe<RequestRedrawSignal>(_ => deliveredOnThread = Environment.CurrentManagedThreadId);

        // Post from a different thread.
        var postTask = Task.Run(() => bus.Post(new RequestRedrawSignal()), TestContext.Current.CancellationToken);
        await postTask.WaitAsync(TestContext.Current.CancellationToken);

        // Capture the thread we're *about to call ProcessPending on*, NOT
        // at the start of the test — xUnit v3 runs async tests on the
        // thread pool with no SynchronizationContext, so the await above
        // can resume on a different thread than the test entry. Capturing
        // here, immediately before the call, is what the assertion below
        // actually means: "delivery happens on the thread that calls
        // ProcessPending, not on the thread that posted".
        var processingThread = Environment.CurrentManagedThreadId;
        bus.ProcessPending();

        deliveredOnThread.ShouldBe(processingThread);
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

    // Lock the "Subscribe is thread-safe" contract in so a future refactor
    // can't quietly regress it. Spawns N threads that each subscribe to the
    // same signal type, then drives one Post + ProcessPending and asserts
    // every handler fired exactly once. With the original
    // Dictionary<Type, List<...>> backing this would either lose handlers
    // (lost write under contention) or throw on the foreach in
    // ProcessPending (collection mutated during iteration); with the
    // ConcurrentDictionary<Type, ImmutableArray<...>> backing every Add
    // composes via AddOrUpdate's retry loop.
    [Fact]
    public async Task ConcurrentSubscribe_AllHandlersDelivered()
    {
        var bus = new SignalBus();
        const int handlerCount = 64;
        var fired = new int[handlerCount];

        var subscribeTasks = new Task[handlerCount];
        for (var i = 0; i < handlerCount; i++)
        {
            var idx = i;
            subscribeTasks[i] = Task.Run(() => bus.Subscribe<RequestRedrawSignal>(_ =>
            {
                Interlocked.Increment(ref fired[idx]);
            }), TestContext.Current.CancellationToken);
        }
        await Task.WhenAll(subscribeTasks).WaitAsync(TestContext.Current.CancellationToken);

        bus.Post(new RequestRedrawSignal());
        bus.ProcessPending();

        for (var i = 0; i < handlerCount; i++)
        {
            fired[i].ShouldBe(1, customMessage: $"handler {i} did not fire exactly once");
        }
    }

    // Subscribing mid-ProcessPending must not crash the iteration (the old
    // Dictionary+List would throw "Collection was modified" if a handler
    // happened to add a peer). With ImmutableArray snapshotting the late
    // subscriber lands in a new array that the *next* ProcessPending sees,
    // not the in-flight one.
    [Fact]
    public void SubscribeDuringDelivery_DoesNotDisturbInFlightIteration()
    {
        var bus = new SignalBus();
        var lateFiredOnFirst = false;
        var lateFiredOnSecond = false;

        bus.Subscribe<RequestRedrawSignal>(_ =>
        {
            bus.Subscribe<RequestRedrawSignal>(_ =>
            {
                if (!lateFiredOnFirst) lateFiredOnFirst = true;
                else lateFiredOnSecond = true;
            });
        });

        bus.Post(new RequestRedrawSignal());
        bus.ProcessPending();
        lateFiredOnFirst.ShouldBeFalse("subscriber added mid-delivery must not fire on the in-flight signal");

        bus.Post(new RequestRedrawSignal());
        bus.ProcessPending();
        lateFiredOnFirst.ShouldBeTrue("subscriber added during prior frame must fire on the next signal");
        lateFiredOnSecond.ShouldBeFalse("a second-frame self-add must not fire on its own frame either");
    }
}
