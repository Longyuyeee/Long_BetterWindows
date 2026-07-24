namespace LongBetterWindows.Host.Interaction
{
    internal sealed class SuperPanelGroupEditorSession
    {
        private readonly SuperPanelGroupCoordinator _groups;
        private string? _editingGroupId;

        internal SuperPanelGroupEditorSession(SuperPanelGroupCoordinator groups)
        {
            _groups = groups;
        }

        public SuperPanelGroupEditorState State { get; private set; } =
            SuperPanelGroupEditorState.Closed;

        public void Open(string? groupId, string title)
        {
            _editingGroupId = groupId;
            State = new SuperPanelGroupEditorState(
                true,
                groupId is null
                    ? _groups.Text(
                        "superPanel.editor.createTitle",
                        "新建操作分组")
                    : _groups.Text(
                        "superPanel.editor.renameTitle",
                        "重命名操作分组"),
                title);
        }

        public async Task<SuperPanelGroupMutationResult> SaveAsync(string title)
        {
            var outcome = await _groups.SaveGroupAsync(_editingGroupId, title);
            if (outcome.Success)
                Close();
            return outcome;
        }

        public void Cancel() => Close();

        private void Close()
        {
            _editingGroupId = null;
            State = SuperPanelGroupEditorState.Closed;
        }
    }

    internal sealed record SuperPanelGroupEditorState(
        bool IsOpen,
        string Heading,
        string Title)
    {
        public static SuperPanelGroupEditorState Closed { get; } =
            new(false, string.Empty, string.Empty);
    }
}
