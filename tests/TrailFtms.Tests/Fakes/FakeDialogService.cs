using Blazor.WinOld;
using Blazor.WinOld.Components;

namespace TrailFtms.Tests.Fakes;

/// <summary>Records message boxes and answers them with <see cref="Answer"/>.</summary>
public sealed class FakeDialogService : IDialogService
{
    public List<MessageBoxOptions> MessageBoxes { get; } = [];

    public bool? Answer { get; set; } = true;

    public Task<bool?> ShowMessageBox(MessageBoxOptions options)
    {
        MessageBoxes.Add(options);
        return Task.FromResult(Answer);
    }

    public Task<object?> ShowInputBoxCore(InputBoxOptions options) => throw new NotSupportedException();

    public Task<bool?> ShowDialog(DialogOptions options) => throw new NotSupportedException();
}
