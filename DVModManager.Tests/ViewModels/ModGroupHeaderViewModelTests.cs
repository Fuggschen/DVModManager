using DVModManager.ViewModels;
using Xunit;

namespace DVModManager.Tests.ViewModels;

public class ModGroupHeaderViewModelTests
{
    [Fact]
    public async Task ToggleCollapseCommand_TogglesCollapseAndInvokesCallback()
    {
        bool callbackCalled = false;
        string? toggledGroup = null;
        bool? toggledState = null;
        string? toggledPanel = null;

        using var vm = new ModGroupHeaderViewModel(
            groupId: "group1",
            name: "Gameplay Mods",
            modCount: 5,
            isCollapsed: false,
            panel: "available",
            onRename: (_, _) => Task.CompletedTask,
            onDelete: _ => Task.CompletedTask,
            onToggle: (gid, isCol, panel) =>
            {
                callbackCalled = true;
                toggledGroup = gid;
                toggledState = isCol;
                toggledPanel = panel;
                return Task.CompletedTask;
            });

        Assert.False(vm.IsCollapsed);
        Assert.Equal("group1", vm.GroupId);
        Assert.Equal("Gameplay Mods", vm.Name);
        Assert.Equal(5, vm.ModCount);
        Assert.Equal("available", vm.Panel);
        Assert.Equal("5 mod(s)", vm.ModCountLabel);

        await vm.ToggleCollapseCommand.ExecuteAsync(null);

        Assert.True(vm.IsCollapsed);
        Assert.True(callbackCalled);
        Assert.Equal("group1", toggledGroup);
        Assert.True(toggledState);
        Assert.Equal("available", toggledPanel);
    }

    [Fact]
    public async Task RenameFlow_WorksAsExpected()
    {
        string? renamedGroup = null;
        string? newNameResult = null;

        using var vm = new ModGroupHeaderViewModel(
            groupId: "group1",
            name: "Original Name",
            modCount: 2,
            isCollapsed: false,
            panel: "active",
            onRename: (gid, newName) =>
            {
                renamedGroup = gid;
                newNameResult = newName;
                return Task.CompletedTask;
            },
            onDelete: _ => Task.CompletedTask,
            onToggle: (_, _, _) => Task.CompletedTask);

        // Begin rename
        vm.BeginRenameCommand.Execute(null);
        Assert.True(vm.IsRenaming);
        Assert.Equal("Original Name", vm.EditName);

        // Cancel rename
        vm.EditName = "Temp Name";
        vm.CancelRenameCommand.Execute(null);
        Assert.False(vm.IsRenaming);
        Assert.Equal("Original Name", vm.EditName);

        // Begin and Confirm rename
        vm.BeginRenameCommand.Execute(null);
        vm.EditName = "Updated Name";
        await vm.ConfirmRenameCommand.ExecuteAsync(null);

        Assert.False(vm.IsRenaming);
        Assert.Equal("group1", renamedGroup);
        Assert.Equal("Updated Name", newNameResult);
    }

    [Fact]
    public async Task DeleteGroupCommand_InvokesCallback()
    {
        string? deletedGroup = null;

        using var vm = new ModGroupHeaderViewModel(
            groupId: "group_to_delete",
            name: "Test Group",
            modCount: 0,
            isCollapsed: false,
            panel: "available",
            onRename: (_, _) => Task.CompletedTask,
            onDelete: gid =>
            {
                deletedGroup = gid;
                return Task.CompletedTask;
            },
            onToggle: (_, _, _) => Task.CompletedTask);

        await vm.DeleteGroupCommand.ExecuteAsync(null);

        Assert.Equal("group_to_delete", deletedGroup);
    }
}
