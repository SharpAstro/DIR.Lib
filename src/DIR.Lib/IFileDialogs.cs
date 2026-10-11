namespace DIR.Lib;

/// <summary>
/// The platform's file dialogs, asked for by a widget that does not know the platform: open one or more
/// files, save one, pick a folder.
/// </summary>
/// <remarks>
/// <para>
/// <b>An interface, so a test substitutes it.</b> A widget that wants a file is still a widget a test drives
/// with no window and no desktop, and the dialog is exactly the part such a test cannot run. So the widget
/// asks through this, the host hands it the implementation over its own windowing (SdlVulkan.Renderer's
/// <c>SdlFileDialogs</c>, say), and a test hands it a stub that answers with a path.
/// </para>
/// <para>
/// <b>The contract every implementation keeps:</b>
/// <list type="bullet">
/// <item>The call returns at once; the dialog never blocks the thread that asked, which is the render
/// thread. A dialog that blocked it would freeze the window behind it, and a modal one froze the app.</item>
/// <item>The task completes on the thread that drives the window's events, so a continuation that touches
/// widget state needs no lock and no hop.</item>
/// <item>A user's Cancel completes the task with an empty <see cref="FileDialogResult.Paths"/>. Cancelling
/// the token completes it as CANCELLED (<see cref="OperationCanceledException"/>) instead, and whatever the
/// dialog answers afterwards is dropped: "the user said no" and "the caller gave up" are different facts,
/// and only the token can tell them apart. A platform may have no way to close a dialog it has shown, so
/// the dialog can outlive the cancellation.</item>
/// <item>A save path always carries an extension when one could be inferred: see
/// <see cref="FileDialogFilter.WithDefaultExtension"/>.</item>
/// </list>
/// </para>
/// </remarks>
public interface IFileDialogs
{
    /// <summary>Lets the user choose one file to open, or several when <see cref="FileDialogRequest.AllowMany"/>
    /// is set.</summary>
    Task<FileDialogResult> OpenAsync(FileDialogRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lets the user choose where to save a file, new or existing.
    /// <see cref="FileDialogRequest.SuggestedFileName"/> seeds the name.</summary>
    Task<FileDialogResult> SaveAsync(FileDialogRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lets the user choose a folder. <see cref="FileDialogRequest.Filters"/> is ignored.</summary>
    Task<FileDialogResult> OpenFolderAsync(FileDialogRequest request, CancellationToken cancellationToken = default);
}
